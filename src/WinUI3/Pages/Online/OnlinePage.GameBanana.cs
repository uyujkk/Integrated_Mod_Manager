using System.Globalization;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private static Uri? GameBananaModPage(OnlineModCard mod)
    {
        string path = "/mods/" + mod.ItemId.ToString(CultureInfo.InvariantCulture);
        return mod.ItemId > 0 && Uri.TryCreate(mod.ProfileUrl, UriKind.Absolute, out Uri? url)
            && IsGameBanana(url) && url.AbsolutePath.TrimEnd('/') == path
            ? new Uri("https://gamebanana.com" + path)
            : null;
    }

    private static bool IsGameBanana(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.IdnHost is "gamebanana.com" or "www.gamebanana.com";

    private async Task ShowGameBananaPageAsync(Uri modPage)
    {
        if (GameBananaPageHost.Content is not null) return;

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
        var openOriginal = new HyperlinkButton
        {
            Content = T("Common.OpenOriginalPage"),
            Visibility = Visibility.Collapsed
        };
        var content = new Grid { RowSpacing = 4 };
        AutomationProperties.SetName(content, "GameBanana");
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(toolbar);
        Grid.SetRow(status, 1);
        content.Children.Add(status);
        Grid.SetRow(openOriginal, 2);
        content.Children.Add(openOriginal);
        Grid.SetRow(view, 3);
        content.Children.Add(view);

        void ClosePage(object sender, RoutedEventArgs args)
        {
            if (!ReferenceEquals(GameBananaPageHost.Content, content)) return;
            Unloaded -= ClosePage;
            GameBananaPageHost.Content = null;
            GameBananaPageHost.Visibility = Visibility.Collapsed;
            OnlineScrollViewer.Visibility = Visibility.Visible;
            view.Close();
            if (ReferenceEquals(sender, close))
                LikeOnlineDetailButton.Focus(FocusState.Programmatic);
        }

        void ShowUnavailable()
        {
            status.Text = T("OnlineDetails.GameBananaPageUnavailable");
            openOriginal.Visibility = Visibility.Visible;
        }

        close.Click += ClosePage;
        openOriginal.Click += async (_, _) =>
            await _services.Dialogs.OpenExternalUrlAsync(modPage.AbsoluteUri, T("Common.OpenPageFailed"));
        Unloaded += ClosePage;
        OnlineScrollViewer.Visibility = Visibility.Collapsed;
        GameBananaPageHost.Content = content;
        GameBananaPageHost.Visibility = Visibility.Visible;
        close.Focus(FocusState.Programmatic);

        try
        {
            string profile = Path.Combine(AppContext.BaseDirectory, "cache", "gamebanana-webview2");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null);
            if (!ReferenceEquals(GameBananaPageHost.Content, content)) return;
            await view.EnsureCoreWebView2Async(environment);
            if (!ReferenceEquals(GameBananaPageHost.Content, content)) return;
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
                    openOriginal.Visibility = Visibility.Collapsed;
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
            if (ReferenceEquals(GameBananaPageHost.Content, content)) ShowUnavailable();
        }
    }
}
