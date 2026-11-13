using System.Globalization;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ModFolderCopier.WinUI;

internal static class GameBananaPageDialog
{
    public static Uri? ModPage(OnlineModCard mod)
    {
        string path = "/mods/" + mod.ItemId.ToString(CultureInfo.InvariantCulture);
        return mod.ItemId > 0 && Uri.TryCreate(mod.ProfileUrl, UriKind.Absolute, out Uri? url)
            && IsGameBanana(url) && url.AbsolutePath.TrimEnd('/') == path
            ? new Uri("https://gamebanana.com" + path)
            : null;
    }

    private static bool IsGameBanana(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.IdnHost is "gamebanana.com" or "www.gamebanana.com";

    public static async Task ShowAsync(AppServices services, Uri modPage)
    {
        string T(string key) => services.Language.T(key);
        var windowSize = services.Dialogs.Root?.Size ?? default;
        double dialogWidth = Math.Clamp(windowSize.Width - 64, 280, 1120);
        var view = new WebView2();
        var returnToMod = new Button { Content = T("OnlineDetails.ReturnToGameBananaMod"), IsEnabled = false };
        var status = new TextBlock { Text = T("Common.Loading"), TextWrapping = TextWrapping.Wrap };
        var content = new Grid
        {
            Width = dialogWidth - 48,
            Height = Math.Clamp(windowSize.Height - 150, 160, 880),
            RowSpacing = 8
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(returnToMod);
        Grid.SetRow(status, 1);
        content.Children.Add(status);
        Grid.SetRow(view, 2);
        content.Children.Add(view);

        var dialog = new ContentDialog
        {
            Title = "GameBanana",
            Content = content,
            CloseButtonText = T("Common.Close")
        };
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        void ShowUnavailable()
        {
            status.Text = T("OnlineDetails.GameBananaPageUnavailable");
            dialog.PrimaryButtonText = T("Common.OpenOriginalPage");
        }

        bool closed = false;
        dialog.Closed += (_, _) => closed = true;
        dialog.Opened += async (_, _) =>
        {
            try
            {
                string profile = Path.Combine(AppContext.BaseDirectory, "cache", "gamebanana-webview2");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null);
                if (closed) return;
                await view.EnsureCoreWebView2Async(environment);
                if (closed) return;
                CoreWebView2 browser = view.CoreWebView2;
                browser.Settings.IsWebMessageEnabled = false;
                browser.Settings.AreHostObjectsAllowed = false;
                browser.NavigationStarting += (_, args) =>
                    args.Cancel = !Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? target) || !IsGameBanana(target);
                browser.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    if (args.IsUserInitiated && Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? target) && IsGameBanana(target))
                        browser.Navigate(target.AbsoluteUri);
                };
                browser.NavigationCompleted += (_, args) =>
                {
                    if (args.IsSuccess)
                    {
                        status.Text = T("OnlineDetails.GameBananaLikeHint");
                        dialog.PrimaryButtonText = string.Empty;
                    }
                    else if (args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                        ShowUnavailable();
                };
                returnToMod.Click += (_, _) => browser.Navigate(modPage.AbsoluteUri);
                returnToMod.IsEnabled = true;
                browser.Navigate(modPage.AbsoluteUri);
            }
            catch (Exception)
            {
                if (!closed) ShowUnavailable();
            }
        };

        ContentDialogResult result;
        try { result = await services.Dialogs.ShowAsync(dialog); }
        finally { view.Close(); }
        if (result == ContentDialogResult.Primary)
            await services.Dialogs.OpenExternalUrlAsync(modPage.AbsoluteUri, T("Common.OpenPageFailed"));
    }
}
