using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
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
    private ToggleSwitch? _characterFollowToggle;
    private TextBlock? _characterFollowStatus;
    private System.Threading.Timer? _characterFollowTimer;
    private bool _characterCaptureBusy;
    private bool _characterAutoSelecting;
    private readonly Dictionary<string, string> _characterPreferredMods = new(StringComparer.OrdinalIgnoreCase);

    private void BuildCharacterFollowControls(StackPanel pane)
    {
        pane.Children.Add(new TextBlock { Text = T("CharacterFollow.FollowInGameCharacterLocalMatching"), FontSize = 18 });
        pane.Children.Add(new TextBlock
        {
            Text = T("CharacterFollow.SelectAHUDRegionThatChanges"),
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(ShortcutButton(T("CharacterFollow.SelectHUDRegion"), () => _ = SelectCharacterRegionAsync()));
        buttons.Children.Add(ShortcutButton(T("CharacterFollow.SaveCharacterReference"), () => _ = SaveCharacterReferenceAsync()));
        pane.Children.Add(buttons);
        _characterFollowToggle = LocalizedToggle(T("CharacterFollow.AutomaticallyFollowCharacterShortcuts"));
        _characterFollowToggle.Toggled += (_, _) =>
        {
            _characterMatcher.Reset();
            if (_characterFollowToggle.IsOn && (_characterProfile.Region?.IsValid != true || _characterProfile.Templates.Count == 0))
            {
                _characterFollowToggle.IsOn = false;
                _characterFollowStatus!.Text = T("CharacterFollow.SelectARegionAndSaveReferences");
                return;
            }
            _characterFollowStatus!.Text = _characterFollowToggle.IsOn
                ? T("CharacterFollow.WaitingForForegroundGameSwitchesAfter")
                : T("CharacterFollow.AutomaticFollowingPaused");
        };
        pane.Children.Add(_characterFollowToggle);
        // Updated every recognition tick; a fixed height keeps the shortcut list below it still.
        _characterFollowStatus = FixedStatusText(lines: 2, fontSize: 14);
        _characterFollowStatus.Text = T("CharacterFollow.NotConfiguredReferencesRemainLocal");
        pane.Children.Add(_characterFollowStatus);
        _characterFollowTimer = new System.Threading.Timer(_ =>
            DispatcherQueue.TryEnqueue(() => _ = ObserveCharacterAsync()), null, 700, 700);
    }

    private string? SelectedShortcutRole()
    {
        if (_shortcutModPicker?.SelectedItem is not ShortcutModOption mod) return null;
        return mod.Label.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
    }

    private void LoadCharacterFollowProfile(string repository)
    {
        if (_characterFollowToggle is null) return;
        _characterFollowToggle.IsOn = false;
        _characterMatcher.Reset();
        _characterProfile = new();
        _characterPreferredMods.Clear();
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repository.ToUpperInvariant())))[..24];
        _characterProfilePath = Path.Combine(AppContext.BaseDirectory, "character-references", name + ".json");
        try
        {
            if (File.Exists(_characterProfilePath) && new FileInfo(_characterProfilePath).Length < 2 * 1024 * 1024)
                _characterProfile = JsonSerializer.Deserialize<CharacterFollowProfile>(File.ReadAllText(_characterProfilePath)) ?? new();
            _characterProfile.Templates ??= new();
            _characterProfile.PreferredMods ??= new();
            foreach (var pair in _characterProfile.PreferredMods) _characterPreferredMods[pair.Key] = pair.Value;
            _characterFollowStatus!.Text = T("CharacterFollow.LoadedCharacterReferencesAutoFollowStarts", _characterProfile.Templates.Count);
        }
        catch (Exception ex) { _characterFollowStatus!.Text = ex.Message; _characterProfile = new(); }
    }

    private void SaveCharacterFollowProfile()
    {
        if (_characterProfilePath is null) throw new InvalidOperationException(T("Common.SelectARepositoryFirst"));
        Directory.CreateDirectory(Path.GetDirectoryName(_characterProfilePath)!);
        string temporary = _characterProfilePath + ".tmp";
        _characterProfile.PreferredMods = new(_characterPreferredMods, StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(temporary, JsonSerializer.Serialize(_characterProfile));
        File.Move(temporary, _characterProfilePath, true);
    }

    private async Task<CharacterFrame> CaptureCharacterReferenceAsync()
    {
        if (_shortcutTargetPicker?.SelectedItem is not ShortcutTarget target) throw new InvalidOperationException(T("CharacterFollow.SelectAGameWindow"));
        _shortcutOverlay?.AppWindow.Hide();
        if (!ShortcutInputService.FocusTarget(target)) throw new InvalidOperationException(T("CharacterFollow.CouldNotFocusTheGameWindow"));
        await Task.Delay(300);
        return await Task.Run(() => CharacterScreenCapture.Capture(target, IsEnglishUi));
    }

    private async Task SelectCharacterRegionAsync()
    {
        if (_characterCaptureBusy) return;
        _characterCaptureBusy = true;
        _characterFollowToggle!.IsOn = false;
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
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && chosen?.IsValid == true)
            {
                _characterProfile.Region = chosen;
                _characterProfile.Templates.Clear(); // Different geometry invalidates all old references.
                SaveCharacterFollowProfile();
                _characterFollowStatus!.Text = T("CharacterFollow.RegionSavedSaveReferencesForEach");
            }
        }
        catch (Exception ex) { if (!_onlineWindowClosed) _characterFollowStatus!.Text = ex.Message; }
        finally { _characterCaptureBusy = false; if (!_onlineWindowClosed && _shortcutOverlayVisible) _shortcutOverlay?.ShowPanel(); }
    }

    private async Task SaveCharacterReferenceAsync()
    {
        if (_characterCaptureBusy) return;
        _characterCaptureBusy = true;
        _characterFollowToggle!.IsOn = false;
        try
        {
            string role = SelectedShortcutRole() ?? throw new InvalidOperationException(T("CharacterFollow.SelectAModForThisCharacter"));
            CharacterScreenRegion region = _characterProfile.Region ?? throw new InvalidOperationException(T("CharacterFollow.SelectAHUDRegionFirst"));
            CharacterFrame frame = await CaptureCharacterReferenceAsync();
            if (_onlineWindowClosed) return;
            byte[] feature = CharacterImageMatcher.Extract(frame.Bgra, frame.Width, frame.Height, region);
            if (!CharacterImageMatcher.HasDetail(feature)) throw new InvalidOperationException(T("CharacterFollow.TheRegionIsTooUniformOr"));
            _characterProfile.Templates[role] = feature;
            if (_shortcutModPicker?.SelectedItem is ShortcutModOption selected) _characterPreferredMods[role] = selected.Path;
            SaveCharacterFollowProfile();
            Activate();
            _characterFollowStatus!.Text = T("CharacterFollow.SavedReferenceForTotal", role, _characterProfile.Templates.Count);
        }
        catch (Exception ex) { if (!_onlineWindowClosed) _characterFollowStatus!.Text = ex.Message; }
        finally { _characterCaptureBusy = false; if (!_onlineWindowClosed && _shortcutOverlayVisible) _shortcutOverlay?.ShowPanel(); }
    }

    private async Task ObserveCharacterAsync()
    {
        if (_characterCaptureBusy || _onlineWindowClosed || _characterFollowToggle?.IsOn != true
            || _shortcutSendInProgress || _shortcutTargetPicker?.SelectedItem is not ShortcutTarget target
            || _characterProfile.Region is not CharacterScreenRegion region) return;
        if (!ShortcutInputService.IsTargetForeground(target))
        {
            _characterMatcher.Reset();
            _characterFollowStatus!.Text = T("CharacterFollow.WaitingForTheSelectedGameTo");
            return;
        }
        _characterCaptureBusy = true;
        try
        {
            // If our panel covers the chosen HUD region, never match the panel itself.
            if (_shortcutOverlayVisible && _shortcutOverlay is not null
                && CharacterScreenCapture.OverlapsRegion(WinRT.Interop.WindowNative.GetWindowHandle(_shortcutOverlay), target, region))
            { _characterMatcher.Reset(); _characterFollowStatus!.Text = T("CharacterFollow.MoveTheOverlayAwayFromThe"); return; }
            CharacterFrame frame = await Task.Run(() => CharacterScreenCapture.Capture(target, IsEnglishUi));
            if (_onlineWindowClosed || _characterFollowToggle?.IsOn != true) return;
            if (_shortcutTargetPicker?.SelectedItem is not ShortcutTarget current
                || current.Handle != target.Handle || current.StartTicks != target.StartTicks)
            { _characterMatcher.Reset(); return; }
            byte[] feature = CharacterImageMatcher.Extract(frame.Bgra, frame.Width, frame.Height, region);
            string? role = _characterMatcher.Observe(feature, _characterProfile.Templates, out double confidence);
            _characterFollowStatus!.Text = role is null
                ? T("CharacterFollow.WaitingForStableRecognition", confidence)
                : T("CharacterFollow.CurrentCharacter", role, confidence);
            if (role is null || role.Equals(SelectedShortcutRole(), StringComparison.OrdinalIgnoreCase)) return;
            var candidates = _shortcutModPicker!.ItemsSource as ShortcutModOption[] ?? [];
            var matching = candidates.Where(p => p.Label.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0].Equals(role, StringComparison.OrdinalIgnoreCase)).ToArray();
            // Only an enabled Mod can react in game; prefer those when the role has any.
            var enabled = matching.Where(IsShortcutModDeployed).ToArray();
            if (enabled.Length > 0) matching = enabled;
            var selected = matching.FirstOrDefault(p => _characterPreferredMods.TryGetValue(role, out string? preferred) && p.Path == preferred)
                ?? (matching.Length == 1 ? matching[0] : null);
            if (selected is null && matching.Length > 1)
                _characterFollowStatus.Text = T("CharacterFollow.HasMultipleModsChooseOneManually", role);
            if (selected is null) return;
            _characterAutoSelecting = true;
            try { _shortcutModPicker.SelectedItem = selected; }
            finally { _characterAutoSelecting = false; }
        }
        catch (Exception ex) { _characterMatcher.Reset(); if (!_onlineWindowClosed) _characterFollowStatus!.Text = ex.Message; }
        finally { _characterCaptureBusy = false; }
    }
}
