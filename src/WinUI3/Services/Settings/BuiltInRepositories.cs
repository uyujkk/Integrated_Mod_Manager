using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

// The built-in game repositories offered on the dashboard (stage J finish, step 8a: moved from the window unchanged).

internal static class BuiltInRepositories
{
    public static readonly RepositoryPresetInfo[] Presets =
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
            OnlineSourceRules.DefaultCategoryId,
            OnlineSourceRules.EndfieldOfficialWikiUrl)
    ];

}
