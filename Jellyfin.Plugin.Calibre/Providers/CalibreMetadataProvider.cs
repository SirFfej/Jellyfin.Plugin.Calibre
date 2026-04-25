using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Jellyfin.Data.Enums;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Calibre;

public class CalibreMetadataProvider : IRemoteMetadataProvider<Book, BookInfo>
{
    private readonly ILogger<CalibreMetadataProvider> _logger;

    public CalibreMetadataProvider(ILogger<CalibreMetadataProvider> logger)
    {
        _logger = logger;
    }

    public string Name => "Calibre";

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        }

        if (config.UseHostAccessMode)
        {
            return SearchByHttpAsync(config, searchInfo.Name, cancellationToken);
        }

        if (string.IsNullOrEmpty(config.CalibreLibraryPath))
        {
            return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        }

        try
        {
            using var db = new CalibreDatabase(config.CalibreLibraryPath);
            var books = db.SearchBooksAsync(searchInfo.Name, 20, cancellationToken).GetAwaiter().GetResult();
            var results = books.Select(b =>
            {
                var r = new RemoteSearchResult
                {
                    Name = b.Title,
                    ProductionYear = b.PubDate?.Year,
                    SearchProviderName = Name,
                    PremiereDate = b.PubDate
                };
                r.ProviderIds["Calibre"] = b.Id.ToString();
                return r;
            });
            return Task.FromResult<IEnumerable<RemoteSearchResult>>(results.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Calibre for {Query}", searchInfo.Name);
            return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        }
    }

    public Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            return Task.FromResult(new MetadataResult<Book>());
        }

        var providerId = GetProviderId(info);

        if (string.IsNullOrEmpty(providerId))
        {
            var searchResults = GetSearchResults(info, cancellationToken).GetAwaiter().GetResult();
            var first = searchResults.FirstOrDefault();
            if (first != null)
            {
                providerId = first.ProviderIds.GetValueOrDefault(Name);
            }
        }

        if (string.IsNullOrEmpty(providerId))
        {
            return Task.FromResult(new MetadataResult<Book>());
        }

        if (!int.TryParse(providerId, out var bookId))
        {
            return Task.FromResult(new MetadataResult<Book>());
        }

        if (config.UseHostAccessMode)
        {
            return GetMetadataByHttpAsync(config, bookId, cancellationToken);
        }

        if (string.IsNullOrEmpty(config.CalibreLibraryPath))
        {
            return Task.FromResult(new MetadataResult<Book>());
        }

        try
        {
            using var db = new CalibreDatabase(config.CalibreLibraryPath);
            var book = db.GetBookByIdAsync(bookId, cancellationToken).GetAwaiter().GetResult();
            if (book == null)
            {
                return Task.FromResult(new MetadataResult<Book>());
            }

            var resultItem = new Book
            {
                Name = book.Title,
                Overview = book.Comments,
                ProductionYear = book.PubDate?.Year,
                PremiereDate = book.PubDate
            };

            foreach (var genre in book.Tags)
            {
                resultItem.AddGenre(genre);
            }

            var people = new List<PersonInfo>();
            foreach (var authorName in book.Authors)
            {
                people.Add(new PersonInfo
                {
                    Name = authorName,
                    Type = PersonKind.Author
                });
            }

            resultItem.ProviderIds[Name] = providerId;

            var coverPath = db.GetCoverPath(config.CalibreLibraryPath, book);
            var hasCover = !string.IsNullOrEmpty(coverPath);

            var result = new MetadataResult<Book>
            {
                Item = resultItem,
                HasMetadata = true,
                People = people
            };

            if (hasCover)
            {
                result.RemoteImages.Add((coverPath, ImageType.Primary));
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching metadata for book {BookId}", bookId);
            return Task.FromResult(new MetadataResult<Book>());
        }
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || string.IsNullOrEmpty(url))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        try
        {
            if (!File.Exists(url))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var bytes = File.ReadAllBytes(url);
            var contentType = GetContentType(url);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching cover image: {Url}", url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private Task<IEnumerable<RemoteSearchResult>> SearchByHttpAsync(PluginConfiguration config, string query, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new CalibreContentServerClient(
                config.CalibreServerUrl,
                config.Username,
                config.Password);

            var library = client.GetLibraryAsync(cancellationToken).GetAwaiter().GetResult();
            if (library?.Books == null)
            {
                return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
            }

            var books = library.Books.Values
                .Where(b => b.Title?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(20)
                .Select(b =>
                {
                    var r = new RemoteSearchResult
                    {
                        Name = b.Title ?? "",
                        SearchProviderName = Name
                    };
                    r.ProviderIds["Calibre"] = b.Id.ToString();
                    return r;
                })
                .ToList();

            return Task.FromResult<IEnumerable<RemoteSearchResult>>(books);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Calibre via HTTP for {Query}", query);
            return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        }
    }

    private Task<MetadataResult<Book>> GetMetadataByHttpAsync(PluginConfiguration config, int bookId, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new CalibreContentServerClient(
                config.CalibreServerUrl,
                config.Username,
                config.Password);

            var book = client.GetBookByIdAsync(bookId, cancellationToken).GetAwaiter().GetResult();
            if (book == null)
            {
                return Task.FromResult(new MetadataResult<Book>());
            }

            var resultItem = new Book
            {
                Name = book.Title,
                Overview = book.Comments,
                ProductionYear = book.PubDate?.Year,
                PremiereDate = book.PubDate
            };

            foreach (var genre in book.Tags)
            {
                resultItem.AddGenre(genre);
            }

            var people = new List<PersonInfo>();
            foreach (var authorName in book.Authors)
            {
                people.Add(new PersonInfo
                {
                    Name = authorName,
                    Type = PersonKind.Author
                });
            }

            resultItem.ProviderIds[Name] = bookId.ToString();

            var result = new MetadataResult<Book>
            {
                Item = resultItem,
                HasMetadata = true,
                People = people
            };

            if (book.HasCover)
            {
                var coverUrl = client.GetCoverUrlAsync(bookId, cancellationToken).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    result.RemoteImages.Add((coverUrl, ImageType.Primary));
                }
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching metadata via HTTP for book {BookId}", bookId);
            return Task.FromResult(new MetadataResult<Book>());
        }
    }

    private static string? GetProviderId(BookInfo info)
    {
        return info.ProviderIds.GetValueOrDefault("Calibre");
    }

    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }
}