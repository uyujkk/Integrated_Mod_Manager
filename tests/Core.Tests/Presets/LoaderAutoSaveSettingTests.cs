using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

public sealed class LoaderAutoSaveSettingTests
{
    // Shaped like the d3dx.ini that XXMI ships.
    private const string Ini = "[Loader]\r\nsettings_auto_save_interval = 99\r\n[System]\r\n; Time in seconds between writing persistent vars to d3dx_user.ini.\r\n; settings_auto_save_interval = 1\r\nsettings_auto_save_interval = 60\r\nclear_unknown_settings = 1\r\n[Device]\r\n";

    [Fact]
    public void Read_TakesTheSystemSectionValueOnly() => Assert.Equal(60, LoaderAutoSaveSetting.Read(Ini));

    [Fact]
    public void Write_ChangesOnlyTheNumber()
    {
        string written = LoaderAutoSaveSetting.Write(Ini, 10)!;

        Assert.Equal(Ini.Replace("settings_auto_save_interval = 60", "settings_auto_save_interval = 10"), written);
        Assert.Equal(10, LoaderAutoSaveSetting.Read(written));
    }

    [Fact]
    public void Write_RefusesAFileWithoutTheSetting()
    {
        Assert.Null(LoaderAutoSaveSetting.Write("[System]\r\nclear_unknown_settings = 1\r\n", 10));
        Assert.Null(LoaderAutoSaveSetting.Read("[System]\r\n; settings_auto_save_interval = 60\r\n"));
    }

    [Fact]
    public void Write_RejectsNonsense() => Assert.Throws<ArgumentOutOfRangeException>(() => LoaderAutoSaveSetting.Write(Ini, -5));
}
