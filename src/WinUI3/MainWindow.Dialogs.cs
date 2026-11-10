using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

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
            BitmapImage? cachedImage = await _services.Images.LoadOnlineBitmapAsync(navigationImages[currentIndex]);
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

    // Any page can ask for a notification through the status service; the window shows it.
    private void ShowAppNotification(string message, InfoBarSeverity severity = InfoBarSeverity.Success, Exception? error = null)
        => _services.Status.Notify(message, severity, error);

    private async void OnNotificationRequested(object? sender, StatusNotification notification)
    {
        string message = notification.Message;
        InfoBarSeverity severity = notification.Severity;
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
            AppInfoBar.Message = message;
            // An error with the failure behind it offers its details, to read or copy for a report (J8).
            if (notification.Error is Exception error)
            {
                var details = new HyperlinkButton { Content = T("Dialogs.ViewDetails") };
                details.Click += async (_, _) => await RunUiActionAsync("Show error details",
                    () => _services.Dialogs.ErrorAsync(AppInfoBar.Title, message, error));
                AppInfoBar.ActionButton = details;
            }
            else
            {
                AppInfoBar.ActionButton = null;
            }
            AppInfoBar.IsOpen = true;

            // Done and for-your-information notes close themselves; warnings and errors stay until closed.
            if (severity is not (InfoBarSeverity.Success or InfoBarSeverity.Informational)) return;
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (notificationVersion == _notificationVersion && !_services.IsClosing)
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

    // Stage J finish, step 2: these live in DialogService; the window keeps one-line forwards for its own callers.
    private Task OpenExternalUrlAsync(string? url, string failureTitle) => _services.Dialogs.OpenExternalUrlAsync(url, failureTitle);

    private Task<string?> PickFolderAsync(string? preferredPath = null) => _services.Dialogs.PickFolderAsync(preferredPath);

    private Task<string?> PickFileAsync(string[] extensions) => _services.Dialogs.PickFileAsync(extensions);

    private void OpenDirectory(string? directory, string title) => _services.Dialogs.OpenDirectory(directory, title);

    private void OpenFileLocation(string? filePath, string title) => _services.Dialogs.OpenFileLocation(filePath, title);

    // The common dialogs, shown by DialogService (stage J8).
    private Task ShowMessageAsync(string content, string title) => _services.Dialogs.MessageAsync(title, content);

    private Task<string?> PromptForTextAsync(string content, string title, string defaultValue, string primaryText)
        => _services.Dialogs.PromptAsync(title, content, defaultValue, primaryText);

    private Task<bool> ShowConfirmAsync(string content, string title, string primaryText) => _services.Dialogs.ConfirmAsync(title, content, primaryText);

    // Delete, remove, overwrite: the red button, and Cancel is the default.
    private Task<bool> ShowDangerConfirmAsync(string content, string title, string primaryText)
        => _services.Dialogs.DangerConfirmAsync(title, content, primaryText);
}
