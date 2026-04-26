using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Calibre;

public class CalibreContentServerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string? _username;
    private readonly string? _password;

    public CalibreContentServerClient(string serverUrl, string? username = null, string? password = null)
    {
        _baseUrl = serverUrl.TrimEnd('/');
        _username = username;
        _password = password;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            var auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/interface-data/library", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<CalibreBook?> GetBookByIdAsync(int bookId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/books/{bookId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseBookJson(bookId, json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetCoverUrlAsync(int bookId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/books/{bookId}/cover", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return $"{_baseUrl}/books/{bookId}/cover";
        }
        catch
        {
            return null;
        }
    }

    public async Task<CalibreLibrary?> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/interface-data/library", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonConvert.DeserializeObject<CalibreLibrary>(json);
        }
        catch
        {
            return null;
        }
    }

    private CalibreBook ParseBookJson(int bookId, string json)
    {
        try
        {
            var data = JsonConvert.DeserializeObject<dynamic>(json);
            if (data == null) return new CalibreBook { Id = bookId };

            var book = new CalibreBook
            {
                Id = bookId,
                Title = data.title?.ToString() ?? "",
                Comments = data.comments?.ToString() ?? ""
            };

            if (data.authors != null)
            {
                var authors = new System.Collections.Generic.List<string>();
                foreach (var author in data.authors)
                {
                    authors.Add(author.name?.ToString() ?? "");
                }
                book.Authors = authors.ToArray();
            }

            if (data.tags != null)
            {
                var tags = new System.Collections.Generic.List<string>();
                foreach (var tag in data.tags)
                {
                    tags.Add(tag.name?.ToString() ?? "");
                }
                book.Tags = tags.ToArray();
            }

            DateTime parsedDate;
            if (DateTime.TryParse(data.pubdate?.ToString(), out parsedDate))
            {
                book.PubDate = parsedDate;
            }

            book.HasCover = data.has_cover == true;

            return book;
        }
        catch
        {
            return new CalibreBook { Id = bookId };
        }
    }

    public async Task<CalibreBook?> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            return null;

        isbn = isbn.Replace("-", "").Replace(" ", "");

        var library = await GetLibraryAsync(cancellationToken);
        if (library?.Books == null)
            return null;

        var normalizedIsbn = isbn.ToLowerInvariant();

        foreach (var book in library.Books.Values)
        {
            if (book.Identifiers == null) continue;

            foreach (var kvp in book.Identifiers)
            {
                if (string.Equals(kvp.Key, "isbn", StringComparison.OrdinalIgnoreCase))
                {
                    var bookIsbn = kvp.Value?.Replace("-", "").Replace(" ", "").ToLowerInvariant();
                    if (string.Equals(bookIsbn, normalizedIsbn))
                    {
                        return await GetBookByIdAsync(book.Id, cancellationToken);
                    }
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public class CalibreLibrary
{
    [JsonProperty("last_modified")]
    public string? LastModified { get; set; }

    [JsonProperty("books")]
    public Dictionary<string, CalibreLibraryBook>? Books { get; set; }
}

public class CalibreLibraryBook
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("authors")]
    public int[]? Authors { get; set; }

    [JsonProperty("has_cover")]
    public bool HasCover { get; set; }

    [JsonProperty("identifiers")]
    public Dictionary<string, string?>? Identifiers { get; set; }
}