using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>What is new in this version (moved from the window in stage J8).</summary>
internal static class WhatsNewDialog
{
    /// <summary>The dialog; the caller shows it and opens the details when the primary button is chosen.</summary>
    public static ContentDialog Create(AppServices services, string featureSet, double windowHeight)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        return new ContentDialog
        {
            Title = T("WhatsNew.Title", featureSet),
            Content = new ScrollViewer
            {
                MaxHeight = Math.Max(240, windowHeight - 260),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock { Text = T("WhatsNew.Body"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            },
            PrimaryButtonText = T("WhatsNew.OpenDetails"),
            CloseButtonText = T("WhatsNew.Close"),
            DefaultButton = ContentDialogButton.Close
        };
    }
}
