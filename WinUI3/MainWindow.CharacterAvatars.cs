using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ModFolderCopier.WinUI;

// Avatars for repository character folders: the folder's own image, the character the
// folder is bound to, or the catalog character with the same name (CharacterFolderAvatars).
// The catalog is the built-in Endfield list or the cached online character catalog; this
// never downloads a catalog by itself.
public sealed partial class MainWindow
{
    private const int CharacterAvatarDecodeWidth = 64;
    private CancellationTokenSource _characterAvatarCancellation = new();

    private async Task RefreshCharacterFolderAvatarsAsync()
    {
        _characterAvatarCancellation.Cancel();
        _characterAvatarCancellation.Dispose();
        _characterAvatarCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _characterAvatarCancellation.Token;

        WorkspaceRepository? repository = GetSelectedRepository();
        FirstLevelFolderItem[] items = [.. _allFirstLevelItems];
        if (repository is null || items.Length == 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<OnlineCharacterOption> catalog = await GetCharacterAvatarCatalogAsync(repository);
            var bindings = new Dictionary<string, string>(repository.CharacterAvatarBindings ?? [], StringComparer.OrdinalIgnoreCase);
            CharacterAvatar[] avatars = await Task.Run(
                () => items.Select(item => CharacterFolderAvatars.Resolve(item.Path, catalog, bindings)).ToArray(),
                cancellationToken);
            for (int index = 0; index < items.Length && !cancellationToken.IsCancellationRequested; index++)
            {
                await ApplyCharacterAvatarAsync(items[index], avatars[index], cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogApplicationIssue("Character folder avatars", exception);
        }
    }

    private async Task RefreshCharacterFolderAvatarAsync(FirstLevelFolderItem item)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        IReadOnlyList<OnlineCharacterOption> catalog = await GetCharacterAvatarCatalogAsync(repository);
        var bindings = new Dictionary<string, string>(repository.CharacterAvatarBindings ?? [], StringComparer.OrdinalIgnoreCase);
        item.AvatarImageSource = null;
        await ApplyCharacterAvatarAsync(item, CharacterFolderAvatars.Resolve(item.Path, catalog, bindings), _characterAvatarCancellation.Token);
    }

    private async Task ApplyCharacterAvatarAsync(FirstLevelFolderItem item, CharacterAvatar avatar, CancellationToken cancellationToken)
    {
        try
        {
            BitmapImage? bitmap = avatar switch
            {
                { FilePath: { } path } => await LoadLocalBitmapAsync(path, CharacterAvatarDecodeWidth),
                { ImageUrl: { } url } => await LoadOnlineBitmapAsync(url, cancellationToken, CharacterAvatarDecodeWidth),
                _ => null
            };
            if (!cancellationToken.IsCancellationRequested)
            {
                item.AvatarImageSource = bitmap;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // An unreadable image leaves the initial placeholder.
            LogApplicationIssue("Character folder avatar " + item.Name, exception);
        }
    }

    private async Task<IReadOnlyList<OnlineCharacterOption>> GetCharacterAvatarCatalogAsync(WorkspaceRepository repository)
    {
        if (IsEndfieldRepository(repository))
        {
            return EndfieldCharacters.Select((character, index) => new OnlineCharacterOption
            {
                Key = character.EnglishName,
                DisplayNameZh = character.ChineseName,
                DisplayNameEn = character.EnglishName,
                AvatarUrl = character.AvatarUrl,
                Aliases = [.. character.AllNames],
                WikiOrder = index
            }).ToList();
        }

        string catalogKey = GetOnlineCharacterCatalogKey(repository);
        if (string.Equals(_onlineCharacterCatalogKey, catalogKey, StringComparison.Ordinal) && _onlineCharacterOptions.Count > 0)
        {
            return [.. _onlineCharacterOptions];
        }

        OnlineCharacterCatalogCacheSnapshot? cached = await TryReadOnlineCharacterCatalogCacheAsync(catalogKey);
        return cached?.Characters ?? [];
    }

    private void AddCharacterAvatarMenuItems(MenuFlyout menu, FirstLevelFolderItem item)
    {
        var avatarMenu = new MenuFlyoutSubItem
        {
            Text = T("CharacterAvatars.Avatar"),
            Icon = new FontIcon { Glyph = "" }
        };

        var choose = new MenuFlyoutItem { Text = T("CharacterAvatars.ChooseImage") };
        choose.Click += async (_, _) => await RunCharacterAvatarActionAsync(item, async () =>
        {
            string? image = await PickFileAsync([.. CharacterFolderAvatars.ImageExtensions]);
            if (image is null) return false;
            CharacterFolderAvatars.SetCustomAvatar(item.Path, image);
            return true;
        });
        avatarMenu.Items.Add(choose);

        var bind = new MenuFlyoutItem { Text = T("CharacterAvatars.BindToCharacter") };
        bind.Click += async (_, _) => await RunCharacterAvatarActionAsync(item, () => PromptCharacterAvatarBindingAsync(item));
        avatarMenu.Items.Add(bind);

        avatarMenu.Items.Add(new MenuFlyoutSeparator());
        var restore = new MenuFlyoutItem { Text = T("CharacterAvatars.RestorePreviousImage") };
        restore.Click += async (_, _) => await RunCharacterAvatarActionAsync(item, () => Task.FromResult(CharacterFolderAvatars.RestorePreviousAvatar(item.Path)));
        avatarMenu.Items.Add(restore);

        var remove = new MenuFlyoutItem { Text = T("CharacterAvatars.RemoveCustomImage") };
        remove.Click += async (_, _) => await RunCharacterAvatarActionAsync(item, () => Task.FromResult(CharacterFolderAvatars.RemoveCustomAvatar(item.Path)));
        avatarMenu.Items.Add(remove);

        menu.Items.Add(avatarMenu);
    }

    private async Task RunCharacterAvatarActionAsync(FirstLevelFolderItem item, Func<Task<bool>> action)
    {
        try
        {
            if (await action())
            {
                await RefreshCharacterFolderAvatarAsync(item);
            }
        }
        catch (Exception exception)
        {
            LogApplicationIssue("Character folder avatar action", exception);
            await ShowMessageAsync(T("CharacterAvatars.CouldNotChangeTheAvatar") + exception.Message, T("CharacterAvatars.Avatar"));
        }
    }

    // Binds the folder to a catalog character, or clears the binding (automatic matching).
    private async Task<bool> PromptCharacterAvatarBindingAsync(FirstLevelFolderItem item)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return false;
        }

        IReadOnlyList<OnlineCharacterOption> catalog = await GetCharacterAvatarCatalogAsync(repository);
        if (catalog.Count == 0)
        {
            await ShowMessageAsync(T("CharacterAvatars.NoCharacterListYet"), T("CharacterAvatars.BindToCharacter"));
            return false;
        }

        repository.CharacterAvatarBindings ??= new(StringComparer.OrdinalIgnoreCase);
        string? current = repository.CharacterAvatarBindings
            .FirstOrDefault(binding => string.Equals(binding.Key, item.Name, StringComparison.OrdinalIgnoreCase)).Value;
        var choices = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        choices.Items.Add(new ComboBoxItem { Content = T("CharacterAvatars.AutomaticByFolderName"), Tag = string.Empty });
        foreach (OnlineCharacterOption character in catalog)
        {
            string label = string.Equals(character.DisplayNameZh, character.DisplayNameEn, StringComparison.OrdinalIgnoreCase)
                ? character.DisplayNameZh
                : $"{character.DisplayNameZh} / {character.DisplayNameEn}";
            choices.Items.Add(new ComboBoxItem { Content = label, Tag = character.Key });
        }
        choices.SelectedItem = choices.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(choice => string.Equals((string)choice.Tag, current ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            ?? choices.Items[0];

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = T("CharacterAvatars.ChooseTheCharacterForFolder", item.Name), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(choices);
        var dialog = new ContentDialog
        {
            Title = T("CharacterAvatars.BindToCharacter"),
            Content = panel,
            PrimaryButtonText = T("Dialogs.OK"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = RootGrid.ActualTheme,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        foreach (string folderName in repository.CharacterAvatarBindings.Keys.Where(name => string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            repository.CharacterAvatarBindings.Remove(folderName);
        }
        if (choices.SelectedItem is ComboBoxItem { Tag: string characterKey } && characterKey.Length > 0)
        {
            repository.CharacterAvatarBindings[item.Name] = characterKey;
        }
        SaveShellConfig();
        return true;
    }
}
