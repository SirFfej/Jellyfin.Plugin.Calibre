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

public class ValidateCalibreTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;

    public ValidateCalibreTask(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public string Name => "Validate Calibre Links";

    public string Key => "CalibreValidateLinks";

    public string Description => "Validates Calibre links and removes orphaned links.";

    public string Category => "Calibre";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.WeeklyTrigger,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
            }
        ];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        var logDir = Plugin.AppPaths?.LogDirectoryPath ?? string.Empty;
        const string taskName = "validate";

        TaskLogger.Log(logDir, taskName, "=== Validate Calibre Task Started ===");

        if (config == null || !config.EnableMetadataProvider)
        {
            TaskLogger.Log(logDir, taskName, "Calibre validation is disabled — skipping.");
            return;
        }

        var useHostMode = config.UseHostAccessMode;
        TaskLogger.Log(logDir, taskName, $"Mode: {(useHostMode ? "Host" : "Local")}");

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

        TaskLogger.Log(logDir, taskName, $"Found {linkedItems.Count} linked books.");

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

        var valid = 0;
        var removed = 0;
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
                        item.ProviderIds.Remove("Calibre");
                        removed++;
                        continue;
                    }

                    CalibreBook? book = null;
                    if (db != null)
                    {
                        book = db.GetBookByIdAsync(bookId, cancellationToken).GetAwaiter().GetResult();
                    }
                    else if (client != null)
                    {
                        book = client.GetBookByIdAsync(bookId, cancellationToken).GetAwaiter().GetResult();
                    }

                    if (book != null)
                    {
                        var titleMatch = string.Equals(book.Title, item.Name, StringComparison.OrdinalIgnoreCase);
                        TaskLogger.Log(logDir, titleMatch ? "OK" : "MISMATCH", $"[{book.Id}] {item.Name}");
                        valid++;
                    }
                    else
                    {
                        TaskLogger.Log(logDir, taskName, $"REMOVED: {item.Name} (Calibre ID {bookId} not found)");
                        item.ProviderIds.Remove("Calibre");
                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            cancellationToken).ConfigureAwait(false);
                        removed++;
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
        TaskLogger.Log(logDir, taskName, $"Complete. Valid: {valid}, Removed: {removed}, Errors: {errors}");
        TaskLogger.Log(logDir, taskName, "=== Validate Calibre Task Complete ===");
    }
}