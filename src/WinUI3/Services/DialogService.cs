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
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Dialogs closed (Close) while still waiting in the queue: they are skipped instead of shown.
    private readonly HashSet<ContentDialog> _withdrawn = [];
    private ContentDialog? _current;
    private Func<XamlRoot?> _root = () => null;
    private Func<ElementTheme> _theme = () => ElementTheme.Default;

    public XamlRoot? Root => _root();

    /// <summary>The most a dialog's content shows before it scrolls.</summary>
    public const double MaxContentHeight = 400;

    private string T(string key) => language.T(key);

    /// <summary>Called once the window exists.</summary>
    public void Attach(Func<XamlRoot?> root, Func<ElementTheme>? theme = null)
    {
        _root = root;
        if (theme is not null) _theme = theme;
    }

    /// <summary>A dialog is open or waiting to open.</summary>
    public bool IsDialogOpen => _gate.CurrentCount == 0 || Root is XamlRoot root
        && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is ContentDialog);

    /// <summary>The space a dialog's content may use in the current window.</summary>
    public DialogViewport Viewport => Root is XamlRoot root
        ? DialogViewportPolicy.Selection(root.Size.Width, root.Size.Height)
        : DialogViewportPolicy.Selection(0, 0);

    /// <summary>
    /// Shows the dialog once no other dialog is open. A dialog closed with <see cref="Close"/> before its turn is
    /// not shown and returns <see cref="ContentDialogResult.None"/>.
    /// </summary>
    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _gate.WaitAsync();
        try
        {
            if (_withdrawn.Remove(dialog)) return ContentDialogResult.None;
            Prepare(dialog);
            _current = dialog;
            return await dialog.ShowAsync();
        }
        finally
        {
            if (ReferenceEquals(_current, dialog)) _current = null;
            _gate.Release();
        }
    }

    /// <summary>Closes a dialog shown with <see cref="ShowAsync"/>, or keeps it from opening if it is still queued.</summary>
    public void Close(ContentDialog dialog)
    {
        if (ReferenceEquals(_current, dialog)) dialog.Hide();
        else _withdrawn.Add(dialog);
    }

    public async Task MessageAsync(string title, string content)
        => await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = TextContent(content),
            CloseButtonText = T("Dialogs.OK")
        });

    /// <summary>An error; <paramref name="exception"/> is the failure behind it, when there is one.</summary>
    public async Task ErrorAsync(string title, string content, Exception? exception = null)
    {
        _ = exception;
        await MessageAsync(title, content);
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
    /// The primary button gets the fixed red danger style, Cancel stays a plain button and is the default, so Enter
    /// and Esc both cancel and the danger button never looks like an accent-coloured one.
    /// </summary>
    public static void MarkDangerous(ContentDialog dialog)
    {
        dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["DangerAccentButtonStyle"];
        dialog.CloseButtonStyle = (Style)Application.Current.Resources["DefaultButtonStyle"];
        dialog.DefaultButton = ContentDialogButton.Close;
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
