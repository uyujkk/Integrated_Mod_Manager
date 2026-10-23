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

    private static readonly EndfieldCharacterInfo[] EndfieldCharacters =
    [
        new("Liino", "梨诺", "", "https://bbs.hycdn.cn/image/common/20260809/4826366/6a77c4b6a87c0071fa15da57_3eac6ad0.png"),
        new("Arcane", "诀", "47395", "https://bbs.hycdn.cn/image/common/20260714/4634516/6a55d3861da1e29cd294f0d1_6ad0f152.png"),
        new("Camille", "卡缪", "46924", "https://bbs.hycdn.cn/image/common/20260626/5246026/6a3dfcd1690f9d18da26a0cd_3eac6ad0.png"),
        new("Mi Fu", "弭弗", "46272", "https://bbs.hycdn.cn/image/common/20260605/4826366/6a22112497bfaba8393d24e0_56331367.png", "Mifu"),
        new("Zhuang Fangyi", "庄方宜", "44984", "https://bbs.hycdn.cn/image/common/20260417/2641152/69e12fbefe4a20e7c0d83e4e_f1525633.png", "Fangyi", "ZFY", "庄方怡"),
        new("Rossi", "洛茜", "44510", "https://bbs.hycdn.cn/image/common/20260329/4826366/69c8998745f7d92a73c88faf_2f76db19.png"),
        new("Tangtang", "汤汤", "44247", "https://bbs.hycdn.cn/image/common/20260312/2274383/69b211574298be0edf042cea_f1525633.png"),
        new("Yvonne", "伊冯", "42742", "https://bbs.hycdn.cn/image/common/20260202/4850358/69807e73d262875d5b986600_2f76db19.png"),
        new("Gilberta", "洁尔佩塔", "42733", "https://bbs.hycdn.cn/image/common/20260203/101423/6980d4aa4973d8a11340c613_2f76db19.png", "Berta"),
        new("Laevatain", "莱万汀", "42734", "https://bbs.hycdn.cn/image/2025/12/29/2054140/9a215e451767e4527e1f8d1f8ce09fef.png", "Laev"),
        new("Endministrator (M)", "管理员 (男)", "42720", "https://bbs.hycdn.cn/image/2026/01/12/2641152/40da4202dc8dd5d4a943077b195f5f25.png", "Male Endministrator", "Endministrator Male"),
        new("Endministrator (F)", "管理员 (女)", "42719", "https://bbs.hycdn.cn/image/2026/01/12/2641152/837428a7e2fdc13972d45599675f53a4.png", "Female Endministrator", "Endministrator Female"),
        new("Pogranichnik", "骏卫", "42738", "https://bbs.hycdn.cn/image/common/20260204/2054140/6983540cf139cae54d734e26_13673ac9.png", "Pog"),
        new("Ember", "余烬", "42730", "https://bbs.hycdn.cn/image/common/20260217/2054140/69940065223662a892b01da4_db193eac.png"),
        new("Last Rite", "别礼", "42735", "https://bbs.hycdn.cn/image/common/20260203/101423/6981a44d9fd9f8e1d8fd5d22_56331367.png", "Lastrite"),
        new("Lifeng", "黎风", "42736", "https://bbs.hycdn.cn/image/common/20260204/2274383/69822586645bf8ceddc4e329_f1525633.png"),
        new("Ardelia", "艾尔黛拉", "42725", "https://bbs.hycdn.cn/image/2026/01/26/4826366/3f24dfab756936f20757e5a5b5ddd966.png"),
        new("Perlica", "佩丽卡", "42737", "https://bbs.hycdn.cn/image/common/20260128/5246026/6979ace2deae33c4ae1159c0_3eac6ad0.png"),
        new("Chen Qianyu", "陈千语", "42728", "https://bbs.hycdn.cn/image/common/20260129/4826366/697b015c4973d8a1133f7876_6ad0f152.png", "Qianyu"),
        new("Wulfgard", "狼卫", "42740", "https://bbs.hycdn.cn/image/common/20260203/5246026/6981cf549fd9f8e1d8fd61cc_3eac6ad0.png", "Wulf"),
        new("Arclight", "弧光", "42724", "https://bbs.hycdn.cn/image/common/20260203/101423/6981b85449da91d62c6689cc_f1525633.png"),
        new("Xaihi", "赛希", "42741", "https://bbs.hycdn.cn/image/common/20260217/2054140/6993df14b32aacba0e7256d4_13673ac9.png", "Saihi"),
        new("Alesh", "阿列什", "42722", "https://bbs.hycdn.cn/image/common/20260217/4826366/699434292468ffc1c1e2b772_f1525633.png"),
        new("Da Pan", "大潘", "42729", "https://bbs.hycdn.cn/image/common/20260208/4826366/698771821acbba54e4b0e048_3eac6ad0.png", "Dapan"),
        new("Avywenna", "艾维文娜", "42726", "https://bbs.hycdn.cn/image/common/20260208/4826366/6987747b94e337a3813bf30c_13673ac9.png"),
        new("Snowshine", "昼雪", "42739", "https://bbs.hycdn.cn/image/common/20260202/4826366/6980bf4a3a151ae9156138f7_f1525633.png"),
        new("Akekuri", "秋栗", "42721", "https://bbs.hycdn.cn/image/2026/01/12/2641152/00598faf8a7eccab9c4d64e102faac6b.png"),
        new("Estella", "埃特拉", "42731", "https://bbs.hycdn.cn/image/common/20260204/2274383/6982b354f139cae54d7333a2_6ad0f152.png"),
        new("Catcher", "卡契尔", "42727", "https://bbs.hycdn.cn/image/common/20260202/5246026/6980af993a151ae9156136ea_f1525633.png"),
        new("Fluorite", "萤石", "42732", "https://bbs.hycdn.cn/image/2026/01/12/2641152/d50a55c6f0bb70419677592a21b635ef.png"),
        new("Antal", "安塔尔", "42723", "https://bbs.hycdn.cn/image/common/20260204/4850358/69835ef9645bf8ceddc50a96_2f76db19.png")
    ];
}
