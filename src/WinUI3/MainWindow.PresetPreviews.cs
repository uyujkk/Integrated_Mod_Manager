using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Optional game screenshots attached to presets saved from the overlay. Screenshots can show a UID, a
// nickname or chat, so this stays off until the user agrees, and they never go into diagnostic reports.
public sealed partial class MainWindow
{

    // In game: frame the character on the current slot's look, so its picture can be clicked to switch back to it later.
    private async Task FrameCurrentSlotPreviewAsync()
    {
        if (_savingOverlaySinglePreset || _services.Slots.IsRestoring || _services.Combinations.IsApplying || _shortcuts.SendInProgress
            || _overlay.Window is not { } overlay) return;
        ModPersistentSlot? slot = _services.Slots.Items.FirstOrDefault(item => item.Id == _overlay.CurrentSlotId);
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
            if ((_services.PresetPreviews.LastRegion ?? _services.PresetPreviews.DefaultRegion) is { } known
                && PresetPreviewStore.RegionToPixels(known, game.Width, game.Height) is { } remembered)
                start = remembered with { X = game.X + remembered.X, Y = game.Y + remembered.Y };
            var text = new RegionSelectionText(T("PresetPreview.FrameHint"), T("PresetPreview.LockFrameSize"), T("PresetPreview.UseAsDefaultRegion"), T("PresetPreview.Capture"), T("Common.Cancel"));
            RegionSelection? selection = await overlay.SelectRegionAsync(start, text, _services.PresetPreviews.FrameLocked, _services.PresetPreviews.DefaultRegion is not null, animate);
            if (selection is null || _services.IsClosing || !ReferenceEquals(_overlay.Window, overlay)) return;
            string? stored = null;
            PixelRect? clipped = PresetPreviewStore.ClipToGame(game, selection.Area);
            _services.PresetPreviews.FrameLocked = selection.LockSize;
            if (clipped is { } chosen)
            {
                _services.PresetPreviews.LastRegion = PresetPreviewStore.RegionOf(chosen, game.Width, game.Height);
                _services.PresetPreviews.DefaultRegion = selection.UseAsDefault ? _services.PresetPreviews.LastRegion : null;
            }
            SaveShellConfig();
            if (clipped is { } area
                && await _services.PresetPreviews.CaptureTargetFrameAsync(target, overlay) is { } frame
                && frame.Width == game.Width && frame.Height == game.Height)
            {
                byte[] pixels = PresetPreviewStore.CropBgra(frame.Bgra, frame.Width, area);
                if (!PresetPreviewStore.IsNearlyBlack(pixels))
                    stored = await _services.PresetPreviews.StorePresetPreviewAsync(slot.RepositoryId, slot.Id, new CharacterFrame(area.Width, area.Height, pixels));
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
            if (!_services.IsClosing) _shortcuts.ReportError(T("PresetPreview.CannotUse"));
        }
        finally
        {
            _savingOverlaySinglePreset = false;
            if (ReferenceEquals(_overlay.Window, overlay)) await overlay.LeaveLayerAsync(animate);
            if (!_services.IsClosing)
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
        if (_overlay.Window is not { } overlay || _savingOverlaySinglePreset || _services.Combinations.IsApplying) return;
        if (_services.Slots.IsRestoring || _shortcuts.SendInProgress)
        {
            _shortcuts.StatusText = T("HotReload.StillApplying");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        ModPersistentSlot[] slots = _services.Slots.OverlaySlots(out string? modPath);
        if (slots.Length == 0 || modPath is null) return;

        // A fixed window: more slots scroll inside it instead of widening it past the screen.
        const int cardWidth = 220, imageHeight = 260, spacing = 10, maxVisibleCards = 4;
        var chosen = new TaskCompletionSource<string?>();
        overlay.Closed += (_, _) => chosen.TrySetResult(null);
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        ModPersistentSlot? current = SlotService.CurrentPersistentSlot(slots);
        foreach (ModPersistentSlot slot in slots)
        {
            var card = new StackPanel { Spacing = 6, Width = cardWidth };
            if (_services.PresetPreviews.CreatePresetPreviewImage(slot.PreviewFile, cardWidth, imageHeight) is { } image)
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
        if (selected is not null && !_services.IsClosing) await _services.Slots.SwitchSlotFromOverlayAsync(selected, modPath);
        await leaving;
    }

}
