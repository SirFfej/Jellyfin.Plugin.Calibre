using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Calibre.Tasks;

public class EnrichMetadataTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;

    public EnrichMetadataTask(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public string Name => "Enrich Data - Update JellyfinDB";

    public string Key => "CalibreUpdateJellyfin";

    public string Description => "Updates linked books with Calibre overview/comments.";

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
        var logDir = Plugin.AppPaths?.LogDirectoryPath ?? string.Empty;
        const string taskName = "enrich";

        TaskLogger.Log(logDir, taskName, "=== Enrich Calibre Metadata Task Started ===");

        if (config == null || !config.EnableMetadataProvider)
        {
            TaskLogger.Log(logDir, taskName, "Calibre enrichment is disabled — skipping.");
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

        var linkedItems = allItems.Where(i => i.ProviderIds.ContainsKey("Calibre")).ToList();

        if (linkedItems.Count == 0)
        {
            TaskLogger.Log(logDir, taskName, "No linked Calibre books found.");
            return;
        }

        TaskLogger.Log(logDir, taskName, $"Found {linkedItems.Count} linked books to enrich.");

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

        var enriched = 0;
        var skipped = 0;
        var errors = 0;

        using (db)
        using (client)
        {
            for (int i = 0; i < linkedItems.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(100.0 * i / linkedItems.Count);

                var item = linkedItems[i];

                try
                {
                    var providerId = item.ProviderIds.GetValueOrDefault("Calibre");
                    if (string.IsNullOrEmpty(providerId) || !int.TryParse(providerId, out var bookId))
                    {
                        skipped++;
                        continue;
                    }

                    CalibreBook? book = null;
                    if (db != null)
                    {
                        book = db.GetBookByIdAsync(bookId, cancellationToken).GetAwaiter().GetResult();
                    }
                    else if (client != null)
                    {
                        book = await client.GetBookByIdAsync(bookId, cancellationToken);
                    }

                    if (book == null)
                    {
                        TaskLogger.Log(logDir, taskName, $"WARNING: Calibre book ID {bookId} not found for {item.Name}");
                        skipped++;
                        continue;
                    }

                    var updated = false;

                    if (!string.IsNullOrEmpty(book.Comments))
                    {
                        item.Overview = book.Comments;
                        updated = true;
                        TaskLogger.Log(logDir, taskName, $"Added overview to: {item.Name}");
                    }

                    if (updated)
                    {
                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            cancellationToken).ConfigureAwait(false);

                        enriched++;
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
        TaskLogger.Log(logDir, taskName, $"Complete. Enriched: {enriched}, Skipped: {skipped}, Errors: {errors}");
        TaskLogger.Log(logDir, taskName, "=== Enrich Calibre Metadata Task Complete ===");
    }
}