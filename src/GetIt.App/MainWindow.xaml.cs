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
    private bool _navigationInitialized;
    private bool _restoringWindow;
    private Windows.Graphics.SizeInt32 _fixedWindowSize;
    private uint _configuredDpi;
    private Windows.Graphics.RectInt32 _configuredWorkArea;
    private MainPage? _downloadPage;

    public MainWindow()
    {
        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        ConfigureFixedWindow();
        AppWindow.Changed += AppWindow_Changed;
        RootFrame.Navigated += (_, args) =>
        {
            if (args.Content is MainPage page) _downloadPage = page;
        };
        Closed += (_, _) =>
        {
            AppWindow.Changed -= AppWindow_Changed;
            _downloadPage?.ViewModel.Shutdown();
        };
    }

    private void ConfigureFixedWindow(bool center = true)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;

        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = true;

        _configuredDpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = Math.Max(1d, _configuredDpi / 96d);
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        _configuredWorkArea = workArea;
        var margin = (int)Math.Ceiling(24 * scale);
        _fixedWindowSize = new Windows.Graphics.SizeInt32(
            Math.Min((int)Math.Round(PreferredWindowWidth * scale), Math.Max(1, workArea.Width - margin)),
            Math.Min((int)Math.Round(PreferredWindowHeight * scale), Math.Max(1, workArea.Height - margin)));
        AppWindow.Resize(_fixedWindowSize);
        var size = AppWindow.Size;
        if (center)
            AppWindow.Move(new Windows.Graphics.PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2)));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_restoringWindow || sender.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Minimized) return;
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var area = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Nearest).WorkArea;
        if (dpi != _configuredDpi || area.X != _configuredWorkArea.X || area.Y != _configuredWorkArea.Y
            || area.Width != _configuredWorkArea.Width || area.Height != _configuredWorkArea.Height)
        {
            // Keep the same logical size when Windows moves the app to a different display scale.
            _restoringWindow = true;
            try { ConfigureFixedWindow(center: false); }
            finally { _restoringWindow = false; }
            return;
        }
        // Also cover title-bar double-click and OS shortcuts on older Windows App SDK builds.
        var maximized = presenter.State == OverlappedPresenterState.Maximized;
        var changedSize = args.DidSizeChange && presenter.State == OverlappedPresenterState.Restored
            && (sender.Size.Width != _fixedWindowSize.Width || sender.Size.Height != _fixedWindowSize.Height);
        if (maximized || changedSize)
        {
            _restoringWindow = true;
            try
            {
                if (maximized) presenter.Restore();
                sender.Resize(_fixedWindowSize);
            }
            finally { _restoringWindow = false; }
        }
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
