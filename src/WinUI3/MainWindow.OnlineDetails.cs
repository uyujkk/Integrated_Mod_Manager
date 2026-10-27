using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void OnOnlineDetailHeroImageOpened(object sender, RoutedEventArgs e)
    {
        AnimateImageReveal(OnlineDetailHeroImage);
    }

    private async void OnOnlineDetailHeroImageTapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            await ShowImageViewerAsync(
                OnlineDetailHeroImage.Source,
                T("OnlineDetails.OnlineModImages"),
                _onlineDetailImageUrls,
                _onlineDetailImageIndex);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnOnlineDetailHeroImageTapped", ex);
        }
    }

    private async Task ShowOnlineModDetailsAsync(OnlineModCard mod)
    {
        TraceOnlineUiState("Details requested", mod.ItemId);
        _activeOnlineDetailMod = mod;
        RefreshOnlineDetailNavigation();
        string contextKey = GetOnlineDetailContextKey();
        await _onlineDetailState.RunLatestAsync(
            OpenOnlineDetailPaneAsync,
            token => GetOnlineModDetailsAsync(mod, token),
            (details, token) => TryTranslateOnlineModDetailsAsync(details).WaitAsync(token),
            (details, displayDetails) =>
            {
                PopulateOnlineDetailPane(mod, details, displayDetails);
                AnimateOnlineDetailContentRefresh();
            },
            ex => ReportOnlineActionErrorAsync(
                T("OnlineDetails.FailedToOpenModDetails") + ex.Message,
                T("OnlineDetails.DetailsLoadFailed")),
            () => !_onlineWindowClosed && ReferenceEquals(_activeOnlineDetailMod, mod)
                && string.Equals(contextKey, GetOnlineDetailContextKey(), StringComparison.Ordinal),
            loading =>
            {
                if (loading) PrepareOnlineDetailLoadingState(mod);
                else RefreshOnlineDetailActionState();
            });
    }

    private async Task<OnlineModDetails> GetOnlineModDetailsAsync(OnlineModCard mod, CancellationToken cancellationToken = default)
    {
        GameBananaDetails raw = await _gameBananaMetadataService.GetDetailsAsync(mod.ItemId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string text = string.Join(Environment.NewLine,
            new[] { raw.Summary, raw.Description }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new OnlineModDetails
        {
            Summary = raw.Summary, Description = raw.Description, ImageUrls = raw.ImageUrls.ToList(),
            AccessRequirementSummary = ExtractAccessRequirementSummary(text),
            ShortcutBindings = ExtractShortcutBindingsFromText(text)
        };
    }

    private async Task<List<ShortcutBinding>> BuildLocalizedShortcutBindingsAsync(List<ShortcutBinding> bindings)
    {
        if (bindings.Count == 0)
        {
            return [];
        }

        if (_currentLanguage != AppLanguage.ZhCn)
        {
            return bindings
                .Take(ShortcutScanSafetyLimit)
                .Select(binding => new ShortcutBinding(binding.Shortcut, binding.Action))
                .ToList();
        }

        var localized = new List<ShortcutBinding>();
        foreach (ShortcutBinding binding in bindings.Take(ShortcutScanSafetyLimit))
        {
            string action = binding.Action;
            if (!string.IsNullOrWhiteSpace(action))
            {
                try
                {
                    string translatedAction = await TranslateTextForDisplayAsync(action, "zh-CN");
                    if (!string.IsNullOrWhiteSpace(translatedAction))
                    {
                        action = translatedAction;
                    }
                }
                catch (Exception ex)
                {
                    LogApplicationIssue("Translate shortcut action", ex);
                }
            }

            localized.Add(new ShortcutBinding(binding.Shortcut, action));
        }

        return localized;
    }

    private static string ExtractAccessRequirementSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] keywords =
        [
            "patreon",
            "subscribe",
            "subscription",
            "paid",
            "payment",
            "paywall",
            "donat",
            "unlock",
            "exclusive",
            "early access",
            "free after",
            "public release",
            "supporter",
            "supporters",
            "boosty",
            "gumroad",
            "ko-fi",
            "kofi",
            "discord",
            "like goal",
            "unlock at",
            "available after"
        ];

        var matches = new List<string>();
        foreach (string rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = Regex.Replace(rawLine, @"\s+", " ").Trim(' ', '-', '•', '*', ':', '：');
            if (line.Length < 6)
            {
                continue;
            }

            if (keywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                && !matches.Contains(line, StringComparer.OrdinalIgnoreCase))
            {
                matches.Add(line);
                if (matches.Count >= 3)
                {
                    break;
                }
            }
        }

        return string.Join(Environment.NewLine, matches);
    }

    private static List<ShortcutBinding> ExtractShortcutBindingsFromText(string text)
    {
        var bindings = new List<ShortcutBinding>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return bindings;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = Regex.Replace(rawLine, @"\s+", " ").Trim();
            if (line.Length < 4)
            {
                continue;
            }

            if (TryParseShortcutBindingLine(line, out ShortcutBinding? binding) && binding is not null)
            {
                string identity = NormalizeShortcut(binding.Shortcut) + "|" + binding.Action.Trim();
                if (seen.Add(identity))
                {
                    bindings.Add(binding);
                    if (bindings.Count >= ShortcutScanSafetyLimit)
                    {
                        break;
                    }
                }
            }
        }

        return bindings;
    }

    private static bool TryParseShortcutBindingLine(string line, out ShortcutBinding? binding)
    {
        binding = null;
        string normalizedLine = line.Trim().TrimStart('-', '•', '*', '>', ' ');
        if (string.IsNullOrWhiteSpace(normalizedLine))
        {
            return false;
        }

        Match sentenceMatch = Regex.Match(
            normalizedLine,
            @"(?i)\b(?:press|use|hit|key|shortcut|hotkey)\s+(?<shortcut>.+?)\s+(?:to|for)\s+(?<action>.+)$");
        if (sentenceMatch.Success)
        {
            string shortcutCandidate = NormalizeShortcutDisplay(sentenceMatch.Groups["shortcut"].Value);
            string actionCandidate = sentenceMatch.Groups["action"].Value.Trim();
            if (LooksLikeShortcutCandidate(shortcutCandidate) && actionCandidate.Length >= 2)
            {
                binding = new ShortcutBinding(shortcutCandidate, actionCandidate);
                return true;
            }
        }

        foreach (string separator in new[] { " => ", " -> ", " → ", "：", ":", " - ", " – ", " — ", " = " })
        {
            int index = normalizedLine.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            string left = normalizedLine[..index].Trim(' ', '-', '•', '*', ':', '：');
            string right = normalizedLine[(index + separator.Length)..].Trim(' ', '-', '•', '*', ':', '：', '>', '=');
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                continue;
            }

            bool leftLooksLikeShortcut = LooksLikeShortcutCandidate(left);
            bool rightLooksLikeShortcut = LooksLikeShortcutCandidate(right);
            if (leftLooksLikeShortcut == rightLooksLikeShortcut)
            {
                continue;
            }

            string shortcut = NormalizeShortcutDisplay(leftLooksLikeShortcut ? left : right);
            string action = (leftLooksLikeShortcut ? right : left).Trim();
            if (!string.IsNullOrWhiteSpace(shortcut) && action.Length >= 2)
            {
                binding = new ShortcutBinding(shortcut, action);
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeShortcutCandidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim().Trim('[', ']', '(', ')');
        if (candidate.Length is 0 or > 40)
        {
            return false;
        }

        if (Regex.IsMatch(candidate, @"(?i)\b(author|description|download|preview|updated|version|image|requirement)\b"))
        {
            return false;
        }

        string[] parts = candidate.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        bool hasKey = false;
        foreach (string part in parts)
        {
            string token = NormalizeShortcutToken(part);
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (token is "Ctrl" or "Shift" or "Alt" or "Win")
            {
                continue;
            }

            hasKey = Regex.IsMatch(
                token,
                @"^(?:F(?:[1-9]|1[0-9]|2[0-4])|Num[0-9]|[A-Z0-9]|Esc|Enter|Tab|Space|Left|Right|Up|Down|Home|End|PageUp|PageDown|Insert|Delete|[`~!@#$%^&*()_\-+=\[\]{}\\|;:'"",.<>/?])$");
            if (!hasKey)
            {
                return false;
            }
        }

        return hasKey;
    }

    private static string NormalizeShortcutDisplay(string value)
    {
        string[] parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var normalized = new List<string>();
        foreach (string part in parts)
        {
            string token = NormalizeShortcutToken(part);
            if (!string.IsNullOrWhiteSpace(token))
            {
                normalized.Add(token);
            }
        }

        return normalized.Count == 0 ? string.Empty : string.Join("+", normalized);
    }

    private static string NormalizeShortcutToken(string value)
    {
        string token = value.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        token = token
            .Replace("Arrow", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Key", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        // A description part that was only "Key" or "Arrow" is not a key. Without this the
        // fallback read token[0] and the whole details pane failed to load.
        if (token.Length == 0)
        {
            return string.Empty;
        }

        return token.ToLowerInvariant() switch
        {
            "control" => "Ctrl",
            "ctrl" => "Ctrl",
            "shift" => "Shift",
            "alt" => "Alt",
            "menu" => "Alt",
            "win" => "Win",
            "windows" => "Win",
            "escape" => "Esc",
            "esc" => "Esc",
            "return" => "Enter",
            "enter" => "Enter",
            "space" => "Space",
            "spacebar" => "Space",
            "pageup" => "PageUp",
            "page up" => "PageUp",
            "pagedown" => "PageDown",
            "page down" => "PageDown",
            "insert" => "Insert",
            "delete" => "Delete",
            "left" => "Left",
            "right" => "Right",
            "up" => "Up",
            "down" => "Down",
            "home" => "Home",
            "end" => "End",
            "tab" => "Tab",
            "slash" => "/",
            "forward slash" => "/",
            "backslash" => "\\",
            "pipe" => "\\",
            "semicolon" => ";",
            "comma" => ",",
            "period" => ".",
            "dot" => ".",
            "minus" => "-",
            "hyphen" => "-",
            "equal" => "=",
            "equals" => "=",
            "quote" => "'",
            "apostrophe" => "'",
            "backtick" => "`",
            "grave" => "`",
            "left bracket" => "[",
            "right bracket" => "]",
            _ => NormalizeShortcutTokenFallback(token)
        };
    }

    private static string NormalizeShortcutTokenFallback(string token)
    {
        if (token.Length == 1)
        {
            return char.IsLetter(token[0]) ? token.ToUpperInvariant() : token;
        }

        if (Regex.IsMatch(token, @"^(?i)f(?:[1-9]|1[0-9]|2[0-4])$"))
        {
            return token.ToUpperInvariant();
        }

        Match numpadMatch = Regex.Match(token, @"^(?i)(?:num|numpad)\s*([0-9])$");
        if (numpadMatch.Success)
        {
            return "Num" + numpadMatch.Groups[1].Value;
        }

        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }

    private void PrepareOnlineDetailLoadingState(OnlineModCard mod)
    {
        OnlineDetailPaneTitleTextBlock.Text = mod.Title;
        string characterSource = mod.IsCharacterNameInferred ? T("Common.Detected") : string.Empty;
        OnlineDetailPaneMetaTextBlock.Text = $"{T("OnlineDetails.Character")}: {mod.CharacterName}{characterSource}    {T("OnlineDetails.Author")}: {mod.Author}";
        OnlineDetailPaneStatusTextBlock.Text = T("OnlineDetails.LoadingDetailsAndPreviewImages");
        OnlineDetailSummaryTextBlock.Text = T("OnlineDetails.PreparingTheModSummary");
        OnlineDetailDescriptionTextBlock.Text = string.Empty;
        OnlineDetailRequirementBorder.Visibility = Visibility.Collapsed;
        OnlineDetailTranslationNoteTextBlock.Visibility = Visibility.Collapsed;
        OnlineDetailDownloadHintTextBlock.Text = T("OnlineDetails.DownloadAndPageActionsWillBe");
        OpenOnlineDetailPageButton.Content = T("Common.OpenOriginalPage");
        DownloadOnlineDetailButton.Content = T("Common.Loading");
        OpenOnlineDetailPageButton.IsEnabled = !string.IsNullOrWhiteSpace(mod.ProfileUrl);
        DownloadOnlineDetailButton.IsEnabled = false;
        SelectOnlineDetailDownloadButton.IsEnabled = false;
        PopulateOnlineDetailImages([]);
        OnlineDetailHeroPlaceholderTextBlock.Text = T("OnlineDetails.LoadingPreviewImages");
    }

    private async Task OpenOnlineDetailPaneAsync()
    {
        TraceOnlineUiState("Pane opening");
        int animationVersion = ++_onlineDetailAnimationVersion;
        bool wasOpen = IsOnlineDetailOpen;
        OnlineDetailDrawer.Visibility = Visibility.Visible;
        await Task.Delay(1);
        if (_onlineWindowClosed || animationVersion != _onlineDetailAnimationVersion) return;
        UpdateOnlineResponsiveLayout(Bounds.Width);
        UpdateOnlineWorkspaceWidthConstraint();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_onlineWindowClosed || animationVersion != _onlineDetailAnimationVersion) return;
            UpdateOnlineResponsiveLayout(Bounds.Width);
            UpdateOnlineWorkspaceWidthConstraint();
            // The cards re-flowed around the drawer; bring the one being shown back into view.
            int index = GetActiveOnlineDetailIndex();
            if (index >= 0 && GetOnlineResultsElement() is ListViewBase results)
                results.ScrollIntoView(_onlinePage.VisibleMods[index]);
        });
        if (animationVersion != _onlineDetailAnimationVersion)
        {
            return;
        }

        // Animate the panel inside the drawer: the drawer itself is placed by layout through
        // its visual Offset, so animating that would pull it to the left edge.
        var visual = ElementCompositionPreview.GetElementVisual(OnlineDetailPaneBorder);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Offset");
        if (ShouldReduceAnimations)
        {
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
            return;
        }

        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        visual.Opacity = wasOpen ? 0.72f : 0.12f;
        visual.Offset = new Vector3(wasOpen ? 14f : 56f, 0f, 0f);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(wasOpen ? 150 : 230);
        fade.InsertKeyFrame(1f, 1f, easing);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Duration = TimeSpan.FromMilliseconds(wasOpen ? 180 : 270);
        slide.InsertKeyFrame(1f, Vector3.Zero, easing);
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Offset", slide);
    }

    private void AnimateOnlineDetailContentRefresh()
    {
        if (ShouldReduceAnimations)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(OnlineDetailPaneBorder);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(160);
        fade.InsertKeyFrame(0f, 0.78f);
        fade.InsertKeyFrame(1f, 1f);
        visual.StartAnimation("Opacity", fade);
    }

    private UIElement CreateOnlineDetailImage(string? imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
        {
            var image = new Image
            {
                Stretch = Stretch.UniformToFill
            };
            _ = SetCachedOnlineImageAsync(image, imageUrl!);
            return image;
        }

        return new TextBlock
        {
            Text = T("OnlineDetails.PreviewFailedToLoad"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        };
    }

    private async Task<OnlineModDetails> TryTranslateOnlineModDetailsAsync(OnlineModDetails details)
    {
        if (_currentLanguage != AppLanguage.ZhCn)
        {
            return details;
        }

        try
        {
            string translatedSummary = string.IsNullOrWhiteSpace(details.Summary)
                ? details.Summary
                : await TranslateTextForDisplayAsync(details.Summary, "zh-CN");
            string translatedDescription = string.IsNullOrWhiteSpace(details.Description)
                ? details.Description
                : await TranslateTextForDisplayAsync(details.Description, "zh-CN");
            string translatedRequirement = string.IsNullOrWhiteSpace(details.AccessRequirementSummary)
                ? details.AccessRequirementSummary
                : await TranslateTextForDisplayAsync(details.AccessRequirementSummary, "zh-CN");
            List<ShortcutBinding> translatedBindings = await BuildLocalizedShortcutBindingsAsync(details.ShortcutBindings);

            return new OnlineModDetails
            {
                Summary = string.IsNullOrWhiteSpace(translatedSummary) ? details.Summary : translatedSummary,
                Description = string.IsNullOrWhiteSpace(translatedDescription) ? details.Description : translatedDescription,
                ImageUrls = details.ImageUrls,
                TranslationNote = T("OnlineDetails.TheContentBelowWasTranslatedAutomatically"),
                AccessRequirementSummary = string.IsNullOrWhiteSpace(translatedRequirement) ? details.AccessRequirementSummary : translatedRequirement,
                ShortcutBindings = translatedBindings.Count > 0 ? translatedBindings : details.ShortcutBindings
            };
        }
        catch
        {
            return new OnlineModDetails
            {
                Summary = details.Summary,
                Description = details.Description,
                ImageUrls = details.ImageUrls,
                TranslationNote = T("OnlineDetails.AutomaticTranslationIsUnavailableRightNow"),
                AccessRequirementSummary = details.AccessRequirementSummary,
                ShortcutBindings = details.ShortcutBindings
            };
        }
    }

    private void PopulateOnlineDetailPane(OnlineModCard mod, OnlineModDetails rawDetails, OnlineModDetails displayDetails)
    {
        TraceOnlineUiState("Details publishing", mod.ItemId);
        TrackedModOrigin? installedOrigin = FindInstalledOnlineMod(mod);
        OnlineDetailPaneTitleTextBlock.Text = mod.Title;
        string characterSource = mod.IsCharacterNameInferred ? T("Common.Detected") : string.Empty;
        OnlineDetailPaneMetaTextBlock.Text = $"{T("OnlineDetails.Character")}: {mod.CharacterName}{characterSource}    {T("OnlineDetails.Author")}: {mod.Author}";

        var statusParts = new List<string>
        {
            $"{T("OnlineDetails.Category")}: {mod.RootCategoryName}",
            $"{T("OnlineDetails.Updated")}: {mod.UpdatedAt:yyyy-MM-dd HH:mm}",
            $"ID: {mod.ItemId}"
        };

        if (installedOrigin is not null)
        {
            statusParts.Insert(0, T("Common.Installed"));
        }

        if (displayDetails.ShortcutBindings.Count > 0)
        {
            statusParts.Add(T("OnlineDetails.ShortcutNotesDetected", displayDetails.ShortcutBindings.Count));
        }

        OnlineDetailPaneStatusTextBlock.Text = string.Join("    ", statusParts);

        if (!string.IsNullOrWhiteSpace(displayDetails.AccessRequirementSummary))
        {
            OnlineDetailRequirementBorder.Visibility = Visibility.Visible;
            OnlineDetailRequirementTitleTextBlock.Text = T("Common.PossibleExtraAccessRequirements");
            OnlineDetailRequirementTextBlock.Text = displayDetails.AccessRequirementSummary;
        }
        else
        {
            OnlineDetailRequirementBorder.Visibility = Visibility.Collapsed;
            OnlineDetailRequirementTitleTextBlock.Text = string.Empty;
            OnlineDetailRequirementTextBlock.Text = string.Empty;
        }

        OnlineDetailSummaryTextBlock.Text = string.IsNullOrWhiteSpace(displayDetails.Summary)
            ? T("OnlineDetails.ThisEntryDoesNotProvideA")
            : displayDetails.Summary;
        OnlineDetailDescriptionTextBlock.Text = displayDetails.Description ?? string.Empty;

        if (string.IsNullOrWhiteSpace(displayDetails.TranslationNote))
        {
            OnlineDetailTranslationNoteTextBlock.Visibility = Visibility.Collapsed;
            OnlineDetailTranslationNoteTextBlock.Text = string.Empty;
        }
        else
        {
            OnlineDetailTranslationNoteTextBlock.Visibility = Visibility.Visible;
            OnlineDetailTranslationNoteTextBlock.Text = displayDetails.TranslationNote;
        }

        if (installedOrigin is not null)
        {
            OnlineDetailDownloadHintTextBlock.Text = T("OnlineDetails.InstalledAtYouCanStillDownload", installedOrigin.Path);
        }
        else if (!string.IsNullOrWhiteSpace(rawDetails.AccessRequirementSummary))
        {
            OnlineDetailDownloadHintTextBlock.Text = T("OnlineDetails.TheDownloadStatusWillMentionPossible", rawDetails.AccessRequirementSummary);
        }
        else
        {
            OnlineDetailDownloadHintTextBlock.Text = T("OnlineDetails.YouCanOpenTheOriginalPage");
        }

        UpdateOnlineDetailDownloadControls(mod);

        PopulateOnlineDetailImages(displayDetails.ImageUrls);
    }

    private void UpdateOnlineDetailDownloadControls(OnlineModCard mod)
    {
        bool isInstalled = FindInstalledOnlineMod(mod) is not null;
        OnlineDownloadCandidate? defaultFile = OnlineDownloadSelectionPolicy.SelectDefault(mod.DownloadFiles);
        int availableFileCount = mod.DownloadFiles.Count(file => !string.IsNullOrWhiteSpace(file.DownloadUrl));

        OpenOnlineDetailPageButton.Content = T("Common.OpenOriginalPage");
        SelectOnlineDetailDownloadButton.Content = availableFileCount > 0
            ? T("OnlineDetails.SelectFile", availableFileCount)
            : T("Common.SelectFile");
        DownloadOnlineDetailButton.Content = !string.IsNullOrWhiteSpace(mod.DownloadUrl) || mod.ItemId > 0
            ? (isInstalled
                ? T("OnlineDetails.DownloadLatestAgain")
                : T("Common.DownloadLatestArchive"))
            : T("OnlineDetails.NoDownload");

        OpenOnlineDetailPageButton.IsEnabled = !string.IsNullOrWhiteSpace(mod.ProfileUrl);
        SelectOnlineDetailDownloadButton.IsEnabled = mod.ItemId > 0;
        DownloadOnlineDetailButton.IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl) || mod.ItemId > 0;
        if (_onlineDetailState.IsLoading || IsOnlineDownloadActionBlocked(mod))
        {
            SelectOnlineDetailDownloadButton.IsEnabled = false;
            DownloadOnlineDetailButton.IsEnabled = false;
        }

        if (defaultFile is not null)
        {
            string size = defaultFile.FileSizeBytes > 0 ? $" · {FormatFileSize(defaultFile.FileSizeBytes)}" : string.Empty;
            string date = defaultFile.AddedAt > DateTimeOffset.MinValue
                ? $" · {defaultFile.AddedAt:yyyy-MM-dd}"
                : string.Empty;
            OnlineDetailDownloadSelectionTextBlock.Text = T("OnlineDetails.Default", defaultFile.FileName, size, date);
            ToolTipService.SetToolTip(OnlineDetailDownloadSelectionTextBlock, OnlineDetailDownloadSelectionTextBlock.Text);
        }
        else if (!string.IsNullOrWhiteSpace(mod.DownloadUrl) || mod.ItemId > 0)
        {
            OnlineDetailDownloadSelectionTextBlock.Text = T("OnlineDetails.TheNewestAvailableArchiveIsDownloaded");
            ToolTipService.SetToolTip(OnlineDetailDownloadSelectionTextBlock, null);
        }
        else
        {
            OnlineDetailDownloadSelectionTextBlock.Text = T("OnlineDetails.NoDownloadableFileIsCurrentlyAvailable");
            ToolTipService.SetToolTip(OnlineDetailDownloadSelectionTextBlock, null);
        }
    }

    private void ResetOnlineDetailPaneToPlaceholder()
    {
        OnlineDetailPaneTitleTextBlock.Text = T("OnlineDetails.ModDetailsAndPreview");
        OnlineDetailPaneMetaTextBlock.Text = T("OnlineDetails.SelectAnOnlineModFromThe");
        OnlineDetailPaneStatusTextBlock.Text = string.Empty;
        OnlineDetailSummaryTextBlock.Text = string.Empty;
        OnlineDetailDescriptionTextBlock.Text = string.Empty;
        OnlineDetailDownloadHintTextBlock.Text = T("OnlineDetails.OpenPageAndDownloadExtractActions");
        OnlineDetailRequirementBorder.Visibility = Visibility.Collapsed;
        OnlineDetailRequirementTitleTextBlock.Text = T("Common.PossibleExtraAccessRequirements");
        OnlineDetailRequirementTextBlock.Text = string.Empty;
        OnlineDetailTranslationNoteTextBlock.Visibility = Visibility.Collapsed;
        OnlineDetailTranslationNoteTextBlock.Text = string.Empty;
        OpenOnlineDetailPageButton.IsEnabled = false;
        SelectOnlineDetailDownloadButton.IsEnabled = false;
        DownloadOnlineDetailButton.IsEnabled = false;
        OpenOnlineDetailPageButton.Content = T("Common.OpenOriginalPage");
        SelectOnlineDetailDownloadButton.Content = T("Common.SelectFile");
        DownloadOnlineDetailButton.Content = T("Common.DownloadLatestArchive");
        OnlineDetailDownloadSelectionTextBlock.Text = T("OnlineDetails.TheNewestAvailableArchiveIsDownloaded2");
        ToolTipService.SetToolTip(OnlineDetailDownloadSelectionTextBlock, null);
        PopulateOnlineDetailImages([]);
    }

    private void PopulateOnlineDetailImages(List<string> imageUrls)
    {
        OnlineDetailThumbnailPanel.Children.Clear();
        List<string> usableImages = imageUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Skip(1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _onlineDetailImageUrls.Clear();
        _onlineDetailImageUrls.AddRange(usableImages);
        _onlineDetailImageIndex = 0;

        if (usableImages.Count == 0)
        {
            _onlineHeroImageRequestVersion++;
            OnlineDetailHeroImage.Source = null;
            OnlineDetailHeroImage.Visibility = Visibility.Collapsed;
            OnlineDetailHeroPlaceholderTextBlock.Visibility = Visibility.Visible;
            OnlineDetailHeroPlaceholderTextBlock.Text = T("OnlineDetails.ThisEntryDoesNotProvideUsable");
            OnlineDetailThumbnailScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        ShowOnlineDetailHeroImage(usableImages[0], 0);

        if (usableImages.Count == 1)
        {
            OnlineDetailThumbnailScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        OnlineDetailThumbnailScrollViewer.Visibility = Visibility.Visible;
        for (int index = 0; index < Math.Min(usableImages.Count, 10); index++)
        {
            int imageIndex = index;
            string imageUrl = usableImages[index];
            Button thumbButton = new()
            {
                Width = 112,
                Height = 72,
                Padding = new Thickness(0),
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Content = new Border
                {
                    Style = (Style)Application.Current.Resources["InsetBorderStyle"],
                    Child = CreateOnlineDetailImage(imageUrl)
                }
            };
            thumbButton.Click += (_, _) => ShowOnlineDetailHeroImage(imageUrl, imageIndex);
            OnlineDetailThumbnailPanel.Children.Add(thumbButton);
        }
    }

    private async void ShowOnlineDetailHeroImage(string? imageUrl, int imageIndex = 0)
    {
        try
        {
            TraceOnlineUiState("Hero requested");
            _onlineDetailImageIndex = Math.Clamp(
                imageIndex,
                0,
                Math.Max(0, _onlineDetailImageUrls.Count - 1));
            int requestVersion = ++_onlineHeroImageRequestVersion;
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
            {
                OnlineDetailHeroImage.Source = null;
                OnlineDetailHeroImage.Visibility = Visibility.Visible;
                OnlineDetailHeroPlaceholderTextBlock.Visibility = Visibility.Visible;
                OnlineDetailHeroPlaceholderTextBlock.Text = T("OnlineDetails.LoadingHighResolutionPreview");
                BitmapImage? bitmap = await LoadOnlineBitmapAsync(imageUrl!);
                if (requestVersion != _onlineHeroImageRequestVersion)
                {
                    return;
                }

                if (bitmap is not null)
                {
                    TraceOnlineUiState("Hero assigning decoded bitmap");
                    OnlineDetailHeroImage.Source = bitmap;
                    OnlineDetailHeroImage.Visibility = Visibility.Visible;
                    OnlineDetailHeroPlaceholderTextBlock.Visibility = Visibility.Collapsed;
                    OnlineDetailHeroPlaceholderTextBlock.Text = string.Empty;
                    return;
                }
            }

            OnlineDetailHeroImage.Source = null;
            OnlineDetailHeroImage.Visibility = Visibility.Collapsed;
            OnlineDetailHeroPlaceholderTextBlock.Visibility = Visibility.Visible;
            OnlineDetailHeroPlaceholderTextBlock.Text = T("OnlineDetails.FailedToLoadThePreviewImage");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("ShowOnlineDetailHeroImage", ex);
        }
    }

    private async Task<string> TranslateTextForDisplayAsync(string text, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        List<string> chunks = SplitTextForTranslation(text, 2500);
        var builder = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            string translatedChunk = await TranslateChunkAsync(chunks[i], targetLanguage);
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(translatedChunk.Trim());
        }

        return builder.ToString().Trim();
    }

    private async Task<string> TranslateChunkAsync(string text, string targetLanguage)
    {
        string requestUrl = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(text)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
        {
            return text;
        }

        var builder = new StringBuilder();
        foreach (JsonElement sentence in root[0].EnumerateArray())
        {
            if (sentence.ValueKind == JsonValueKind.Array && sentence.GetArrayLength() > 0 && sentence[0].ValueKind == JsonValueKind.String)
            {
                builder.Append(sentence[0].GetString());
            }
        }

        return string.IsNullOrWhiteSpace(builder.ToString()) ? text : builder.ToString();
    }

    private static List<string> SplitTextForTranslation(string text, int maxChunkLength)
    {
        List<string> chunks = [];
        string[] paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.None);
        var builder = new StringBuilder();

        foreach (string paragraph in paragraphs)
        {
            string current = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            if (builder.Length > 0 && builder.Length + current.Length + 2 > maxChunkLength)
            {
                chunks.Add(builder.ToString());
                builder.Clear();
            }

            if (current.Length > maxChunkLength)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString());
                    builder.Clear();
                }

                for (int index = 0; index < current.Length; index += maxChunkLength)
                {
                    chunks.Add(current.Substring(index, Math.Min(maxChunkLength, current.Length - index)));
                }
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }
            builder.Append(current);
        }

        if (builder.Length > 0)
        {
            chunks.Add(builder.ToString());
        }

        return chunks.Count == 0 ? [text] : chunks;
    }

    private async void OnCloseOnlineDetailsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await CloseOnlineDetailPaneAsync();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnCloseOnlineDetailsClicked", ex);
        }
    }

    // ‹ › in the drawer header step through the Mods on this page, in the order shown.
    // A refreshed card can replace the active one, so match by item id as well.
    private int GetActiveOnlineDetailIndex()
    {
        if (_activeOnlineDetailMod is not { } active) return -1;
        for (int index = 0; index < _onlinePage.VisibleMods.Count; index++)
        {
            OnlineModCard candidate = _onlinePage.VisibleMods[index];
            if (ReferenceEquals(candidate, active) || (active.ItemId > 0 && candidate.ItemId == active.ItemId)) return index;
        }
        return -1;
    }

    private void RefreshOnlineDetailNavigation()
    {
        int index = GetActiveOnlineDetailIndex();
        OnlineDetailPreviousButton.IsEnabled = index > 0;
        OnlineDetailNextButton.IsEnabled = index >= 0 && index < _onlinePage.VisibleMods.Count - 1;
        foreach ((Button button, string label) in new[]
        {
            (OnlineDetailPreviousButton, T("OnlineDetails.PreviousMod")),
            (OnlineDetailNextButton, T("OnlineDetails.NextMod"))
        })
        {
            ToolTipService.SetToolTip(button, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        }
    }

    private async void OnOnlineDetailPreviousClicked(object sender, RoutedEventArgs e) => await ShowAdjacentOnlineDetailAsync(-1);

    private async void OnOnlineDetailNextClicked(object sender, RoutedEventArgs e) => await ShowAdjacentOnlineDetailAsync(1);

    private async Task ShowAdjacentOnlineDetailAsync(int step)
    {
        int index = GetActiveOnlineDetailIndex();
        int target = index + step;
        if (index < 0 || target < 0 || target >= _onlinePage.VisibleMods.Count) return;
        OnlineModCard mod = _onlinePage.VisibleMods[target];
        if (GetOnlineResultsElement() is ListViewBase results) results.ScrollIntoView(mod);
        await RunUiActionAsync("ShowOnlineModDetailsAsync", () => ShowOnlineModDetailsAsync(mod));
    }

    private async Task CloseOnlineDetailPaneAsync()
    {
        if (_onlineWindowClosed) return;
        // The cards re-flow to more columns on close; keep the one just viewed on screen.
        int lastShownIndex = GetActiveOnlineDetailIndex();
        OnlineModCard? lastShown = lastShownIndex >= 0 ? _onlinePage.VisibleMods[lastShownIndex] : null;
        // Close takes effect before the animation awaits, not 165ms later.
        InvalidateOnlineDetailRequest();
        _activeOnlineDetailMod = null;
        DownloadOnlineDetailButton.IsEnabled = false;
        SelectOnlineDetailDownloadButton.IsEnabled = false;
        int animationVersion = ++_onlineDetailAnimationVersion;
        var visual = ElementCompositionPreview.GetElementVisual(OnlineDetailPaneBorder);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Offset");

        if (!ShouldReduceAnimations && IsOnlineDetailOpen)
        {
            var compositor = visual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = TimeSpan.FromMilliseconds(130);
            fade.InsertKeyFrame(1f, 0f, easing);
            var slide = compositor.CreateVector3KeyFrameAnimation();
            slide.Duration = TimeSpan.FromMilliseconds(160);
            slide.InsertKeyFrame(1f, new Vector3(42f, 0f, 0f), easing);
            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Offset", slide);
            await Task.Delay(165);
        }

        if (_onlineWindowClosed || animationVersion != _onlineDetailAnimationVersion)
        {
            return;
        }

        _activeOnlineDetailMod = null;
        OnlineDetailDrawer.Visibility = Visibility.Collapsed;
        UpdateOnlineResponsiveLayout(Bounds.Width);
        UpdateOnlineWorkspaceWidthConstraint();
        if (lastShown is not null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_onlineWindowClosed && _onlinePage.VisibleMods.Contains(lastShown) && GetOnlineResultsElement() is ListViewBase results)
                    results.ScrollIntoView(lastShown);
            });
        }
        ResetOnlineDetailPaneToPlaceholder();
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Offset");
        visual.Opacity = 1f;
        visual.Offset = Vector3.Zero;
    }

    private async void OnOpenOnlineDetailPageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeOnlineDetailMod is null)
            {
                return;
            }

            await OpenExternalUrlAsync(_activeOnlineDetailMod.ProfileUrl, T("Common.OpenPageFailed"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnOpenOnlineDetailPageClicked", ex);
        }
    }

    private async void OnDownloadOnlineDetailClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeOnlineDetailMod is null)
            {
                return;
            }

            await DownloadAndExtractOnlineModAsync(_activeOnlineDetailMod);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnDownloadOnlineDetailClicked", ex);
        }
    }

    private async void OnSelectOnlineDetailDownloadClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeOnlineDetailMod is null)
            {
                return;
            }

            OnlineModCard sourceMod = _activeOnlineDetailMod;
            WorkspaceRepository? downloadRepository = GetSelectedRepository();
            OnlineDownloadContext downloadContext = CaptureOnlineDownloadContext(downloadRepository);
            bool useEndfieldMetadata = IsEndfieldRepository(downloadRepository);
            IReadOnlyCollection<string> characterAliases = GetCharacterFolderAliases(sourceMod);
            string detailContext = GetOnlineDetailContextKey();
            using OnlineDownloadActionAdapter.Lease? action = _onlineDownloadActions.TryBegin(
                downloadContext, GetOnlineDownloadIdentity(sourceMod));
            if (action is null) return;
            try
            {
                RefreshOnlineDetailActionState();
                OnlineModCard effectiveMod = await RefreshOnlineModDownloadInfoAsync(sourceMod, useEndfieldMetadata, action.Token);
                action.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_activeOnlineDetailMod, sourceMod)
                    || !string.Equals(detailContext, GetOnlineDetailContextKey(), StringComparison.Ordinal)) return;

                _activeOnlineDetailMod = effectiveMod;
                UpdateOnlineDetailDownloadControls(effectiveMod);
                IReadOnlyList<OnlineDownloadCandidate> candidates =
                    OnlineDownloadSelectionPolicy.OrderForManualSelection(effectiveMod.DownloadFiles);
                if (candidates.Count == 0)
                {
                    await ShowMessageAsync(
                        T("OnlineDetails.ThisItemDidNotReturnSelectable"),
                        T("OnlineDetails.NoFileList"));
                    return;
                }

                OnlineDownloadCandidate? selectedFile = await ShowOnlineDownloadFilePickerAsync(effectiveMod, candidates);
                action.Token.ThrowIfCancellationRequested();
                if (selectedFile is null || !ReferenceEquals(_activeOnlineDetailMod, effectiveMod)
                    || !string.Equals(detailContext, GetOnlineDetailContextKey(), StringComparison.Ordinal)) return;
                await DownloadAndExtractOnlineModCoreAsync(effectiveMod, selectedFile, false,
                    downloadContext, useEndfieldMetadata, characterAliases, action);
            }
            catch (OperationCanceledException) when (action.Token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                LogApplicationIssue("Select online download file", ex);
                await ReportOnlineActionErrorAsync(T("OnlineDetails.FailedToSelectDownloadFile") + ex.Message,
                    T("Common.OnlineDownloadFailed"));
            }
            finally
            {
                action.Dispose();
                RefreshOnlineDetailActionState();
                if (!_onlineWindowClosed) RefreshOnlinePaneV2();
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnSelectOnlineDetailDownloadClicked", ex);
        }
    }

    private async Task<OnlineDownloadCandidate?> ShowOnlineDownloadFilePickerAsync(
        OnlineModCard mod,
        IReadOnlyList<OnlineDownloadCandidate> candidates)
    {
        DialogViewport viewport = DialogViewportPolicy.Selection(RootGrid.ActualWidth, RootGrid.ActualHeight);
        var fileList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        OnlineDownloadCandidate? defaultFile = OnlineDownloadSelectionPolicy.SelectDefault(candidates);
        ListViewItem? defaultItem = null;
        foreach (OnlineDownloadCandidate candidate in candidates)
        {
            string fileName = string.IsNullOrWhiteSpace(candidate.FileName)
                ? T("OnlineDetails.File", candidate.FileId)
                : candidate.FileName;

            var title = new TextBlock
            {
                Text = fileName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2
            };

            var metadata = new List<string>();
            if (!string.IsNullOrWhiteSpace(candidate.Version))
            {
                metadata.Add(T("OnlineDetails.Version", candidate.Version));
            }
            if (candidate.FileSizeBytes > 0)
            {
                metadata.Add(FormatFileSize(candidate.FileSizeBytes));
            }
            if (candidate.AddedAt > DateTimeOffset.MinValue)
            {
                metadata.Add(candidate.AddedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
            }
            metadata.Add(candidate.IsArchived ? T("OnlineDetails.Archived") : T("OnlineDetails.Current"));
            if (!candidate.IsSupportedArchive)
            {
                metadata.Add(T("OnlineDetails.CannotAutoExtract"));
            }

            var details = new StackPanel { Spacing = 3 };
            details.Children.Add(title);
            details.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", metadata),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(candidate.Description))
            {
                details.Children.Add(new TextBlock
                {
                    Text = candidate.Description,
                    Style = (Style)Application.Current.Resources["MutedTextStyle"],
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 2
                });
            }

            var item = new ListViewItem
            {
                Content = details,
                Tag = candidate,
                Padding = new Thickness(10, 8, 10, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsEnabled = candidate.IsSupportedArchive
            };
            fileList.Items.Add(item);
            if (ReferenceEquals(candidate, defaultFile))
            {
                defaultItem = item;
            }
        }

        fileList.SelectedItem = defaultItem
            ?? fileList.Items.OfType<ListViewItem>().FirstOrDefault(item => item.IsEnabled);

        var content = new Grid
        {
            Width = Math.Min(620, viewport.MaxWidth - 48),
            Height = Math.Min(480, viewport.ContentHeight),
            RowSpacing = 10
        };
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new TextBlock
        {
            Text = T("OnlineDetails.TheNewestCurrentArchiveIsSelected"),
            Style = (Style)Application.Current.Resources["MutedTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(fileList, 1);
        content.Children.Add(fileList);

        var dialog = new ContentDialog
        {
            Title = T("OnlineDetails.SelectDownloadFile", mod.Title),
            Content = content,
            PrimaryButtonText = T("OnlineDetails.DownloadSelected"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = fileList.SelectedItem is ListViewItem,
            RequestedTheme = RootGrid.ActualTheme,
            XamlRoot = RootGrid.XamlRoot
        };
        fileList.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = fileList.SelectedItem is ListViewItem { IsEnabled: true };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            && fileList.SelectedItem is ListViewItem { Tag: OnlineDownloadCandidate selected }
                ? selected
                : null;
    }
}
