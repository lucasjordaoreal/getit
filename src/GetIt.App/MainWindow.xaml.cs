using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace GetIt_App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Select Home by default
        NavView.SelectedItem = NavView.MenuItems[0];
        RootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
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
