using System;
using System.Collections.Generic;

namespace GetIt_App.Services;

public enum DownloadPreset
{
    VideoMp4,
    AudioMp3,
    AudioFlac,
    Custom
}

public enum DownloadPresetFormat
{
    Mp4,
    Mp3,
    Flac
}

public sealed record DownloadPresetOption(DownloadPreset Preset, string Name);

public sealed record DownloadPresetDefaults(DownloadPresetFormat Format, string? AudioBitrate);

public sealed record DownloadPresetResolution(DownloadPresetFormat Format, string? Notice);

public static class DownloadPresetPolicy
{
    public static IReadOnlyList<DownloadPresetOption> Options { get; } = Array.AsReadOnly(new[]
    {
        new DownloadPresetOption(DownloadPreset.VideoMp4, "Vídeo MP4"),
        new DownloadPresetOption(DownloadPreset.AudioMp3, "Áudio MP3 (320 kbps)"),
        new DownloadPresetOption(DownloadPreset.AudioFlac, "Áudio FLAC"),
        new DownloadPresetOption(DownloadPreset.Custom, "Personalizado")
    });

    public static IReadOnlyList<string> SupportedFormats { get; } =
        Array.AsReadOnly(new[] { "MP4", "MOV", "MP3", "WAV", "FLAC" });

    public static DownloadPresetDefaults GetDefaults(DownloadPreset preset) => preset switch
    {
        DownloadPreset.VideoMp4 => new DownloadPresetDefaults(DownloadPresetFormat.Mp4, null),
        DownloadPreset.AudioMp3 => new DownloadPresetDefaults(DownloadPresetFormat.Mp3, "320"),
        DownloadPreset.AudioFlac => new DownloadPresetDefaults(DownloadPresetFormat.Flac, null),
        DownloadPreset.Custom => new DownloadPresetDefaults(DownloadPresetFormat.Mp4, null),
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    public static DownloadPresetResolution ResolveFormat(DownloadPreset preset, bool hasAudio, bool hasVideo)
    {
        var requested = GetDefaults(preset).Format;
        if ((requested == DownloadPresetFormat.Mp4 && hasVideo) ||
            ((requested is DownloadPresetFormat.Mp3 or DownloadPresetFormat.Flac) && hasAudio))
            return new DownloadPresetResolution(requested, null);

        if (hasAudio)
            return new DownloadPresetResolution(DownloadPresetFormat.Mp3,
                "Este link não oferece vídeo. Selecionamos MP3; você pode escolher outro formato.");

        if (hasVideo)
            return new DownloadPresetResolution(DownloadPresetFormat.Mp4,
                "Este link não oferece áudio separado. Selecionamos MP4; você pode escolher outra qualidade.");

        return new DownloadPresetResolution(requested,
            "Não foi possível confirmar os formatos disponíveis para este link. Confira as opções antes de baixar.");
    }
}
