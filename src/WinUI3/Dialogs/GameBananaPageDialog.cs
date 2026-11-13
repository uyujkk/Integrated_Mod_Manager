using System.Globalization;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        var view = new WebView2();
        var returnToMod = new Button { Content = T("OnlineDetails.ReturnToGameBananaMod"), IsEnabled = false };
        var close = new Button { Content = T("Common.Close"), HorizontalAlignment = HorizontalAlignment.Right };
        var toolbar = new Grid { ColumnSpacing = 8 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(returnToMod);
        Grid.SetColumn(close, 1);
        toolbar.Children.Add(close);
        var status = new TextBlock { Text = T("Common.Loading"), TextWrapping = TextWrapping.Wrap };
        var content = new Grid { RowSpacing = 4 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(toolbar);
        Grid.SetRow(status, 1);
        content.Children.Add(status);
        Grid.SetRow(view, 2);
        content.Children.Add(view);

        var dialog = new ContentDialog { Content = content };
        AutomationProperties.SetName(dialog, "GameBanana");
        const double padding = 8;
        const double maxWidth = 1600;
        const double maxHeight = 1100;
        dialog.Resources["ContentDialogPadding"] = new Thickness(padding);
        dialog.Resources["ContentDialogMinWidth"] = 0d;
        dialog.Resources["ContentDialogMinHeight"] = 0d;
        dialog.Resources["ContentDialogMaxWidth"] = maxWidth;
        dialog.Resources["ContentDialogMaxHeight"] = maxHeight;
        close.Click += (_, _) => services.Dialogs.Close(dialog);

        XamlRoot? root = services.Dialogs.Root;
        void Resize()
        {
            var size = root?.Size ?? default;
            double width = Math.Clamp(size.Width - 24, 260, maxWidth);
            double height = Math.Clamp(size.Height - 24, 180, maxHeight);
            content.Width = width - 2 * (padding + 1);
            content.Height = height - 2 * (padding + 1)
                - (string.IsNullOrEmpty(dialog.PrimaryButtonText) ? 0 : 52);
        }
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
        Resize();
        void ShowUnavailable()
        {
            status.Text = T("OnlineDetails.GameBananaPageUnavailable");
            dialog.PrimaryButtonText = T("Common.OpenOriginalPage");
            Resize();
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
                        Resize();
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
        if (root is not null) root.Changed += OnRootChanged;
        try { result = await services.Dialogs.ShowAsync(dialog); }
        finally
        {
            if (root is not null) root.Changed -= OnRootChanged;
            view.Close();
        }
        if (result == ContentDialogResult.Primary)
            await services.Dialogs.OpenExternalUrlAsync(modPage.AbsoluteUri, T("Common.OpenPageFailed"));
    }
}
