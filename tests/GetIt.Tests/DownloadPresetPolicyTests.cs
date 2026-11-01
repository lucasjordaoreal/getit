using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class DownloadPresetPolicyTests
{
    [Fact]
    public void VideoPresetKeepsMp4AsTheDefaultWithoutPromisingAResolution()
    {
        var defaults = DownloadPresetPolicy.GetDefaults(DownloadPreset.VideoMp4);

        Assert.Equal(DownloadPresetFormat.Mp4, defaults.Format);
        Assert.Null(defaults.AudioBitrate);
    }

    [Fact]
    public void AudioMp3PresetSelects320Kbps()
    {
        var defaults = DownloadPresetPolicy.GetDefaults(DownloadPreset.AudioMp3);

        Assert.Equal(DownloadPresetFormat.Mp3, defaults.Format);
        Assert.Equal("320", defaults.AudioBitrate);
    }

    [Fact]
    public void FlacPresetSelectsFlacWithoutClaimingLosslessSourceQuality()
    {
        var defaults = DownloadPresetPolicy.GetDefaults(DownloadPreset.AudioFlac);

        Assert.Equal(DownloadPresetFormat.Flac, defaults.Format);
        Assert.Null(defaults.AudioBitrate);
    }

    [Fact]
    public void CustomPresetKeepsTheExistingMp4StartingBehaviorAndExposesAdvancedFormats()
    {
        var defaults = DownloadPresetPolicy.GetDefaults(DownloadPreset.Custom);

        Assert.Equal(DownloadPresetFormat.Mp4, defaults.Format);
        Assert.Contains("MOV", DownloadPresetPolicy.SupportedFormats);
        Assert.Contains("WAV", DownloadPresetPolicy.SupportedFormats);
        Assert.Contains("MP3", DownloadPresetPolicy.SupportedFormats);
        Assert.Contains("FLAC", DownloadPresetPolicy.SupportedFormats);
    }

    [Fact]
    public void VideoPresetFallsBackToAudioWhenTheSourceHasNoVideo()
    {
        var result = DownloadPresetPolicy.ResolveFormat(DownloadPreset.VideoMp4, hasAudio: true, hasVideo: false);

        Assert.Equal(DownloadPresetFormat.Mp3, result.Format);
        Assert.NotNull(result.Notice);
    }

    [Fact]
    public void AudioPresetFallsBackToVideoWhenTheSourceHasNoAudio()
    {
        var result = DownloadPresetPolicy.ResolveFormat(DownloadPreset.AudioFlac, hasAudio: false, hasVideo: true);

        Assert.Equal(DownloadPresetFormat.Mp4, result.Format);
        Assert.NotNull(result.Notice);
    }

    [Fact]
    public void PresetResolutionReportsWhenNoCompatibleMediaTypeWasConfirmed()
    {
        var result = DownloadPresetPolicy.ResolveFormat(DownloadPreset.AudioMp3, hasAudio: false, hasVideo: false);

        Assert.Equal(DownloadPresetFormat.Mp3, result.Format);
        Assert.NotNull(result.Notice);
    }
}
