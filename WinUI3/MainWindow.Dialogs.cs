using System.Diagnostics;
using IntegratedModManager.Core.Layout;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private async Task ShowImageViewerAsync(
        ImageSource? source,
        string title,
        IReadOnlyList<string>? imageUrls = null,
        int selectedIndex = 0)
    {
        List<string> navigationImages = imageUrls?
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        if (source is null && navigationImages.Count == 0)
        {
            return;
        }

        double viewerWidth = Math.Max(520, RootGrid.ActualWidth - 96);
        double viewerHeight = Math.Max(360, RootGrid.ActualHeight - 96);
        var completion = new TaskCompletionSource<bool>();
        var overlay = new Grid
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(230, 0, 0, 0)),
            IsTabStop = true
        };
        Canvas.SetZIndex(overlay, 10000);
        overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        overlay.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = title,
            Margin = new Thickness(24, 18, 24, 8),
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        overlay.Children.Add(titleText);

        var imageStage = new Grid
        {
            Width = viewerWidth,
            Height = viewerHeight - 80,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(imageStage, 1);
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = viewerWidth - 72,
            MaxHeight = viewerHeight - 96
        };
        image.Tapped += (_, args) => args.Handled = true;
        imageStage.Children.Add(image);

        var loadingText = new TextBlock
        {
            Text = T("Dialogs.LoadingHighResolutionImage"),
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        imageStage.Children.Add(loadingText);

        Button previousButton = CreateImageViewerNavigationButton("\uE76B", T("Dialogs.PreviousImage"));
        previousButton.Margin = new Thickness(16);
        previousButton.HorizontalAlignment = HorizontalAlignment.Left;
        previousButton.VerticalAlignment = VerticalAlignment.Center;
        imageStage.Children.Add(previousButton);

        Button nextButton = CreateImageViewerNavigationButton("\uE76C", T("Dialogs.NextImage"));
        nextButton.Margin = new Thickness(16);
        nextButton.HorizontalAlignment = HorizontalAlignment.Right;
        nextButton.VerticalAlignment = VerticalAlignment.Center;
        imageStage.Children.Add(nextButton);
        overlay.Children.Add(imageStage);

        var counterText = new TextBlock
        {
            Margin = new Thickness(24, 8, 24, 18),
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetRow(counterText, 2);
        overlay.Children.Add(counterText);

        int currentIndex = navigationImages.Count == 0
            ? 0
            : Math.Clamp(selectedIndex, 0, navigationImages.Count - 1);
        int loadVersion = 0;
        bool isClosed = false;

        void CloseViewer()
        {
            if (isClosed)
            {
                return;
            }

            isClosed = true;
            RootGrid.Children.Remove(overlay);
            completion.TrySetResult(true);
        }

        void UpdateNavigationState()
        {
            bool hasMultipleImages = navigationImages.Count > 1;
            previousButton.Visibility = hasMultipleImages ? Visibility.Visible : Visibility.Collapsed;
            nextButton.Visibility = hasMultipleImages ? Visibility.Visible : Visibility.Collapsed;
            previousButton.IsEnabled = currentIndex > 0;
            nextButton.IsEnabled = currentIndex < navigationImages.Count - 1;
            counterText.Visibility = hasMultipleImages ? Visibility.Visible : Visibility.Collapsed;
            counterText.Text = hasMultipleImages ? $"{currentIndex + 1} / {navigationImages.Count}" : string.Empty;
        }

        async Task DisplayImageAsync(int index)
        {
            currentIndex = navigationImages.Count == 0
                ? 0
                : Math.Clamp(index, 0, navigationImages.Count - 1);
            UpdateNavigationState();

            if (navigationImages.Count == 0)
            {
                image.Source = source;
                return;
            }

            int requestVersion = ++loadVersion;
            image.Source = null;
            loadingText.Visibility = Visibility.Visible;
            BitmapImage? cachedImage = await LoadOnlineBitmapAsync(navigationImages[currentIndex]);
            if (requestVersion != loadVersion)
            {
                return;
            }

            loadingText.Visibility = Visibility.Collapsed;
            image.Source = cachedImage ?? source;
        }

        previousButton.Click += async (_, _) => await RunUiActionAsync("DisplayImageAsync", () => DisplayImageAsync(currentIndex - 1));
        nextButton.Click += async (_, _) => await RunUiActionAsync("DisplayImageAsync", () => DisplayImageAsync(currentIndex + 1));
        previousButton.Tapped += (_, args) => args.Handled = true;
        nextButton.Tapped += (_, args) => args.Handled = true;
        overlay.Tapped += (_, _) => CloseViewer();
        overlay.KeyDown += async (_, args) =>
        {
            try
            {
                if (args.Key == VirtualKey.Left && currentIndex > 0)
                {
                    args.Handled = true;
                    await DisplayImageAsync(currentIndex - 1);
                }
                else if (args.Key == VirtualKey.Right && currentIndex < navigationImages.Count - 1)
                {
                    args.Handled = true;
                    await DisplayImageAsync(currentIndex + 1);
                }
                else if (args.Key == VirtualKey.Escape)
                {
                    args.Handled = true;
                    CloseViewer();
                }
            }
            catch (Exception ex)
            {
                ReportUiActionFailure("overlay.KeyDown", ex);
            }
        };

        RootGrid.Children.Add(overlay);
        overlay.Loaded += (_, _) => overlay.Focus(FocusState.Programmatic);
        await DisplayImageAsync(currentIndex);
        await completion.Task;
    }

    private Button CreateImageViewerNavigationButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 48,
            Height = 48,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(24),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            Content = new FontIcon { Glyph = glyph, FontSize = 19 }
        };
        ToolTipService.SetToolTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private async void ShowAppNotification(string zh, string en, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        try
        {
            int notificationVersion = ++_notificationVersion;
            AppInfoBar.Severity = severity;
            AppInfoBar.Title = severity switch
            {
                InfoBarSeverity.Error => T("Dialogs.ActionNotCompleted"),
                InfoBarSeverity.Warning => T("Dialogs.Stopped"),
                InfoBarSeverity.Informational => T("Dialogs.Notice"),
                _ => T("Dialogs.Completed")
            };
            AppInfoBar.Message = L(zh, en);
            AppInfoBar.IsOpen = true;

            await Task.Delay(TimeSpan.FromSeconds(3.5));
            if (notificationVersion == _notificationVersion && !_onlineWindowClosed)
            {
                AppInfoBar.IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            // Not routed through ReportUiActionFailure: that would notify again.
            LogApplicationIssue("Show notification", ex);
        }
    }

    private async Task OpenExternalUrlAsync(string? url, string failureTitle)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            await ShowMessageAsync(
                T("Dialogs.ThisEntryDoesNotHaveA"),
                failureTitle);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                T("Common.FailedToOpenLink") + ex.Message,
                failureTitle);
        }
    }

    private UIElement CreateInfoCard(string title, string body)
    {
        return new Border
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = body,
                        Style = (Style)Application.Current.Resources["MutedTextStyle"],
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private async Task<string?> PickFolderAsync(string? preferredPath = null)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickFileAsync(string[] extensions)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        foreach (string extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private void OpenDirectory(string? directory, string title)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                _ = ShowMessageAsync(
                    title + T("Dialogs.DoesNotExistPleaseChooseIt"),
                T("Common.InvalidPath"));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(
                T("Dialogs.FailedToOpenFolder") + ex.Message,
                T("Dialogs.OperationFailed"));
        }
    }

    private void OpenFileLocation(string? filePath, string title)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _ = ShowMessageAsync(
                    title + T("Dialogs.DoesNotExistPleaseChooseIt"),
                T("Common.InvalidPath"));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + filePath + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(
                T("Dialogs.FailedToOpenFileLocation") + ex.Message,
                T("Dialogs.OperationFailed"));
        }
    }

    private async Task ShowMessageAsync(string content, string title)
    {
        DialogViewport viewport = DialogViewportPolicy.Selection(RootGrid.ActualWidth, RootGrid.ActualHeight);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = viewport.ContentHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap }
            },
            CloseButtonText = T("Dialogs.OK"),
            RequestedTheme = RootGrid.ActualTheme,
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async Task<string?> PromptForTextAsync(string content, string title, string defaultValue)
    {
        var textBox = new TextBox
        {
            Text = defaultValue,
            PlaceholderText = T("Dialogs.EnterAName"),
            Margin = new Thickness(0, 12, 0, 0)
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = T("Dialogs.OK"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    private async Task<RepositoryEditorResult?> PromptForRepositoryAsync(string content, string title, WorkspaceRepository initial)
    {
        TextBox nameBox = new()
        {
            Text = initial.Name,
            PlaceholderText = T("Dialogs.EnterRepositoryName")
        };

        TextBox sourceBox = new()
        {
            Text = initial.SourcePath,
            PlaceholderText = T("Dialogs.FirstLevelModStoragePath")
        };

        TextBox targetBox = new()
        {
            Text = initial.TargetPath,
            PlaceholderText = T("Dialogs.TargetGameModFolderPath")
        };

        CheckBox linkDeploymentBox = new()
        {
            IsChecked = initial.UseDirectoryLinks,
            Content = T("Dialogs.DeployWithDirectoryJunctionsRepositoryAnd")
        };

        TextBox launcherBox = new()
        {
            Text = initial.LauncherPath,
            PlaceholderText = T("Dialogs.LauncherPathOptional")
        };

        TextBox siteBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineSourceSite) ? DefaultOnlineSourceSite : initial.OnlineSourceSite,
            PlaceholderText = T("Dialogs.OnlineSourceForExampleGameBanana")
        };

        TextBox gameBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineGameName) ? DefaultOnlineGameName : initial.OnlineGameName,
            PlaceholderText = T("Dialogs.TargetGameNameForExampleArknights")
        };

        TextBox categoryBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineCategoryId) ? DefaultOnlineCategoryId : initial.OnlineCategoryId,
            PlaceholderText = T("Dialogs.CategoryIDForExample21842")
        };

        TextBox wikiBox = new()
        {
            Text = initial.WikiUrl,
            PlaceholderText = T("Dialogs.GameWikiURLOptional")
        };

        TextBox notesBox = new()
        {
            Text = initial.Notes,
            PlaceholderText = T("Dialogs.NotesOptional"),
            AcceptsReturn = true,
            MinHeight = 72,
            TextWrapping = TextWrapping.Wrap
        };

        StackPanel panel = new() { Spacing = 10, MaxWidth = 620 };
        panel.Children.Add(new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateLabeledEditor(T("Dialogs.RepositoryName"), nameBox));
        panel.Children.Add(CreateLabeledEditor(T("Dialogs.ModStoragePath"), sourceBox));
        panel.Children.Add(CreateLabeledEditor(T("Dialogs.TargetPath"), targetBox));
        panel.Children.Add(linkDeploymentBox);
        panel.Children.Add(CreateLabeledEditor(T("Dialogs.LauncherPath"), launcherBox));
        panel.Children.Add(CreateLabeledEditor(T("Common.OnlineSource"), siteBox));
        panel.Children.Add(CreateLabeledEditor(T("Dialogs.TargetGame"), gameBox));
        panel.Children.Add(CreateLabeledEditor(T("Dialogs.CategoryID"), categoryBox));
        panel.Children.Add(CreateLabeledEditor(T("Common.Notes"), notesBox));

        panel.Children.Insert(panel.Children.Count - 1, CreateLabeledEditor(T("Dialogs.GameWiki"), wikiBox));

        ContentDialog dialog = new()
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = T("Common.Save"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return new RepositoryEditorResult
        {
            Name = nameBox.Text,
            SourcePath = sourceBox.Text,
            TargetPath = targetBox.Text,
            UseDirectoryLinks = linkDeploymentBox.IsChecked == true,
            LauncherPath = launcherBox.Text,
            OnlineSourceSite = siteBox.Text,
            OnlineGameName = gameBox.Text,
            OnlineCategoryId = categoryBox.Text,
            WikiUrl = wikiBox.Text,
            Notes = notesBox.Text
        };
    }

    private async Task<OnlineRepositoryConfigResult?> PromptForOnlineRepositoryConfigAsync(WorkspaceRepository initial)
    {
        TextBox siteBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineSourceSite) ? DefaultOnlineSourceSite : initial.OnlineSourceSite,
            PlaceholderText = T("Dialogs.OnlineSourceForExampleGameBanana")
        };

        TextBox gameBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineGameName) ? DefaultOnlineGameName : initial.OnlineGameName,
            PlaceholderText = T("Dialogs.TargetGameNameForExampleArknights")
        };

        TextBox categoryBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineCategoryId) ? DefaultOnlineCategoryId : initial.OnlineCategoryId,
            PlaceholderText = T("Dialogs.SkinCategoryIDForExample42770")
        };

        TextBox wikiBox = new()
        {
            Text = initial.WikiUrl,
            PlaceholderText = T("Dialogs.GameWikiURLOptional")
        };

        TextBox notesBox = new()
        {
            Text = initial.Notes,
            PlaceholderText = T("Dialogs.NotesOptional"),
            AcceptsReturn = true,
            MinHeight = 88,
            TextWrapping = TextWrapping.Wrap
        };

        StackPanel panel = new() { Spacing = 10, MaxWidth = 620 };
        panel.Children.Add(new TextBlock
        {
            Text = T("Dialogs.ThisConfigControlsTheOnlineMod"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateLabeledEditor(T("Common.OnlineSource"), siteBox));
        panel.Children.Add(CreateLabeledEditor(T("Dialogs.TargetGame"), gameBox));
        panel.Children.Add(CreateLabeledEditor(T("Dialogs.SkinCategoryID"), categoryBox));
        panel.Children.Add(CreateLabeledEditor(T("Common.Notes"), notesBox));

        panel.Children.Add(new TextBlock
        {
            Text = T("Dialogs.TheCurrentFetchLogicPassesThis"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });

        panel.Children.Insert(panel.Children.Count - 1, CreateLabeledEditor(T("Dialogs.GameWiki"), wikiBox));

        ContentDialog dialog = new()
        {
            Title = T("Dialogs.EditCurrentRepositoryOnlineCategory"),
            Content = panel,
            PrimaryButtonText = T("Common.Save"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return new OnlineRepositoryConfigResult
        {
            OnlineSourceSite = siteBox.Text,
            OnlineGameName = gameBox.Text,
            OnlineCategoryId = categoryBox.Text,
            WikiUrl = wikiBox.Text,
            Notes = notesBox.Text
        };
    }

    private static UIElement CreateLabeledEditor(string label, Control editor)
    {
        StackPanel panel = new() { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = label
        });
        panel.Children.Add(editor);
        return panel;
    }

    private async Task<bool> ShowConfirmAsync(string content, string title)
    {
        DialogViewport viewport = DialogViewportPolicy.Selection(RootGrid.ActualWidth, RootGrid.ActualHeight);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = viewport.ContentHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap }
            },
            PrimaryButtonText = T("Dialogs.Confirm"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = RootGrid.ActualTheme,
            XamlRoot = RootGrid.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
