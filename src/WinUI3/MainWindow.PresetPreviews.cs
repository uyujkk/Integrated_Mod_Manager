using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    private bool _presetPreviewFrameLocked;
    private CharacterScreenRegion? _presetPreviewDefaultRegion;
    private CharacterScreenRegion? _presetPreviewLastRegion;
    private readonly PresetPreviewStore _presetPreviews = new(Path.Combine(AppContext.BaseDirectory, "snapshots", "preset-previews"));

    // Screenshots can show a UID, a nickname or chat: pictures are taken only after the user agrees.
    private async Task EnablePresetPreviewsAsync()
    {
        if (await ShowConfirmAsync(T("PresetPreview.PrivacyNotice"), T("PresetPreview.CaptureWhenSaving"), T("Dialogs.TurnOn")))
            _shortcuts.CapturePresetPreviews = true;
    }

    private void ClearPresetPreviewRegion()
    {
        _presetPreviewDefaultRegion = null;
        SaveShellConfig();
        StatusTextBlock.Text = T("PresetPreview.DefaultRegionCleared");
    }

    // A saved slot shows the same part of the game every time, once the user has set a default region.
    private CharacterFrame? CropToDefaultRegion(CharacterFrame? frame)
    {
        if (frame is null || _presetPreviewDefaultRegion is null
            || PresetPreviewStore.RegionToPixels(_presetPreviewDefaultRegion, frame.Width, frame.Height) is not { } area) return frame;
        byte[] pixels = PresetPreviewStore.CropBgra(frame.Bgra, frame.Width, area);
        return PresetPreviewStore.IsNearlyBlack(pixels) ? null : new CharacterFrame(area.Width, area.Height, pixels);
    }

    // Taken before F10, which briefly disturbs the picture. A failure only means no preview.
    private async Task<CharacterFrame?> CapturePresetPreviewAsync()
    {
        if (!_shortcuts.CapturePresetPreviews || _overlay.Window is not { } overlay
            || _shortcuts.SelectedTarget is not ShortcutTarget target) return null;
        return await CaptureTargetFrameAsync(target, overlay);
    }

    // Null when the game cannot be captured: not in front, partly off screen, or a black exclusive full-screen frame.
    private async Task<CharacterFrame?> CaptureTargetFrameAsync(ShortcutTarget target, ShortcutOverlayWindow? overlay)
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

    // In game: frame the character on the current slot's look, so its picture can be clicked to switch back to it later.
    private async Task FrameCurrentSlotPreviewAsync()
    {
        if (_savingOverlaySinglePreset || _restoringPersistentRuntime || _applyingCombinationProfile || _shortcuts.SendInProgress
            || _overlay.Window is not { } overlay) return;
        ModPersistentSlot? slot = _modPersistentSlots.FirstOrDefault(item => item.Id == _overlay.CurrentSlotId);
        if (slot is null)
        {
            _shortcuts.StatusText = T("PresetPreview.SwitchToASlotFirst");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        if (_shortcuts.SelectedTarget is not ShortcutTarget target
            || CharacterScreenCapture.ClientScreenRect(target.Handle) is not { } game)
        {
            _shortcuts.StatusText = T("PresetPreview.SelectGameWindow");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }

        _savingOverlaySinglePreset = true;
        bool animate = !_reduceMotion;
        try
        {
            // Reopen where the last frame was. Otherwise a character stands taller than wide: start from the middle of the game.
            int width = game.Width * 3 / 10, height = game.Height * 7 / 10;
            PixelRect start = new(game.X + (game.Width - width) / 2, game.Y + (game.Height - height) / 2, width, height);
            if ((_presetPreviewLastRegion ?? _presetPreviewDefaultRegion) is { } known
                && PresetPreviewStore.RegionToPixels(known, game.Width, game.Height) is { } remembered)
                start = remembered with { X = game.X + remembered.X, Y = game.Y + remembered.Y };
            var text = new RegionSelectionText(T("PresetPreview.FrameHint"), T("PresetPreview.LockFrameSize"), T("PresetPreview.UseAsDefaultRegion"), T("PresetPreview.Capture"), T("Common.Cancel"));
            RegionSelection? selection = await overlay.SelectRegionAsync(start, text, _presetPreviewFrameLocked, _presetPreviewDefaultRegion is not null, animate);
            if (selection is null || _onlineView._onlineWindowClosed || !ReferenceEquals(_overlay.Window, overlay)) return;
            string? stored = null;
            PixelRect? clipped = PresetPreviewStore.ClipToGame(game, selection.Area);
            _presetPreviewFrameLocked = selection.LockSize;
            if (clipped is { } chosen)
            {
                _presetPreviewLastRegion = PresetPreviewStore.RegionOf(chosen, game.Width, game.Height);
                _presetPreviewDefaultRegion = selection.UseAsDefault ? _presetPreviewLastRegion : null;
            }
            SaveShellConfig();
            if (clipped is { } area
                && await CaptureTargetFrameAsync(target, overlay) is { } frame
                && frame.Width == game.Width && frame.Height == game.Height)
            {
                byte[] pixels = PresetPreviewStore.CropBgra(frame.Bgra, frame.Width, area);
                if (!PresetPreviewStore.IsNearlyBlack(pixels))
                    stored = await StorePresetPreviewAsync(slot.RepositoryId, slot.Id, new CharacterFrame(area.Width, area.Height, pixels));
            }
            if (stored is not null)
            {
                slot.PreviewFile = stored;
                SaveShellConfig();
            }
            _shortcuts.StatusText = stored is null ? T("PresetPreview.CannotUse") : T("PresetPreview.Framed", slot.Name);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Preset preview framing", ex);
            if (!_onlineView._onlineWindowClosed) _shortcuts.StatusText = T("PresetPreview.CannotUse");
        }
        finally
        {
            _savingOverlaySinglePreset = false;
            if (ReferenceEquals(_overlay.Window, overlay)) await overlay.LeaveLayerAsync(animate);
            if (!_onlineView._onlineWindowClosed)
            {
                _overlay.InvalidateSlots();
                RefreshShortcutOverlay(bindingsChanged: true);
                RefreshPersistentSlotControls();
            }
        }
    }

    // In game: the slots of the overlay's Mod as a row of pictures in the middle of the screen. Choosing one switches to it.
    private async Task ShowOverlaySlotGalleryAsync()
    {
        if (_overlay.Window is not { } overlay || _savingOverlaySinglePreset || _applyingCombinationProfile) return;
        if (_restoringPersistentRuntime || _shortcuts.SendInProgress)
        {
            _shortcuts.StatusText = T("HotReload.StillApplying");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        ModPersistentSlot[] slots = OverlaySlots(out string? modPath);
        if (slots.Length == 0 || modPath is null) return;

        // A fixed window: more slots scroll inside it instead of widening it past the screen.
        const int cardWidth = 220, imageHeight = 260, spacing = 10, maxVisibleCards = 4;
        var chosen = new TaskCompletionSource<string?>();
        overlay.Closed += (_, _) => chosen.TrySetResult(null);
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        ModPersistentSlot? current = CurrentPersistentSlot(slots);
        foreach (ModPersistentSlot slot in slots)
        {
            var card = new StackPanel { Spacing = 6, Width = cardWidth };
            if (CreatePresetPreviewImage(slot.PreviewFile, cardWidth, imageHeight) is { } image)
            {
                image.HorizontalAlignment = HorizontalAlignment.Center;
                card.Children.Add(image);
            }
            else
            {
                card.Children.Add(new TextBlock
                {
                    Text = T("PresetPreview.NoPicture"), Height = imageHeight, Foreground = _overlay.TextBrush, TextAlignment = TextAlignment.Center,
                    Padding = new Thickness(0, imageHeight / 2 - 10, 0, 0)
                });
            }
            card.Children.Add(new TextBlock
            {
                Text = ReferenceEquals(slot, current) ? "● " + slot.Name : slot.Name, Foreground = _overlay.TextBrush,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });
            var button = new Button { Content = card, AllowFocusOnInteraction = false, Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(button, slot.Name);
            string id = slot.Id;
            button.Click += (_, _) => chosen.TrySetResult(id);
            cards.Children.Add(button);
        }
        var scroll = new ScrollViewer
        {
            Content = cards, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        // A mouse wheel only scrolls vertically by default, which would leave a long row stuck.
        scroll.PointerWheelChanged += (_, e) =>
        {
            scroll.ChangeView(scroll.HorizontalOffset - e.GetCurrentPoint(scroll).Properties.MouseWheelDelta, null, null);
            e.Handled = true;
        };
        var cancel = new Button { Content = T("Common.Cancel"), Foreground = _overlay.TextBrush, AllowFocusOnInteraction = false, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => chosen.TrySetResult(null);
        var layer = new Grid { RowSpacing = 8, Padding = new Thickness(14, 0, 14, 12) };
        layer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _overlay.ApplyElementResources(layer);
        Grid.SetRow(cancel, 1);
        layer.Children.Add(scroll);
        layer.Children.Add(cancel);

        bool animate = !_reduceMotion;
        // Half a card showing at the edge tells the user the row goes on.
        double visibleCards = slots.Length > maxVisibleCards ? maxVisibleCards + .5 : slots.Length;
        int width = Math.Max(420, (int)(visibleCards * (cardWidth + 16 + spacing)) + 48);
        _savingOverlaySinglePreset = true;
        string? selected = null;
        try
        {
            await overlay.EnterLayerAsync(layer, overlay.CenteredOnScreen(width, imageHeight + 150), null, animate);
            selected = await chosen.Task;
        }
        finally { _savingOverlaySinglePreset = false; }
        // Switch while the window goes back: waiting for the animation would only delay the game.
        Task leaving = ReferenceEquals(_overlay.Window, overlay) ? overlay.LeaveLayerAsync(animate) : Task.CompletedTask;
        if (selected is not null && !_onlineView._onlineWindowClosed) await SwitchSlotFromOverlayAsync(selected, modPath);
        await leaving;
    }

    // The buttons under a selected preset in the main window: picture, then choosing or clearing it.
    private FrameworkElement CreatePresetPreviewEditor(string repositoryId, string presetId, string? previewFile, Action<string?> assign, Action refresh)
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
            Activate();
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
            IsEnabled = !_restoringPersistentRuntime && !_applyingCombinationProfile
        });
        return panel;
    }

    private async Task ReplacePresetPreviewAsync(string repositoryId, string presetId, CharacterFrame? frame, string? previous, Action<string?> assign, Action refresh)
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

    private static async Task<CharacterFrame> ReadPresetPreviewFileAsync(string path)
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

    private object PresetPreviewLabel(string text, string? previewFile)
    {
        if (CreatePresetPreviewImage(previewFile, 64, 36) is not { } image) return text;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(image);
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private async Task<string?> StorePresetPreviewAsync(string repositoryId, string presetId, CharacterFrame? frame)
    {
        if (frame is null) return null;
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
        _shortcuts.CapturePresetPreviews && previewFile is null ? T("PresetPreview.NotCaptured") : string.Empty;

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
            try
            {
                PreviewCleanupResult result = _presetPreviews.RemoveOrphans(kept);
                // What it left in place, and why: a link it will not delete through, or a file it did not write.
                foreach (PreviewCleanupSkip skip in result.Skipped)
                    LogApplicationIssue("Preset preview cleanup", new IOException($"Left in place ({skip.Reason}): {skip.Path}" + (skip.Link is null ? string.Empty : $" · link {skip.Link}")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogApplicationIssue("Preset preview cleanup", ex); }
        });
    }
}
