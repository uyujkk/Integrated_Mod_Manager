using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>What is new in this version (moved from the window in stage J8).</summary>
internal static class WhatsNewDialog
{
    /// <summary>
    /// The dialog; the caller shows it and opens the details (primary) or the Discord server (secondary). Under the text, a
    /// link closes it and opens the search's list of every feature and where it is (<paramref name="showAllFeatures"/>).
    /// </summary>
    public static ContentDialog Create(AppServices services, string featureSet, double windowHeight, Action showAllFeatures)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = T("WhatsNew.Body"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        var allFeatures = new HyperlinkButton { Content = T("WhatsNew.AllFeatures"), Padding = new Thickness(0, 2, 0, 2) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(allFeatures, "WhatsNewAllFeaturesButton");
        body.Children.Add(allFeatures);
        var dialog = new ContentDialog
        {
            Title = T("WhatsNew.Title", featureSet),
            Content = new ScrollViewer
            {
                MaxHeight = Math.Max(240, windowHeight - 260),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = body
            },
            PrimaryButtonText = T("WhatsNew.OpenDetails"),
            SecondaryButtonText = T("WhatsNew.JoinDiscord"),
            CloseButtonText = T("WhatsNew.Close"),
            DefaultButton = ContentDialogButton.Close
        };
        allFeatures.Click += (_, _) =>
        {
            dialog.Hide();
            showAllFeatures();
        };
        return dialog;
    }
}
