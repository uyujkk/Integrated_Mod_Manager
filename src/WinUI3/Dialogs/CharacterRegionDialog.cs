using System.Runtime.InteropServices.WindowsRuntime;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace ModFolderCopier.WinUI;

/// <summary>Dragging the region of the game screen that shows the active character (moved from the window in stage J8).</summary>
internal static class CharacterRegionDialog
{
    /// <summary>The chosen region of a captured frame, or null when cancelled.</summary>
    public static async Task<CharacterScreenRegion?> ChooseAsync(AppServices services, byte[] bgra, int width, int height)
    {
        string T(string key) => services.Language.T(key);
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(bgra, 0, bgra.Length);
        double scale = Math.Min(800d / width, 430d / height);
        var canvas = new Canvas { Width = width * scale, Height = height * scale };
        canvas.Children.Add(new Image { Source = bitmap, Width = canvas.Width, Height = canvas.Height });
        var selection = new Rectangle { Stroke = new SolidColorBrush(Microsoft.UI.Colors.Orange), StrokeThickness = 2, IsHitTestVisible = false };
        canvas.Children.Add(selection);
        Point? start = null;
        CharacterScreenRegion? chosen = null;
        canvas.PointerPressed += (_, e) => { start = e.GetCurrentPoint(canvas).Position; chosen = null; canvas.CapturePointer(e.Pointer); };
        canvas.PointerMoved += (_, e) =>
        {
            if (start is not Point from) return;
            Point to = e.GetCurrentPoint(canvas).Position;
            double x = Math.Clamp(Math.Min(from.X, to.X), 0, canvas.Width);
            double y = Math.Clamp(Math.Min(from.Y, to.Y), 0, canvas.Height);
            selection.Width = Math.Min(Math.Abs(to.X - from.X), canvas.Width - x);
            selection.Height = Math.Min(Math.Abs(to.Y - from.Y), canvas.Height - y);
            Canvas.SetLeft(selection, x); Canvas.SetTop(selection, y);
            chosen = new(x / canvas.Width, y / canvas.Height, selection.Width / canvas.Width, selection.Height / canvas.Height);
        };
        canvas.PointerReleased += (_, e) => { start = null; canvas.ReleasePointerCapture(e.Pointer); };
        var dialog = new ContentDialog
        {
            Title = T("CharacterFollow.DragOverTheActiveCharacterPortrait"),
            Content = canvas, PrimaryButtonText = T("CharacterFollow.SaveRegion"), CloseButtonText = T("Common.Cancel")
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900d;
        dialog.PrimaryButtonClick += (_, e) => { if (chosen?.IsValid != true) e.Cancel = true; };
        return await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary && chosen?.IsValid == true ? chosen : null;
    }
}
