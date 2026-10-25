#if IMM_SHORTCUT_BENCHMARK
using System.Diagnostics;
using System.Text.Json;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Compiled only with /p:ImmShortcutBenchmark=true. The distributed app has no benchmark entry point.
public sealed partial class MainWindow
{
    private string _benchmarkF10Result = "not run";
    internal async Task RunShortcutBenchmarkAsync()
    {
        string output = Path.Combine(AppContext.BaseDirectory, "shortcut-benchmark.json");
        try
        {
            await Task.Delay(1500);
            BuildShortcutPane();
            var rows = new List<object>();
            foreach (int count in new[] { 100, 200, 300 })
            {
                string fixture = Path.Combine(AppContext.BaseDirectory, "synthetic-shortcuts", count.ToString());
                Directory.CreateDirectory(fixture);
                File.WriteAllText(Path.Combine(fixture, "mod.ini"), string.Join("\n", Enumerable.Range(0, count)
                    .Select(i => $"[Key{i}]\nkey = {((char)('A' + i % 26))}\n$x{i} = 0,1,2\n")));
                var clock = Stopwatch.StartNew();
                var scanned = ModShortcutScanner.ScanDirectory(fixture, activeOnly: true);
                double scan = clock.Elapsed.TotalMilliseconds;
                if (scanned.Count != count) throw new InvalidDataException("Synthetic binding count differs.");
                var cache = new ModShortcutScanCache(); cache.Scan(fixture);
                clock.Restart(); cache.Scan(fixture); double cached = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                var baseline = scanned.Select(s => CreateShortcutAction(s)).ToArray();
                double create = clock.Elapsed.TotalMilliseconds;
                _shortcutDetected = scanned;
                _currentPrimarySection = PrimarySection.Shortcuts;
                DashboardScrollViewer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                ShortcutsScrollViewer.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                clock.Restart(); RenderShortcutButtons(); RootGrid.UpdateLayout(); double select = clock.Elapsed.TotalMilliseconds;
                clock.Restart(); RenderShortcutButtons(); RootGrid.UpdateLayout(); double reuse = clock.Elapsed.TotalMilliseconds;
                _currentPrimarySection = PrimarySection.Repository;
                ShortcutsScrollViewer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                clock.Restart(); RenderShortcutButtons(); double hidden = clock.Elapsed.TotalMilliseconds;
                // Test the actual overlay content and its layout without any target window or input.
                string label = "Synthetic/" + count;
                var option = new ShortcutModOption(fixture, label);
                _shortcutModPicker!.ItemsSource = new[] { option };
                _shortcutModPicker.SelectedItem = option;
                await LoadShortcutSelectionAsync();
                _shortcutDeployedMods = new(StringComparer.OrdinalIgnoreCase) { label };
                clock.Restart();
                _shortcutOverlay = new ShortcutOverlayWindow(BuildShortcutOverlayContent(), _overlayTextBrush, "Synthetic benchmark");
                _shortcutOverlayVisible = true;
                _shortcutOverlay.ShowPanel();
                ((Microsoft.UI.Xaml.FrameworkElement)_shortcutOverlay.Content).UpdateLayout();
                double open = clock.Elapsed.TotalMilliseconds;
                Button? first = _overlayButtonList!.Children.FirstOrDefault() as Button;
                clock.Restart();
                for (int i = 0; i < 100; i++) RefreshShortcutOverlay();
                double status = clock.Elapsed.TotalMilliseconds / 100;
                if (!ReferenceEquals(first, _overlayButtonList.Children.FirstOrDefault())) throw new InvalidDataException("A status update rebuilt bindings.");
                _shortcutOverlay.Close(); _shortcutOverlay = null; _shortcutOverlayVisible = false;
                rows.Add(new { Bindings = count, ScanMs = scan, CachedScanMs = cached, CreateAllButtonsMs = create,
                    SelectAndLayoutMs = select, ReusedSelectAndLayoutMs = reuse, HiddenRenderMs = hidden,
                    OpenOverlayAndLayoutMs = open, StatusOnlyMeanMs = status });
                GC.KeepAlive(baseline);
            }
            await CheckOverlayDeploymentRefreshAsync();
            File.WriteAllText(output, JsonSerializer.Serialize(new { Scope = "Synthetic WinUI controls, repository and native F10 receiver only; no game access.",
                DeploymentReplacementAndClearPassed = true, ManualRefreshReadsNewModsPassed = true,
                F10ReceiverResult = _benchmarkF10Result, Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { Error = error.ToString() })); }
        finally { _shortcutOverlay?.Close(); _shortcutOverlay = null; Close(); }
    }

    private async Task CheckOverlayDeploymentRefreshAsync()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "synthetic-refresh");
        string source = Path.Combine(fixture, "Repository"), target = Path.Combine(fixture, "Mods");
        string a = Path.Combine(source, "Character", "A"), b = Path.Combine(source, "Character", "B");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(a, "mod.ini"), "[KeyA]\nkey = A\n$x = 1\n");
        File.WriteAllText(Path.Combine(b, "mod.ini"), "[KeyB]\nkey = B\n$x = 2\n");
        var repository = new WorkspaceRepository { Name = "Synthetic", SourcePath = source, TargetPath = target, UseDirectoryLinks = true };
        _repositories.Add(repository); _selectedRepositoryId = repository.Id;
        DirectoryLinkDeployment.CreateJunction(Path.Combine(target, "A"), a);
        _shortcutModPicker!.ItemsSource = ReadShortcutModOptions(source);
        _shortcutModPicker.SelectedItem = ((ShortcutModOption[])_shortcutModPicker.ItemsSource)[0];
        await LoadShortcutSelectionAsync();
        await RefreshShortcutDeployedModsAsync();
        _shortcutOverlay = new ShortcutOverlayWindow(BuildShortcutOverlayContent(), _overlayTextBrush, "Synthetic refresh");
        _shortcutOverlayVisible = true;
        DirectoryLinkDeployment.RemoveLink(Path.Combine(target, "A"));
        DirectoryLinkDeployment.CreateJunction(Path.Combine(target, "B"), b);
        await RefreshShortcutDeployedModsAsync(b);
        if ((_shortcutModPicker.SelectedItem as ShortcutModOption)?.Path != b || _overlayBindings.Single().RawKey != "B")
            throw new InvalidDataException("Overlay did not follow the replaced Mod.");
        DirectoryLinkDeployment.RemoveLink(Path.Combine(target, "B"));
        await RefreshShortcutDeployedModsAsync();
        if (_shortcutModPicker.SelectedItem is not null || _overlayBindings.Length != 0 || _overlayModOptions.Length != 0)
            throw new InvalidDataException("Disabled Mod left stale overlay controls.");
        string c = Path.Combine(source, "Character", "C"); Directory.CreateDirectory(c);
        File.WriteAllText(Path.Combine(c, "mod.ini"), "[KeyC]\nkey = C\n$x = 3\n");
        DirectoryLinkDeployment.CreateJunction(Path.Combine(target, "C"), c);
        await RefreshOverlayFromDiskAsync();
        if (!_overlayModOptions.Any(option => option.Path == c)) throw new InvalidDataException("Refresh omitted a new Mod.");
        await CheckLoaderFlushCaptureAsync(repository, c, fixture);
        _shortcutOverlay.Close(); _shortcutOverlay = null; _shortcutOverlayVisible = false;
    }

    private async Task CheckLoaderFlushCaptureAsync(WorkspaceRepository repository, string mod, string fixture)
    {
        string ini = Path.Combine(fixture, "d3dx_user.ini");
        File.WriteAllText(Path.Combine(mod, "mod.ini"), "[Constants]\nglobal persist $x = 0\n[KeyC]\nkey = C\n$x = 0,1\n");
        File.WriteAllText(ini, "[Constants]\n$\\Mods\\C\\mod.ini\\x = 0\n");
        repository.PersistentUserIniPath = ini;
        SourceTextBox.Text = repository.SourcePath; TargetTextBox.Text = repository.TargetPath;
        _shortcutModPicker!.SelectedItem = ((ShortcutModOption[])_shortcutModPicker.ItemsSource).Single(option => option.Path == mod);
        await LoadShortcutSelectionAsync(force: true);
        using var receiver = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "LoaderFlushReceiver.exe"))
            { Arguments = "\"" + ini + "\"", UseShellExecute = false, WindowStyle = ProcessWindowStyle.Normal })!;
        try
        {
            await Task.Delay(800);
            var targets = ShortcutInputService.ListTargets();
            _shortcutTargetPicker!.ItemsSource = targets;
            _shortcutTargetPicker.SelectedItem = targets.Single(target => target.ProcessId == receiver.Id);
            _shortcutEnabledToggle!.IsOn = true;
            int before = _configurationProfiles.Count;
            await QuickSaveCombinationFromOverlayAsync();
            if (_configurationProfiles.Count == before && _shortcutStatus!.Text.Contains(IsEnglishUi
                ? "Cannot focus the target window" : "无法激活目标窗口"))
            {
                // The unattended test desktop may deny foreground transfer. Record the real input
                // boundary, then independently check the flush/capture contract with a simulated write.
                await LoaderStateCapture.FlushAsync(ini, _ =>
                {
                    File.WriteAllText(ini, "[Constants]\n$\\Mods\\C\\mod.ini\\x = 7\n");
                    return Task.CompletedTask;
                });
                var captured = ModPersistentPresetEngine.CaptureCombination([(mod, "C")], ini);
                if (captured.Single().Values.Single().Value != "7") throw new InvalidDataException("Synthetic flush captured stale state.");
                _benchmarkF10Result = "Foreground transfer denied; the overlay refused to save. A simulated loader write followed by strict capture passed. Native F10 success, timeout and hot-apply UI checks remain unverified.";
                return;
            }
            if (_configurationProfiles.Count != before + 1
                || _configurationProfiles.Last().PersistentStates.Single().Values.Single().Value != "7"
                || File.GetAttributes(ini).HasFlag(FileAttributes.ReadOnly))
                throw new InvalidDataException("F10 did not capture the synthetic loader memory value: " + _shortcutStatus!.Text);
            File.WriteAllText(ini + ".ignore", "ignore F10");
            await Task.Delay(400);
            await QuickSaveCombinationFromOverlayAsync();
            if (_configurationProfiles.Count != before + 1 || !_shortcutStatus!.Text.Contains(T("ShortcutCapture.TimedOut")))
                throw new InvalidDataException("A timed-out flush saved a preset or showed the wrong error.");
            File.Delete(ini + ".ignore");
            File.SetAttributes(ini, FileAttributes.ReadOnly);
            _restoringPersistentRuntime = _shortcutSendInProgress = true;
            async Task EndHotApply()
            {
                await Task.Delay(250);
                File.SetAttributes(ini, FileAttributes.Normal);
                _restoringPersistentRuntime = _shortcutSendInProgress = false;
            }
            Task release = EndHotApply();
            await QuickSaveCombinationFromOverlayAsync(); await release;
            if (_configurationProfiles.Count != before + 2) throw new InvalidDataException("Save did not queue behind hot apply.");
            File.AppendAllText(Path.Combine(mod, "mod.ini"), "\n[Constants]\nglobal persist $missing = 0\n");
            await Task.Delay(400);
            await QuickSaveCombinationFromOverlayAsync();
            if (_configurationProfiles.Count != before + 2 || _shortcutStatus!.Text.Contains("Missing saved loader value"))
                throw new InvalidDataException("Missing loader value was saved or its core English error escaped localization.");
            _benchmarkF10Result = "Native synthetic F10 capture, timeout rejection, queuing behind hot apply and localized missing values passed. No real game validation.";
        }
        finally { receiver.CloseMainWindow(); await Task.Run(() => receiver.WaitForExit(3000)); }
    }
}
#endif
