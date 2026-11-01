using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GetIt_App.Services;

public sealed record PlaylistVideoEntry(
    int Position,
    string? Title,
    double? DurationSeconds,
    string? Thumbnail,
    string? Url,
    bool IsAvailable,
    string? UnavailableReason);

public sealed record PlaylistPreview(
    string? Title,
    string? Uploader,
    string? Thumbnail,
    int? TotalCount,
    IReadOnlyList<PlaylistVideoEntry> Videos,
    bool IsPartial);

public sealed record PlaylistSelectionResult(IReadOnlyList<string> Urls, int DuplicateCount, int UnavailableCount);

public static class PlaylistPreviewPolicy
{
    public const int MaximumPreviewItems = 50;

    public static PlaylistPreview? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("_type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            type.GetString() is not ("playlist" or "multi_video"))
        {
            return null;
        }

        var entries = root.TryGetProperty("entries", out var entriesElement) &&
            entriesElement.ValueKind == JsonValueKind.Array
                ? entriesElement
                : default;
        var entryCount = entries.ValueKind == JsonValueKind.Array ? entries.GetArrayLength() : 0;
        var videos = new List<PlaylistVideoEntry>(Math.Min(entryCount, MaximumPreviewItems));
        if (entries.ValueKind == JsonValueKind.Array)
        {
            var position = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                position++;
                if (position > MaximumPreviewItems)
                    break;

                videos.Add(ParseEntry(entry, position));
            }
        }

        var totalCount = ReadNonNegativeInt(root, "playlist_count");
        return new PlaylistPreview(
            ReadString(root, "title"),
            ReadString(root, "uploader") ?? ReadString(root, "channel"),
            ReadString(root, "thumbnail"),
            totalCount,
            videos,
            entryCount > MaximumPreviewItems ||
            totalCount > MaximumPreviewItems ||
            (totalCount is null && entryCount >= MaximumPreviewItems));
    }

    public static PlaylistSelectionResult ResolveSelection(
        PlaylistPreview preview,
        IEnumerable<int> selectedPositions,
        IEnumerable<string> queuedUrls)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(selectedPositions);
        ArgumentNullException.ThrowIfNull(queuedUrls);

        var selected = selectedPositions.ToHashSet();
        var queued = queuedUrls.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var urls = new List<string>();
        var duplicates = 0;
        var unavailable = 0;

        foreach (var video in preview.Videos)
        {
            if (!selected.Contains(video.Position))
                continue;

            if (!video.IsAvailable || string.IsNullOrWhiteSpace(video.Url))
            {
                unavailable++;
                continue;
            }

            if (queued.Contains(video.Url) || !seen.Add(video.Url))
            {
                duplicates++;
                continue;
            }

            if (urls.Count == BatchUrlPolicy.MaximumLinks)
            {
                unavailable++;
                continue;
            }

            urls.Add(video.Url);
        }

        return new PlaylistSelectionResult(urls, duplicates, unavailable);
    }

    private static PlaylistVideoEntry ParseEntry(JsonElement entry, int position)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return Unavailable(position, null, null, "Este item não retornou metadados.");

        var title = ReadString(entry, "title");
        var availability = ReadString(entry, "availability");
        var unavailable = availability is not null &&
            availability is not ("public" or "unlisted");
        var id = ReadString(entry, "id");
        var thumbnail = ReadThumbnail(entry);
        if (thumbnail is null && IsYoutubeExtractor(entry) && IsYoutubeVideoId(id))
            thumbnail = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";

        var url = ReadSafeUrl(entry, "webpage_url");
        if (url is null && IsYoutubeExtractor(entry))
        {
            if (IsYoutubeVideoId(id))
                url = $"https://www.youtube.com/watch?v={id}";
        }

        if (unavailable || url is null)
        {
            var reason = availability is not null && availability is not ("public" or "unlisted")
                ? "Indisponível ou requer acesso."
                : "O extrator não forneceu um endereço individual seguro.";
            return Unavailable(position, title, thumbnail, reason,
                ReadNonNegativeDouble(entry, "duration"));
        }

        return new PlaylistVideoEntry(
            ReadPositiveInt(entry, "playlist_index") ?? position,
            title,
            ReadNonNegativeDouble(entry, "duration"),
            thumbnail,
            url,
            true,
            null);
    }

    private static string? ReadThumbnail(JsonElement entry)
    {
        var thumbnail = ReadSafeUrl(entry, "thumbnail");
        if (thumbnail is not null ||
            !entry.TryGetProperty("thumbnails", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
        {
            return thumbnail;
        }

        foreach (var candidate in thumbnails.EnumerateArray().Reverse())
        {
            thumbnail = ReadSafeUrl(candidate, "url");
            if (thumbnail is not null)
                return thumbnail;
        }

        return null;
    }

    private static PlaylistVideoEntry Unavailable(
        int position,
        string? title,
        string? thumbnail,
        string reason,
        double? duration = null) =>
        new(position, title, duration, thumbnail, null, false, reason);

    private static string? ReadSafeUrl(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        if (!MediaUrlPolicy.TryNormalizeHttpUrl(value, out var normalized) ||
            !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return normalized;
    }

    private static bool IsYoutubeExtractor(JsonElement entry)
    {
        var extractor = ReadString(entry, "ie_key") ?? ReadString(entry, "extractor_key");
        return string.Equals(extractor, "Youtube", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYoutubeVideoId(string? id) =>
        id is { Length: 11 } && id.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadNonNegativeInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result) &&
        result >= 0
            ? result
            : null;

    private static int? ReadPositiveInt(JsonElement element, string propertyName) =>
        ReadNonNegativeInt(element, propertyName) is > 0 and var result ? result : null;

    private static double? ReadNonNegativeDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var result) &&
        double.IsFinite(result) &&
        result >= 0
            ? result
            : null;
}

public static class PlaylistUrlPolicy
{
    public static bool MayRepresentPlaylist(string? text)
    {
        if (!MediaUrlPolicy.TryNormalizeHttpUrl(text, out var normalized))
            return false;

        var uri = new Uri(normalized);
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (query.Any(parameter =>
                parameter.Split('=', 2)[0].Equals("list", StringComparison.OrdinalIgnoreCase)))
            return true;

        return uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("playlist", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("collection", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("sets", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAmbiguousVideoAndPlaylistUrl(string? text)
    {
        if (!MediaUrlPolicy.TryNormalizeHttpUrl(text, out var normalized))
            return false;

        var uri = new Uri(normalized);
        var hasPlaylist = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(parameter => parameter.Split('=', 2)[0].Equals("list", StringComparison.OrdinalIgnoreCase));
        return hasPlaylist && MediaUrlPolicy.IsYouTubeVideoUrl(normalized);
    }
}
