using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// Stage J8: a dangerous confirmation (here deleting a combination preset) opens with Cancel focused, and Esc or
// Enter leave everything as it was. Only AppSession's disposable copy and data are used.
public sealed class DialogTests
{
    [Fact]
    public void DangerConfirm_CancelIsTheDefaultAndEscDoesNothing()
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "repository";
            shell["RepositoryWorkspaceView"] = "presets";
            shell["SelectedConfigurationProfileId"] = "keep-me";
            shell["ConfigurationProfiles"] = new JsonArray(new JsonObject
            {
                ["Id"] = "keep-me", ["RepositoryId"] = "ui-repository", ["Name"] = "Keep me",
                ["ModRelativePaths"] = new JsonArray(@"TestCharacter\TestModB"),
                ["IncludesPersistentState"] = false, ["PersistentStates"] = new JsonArray()
            });
        }).Start();

        foreach (VirtualKeyShort key in new[] { VirtualKeyShort.ESCAPE, VirtualKeyShort.ENTER })
        {
            AppSession.Invoke(app.Find("DeleteConfigurationProfileButton", TimeSpan.FromSeconds(15)));
            AutomationElement delete = app.FindByName(app.Text("Dialogs.Delete"), TimeSpan.FromSeconds(10));
            AutomationElement cancel = delete.Parent.FindFirstChild(cf => cf.ByName(app.Text("Common.Cancel")))
                ?? app.FindByName(app.Text("Common.Cancel"));
            Assert.True(FlaUI.Core.Tools.Retry.WhileFalse(() => cancel.Properties.HasKeyboardFocus.ValueOrDefault, TimeSpan.FromSeconds(3)).Result,
                "Cancel should have the keyboard focus");
            Assert.False(delete.Properties.HasKeyboardFocus.ValueOrDefault);

            Keyboard.Type(key);
            Assert.True(FlaUI.Core.Tools.Retry.WhileTrue(
                () => app.MainWindow.FindFirstDescendant(cf => cf.ByName(app.Text("Dialogs.Delete"))) is not null, TimeSpan.FromSeconds(5)).Result,
                "The dialog should close");
            JsonArray profiles = app.ReadSaved()["ConfigurationProfiles"]!.AsArray();
            Assert.Equal("keep-me", (string?)Assert.Single(profiles)!["Id"]);
        }
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }
}
