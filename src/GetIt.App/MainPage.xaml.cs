using System;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using GetIt_App.ViewModels;
using GetIt_App.Services;

namespace GetIt_App;

public sealed partial class MainPage : Page
{
    public static ElementTheme CurrentTheme { get; set; } = ElementTheme.Default;

    // Loaded settings (kept in memory so we can update only the Theme field on toggle)
    private AppSettings _settings = new();

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.DeactivateClipboardMonitoring();

        var focusLink = new KeyboardAccelerator { Key = VirtualKey.L, Modifiers = VirtualKeyModifiers.Control };
        focusLink.Invoked += (_, args) =>
        {
            UrlTextBox.Focus(FocusState.Keyboard);
            UrlTextBox.SelectAll();
            args.Handled = true;
        };
        KeyboardAccelerators.Add(focusLink);

        var startQueue = new KeyboardAccelerator { Key = VirtualKey.Enter, Modifiers = VirtualKeyModifiers.Control };
        startQueue.Invoked += (_, args) =>
        {
            if (ViewModel.CanStartDownloads) ViewModel.DownloadAllCommand.Execute(null);
            args.Handled = true;
        };
        KeyboardAccelerators.Add(startQueue);
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        var controlDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (controlDown)
        {
            if (ViewModel.CanStartDownloads) ViewModel.DownloadAllCommand.Execute(null);
        }
        else if (ViewModel.CanAddUrl && ViewModel.AddUrlCommand.CanExecute(null))
        {
            ViewModel.AddUrlCommand.Execute(null);
        }
    }

    private async void AddMultipleLinks_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsNotDownloading) return;

        var linkInput = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 150,
            MaxHeight = 260,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "Cole um link por linha"
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(linkInput, "Links para adicionar");
        ScrollViewer.SetVerticalScrollBarVisibility(linkInput, ScrollBarVisibility.Auto);
        var summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };
        var details = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 340 };
        content.Children.Add(new TextBlock
        {
            Text = "Cada linha será revisada antes de entrar na fila. Nenhum download começa automaticamente.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(linkInput);
        content.Children.Add(summary);
        content.Children.Add(new ScrollViewer
        {
            Content = details,
            MaxHeight = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        var dialog = new ContentDialog
        {
            Title = "Adicionar vários links",
            Content = content,
            PrimaryButtonText = "Adicionar links válidos",
            SecondaryButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        void UpdatePreview()
        {
            var preview = ViewModel.PreviewBatchUrls(linkInput.Text);
            summary.Text = $"Links encontrados: {preview.NonEmptyLineCount}\n" +
                $"{preview.ReadyCount} válidos · {preview.DuplicateInBatchCount + preview.AlreadyInQueueCount} duplicados · " +
                $"{preview.InvalidCount} inválidos";
            if (preview.IsOverLimit)
                summary.Text += $"\nO limite é {BatchUrlPolicy.MaximumLinks} links. A classificação abaixo é parcial; divida o lote para continuar.";

            var detailText = new StringBuilder();
            foreach (var entry in preview.Entries)
            {
                var description = entry.Status switch
                {
                    BatchUrlStatus.Ready => "Será adicionado",
                    BatchUrlStatus.DuplicateInBatch => "Duplicado neste lote; será ignorado",
                    BatchUrlStatus.AlreadyInQueue => "Já está na fila; será ignorado",
                    _ => "URL inválida"
                };
                var value = entry.NormalizedUrl ?? entry.Input;
                if (value.Length > 180) value = value[..177] + "...";
                detailText.Append("Linha ").Append(entry.LineNumber).Append(" · ")
                    .Append(description).Append(": ").AppendLine(value);
            }

            if (preview.IsOverLimit)
                detailText.Append("A prévia mostra somente os primeiros ")
                    .Append(BatchUrlPolicy.MaximumLinks).AppendLine(" links. Reduza a entrada para revisar o lote.");
            details.Text = detailText.ToString();
            dialog.IsPrimaryButtonEnabled = preview.ReadyCount > 0 && !preview.IsOverLimit;
        }

        linkInput.TextChanged += (_, _) => UpdatePreview();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (ViewModel.AddBatchUrls(linkInput.Text) == 0)
            {
                args.Cancel = true;
                summary.Text = ViewModel.InputError;
                UpdatePreview();
            }
        };
        UpdatePreview();

        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "Failed to show the multiple-links dialog");
            ViewModel.InputError = "Não foi possível abrir a inclusão de vários links. Tente novamente.";
        }
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsNotDownloading) return;
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                var copiedText = await content.GetTextAsync();
                if (!ViewModel.IsNotDownloading) return;
                ViewModel.Url = copiedText.Trim();
                UrlTextBox.Focus(FocusState.Programmatic);
            }
            else ViewModel.InputError = "Copie um link de vídeo ou áudio para colar aqui.";
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "Failed to paste a link");
            ViewModel.InputError = "Não foi possível colar. Tente novamente ou use Ctrl+V no campo do link.";
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // --- Theme resolution ---
        // Priority: saved user preference > Windows system theme
        _settings = SettingsService.LoadSettings();
        ViewModel.InputError = SettingsService.LastLoadWarning ?? string.Empty;

        string resolvedTheme = _settings.Theme ?? SettingsService.GetWindowsTheme();
        CurrentTheme = resolvedTheme == "Light" ? ElementTheme.Light : ElementTheme.Dark;

        ApplyTheme(CurrentTheme);
#if DEBUG
        if (!LayoutRegressionProbe.IsEnabled)
#endif
            ViewModel.ActivateClipboardMonitoring();
    }

    /// <summary>Applies a theme to this page.</summary>
    private void ApplyTheme(ElementTheme theme)
    {
        if (XamlRoot?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
        }

        RequestedTheme = theme;
    }
}
