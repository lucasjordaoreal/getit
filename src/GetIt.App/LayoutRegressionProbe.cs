#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GetIt_App.Models;
using GetIt_App.Services;
using GetIt_App.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace GetIt_App;

/// <summary>
/// Opt-in, debug-only regression check against the actual measured WinUI tree.
/// It uses synthetic metadata and never invokes a download or clipboard operation.
/// </summary>
internal static class LayoutRegressionProbe
{
    private const double Tolerance = 0.75;
    public static bool IsEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GETIT_LAYOUT_CHECK_DIRECTORY"));

    public static async Task RunAsync(MainWindow window, Frame frame)
    {
        if (!IsEnabled) return;
        var outputDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("GETIT_LAYOUT_CHECK_DIRECTORY")!);
        var report = new ProbeReport();
        MainPageViewModel? viewModel = null;
        DownloadItemViewModel[] originalQueue = Array.Empty<DownloadItemViewModel>();
        string? originalFolder = null;
        var originalDownloading = false;
        var originalCancelling = false;
        var fakeService = new NoIoDownloadService();
        try
        {
            Directory.CreateDirectory(outputDirectory);
            await SettleAsync(frame);
            var page = frame.Content as MainPage ?? throw new InvalidOperationException("The download page is not loaded.");
            viewModel = page.ViewModel;
            viewModel.DeactivateClipboardMonitoring();
            originalQueue = viewModel.DownloadQueue.ToArray();
            originalFolder = viewModel.DownloadFolder;
            originalDownloading = viewModel.IsDownloadingAll;
            originalCancelling = viewModel.IsCancelling;
            if (originalDownloading || originalQueue.Any(item => item.IsDownloading || item.IsFetching))
                throw new InvalidOperationException("The probe refuses to interrupt an existing operation.");

            var input = Descendants<TextBox>(page).Single(control => control.Name == "UrlTextBox");
            var add = FindButton(page, viewModel.AddUrlCommand);
            input.Text = string.Empty;
            await SettleAsync(page);
            report.Check("add/empty-input-disabled", !add.IsEnabled);
            var caption = Descendants<TextBlock>(add).Single(text => text.Text == "Adicionar");
            report.Check("add/caption-fits-button", Contains(Measure(add, page), Measure(caption, page)));
            input.Text = "https://example.invalid/video";
            await SettleAsync(page);
            report.Check("add/link-enables-button", add.IsEnabled && add.Command.CanExecute(null));
            input.Text = "endereço inválido";
            await SettleAsync(page);
            add.Command.Execute(null);
            report.Check("add/invalid-link-feedback", !string.IsNullOrWhiteSpace(viewModel.InputError));
            report.Check("add/invalid-link-not-enqueued", viewModel.DownloadQueue.Count == originalQueue.Length);
            input.Text = string.Empty;
            await SettleAsync(page);

            // Exercise the real Add command against an in-memory metadata provider.
            var metadataService = new SyntheticMetadataService();
            var commandModel = new MainPageViewModel(metadataService);
            try
            {
                commandModel.Url = "https://example.invalid/video";
                commandModel.AddUrlCommand.Execute(null);
                await SettleAsync(page);
                report.Check("add/valid-link-enqueued", commandModel.DownloadQueue.Count == 1);
                report.Check("add/metadata-loaded", commandModel.DownloadQueue.Single().Metadata?.Title == "Vídeo de teste");
                report.Check("add/input-cleared", commandModel.Url.Length == 0 && !commandModel.CanAddUrl);
                commandModel.Url = "https://example.invalid/video";
                commandModel.AddUrlCommand.Execute(null);
                report.Check("add/duplicate-rejected", commandModel.DownloadQueue.Count == 1 && commandModel.InputError.Length > 0);
                report.Check("add/single-metadata-request", metadataService.Calls == 1);
            }
            finally { commandModel.Shutdown(); }

            viewModel.DownloadQueue.Clear();
            viewModel.DownloadFolder = @"G:\" + string.Concat(Enumerable.Repeat("pasta-com-nome-muito-comprido\\", 80));
            var item = new DownloadItemViewModel("https://example.invalid/layout-check", fakeService);
            item.AvailableFormats.Add(new VideoFormat
            {
                FormatId = "synthetic-1080", Height = 1080, Width = 1920,
                Resolution = "1920x1080", Fps = 60, VideoCodec = "h264", AudioCodec = "none"
            });
            item.SelectedFormat = item.AvailableFormats[0];
            viewModel.DownloadQueue.Add(item);

            var baselines = new Dictionary<string, Bounds>();
            Bounds? footerBaseline = null;
            foreach (var titleLength in new[] { 1, 512, 2048 })
            {
                item.Metadata = new VideoMetadata { Id = "synthetic", Title = new string('W', titleLength), Thumbnail = string.Empty };
                item.StatusMessage = new string('S', 3000);
                item.Speed = "123.45 MB/s";
                item.Eta = "12:34:56";
                foreach (var state in new[] { "completed", "pending", "downloading" })
                {
                    viewModel.IsDownloadingAll = false;
                    item.IsDownloading = false;
                    item.IsQueueLocked = false;
                    item.IsDownloadComplete = state == "completed";
                    if (state == "downloading")
                    {
                        viewModel.IsDownloadingAll = true;
                        item.IsQueueLocked = true;
                        item.IsDownloading = true;
                        item.Progress = 63;
                    }
                    await SettleAsync(page);
                    var list = Descendants<ListView>(page).Single(element => ReferenceEquals(element.ItemsSource, viewModel.DownloadQueue));
                    var viewport = Measure(list, page);
                    var pageBounds = new Bounds(0, 0, page.ActualWidth, page.ActualHeight);
                    var sample = new ProbeSample { State = state, TitleLength = titleLength, Page = pageBounds, QueueViewport = viewport };
                    report.Samples.Add(sample);
                    var actions = new (string Name, ICommand Command, bool ExpectedVisible)[]
                    {
                        ("another-format", item.PrepareAnotherDownloadCommand, state == "completed"),
                        ("show-in-folder", item.OpenLocationCommand, state == "completed"),
                        ("remove", item.RemoveCommand, true)
                    };
                    foreach (var action in actions)
                    {
                        var button = FindButton(page, action.Command);
                        var visible = button.Visibility == Visibility.Visible;
                        report.Check($"{titleLength}/{state}/{action.Name}/visibility", visible == action.ExpectedVisible);
                        if (!action.ExpectedVisible) continue;
                        var bounds = Measure(button, page);
                        sample.Actions[action.Name] = bounds;
                        report.Check($"{titleLength}/{state}/{action.Name}/inside-window", Contains(pageBounds, bounds));
                        report.Check($"{titleLength}/{state}/{action.Name}/inside-queue", Contains(viewport, bounds));
                        report.Check($"{titleLength}/{state}/{action.Name}/nonzero-size", bounds.Width >= 30 && bounds.Height >= 30);
                        if (baselines.TryGetValue(action.Name, out var baseline))
                            report.Check($"{titleLength}/{state}/{action.Name}/fixed-position", SameBounds(baseline, bounds));
                        else baselines[action.Name] = bounds;
                    }
                    var footerButton = FindButton(page, state == "downloading" ? viewModel.CancelAllCommand : viewModel.DownloadAllCommand);
                    var footer = Measure(footerButton, page);
                    sample.FooterAction = footer;
                    report.Check($"{titleLength}/{state}/footer/visible", footerButton.Visibility == Visibility.Visible);
                    report.Check($"{titleLength}/{state}/footer/inside-window", Contains(pageBounds, footer));
                    if (footerBaseline is not null)
                        report.Check($"{titleLength}/{state}/footer/fixed-slot", SameBounds(footerBaseline, footer));
                    else footerBaseline = footer;
                    var folderButton = FindButton(page, viewModel.ChangeFolderCommand);
                    report.Check($"{titleLength}/{state}/change-folder/inside-window", Contains(pageBounds, Measure(folderButton, page)));
                    report.Check($"{titleLength}/{state}/no-horizontal-overflow", list.ActualWidth <= page.ActualWidth + Tolerance);
                }
            }

            viewModel.IsDownloadingAll = false;
            item.IsDownloading = false;
            item.IsQueueLocked = false;
            viewModel.DownloadQueue.Clear();
            await SettleAsync(page);
            var emptyStart = FindButton(page, viewModel.DownloadAllCommand);
            var emptyBounds = Measure(emptyStart, page);
            report.Check("empty-queue/start-stays-visible", emptyStart.Visibility == Visibility.Visible && !emptyStart.IsEnabled);
            report.Check("empty-queue/footer-fixed-slot", footerBaseline is not null && SameBounds(footerBaseline, emptyBounds));
            report.Check("no-download-service-calls", fakeService.Calls == 0);
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                report.Window = new WindowRules(presenter.IsResizable, presenter.IsMaximizable, presenter.IsMinimizable,
                    window.AppWindow.ClientSize.Width, window.AppWindow.ClientSize.Height);
                report.Check("window/cannot-resize", !presenter.IsResizable);
                report.Check("window/cannot-maximize", !presenter.IsMaximizable);
                report.Check("window/can-minimize", presenter.IsMinimizable);
            }
            else report.Check("window/overlapped-presenter", false);
        }
        catch (Exception exception)
        {
            report.Error = exception.ToString();
            report.Check("probe-completed-without-exception", false);
        }
        finally
        {
            if (viewModel is not null)
            {
                viewModel.IsDownloadingAll = false;
                viewModel.IsCancelling = false;
                foreach (var item in viewModel.DownloadQueue) { item.IsDownloading = false; item.IsQueueLocked = false; }
                viewModel.DownloadQueue.Clear();
                foreach (var item in originalQueue) viewModel.DownloadQueue.Add(item);
                if (originalFolder is not null) viewModel.DownloadFolder = originalFolder;
                viewModel.IsDownloadingAll = originalDownloading;
                viewModel.IsCancelling = originalCancelling;
            }
            report.FinishedAt = DateTimeOffset.Now;
            try
            {
                Directory.CreateDirectory(outputDirectory);
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, "layout-regression.json"),
                    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { window.Close(); }
        }
    }

    private static async Task SettleAsync(FrameworkElement element)
    {
        element.UpdateLayout();
        await Task.Delay(300);
        element.UpdateLayout();
    }

    private static Button FindButton(DependencyObject root, ICommand command) =>
        Descendants<Button>(root).SingleOrDefault(button => ReferenceEquals(button.Command, command))
        ?? throw new InvalidOperationException("An expected action button was not realized in the visual tree.");

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T matching) yield return matching;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Bounds Measure(FrameworkElement element, FrameworkElement reference)
    {
        var rectangle = element.TransformToVisual(reference).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return new Bounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private static bool Contains(Bounds outer, Bounds inner) =>
        inner.X >= outer.X - Tolerance && inner.Y >= outer.Y - Tolerance &&
        inner.X + inner.Width <= outer.X + outer.Width + Tolerance &&
        inner.Y + inner.Height <= outer.Y + outer.Height + Tolerance;

    private static bool SameBounds(Bounds expected, Bounds actual) =>
        Math.Abs(expected.X - actual.X) <= Tolerance && Math.Abs(expected.Y - actual.Y) <= Tolerance &&
        Math.Abs(expected.Width - actual.Width) <= Tolerance && Math.Abs(expected.Height - actual.Height) <= Tolerance;

    private sealed class NoIoDownloadService : IDownloadService
    {
        public int Calls { get; private set; }
        public Task<VideoMetadata?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Network access is not allowed in the layout probe.");
        }
        public Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioFormat, string audioBitrate,
            string videoExt, string downloadDir, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default,
            bool overwriteExistingFiles = false, string? titleHint = null)
        {
            Calls++;
            throw new InvalidOperationException("Downloads are not allowed in the layout probe.");
        }
    }

    private sealed class SyntheticMetadataService : IDownloadService
    {
        public int Calls { get; private set; }
        public Task<VideoMetadata?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<VideoMetadata?>(new VideoMetadata
            {
                Id = "synthetic", Title = "Vídeo de teste", Thumbnail = string.Empty,
                Formats = new List<VideoFormat> { new() { FormatId = "test", Height = 1080, Resolution = "1920x1080", VideoCodec = "h264", AudioCodec = "aac" } }
            });
        }
        public Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioFormat, string audioBitrate,
            string videoExt, string downloadDir, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default,
            bool overwriteExistingFiles = false, string? titleHint = null) =>
            throw new InvalidOperationException("Downloads are not allowed in the Add command check.");
    }

    private sealed class ProbeReport
    {
        public bool Passed => Error is null && Checks.Count > 0 && Checks.All(check => check.Passed);
        public DateTimeOffset FinishedAt { get; set; }
        public string? Error { get; set; }
        public WindowRules? Window { get; set; }
        public List<ProbeSample> Samples { get; } = new();
        public List<ProbeCheck> Checks { get; } = new();
        public void Check(string name, bool passed) => Checks.Add(new ProbeCheck(name, passed));
    }
    private sealed class ProbeSample
    {
        public string State { get; init; } = string.Empty;
        public int TitleLength { get; init; }
        public Bounds? Page { get; init; }
        public Bounds? QueueViewport { get; init; }
        public Dictionary<string, Bounds> Actions { get; } = new();
        public Bounds? FooterAction { get; set; }
    }
    private sealed record ProbeCheck(string Name, bool Passed);
    private sealed record Bounds(double X, double Y, double Width, double Height);
    private sealed record WindowRules(bool CanResize, bool CanMaximize, bool CanMinimize, int ClientWidth, int ClientHeight);
}
#endif
