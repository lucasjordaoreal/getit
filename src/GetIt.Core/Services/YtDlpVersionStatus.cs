using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GetIt_App.Services;

public sealed record YtDlpVersionStatus(string? InstalledVersion, string? LatestVersion, string? Error = null)
{
    public int? Comparison => YtDlpVersionPolicy.Compare(InstalledVersion, LatestVersion);
    public bool UpdateAvailable => Error is null && Comparison is < 0;
    public bool IsUpToDate => Error is null && Comparison is >= 0;

    public string Message => !string.IsNullOrWhiteSpace(Error) ? Error
        : Comparison is null ? $"Instalada: {InstalledVersion ?? "não identificada"}. Mais recente: {LatestVersion ?? "não identificada"}. Não foi possível comparar as versões."
        : Comparison < 0 ? $"Nova versão do yt-dlp disponível: {LatestVersion}. Você está usando {InstalledVersion}."
        : Comparison == 0 ? $"O yt-dlp já está atualizado (versão {InstalledVersion})."
        : $"Você está usando {InstalledVersion}, mais recente que a última versão estável ({LatestVersion}).";
}

public sealed record YtDlpUpdateResult(YtDlpVersionStatus Before, YtDlpVersionStatus After, bool Succeeded, string Message);

public static partial class YtDlpVersionPolicy
{
    [GeneratedRegex(@"^[vV]?(?<year>\d{4})\.(?<month>\d{1,2})\.(?<day>\d{1,2})(?:\.(?<revision>\d{1,12}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static int? Compare(string? installed, string? latest)
    {
        if (!TryParse(installed, out var installedDate, out var installedRevision)
            || !TryParse(latest, out var latestDate, out var latestRevision)) return null;
        var dateComparison = installedDate.CompareTo(latestDate);
        return dateComparison == 0 ? installedRevision.CompareTo(latestRevision) : dateComparison;
    }

    public static bool IsSupportedVersion(string? version) => TryParse(version, out _, out _);

    public static YtDlpUpdateResult VerifyUpdate(YtDlpVersionStatus before, string? installedAfter, string? error = null)
    {
        var after = new YtDlpVersionStatus(installedAfter, before.LatestVersion, error);
        if (error is not null)
            return new(before, after, false, $"Não foi possível atualizar o yt-dlp. {error}");
        if (!after.IsUpToDate)
            return new(before, after, false,
                $"A atualização não foi confirmada. Instalada: {installedAfter ?? "não identificada"}. Mais recente: {before.LatestVersion ?? "não identificada"}.");
        var changed = Compare(before.InstalledVersion, installedAfter) is < 0;
        return new(before, after, true, changed
            ? $"yt-dlp atualizado de {before.InstalledVersion} para {installedAfter}."
            : after.Message);
    }

    private static bool TryParse(string? text, out DateOnly date, out long revision)
    {
        date = default;
        revision = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = VersionPattern().Match(text.Trim());
        if (!match.Success) return false;
        var dateText = $"{match.Groups["year"].Value}-{int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture):00}-{int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture):00}";
        return DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            && (!match.Groups["revision"].Success || long.TryParse(match.Groups["revision"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out revision));
    }
}
