using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ModFolderCopier.WinUI;

// Issue #129: the Mod's GameBanana page inside the Online page, so a signed-in user can like it on the site itself.
// The address rules are in Core (GameBananaWeb); the sign-in stays in its own profile folder, which Settings clears.
public sealed partial class OnlinePage
{
    // Every request of the page goes through the app's sample client; one without a sample gets a 404.
    private void AnswerGameBananaPageFromSamples(CoreWebView2 browser, CoreWebView2Environment environment)
    {
        browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        browser.WebResourceRequested += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                using HttpResponseMessage response = await _host.Http.GetAsync(args.Request.Uri);
                byte[] body = await response.Content.ReadAsByteArrayAsync();
                string type = response.Content.Headers.ContentType?.ToString() ?? "text/html";
                args.Response = environment.CreateWebResourceResponse(new MemoryStream(body).AsRandomAccessStream(), 200, "OK", "Content-Type: " + type);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                args.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
            }
            catch (Exception ex)
            {
                _host.LogApplicationIssue("GameBanana page sample", ex);
                args.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
            }
            finally { deferral.Complete(); }
        };
    }

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

        // What this page is and where its sign-in goes, always on top of it.
        var notice = new InfoBar
        {
            Severity = InfoBarSeverity.Informational,
            IsClosable = false,
            IsOpen = true,
            Message = T("OnlineDetails.GameBananaPageNotice")
        };
        var status = new TextBlock { Text = T("Common.Loading"), TextWrapping = TextWrapping.Wrap };
        var openOriginal = new HyperlinkButton
        {
            Content = T("Common.OpenOriginalPage"),
            Visibility = Visibility.Collapsed
        };
        var content = new Grid { RowSpacing = 4 };
        AutomationProperties.SetName(content, "GameBanana");
        AutomationProperties.SetAutomationId(content, "GameBananaPage");
        for (int row = 0; row < 4; row++) content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(toolbar);
        Grid.SetRow(notice, 1);
        content.Children.Add(notice);
        Grid.SetRow(status, 2);
        content.Children.Add(status);
        Grid.SetRow(openOriginal, 3);
        content.Children.Add(openOriginal);
        Grid.SetRow(view, 4);
        content.Children.Add(view);

        // Set once the page has its browser: completes when that browser process has exited after closing.
        TaskCompletionSource? browserExited = null;

        void ClosePage(bool returnFocus)
        {
            if (!ReferenceEquals(GameBananaPageHost.Content, content)) return;
            Unloaded -= OnUnloaded;
            _services.GameBananaWeb.SetOpenPage(null);
            GameBananaPageHost.Content = null;
            GameBananaPageHost.Visibility = Visibility.Collapsed;
            OnlineScrollViewer.Visibility = Visibility.Visible;
            view.Close();
            if (returnFocus) LikeOnlineDetailButton.Focus(FocusState.Programmatic);
        }

        void OnUnloaded(object sender, RoutedEventArgs args) => ClosePage(returnFocus: false);

        void ShowUnavailable()
        {
            status.Text = T("OnlineDetails.GameBananaPageUnavailable");
            openOriginal.Visibility = Visibility.Visible;
        }

        close.Click += (_, _) => ClosePage(returnFocus: true);
        openOriginal.Click += async (_, _) =>
            await _services.Dialogs.OpenExternalUrlAsync(modPage.AbsoluteUri, T("Common.OpenPageFailed"));
        Unloaded += OnUnloaded;
        // Settings → clear GameBanana sign-in closes the page first and waits for its browser to let go of the files.
        _services.GameBananaWeb.SetOpenPage(async () =>
        {
            TaskCompletionSource? exited = browserExited;
            ClosePage(returnFocus: false);
            if (exited is not null) await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        });
        OnlineScrollViewer.Visibility = Visibility.Collapsed;
        GameBananaPageHost.Content = content;
        GameBananaPageHost.Visibility = Visibility.Visible;
        close.Focus(FocusState.Programmatic);

        try
        {
            // Sample mode (IMM_ONLINE_FIXTURES, screenshots and UI tests only): the page is answered from the samples, and a
            // proxy that refuses every connection makes sure nothing it asks for reaches the network.
            bool samples = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OnlineHttpClientFactory.FixturesVariable));
            CoreWebView2EnvironmentOptions? options = samples
                ? new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--proxy-server=127.0.0.1:9 --proxy-bypass-list=<-loopback>" }
                : null;
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, _services.GameBananaWeb.ProfileFolder, options);
            if (!ReferenceEquals(GameBananaPageHost.Content, content)) return;
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            browserExited = exited;
            await view.EnsureCoreWebView2Async(environment);
            if (!ReferenceEquals(GameBananaPageHost.Content, content)) return;
            CoreWebView2 browser = view.CoreWebView2;
            browser.Settings.IsWebMessageEnabled = false;
            browser.Settings.AreHostObjectsAllowed = false;
            if (samples) AnswerGameBananaPageFromSamples(browser, environment);
            // Only GameBanana at the top level; its sign-in never leaves the site (GameBananaWeb.AllowsTopLevelNavigation).
            browser.NavigationStarting += (_, args) => args.Cancel = !GameBananaWeb.AllowsTopLevelNavigation(args.Uri);
            browser.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated && GameBananaWeb.AllowsTopLevelNavigation(args.Uri))
                    browser.Navigate(args.Uri);
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
