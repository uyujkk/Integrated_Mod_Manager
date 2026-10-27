using System.Diagnostics;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void BuildShortcutRows()
    {
        ShortcutRowsPanel.Children.Clear();
        _shortcutKeyBorders.Clear();
        _shortcutKeyBoxes.Clear();
        _shortcutActionBoxes.Clear();

        EnsureShortcutRowCapacity(InitialShortcutRows);

        OnlineTitleTextBlock.Text = T("Common.OnlineModBrowser");
        OnlineSubtitleTextBlock.Text = T("ShortcutBindings.ThisPageNowFocusesOnThe");
        OnlinePreviewTitleTextBlock.Text = T("Common.OnlineModList");
        OnlinePreviewHintTextBlock.Text = T("ShortcutBindings.ThisPageLoadsDataFromAn");
        RefreshSettingsButtonLabels();
        RefreshOnlineNavigationButtonLabels();
        OpenOnlineDetailPageButton.Content = T("Common.OpenOriginalPage");
        SelectOnlineDetailDownloadButton.Content = T("Common.SelectFile");
        DownloadOnlineDetailButton.Content = T("Common.DownloadLatestArchive");
        if (_activeOnlineDetailMod is null)
        {
            ResetOnlineDetailPaneToPlaceholder();
        }
        else
        {
            UpdateOnlineDetailDownloadControls(_activeOnlineDetailMod);
        }
        OnlineDetailRequirementTitleTextBlock.Text = T("Common.PossibleExtraAccessRequirements");
        ApplyShortcutPlaceholders();
        UpdateShortcutRowVisibility();
        UpdateShortcutKeyFocusVisuals();
        RefreshRefinedActionIcons();
    }

    private void EnsureShortcutRowCapacity(int requiredRows)
    {
        requiredRows = Math.Max(InitialShortcutRows, requiredRows);
        while (_shortcutKeyBoxes.Count < requiredRows)
        {
            AddShortcutRowControls();
        }
    }

    private void AddShortcutRowControls()
    {
        var rowGrid = new Grid { ColumnSpacing = 12 };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var keyBorder = CreateInsetBorder();
        var keyBox = new TextBox
        {
            IsReadOnly = true,
            Style = (Style)Application.Current.Resources["ShortcutInputTextBoxStyle"],
            PlaceholderText = T("Common.Shortcut")
        };
        ConfigureShortcutTextBoxTheme(keyBox);
        AttachTrackedTextInput(keyBox);
        keyBox.TextChanged += OnShortcutTextChanged;
        keyBox.KeyDown += OnShortcutKeyBoxKeyDown;
        keyBox.GotFocus += OnShortcutKeyBoxGotFocus;
        keyBox.LostFocus += OnShortcutKeyBoxLostFocus;
        keyBorder.Child = keyBox;

        var actionBorder = CreateInsetBorder();
        var actionBox = new TextBox
        {
            Style = (Style)Application.Current.Resources["ShortcutInputTextBoxStyle"],
            PlaceholderText = T("Common.Description")
        };
        ConfigureShortcutTextBoxTheme(actionBox);
        AttachTrackedTextInput(actionBox);
        actionBox.TextChanged += OnShortcutTextChanged;
        actionBorder.Child = actionBox;

        Grid.SetColumn(keyBorder, 0);
        Grid.SetColumn(actionBorder, 1);
        rowGrid.Children.Add(keyBorder);
        rowGrid.Children.Add(actionBorder);

        ShortcutRowsPanel.Children.Add(rowGrid);
        _shortcutKeyBorders.Add(keyBorder);
        _shortcutKeyBoxes.Add(keyBox);
        _shortcutActionBoxes.Add(actionBox);
    }

    private static Border CreateInsetBorder()
    {
        return new Border
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"]
        };
    }

    private void ConfigureShortcutTextBoxTheme(TextBox box)
    {
        var transparentBrush = new SolidColorBrush(Colors.Transparent);
        box.Resources["TextControlBackground"] = transparentBrush;
        box.Resources["TextControlBackgroundFocused"] = transparentBrush;
        box.Resources["TextControlBackgroundPointerOver"] = transparentBrush;
        box.Resources["TextControlBackgroundDisabled"] = transparentBrush;
        box.Resources["TextControlBackgroundReadOnly"] = transparentBrush;
        box.Resources["TextControlBorderBrush"] = transparentBrush;
        box.Resources["TextControlBorderBrushFocused"] = transparentBrush;
        box.Resources["TextControlBorderBrushPointerOver"] = transparentBrush;
        box.Resources["TextControlBorderBrushDisabled"] = transparentBrush;
        box.Resources["TextControlBorderBrushReadOnly"] = transparentBrush;
        box.Resources["TextControlForegroundReadOnly"] = GetAppThemeBrush("AppNavDefaultForegroundBrush");
    }

    private void OnShortcutKeyBoxGotFocus(object sender, RoutedEventArgs e)
    {
        _activeShortcutKeyBox = sender as TextBox;
        UpdateShortcutKeyFocusVisuals();
    }

    private void OnShortcutKeyBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_activeShortcutKeyBox, sender))
        {
            _activeShortcutKeyBox = null;
        }

        UpdateShortcutKeyFocusVisuals();
    }

    private void UpdateShortcutKeyFocusVisuals()
    {
        Brush normalBackground = GetAppThemeBrush("AppInsetBackgroundBrush");
        Brush normalBorder = GetAppThemeBrush("AppCardBorderBrush");
        Brush activeBackground = GetAppThemeBrush("AppAccentSoftBrush");
        Brush activeBorder = GetAppThemeBrush("AppNavSelectedBorderBrush");
        object? focusedElement = Content is FrameworkElement root && root.XamlRoot is not null
            ? FocusManager.GetFocusedElement(root.XamlRoot)
            : null;

        for (int i = 0; i < _shortcutKeyBorders.Count; i++)
        {
            bool isActive = i < _shortcutKeyBoxes.Count &&
                ReferenceEquals(_shortcutKeyBoxes[i], _activeShortcutKeyBox) &&
                ReferenceEquals(_shortcutKeyBoxes[i], focusedElement);
            _shortcutKeyBorders[i].Background = isActive ? activeBackground : normalBackground;
            _shortcutKeyBorders[i].BorderBrush = isActive ? activeBorder : normalBorder;
            _shortcutKeyBorders[i].BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
        }
    }

    private async void OnAddShortcutRowClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_currentSecondLevelPath))
            {
                await ShowMessageAsync(
                    T("Common.SelectASecondLevelModFirst"),
                    T("Common.NoModSelected"));
                return;
            }

            _visibleShortcutRows++;
            EnsureShortcutRowCapacity(_visibleShortcutRows);
            UpdateShortcutRowVisibility();
            _shortcutKeyBoxes[_visibleShortcutRows - 1].Focus(FocusState.Programmatic);
            SaveCurrentModBindings();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnAddShortcutRowClicked", ex);
        }
    }

    private void OnShortcutTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoadingBindings)
        {
            SaveCurrentModBindings();
        }
    }

    private void OnShortcutKeyBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        // Tab belongs to focus/window navigation. Never overwrite a shortcut
        // while the user is leaving the app or moving between editor fields.
        if (e.Key == VirtualKey.Tab
            || IsModifierDown(VirtualKey.LeftWindows) || IsModifierDown(VirtualKey.RightWindows)
            || (IsModifierDown(VirtualKey.Menu) && e.Key is VirtualKey.Escape or VirtualKey.F4)) return;

        if (e.KeyStatus.WasKeyDown || e.KeyStatus.RepeatCount > 1)
        {
            e.Handled = true;
            return;
        }

        if (e.Key is VirtualKey.Back or VirtualKey.Delete or VirtualKey.Escape)
        {
            box.Text = string.Empty;
            e.Handled = true;
            return;
        }

        string shortcut = BuildShortcutFromEvent(e);
        if (string.IsNullOrEmpty(shortcut))
        {
            return;
        }

        box.Text = shortcut;
        box.SelectionStart = box.Text.Length;
        e.Handled = true;
    }

    private void OnModLinkTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoadingModLink)
        {
            SaveCurrentModLink();
        }
    }

    private async void OnOpenModLinkClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string currentLink = (ModLinkTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(currentLink))
            {
                await ShowMessageAsync(
                    T("ShortcutBindings.EnterAValidWebLinkFor"),
                    T("ShortcutBindings.LinkNotSet"));
                return;
            }

            try
            {
                string normalized = NormalizeLink(currentLink);
                Process.Start(new ProcessStartInfo
                {
                    FileName = normalized,
                    UseShellExecute = true
                });
                StatusTextBlock.Text = T("ShortcutBindings.OpenedTheModLinkInThe");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    T("Common.FailedToOpenLink") + ex.Message,
                    T("Common.OpenFailed"));
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnOpenModLinkClicked", ex);
        }
    }

    private void LoadDefaultShortcutTemplate()
    {
        _isLoadingBindings = true;
        _activeShortcutKeyBox = null;
        foreach (TextBox box in _shortcutKeyBoxes)
        {
            box.Text = string.Empty;
        }

        foreach (TextBox box in _shortcutActionBoxes)
        {
            box.Text = string.Empty;
        }

        _visibleShortcutRows = 1;
        UpdateShortcutRowVisibility();
        UpdateShortcutKeyFocusVisuals();
        _isLoadingBindings = false;
    }

    private void LoadBindingsForCurrentMod(SecondLevelFolderItem? item)
    {
        _isLoadingBindings = true;
        _activeShortcutKeyBox = null;

        foreach (TextBox box in _shortcutKeyBoxes)
        {
            box.Text = string.Empty;
        }

        foreach (TextBox box in _shortcutActionBoxes)
        {
            box.Text = string.Empty;
        }

        if (item is not null && _modBindings.TryGetValue(item.Path, out List<ShortcutBinding>? bindings) && bindings.Count > 0)
        {
            _visibleShortcutRows = Math.Max(InitialShortcutRows, bindings.Count);
            EnsureShortcutRowCapacity(_visibleShortcutRows);
            for (int i = 0; i < _visibleShortcutRows; i++)
            {
                _shortcutKeyBoxes[i].Text = bindings[i].Shortcut;
                _shortcutActionBoxes[i].Text = GetShortcutBindingDisplayAction(bindings[i]);
            }
        }
        else
        {
            _visibleShortcutRows = 1;
        }

        UpdateShortcutRowVisibility();
        UpdateShortcutKeyFocusVisuals();
        _isLoadingBindings = false;
    }

    private async Task<int> TryImportShortcutBindingsFromModFilesAsync(string modFolder)
    {
        if (string.IsNullOrWhiteSpace(modFolder)
            || !Directory.Exists(modFolder))
        {
            return 0;
        }

        bool hasExistingBindings = _modBindings.TryGetValue(modFolder, out List<ShortcutBinding>? existing)
            && existing.Any(binding => !string.IsNullOrWhiteSpace(binding.Shortcut)
                || !string.IsNullOrWhiteSpace(binding.Action));
        bool mayContainLegacyAutomaticDescriptions = hasExistingBindings
            && existing!.Any(binding => IsPossibleLegacyAutomaticShortcutDescription(binding.Action));
        if (hasExistingBindings && !mayContainLegacyAutomaticDescriptions)
        {
            return 0;
        }

        IReadOnlyList<DetectedModShortcut> detected;
        try
        {
            detected = await Task.Run(() => ModShortcutScanner.ScanDirectory(modFolder, ShortcutScanSafetyLimit));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Read Mod shortcut INI", ex);
            return 0;
        }

        if (detected.Count == 0)
        {
            return 0;
        }

        if (hasExistingBindings)
        {
            int upgradedCount = UpgradeLegacyAutomaticShortcutBindings(existing!, detected);
            if (upgradedCount > 0)
            {
                SaveConfig();
            }

            return upgradedCount;
        }

        _modBindings[modFolder] = detected
            .Take(ShortcutScanSafetyLimit)
            .Select(CreateAutomaticShortcutBinding)
            .ToList();
        SaveConfig();
        return _modBindings[modFolder].Count;
    }

    private string DescribeDetectedShortcut(DetectedModShortcut shortcut)
    {
        return ModShortcutDescriptionFormatter.Describe(
            shortcut,
            useEnglish: _currentLanguage == AppLanguage.EnUs);
    }

    private ShortcutBinding CreateAutomaticShortcutBinding(DetectedModShortcut shortcut)
    {
        return new ShortcutBinding(shortcut.Shortcut, DescribeDetectedShortcut(shortcut))
        {
            AutoSection = shortcut.Section,
            AutoBehavior = shortcut.Behavior,
            AutoTarget = shortcut.Target,
            AutoSourceFile = shortcut.SourceFile
        };
    }

    private string GetShortcutBindingDisplayAction(ShortcutBinding binding)
    {
        return binding.HasAutoDescription
            ? DescribeDetectedShortcut(ToDetectedModShortcut(binding))
            : binding.Action;
    }

    private static DetectedModShortcut ToDetectedModShortcut(ShortcutBinding binding) =>
        new(
            binding.Shortcut,
            binding.AutoSection,
            binding.AutoBehavior,
            binding.AutoTarget,
            binding.AutoSourceFile);

    private static bool IsPossibleLegacyAutomaticShortcutDescription(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        string value = action.Trim();
        return value.StartsWith("循环切换 ", StringComparison.Ordinal)
            || value.StartsWith("按住触发 ", StringComparison.Ordinal)
            || value.StartsWith("切换 ", StringComparison.Ordinal)
            || value.StartsWith("触发 ", StringComparison.Ordinal)
            || value.StartsWith("Cycle ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Hold ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Toggle ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Trigger ", StringComparison.OrdinalIgnoreCase);
    }

    private int UpgradeLegacyAutomaticShortcutBindings(
        List<ShortcutBinding> existingBindings,
        IReadOnlyList<DetectedModShortcut> detectedShortcuts)
    {
        int upgradedCount = 0;
        for (int index = 0; index < existingBindings.Count; index++)
        {
            ShortcutBinding existingBinding = existingBindings[index];
            if (existingBinding.HasAutoDescription
                || !IsPossibleLegacyAutomaticShortcutDescription(existingBinding.Action))
            {
                continue;
            }

            DetectedModShortcut? match = detectedShortcuts.FirstOrDefault(shortcut =>
                string.Equals(shortcut.Shortcut, existingBinding.Shortcut, StringComparison.OrdinalIgnoreCase)
                && ModShortcutDescriptionFormatter.MatchesLegacyDescription(existingBinding.Action, shortcut));
            if (match is null)
            {
                continue;
            }

            existingBindings[index] = CreateAutomaticShortcutBinding(match);
            upgradedCount++;
        }

        return upgradedCount;
    }

    private void SaveCurrentModBindings()
    {
        if (_isLoadingBindings || string.IsNullOrEmpty(_currentSecondLevelPath))
        {
            return;
        }

        _modBindings.TryGetValue(_currentSecondLevelPath, out List<ShortcutBinding>? previousBindings);
        var bindings = new List<ShortcutBinding>();
        for (int i = 0; i < _visibleShortcutRows; i++)
        {
            string shortcut = (_shortcutKeyBoxes[i].Text ?? string.Empty).Trim();
            string action = (_shortcutActionBoxes[i].Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(shortcut) || !string.IsNullOrWhiteSpace(action))
            {
                ShortcutBinding? previous = previousBindings is not null && i < previousBindings.Count
                    ? previousBindings[i]
                    : null;
                bool unchangedAutomaticBinding = previous is not null
                    && previous.HasAutoDescription
                    && string.Equals(shortcut, previous.Shortcut, StringComparison.Ordinal)
                    && string.Equals(action, GetShortcutBindingDisplayAction(previous), StringComparison.Ordinal);
                bindings.Add(unchangedAutomaticBinding
                    ? previous!
                    : new ShortcutBinding(shortcut, action));
            }
        }

        if (bindings.Count == 0)
        {
            _modBindings.Remove(_currentSecondLevelPath);
        }
        else
        {
            _modBindings[_currentSecondLevelPath] = bindings;
        }

        SaveConfig();
        UpdateShortcutRowVisibility();
    }

    private void LoadLinkForCurrentMod(SecondLevelFolderItem? item)
    {
        _isLoadingModLink = true;
        ModLinkTextBox.Text = item is not null && _modLinks.TryGetValue(item.Path, out string? link) ? link : string.Empty;
        _isLoadingModLink = false;
        UpdateLinkButtonState();
    }

    private void SaveCurrentModLink()
    {
        if (_isLoadingModLink || string.IsNullOrEmpty(_currentSecondLevelPath))
        {
            UpdateLinkButtonState();
            return;
        }

        string link = (ModLinkTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(link))
        {
            _modLinks.Remove(_currentSecondLevelPath);
        }
        else
        {
            _modLinks[_currentSecondLevelPath] = link;
        }

        SaveConfig();
        UpdateLinkButtonState();
    }

    private void UpdateLinkButtonState()
    {
        OpenModLinkButton.IsEnabled = !string.IsNullOrWhiteSpace(ModLinkTextBox.Text);
    }

    private void UpdateShortcutRowVisibility()
    {
        EnsureShortcutRowCapacity(_visibleShortcutRows);
        for (int i = 0; i < ShortcutRowsPanel.Children.Count; i++)
        {
            ShortcutRowsPanel.Children[i].Visibility = i < _visibleShortcutRows ? Visibility.Visible : Visibility.Collapsed;
        }

        ShortcutRowsTextBlock.Text = T("ShortcutBindings.Rows", _visibleShortcutRows);
    }

    private void RefreshVisibleShortcutDescriptionsForLanguage()
    {
        if (string.IsNullOrWhiteSpace(_currentSecondLevelPath)
            || !_modBindings.TryGetValue(_currentSecondLevelPath, out List<ShortcutBinding>? bindings)
            || bindings.Count == 0)
        {
            return;
        }

        EnsureShortcutRowCapacity(bindings.Count);
        _isLoadingBindings = true;
        try
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].HasAutoDescription)
                {
                    _shortcutActionBoxes[i].Text = GetShortcutBindingDisplayAction(bindings[i]);
                }
            }
        }
        finally
        {
            _isLoadingBindings = false;
        }
    }

    private static string BuildShortcutFromEvent(KeyRoutedEventArgs e)
    {
        var parts = new List<string>();

        if (IsModifierDown(VirtualKey.Control))
        {
            parts.Add("Ctrl");
        }

        if (IsModifierDown(VirtualKey.Shift))
        {
            parts.Add("Shift");
        }

        if (IsModifierDown(VirtualKey.Menu))
        {
            parts.Add("Alt");
        }

        string key = KeyToString(e.Key);
        if (string.IsNullOrEmpty(key) || key is "Control" or "Shift" or "Alt" or "LeftWindows" or "RightWindows")
        {
            return string.Empty;
        }

        parts.Add(key);
        return string.Join("+", parts);
    }

    private static bool IsModifierDown(VirtualKey key)
    {
        CoreVirtualKeyStates state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private static string KeyToString(VirtualKey key)
    {
        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            return ((int)key - (int)VirtualKey.Number0).ToString();
        }

        if (key is >= VirtualKey.F1 and <= VirtualKey.F24)
        {
            return key.ToString();
        }

        if (key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9)
        {
            return "Num" + ((int)key - (int)VirtualKey.NumberPad0);
        }

        int keyCode = (int)key;
        if (keyCode is >= 186 and <= 192 or >= 219 and <= 222)
        {
            return keyCode switch
            {
                186 => ";",
                187 => "=",
                188 => ",",
                189 => "-",
                190 => ".",
                191 => "/",
                192 => "`",
                219 => "[",
                220 => "\\",
                221 => "]",
                222 => "'",
                _ => string.Empty
            };
        }

        return key switch
        {
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Esc",
            VirtualKey.Space => "Space",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Tab => "Tab",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Decimal => ".",
            VirtualKey.Add => "+",
            VirtualKey.Subtract => "-",
            VirtualKey.Multiply => "*",
            VirtualKey.Divide => "/",
            _ => key.ToString()
        };
    }

    private static string NormalizeShortcut(string? shortcut)
    {
        return string.Join("+", (shortcut ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                if (string.Equals(part, "control", StringComparison.OrdinalIgnoreCase))
                {
                    return "Ctrl";
                }

                if (string.Equals(part, "menu", StringComparison.OrdinalIgnoreCase))
                {
                    return "Alt";
                }

                return part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
            }));
    }

    private static string NormalizeLink(string link)
    {
        string trimmed = link.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return "https://" + trimmed;
    }
}
