using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ModShortcutScannerTests
{
    [Fact]
    public void ParseText_Reads3DmigotoKeySectionsAndNormalizesModifiers()
    {
        const string ini = """
            [Keyhair]
            condition = $active == 1
            type = cycle
            $hair = 0, 1
            key = no_ctrl alt vk_left

            [KeyMenu.ResetPosition]
            key = ctrl no_alt /
            run = CommandListResetMenuPos
            """;

        IReadOnlyList<DetectedModShortcut> result = ModShortcutScanner.ParseText(ini);

        Assert.Collection(result,
            first =>
            {
                Assert.Equal("Alt+Left", first.Shortcut);
                Assert.Equal("cycle", first.Behavior, ignoreCase: true);
                Assert.Equal("hair", first.Target);
            },
            second =>
            {
                Assert.Equal("Ctrl+/", second.Shortcut);
                Assert.Equal("Reset Menu Pos", second.Target);
            });
    }

    [Fact]
    public void ParseText_HandlesMouseAndMultipleKeyLines()
    {
        const string ini = """
            [KeyHelp]
            key = no_modifiers VK_LBUTTON
            key = XB_A
            type = hold
            $help = 1
            """;

        IReadOnlyList<DetectedModShortcut> result = ModShortcutScanner.ParseText(ini);

        Assert.Equal(2, result.Count);
        Assert.Equal("Mouse Left", result[0].Shortcut);
        Assert.Equal("XB_A", result[1].Shortcut);
        Assert.All(result, item => Assert.Equal("help", item.Target));
    }

    [Fact]
    public void ScanDirectory_SkipsOversizedFilesAndHonorsResultLimit()
    {
        string root = Path.Combine(Path.GetTempPath(), "ModShortcutScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "mod.ini"), "[KeyA]\nkey = 1\n$x = 1\n[KeyB]\nkey = 2\n$y = 1");

            IReadOnlyList<DetectedModShortcut> result = ModShortcutScanner.ScanDirectory(root, maximumResults: 1);

            Assert.Single(result);
            Assert.Equal("1", result[0].Shortcut);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanDirectory_DefaultLimitAllowsMoreThanTenShortcuts()
    {
        string root = Path.Combine(Path.GetTempPath(), "ModShortcutScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string ini = string.Join(
                Environment.NewLine,
                Enumerable.Range(1, 12).Select(index => $"[KeyOption{index}]\nkey = F{index}\n$option{index} = 0, 1"));
            File.WriteAllText(Path.Combine(root, "many-shortcuts.ini"), ini);

            IReadOnlyList<DetectedModShortcut> result = ModShortcutScanner.ScanDirectory(root);

            Assert.Equal(12, result.Count);
            Assert.Equal("F12", result[^1].Shortcut);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ParseText_NormalizesSupportedKeyboardMouseAndControllerTokens()
    {
        const string ini = """
            [KeyInputs]
            key = ctrl control shift alt win windows VK_RBUTTON
            key = VK_MBUTTON
            key = VK_XBUTTON1
            key = VK_XBUTTON2
            key = VK_RETURN
            key = VK_ESCAPE
            key = VK_PRIOR
            key = VK_NEXT
            key = VK_SPACE
            key = VK_UP
            key = VK_DOWN
            key = VK_LEFT
            key = VK_RIGHT
            key = VK_OEM_PERIOD
            key = VK_OEM_COMMA
            key = VK_OEM_PLUS
            key = VK_OEM_MINUS
            key = VK_NUMPAD7
            key = XB_LEFT_THUMB
            key = a
            key = F24
            key = customShortcutName
            """;

        IReadOnlyList<DetectedModShortcut> result = ModShortcutScanner.ParseText(ini, "inputs.ini");

        Assert.Equal(22, result.Count);
        Assert.Equal("Ctrl+Shift+Alt+Win+Mouse Right", result[0].Shortcut);
        Assert.Equal("Mouse Middle", result[1].Shortcut);
        Assert.Equal("Mouse X1", result[2].Shortcut);
        Assert.Equal("Mouse X2", result[3].Shortcut);
        Assert.Equal("Enter", result[4].Shortcut);
        Assert.Equal("Esc", result[5].Shortcut);
        Assert.Equal("PageUp", result[6].Shortcut);
        Assert.Equal("PageDown", result[7].Shortcut);
        Assert.Equal("Space", result[8].Shortcut);
        Assert.Equal("Up", result[9].Shortcut);
        Assert.Equal("Down", result[10].Shortcut);
        Assert.Equal("Left", result[11].Shortcut);
        Assert.Equal("Right", result[12].Shortcut);
        Assert.Equal(".", result[13].Shortcut);
        Assert.Equal(",", result[14].Shortcut);
        Assert.Equal("+", result[15].Shortcut);
        Assert.Equal("-", result[16].Shortcut);
        Assert.Equal("Num7", result[17].Shortcut);
        Assert.Equal("XB_LEFT_THUMB", result[18].Shortcut);
        Assert.Equal("A", result[19].Shortcut);
        Assert.Equal("F24", result[20].Shortcut);
        Assert.Equal("custom Shortcut Name", result[21].Shortcut);
        Assert.Equal("inputs.ini", result[0].SourceFile);
    }

    [Fact]
    public void ParseText_IgnoresNonKeySectionsCommentsAndEmptyBindings()
    {
        const string ini = """
            ignored text
            [Constants]
            key = F1

            [Key]
            condition = $active
            key = no_modifiers no_ctrl no_control no_shift no_alt no_win ; nothing remains

            [KeyFancy.Name]
            unknown setting
            key = F2 ; inline comment
            """;

        IReadOnlyList<DetectedModShortcut> result = ModShortcutScanner.ParseText(ini);

        DetectedModShortcut shortcut = Assert.Single(result);
        Assert.Equal("F2", shortcut.Shortcut);
        Assert.Equal("Fancy Name", shortcut.Target);
        Assert.Empty(shortcut.Behavior);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n")]
    public void ParseText_ReturnsEmptyForBlankInput(string text)
    {
        Assert.Empty(ModShortcutScanner.ParseText(text));
    }

    [Fact]
    public void ParseText_UsesRunAfterConditionAndStripsComments()
    {
        const string ini = """
            [KeyOpenMenu]
            condition = $active == 1
            run = CommandListOpen_Character.Menu ; explanation
            key = alt + alt + VK_OEM_PLUS,
            """;

        DetectedModShortcut shortcut = Assert.Single(ModShortcutScanner.ParseText(ini));

        Assert.Equal("Alt++", shortcut.Shortcut);
        Assert.Equal("Open Character Menu", shortcut.Target);
        Assert.Equal("run", shortcut.Behavior);
    }

    [Fact]
    public void ParseText_InfersCycleAndToggleWhenTypeIsMissing()
    {
        const string ini = """
            [KeyHair]
            key = 1
            $hair = 0, 1, 2

            [KeyCoat]
            key = 2
            $coat = !$coat
            """;

        IReadOnlyList<DetectedModShortcut> result = ModShortcutScanner.ParseText(ini);

        Assert.Equal("cycle", result[0].Behavior);
        Assert.Equal("toggle", result[1].Behavior);
    }

    [Theory]
    [InlineData("hair", "发型", "Hairstyle")]
    [InlineData("hairacc", "发饰", "Hair accessory")]
    [InlineData("img_x", "图像横向选项", "Horizontal image option")]
    [InlineData("mouse_clicked", "鼠标点击状态", "Mouse-click state")]
    [InlineData("bodysuit", "连体服", "Bodysuit")]
    [InlineData("panty", "内裤", "Underwear")]
    public void DescriptionFormatter_LocalizesKnownModTargets(string target, string expectedZh, string expectedEn)
    {
        Assert.Equal(expectedZh, ModShortcutDescriptionFormatter.FormatTarget(target, "KeyOption", useEnglish: false));
        Assert.Equal(expectedEn, ModShortcutDescriptionFormatter.FormatTarget(target, "KeyOption", useEnglish: true));
    }

    [Fact]
    public void DescriptionFormatter_UsesReadableLanguageSpecificActions()
    {
        var shortcut = new DetectedModShortcut("8", "KeyHorn", "cycle", "horn", "mod.ini");

        Assert.Equal("循环选择：角饰", ModShortcutDescriptionFormatter.Describe(shortcut, useEnglish: false));
        Assert.Equal("Cycle: Horns", ModShortcutDescriptionFormatter.Describe(shortcut, useEnglish: true));
    }

    [Theory]
    [InlineData("cycle", false, "循环选择：发型")]
    [InlineData("cycle", true, "Cycle: Hairstyle")]
    [InlineData("hold", false, "按住生效：发型")]
    [InlineData("hold", true, "Hold to activate: Hairstyle")]
    [InlineData("toggle", false, "开启/关闭：发型")]
    [InlineData("toggle", true, "Toggle on/off: Hairstyle")]
    [InlineData("run", false, "执行：发型")]
    [InlineData("run", true, "Run: Hairstyle")]
    [InlineData("", false, "触发：发型")]
    [InlineData("", true, "Activate: Hairstyle")]
    public void DescriptionFormatter_LocalizesEverySupportedBehavior(string behavior, bool useEnglish, string expected)
    {
        var shortcut = new DetectedModShortcut("F1", "KeyHair", behavior, "hair", "mod.ini");

        Assert.Equal(expected, ModShortcutDescriptionFormatter.Describe(shortcut, useEnglish));
    }

    [Fact]
    public void DescriptionFormatter_RecognizesLegacyGeneratedTextWithoutClaimingManualNotes()
    {
        var shortcut = new DetectedModShortcut("0", "KeyHair", "cycle", "hair", "mod.ini");

        Assert.True(ModShortcutDescriptionFormatter.MatchesLegacyDescription("循环切换 hair", shortcut));
        Assert.True(ModShortcutDescriptionFormatter.MatchesLegacyDescription("Cycle hair", shortcut));
        Assert.False(ModShortcutDescriptionFormatter.MatchesLegacyDescription("My hairstyle preset", shortcut));
    }

    [Theory]
    [InlineData("hold", "target", "Hold target")]
    [InlineData("toggle", "target", "切换 target")]
    [InlineData("run", "", "Trigger KeyFallback")]
    public void DescriptionFormatter_RecognizesOtherLegacyBehaviors(string behavior, string target, string legacyDescription)
    {
        var shortcut = new DetectedModShortcut("F1", "KeyFallback", behavior, target, "mod.ini");

        Assert.True(ModShortcutDescriptionFormatter.MatchesLegacyDescription(legacyDescription, shortcut));
        Assert.False(ModShortcutDescriptionFormatter.MatchesLegacyDescription(null, shortcut));
    }

    [Theory]
    [InlineData(null, null, false, "Mod 选项")]
    [InlineData(null, null, true, "Mod option")]
    [InlineData("$config_toggle", "KeyIgnored", false, "Mod 选项")]
    [InlineData("123_456", "KeyIgnored", true, "Mod option")]
    [InlineData("foo", "KeyIgnored", false, "“foo”选项")]
    [InlineData("FX", "KeyIgnored", true, "FX")]
    [InlineData("open_customMenu_X", "KeyIgnored", false, "打开custom菜单横向")]
    [InlineData("open_customMenu_X", "KeyIgnored", true, "Open custom menu X")]
    public void DescriptionFormatter_ProducesReadableFallbacks(
        string? target,
        string? section,
        bool useEnglish,
        string expected)
    {
        Assert.Equal(expected, ModShortcutDescriptionFormatter.FormatTarget(target, section, useEnglish));
    }

    [Fact]
    public void ScanDirectory_RejectsInvalidRequestsAndDeduplicatesAcrossFiles()
    {
        Assert.Empty(ModShortcutScanner.ScanDirectory(string.Empty));
        Assert.Empty(ModShortcutScanner.ScanDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        string root = Path.Combine(Path.GetTempPath(), "ModShortcutScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.ini"), "[KeySame]\nkey = F1\n$x = 1");
            File.WriteAllText(Path.Combine(root, "b.ini"), "[KeySame]\nkey = F1\n$x = 2\n[KeyOther]\nkey = F2");

            Assert.Empty(ModShortcutScanner.ScanDirectory(root, maximumResults: 0));
            Assert.Empty(ModShortcutScanner.ScanDirectory(root, maximumFiles: 0));

            IReadOnlyList<DetectedModShortcut> oneFile = ModShortcutScanner.ScanDirectory(root, maximumResults: 10, maximumFiles: 1);
            DetectedModShortcut only = Assert.Single(oneFile);
            Assert.Equal("F1", only.Shortcut);

            IReadOnlyList<DetectedModShortcut> allFiles = ModShortcutScanner.ScanDirectory(root, maximumResults: 10);
            Assert.Equal(2, allFiles.Count);
            Assert.Equal("F2", allFiles[1].Shortcut);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanDirectory_SkipsOversizedIniAndReadsNestedFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "ModShortcutScannerTests_" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            using (FileStream stream = File.Create(Path.Combine(root, "oversized.ini")))
            {
                stream.SetLength(2L * 1024 * 1024 + 1);
            }
            File.WriteAllText(Path.Combine(nested, "valid.ini"), "[KeyNested]\nkey = VK_LBUTTON\nrun = CommandListNested_Action");

            DetectedModShortcut shortcut = Assert.Single(ModShortcutScanner.ScanDirectory(root));

            Assert.Equal("Mouse Left", shortcut.Shortcut);
            Assert.Equal("Nested Action", shortcut.Target);
            Assert.Equal("valid.ini", shortcut.SourceFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
