using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using GetIt_App.ViewModels;
using GetIt_App.Services;

namespace GetIt_App;

public sealed partial class MainPage : Page
{
    public static ElementTheme CurrentTheme { get; set; } = ElementTheme.Default;
    private static bool _mainWindowReadyNotified;

    // Loaded settings (kept in memory so we can update only the Theme field on toggle)
    private AppSettings _settings = new();
    private bool _initialDependencyPromptShown;
    private bool _dependencySetupBusy;

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        ViewModel.PlaylistSelectionRequested = ShowPlaylistSelectionAsync;
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
            RequestedTheme = CurrentTheme,
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

    private async void ConfigureDependencies_Click(object sender, RoutedEventArgs e)
    {
        await ShowDependencySetupPromptAsync();
    }

    private async Task ShowDependencySetupPromptAsync(bool showAlreadyConfiguredMessage = true)
    {
        if (_dependencySetupBusy) return;

        await ViewModel.RefreshEngineHealthAsync();
        if (string.IsNullOrWhiteSpace(ViewModel.EngineStatusMessage))
        {
            if (showAlreadyConfiguredMessage)
            {
                DependencySetupInfoBar.Severity = InfoBarSeverity.Success;
                DependencySetupInfoBar.Message = "FFmpeg, FFprobe e Deno já estão configurados.";
                DependencySetupInfoBar.IsOpen = true;
            }
            return;
        }

        var details = new StackPanel { Spacing = 10, MinWidth = 420 };
        details.Children.Add(new TextBlock
        {
            Text = "O GetIt pode baixar as dependências ausentes diretamente dos releases oficiais e validá-las antes de usá-las. Os arquivos ficam em %LOCALAPPDATA%\\GetIt\\Tools; não são incluídos no instalador nem hospedados por servidores do GetIt.",
            TextWrapping = TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = $"Verificação atual:{Environment.NewLine}{ViewModel.EngineStatusMessage}",
            TextWrapping = TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = "FFmpeg + FFprobe · BtbN FFmpeg 8.1.3 · licença GPLv3. Inclui libx264 para manter a conversão H.264 por CPU. SHA-256 do ZIP: 401dffcf14c301082e1fc03b0bc876b92ea676bfc521fc56e80a1537d137034f. O ZIP só será extraído após a verificação do hash.",
            TextWrapping = TextWrapping.Wrap
        });
        details.Children.Add(new HyperlinkButton
        {
            Content = "Release BtbN e informações de build/licença",
            NavigateUri = new Uri("https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-10-31-12-59"),
            HorizontalAlignment = HorizontalAlignment.Left
        });
        details.Children.Add(new TextBlock
        {
            Text = "Deno 2.7.1 · release oficial Deno · licença MIT declarada pelo projeto. SHA-256 oficial do ZIP: 94d71d4772436de27a0495933ca4bab7b6895992622b65baeaf4b7995dae1e69. A release não fornece um inventário completo separado das licenças de todas as dependências embutidas; isso não é apresentado como isenção ou aprovação jurídica.",
            TextWrapping = TextWrapping.Wrap
        });
        details.Children.Add(new HyperlinkButton
        {
            Content = "Release Deno 2.7.1 e checksum oficial",
            NavigateUri = new Uri("https://github.com/denoland/deno/releases/tag/v2.7.1"),
            HorizontalAlignment = HorizontalAlignment.Left
        });
        details.Children.Add(new TextBlock
        {
            Text = "Os scripts EJS acompanham o executável oficial PyInstaller do yt-dlp e usam Unlicense; não serão baixados separadamente.",
            TextWrapping = TextWrapping.Wrap
        });
        details.Children.Add(new HyperlinkButton
        {
            Content = "Documentação EJS e licença",
            NavigateUri = new Uri("https://github.com/yt-dlp/yt-dlp/wiki/EJS"),
            HorizontalAlignment = HorizontalAlignment.Left
        });
        details.Children.Add(new TextBlock
        {
            Text = "O download direto do fornecedor não é tratado como isenção automática. Para o asset BtbN fixo, os registros públicos examinados não incluem um pacote completo de fontes correspondentes e manifesto de dependências daquele build. O GetIt não hospeda nem inclui esses executáveis no instalador, mas não certifica a regularidade jurídica das condições de aquisição. Consulte as licenças e os materiais de origem antes de redistribuir o aplicativo.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            Title = "Configuração inicial das dependências",
            Content = new ScrollViewer
            {
                Content = details,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "Configurar automaticamente",
            SecondaryButtonText = "Configurar manualmente",
            CloseButtonText = "Agora não",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = CurrentTheme,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ConfigureDependenciesAsync();
        }
        else if (result == ContentDialogResult.Secondary && App.Window is MainWindow mainWindow)
        {
            mainWindow.NavigateToSettings();
        }
    }

    private async Task ConfigureDependenciesAsync()
    {
        if (_dependencySetupBusy) return;
        _dependencySetupBusy = true;
        ConfigureDependenciesButton.IsEnabled = false;
        DependencySetupInfoBar.IsOpen = false;

        using var cancellation = new CancellationTokenSource();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, IsIndeterminate = true };
        var progressContent = new StackPanel { Spacing = 12, MinWidth = 360 };
        progressContent.Children.Add(status);
        progressContent.Children.Add(progressBar);
        var progressDialog = new ContentDialog
        {
            Title = "Configurando dependências",
            Content = progressContent,
            CloseButtonText = "Cancelar",
            RequestedTheme = CurrentTheme,
            XamlRoot = XamlRoot
        };

        var progress = new Progress<DependencySetupProgress>(value =>
        {
            status.Text = value.Status;
            progressBar.IsIndeterminate = value.TotalBytes is not > 0;
            if (value.TotalBytes is > 0)
            {
                progressBar.Value = Math.Clamp(value.BytesReceived * 100d / value.TotalBytes.Value, 0, 100);
            }
        });

        try
        {
            _dependencySetupBusy = true;
            var setupTask = new ExternalDependencySetupService().ConfigureAsync(progress, cancellation.Token);
            var dialogTask = progressDialog.ShowAsync().AsTask();
            var completedTask = await Task.WhenAny(setupTask, dialogTask);
            if (completedTask == dialogTask && !setupTask.IsCompleted)
            {
                cancellation.Cancel();
            }
            else
            {
                progressDialog.Hide();
            }

            var setupResult = await setupTask;
            progressDialog.Hide();
            await dialogTask;
            await ViewModel.RefreshEngineHealthAsync();
            if (!string.IsNullOrWhiteSpace(ViewModel.EngineStatusMessage))
            {
                DependencySetupInfoBar.Severity = InfoBarSeverity.Warning;
                DependencySetupInfoBar.Message =
                    $"As ferramentas baixadas foram validadas, mas ainda há uma dependência indisponível: {ViewModel.EngineStatusMessage}";
                DependencySetupInfoBar.IsOpen = true;
                return;
            }

            DependencySetupInfoBar.Severity = InfoBarSeverity.Success;
            DependencySetupInfoBar.Message =
                $"Configuração concluída. FFmpeg/FFprobe: {setupResult.FfmpegDirectory}. Deno: {setupResult.DenoDirectory}.";
            DependencySetupInfoBar.IsOpen = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            DependencySetupInfoBar.Severity = InfoBarSeverity.Informational;
            DependencySetupInfoBar.Message = "Configuração cancelada. Nenhum caminho foi alterado; você pode tentar novamente.";
            DependencySetupInfoBar.IsOpen = true;
        }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Automatic dependency setup failed");
            DependencySetupInfoBar.Severity = InfoBarSeverity.Error;
            DependencySetupInfoBar.Message = $"Não foi possível configurar as dependências: {exception.Message} Você pode tentar novamente ou selecionar instalações existentes nas Configurações.";
            DependencySetupInfoBar.IsOpen = true;
        }
        finally
        {
            _dependencySetupBusy = false;
            ConfigureDependenciesButton.IsEnabled = true;
            await ViewModel.RefreshEngineHealthAsync();
        }
    }

    private async void AddPlaylist_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddPlaylistFromUrlAsync();
    }

    private async Task<PlaylistInputSelection> ShowPlaylistSelectionAsync(PlaylistPreview preview, bool isAmbiguous)
    {
        if (isAmbiguous)
        {
            var choiceDialog = new ContentDialog
            {
                Title = "Link de vídeo e playlist",
                Content = "Este endereço identifica um vídeo e também uma playlist. Escolha se deseja adicionar somente o vídeo ou revisar a playlist.",
                PrimaryButtonText = "Escolher vídeos da playlist",
                SecondaryButtonText = "Somente este vídeo",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = CurrentTheme,
                XamlRoot = XamlRoot
            };
            var choice = await choiceDialog.ShowAsync();
            if (choice == ContentDialogResult.Secondary)
                return new PlaylistInputSelection(PlaylistInputChoice.AddVideoOnly, Array.Empty<int>());
            if (choice != ContentDialogResult.Primary)
                return new PlaylistInputSelection(PlaylistInputChoice.Cancel, Array.Empty<int>());
        }

        if (preview.Videos.Count == 0)
        {
            var emptyDialog = new ContentDialog
            {
                Title = "Nenhum vídeo disponível",
                Content = "O yt-dlp não encontrou itens selecionáveis nesta playlist. Ela pode estar vazia, indisponível ou não ser compatível.",
                CloseButtonText = "Fechar",
                RequestedTheme = CurrentTheme,
                XamlRoot = XamlRoot
            };
            await emptyDialog.ShowAsync();
            return new PlaylistInputSelection(PlaylistInputChoice.Cancel, Array.Empty<int>());
        }

        var items = new ObservableCollection<PlaylistVideoSelectionViewModel>(
            preview.Videos.Select(video => new PlaylistVideoSelectionViewModel(video)));
        var selectedCount = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
        var note = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        if (preview.IsPartial)
        {
            note.Text = preview.TotalCount is { } total
                ? $"Exibindo os primeiros {items.Count} de {total} itens. Para manter a prévia rápida, selecione somente entre os itens exibidos."
                : $"O extrator retornou uma lista limitada a {items.Count} itens; ela pode não representar a playlist inteira.";
        }
        else
        {
            note.Text = preview.TotalCount is { } total
                ? $"{total} itens encontrados. Itens sem endereço individual seguro não podem ser selecionados."
                : $"{items.Count} itens exibidos. Itens sem endereço individual seguro não podem ser selecionados.";
        }

        var selectAllButton = new Button { Content = "Selecionar todos", MinHeight = 32 };
        var clearButton = new Button { Content = "Desmarcar todos", MinHeight = 32 };
        var list = new ListView
        {
            ItemsSource = items,
            SelectionMode = ListViewSelectionMode.None,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Grid ColumnSpacing="10" Padding="4">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="64" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}"
                                  IsEnabled="{Binding IsEligible}"
                                  AutomationProperties.Name="{Binding DisplayTitle}"
                                  VerticalAlignment="Center" />
                        <Border Grid.Column="1" Width="64" Height="40" CornerRadius="5"
                                Background="{ThemeResource AppInputBrush}">
                            <Image Source="{Binding ThumbnailImage}" Width="64" Height="40"
                                   Stretch="UniformToFill" VerticalAlignment="Center" />
                        </Border>
                        <StackPanel Grid.Column="2" Spacing="2" VerticalAlignment="Center">
                            <TextBlock Text="{Binding DisplayTitle}" TextWrapping="Wrap" MaxLines="2" />
                            <TextBlock Text="{Binding DetailsText}" FontSize="12"
                                       Foreground="{ThemeResource AppSecondaryTextBrush}" TextWrapping="Wrap" />
                        </StackPanel>
                    </Grid>
                </DataTemplate>
                """),
            MaxHeight = 420,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var content = new StackPanel { Spacing = 10, MinWidth = 380 };
        var heading = string.IsNullOrWhiteSpace(preview.Title) ? "Playlist" : preview.Title;
        if (!string.IsNullOrWhiteSpace(preview.Uploader))
            heading += $" · {preview.Uploader}";
        var headingGrid = new Grid { ColumnSpacing = 12 };
        headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (Uri.TryCreate(preview.Thumbnail, UriKind.Absolute, out var playlistThumbnail) &&
            (playlistThumbnail.Scheme == Uri.UriSchemeHttps || playlistThumbnail.Scheme == Uri.UriSchemeHttp))
        {
            var image = new Image
            {
                Source = new BitmapImage { UriSource = playlistThumbnail, DecodePixelWidth = 144 },
                Width = 72,
                Height = 54,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(image, "Miniatura da playlist");
            headingGrid.Children.Add(image);
        }

        var headingText = new TextBlock
        {
            Text = heading,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(headingText, headingGrid.Children.Count > 0 ? 1 : 0);
        headingGrid.Children.Add(headingText);
        content.Children.Add(headingGrid);
        content.Children.Add(note);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(selectAllButton);
        actions.Children.Add(clearButton);
        content.Children.Add(actions);
        content.Children.Add(selectedCount);
        content.Children.Add(list);

        var dialog = new ContentDialog
        {
            Title = "Selecionar vídeos",
            Content = content,
            PrimaryButtonText = "Adicionar selecionados",
            SecondaryButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = CurrentTheme,
            XamlRoot = XamlRoot
        };

        void UpdateSelectionState()
        {
            var count = items.Count(item => item.IsSelected && item.IsEligible);
            selectedCount.Text = $"{count} selecionados · limite de {BatchUrlPolicy.MaximumLinks} por adição";
            dialog.IsPrimaryButtonEnabled = count > 0;
        }

        foreach (var item in items)
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PlaylistVideoSelectionViewModel.IsSelected))
                    UpdateSelectionState();
            };
        selectAllButton.Click += (_, _) =>
        {
            foreach (var item in items.Where(item => item.IsEligible))
                item.IsSelected = true;
        };
        clearButton.Click += (_, _) =>
        {
            foreach (var item in items)
                item.IsSelected = false;
        };
        UpdateSelectionState();

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return new PlaylistInputSelection(PlaylistInputChoice.Cancel, Array.Empty<int>());

        return new PlaylistInputSelection(
            PlaylistInputChoice.SelectPlaylistVideos,
            items.Where(item => item.IsSelected && item.IsEligible).Select(item => item.Position).ToArray());
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

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // --- Theme resolution ---
        // Priority: saved user preference > Windows system theme
        _settings = SettingsService.LoadSettings();
        ViewModel.InputError = SettingsService.LastLoadWarning ?? string.Empty;

        string resolvedTheme = _settings.Theme ?? SettingsService.GetWindowsTheme();
        CurrentTheme = resolvedTheme == "Light" ? ElementTheme.Light : ElementTheme.Dark;

        ApplyTheme(CurrentTheme);
        await PromptRestoreQueueAsync();
        if (!_initialDependencyPromptShown)
        {
            _initialDependencyPromptShown = true;
            await ShowDependencySetupPromptAsync(showAlreadyConfiguredMessage: false);
#if DEBUG
            if (!LayoutRegressionProbe.IsEnabled)
#endif
                NotifyMainWindowReady();
        }
#if DEBUG
        if (!LayoutRegressionProbe.IsEnabled)
#endif
            ViewModel.ActivateClipboardMonitoring();
    }

    private static void NotifyMainWindowReady()
    {
        if (_mainWindowReadyNotified) return;
        _mainWindowReadyNotified = true;
        App.OnMainWindowReady();
    }

    private async Task PromptRestoreQueueAsync()
    {
        try
        {
            var savedQueue = await ViewModel.LoadSavedQueueAsync();
            if (savedQueue is null)
                return;

            var notice = savedQueue.Warning;
            if (savedQueue.WasRecovered)
                notice = string.IsNullOrWhiteSpace(notice)
                    ? "A fila foi recuperada de uma cópia local."
                    : $"{notice}\nA fila foi recuperada de uma cópia local.";

            var content = $"Há {savedQueue.Snapshot.Items.Count} link(s) pendente(s) salvos. Deseja restaurá-los? " +
                "Os downloads não serão iniciados automaticamente.";
            if (!string.IsNullOrWhiteSpace(notice))
                content += $"\n\n{notice}";

            var dialog = new ContentDialog
            {
                Title = "Deseja restaurar sua fila anterior?",
                Content = content,
                PrimaryButtonText = "Restaurar fila",
                SecondaryButtonText = "Descartar fila salva",
                CloseButtonText = "Agora não",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.RestoreSavedQueue(savedQueue.Snapshot);
                return;
            }

            if (result != ContentDialogResult.Secondary)
            {
                ViewModel.DeferSavedQueueRestoration();
                return;
            }

            var confirmation = new ContentDialog
            {
                Title = "Descartar a fila salva?",
                Content = "Somente os links e as preferências salvos da fila serão apagados. Downloads concluídos, arquivos e histórico não serão alterados.",
                PrimaryButtonText = "Descartar fila",
                CloseButtonText = "Manter fila salva",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DiscardSavedQueueAsync();
                ViewModel.InputError = "A fila salva foi descartada.";
            }
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Could not read the saved download queue");
            ViewModel.InputError = "A fila salva não pôde ser recuperada com segurança. O arquivo original foi preservado; verifique a pasta de dados do GetIt.";
        }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Could not restore the saved download queue");
            ViewModel.InputError = "Não foi possível restaurar a fila salva. Os arquivos existentes foram preservados.";
        }
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
