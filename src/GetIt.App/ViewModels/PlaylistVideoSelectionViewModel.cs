using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GetIt_App.Services;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GetIt_App.ViewModels;

public partial class PlaylistVideoSelectionViewModel : ObservableObject
{
    public int Position { get; }
    public string PositionText => $"#{Position}";
    public string DisplayTitle => string.IsNullOrWhiteSpace(Entry.Title) ? "Título não disponível" : Entry.Title;
    public string DetailsText
    {
        get
        {
            var duration = Entry.DurationSeconds is { } seconds && seconds <= TimeSpan.MaxValue.TotalSeconds
                ? FormatDuration(seconds)
                : null;
            var status = Entry.IsAvailable ? duration : Entry.UnavailableReason;
            return string.IsNullOrWhiteSpace(status) ? PositionText : $"{PositionText} · {status}";
        }
    }

    public bool IsEligible => Entry.IsAvailable && !string.IsNullOrWhiteSpace(Entry.Url);
    public PlaylistVideoEntry Entry { get; }
    public BitmapImage? ThumbnailImage { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public PlaylistVideoSelectionViewModel(PlaylistVideoEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Position = entry.Position;
        if (Uri.TryCreate(entry.Thumbnail, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            ThumbnailImage = new BitmapImage { UriSource = uri, DecodePixelWidth = 128 };
        }
    }

    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1:D2}:{2:D2}",
            (long)Math.Floor(duration.TotalHours),
            duration.Minutes,
            duration.Seconds);
    }
}
