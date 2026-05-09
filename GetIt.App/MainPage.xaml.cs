using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using GetIt_App.ViewModels;
using GetIt_App.Services;

namespace GetIt_App;

public sealed partial class MainPage : Page
{
    public static ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    // Guard to prevent saving when we programmatically set the toggle during init
    private bool _applyingTheme;

    // Loaded settings (kept in memory so we can update only the Theme field on toggle)
    private AppSettings _settings = new();

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
    }

    private void Button_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            
            visual.CenterPoint = new Vector3((float)element.ActualSize.X / 2, (float)element.ActualSize.Y / 2, 0);

            var anim = compositor.CreateVector3KeyFrameAnimation();
            anim.InsertKeyFrame(1.0f, new Vector3(1.04f, 1.04f, 1.0f));
            anim.Duration = TimeSpan.FromMilliseconds(150);
            
            visual.StartAnimation("Scale", anim);
        }
    }

    private void Button_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            
            visual.CenterPoint = new Vector3((float)element.ActualSize.X / 2, (float)element.ActualSize.Y / 2, 0);

            var anim = compositor.CreateVector3KeyFrameAnimation();
            anim.InsertKeyFrame(1.0f, new Vector3(1.0f, 1.0f, 1.0f));
            anim.Duration = TimeSpan.FromMilliseconds(150);
            
            visual.StartAnimation("Scale", anim);
        }
    }

    private HistoryWindow? _historyWindow;

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_historyWindow == null)
        {
            _historyWindow = new HistoryWindow();
            _historyWindow.Closed += (s, args) => _historyWindow = null;
        }
        _historyWindow.Activate();
    }

    private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // Skip when we are setting the toggle programmatically
        if (_applyingTheme) return;

        if (sender is ToggleSwitch toggleSwitch)
        {
            CurrentTheme = toggleSwitch.IsOn ? ElementTheme.Light : ElementTheme.Dark;
            ApplyTheme(CurrentTheme);

            // Persist user preference
            _settings.Theme = toggleSwitch.IsOn ? "Light" : "Dark";
            SettingsService.SaveSettings(_settings);
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // --- Theme resolution ---
        // Priority: saved user preference > Windows system theme
        _settings = SettingsService.LoadSettings();

        string resolvedTheme = _settings.Theme ?? SettingsService.GetWindowsTheme();
        CurrentTheme = resolvedTheme == "Light" ? ElementTheme.Light : ElementTheme.Dark;

        // Set toggle position without triggering the Toggled event
        _applyingTheme = true;
        ThemeToggleSwitch.IsOn = CurrentTheme == ElementTheme.Light;
        _applyingTheme = false;

        ApplyTheme(CurrentTheme);

        // --- Update check ---
        await PerformUpdateCheck(silent: true);
    }

    /// <summary>Applies a theme to this page and the open history window (if any).</summary>
    private void ApplyTheme(ElementTheme theme)
    {
        RequestedTheme = theme;
        if (_historyWindow?.Content is FrameworkElement root)
            root.RequestedTheme = theme;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        await PerformUpdateCheck(silent: false);
    }

    private async Task PerformUpdateCheck(bool silent)
    {
        BtnUpdate.IsEnabled = false;
        var originalText = BtnUpdate.Content;
        BtnUpdate.Content = "Buscando...";

        var update = await UpdateService.CheckForUpdatesAsync();

        BtnUpdate.Content = originalText;
        BtnUpdate.IsEnabled = true;

        if (update != null)
        {
            var dialog = new ContentDialog
            {
                Title = "Nova atualização disponível!",
                XamlRoot = this.XamlRoot,
                RequestedTheme = CurrentTheme
            };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock { Text = $"A versão {update.TagName} está disponível para download. Deseja atualizar agora?", TextWrapping = TextWrapping.Wrap });

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            
            var result = ContentDialogResult.None;
            var btnYes = new Button { Content = "Sim, atualizar" };
            btnYes.Click += (s, ev) => { result = ContentDialogResult.Primary; dialog.Hide(); };
            
            var btnNo = new Button { Content = "Agora não" };
            btnNo.Click += (s, ev) => { result = ContentDialogResult.None; dialog.Hide(); };
            
            btnPanel.Children.Add(btnYes);
            btnPanel.Children.Add(btnNo);
            stack.Children.Add(btnPanel);
            dialog.Content = stack;

            await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var asset = update.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                if (asset != null)
                {
                    var progressDialog = new ContentDialog
                    {
                        Title = "Baixando atualização...",
                        Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 20, 0, 0) },
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = CurrentTheme
                    };
                    _ = progressDialog.ShowAsync();

                    try
                    {
                        await UpdateService.DownloadAndInstallUpdateAsync(asset.BrowserDownloadUrl);
                    }
                    catch (Exception ex)
                    {
                        progressDialog.Hide();
                        var errDialog = new ContentDialog
                        {
                            Title = "Erro ao atualizar",
                            XamlRoot = this.XamlRoot,
                            RequestedTheme = CurrentTheme
                        };
                        var errStack = new StackPanel { Spacing = 12 };
                        errStack.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap });
                        var errBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
                        var errBtnOk = new Button { Content = "Ok" };
                        errBtnOk.Click += (s, ev) => errDialog.Hide();
                        errBtnPanel.Children.Add(errBtnOk);
                        errStack.Children.Add(errBtnPanel);
                        errDialog.Content = errStack;
                        await errDialog.ShowAsync();
                    }
                }
                else
                {
                    var errDialog = new ContentDialog
                    {
                        Title = "Erro",
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = CurrentTheme
                    };
                    var errStack = new StackPanel { Spacing = 12 };
                    errStack.Children.Add(new TextBlock { Text = "Nenhum arquivo .zip encontrado na release.", TextWrapping = TextWrapping.Wrap });
                    var errBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
                    var errBtnOk = new Button { Content = "Ok" };
                    errBtnOk.Click += (s, ev) => errDialog.Hide();
                    errBtnPanel.Children.Add(errBtnOk);
                    errStack.Children.Add(errBtnPanel);
                    errDialog.Content = errStack;
                    await errDialog.ShowAsync();
                }
            }
        }
        else if (!silent)
        {
            var dialog = new ContentDialog
            {
                Title = "Tudo certo!",
                XamlRoot = this.XamlRoot,
                RequestedTheme = CurrentTheme
            };
            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock { Text = "Você já está usando a última versão disponível.", TextWrapping = TextWrapping.Wrap });
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            var btnOk = new Button { Content = "Ok" };
            btnOk.Click += (s, ev) => dialog.Hide();
            btnPanel.Children.Add(btnOk);
            stack.Children.Add(btnPanel);
            dialog.Content = stack;
            await dialog.ShowAsync();
        }
    }
}

