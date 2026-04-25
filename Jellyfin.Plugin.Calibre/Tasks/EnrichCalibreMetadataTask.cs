using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Calibre.Tasks;

public class EnrichCalibreMetadataTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<EnrichCalibreMetadataTask> _logger;

    public EnrichCalibreMetadataTask(
        ILibraryManager libraryManager,
        ILogger<EnrichCalibreMetadataTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "Enrich existing data with Calibre";

    public string Key => "CalibreEnrichExisting";

    public string Description => "Checks existing items against Calibre for items not yet connected and enriches them with Calibre metadata.";

    public string Category => "Calibre";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        ];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || !config.EnableMetadataProvider)
        {
            _logger.LogInformation("Calibre metadata enrichment is disabled — skipping.");
            return;
        }

        var includedLibraryIds = config.IncludedLibraryIds ?? [];
        var virtualFolders = _libraryManager.GetVirtualFolders()
            .Where(lf => lf.CollectionType.HasValue && lf.CollectionType.Value.ToString().Contains("book", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var matchingLibraries = virtualFolders
            .Where(lf => includedLibraryIds.Count == 0 || includedLibraryIds.Contains(lf.ItemId.ToString()))
            .ToList();

        if (matchingLibraries.Count == 0 && includedLibraryIds.Count > 0)
        {
            _logger.LogWarning("No matching libraries found for the configured IncludedLibraryIds: {Ids}", string.Join(", ", includedLibraryIds));
            return;
        }

        _logger.LogInformation("Processing {Count} book libraries: {Names}", matchingLibraries.Count, string.Join(", ", matchingLibraries.Select(l => l.Name)));

        var allItems = new List<BaseItem>();
        foreach (var lib in matchingLibraries)
        {
            var libQuery = new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Book],
                Recursive = true,
                ParentId = Guid.Parse(lib.ItemId.ToString())
            };

            var libItems = _libraryManager.GetItemList(libQuery);
            allItems.AddRange(libItems);
        }

        if (allItems.Count == 0)
        {
            _logger.LogInformation("No Book items found in library.");
            return;
        }

        _logger.LogInformation("Starting Calibre metadata enrichment for {Count} items.", allItems.Count);

        var useHostMode = config.UseHostAccessMode;
        CalibreDatabase? db = null;
        CalibreContentServerClient? client = null;

        if (!useHostMode && !string.IsNullOrEmpty(config.CalibreLibraryPath))
        {
            db = new CalibreDatabase(config.CalibreLibraryPath);
        }
        else if (useHostMode && !string.IsNullOrEmpty(config.CalibreServerUrl))
        {
            client = new CalibreContentServerClient(config.CalibreServerUrl, config.Username, config.Password);
        }
        else
        {
            _logger.LogWarning("No Calibre configuration found.");
            return;
        }

        var matched = 0;
        var notMatched = 0;

        using (db)
        using (client)
        {
            for (int i = 0; i < allItems.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(100.0 * i / allItems.Count);

                var item = allItems[i];

                try
                {
                    if (item.ProviderIds.ContainsKey("Calibre"))
                    {
                        notMatched++;
                        continue;
                    }

                    var bookTitle = item.Name ?? string.Empty;
                    CalibreBook? calibreBook = null;

                    if (db != null)
                    {
                        var results = db.SearchBooksAsync(bookTitle, 5, cancellationToken).GetAwaiter().GetResult();
                        calibreBook = results.FirstOrDefault();
                    }
                    else if (client != null)
                    {
                        var library = client.GetLibraryAsync(cancellationToken).GetAwaiter().GetResult();
                        if (library?.Books != null)
                        {
                            var match = library.Books.Values
                                .FirstOrDefault(b => b.Title?.IndexOf(bookTitle, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (match != null)
                            {
                                calibreBook = new CalibreBook
                                {
                                    Id = match.Id,
                                    Title = match.Title ?? "",
                                    Authors = Array.Empty<string>()
                                };
                            }
                        }
                    }

                    if (calibreBook != null)
                    {
                        item.ProviderIds["Calibre"] = calibreBook.Id.ToString();

                        if (!string.IsNullOrEmpty(calibreBook.Comments))
                        {
                            item.Overview = calibreBook.Comments;
                        }

                        var people = new List<PersonInfo>();
                        foreach (var authorName in calibreBook.Authors)
                        {
                            people.Add(new PersonInfo
                            {
                                Name = authorName,
                                Type = PersonKind.Author
                            });
                        }

                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            cancellationToken).ConfigureAwait(false);

                        _logger.LogInformation("Enriched: {Name} (matched to Calibre ID {CalibreId})", item.Name, calibreBook.Id);
                        matched++;
                    }
                    else
                    {
                        notMatched++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Enrichment failed for {Name}", item.Name);
                }
            }
        }

        progress.Report(100);
        _logger.LogInformation("Calibre metadata enrichment complete. Matched: {Matched}, Skipped: {Skipped}", matched, notMatched);
    }
}