using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = System.IO.Path;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private sealed class CharacterFollowProfile
    {
        public CharacterScreenRegion? Region { get; set; }
        public Dictionary<string, byte[]> Templates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PreferredMods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private readonly CharacterImageMatcher _characterMatcher = new();
    private CharacterFollowProfile _characterProfile = new();
    private string? _characterProfilePath;
    private System.Threading.Timer? _characterFollowTimer;
    private bool _characterCaptureBusy;
    private bool _characterAutoSelecting;
    private readonly Dictionary<string, string> _characterPreferredMods = new(StringComparer.OrdinalIgnoreCase);

    // Character follow runs once the Shortcuts page or the overlay was used; the page shows its switch and state.
    private void StartCharacterFollow()
    {
        if (_characterFollowTimer is not null) return;
        _shortcuts.CharacterFollowStatus = T("CharacterFollow.NotConfiguredReferencesRemainLocal");
        UpdateCharacterFollowSummary();
        _characterFollowTimer = new System.Threading.Timer(_ =>
            DispatcherQueue.TryEnqueue(() => _ = ObserveCharacterAsync()), null, 700, 700);
    }

    // The switch: turning it on needs a region and references, else it turns itself back off.
    private void OnCharacterFollowToggled()
    {
        _characterMatcher.Reset();
        if (_shortcuts.CharacterFollowEnabled && (_characterProfile.Region?.IsValid != true || _characterProfile.Templates.Count == 0))
        {
            _shortcuts.CharacterFollowEnabled = false;
            _shortcuts.CharacterFollowStatus = T("CharacterFollow.SelectARegionAndSaveReferences");
            return;
        }
        _shortcuts.CharacterFollowStatus = _shortcuts.CharacterFollowEnabled
            ? T("CharacterFollow.WaitingForForegroundGameSwitchesAfter")
            : T("CharacterFollow.AutomaticFollowingPaused");
        UpdateCharacterFollowSummary();
    }

    // The one line shown on the folded row.
    private void UpdateCharacterFollowSummary()
    {
        _shortcuts.CharacterFollowSummary = _shortcuts.CharacterFollowEnabled
            ? T("CharacterFollow.SummaryFollowing")
            : _characterProfile.Region?.IsValid == true && _characterProfile.Templates.Count > 0
                ? T("CharacterFollow.SummaryReady", _characterProfile.Templates.Count)
                : T("CharacterFollow.SummaryNotSetUp");
    }

    private string? SelectedShortcutRole()
    {
        if (_shortcuts.SelectedMod is not ShortcutModOption mod) return null;
        return mod.Label.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
    }

    private void LoadCharacterFollowProfile(string repository)
    {
        if (_characterFollowTimer is null) return;
        _shortcuts.CharacterFollowEnabled = false;
        _characterMatcher.Reset();
        _characterProfile = new();
        _characterPreferredMods.Clear();
        _characterProfilePath = CharacterFollowProfilePath(repository);
        try
        {
            if (File.Exists(_characterProfilePath) && new FileInfo(_characterProfilePath).Length < 2 * 1024 * 1024)
                _characterProfile = JsonSerializer.Deserialize<CharacterFollowProfile>(File.ReadAllText(_characterProfilePath)) ?? new();
            _characterProfile.Templates ??= new();
            _characterProfile.PreferredMods ??= new();
            foreach (var pair in _characterProfile.PreferredMods) _characterPreferredMods[pair.Key] = pair.Value;
            _shortcuts.CharacterFollowStatus = T("CharacterFollow.LoadedCharacterReferencesAutoFollowStarts", _characterProfile.Templates.Count);
        }
        catch (Exception ex) { _shortcuts.CharacterFollowStatus = ex.Message; _characterProfile = new(); }
        UpdateCharacterFollowSummary();
    }

    private void SaveCharacterFollowProfile()
    {
        if (_characterProfilePath is null) throw new InvalidOperationException(T("Common.SelectARepositoryFirst"));
        Directory.CreateDirectory(Path.GetDirectoryName(_characterProfilePath)!);
        string temporary = _characterProfilePath + ".tmp";
        _characterProfile.PreferredMods = new(_characterPreferredMods, StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(temporary, JsonSerializer.Serialize(_characterProfile));
        File.Move(temporary, _characterProfilePath, true);
        UpdateCharacterFollowSummary();
    }

    private async Task<CharacterFrame> CaptureCharacterReferenceAsync()
    {
        if (_shortcuts.SelectedTarget is not ShortcutTarget target) throw new InvalidOperationException(T("CharacterFollow.SelectAGameWindow"));
        _overlay.Window?.AppWindow.Hide();
        if (!ShortcutInputService.FocusTarget(target)) throw new InvalidOperationException(T("CharacterFollow.CouldNotFocusTheGameWindow"));
        await Task.Delay(300);
        return await Task.Run(() => CharacterScreenCapture.Capture(target, _services.Language.Current));
    }

    private async Task SelectCharacterRegionAsync()
    {
        if (_characterCaptureBusy) return;
        _characterCaptureBusy = true;
        _shortcuts.CharacterFollowEnabled = false;
        try
        {
            CharacterFrame frame = await CaptureCharacterReferenceAsync();
            if (_onlineWindowClosed) return;
            Activate();
            var bitmap = new WriteableBitmap(frame.Width, frame.Height);
            using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(frame.Bgra, 0, frame.Bgra.Length);
            double scale = Math.Min(800d / frame.Width, 430d / frame.Height);
            var canvas = new Canvas { Width = frame.Width * scale, Height = frame.Height * scale };
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
                XamlRoot = RootGrid.XamlRoot, Title = T("CharacterFollow.DragOverTheActiveCharacterPortrait"),
                Content = canvas, PrimaryButtonText = T("CharacterFollow.SaveRegion"), CloseButtonText = T("Common.Cancel")
            };
            dialog.Resources["ContentDialogMaxWidth"] = 900d;
            dialog.PrimaryButtonClick += (_, e) => { if (chosen?.IsValid != true) e.Cancel = true; };
            if (await _services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary && chosen?.IsValid == true)
            {
                _characterProfile.Region = chosen;
                _characterProfile.Templates.Clear(); // Different geometry invalidates all old references.
                SaveCharacterFollowProfile();
                _shortcuts.CharacterFollowStatus = T("CharacterFollow.RegionSavedSaveReferencesForEach");
            }
        }
        catch (Exception ex) { if (!_onlineWindowClosed) _shortcuts.CharacterFollowStatus = ex.Message; }
        finally { _characterCaptureBusy = false; if (!_onlineWindowClosed && _shortcuts.OverlayVisible) _overlay.Window?.ShowPanel(); }
    }

    private async Task SaveCharacterReferenceAsync()
    {
        if (_characterCaptureBusy) return;
        _characterCaptureBusy = true;
        _shortcuts.CharacterFollowEnabled = false;
        try
        {
            string role = SelectedShortcutRole() ?? throw new InvalidOperationException(T("CharacterFollow.SelectAModForThisCharacter"));
            CharacterScreenRegion region = _characterProfile.Region ?? throw new InvalidOperationException(T("CharacterFollow.SelectAHUDRegionFirst"));
            CharacterFrame frame = await CaptureCharacterReferenceAsync();
            if (_onlineWindowClosed) return;
            byte[] feature = CharacterImageMatcher.Extract(frame.Bgra, frame.Width, frame.Height, region);
            if (!CharacterImageMatcher.HasDetail(feature)) throw new InvalidOperationException(T("CharacterFollow.TheRegionIsTooUniformOr"));
            _characterProfile.Templates[role] = feature;
            if (_shortcuts.SelectedMod is ShortcutModOption selected) _characterPreferredMods[role] = selected.Path;
            SaveCharacterFollowProfile();
            Activate();
            _shortcuts.CharacterFollowStatus = T("CharacterFollow.SavedReferenceForTotal", role, _characterProfile.Templates.Count);
        }
        catch (Exception ex) { if (!_onlineWindowClosed) _shortcuts.CharacterFollowStatus = ex.Message; }
        finally { _characterCaptureBusy = false; if (!_onlineWindowClosed && _shortcuts.OverlayVisible) _overlay.Window?.ShowPanel(); }
    }

    private async Task ObserveCharacterAsync()
    {
        if (_characterCaptureBusy || _onlineWindowClosed || !_shortcuts.CharacterFollowEnabled
            || _shortcuts.SendInProgress || _shortcuts.SelectedTarget is not ShortcutTarget target
            || _characterProfile.Region is not CharacterScreenRegion region) return;
        if (!ShortcutInputService.IsTargetForeground(target))
        {
            _characterMatcher.Reset();
            _shortcuts.CharacterFollowStatus = T("CharacterFollow.WaitingForTheSelectedGameTo");
            return;
        }
        _characterCaptureBusy = true;
        try
        {
            // If our panel covers the chosen HUD region, never match the panel itself.
            if (_shortcuts.OverlayVisible && _overlay.Window is not null
                && CharacterScreenCapture.OverlapsRegion(WinRT.Interop.WindowNative.GetWindowHandle(_overlay.Window), target, region))
            { _characterMatcher.Reset(); _shortcuts.CharacterFollowStatus = T("CharacterFollow.MoveTheOverlayAwayFromThe"); return; }
            CharacterFrame frame = await Task.Run(() => CharacterScreenCapture.Capture(target, _services.Language.Current));
            if (_onlineWindowClosed || !_shortcuts.CharacterFollowEnabled) return;
            if (_shortcuts.SelectedTarget is not ShortcutTarget current
                || current.Handle != target.Handle || current.StartTicks != target.StartTicks)
            { _characterMatcher.Reset(); return; }
            byte[] feature = CharacterImageMatcher.Extract(frame.Bgra, frame.Width, frame.Height, region);
            string? role = _characterMatcher.Observe(feature, _characterProfile.Templates, out double confidence);
            _shortcuts.CharacterFollowStatus = role is null
                ? T("CharacterFollow.WaitingForStableRecognition", confidence)
                : T("CharacterFollow.CurrentCharacter", role, confidence);
            if (role is null || role.Equals(SelectedShortcutRole(), StringComparison.OrdinalIgnoreCase)) return;
            var candidates = _shortcuts.ModOptions as ShortcutModOption[] ?? [];
            var matching = candidates.Where(p => p.Label.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0].Equals(role, StringComparison.OrdinalIgnoreCase)).ToArray();
            // Only an enabled Mod can react in game; prefer those when the role has any.
            var enabled = matching.Where(IsShortcutModDeployed).ToArray();
            if (enabled.Length > 0) matching = enabled;
            var selected = matching.FirstOrDefault(p => _characterPreferredMods.TryGetValue(role, out string? preferred) && p.Path == preferred)
                ?? (matching.Length == 1 ? matching[0] : null);
            if (selected is null && matching.Length > 1)
                _shortcuts.CharacterFollowStatus = T("CharacterFollow.HasMultipleModsChooseOneManually", role);
            if (selected is null) return;
            _characterAutoSelecting = true;
            try { _shortcuts.SelectedMod = selected; }
            finally { _characterAutoSelecting = false; }
        }
        catch (Exception ex) { _characterMatcher.Reset(); if (!_onlineWindowClosed) _shortcuts.CharacterFollowStatus = ex.Message; }
        finally { _characterCaptureBusy = false; }
    }
}
