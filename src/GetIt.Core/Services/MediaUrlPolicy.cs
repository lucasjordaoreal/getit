using System;
using System.Linq;

namespace GetIt_App.Services;

/// <summary>Normalizes a single media URL and identifies links eligible for automatic pasting.</summary>
public static class MediaUrlPolicy
{
    public static bool TryNormalizeHttpUrl(string? text, out string url)
    {
        url = string.Empty;
        var candidate = text?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Any(char.IsControl) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        // AbsoluteUri escapes quotation marks/spaces instead of passing them into process arguments.
        url = uri.AbsoluteUri;
        return true;
    }

    public static bool IsYouTubeVideoUrl(string? text)
    {
        if (!TryNormalizeHttpUrl(text, out var normalized)) return false;
        var uri = new Uri(normalized);
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return path.Length == 1;

        if (!uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Length == 2 && path[0] is "shorts" or "live" or "embed") return true;
        return path.Length == 1 && path[0] == "watch" && uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.StartsWith("v=", StringComparison.Ordinal) && part.Length > 2);
    }
}
