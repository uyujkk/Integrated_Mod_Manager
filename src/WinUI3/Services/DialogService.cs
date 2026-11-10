using IntegratedModManager.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Shows every dialog of the app (stage J8): sets the XamlRoot, the theme and the default dialog style, and
/// queues dialogs so only one is open at a time. Also builds the common message, confirmation, dangerous
/// confirmation, error and text-input dialogs.
/// </summary>
internal sealed class DialogService(LanguageService language)
{
    private readonly DialogQueue<ContentDialog> _queue = new();
    private Func<XamlRoot?> _root = () => null;
    private Func<ElementTheme> _theme = () => ElementTheme.Default;

    public XamlRoot? Root => _root();

    /// <summary>The most a dialog's content shows before it scrolls.</summary>
    public const double MaxContentHeight = 400;

    private string T(string key) => language.T(key);

    private string T(string key, params object?[] values) => language.T(key, values);

    /// <summary>Called once the window exists.</summary>
    public void Attach(Func<XamlRoot?> root, Func<ElementTheme>? theme = null)
    {
        _root = root;
        if (theme is not null) _theme = theme;
    }

    /// <summary>A dialog is open or waiting to open.</summary>
    public bool IsDialogOpen => _queue.IsBusy || Root is XamlRoot root
        && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is ContentDialog);

    /// <summary>The space a dialog's content may use in the current window.</summary>
    public DialogViewport Viewport => Root is XamlRoot root
        ? DialogViewportPolicy.Selection(root.Size.Width, root.Size.Height)
        : DialogViewportPolicy.Selection(0, 0);

    /// <summary>
    /// Shows the dialog once no other dialog is open. A dialog closed with <see cref="Close"/> before its turn is
    /// not shown and returns <see cref="ContentDialogResult.None"/>.
    /// </summary>
    public Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
        => _queue.ShowAsync(dialog, async shown =>
        {
            Prepare(shown);
            return await shown.ShowAsync();
        }, ContentDialogResult.None);

    /// <summary>Closes a dialog shown with <see cref="ShowAsync"/>, or keeps it from opening if it is still queued.</summary>
    public void Close(ContentDialog dialog)
    {
        if (_queue.Close(dialog)) dialog.Hide();
    }

    public async Task MessageAsync(string title, string content)
        => await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = TextContent(content),
            CloseButtonText = T("Dialogs.OK")
        });

    /// <summary>
    /// An error. With the failure behind it, "View details" expands its type, code, message and stack and the path of
    /// the error log, which the user can copy for a report (J8).
    /// </summary>
    public async Task ErrorAsync(string title, string content, Exception? exception = null)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        if (exception is not null) panel.Children.Add(ErrorDetails(exception));
        await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = panel, MaxHeight = Math.Min(MaxContentHeight, Viewport.ContentHeight), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            CloseButtonText = T("Common.Close"),
            DefaultButton = ContentDialogButton.Close
        });
    }

    /// <summary>The error log the app writes (see MainWindow.LogApplicationIssue).</summary>
    public static string ErrorLogPath => Path.Combine(AppContext.BaseDirectory, "app-errors.log");

    /// <summary>The text "View details" shows and copies.</summary>
    public static string DetailsText(Exception exception, string logLabel)
        => $"{exception.GetType().FullName} (0x{exception.HResult:X8})\n{exception.Message}\n\n{exception.StackTrace}".TrimEnd()
            + "\n\n" + logLabel;

    private Expander ErrorDetails(Exception exception)
    {
        string details = DetailsText(exception, T("Dialogs.ErrorLog", ErrorLogPath));
        var text = new TextBlock
        {
            Text = details, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(text, T("Dialogs.ViewDetails"));
        var copy = new Button { Content = T("Dialogs.CopyDetails") };
        copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(details);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new ScrollViewer { Content = text, MaxHeight = 200, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        body.Children.Add(copy);
        return new Expander
        {
            Header = T("Dialogs.ViewDetails"), Content = body,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
    }

    /// <summary>A confirmation; the primary button says what happens (a verb, not "OK").</summary>
    public async Task<bool> ConfirmAsync(string title, string content, string primaryText)
        => await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = TextContent(content),
            PrimaryButtonText = primaryText,
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        }) == ContentDialogResult.Primary;

    /// <summary>A confirmation of something that cannot be undone (delete, remove, overwrite): see <see cref="MarkDangerous"/>.</summary>
    public async Task<bool> DangerConfirmAsync(string title, string content, string primaryText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = TextContent(content),
            PrimaryButtonText = primaryText,
            CloseButtonText = T("Common.Cancel")
        };
        MarkDangerous(dialog);
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>
    /// The primary button gets the fixed red danger style and Cancel stays a plain button with the keyboard focus, so
    /// Enter and Esc both cancel. No button is the dialog's default: the default button is drawn in the accent colour,
    /// and with a red accent Cancel would look like the danger button.
    /// </summary>
    public static void MarkDangerous(ContentDialog dialog)
    {
        dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["DangerAccentButtonStyle"];
        dialog.CloseButtonStyle = (Style)Application.Current.Resources["DefaultButtonStyle"];
        dialog.DefaultButton = ContentDialogButton.None;
        // Enter invokes the focused button: Cancel, so a stray key press never deletes.
        // The dialog's buttons live in its popup; the dialog moves the focus itself right after Opened, so Cancel
        // takes it a moment later.
        dialog.Opened += async (_, _) =>
        {
            await Task.Delay(150);
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot))
                if (popup.Child is not null && FindButton(popup.Child, "CloseButton") is Button cancel)
                {
                    cancel.Focus(FocusState.Keyboard);
                    return;
                }
        };
    }

    private static Button? FindButton(DependencyObject parent, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button { Name: var found } button && found == name) return button;
            if (FindButton(child, name) is Button nested) return nested;
        }
        return null;
    }

    public async Task<string?> PromptAsync(string title, string content, string defaultValue, string primaryText)
    {
        var textBox = new TextBox
        {
            Text = defaultValue,
            PlaceholderText = T("Dialogs.EnterAName"),
            Name = "PromptTextBox",
            Margin = new Thickness(0, 12, 0, 0)
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(textBox);
        ContentDialogResult result = await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = primaryText,
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        });
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    // Text longer than the dialog scrolls, at most 400 high like the dialogs of Windows Settings.
    private ScrollViewer TextContent(string content) => new()
    {
        MaxHeight = Math.Min(MaxContentHeight, Viewport.ContentHeight),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap }
    };

    private void Prepare(ContentDialog dialog)
    {
        dialog.XamlRoot ??= Root;
        if (dialog.RequestedTheme == ElementTheme.Default) dialog.RequestedTheme = _theme();
        if (dialog.Style is null && Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out object style))
            dialog.Style = (Style)style;
    }
}
