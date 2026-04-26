using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Calibre.Tasks;

public class LinkCalibreTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;

    public LinkCalibreTask(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public string Name => "Enrich Data - Link to Calibre book";

    public string Key => "CalibreLinkToBook";

    public string Description => "Links unlinked Jellyfin books to Calibre by title matching.";

    public string Category => "Calibre";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
            }
        ];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        var logDir = Plugin.AppPaths?.LogDirectoryPath ?? string.Empty;
        const string taskName = "link";

        TaskLogger.Log(logDir, taskName, "=== Link Calibre Task Started ===");

        if (config == null || !config.EnableMetadataProvider)
        {
            TaskLogger.Log(logDir, taskName, "Calibre linking is disabled — skipping.");
            return;
        }

        var useHostMode = config.UseHostAccessMode;
        var location = useHostMode ? config.CalibreServerUrl : config.CalibreLibraryPath;
        TaskLogger.Log(logDir, taskName, $"Mode: {(useHostMode ? "Host (Calibre Content Server)" : "Local (Direct database)")}");
        TaskLogger.Log(logDir, taskName, $"Location: {location ?? "(not configured)"}");

        var includedLibraryIds = config.IncludedLibraryIds ?? [];
        var virtualFolders = _libraryManager.GetVirtualFolders()
            .Where(lf => lf.CollectionType.HasValue && lf.CollectionType.Value.ToString().Contains("book", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var matchingLibraries = virtualFolders
            .Where(lf => includedLibraryIds.Count == 0 || includedLibraryIds.Contains(lf.ItemId.ToString()))
            .ToList();

        if (matchingLibraries.Count == 0 && includedLibraryIds.Count > 0)
        {
            TaskLogger.Log(logDir, taskName, $"No matching libraries found for the configured IncludedLibraryIds: {string.Join(", ", includedLibraryIds)}");
            return;
        }

        var allItems = new List<BaseItem>();
        foreach (var lib in matchingLibraries)
        {
            var libQuery = new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Book],
                Recursive = true,
                ParentId = Guid.Parse(lib.ItemId.ToString())
            };
            allItems.AddRange(_libraryManager.GetItemList(libQuery));
        }

        if (allItems.Count == 0)
        {
            TaskLogger.Log(logDir, taskName, "No Book items found in library.");
            return;
        }

        TaskLogger.Log(logDir, taskName, $"Found {allItems.Count} books to check.");

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
            TaskLogger.Log(logDir, taskName, "No Calibre configuration found.");
            return;
        }

        var linked = 0;
        var skipped = 0;
        var errors = 0;

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
                        skipped++;
                        continue;
                    }

                    var title = item.Name ?? string.Empty;
                    string? matchedTitle = null;
                    int? matchedId = null;
                    string matchMethod = "none";

                    string? itemIsbn = item.ProviderIds.GetValueOrDefault("ISBN");

                    if (db != null)
                    {
                        if (!string.IsNullOrWhiteSpace(itemIsbn))
                        {
                            var book = db.SearchByIsbnAsync(itemIsbn, cancellationToken).GetAwaiter().GetResult();
                            if (book != null)
                            {
                                matchedId = book.Id;
                                matchedTitle = book.Title;
                                matchMethod = "ISBN";
                            }
                        }

                        if (matchedId == null)
                        {
                            var results = db.SearchBooksAsync(title, 3, cancellationToken).GetAwaiter().GetResult();
                            var match = results.FirstOrDefault();
                            if (match != null)
                            {
                                matchedId = match.Id;
                                matchedTitle = match.Title;
                                matchMethod = "title";
                            }
                        }
                    }
                    else if (client != null)
                    {
                        if (!string.IsNullOrWhiteSpace(itemIsbn))
                        {
                            var book = await client.SearchByIsbnAsync(itemIsbn, cancellationToken);
                            if (book != null)
                            {
                                matchedId = book.Id;
                                matchedTitle = book.Title;
                                matchMethod = "ISBN";
                            }
                        }

                        if (matchedId == null)
                        {
                            var library = client.GetLibraryAsync(cancellationToken).GetAwaiter().GetResult();
                            if (library?.Books != null)
                            {
                                var match = library.Books.Values
                                    .FirstOrDefault(b => b.Title?.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0);
                                if (match != null)
                                {
                                    matchedId = match.Id;
                                    matchedTitle = match.Title;
                                    matchMethod = "title";
                                }
                            }
                        }
                    }

                    if (matchedId != null)
                    {
                        item.ProviderIds["Calibre"] = matchedId.Value.ToString();
                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            cancellationToken).ConfigureAwait(false);

                        TaskLogger.Log(logDir, taskName, $"Linked ({matchMethod}): {item.Name} -> Calibre ID {matchedId}");
                        if (matchedTitle != null && !string.Equals(matchedTitle, item.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            TaskLogger.Log(logDir, taskName, $"  Calibre title: {matchedTitle}");
                        }
                        linked++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    TaskLogger.Log(logDir, taskName, $"ERROR: {item.Name}: {ex.Message}");
                    errors++;
                }
            }
        }

        progress.Report(100);
        TaskLogger.Log(logDir, taskName, $"Complete. Linked: {linked}, Skipped: {skipped}, Errors: {errors}");
        TaskLogger.Log(logDir, taskName, "=== Link Calibre Task Complete ===");
    }
}