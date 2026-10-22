using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core;
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
        pane.Children.Add(new TextBlock { Text = L("跟随游戏角色（本地识别）", "Follow in-game character (local matching)"), FontSize = 18 });
        pane.Children.Add(new TextBlock
        {
            Text = L("首次框选只随当前出场角色变化的头像／名称区域，再逐个切换角色并保存参考图。不要框选固定队伍列表。",
                "Select a HUD region that changes only with the active character, then save a reference for each character. Avoid the fixed party list."),
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(ShortcutButton(L("框选识别区域", "Select HUD region"), () => _ = SelectCharacterRegionAsync()));
        buttons.Children.Add(ShortcutButton(L("保存当前角色参考图", "Save character reference"), () => _ = SaveCharacterReferenceAsync()));
        pane.Children.Add(buttons);
        _characterFollowToggle = new ToggleSwitch { Header = L("自动切换角色快捷键", "Automatically follow character shortcuts") };
        _characterFollowToggle.Toggled += (_, _) =>
        {
            _characterMatcher.Reset();
            if (_characterFollowToggle.IsOn && (_characterProfile.Region?.IsValid != true || _characterProfile.Templates.Count == 0))
            {
                _characterFollowToggle.IsOn = false;
                _characterFollowStatus!.Text = L("请先设置区域并保存角色参考图。", "Select a region and save references first.");
                return;
            }
            _characterFollowStatus!.Text = _characterFollowToggle.IsOn
                ? L("等待目标游戏前台画面；连续 3 次可靠匹配后切换。", "Waiting for foreground game; switches after 3 reliable matches.")
                : L("自动跟随已暂停。", "Automatic following paused.");
        };
        pane.Children.Add(_characterFollowToggle);
        _characterFollowStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = L("未配置；参考图只保存在本机。", "Not configured; references remain local.") };
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
            _characterFollowStatus!.Text = L($"已加载 {_characterProfile.Templates.Count} 个角色参考图，自动跟随默认关闭。", $"Loaded {_characterProfile.Templates.Count} character references. Auto-follow starts disabled.");
        }
        catch (Exception ex) { _characterFollowStatus!.Text = ex.Message; _characterProfile = new(); }
    }

    private void SaveCharacterFollowProfile()
    {
        if (_characterProfilePath is null) throw new InvalidOperationException("Select a repository first.");
        Directory.CreateDirectory(Path.GetDirectoryName(_characterProfilePath)!);
        string temporary = _characterProfilePath + ".tmp";
        _characterProfile.PreferredMods = new(_characterPreferredMods, StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(temporary, JsonSerializer.Serialize(_characterProfile));
        File.Move(temporary, _characterProfilePath, true);
    }

    private async Task<CharacterFrame> CaptureCharacterReferenceAsync()
    {
        if (_shortcutTargetPicker?.SelectedItem is not ShortcutTarget target) throw new InvalidOperationException(L("请选择游戏窗口。", "Select a game window."));
        _shortcutOverlay?.AppWindow.Hide();
        if (!ShortcutInputService.FocusTarget(target)) throw new InvalidOperationException(L("无法切换到游戏窗口。", "Could not focus the game window."));
        await Task.Delay(300);
        return await Task.Run(() => CharacterScreenCapture.Capture(target));
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
                XamlRoot = RootGrid.XamlRoot, Title = L("拖动框选当前角色头像或名称", "Drag over the active character portrait or name"),
                Content = canvas, PrimaryButtonText = L("保存区域", "Save region"), CloseButtonText = L("取消", "Cancel")
            };
            dialog.Resources["ContentDialogMaxWidth"] = 900d;
            dialog.PrimaryButtonClick += (_, e) => { if (chosen?.IsValid != true) e.Cancel = true; };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && chosen?.IsValid == true)
            {
                _characterProfile.Region = chosen;
                _characterProfile.Templates.Clear(); // Different geometry invalidates all old references.
                SaveCharacterFollowProfile();
                _characterFollowStatus!.Text = L("区域已保存，请为各角色保存参考图；旧区域参考已清除。", "Region saved. Save references for each character; previous-region references were cleared.");
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
            string role = SelectedShortcutRole() ?? throw new InvalidOperationException(L("请选择对应角色的 Mod。", "Select a Mod for this character."));
            CharacterScreenRegion region = _characterProfile.Region ?? throw new InvalidOperationException(L("请先框选识别区域。", "Select a HUD region first."));
            CharacterFrame frame = await CaptureCharacterReferenceAsync();
            if (_onlineWindowClosed) return;
            byte[] feature = CharacterImageMatcher.Extract(frame.Bgra, frame.Width, frame.Height, region);
            if (!CharacterImageMatcher.HasDetail(feature)) throw new InvalidOperationException(L("该区域过于单一或画面为空，请重新框选。", "The region is too uniform or blank; choose another region."));
            _characterProfile.Templates[role] = feature;
            if (_shortcutModPicker?.SelectedItem is ShortcutModOption selected) _characterPreferredMods[role] = selected.Path;
            SaveCharacterFollowProfile();
            Activate();
            _characterFollowStatus!.Text = L($"已保存 {role} 的参考图（共 {_characterProfile.Templates.Count} 个）。", $"Saved reference for {role} ({_characterProfile.Templates.Count} total).");
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
            _characterFollowStatus!.Text = L("等待目标游戏回到前台。", "Waiting for the selected game to return to the foreground.");
            return;
        }
        _characterCaptureBusy = true;
        try
        {
            // If our panel covers the chosen HUD region, never match the panel itself.
            if (_shortcutOverlayVisible && _shortcutOverlay is not null
                && CharacterScreenCapture.OverlapsRegion(WinRT.Interop.WindowNative.GetWindowHandle(_shortcutOverlay), target, region))
            { _characterMatcher.Reset(); _characterFollowStatus!.Text = L("浮窗遮挡识别区域，请移动浮窗。", "Move the overlay away from the recognition region."); return; }
            CharacterFrame frame = await Task.Run(() => CharacterScreenCapture.Capture(target));
            if (_onlineWindowClosed || _characterFollowToggle?.IsOn != true) return;
            if (_shortcutTargetPicker?.SelectedItem is not ShortcutTarget current
                || current.Handle != target.Handle || current.StartTicks != target.StartTicks)
            { _characterMatcher.Reset(); return; }
            byte[] feature = CharacterImageMatcher.Extract(frame.Bgra, frame.Width, frame.Height, region);
            string? role = _characterMatcher.Observe(feature, _characterProfile.Templates, out double confidence);
            _characterFollowStatus!.Text = role is null
                ? L($"等待稳定识别（匹配度 {confidence:P0}）", $"Waiting for stable recognition ({confidence:P0})")
                : L($"当前角色：{role}（{confidence:P0}）", $"Current character: {role} ({confidence:P0})");
            if (role is null || role.Equals(SelectedShortcutRole(), StringComparison.OrdinalIgnoreCase)) return;
            var candidates = _shortcutModPicker!.ItemsSource as ShortcutModOption[] ?? [];
            var matching = candidates.Where(p => p.Label.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0].Equals(role, StringComparison.OrdinalIgnoreCase)).ToArray();
            var selected = matching.FirstOrDefault(p => _characterPreferredMods.TryGetValue(role, out string? preferred) && p.Path == preferred)
                ?? (matching.Length == 1 ? matching[0] : null);
            if (selected is null && matching.Length > 1)
                _characterFollowStatus.Text = L($"{role} 有多个 Mod，请手动选择一次。", $"{role} has multiple Mods; choose one manually first.");
            if (selected is null) return;
            _characterAutoSelecting = true;
            try { _shortcutModPicker.SelectedItem = selected; }
            finally { _characterAutoSelecting = false; }
        }
        catch (Exception ex) { _characterMatcher.Reset(); if (!_onlineWindowClosed) _characterFollowStatus!.Text = ex.Message; }
        finally { _characterCaptureBusy = false; }
    }
}
