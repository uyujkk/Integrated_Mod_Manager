using System.Globalization;
using System.Text.Json;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.DeveloperTools;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml.Controls;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>
/// Copy records (#107), state slots and directory links checked against the disk, with the folder to look at. Read-only:
/// nothing is fixed here; the app's own actions (enable, disable, Mod workspace) change records.
/// </summary>
public sealed partial class RecordsPage : UserControl, IToolPage
{
    private const int MaximumRows = 300;
    private readonly ToolContext _context;
    private AuditResult? _result;

    internal RecordsPage(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        ApplyLanguage();
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    public void ApplyLanguage()
    {
        TitleText.Text = T("Nav.Records");
        IntroText.Text = T("Records.Intro");
        RecordsLabel.Text = T("Records.CopyRecords");
        SlotsLabel.Text = T("Records.Slots");
        LinksLabel.Text = T("Records.Links");
        FindingsLabel.Text = T("Records.Findings");
        ListTitle.Text = T("Records.ListTitle");
        if (_result is not null) Show(_result);
    }

    public async Task RefreshAsync()
    {
        var repositories = _context.Array("Repositories")
            .Select(node => new AuditRepository((string?)node?["Id"] ?? "", (string?)node?["Name"] ?? "", (string?)node?["SourcePath"] ?? "", (string?)node?["TargetPath"] ?? ""))
            .ToList();
        var records = _context.Array("CopyDeployments")
            .Select(node => node?.Deserialize<CopyDeploymentRecord>(ToolContext.JsonOptions))
            .Where(record => record is not null).Cast<CopyDeploymentRecord>().ToList();
        var slots = _context.Array("ModPersistentSlots")
            .Select(node => new AuditSlot((string?)node?["RepositoryId"] ?? "", (string?)node?["ModRelativePath"] ?? "", (string?)node?["Name"] ?? "", (string?)node?["PreviewFile"]))
            .ToList();
        string previews = Path.Combine(_context.RootPath, "snapshots", "preset-previews");
        _result = await Task.Run(() => StateRecordAudit.Run(repositories, records, slots, previews));
        Show(_result);
    }

    private void Show(AuditResult result)
    {
        RecordsValue.Text = result.Records.ToString(CultureInfo.CurrentCulture);
        SlotsValue.Text = result.Slots.ToString(CultureInfo.CurrentCulture);
        LinksValue.Text = result.Links.ToString(CultureInfo.CurrentCulture);
        FindingsValue.Text = result.Findings.Count.ToString(CultureInfo.CurrentCulture);
        FindingsPanel.Children.Clear();
        if (result.Findings.Count == 0)
        {
            FindingsPanel.Children.Add(new TextBlock { Text = T("Records.AllGood"), Style = PageKit.Style("MutedTextStyle"), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
            return;
        }
        foreach (AuditFinding finding in result.Findings.Take(MaximumRows))
        {
            string repository = _context.Array("Repositories").FirstOrDefault(node => (string?)node?["Id"] == finding.RepositoryId)?["Name"]?.ToString()
                ?? finding.RepositoryId;
            string detail = string.IsNullOrWhiteSpace(finding.Detail) ? $"{repository} · {finding.Path}" : $"{repository} · {finding.Path}\n{finding.Detail}";
            string path = finding.Path;
            FindingsPanel.Children.Add(PageKit.Row(T("Records.Kind." + finding.Kind), detail,
                PageKit.Button(T("Common.OpenFolder"), (_, _) => _context.OpenFolder(path))));
        }
        if (result.Findings.Count > MaximumRows)
            FindingsPanel.Children.Add(new TextBlock { Text = T("Common.MoreRows", result.Findings.Count - MaximumRows), Style = PageKit.Style("CaptionTextStyle") });
    }
}
