using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ModFolderCopier.WinUI;

// The avatar actions of a character folder (choose an image, bind to a character, restore, remove). Loading the avatars
// is WorkspaceService's (stage J finish, step 7b), so the dashboard has them without the workspace page.
public sealed partial class WorkspacePage
{

    private void AddCharacterAvatarMenuItems(MenuFlyout menu, FirstLevelFolderItem item)
    {
        var avatarMenu = new MenuFlyoutSubItem
        {
            Text = T("CharacterAvatars.Avatar"),
            Icon = new FontIcon { Glyph = "" }
        };
        foreach (MenuFlyoutItemBase entry in CreateCharacterAvatarMenuItems(item))
        {
            avatarMenu.Items.Add(entry);
        }
        menu.Items.Add(avatarMenu);
    }

    // The avatar button in the character list header: the same actions for the selected character.
    private void OnCharacterAvatarButtonClicked(object sender, RoutedEventArgs e)
    {
        if (_services.Workspace.Characters.Selected is not { } item)
        {
            _services.Workspace.Status = T("CharacterAvatars.SelectACharacterFirst");
            return;
        }

        var menu = new MenuFlyout();
        foreach (MenuFlyoutItemBase entry in CreateCharacterAvatarMenuItems(item))
        {
            menu.Items.Add(entry);
        }
        menu.ShowAt((FrameworkElement)sender);
    }

    private List<MenuFlyoutItemBase> CreateCharacterAvatarMenuItems(FirstLevelFolderItem item)
    {
        var avatarMenu = new List<MenuFlyoutItemBase>();
        var choose = new MenuFlyoutItem { Text = T("CharacterAvatars.ChooseImage") };
        choose.Click += async (_, _) => await RunCharacterAvatarActionAsync(item, async () =>
        {
            string? image = await _services.Dialogs.PickFileAsync([.. CharacterFolderAvatars.ImageExtensions]);
            if (image is null) return false;
            CharacterFolderAvatars.SetCustomAvatar(item.Path, image);
            return true;
        });
        avatarMenu.Add(choose);

        var bind = new MenuFlyoutItem { Text = T("CharacterAvatars.BindToCharacter") };
        bind.Click += async (_, _) => await RunCharacterAvatarActionAsync(item, () => PromptCharacterAvatarBindingAsync(item));
        avatarMenu.Add(bind);

        avatarMenu.Add(new MenuFlyoutSeparator());
        var restore = new MenuFlyoutItem { Text = T("CharacterAvatars.RestorePreviousImage") };
        restore.Click += async (_, _) => await RunCharacterAvatarActionAsync(item, () => Task.FromResult(CharacterFolderAvatars.RestorePreviousAvatar(item.Path)));
        avatarMenu.Add(restore);

        var remove = new MenuFlyoutItem { Text = T("CharacterAvatars.RemoveCustomImage") };
        remove.Click += async (_, _) => await RunCharacterAvatarActionAsync(item, () => Task.FromResult(CharacterFolderAvatars.RemoveCustomAvatar(item.Path)));
        avatarMenu.Add(remove);

        return avatarMenu;
    }

    private async Task RunCharacterAvatarActionAsync(FirstLevelFolderItem item, Func<Task<bool>> action)
    {
        try
        {
            if (await action())
            {
                await _services.Workspace.RefreshCharacterFolderAvatarAsync(item);
            }
        }
        catch (Exception exception)
        {
            _host.LogApplicationIssue("Character folder avatar action", exception);
            await ShowMessageAsync(T("CharacterAvatars.CouldNotChangeTheAvatar") + exception.Message, T("CharacterAvatars.Avatar"));
        }
    }

    // Binds the folder to a catalog character, or clears the binding (automatic matching).
    private async Task<bool> PromptCharacterAvatarBindingAsync(FirstLevelFolderItem item)
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (repository is null)
        {
            return false;
        }

        (IReadOnlyList<OnlineCharacterOption> catalog, _) = await _services.Workspace.GetCharacterAvatarCatalogAsync(repository);
        if (catalog.Count == 0)
        {
            await ShowMessageAsync(T("CharacterAvatars.NoCharacterListYet"), T("CharacterAvatars.BindToCharacter"));
            return false;
        }

        repository.CharacterAvatarBindings ??= new(StringComparer.OrdinalIgnoreCase);
        string? current = repository.CharacterAvatarBindings
            .FirstOrDefault(binding => string.Equals(binding.Key, item.Name, StringComparison.OrdinalIgnoreCase)).Value;
        if (await CharacterAvatarDialog.ChooseAsync(_services, item.Name, catalog, current) is not { } chosenKey)
        {
            return false;
        }

        foreach (string folderName in repository.CharacterAvatarBindings.Keys.Where(name => string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            repository.CharacterAvatarBindings.Remove(folderName);
        }
        if (chosenKey.Length > 0)
        {
            repository.CharacterAvatarBindings[item.Name] = chosenKey;
        }
        _host.SaveShellConfig();
        return true;
    }
}
