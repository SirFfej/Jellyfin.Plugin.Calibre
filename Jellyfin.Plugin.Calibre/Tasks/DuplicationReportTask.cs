using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Calibre.Tasks;

public class DuplicationReportTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;

    public DuplicationReportTask(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public string Name => "Report Jellyfin Duplicates";

    public string Key => "JellyfinDuplicationReport";

    public string Description => "Generates a report of duplicate titles with different file locations in Jellyfin library.";

    public string Category => "Calibre";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        var logDir = Plugin.AppPaths?.LogDirectoryPath ?? string.Empty;
        const string taskName = "duplication";

        TaskLogger.Log(logDir, taskName, "=== Duplication Report Task Started ===");

        if (config == null || !config.EnableMetadataProvider)
        {
            TaskLogger.Log(logDir, taskName, "Calibre is disabled — skipping.");
            return Task.CompletedTask;
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
            TaskLogger.Log(logDir, taskName, $"No matching libraries found for the configured IncludedLibraryIds: {string.Join(", ", includedLibraryIds)}");
            TaskLogger.Log(logDir, taskName, "=== Duplication Report Task Complete ===");
            return Task.CompletedTask;
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
            TaskLogger.Log(logDir, taskName, "=== Duplication Report Task Complete ===");
            return Task.CompletedTask;
        }

        TaskLogger.Log(logDir, taskName, $"Found {allItems.Count} books in Jellyfin library.");

        var titleGroups = allItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .GroupBy(i => NormalizeTitle(i.Name))
            .Where(g => g.Count() > 1)
            .ToList();

        if (titleGroups.Count == 0)
        {
            TaskLogger.Log(logDir, taskName, "No duplicate titles found.");
            TaskLogger.Log(logDir, taskName, "=== Duplication Report Task Complete ===");
            return Task.CompletedTask;
        }

        TaskLogger.Log(logDir, taskName, $"Found {titleGroups.Count} duplicate title groups.");

        var totalDuplicates = titleGroups.Sum(g => g.Count());
        var reportPath = Path.Combine("/Jellyfin/Jellyfin", $"calibre-duplication-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        var lines = new List<string>
        {
            "filename,type,location"
        };

        var processed = 0;
        foreach (var group in titleGroups.OrderBy(g => g.Key))
        {
            foreach (var item in group)
            {
                var title = EscapeCsv(item.Name);
                var fileType = GetFileType(item.Path);
                var path = EscapeCsv(item.Path ?? "");
                lines.Add($"{title},{fileType},{path}");

                processed++;
                progress?.Report(100.0 * processed / totalDuplicates);
            }
        }

        File.WriteAllLines(reportPath, lines);

        TaskLogger.Log(logDir, taskName, $"Report Written: {reportPath}");
        TaskLogger.Log(logDir, taskName, $"Total Duplicates: {totalDuplicates} (in {titleGroups.Count} title groups)");
        TaskLogger.Log(logDir, taskName, "=== Duplication Report Task Complete ===");
        return Task.CompletedTask;
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";

        return title.Trim().ToLowerInvariant();
    }

    private static string GetFileType(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        return Path.GetExtension(path)?.TrimStart('.').ToUpperInvariant() ?? "";
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}