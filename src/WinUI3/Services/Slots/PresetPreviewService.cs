using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Pictures of presets and state slots (stage J finish, step 6, moved from the window unchanged): stored as
/// snapshots\preset-previews\&lt;repository&gt;\&lt;preset&gt;.jpg, taken from the game window only after the user
/// agreed (they can show a UID, a nickname or chat), chosen from a file, shown in lists and cleaned up. The overlay's
/// slot gallery and framing still live in the window.
/// </summary>
internal sealed partial class PresetPreviewService(AppServices services)
{
    private readonly AppServices _services = services;
    private Action<string, Exception> _log = (_, _) => { };
    private Action _saveShellConfig = () => { };
    private Action<string> _showStatus = _ => { };
    private Action _activateWindow = () => { };
    private Func<string, Func<Task>, Task> _runUiAction = (_, action) => action();
    private Func<ShortcutOverlayController> _overlayAccess = () => null!;
    private Func<bool> _applyingCombination = () => false;

    /// <summary>
    /// Called once by the window: the error log, saving beta-shell.json, the workspace status line, bringing the window
    /// back after a capture, running a UI action with its error report, the overlay, and whether a combination restore
    /// is running.
    /// </summary>
    public void Attach(Action<string, Exception> log, Action saveShellConfig, Action<string> showStatus, Action activateWindow,
        Func<string, Func<Task>, Task> runUiAction, Func<ShortcutOverlayController> overlay, Func<bool> applyingCombination)
    {
        _log = log;
        _saveShellConfig = saveShellConfig;
        _showStatus = showStatus;
        _activateWindow = activateWindow;
        _runUiAction = runUiAction;
        _overlayAccess = overlay;
        _applyingCombination = applyingCombination;
    }

    public PresetPreviewStore Store { get; } = new(Path.Combine(AppContext.BaseDirectory, "snapshots", "preset-previews"));

    /// <summary>The framing rectangle keeps its size (saved in beta-shell.json).</summary>
    public bool FrameLocked { get; set; }

    /// <summary>The part of the game every slot picture shows, once the user chose one (saved in beta-shell.json).</summary>
    public CharacterScreenRegion? DefaultRegion { get; set; }

    /// <summary>Where the last framing was, to reopen there.</summary>
    public CharacterScreenRegion? LastRegion { get; set; }

    private ShortcutPageViewModel _shortcuts => _services.Shortcuts;
    private ShortcutOverlayController _overlay => _overlayAccess();
    private bool _applyingCombinationProfile => _applyingCombination();

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private void SaveShellConfig() => _saveShellConfig();

    private void LogApplicationIssue(string operation, Exception exception) => _log(operation, exception);

    private Task ShowMessageAsync(string content, string title) => _services.Dialogs.MessageAsync(title, content);

    private Task<bool> ShowConfirmAsync(string content, string title, string primaryText) => _services.Dialogs.ConfirmAsync(title, content, primaryText);

    private Task<string?> PickFileAsync(string[] extensions) => _services.Dialogs.PickFileAsync(extensions);

    private Task RunUiActionAsync(string operation, Func<Task> action) => _runUiAction(operation, action);





    // Screenshots can show a UID, a nickname or chat: pictures are taken only after the user agrees.
    internal async Task EnablePresetPreviewsAsync()
    {
        if (await ShowConfirmAsync(T("PresetPreview.PrivacyNotice"), T("PresetPreview.CaptureWhenSaving"), T("Dialogs.TurnOn")))
            _shortcuts.CapturePresetPreviews = true;
    }

    internal void ClearPresetPreviewRegion()
    {
        DefaultRegion = null;
        SaveShellConfig();
        _showStatus(T("PresetPreview.DefaultRegionCleared"));
    }

    // A saved slot shows the same part of the game every time, once the user has set a default region.
    internal CharacterFrame? CropToDefaultRegion(CharacterFrame? frame)
    {
        if (frame is null || DefaultRegion is null
            || PresetPreviewStore.RegionToPixels(DefaultRegion, frame.Width, frame.Height) is not { } area) return frame;
        byte[] pixels = PresetPreviewStore.CropBgra(frame.Bgra, frame.Width, area);
        return PresetPreviewStore.IsNearlyBlack(pixels) ? null : new CharacterFrame(area.Width, area.Height, pixels);
    }

    // Taken before F10, which briefly disturbs the picture. A failure only means no preview.
    internal async Task<CharacterFrame?> CapturePresetPreviewAsync()
    {
        if (!_shortcuts.CapturePresetPreviews || _overlay.Window is not { } overlay
            || _shortcuts.SelectedTarget is not ShortcutTarget target) return null;
        return await CaptureTargetFrameAsync(target, overlay);
    }

    // Null when the game cannot be captured: not in front, partly off screen, or a black exclusive full-screen frame.
    internal async Task<CharacterFrame?> CaptureTargetFrameAsync(ShortcutTarget target, ShortcutOverlayWindow? overlay)
    {
        overlay?.BeginCaptureExclusion();
        try
        {
            // Let the desktop compositor apply the exclusion, and the game regain focus after a dialog.
            await Task.Delay(150);
            CharacterFrame frame = CharacterScreenCapture.Capture(target, _services.Language.Current);
            return PresetPreviewStore.IsNearlyBlack(frame.Bgra) ? null : frame;
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Preset preview capture", ex);
            return null;
        }
        finally
        {
            if (overlay is not null && ReferenceEquals(_overlay.Window, overlay)) overlay.EndCaptureExclusion();
        }
    }

    // The buttons under a selected preset in the main window: picture, then choosing or clearing it.
    internal FrameworkElement CreatePresetPreviewEditor(string repositoryId, string presetId, string? previewFile, Action<string?> assign, Action refresh)
    {
        var panel = new StackPanel { Spacing = 6 };
        if (CreatePresetPreviewImage(previewFile, 320, 180) is { } image) panel.Children.Add(image);
        var menu = new MenuFlyout();
        void Add(string text, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += async (_, _) => await RunUiActionAsync("Preset preview", action);
            menu.Items.Add(item);
        }
        Add(T("PresetPreview.FromGame"), async () =>
        {
            if (_shortcuts.SelectedTarget is not ShortcutTarget target)
            {
                await ShowMessageAsync(T("PresetPreview.SelectGameWindow"), T("PresetPreview.SetPreview"));
                return;
            }
            ShortcutInputService.FocusTarget(target);
            CharacterFrame? frame = await CaptureTargetFrameAsync(target, _overlay.Window);
            _activateWindow();
            await ReplacePresetPreviewAsync(repositoryId, presetId, frame, previewFile, assign, refresh);
        });
        Add(T("PresetPreview.FromFile"), async () =>
        {
            string? path = await PickFileAsync([".jpg", ".jpeg", ".png", ".bmp"]);
            if (path is null) return;
            CharacterFrame? frame = null;
            try { frame = await ReadPresetPreviewFileAsync(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException) { LogApplicationIssue("Preset preview file", ex); }
            await ReplacePresetPreviewAsync(repositoryId, presetId, frame, previewFile, assign, refresh);
        });
        if (previewFile is not null) Add(T("PresetPreview.Clear"), () =>
        {
            assign(null);
            SaveShellConfig();
            DeletePresetPreview(previewFile);
            refresh();
            return Task.CompletedTask;
        });
        panel.Children.Add(new DropDownButton
        {
            Content = T("PresetPreview.SetPreview"), Flyout = menu,
            IsEnabled = !_services.Slots.IsRestoring && !_applyingCombinationProfile
        });
        return panel;
    }

    internal async Task ReplacePresetPreviewAsync(string repositoryId, string presetId, CharacterFrame? frame, string? previous, Action<string?> assign, Action refresh)
    {
        string? stored = await StorePresetPreviewAsync(repositoryId, presetId, frame);
        if (stored is null)
        {
            await ShowMessageAsync(T("PresetPreview.CannotUse"), T("PresetPreview.SetPreview"));
            return;
        }
        assign(stored);
        SaveShellConfig();
        if (previous != stored) DeletePresetPreview(previous);
        refresh();
    }

    internal static async Task<CharacterFrame> ReadPresetPreviewFileAsync(string path)
    {
        using var stream = new InMemoryRandomAccessStream();
        byte[] bytes = await File.ReadAllBytesAsync(path);
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        (int width, int height) = PresetPreviewStore.ThumbnailSize((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        var transform = new BitmapTransform { ScaledWidth = (uint)width, ScaledHeight = (uint)height, InterpolationMode = BitmapInterpolationMode.Fant };
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        // The scale applies before the orientation, so a rotated photo comes back with its sides swapped.
        bool swapped = decoder.OrientedPixelWidth != decoder.PixelWidth;
        return new CharacterFrame(swapped ? height : width, swapped ? width : height, pixels.DetachPixelData());
    }

    internal object PresetPreviewLabel(string text, string? previewFile)
    {
        if (CreatePresetPreviewImage(previewFile, 64, 36) is not { } image) return text;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(image);
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    internal async Task<string?> StorePresetPreviewAsync(string repositoryId, string presetId, CharacterFrame? frame)
    {
        if (frame is null) return null;
        try
        {
            byte[] jpeg = await EncodePresetPreviewAsync(frame);
            return await Task.Run(() => Store.Write(repositoryId, presetId, jpeg));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Preset preview save", ex);
            return null;
        }
    }

    internal string PresetPreviewNote(string? previewFile) =>
        _shortcuts.CapturePresetPreviews && previewFile is null ? T("PresetPreview.NotCaptured") : string.Empty;

    internal static async Task<byte[]> EncodePresetPreviewAsync(CharacterFrame frame)
    {
        using var stream = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(.8f, PropertyType.Single) };
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, options);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)frame.Width, (uint)frame.Height, 96, 96, frame.Bgra);
        (int width, int height) = PresetPreviewStore.ThumbnailSize(frame.Width, frame.Height);
        encoder.BitmapTransform.ScaledWidth = (uint)width;
        encoder.BitmapTransform.ScaledHeight = (uint)height;
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync();
        using var output = new MemoryStream();
        using (Stream encoded = stream.GetInputStreamAt(0).AsStreamForRead()) await encoded.CopyToAsync(output);
        return output.ToArray();
    }

    internal Image? CreatePresetPreviewImage(string? previewFile, double width, double height)
    {
        if (Store.Resolve(previewFile) is not { } path || !File.Exists(path)) return null;
        // From memory, not a URI: a replaced preview keeps its path, and the image cache would show the old picture.
        var bitmap = new BitmapImage();
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            bitmap.SetSource(stream.AsRandomAccessStream());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            LogApplicationIssue("Preset preview load", ex);
            return null;
        }
        return new Image
        {
            Source = bitmap, Width = width, Height = height,
            // Whole picture: framed characters are taller than wide, and cropping would cut them off.
            Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    internal void DeletePresetPreview(string? previewFile)
    {
        try { Store.Delete(previewFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogApplicationIssue("Preset preview delete", ex); }
    }

    internal void RemoveOrphanPresetPreviews(BetaShellConfig config)
    {
        string?[] kept = [.. config.ModPersistentSlots.Select(slot => slot.PreviewFile), .. config.ConfigurationProfiles.Select(profile => profile.PreviewFile)];
        _ = Task.Run(() =>
        {
            try
            {
                PreviewCleanupResult result = Store.RemoveOrphans(kept);
                // What it left in place, and why: a link it will not delete through, or a file it did not write.
                foreach (PreviewCleanupSkip skip in result.Skipped)
                    LogApplicationIssue("Preset preview cleanup", new IOException($"Left in place ({skip.Reason}): {skip.Path}" + (skip.Link is null ? string.Empty : $" · link {skip.Link}")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogApplicationIssue("Preset preview cleanup", ex); }
        });
    }
}
