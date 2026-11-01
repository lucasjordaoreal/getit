using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GetIt_App.Services;

public static class DownloadOutputResolver
{
    public static IReadOnlyList<string> GetProducedFiles(IEnumerable<string>? reportedPaths, string operationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationDirectory);

        var fullDirectory = Path.GetFullPath(operationDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return Array.Empty<string>();
        }

        var reportedFiles = new List<string>();
        foreach (var reportedPath in reportedPaths ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(reportedPath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(reportedPath);
            EnsurePathBelongsToOperation(fullPath, fullDirectory);
            if (IsCompleteFile(fullPath))
            {
                reportedFiles.Add(fullPath);
            }
        }

        if (reportedFiles.Count > 0)
        {
            return reportedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return Directory.EnumerateFiles(fullDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsCompleteFile)
            .ToArray();
    }

    public static string? ResolveSingleFile(string? reportedPath, string operationDirectory, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        var expectedExtension = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
        var candidates = GetProducedFiles(
            string.IsNullOrWhiteSpace(reportedPath) ? null : new[] { reportedPath },
            operationDirectory);
        var matchingFiles = candidates
            .Where(path => string.Equals(Path.GetExtension(path), expectedExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matchingFiles.Length switch
        {
            0 => null,
            1 => matchingFiles[0],
            _ => throw new InvalidDataException(
                $"Mais de um arquivo {expectedExtension} foi produzido pela mesma operação de download.")
        };
    }

    private static bool IsCompleteFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return File.Exists(path)
            && !fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains(".temp.", StringComparison.OrdinalIgnoreCase)
            && new FileInfo(path).Length > 0;
    }

    private static void EnsurePathBelongsToOperation(string path, string operationDirectory)
    {
        var relativePath = Path.GetRelativePath(operationDirectory, path);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals(".", StringComparison.Ordinal)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("O caminho informado pelo downloader está fora da operação atual.");
        }
    }
}
