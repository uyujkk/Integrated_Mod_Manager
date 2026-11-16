using System.Text.Json.Nodes;

namespace IntegratedModManager.UI.Tests;

/// <summary>
/// Settings that make the overlay show what it is for, on the synthetic test data: the sample Mods as copies the
/// manager made (since #107 an unrecorded copy that differs from its source counts as someone else's folder, so the
/// overlay would list no enabled Mod), TestModA's state slots, and hot switching on. Only the test's own copy changes.
/// </summary>
internal static class OverlaySamples
{
    public static void Apply(JsonObject shell, AppSession app)
    {
        string loader = Path.Combine(app.TestData, "Loader", "Mods");
        shell["CopyDeployments"] = new JsonArray(Copy("TestModA", loader), Copy("TestModB", loader));
        shell["OverlaySlotSwitchEnabled"] = true;
        shell["ModPersistentSlots"] = new JsonArray(
            Slot("sample-battle", "Battle", hair: 2, coat: 1, activated: null),
            Slot("sample-casual", "Casual", hair: 0, coat: 0, activated: null),
            Slot("sample-everyday", "Everyday", hair: 1, coat: 0, activated: "2026-11-01T10:00:00Z"));
    }

    private static JsonObject Copy(string mod, string loader) => new()
    {
        ["RepositoryId"] = "ui-repository",
        ["SourceRelativePath"] = @"TestCharacter\" + mod,
        ["DeployedPath"] = Path.Combine(loader, mod),
        ["Origin"] = "Copied"
    };

    private static JsonObject Slot(string id, string name, int hair, int coat, string? activated)
    {
        var slot = new JsonObject
        {
            ["Id"] = id, ["RepositoryId"] = "ui-repository", ["ModRelativePath"] = @"TestCharacter\TestModA", ["Name"] = name,
            ["Values"] = new JsonArray(Value("hair", hair), Value("coat", coat))
        };
        if (activated is not null) slot["ActivatedAtUtc"] = activated;
        return slot;
    }

    private static JsonObject Value(string variable, int value) => new()
    {
        ["RelativeIniPath"] = "mod.ini", ["Variable"] = variable,
        ["RuntimeKey"] = @"$\Mods\TestModA\mod.ini\" + variable, ["Value"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}
