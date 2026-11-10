using System.Diagnostics;
using IntegratedModManager.Core.Configuration;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace ModFolderCopier.WinUI;

public sealed partial class WorkspacePage
{
    internal void BuildShortcutRows()
    {
        ShortcutRowsPanel.Children.Clear();
        _shortcutKeyBorders.Clear();
        _host.ShortcutKeyBoxes.Clear();
        _host.ShortcutActionBoxes.Clear();

        EnsureShortcutRowCapacity(InitialShortcutRows);

        _host.OnlineView.OnlineTitleTextBlock.Text = T("Common.OnlineModBrowser");
        _host.OnlineView.OnlineSubtitleTextBlock.Text = T("ShortcutBindings.ThisPageNowFocusesOnThe");
        _host.OnlineView.OnlinePreviewTitleTextBlock.Text = T("Common.OnlineModList");
        _host.OnlineView.OnlinePreviewHintTextBlock.Text = T("ShortcutBindings.ThisPageLoadsDataFromAn");
        _host.OnlineView.RefreshOnlineNavigationButtonLabels();
        _host.OnlineView.OpenOnlineDetailPageButton.Content = T("Common.OpenOriginalPage");
        _host.OnlineView.SelectOnlineDetailDownloadButton.Content = T("Common.SelectFile");
        _host.OnlineView.DownloadOnlineDetailButton.Content = T("Common.DownloadLatestArchive");
        if (_host.OnlineView._activeOnlineDetailMod is null)
        {
            _host.OnlineView.ResetOnlineDetailPaneToPlaceholder();
        }
        else
        {
            _host.OnlineView.UpdateOnlineDetailDownloadControls(_host.OnlineView._activeOnlineDetailMod);
        }
        _host.OnlineView.OnlineDetailRequirementTitleTextBlock.Text = T("Common.PossibleExtraAccessRequirements");
        _host.ApplyShortcutPlaceholders();
        UpdateShortcutRowVisibility();
        UpdateShortcutKeyFocusVisuals();
        RefreshRefinedActionIcons();
    }

    private void EnsureShortcutRowCapacity(int requiredRows)
    {
        requiredRows = Math.Max(InitialShortcutRows, requiredRows);
        while (_host.ShortcutKeyBoxes.Count < requiredRows)
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
        _host.AttachTrackedTextInput(keyBox);
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
        _host.AttachTrackedTextInput(actionBox);
        actionBox.TextChanged += OnShortcutTextChanged;
        actionBorder.Child = actionBox;

        Grid.SetColumn(keyBorder, 0);
        Grid.SetColumn(actionBorder, 1);
        rowGrid.Children.Add(keyBorder);
        rowGrid.Children.Add(actionBorder);

        ShortcutRowsPanel.Children.Add(rowGrid);
        _shortcutKeyBorders.Add(keyBorder);
        _host.ShortcutKeyBoxes.Add(keyBox);
        _host.ShortcutActionBoxes.Add(actionBox);
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

    internal void UpdateShortcutKeyFocusVisuals()
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
            bool isActive = i < _host.ShortcutKeyBoxes.Count &&
                ReferenceEquals(_host.ShortcutKeyBoxes[i], _activeShortcutKeyBox) &&
                ReferenceEquals(_host.ShortcutKeyBoxes[i], focusedElement);
            _shortcutKeyBorders[i].Background = isActive ? activeBackground : normalBackground;
            _shortcutKeyBorders[i].BorderBrush = isActive ? activeBorder : normalBorder;
            _shortcutKeyBorders[i].BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
        }
    }

    private async void OnAddShortcutRowClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_host.CurrentSecondLevelPath))
            {
                await ShowMessageAsync(
                    T("Common.SelectASecondLevelModFirst"),
                    T("Common.NoModSelected"));
                return;
            }

            _visibleShortcutRows++;
            EnsureShortcutRowCapacity(_visibleShortcutRows);
            UpdateShortcutRowVisibility();
            _host.ShortcutKeyBoxes[_visibleShortcutRows - 1].Focus(FocusState.Programmatic);
            SaveCurrentModBindings();
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnAddShortcutRowClicked", ex);
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
                string normalized = _host.NormalizeLink(currentLink);
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
            _host.ReportUiActionFailure("OnOpenModLinkClicked", ex);
        }
    }

    internal void LoadDefaultShortcutTemplate()
    {
        _isLoadingBindings = true;
        _activeShortcutKeyBox = null;
        foreach (TextBox box in _host.ShortcutKeyBoxes)
        {
            box.Text = string.Empty;
        }

        foreach (TextBox box in _host.ShortcutActionBoxes)
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

        foreach (TextBox box in _host.ShortcutKeyBoxes)
        {
            box.Text = string.Empty;
        }

        foreach (TextBox box in _host.ShortcutActionBoxes)
        {
            box.Text = string.Empty;
        }

        if (item is not null && _host.ModBindings.TryGetValue(item.Path, out List<ShortcutBinding>? bindings) && bindings.Count > 0)
        {
            _visibleShortcutRows = Math.Max(InitialShortcutRows, bindings.Count);
            EnsureShortcutRowCapacity(_visibleShortcutRows);
            for (int i = 0; i < _visibleShortcutRows; i++)
            {
                _host.ShortcutKeyBoxes[i].Text = bindings[i].Shortcut;
                _host.ShortcutActionBoxes[i].Text = _host.GetShortcutBindingDisplayAction(bindings[i]);
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

    internal void SaveCurrentModBindings()
    {
        if (_isLoadingBindings || string.IsNullOrEmpty(_host.CurrentSecondLevelPath))
        {
            return;
        }

        _host.ModBindings.TryGetValue(_host.CurrentSecondLevelPath, out List<ShortcutBinding>? previousBindings);
        var bindings = new List<ShortcutBinding>();
        for (int i = 0; i < _visibleShortcutRows; i++)
        {
            string shortcut = (_host.ShortcutKeyBoxes[i].Text ?? string.Empty).Trim();
            string action = (_host.ShortcutActionBoxes[i].Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(shortcut) || !string.IsNullOrWhiteSpace(action))
            {
                ShortcutBinding? previous = previousBindings is not null && i < previousBindings.Count
                    ? previousBindings[i]
                    : null;
                bool unchangedAutomaticBinding = previous is not null
                    && previous.HasAutoDescription
                    && string.Equals(shortcut, previous.Shortcut, StringComparison.Ordinal)
                    && string.Equals(action, _host.GetShortcutBindingDisplayAction(previous), StringComparison.Ordinal);
                bindings.Add(unchangedAutomaticBinding
                    ? previous!
                    : new ShortcutBinding(shortcut, action));
            }
        }

        if (bindings.Count == 0)
        {
            _host.ModBindings.Remove(_host.CurrentSecondLevelPath);
        }
        else
        {
            _host.ModBindings[_host.CurrentSecondLevelPath] = bindings;
        }

        _host.SaveConfig();
        UpdateShortcutRowVisibility();
    }

    internal void LoadLinkForCurrentMod(SecondLevelFolderItem? item)
    {
        _isLoadingModLink = true;
        ModLinkTextBox.Text = item is not null && _host.ModLinks.TryGetValue(item.Path, out string? link) ? link : string.Empty;
        _isLoadingModLink = false;
        UpdateLinkButtonState();
    }

    private void SaveCurrentModLink()
    {
        if (_isLoadingModLink || string.IsNullOrEmpty(_host.CurrentSecondLevelPath))
        {
            UpdateLinkButtonState();
            return;
        }

        string link = (ModLinkTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(link))
        {
            _host.ModLinks.Remove(_host.CurrentSecondLevelPath);
        }
        else
        {
            _host.ModLinks[_host.CurrentSecondLevelPath] = link;
        }

        _host.SaveConfig();
        UpdateLinkButtonState();
    }

    internal void UpdateLinkButtonState()
    {
        OpenModLinkButton.IsEnabled = !string.IsNullOrWhiteSpace(ModLinkTextBox.Text);
    }

    internal void UpdateShortcutRowVisibility()
    {
        EnsureShortcutRowCapacity(_visibleShortcutRows);
        for (int i = 0; i < ShortcutRowsPanel.Children.Count; i++)
        {
            ShortcutRowsPanel.Children[i].Visibility = i < _visibleShortcutRows ? Visibility.Visible : Visibility.Collapsed;
        }

        ShortcutRowsTextBlock.Text = T("ShortcutBindings.Rows", _visibleShortcutRows);
    }

    internal void RefreshVisibleShortcutDescriptionsForLanguage()
    {
        if (string.IsNullOrWhiteSpace(_host.CurrentSecondLevelPath)
            || !_host.ModBindings.TryGetValue(_host.CurrentSecondLevelPath, out List<ShortcutBinding>? bindings)
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
                    _host.ShortcutActionBoxes[i].Text = _host.GetShortcutBindingDisplayAction(bindings[i]);
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

    internal static bool IsModifierDown(VirtualKey key)
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
}
