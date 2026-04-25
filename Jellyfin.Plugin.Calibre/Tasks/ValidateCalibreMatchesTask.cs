using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Calibre.Tasks;

public class ValidateCalibreMatchesTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ValidateCalibreMatchesTask> _logger;

    public ValidateCalibreMatchesTask(
        ILibraryManager libraryManager,
        ILogger<ValidateCalibreMatchesTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "Validate Calibre matches";

    public string Key => "CalibreValidateMatches";

    public string Description => "Re-validates Calibre matches for previously linked items and logs the results.";

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
        if (config == null || !config.EnableMetadataProvider)
        {
            _logger.LogInformation("Calibre validation is disabled — skipping.");
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

        var itemsWithProviderId = allItems
            .Where(i => i.ProviderIds.ContainsKey("Calibre"))
            .ToList();

        _logger.LogInformation("Found {Count} items with Calibre provider ID to validate.", itemsWithProviderId.Count);

        if (itemsWithProviderId.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Starting Calibre match validation for {Count} items.", itemsWithProviderId.Count);

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

        var valid = 0;
        var missing = 0;
        var errors = 0;

        using (db)
        using (client)
        {
            for (int i = 0; i < itemsWithProviderId.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(100.0 * i / itemsWithProviderId.Count);

                var item = itemsWithProviderId[i];

                try
                {
                    var providerId = item.ProviderIds.GetValueOrDefault("Calibre");
                    if (string.IsNullOrEmpty(providerId) || !int.TryParse(providerId, out var bookId))
                    {
                        errors++;
                        continue;
                    }

                    CalibreBook? calibreBook = null;

                    if (db != null)
                    {
                        calibreBook = db.GetBookByIdAsync(bookId, cancellationToken).GetAwaiter().GetResult();
                    }
                    else if (client != null)
                    {
                        calibreBook = client.GetBookByIdAsync(bookId, cancellationToken).GetAwaiter().GetResult();
                    }

                    if (calibreBook != null)
                    {
                        if (string.Equals(calibreBook.Title, item.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            valid++;
                            _logger.LogDebug("Valid: {Name} (Calibre ID {CalibreId})", item.Name, bookId);
                        }
                        else
                        {
                            _logger.LogInformation("Title mismatch for {Name}: Jellyfin='{JfTitle}', Calibre='{CalibreTitle}'",
                                item.Name, item.Name, calibreBook.Title);
                            valid++;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Missing: {Name} — Calibre book ID {CalibreId} not found in database",
                            item.Name, bookId);
                        item.ProviderIds.Remove("Calibre");
                        await _libraryManager.UpdateItemAsync(
                            item,
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            cancellationToken).ConfigureAwait(false);
                        missing++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Validation failed for {Name}", item.Name);
                    errors++;
                }
            }
        }

        progress.Report(100);
        _logger.LogInformation("Calibre match validation complete. Valid: {Valid}, Missing removed: {Missing}, Errors: {Errors}",
            valid, missing, errors);
    }
}