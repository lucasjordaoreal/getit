using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GetIt_App.Services;

public enum BatchUrlStatus
{
    Ready,
    DuplicateInBatch,
    AlreadyInQueue,
    Invalid
}

public sealed record BatchUrlEntry(int LineNumber, string Input, string? NormalizedUrl, BatchUrlStatus Status);

public sealed record BatchUrlPreview(IReadOnlyList<BatchUrlEntry> Entries, int NonEmptyLineCount, bool IsOverLimit)
{
    public int ReadyCount => Entries.Count(entry => entry.Status == BatchUrlStatus.Ready);
    public int DuplicateInBatchCount => Entries.Count(entry => entry.Status == BatchUrlStatus.DuplicateInBatch);
    public int AlreadyInQueueCount => Entries.Count(entry => entry.Status == BatchUrlStatus.AlreadyInQueue);
    public int InvalidCount => Entries.Count(entry => entry.Status == BatchUrlStatus.Invalid);
}

public static class BatchUrlPolicy
{
    public const int MaximumLinks = 50;

    public static BatchUrlPreview CreatePreview(string? input, IEnumerable<string> queuedUrls)
    {
        ArgumentNullException.ThrowIfNull(queuedUrls);

        var queued = new HashSet<string>(queuedUrls, StringComparer.Ordinal);
        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<BatchUrlEntry>(MaximumLinks);
        var nonEmptyLineCount = 0;
        using var reader = new StringReader(input ?? string.Empty);
        var lineNumber = 0;
        while (reader.ReadLine() is { } value)
        {
            lineNumber++;
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                continue;

            nonEmptyLineCount++;
            if (nonEmptyLineCount > MaximumLinks)
                continue;

            if (!MediaUrlPolicy.TryNormalizeHttpUrl(trimmed, out var normalized))
            {
                entries.Add(new BatchUrlEntry(lineNumber, trimmed, null, BatchUrlStatus.Invalid));
                continue;
            }

            if (queued.Contains(normalized))
            {
                entries.Add(new BatchUrlEntry(lineNumber, trimmed, normalized, BatchUrlStatus.AlreadyInQueue));
                continue;
            }

            if (!seenInBatch.Add(normalized))
            {
                entries.Add(new BatchUrlEntry(lineNumber, trimmed, normalized, BatchUrlStatus.DuplicateInBatch));
                continue;
            }

            entries.Add(new BatchUrlEntry(lineNumber, trimmed, normalized, BatchUrlStatus.Ready));
        }

        return new BatchUrlPreview(entries, nonEmptyLineCount, nonEmptyLineCount > MaximumLinks);
    }
}
