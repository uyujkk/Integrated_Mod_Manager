using IntegratedModManager.Core.Layout;
using IntegratedModManager.EndfieldLaunch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Endfield launch settings: integrated launch or the XXMI launcher, paths, Mods, account-risk consent and the
/// launch diagnostics (moved from the window in stage J8). Saving the launch profile happens in the dialog, as before;
/// the window writes the chosen settings to the repository.
/// </summary>
internal static class EndfieldLaunchSettingsDialog
{
    private const string SourceUrl = "https://github.com/uyujkk/Integrated_Mod_Manager";

    /// <summary>What the dialog uses from the window.</summary>
    public sealed record Host(Func<string, Func<Task>, Task> RunUiAction, Func<string[], Task<string?>> PickFile, Action<string> OpenTextDocument,
        Func<string, Task> OpenUrl, Action<string, Exception> ReportFailure);

    /// <summary>The settings to save to the repository.</summary>
    public sealed record Settings(bool UseIntegrated, string GamePath, string ParentPath, string Arguments, bool UseMods, string LauncherPath);

    /// <summary>The chosen settings, or null when cancelled.</summary>
    /// <param name="migrated">The old bridge settings were just imported; the dialog says so.</param>
    public static async Task<Settings?> ShowAsync(AppServices services, Host host, WorkspaceRepository repository, EndfieldLaunchService service, bool migrated)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        bool english = !services.Language.IsChinese;
        DialogViewport viewport = services.Dialogs.Viewport;

        var gameBox = PathBox(repository.EndfieldGamePath, T("EndfieldBridge.SelectEndfieldExeInTheGame"));
        var parentBox = PathBox(repository.EndfieldParentPath, T("EndfieldBridge.LeaveEmptyToFindAndVerify"));
        var loaderBox = PathBox(EndfieldBridgeService.ResolveLoader(repository.LauncherPath), T("EndfieldBridge.XXMILauncherExe"));
        var argumentsBox = PathBox(repository.EndfieldGameArguments, "-force-d3d11");
        // Switch captions stay short (ToggleSwitch content cannot wrap); the explanation wraps below it.
        var modsSwitch = new ToggleSwitch
        {
            Header = T("EndfieldBridge.LoadModsThroughXXMIEFMI"),
            OnContent = T("Common.On"), OffContent = T("Common.Off"),
            IsOn = repository.EndfieldUseMods
        };
        var modsHint = HintText();
        var integrated = new ToggleSwitch
        {
            Header = T("EndfieldBridge.LaunchMethod"),
            OnContent = T("EndfieldBridge.IntegratedEndfieldLaunch"),
            OffContent = T("EndfieldBridge.ExistingLoader"),
            IsOn = repository.UseEndfieldBridge
        };
        var launchHint = HintText();
        var output = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12 };
        // Results sit below the scrolling settings, so showing them never pushes the form around.
        var outputViewer = new ScrollViewer
        {
            Content = output, MaxHeight = 140,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled
        };
        var outputClose = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(8, 4, 8, 4),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"], VerticalAlignment = VerticalAlignment.Top
        };
        ToolTipService.SetToolTip(outputClose, T("EndfieldBridge.CloseResult"));
        var outputGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        outputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outputGrid.Children.Add(new TextBlock { Text = T("EndfieldBridge.Result"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(outputClose, 1);
        outputGrid.Children.Add(outputClose);
        Grid.SetRow(outputViewer, 1);
        Grid.SetColumnSpan(outputViewer, 2);
        outputGrid.Children.Add(outputViewer);
        var outputBorder = new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Padding = new Thickness(12, 8, 8, 10), Child = outputGrid, Visibility = Visibility.Collapsed };
        outputClose.Click += (_, _) => outputBorder.Visibility = Visibility.Collapsed;
        var consent = new CheckBox
        {
            Content = new TextBlock { Text = T("EndfieldBridge.IUnderstandTheAdditionalAccountRisk"), TextWrapping = TextWrapping.Wrap },
            IsChecked = repository.EndfieldRiskAccepted
        };

        LaunchProfile CurrentProfile() => new()
        {
            Game = gameBox.Text.Trim(), Parent = parentBox.Text.Trim(), Xxmi = loaderBox.Text.Trim(),
            GameArguments = argumentsBox.Text.Trim(), WithMods = modsSwitch.IsOn,
            Language = english ? "en-US" : "zh-CN"
        };
        // Text from the language files, as a launch message in the current language.
        LaunchText Here(string text) => new(text, text);

        void Show(LaunchText text, bool failure = false)
        {
            output.Text = (failure ? T("EndfieldBridge.NotCompleted") : string.Empty) + text.For(english);
            outputBorder.Visibility = Visibility.Visible;
        }

        Button ActionButton(string text, Func<Task> action)
        {
            var button = new Button
            {
                // Wrapping label: long English labels must not be clipped in narrow columns.
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
            };
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await action();
                }
                catch (EndfieldLaunchException ex)
                {
                    Show(ex.Text, failure: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                    or InvalidOperationException or ArgumentException)
                {
                    Show(LaunchText.Both(ex.Message), failure: true);
                }
                catch (Exception ex)
                {
                    host.ReportFailure("Endfield launch settings: " + text, ex);
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
            return button;
        }

        // ContentDialog sizes to its content, so a width is needed for the larger max width to apply.
        var body = new StackPanel { Spacing = 14, Width = Math.Max(320, Math.Min(760, viewport.MaxWidth - 72)) };
        body.Children.Add(new TextBlock
        {
            // Settings imported from a standalone EndfieldBridge (4.1.1–4.2.0) need EFMI set up again.
            Text = T("EndfieldBridge.Repository", repository.Name) + (migrated
                ? "\n" + T("EndfieldBridge.LegacySettingsImported")
                : string.Empty),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });
        body.Children.Add(new InfoBar
        {
            Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = false,
            Title = T("EndfieldBridge.AccountRisk"), Message = T("EndfieldBridge.AccountRiskTheIntegratedEndfieldLaunch")
        });

        // The risk confirmation gates Save, so it sits right under the switch it belongs to.
        var launch = new StackPanel { Spacing = 6 };
        launch.Children.Add(integrated);
        launch.Children.Add(launchHint);
        launch.Children.Add(consent);
        body.Children.Add(launch);

        var paths = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        paths.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        paths.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        paths.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddPathRow(paths, 0, T("EndfieldBridge.GameExecutable"), gameBox, ".exe");
        AddPathRow(paths, 1, T("EndfieldBridge.OfficialLauncherOptional"), parentBox, ".exe");
        AddPathRow(paths, 2, T("EndfieldBridge.XXMILauncher"), loaderBox, ".exe");
        AddPathRow(paths, 3, T("EndfieldBridge.GameArguments"), argumentsBox, null);
        var settings = new StackPanel { Spacing = 10 };
        settings.Children.Add(SectionTitle(T("EndfieldBridge.GameAndLoader")));
        settings.Children.Add(paths);
        settings.Children.Add(modsSwitch);
        settings.Children.Add(modsHint);
        body.Children.Add(new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Padding = new Thickness(14, 12, 14, 14), Child = settings });

        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(SectionTitle(T("EndfieldBridge.ChecksAndEFMISetup")));
        var actionRow = new ButtonGrid(3);
        actionRow.Add(ActionButton(T("EndfieldBridge.Check"), async () =>
        {
            LaunchProfile profile = CurrentProfile();
            Show(await Task.Run(() => service.Check(profile)));
        }));
        actionRow.Add(ActionButton(T("EndfieldBridge.ImportFromEFMI"), () =>
        {
            LaunchProfile profile = CurrentProfile();
            LaunchText note = service.Integration.Import(profile);
            gameBox.Text = profile.Game;
            argumentsBox.Text = profile.GameArguments;
            Show(note + new LaunchText("\n", "\n") + service.Integration.Inspect(profile));
            return Task.CompletedTask;
        }));
        actionRow.Add(ActionButton(T("EndfieldBridge.BackUpAndSetUpEFMI"), () =>
        {
            LaunchProfile profile = CurrentProfile();
            service.Integration.Import(profile);
            gameBox.Text = profile.Game;
            argumentsBox.Text = profile.GameArguments;
            if (!integrated.IsOn || consent.IsChecked != true)
            {
                Show(Here(T("EndfieldBridge.EnableIntegratedLaunchFirst")), failure: true);
                return Task.CompletedTask;
            }

            string? backup = service.ConfigureXxmi(profile);
            modsSwitch.IsOn = true;
            if (backup is null)
            {
                Show(Here(T("EndfieldBridge.SetupCompleteRestorePointKept")) + service.Integration.AuthorizationStatus(profile));
                return Task.CompletedTask;
            }

            Show(Here(T("EndfieldBridge.SetUpWithBackup", backup)) + service.Integration.Inspect(profile));
            return Task.CompletedTask;
        }));
        actionRow.Add(ActionButton(T("EndfieldBridge.RestoreEFMI"), () =>
        {
            service.Integration.Restore();
            modsSwitch.IsOn = false;
            Show(Here(T("EndfieldBridge.FieldsRestored")));
            return Task.CompletedTask;
        }));
        actionRow.Add(ActionButton(T("EndfieldBridge.OpenLaunchLog"), () =>
        {
            if (!File.Exists(service.Store.LogPath)) service.Store.Log(Here(T("EndfieldBridge.LogCreated")), english);
            host.OpenTextDocument(service.Store.LogPath);
            return Task.CompletedTask;
        }));
        actions.Children.Add(actionRow.Panel);
        body.Children.Add(new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Padding = new Thickness(14, 12, 14, 14), Child = actions });

        var legal = new StackPanel { Spacing = 8 };
        legal.Children.Add(new TextBlock
        {
            Text = T("EndfieldBridge.TheIntegratedLaunchComponentDerivesFrom"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });
        var legalRow = new ButtonGrid(3);
        legalRow.Add(ActionButton(T("EndfieldBridge.AGPL30License"), () => { host.OpenTextDocument(Path.Combine(AppContext.BaseDirectory, "licenses", "AGPL-3.0.txt")); return Task.CompletedTask; }));
        legalRow.Add(ActionButton(T("EndfieldBridge.CreditsAndChanges"), () => { host.OpenTextDocument(Path.Combine(AppContext.BaseDirectory, "licenses", "EndfieldLaunch-NOTICE.md")); return Task.CompletedTask; }));
        legalRow.Add(ActionButton(T("EndfieldBridge.CorrespondingSource"), () => host.OpenUrl(SourceUrl)));
        legal.Children.Add(legalRow.Panel);
        body.Children.Add(new Expander
        {
            Header = T("EndfieldBridge.LicenseAndSource"),
            Content = legal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        });
        // A fixed height (not MaxHeight) bounds the inner ScrollViewer: otherwise the dialog clips the
        // lower sections without a scrollbar. Results get their own row under the settings.
        var content = new Grid { Height = viewport.ContentHeight, RowSpacing = 10 };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var settingsViewer = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            Padding = new Thickness(0, 0, 14, 0)
        };
        content.Children.Add(settingsViewer);
        Grid.SetRow(outputBorder, 1);
        content.Children.Add(outputBorder);
        var dialog = new ContentDialog
        {
            Title = T("EndfieldBridge.EndfieldLaunchSettings"),
            Content = content,
            PrimaryButtonText = T("Common.Save"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Min(860, viewport.MaxWidth);
        void RefreshState()
        {
            bool useIntegrated = integrated.IsOn;
            consent.Visibility = useIntegrated ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = !useIntegrated || consent.IsChecked == true;
            launchHint.Text = useIntegrated
                ? T("EndfieldBridge.StartsTheGameUnderTheOfficially")
                : T("EndfieldBridge.RunsTheXXMIOrOtherLauncher");
            modsHint.Text = modsSwitch.IsOn
                ? T("EndfieldBridge.LoadsModsThroughXXMIEFMISet")
                : T("EndfieldBridge.StartsTheGameDirectlyWithoutMods");
        }

        integrated.Toggled += (_, _) => RefreshState();
        modsSwitch.Toggled += (_, _) => RefreshState();
        consent.Checked += (_, _) => RefreshState();
        consent.Unchecked += (_, _) => RefreshState();
        RefreshState();

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!integrated.IsOn) return;
            try
            {
                service.SaveSettings(CurrentProfile());
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                Show(ex is EndfieldLaunchException known ? known.Text : LaunchText.Both(ex.Message), failure: true);
            }
        };

        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return new Settings(integrated.IsOn, gameBox.Text.Trim(), parentBox.Text.Trim(), argumentsBox.Text.Trim(), modsSwitch.IsOn, loaderBox.Text.Trim());

        TextBox PathBox(string text, string placeholder) => new() { Text = text, PlaceholderText = placeholder, MinWidth = 220 };

        TextBlock SectionTitle(string text) => new() { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

        TextBlock HintText() => new() { TextWrapping = TextWrapping.Wrap, Opacity = .75, FontSize = 12 };

        void AddPathRow(Grid grid, int row, string label, TextBox box, string? extension)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // Wrap long labels instead of letting an auto-width column squeeze the path field.
            var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 150 };
            // Same inset field as the main window's path boxes; the app's TextBox style is borderless.
            box.BorderThickness = new Thickness(0);
            box.Padding = new Thickness(12, 8, 12, 8);
            box.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var field = new Border { Style = (Style)Application.Current.Resources["InsetBorderStyle"], Child = box };
            Grid.SetRow(caption, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(caption);
            grid.Children.Add(field);
            if (extension is null)
            {
                Grid.SetColumnSpan(field, 2);
                return;
            }

            var browse = new Button { Content = T("EndfieldBridge.Browse"), Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
            browse.Click += async (_, _) => await host.RunUiAction("Pick Endfield path", async () =>
            {
                string? picked = await host.PickFile([extension]);
                if (!string.IsNullOrEmpty(picked)) box.Text = picked;
            });
            Grid.SetRow(browse, row);
            Grid.SetColumn(browse, 2);
            grid.Children.Add(browse);
        }
    }

    private sealed class ButtonGrid
    {
        private readonly Grid _grid = new() { ColumnSpacing = 8, RowSpacing = 8 };
        private readonly int _columns;
        private int _count;

        public ButtonGrid(int columns)
        {
            _columns = columns;
            for (int i = 0; i < columns; i++) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        public Panel Panel => _grid;

        public void Add(Button button)
        {
            if (_count % _columns == 0) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(button, _count / _columns);
            Grid.SetColumn(button, _count % _columns);
            _grid.Children.Add(button);
            _count++;
        }
    }
}
