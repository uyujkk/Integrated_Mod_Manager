using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ModFolderCopier.WinUI;

// Optional game screenshots attached to presets saved from the overlay. Screenshots can show a UID, a
// nickname or chat, so this stays off until the user agrees, and they never go into diagnostic reports.
public sealed partial class MainWindow
{
    private bool _capturePresetPreviews;
    private readonly PresetPreviewStore _presetPreviews = new(Path.Combine(AppContext.BaseDirectory, "snapshots", "preset-previews"));

    private void AddPresetPreviewSetting(StackPanel pane)
    {
        var toggle = new ToggleSwitch
        {
            OnContent = T("Common.On2"), OffContent = T("Common.Off2"),
            Header = T("PresetPreview.CaptureWhenSaving"), IsOn = _capturePresetPreviews
        };
        bool reverting = false;
        toggle.Toggled += async (_, _) =>
        {
            if (reverting) return;
            if (toggle.IsOn && !await ShowConfirmAsync(T("PresetPreview.PrivacyNotice"), T("PresetPreview.CaptureWhenSaving")))
            {
                reverting = true;
                toggle.IsOn = false;
                reverting = false;
                return;
            }
            _capturePresetPreviews = toggle.IsOn;
            SaveShellConfig();
        };
        pane.Children.Add(toggle);
    }

    // Taken before F10, which briefly disturbs the picture. A failure only means no preview.
    private async Task<CharacterFrame?> CapturePresetPreviewAsync()
    {
        if (!_capturePresetPreviews || _shortcutOverlay is not { } overlay
            || _shortcutTargetPicker?.SelectedItem is not ShortcutTarget target) return null;
        overlay.BeginCaptureExclusion();
        try
        {
            // Let the desktop compositor apply the exclusion, and the game regain focus after a dialog.
            await Task.Delay(150);
            return CharacterScreenCapture.Capture(target, IsEnglishUi);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Preset preview capture", ex);
            return null;
        }
        finally
        {
            if (ReferenceEquals(_shortcutOverlay, overlay)) overlay.EndCaptureExclusion();
        }
    }

    private async Task<string?> StorePresetPreviewAsync(string repositoryId, string presetId, CharacterFrame? frame)
    {
        if (frame is null || PresetPreviewStore.IsNearlyBlack(frame.Bgra)) return null;
        try
        {
            byte[] jpeg = await EncodePresetPreviewAsync(frame);
            return await Task.Run(() => _presetPreviews.Write(repositoryId, presetId, jpeg));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Preset preview save", ex);
            return null;
        }
    }

    private string PresetPreviewNote(string? previewFile) =>
        _capturePresetPreviews && previewFile is null ? T("PresetPreview.NotCaptured") : string.Empty;

    private static async Task<byte[]> EncodePresetPreviewAsync(CharacterFrame frame)
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

    private Image? CreatePresetPreviewImage(string? previewFile, double width, double height)
    {
        if (_presetPreviews.Resolve(previewFile) is not { } path || !File.Exists(path)) return null;
        return new Image
        {
            Source = new BitmapImage(new Uri(path)), Width = width, Height = height,
            Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private void DeletePresetPreview(string? previewFile)
    {
        try { _presetPreviews.Delete(previewFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogApplicationIssue("Preset preview delete", ex); }
    }

    private void RemoveOrphanPresetPreviews(BetaShellConfig config)
    {
        string?[] kept = [.. config.ModPersistentSlots.Select(slot => slot.PreviewFile), .. config.ConfigurationProfiles.Select(profile => profile.PreviewFile)];
        _ = Task.Run(() =>
        {
            try { _presetPreviews.RemoveOrphans(kept); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogApplicationIssue("Preset preview cleanup", ex); }
        });
    }
}
