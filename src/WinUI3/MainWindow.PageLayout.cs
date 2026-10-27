using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // Pages lay themselves out from their own size. The window's SizeChanged fires before the
    // pages are measured again, so a page's ActualWidth there still holds the previous size:
    // restoring a maximized window left the settings page laid out for the maximized width.
    private void InitializePageLayouts()
    {
        AttachPageLayout(DashboardScrollViewer, UpdateDashboardLayout);
        AttachPageLayout(SettingsScrollViewer, UpdateSettingsLayout);
    }

    private static void AttachPageLayout(FrameworkElement page, Action layout)
    {
        page.SizeChanged += (_, args) =>
        {
            // A collapsed page reports zero; it lays out again when it is shown.
            if (args.NewSize.Width > 0)
            {
                layout();
            }
        };
    }

    // Hidden pages have no size yet, so they estimate from the window minus the navigation.
    private double PageWidth(FrameworkElement page)
        => page.ActualWidth > 0 ? page.ActualWidth : Math.Max(420, Bounds.Width - 240);

    private double SettingsHeaderHeight()
        => SettingsPageHeader.ActualHeight > 0 ? SettingsPageHeader.ActualHeight : 56;
}
