using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ModFolderCopier.WinUI;

// Character and category names, Mod details and cards the flow reads; the page browses with the same ones (moved from OnlinePage).
internal sealed partial class OnlineDownloadService
{
    internal OnlineDuplicateKind GetOnlineDuplicateKind(OnlineModCard mod, OnlineDownloadCandidate? file, OnlineDownloadContext context)
        => OnlineDuplicatePolicy.Classify(file?.FileId, _services.Tracking.Origins.Values.Where(origin =>
            OnlineInstallationDetectionPolicy.Matches(mod.ItemId, mod.ProfileUrl, origin.ItemId, origin.ProfileUrl)
            && Directory.Exists(origin.Path) && IsPathInsideDirectory(origin.Path, context.RepositoryPath)).Select(origin => origin.DownloadFileId));

    /// <summary>Which downloads are being prepared or running, so the same Mod is not started twice.</summary>
    internal OnlineDownloadActionAdapter DownloadActions { get; } = new();

    private int _onlineInstallBusyCount;

    internal static string GetOnlineDownloadIdentity(OnlineModCard mod)
        => mod.ItemId > 0 ? $"GameBanana:{mod.ItemId}" : mod.ProfileUrl;

    internal void SetOnlineInstallBusy(bool busy)
    {
        _onlineInstallBusyCount = Math.Max(0, _onlineInstallBusyCount + (busy ? 1 : -1));
        _host.SetBusyState(_onlineInstallBusyCount > 0);
    }

    internal async Task ReportOnlineActionErrorAsync(string message, string title, Exception? error = null)
    {
        if (_services.IsClosing) return;
        try { await _services.Dialogs.ErrorAsync(title, message, error); }
        catch (Exception ex) { _host.LogApplicationIssue("Report online action error", ex); }
    }

    internal static readonly HashSet<string> GenericOnlineCharacterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "operator", "operators", "character", "characters", "skin", "skins",
        "other", "others", "misc", "miscellaneous", "uncategorized", "unclassified"
    };

    /// <summary>A folder the user may choose to have created (issue #6); created only after confirmation.</summary>
    internal sealed record NewCharacterFolderChoice(string Path);

    /// <summary>The download whose progress the Online page's progress bar shows.</summary>
    internal string? ForegroundDownloadTaskId { get; set; }

    internal string GetOnlineCharacterDisplayName(OnlineCharacterOption character)
    {
        return IsChineseUi
            ? character.DisplayNameZh
            : character.DisplayNameEn;
    }

    internal string GetOnlineCharacterDisplayName(EndfieldCharacterInfo character)
    {
        return IsChineseUi ? character.ChineseName : character.EnglishName;
    }

    internal string GetOnlineCharacterDisplayName(string characterName)
    {
        OnlineCharacterOption? option = _services.Online.Page.CharacterOptions.FirstOrDefault(item =>
            string.Equals(item.Key, characterName, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            return GetOnlineCharacterDisplayName(option);
        }

        if (_services.OnlineSource.IsEndfield(_services.Repositories.Selected))
        {
            EndfieldCharacterInfo? character = FindEndfieldCharacter(characterName);
            return character is null ? characterName : GetOnlineCharacterDisplayName(character);
        }

        return characterName;
    }

    internal string ResolveOnlineCharacterName(string? categoryName, string title, bool useEndfieldMetadata)
    {
        string normalizedCategory = categoryName?.Trim() ?? string.Empty;
        if (useEndfieldMetadata)
        {
            EndfieldCharacterInfo? categoryCharacter = FindEndfieldCharacter(normalizedCategory);
            if (categoryCharacter is not null)
            {
                return categoryCharacter.EnglishName;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedCategory)
            && !GenericOnlineCharacterNames.Contains(normalizedCategory))
        {
            return normalizedCategory;
        }

        if (useEndfieldMetadata)
        {
            EndfieldCharacterInfo? titleCharacter = EndfieldCharacterCatalog.BuiltIn.FirstOrDefault(character =>
                character.AllNames.Any(alias => OnlineCharacterCatalogMatcher.ContainsAlias(title, alias)));
            if (titleCharacter is not null)
            {
                return titleCharacter.EnglishName;
            }
        }

        return "Uncategorized";
    }

    internal EndfieldCharacterInfo? FindEndfieldCharacter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string normalized = OnlineCharacterCatalogMatcher.NormalizeLookup(name);
        return EndfieldCharacterCatalog.BuiltIn.FirstOrDefault(character =>
            character.AllNames.Any(alias => string.Equals(
                OnlineCharacterCatalogMatcher.NormalizeLookup(alias),
                normalized,
                StringComparison.OrdinalIgnoreCase)));
    }

    internal static bool IsGenericOnlineCharacterName(string? categoryName)
    {
        return string.IsNullOrWhiteSpace(categoryName)
            || GenericOnlineCharacterNames.Contains(categoryName.Trim());
    }

    internal IReadOnlyCollection<string> GetCharacterFolderAliases(OnlineModCard mod)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddAlias(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsGenericOnlineCharacterName(value))
            {
                aliases.Add(value.Trim());
            }
        }

        AddAlias(mod.CharacterName);

        string normalizedCharacterName = OnlineCharacterCatalogMatcher.NormalizeLookup(mod.CharacterName);
        OnlineCharacterOption? option = _services.Online.Page.CharacterOptions.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(mod.CategoryId)
                && string.Equals(item.CategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(OnlineCharacterCatalogMatcher.NormalizeLookup(item.Key), normalizedCharacterName, StringComparison.OrdinalIgnoreCase)
            || item.Aliases.Any(alias => string.Equals(
                OnlineCharacterCatalogMatcher.NormalizeLookup(alias),
                normalizedCharacterName,
                StringComparison.OrdinalIgnoreCase)));
        if (option is not null)
        {
            AddAlias(option.Key);
            AddAlias(option.DisplayNameZh);
            AddAlias(option.DisplayNameEn);
            foreach (string alias in option.Aliases)
            {
                AddAlias(alias);
            }
        }

        EndfieldCharacterInfo? endfieldCharacter = FindEndfieldCharacter(mod.CharacterName)
            ?? EndfieldCharacterCatalog.BuiltIn.FirstOrDefault(character =>
                !string.IsNullOrWhiteSpace(mod.CategoryId)
                && string.Equals(
                    character.GameBananaCategoryId,
                    mod.CategoryId,
                    StringComparison.OrdinalIgnoreCase));
        if (endfieldCharacter is not null)
        {
            foreach (string alias in endfieldCharacter.AllNames)
            {
                AddAlias(alias);
            }
        }

        return aliases;
    }

    internal (string? Chinese, string? English) GetCharacterFolderNames(OnlineModCard mod)
    {
        string normalized = OnlineCharacterCatalogMatcher.NormalizeLookup(mod.CharacterName);
        OnlineCharacterOption? option = _services.Online.Page.CharacterOptions.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(mod.CategoryId) && string.Equals(item.CategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(OnlineCharacterCatalogMatcher.NormalizeLookup(item.Key), normalized, StringComparison.OrdinalIgnoreCase));
        EndfieldCharacterInfo? endfield = FindEndfieldCharacter(mod.CharacterName)
            ?? EndfieldCharacterCatalog.BuiltIn.FirstOrDefault(character => !string.IsNullOrWhiteSpace(mod.CategoryId)
                && string.Equals(character.GameBananaCategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase));
        string? chinese = endfield?.ChineseName ?? option?.DisplayNameZh;
        string? english = endfield?.EnglishName ?? option?.DisplayNameEn;
        // A bare site category name still names the folder in its own script.
        if (!IsGenericOnlineCharacterName(mod.CharacterName))
        {
            if (mod.CharacterName.Any(character => character is >= '一' and <= '鿿')) chinese ??= mod.CharacterName;
            else english ??= mod.CharacterName;
        }
        return (chinese, english);
    }

    internal CharacterFolderPlan PlanCharacterFolder(OnlineModCard mod, string repositoryPath, IReadOnlyCollection<string> aliases)
    {
        // Issue #129: weapons, UI, other… go to a folder named after the category, planned and created like a character's.
        if (GetOnlineCategoryFolderName(mod) is { } category)
        {
            return CategoryFolderPlanner.Plan(repositoryPath, Directory.GetDirectories(repositoryPath),
                Directory.GetFiles(repositoryPath).Select(Path.GetFileName).OfType<string>().ToArray(), category);
        }
        (string? chinese, string? english) = GetCharacterFolderNames(mod);
        return CharacterFolderPlanner.Plan(repositoryPath,
            Directory.GetDirectories(repositoryPath),
            Directory.GetFiles(repositoryPath).Select(Path.GetFileName).OfType<string>().ToArray(),
            aliases, mod.CharacterName, chinese, english, IsChineseUi);
    }

    internal string CharacterFolderReasonText(CharacterFolderPlan plan, string character) => plan.Reason switch
    {
        CharacterFolderReason.Ambiguous => T("OnlineCharacters.SeveralFoldersMayBelongToChoose", character),
        CharacterFolderReason.UnknownCharacter => T("OnlineCharacters.TheModSCharacterIsUnknown"),
        CharacterFolderReason.MultipleCharacters => T("OnlineCharacters.CoversSeveralCharactersChooseADestination", character),
        CharacterFolderReason.MixedNamingLanguage => T("OnlineCharacters.TheRepositoryMixesChineseAndEnglish"),
        CharacterFolderReason.NoNameInLanguage => T("OnlineCharacters.NoNameForInTheRepository", character),
        CharacterFolderReason.InvalidName => T("OnlineCharacters.CannotBeUsedAsAFolder", character),
        CharacterFolderReason.NameConflict => T("OnlineCharacters.AFileWithThatNameAlready"),
        _ => string.Empty
    };

    internal string OnlineRepositoryCategoryId() => _services.OnlineSource.CategoryId(_services.Repositories.Selected);

    internal OnlineCategoryIndex? CurrentOnlineCategories() => _services.OnlineCategories.Current(OnlineRepositoryCategoryId());

    /// <summary>The category a Mod outside the characters goes to (its folder name before it is made safe); null for a character Mod.</summary>
    internal string? GetOnlineCategoryFolderName(OnlineModCard mod)
        => OnlineCategoryPlacement.CategoryName(CurrentOnlineCategories(), mod.RootCategoryName, mod.CategoryId, mod.CharacterName);

    internal string CategoryFolderHint(CharacterFolderPlan plan, string category) => plan.Action switch
    {
        CharacterFolderAction.UseExisting => T("OnlineCategories.FolderRecognized", category, Path.GetFileName(plan.Path)),
        CharacterFolderAction.Create => T("OnlineCategories.FolderCreate", category, plan.Path),
        _ => T("OnlineCategories.FolderAsk", category)
    };

    internal async Task<OnlineModDetails> GetOnlineModDetailsAsync(OnlineModCard mod, CancellationToken cancellationToken = default)
    {
        GameBananaDetails raw = await _host.Metadata.GetDetailsAsync(mod.ItemId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string text = string.Join(Environment.NewLine,
            new[] { raw.Summary, raw.Description }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new OnlineModDetails
        {
            Summary = raw.Summary,
            Description = raw.Description,
            ImageUrls = raw.ImageUrls.ToList(),
            AccessRequirementSummary = ExtractAccessRequirementSummary(text),
            ShortcutBindings = ExtractShortcutBindingsFromText(text)
        };
    }

    internal async Task<List<ShortcutBinding>> BuildLocalizedShortcutBindingsAsync(List<ShortcutBinding> bindings)
    {
        if (bindings.Count == 0)
        {
            return [];
        }

        if (!IsChineseUi)
        {
            return bindings
                .Take(_host.ShortcutScanSafetyLimit)
                .Select(binding => new ShortcutBinding(binding.Shortcut, binding.Action))
                .ToList();
        }

        var localized = new List<ShortcutBinding>();
        foreach (ShortcutBinding binding in bindings.Take(_host.ShortcutScanSafetyLimit))
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
                    _host.LogApplicationIssue("Translate shortcut action", ex);
                }
            }

            localized.Add(new ShortcutBinding(binding.Shortcut, action));
        }

        return localized;
    }

    internal static string ExtractAccessRequirementSummary(string text)
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

    internal List<ShortcutBinding> ExtractShortcutBindingsFromText(string text)
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
                    if (bindings.Count >= _host.ShortcutScanSafetyLimit)
                    {
                        break;
                    }
                }
            }
        }

        return bindings;
    }

    internal static bool TryParseShortcutBindingLine(string line, out ShortcutBinding? binding)
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

    internal Task<string> TranslateTextForDisplayAsync(string text, string targetLanguage)
        => TextTranslationClient.TranslateLongTextAsync(_host.Http, text, targetLanguage);

    internal async Task<OnlineModCard?> FetchGameBananaModCardAsyncV2(
        int itemId, bool useEndfieldMetadata = false, CancellationToken cancellationToken = default)
    {
        GameBananaModMetadata metadata = await _host.Metadata.GetModAsync(itemId, cancellationToken);
        string category = string.IsNullOrWhiteSpace(metadata.CharacterName) ? T("OnlineFeed.Uncategorized") : metadata.CharacterName;
        string character = ResolveOnlineCharacterName(category, metadata.Title, useEndfieldMetadata);
        OnlineDownloadCandidate? file = OnlineDownloadSelectionPolicy.SelectDefault(metadata.DownloadFiles);
        return new OnlineModCard
        {
            ItemId = itemId, Title = metadata.Title, CharacterName = character,
            CategoryId = metadata.CategoryId > 0 ? metadata.CategoryId.ToString(CultureInfo.InvariantCulture) : string.Empty,
            IsCharacterNameInferred = IsGenericOnlineCharacterName(category)
                && !string.Equals(character, category, StringComparison.OrdinalIgnoreCase),
            RootCategoryName = metadata.RootCategoryName,
            Author = string.IsNullOrWhiteSpace(metadata.Author) ? T("OnlineFeed.UnknownAuthor") : metadata.Author,
            AuthorId = metadata.AuthorId,
            Likes = metadata.Likes, Views = metadata.Views, Downloads = metadata.Downloads,
            HotnessScore = GameBananaListingParser.Hotness(metadata.Likes, metadata.Views, metadata.Downloads),
            PreviewUrl = metadata.PreviewUrl, ProfileUrl = metadata.ProfileUrl,
            DownloadUrl = file?.DownloadUrl ?? metadata.FallbackDownloadUrl,
            DownloadFileName = file?.FileName ?? string.Empty, DownloadFiles = metadata.DownloadFiles.ToList(),
            DownloadFileId = file?.FileId ?? string.Empty,
            FileSizeBytes = file?.FileSizeBytes ?? 0, HasUpdates = metadata.HasUpdates,
            UpdatedAt = GameBananaJson.UnixTime(metadata.UpdatedEpoch, DateTimeOffset.Now)
        };
    }

    internal static string NormalizeShortcut(string? shortcut)
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

    internal static bool LooksLikeShortcutCandidate(string value)
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

    internal static string NormalizeShortcutDisplay(string value)
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

    internal static string NormalizeShortcutToken(string value)
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

    internal static string NormalizeShortcutTokenFallback(string token)
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
}
