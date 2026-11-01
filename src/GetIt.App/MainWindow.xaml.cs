using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace GetIt_App;

public sealed partial class MainWindow : Window
{
    private const int PreferredWindowWidth = 1200;
    private const int PreferredWindowHeight = 800;
    private const int MinimumWindowWidth = 800;
    private const int MinimumWindowHeight = 560;
    private bool _navigationInitialized;
    private uint _configuredDpi;
    private MainPage? _downloadPage;

    public MainWindow()
    {
        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        ConfigureWindow();
        AppWindow.Changed += AppWindow_Changed;
        RootFrame.Navigated += (_, args) =>
        {
            if (args.Content is MainPage page) _downloadPage = page;
        };
        Closed += async (_, _) =>
        {
            AppWindow.Changed -= AppWindow_Changed;
            if (_downloadPage is not null)
                await _downloadPage.ViewModel.ShutdownAsync();
        };
    }

    private void ConfigureWindow()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;

        presenter.IsResizable = true;
        presenter.IsMaximizable = true;
        presenter.IsMinimizable = true;

        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = dpi / 96d;
        _configuredDpi = dpi;
        SetPreferredMinimumSize(presenter, scale);
        var minimumWidth = (int)Math.Round(MinimumWindowWidth * scale);
        var minimumHeight = (int)Math.Round(MinimumWindowHeight * scale);

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var margin = (int)Math.Ceiling(32 * scale);
        var width = Math.Min((int)Math.Round(PreferredWindowWidth * scale), Math.Max(minimumWidth, workArea.Width - margin));
        var height = Math.Min((int)Math.Round(PreferredWindowHeight * scale), Math.Max(minimumHeight, workArea.Height - margin));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        var size = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2)));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (dpi == _configuredDpi || sender.Presenter is not OverlappedPresenter presenter)
            return;

        _configuredDpi = dpi;
        SetPreferredMinimumSize(presenter, dpi / 96d);
    }

    private static void SetPreferredMinimumSize(OverlappedPresenter presenter, double scale)
    {
        // The presenter uses physical pixels; convert the minimum XAML size from DIPs.
        presenter.PreferredMinimumWidth = (int)Math.Round(MinimumWindowWidth * scale);
        presenter.PreferredMinimumHeight = (int)Math.Round(MinimumWindowHeight * scale);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_navigationInitialized) return;
        _navigationInitialized = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        if (RootFrame.CurrentSourcePageType != typeof(MainPage))
            RootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
#if DEBUG
        if (LayoutRegressionProbe.IsEnabled)
        {
            _ = LayoutRegressionProbe.RunAsync(this, RootFrame);
            return;
        }
#endif
        App.OnMainWindowReady();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type pageType;

        if (args.IsSettingsSelected)
        {
            pageType = typeof(SettingsPage);
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            pageType = tag switch
            {
                "Home" => typeof(MainPage),
                "History" => typeof(HistoryPage),
                _ => typeof(MainPage)
            };
        }
        else
        {
            pageType = typeof(MainPage);
        }

        if (RootFrame.CurrentSourcePageType != pageType)
        {
            RootFrame.Navigate(pageType, null, args.RecommendedNavigationTransitionInfo);
        }
    }
}
