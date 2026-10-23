using IntegratedModManager.Core.Endfield;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private static readonly RepositoryPresetInfo[] BuiltInRepositoryPresets =
    [
        new(
            "zzz",
            "绝区零",
            "Zenless Zone Zero",
            "30305",
            "https://baike.mihoyo.com/zzz/wiki/channel/map/2/43"),
        new(
            "genshin",
            "原神",
            "Genshin Impact",
            "18140",
            "https://baike.mihoyo.com/ys/obc/channel/map/189/25?bbs_presentation_style=no_header&visit_device=pc"),
        new(
            "hsr",
            "崩坏：星穹铁道",
            "Honkai: Star Rail",
            "22633",
            "https://bbs.mihoyo.com/sr/wiki/channel/map/17/18?bbs_presentation_style=no_header"),
        new(
            "endfield",
            "明日方舟：终末地",
            "Arknights: Endfield",
            DefaultOnlineCategoryId,
            EndfieldOfficialWikiUrl)
    ];

    private static readonly IReadOnlyList<EndfieldCharacterInfo> EndfieldCharacters = EndfieldCharacterCatalog.BuiltIn;
}
