using IntegratedModManager.Core.Deployment;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The dialogs of the organized import of an existing Mod library: where from and to, the preview, the running copy,
/// undo and the check of interrupted imports (moved from the window in stage J8).
/// </summary>
internal static class ExistingModImportDialogs
{
    /// <summary>The library folder and the character folder to copy into, or null when cancelled.</summary>
    public static async Task<(string Source, string Destination)?> ChooseAsync(AppServices services, IReadOnlyList<string> characters,
        string? selectedCharacter, Func<Task<string?>> pickFolder)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var sourceBox = new TextBox { Header = T("ExistingModImport.ExistingModLibraryFolder"),
            PlaceholderText = T("ExistingModImport.PasteAFolderPathOrChoose"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var target = new ComboBox { Header = T("ExistingModImport.CopyIntoCharacterFolder"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string character in characters) target.Items.Add(new ComboBoxItem { Content = Path.GetFileName(character), Tag = character });
        target.SelectedItem = target.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == selectedCharacter)
            ?? target.Items[0];
        TextBlock sourceError = DialogLayout.FieldError();
        var choice = new ContentDialog { Title = T("ExistingModImport.ChooseImportDestination"), Content = new StackPanel { Spacing = 12, MinWidth = 320, Children =
            { DialogLayout.Step(T("Dialogs.StepOf", 1, 3)), new TextBlock { Text = T("ExistingModImport.ImmediateSubfoldersContainingINIFilesAre"), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 },
              new StackPanel { Spacing = 4, Children = { sourceBox, sourceError } }, target } },
            PrimaryButtonText = T("ExistingModImport.Preview"), SecondaryButtonText = T("ExistingModImport.BrowseSource"), IsPrimaryButtonEnabled = false,
            CloseButtonText = T("Common.Cancel") };
        sourceBox.TextChanged += (_, _) =>
        {
            string path = sourceBox.Text.Trim();
            choice.IsPrimaryButtonEnabled = Directory.Exists(path);
            sourceError.Text = path.Length == 0 || choice.IsPrimaryButtonEnabled ? string.Empty : T("Dialogs.FolderNotFound");
        };
        while (true)
        {
            ContentDialogResult result = await services.Dialogs.ShowAsync(choice);
            if (result == ContentDialogResult.Primary) break;
            if (result != ContentDialogResult.Secondary) return null;
            string? selectedSource = await pickFolder();
            if (selectedSource is not null) sourceBox.Text = selectedSource;
        }
        return (sourceBox.Text.Trim(), (string)((ComboBoxItem)target.SelectedItem).Tag);
    }

    /// <summary>What will be copied where; true to copy.</summary>
    public static async Task<bool> PreviewAsync(AppServices services, ExistingModImportPlan plan, string destination, string size, double windowHeight)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var previewBody = new Grid { RowSpacing = 12, MaxWidth = 620 };
        previewBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        previewBody.Children.Add(new StackPanel { Spacing = 6, Children =
        {
            DialogLayout.Step(T("Dialogs.StepOf", 2, 3)),
            new TextBlock { Text = T("ExistingModImport.ModsDestination", plan.Items.Length, size, destination), TextWrapping = TextWrapping.Wrap }
        } });
        // Grouped (J8): what is copied, and what a conflict keeps out.
        CombinationPreviewRow Row(ExistingModImportItem item) => new($"{item.SourceName} → {item.DestinationName}", "",
            item.Conflict is null ? string.Empty : T("ExistingModImport.ConflictCannotImport").Trim(' ', '(', ')', '（', '）', '—', '-'));
        var added = plan.Items.Where(item => item.Conflict is null).Select(Row).ToArray();
        var skipped = plan.Items.Where(item => item.Conflict is not null).Select(Row).ToArray();
        ListView list = DialogLayout.GroupedPreview(
        [
            new PreviewGroup(T("Dialogs.PreviewAdded", added.Length), added),
            new PreviewGroup(T("Dialogs.PreviewSkipped", skipped.Length), skipped)
        ], DialogLayout.PreviewRowTemplate());
        Grid.SetRow(list, 1);
        previewBody.Children.Add(list);
        var preview = new ContentDialog { Title = T("ExistingModImport.OrganizedImportPreview"),
            Content = new Border { Child = previewBody, MaxHeight = Math.Min(DialogService.MaxContentHeight + 60, Math.Max(180, windowHeight - 250)) },
            PrimaryButtonText = T("ExistingModImport.CopyAndImport"), IsPrimaryButtonEnabled = plan.Items.All(item => item.Conflict is null),
            CloseButtonText = T("Common.Cancel") };
        return await services.Dialogs.ShowAsync(preview) == ContentDialogResult.Primary;
    }

    /// <summary>The running copy; Cancel asks the copy to stop and the dialog stays until it has.</summary>
    public static (ContentDialog Dialog, TextBlock Status) Progress(AppServices services, CancellationTokenSource cancellation)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var status = new TextBlock { Text = T("ExistingModImport.ValidatingPreviewAndCopying"), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 };
        var operation = new ContentDialog { Title = T("ExistingModImport.Importing"), CloseButtonText = T("ExistingModImport.CancelTask"),
            Content = new StackPanel { Spacing = 8, Children = { DialogLayout.Step(T("Dialogs.StepOf", 3, 3)), status, new ProgressBar { IsIndeterminate = true } } } };
        operation.CloseButtonClick += (_, args) => { cancellation.Cancel(); args.Cancel = true; status.Text = T("ExistingModImport.CancelingWaitingForTheCurrentFile"); };
        return (operation, status);
    }

    public static async Task<bool> ConfirmUndoAsync(AppServices services)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var dialog = new ContentDialog { Title = T("ExistingModImport.UndoOrganizedImport"),
            Content = new TextBlock { Text = T("ExistingModImport.OnlyUnchangedCopiesFromThisImport"), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 },
            PrimaryButtonText = T("ExistingModImport.UndoImport"), CloseButtonText = T("Common.Cancel") };
        DialogService.MarkDangerous(dialog);
        return await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>The interrupted import record to check, or null when cancelled.</summary>
    public static async Task<string?> ChooseInterruptedAsync(AppServices services, IReadOnlyList<string> candidates)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var records = new ComboBox { Header = T("ExistingModImport.SelectAnInterruptedImport"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string candidate in candidates)
            records.Items.Add(new ComboBoxItem { Content = File.GetLastWriteTime(candidate).ToString("yyyy-MM-dd HH:mm:ss") + " · " + Path.GetFileName(candidate), Tag = candidate });
        records.SelectedIndex = 0;
        var dialog = new ContentDialog { Title = T("ExistingModImport.ReviewInterruptedImport"), Content = records,
            PrimaryButtonText = T("ExistingModImport.CheckAndRegister"), CloseButtonText = T("Common.Cancel") };
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return (string)((ComboBoxItem)records.SelectedItem).Tag;
    }
}
