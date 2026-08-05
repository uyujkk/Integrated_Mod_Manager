using System.Text;

namespace ModFolderCopier.WinUI;

internal static class BetaLocalization
{
    private const string MojibakeHints = "鍙鍦鏇璁鐩鍒寮閫鐢璺鏈宸褰鏉娌鎿纭浠撳簱瀵煎叆閰嶇疆鏂囦欢鍥剧墖棰勮妯″潡鎻愮ず";
    private const string ReadablePunctuation = "：:，。、“”‘’！？-_/()[]{}.+";

    private static readonly Dictionary<string, string> EnToZh = new(StringComparer.Ordinal)
    {
        ["Integrated Mod Manager"] = "集成化mod管理器",
        ["Application Settings"] = "应用设置",
        ["Appearance and Language"] = "界面与语言",
        ["Project Links and App Version"] = "项目链接与软件版本",
        ["App update interval"] = "软件更新频率",
        ["Next Steps"] = "后续计划",
        ["Open GitHub Repository"] = "打开 GitHub 仓库",
        ["Check App Updates"] = "检查软件更新",
        ["Manage language, theme, update checks, and project links here."] = "在这里管理语言、主题、更新检查和项目链接。",
        ["Language, theme, repository entry points, and update checks are collected here."] = "这里集中管理语言、主题、仓库入口和更新检查。",
        ["This section keeps the GitHub repository link and app-version checks. Use the Updates section in the left navigation for mod updates."] = "这里保留 GitHub 仓库入口和软件版本检查；Mod 更新请使用左侧的“更新”模块。",
        ["This only affects app-version checks, not mod update checks. Configure mod update frequency in the Updates section."] = "这里只影响软件版本检查，不影响 Mod 更新检查。Mod 更新频率请到左侧“更新”模块设置。",
        ["Phase one already covers repository editing and GitHub update checks. This page will later add fuller online-source, category-mapping, and global preference settings."] = "一期已经完成仓库编辑和 GitHub 更新检查，后续这里会继续补充更完整的在线来源、分类映射和全局偏好设置。",
        ["Dashboard"] = "仪表板",
        ["Repositories"] = "仓库",
        ["Online"] = "在线",
        ["Online Mods"] = "在线 Mod",
        ["Updates"] = "更新",
        ["Settings"] = "设置",
        ["Repository Preview"] = "仓库预览",
        ["Repository Switcher"] = "仓库切换",
        ["Online Sources"] = "在线来源",
        ["Updates Navigation"] = "更新导航",
        ["Settings Navigation"] = "设置导航",
        ["Browse summary data for all repositories or a single repository."] = "查看全部仓库或单个仓库的预览数据。",
        ["Switch workspaces here while reusing the existing single-repository view."] = "从这里切换工作区，主区继续复用现有单仓库逻辑。",
        ["The online page will use the selected repository as its source mapping context."] = "在线页面会按当前选中仓库的来源映射加载内容。",
        ["All Repositories"] = "全部仓库",
        ["Create repository"] = "新建仓库",
        ["Create Repository"] = "新建仓库",
        ["Edit current repository"] = "编辑当前仓库",
        ["Edit Repository"] = "编辑仓库",
        ["Delete current repository"] = "删除当前仓库",
        ["Repository Dashboard"] = "仓库总览",
        ["This dashboard summarizes repository path status and preview data."] = "这里会汇总全部仓库的路径状态和预览数据。",
        ["Repository Cards"] = "仓库卡片",
        ["Workspace"] = "工作区",
        ["Repository"] = "仓库",
        ["Current repository mapping"] = "当前仓库映射",
        ["Readiness"] = "准备状态",
        ["Ready"] = "就绪",
        ["Ready Paths"] = "路径就绪",
        ["Needs setup"] = "待配置",
        ["Not selected"] = "未选择",
        ["Not configured"] = "未配置",
        ["Not copied"] = "未复制",
        ["Copied"] = "已复制",
        ["Not started"] = "未开始",
        ["Preparing"] = "准备中",
        ["Latest"] = "最新",
        ["Waiting for repository"] = "等待选择仓库",
        ["Source mapping"] = "来源映射",
        ["Configured"] = "已配置",
        ["List fetching"] = "列表抓取",
        ["Next"] = "下一步",
        ["Install flow"] = "安装流程",
        ["Planned"] = "待实现",
        ["Current repository mapping"] = "当前仓库映射",
        ["Current second-level folder"] = "当前第二层文件夹",
        ["Current first-level folder: "] = "当前第一层文件夹：",
        ["Copy status"] = "复制状态",
        ["Source"] = "源文件夹",
        ["Target"] = "目标文件夹",
        ["Target Folder"] = "目标文件夹",
        ["Mod Storage"] = "Mod 仓库",
        ["Mod storage path"] = "Mod 仓库路径",
        ["Target path"] = "目标路径",
        ["Target game mod folder path"] = "目标游戏 Mod 文件夹路径",
        ["Launcher"] = "启动器",
        ["Launcher path"] = "启动器路径",
        ["Launcher path (optional)"] = "启动器路径（可选）",
        ["Paths and Launcher"] = "路径与启动器",
        ["Browse"] = "选择",
        ["Browse EXE"] = "选择程序",
        ["Open Source"] = "打开源文件夹",
        ["Open Target"] = "打开目标文件夹",
        ["Open Target Folder"] = "打开目标文件夹",
        ["Open Mod Storage"] = "打开 Mod 仓库",
        ["Open Launcher Folder"] = "打开启动器位置",
        ["Select XXMI Launcher.exe or another launcher"] = "选择 XXMI Launcher.exe 或其他启动器",
        ["Import To Selected Folder"] = "导入到当前选中文件夹",
        ["Copy Selected Mod"] = "复制当前选中的 Mod",
        ["Refresh"] = "刷新目录",
        ["Run Launcher"] = "运行启动器",
        ["No active copy task"] = "当前无复制任务",
        ["Number of categories in the source folder"] = "当前源文件夹中的分类数量",
        ["Number of categories in the mod storage folder"] = "当前 Mod 仓库中的分类数量",
        ["Detected from folders with the same name in the target"] = "根据目标文件夹中的同名目录判断",
        ["First-level folders"] = "第一层文件夹",
        ["Second-level folders"] = "第二层文件夹",
        ["Search first-level folders"] = "搜索第一层文件夹",
        ["New Folder"] = "新建文件夹",
        ["Shortcut and Description"] = "快捷键与描述",
        ["Shortcut Notes"] = "快捷键说明",
        ["Shortcut notes for the selected mod"] = "当前选中 Mod 的快捷键说明",
        ["Record a shortcut and description for the selected mod"] = "为当前选中的 Mod 记录快捷键和描述",
        ["Shortcut"] = "快捷键",
        ["Description"] = "描述",
        ["Add Row"] = "新增一行",
        ["Image Preview"] = "图片预览",
        ["Mod Link"] = "Mod 链接",
        ["Paste the web link for the current mod"] = "粘贴当前 Mod 对应的网页链接",
        ["Shows the currently selected mod name"] = "这里显示当前选中的 Mod 名称",
        ["Open Link"] = "打开链接",
        ["No preview image was found for the current second-level folder."] = "当前第二层文件夹未找到可预览图片。",
        ["The image could not be loaded or is not supported."] = "图片无法读取或格式不受支持。",
        ["No preview"] = "暂无预览图",
        ["Online Mod Browser"] = "在线 Mod 浏览",
        ["Online Mod List"] = "在线 Mod 列表",
        ["Online source"] = "在线来源",
        ["Online source, for example GameBanana"] = "在线来源，例如 GameBanana",
        ["Current repository mapping"] = "当前仓库映射",
        ["This online page currently supports GameBanana first. Change the source to GameBanana or keep the field for later site integrations."] = "当前在线页面优先支持 GameBanana。你可以把在线来源改成 GameBanana，或保留这个字段等待后续站点接入。",
        ["This online page reads the current repository's source site, category ID, and notes first. If omitted, it falls back to GameBanana / 21842."] = "在线页面会优先读取当前仓库的来源站点、分类 ID 和备注；如果未填写，会默认使用 GameBanana / 21842。",
        ["This page loads data from an external mod site. If loading is slow or fails, a VPN may be required."] = "这里读取的是外网 Mod 站点数据，如果加载较慢或失败，可能需要 VPN。",
        ["This page loads data from an external mod site. If loading is slow or fails, a VPN may be required. The detail view includes experimental auto-translation."] = "这里读取的是外网 Mod 站点数据，如果加载较慢或失败，可能需要 VPN。详情页支持实验性自动翻译。",
        ["Edit current repository source config"] = "编辑当前仓库在线配置",
        ["Refresh Online List"] = "刷新在线列表",
        ["Search character or mod name"] = "搜索角色名或 Mod 名称",
        ["All characters"] = "全部角色",
        ["Sort by hotness"] = "按热度排序",
        ["Sort by downloads"] = "按下载量排序",
        ["Sort by likes"] = "按点赞数排序",
        ["Sort by views"] = "按查看数排序",
        ["Sort by updated time"] = "按更新时间排序",
        ["Online list sort"] = "在线列表排序",
        ["No matching skin mods"] = "没有匹配的皮肤 Mod",
        ["This page only shows the Skins category. Try another page, or search by character and mod name."] = "当前页面只显示 Skins 分类。你可以翻页，或尝试用角色名和 Mod 名称搜索。",
        ["No results"] = "没有结果",
        ["Character"] = "角色",
        ["Author"] = "作者",
        ["Category"] = "分类",
        ["Likes"] = "点赞",
        ["Views"] = "查看",
        ["Downloads"] = "下载量",
        ["Hotness"] = "热度",
        ["Updated"] = "更新时间",
        ["Open Page"] = "打开页面",
        ["View Details"] = "查看详情",
        ["Download and Extract"] = "下载并解压",
        ["Mod Details"] = "Mod 详情",
        ["Preview failed to load"] = "预览图加载失败",
        ["Preview failed"] = "预览失败",
        ["This entry does not provide a summary."] = "当前条目没有提供简介。",
        ["The content below was translated automatically and may be imperfect."] = "以下为实验性自动翻译，可能不完全准确。",
        ["This entry reports update records."] = "该条目包含更新记录。",
        ["This entry currently has no update record flag."] = "该条目当前没有更新记录标记。",
        ["This entry does not have a usable link."] = "当前条目没有可用链接。",
        ["This entry does not have a usable download link."] = "当前条目没有可用下载链接。",
        ["Unknown author"] = "未知作者",
        ["Uncategorized"] = "未分类角色",
        ["Mod Updates"] = "Mod 更新",
        ["This page lists mods tracked through online installs and lets you check whether newer versions are available."] = "这里会列出已通过在线安装记录下来的 Mod，并支持自动或手动检查它们是否有新版本。",
        ["Check Settings"] = "检查方式",
        ["Automatic checks run on startup at the selected interval. Manual checks refresh every tracked online mod immediately."] = "自动检查会在应用启动时按你设定的频率执行，手动检查会立即刷新所有已记录的在线 Mod。",
        ["Check interval"] = "检查频率",
        ["Only mods that were installed and saved with a source ID will be checked."] = "只会检查已安装并记录了网站来源 ID 的 Mod。",
        ["Check Now"] = "手动检查更新",
        ["Tracked Mods"] = "已追踪 Mod",
        ["No tracked online mods yet"] = "还没有可追踪的在线 Mod",
        ["After downloading and extracting from the online browser, this page will automatically track the source link, preview image, and remote ID."] = "从在线模块下载并解压到仓库后，这里会自动记录来源链接、预览图和远程 ID。",
        ["Manual only"] = "仅手动检查",
        ["Check daily on startup"] = "每天启动时检查",
        ["Check every 3 days on startup"] = "每 3 天启动时检查",
        ["Check weekly on startup"] = "每周启动时检查",
        ["Not checked yet"] = "尚未检查",
        ["Unknown time"] = "未知时间",
        ["Latest release details will appear here"] = "最新版本信息会显示在这里",
        ["GitHub release notes will appear here after an update check."] = "检查到新版本后，这里会显示 GitHub Release 的更新内容。",
        ["Checking..."] = "检查中...",
        ["Loading..."] = "加载中...",
        ["Loading"] = "加载中",
        ["Ready to refresh"] = "可以刷新",
        ["Ready to load real data"] = "准备加载在线列表",
        ["Open failed"] = "打开失败",
        ["Check failed"] = "检查失败",
        ["Error"] = "错误",
        ["OK"] = "确定",
        ["Cancel"] = "取消",
        ["Confirm"] = "确认",
        ["Save"] = "保存",
        ["Close"] = "关闭",
        ["Rename"] = "重命名",
        ["Delete Mod"] = "删除 Mod",
        ["Delete selected mod"] = "删除当前选中的 Mod",
        ["Confirm delete"] = "确认删除",
        ["First confirmation"] = "第一次确认",
        ["Second confirmation"] = "第二次确认",
        ["Cannot delete"] = "无法删除",
        ["At least one repository must remain."] = "至少需要保留一个仓库。",
        ["Invalid path"] = "路径无效",
        ["Operation failed"] = "操作失败",
        ["Import failed"] = "导入失败",
        ["Archive import failed: "] = "导入压缩文件失败：",
        ["Online load failed"] = "在线加载失败",
        ["Failed to load the GameBanana list: "] = "读取 GameBanana 列表失败：",
        ["Online download failed"] = "在线下载失败",
        ["Download or extraction failed: "] = "下载或解压失败：",
        ["Open page failed"] = "打开页面失败",
        ["Details load failed"] = "详情加载失败",
        ["Failed to open mod details: "] = "打开 Mod 详情失败：",
        ["Cannot download"] = "无法下载",
        ["Download completed"] = "下载完成",
        ["Extracting files..."] = "正在解压文件...",
        ["Extraction complete"] = "解压完成",
        ["Preparing extraction..."] = "正在准备解压...",
        ["Choose download location"] = "选择下载位置",
        ["Use the repository root folder"] = "使用仓库根目录",
        ["Use selected folder"] = "使用选中的文件夹",
        ["Choose another location"] = "选择其他位置",
        ["No archive found"] = "未找到压缩包",
        ["This archive does not contain extractable files."] = "这个压缩包里没有可解压的文件。",
        ["This system could not extract the selected format. Try ZIP or 7Z instead."] = "当前系统无法解压该格式，建议尝试 ZIP 或 7Z。",
        ["Only archive files dragged from File Explorer are supported here."] = "这里只支持从资源管理器拖入压缩文件。",
        ["Only image files dragged from File Explorer are supported."] = "只支持从资源管理器拖入图片文件。",
        ["No supported archive file was detected."] = "没有检测到可用的压缩文件。",
        ["No supported image file was detected."] = "没有检测到可用的图片文件。",
        ["No image found"] = "未找到图片",
        ["Unsupported drop content"] = "不支持的拖拽内容",
        ["Select a first-level category first."] = "请先选择一个第一层分类。",
        ["Select a first-level category before dropping an archive here."] = "请先选择第一层分类，再把压缩包拖到这里。",
        ["Select a first-level folder first."] = "请先选择一个第一层文件夹。",
        ["No category selected"] = "未选择分类",
        ["No folder selected"] = "未选择文件夹",
        ["Select a second-level mod first."] = "请先选择一个第二层 Mod。",
        ["Select a second-level mod before dropping an image."] = "请先选择一个第二层 Mod，再拖入图片。",
        ["No mod selected"] = "未选择 Mod",
        ["Set a valid launcher path first."] = "请先设置有效的启动器路径。",
        ["Launcher not set"] = "未设置启动器",
        ["Choose a valid mod storage folder first."] = "请先选择有效的 Mod 仓库路径。",
        ["Set a valid mod storage folder first."] = "请先设置有效的 Mod 仓库路径。",
        ["Choose a valid target folder first."] = "请先选择有效的目标文件夹。",
        ["Target folder not set"] = "未设置目标文件夹",
        ["The target folder does not exist. Please choose it again."] = "目标文件夹不存在，请重新选择。",
        ["Enter repository name"] = "输入仓库名称",
        ["Repository name"] = "仓库名称",
        ["Notes"] = "备注",
        ["Notes (optional)"] = "备注（可选）",
        ["Create a new custom repository and fill in its primary paths in one step."] = "创建一个新的自定义仓库，并一次性补全它的主要路径信息。",
        ["Edit the current repository name, paths, and reserved online-source fields."] = "编辑当前仓库的名称、路径和预留的在线来源信息。",
        ["Enter a repository name. You can expand its settings later."] = "输入仓库名称，后面可以继续补充路径和用途。",
        ["Enter a new repository name."] = "输入新的仓库名称。",
        ["Enter the new first-level folder name."] = "请输入新的第一层文件夹名称。",
        ["Create first-level folder"] = "新建第一层文件夹",
        ["Rename first-level folder"] = "重命名第一层文件夹",
        ["A first-level folder with the same name already exists."] = "同名第一层文件夹已存在。",
        ["Could not determine the parent directory."] = "无法确定父目录。",
        ["Manage repository switching, online resources, and local mod actions in one place."] = "在一个地方管理仓库切换、在线资源和本地 Mod 操作。",
        ["Tool author: uyujkk"] = "工具作者：uyujkk"
    };

    public static string Resolve(bool useEnglish, string zh, string en)
    {
        if (useEnglish)
        {
            return en;
        }

        if (EnToZh.TryGetValue(en, out string? mapped))
        {
            string normalizedMapped = NormalizeChineseText(mapped);
            return string.IsNullOrWhiteSpace(normalizedMapped) ? en : normalizedMapped;
        }

        string normalizedZh = NormalizeChineseText(zh);
        return string.IsNullOrWhiteSpace(normalizedZh) ? en : normalizedZh;
    }

    public static string NormalizeChineseText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        string best = trimmed;
        string candidate = trimmed;

        for (int i = 0; i < 2; i++)
        {
            string repaired = TryRepairMojibake(candidate);
            if (string.Equals(repaired, candidate, StringComparison.Ordinal))
            {
                break;
            }

            if (GetChineseReadabilityScore(repaired) > GetChineseReadabilityScore(best))
            {
                best = repaired;
            }

            candidate = repaired;
        }

        return best;
    }

    private static string TryRepairMojibake(string value)
    {
        try
        {
            byte[] bytes = Encoding.GetEncoding("GB18030").GetBytes(value);
            string repaired = Encoding.UTF8.GetString(bytes);
            return repaired.Contains('\uFFFD') ? value : repaired;
        }
        catch
        {
            return value;
        }
    }

    private static int GetChineseReadabilityScore(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        int score = 0;

        foreach (char ch in value)
        {
            if (ch is >= '\u4e00' and <= '\u9fff')
            {
                score += 3;
            }
            else if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ReadablePunctuation.Contains(ch))
            {
                score += 1;
            }

            if (MojibakeHints.IndexOf(ch) >= 0)
            {
                score -= 4;
            }
        }

        return score;
    }
}
