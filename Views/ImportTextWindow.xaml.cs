using AccountManager.Models;
using AccountManager.Services;
using System.Text;
using System.Windows;

namespace AccountManager;

public partial class ImportTextWindow : Window
{
    private readonly List<string> _existingEmails;

    public string ImportText => ImportTextBox.Text;
    public string DefaultCategory => CategoryBox.Text;
    public string DefaultTags => TagsBox.Text;
    public DuplicateMode DuplicateMode => DuplicateBox.SelectedIndex switch
    {
        1 => DuplicateMode.Overwrite,
        2 => DuplicateMode.KeepBoth,
        _ => DuplicateMode.Skip
    };

    public ImportTextWindow(string initialText = "", string defaultCategory = "", string defaultTags = "", string sourceDescription = "", IEnumerable<string>? existingEmails = null)
    {
        InitializeComponent();
        _existingEmails = existingEmails?.ToList() ?? new List<string>();
        ImportTextBox.Text = initialText;
        CategoryBox.Text = defaultCategory;
        TagsBox.Text = defaultTags;
        SourceText.Text = string.IsNullOrWhiteSpace(sourceDescription) ? string.Empty : $"来源：{sourceDescription}";
        Loaded += (_, _) =>
        {
            ImportTextBox.Focus();
            UpdatePreview();
        };
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void DuplicateBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded) UpdatePreview();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ImportTextBox.Text))
        {
            AppDialog.Warning(this, "导入", "没有内容。");
            return;
        }

        var preview = UpdatePreview();
        if (preview.ExistingDuplicates > 0 || preview.InputDuplicates > 0 || preview.Errors.Count > 0)
        {
            if (!AppDialog.Confirm(this,
                    "确认导入",
                    "导入内容包含重复或错误，请确认统计结果。",
                    BuildConfirmMessage(preview),
                    preview.Errors.Count > 0 ? AppDialogKind.Warning : AppDialogKind.Info))
            {
                return;
            }
        }

        DialogResult = true;
    }

    private ImportPreviewResult UpdatePreview()
    {
        var preview = AccountImportExport.PreviewPlainText(ImportTextBox.Text, DefaultCategory, DefaultTags, DuplicateMode, _existingEmails);
        PreviewText.Text = $"总 {preview.TotalLines} · 解析 {preview.Parsed} · 新增 {preview.Inserted} · 覆盖 {preview.Updated} · 跳过 {preview.SkippedDuplicates} · 重复 {preview.ExistingDuplicates} · 文内重复 {preview.InputDuplicates} · 错误 {preview.Errors.Count}";

        if (preview.Errors.Count == 0 && preview.DuplicateSamples.Count == 0)
        {
            PreviewErrorsBox.Text = string.Empty;
            return preview;
        }

        var sb = new StringBuilder();
        foreach (var duplicate in preview.DuplicateSamples) sb.AppendLine($"重复：{duplicate}");
        foreach (var error in preview.Errors) sb.AppendLine(error);
        PreviewErrorsBox.Text = sb.ToString();
        return preview;
    }

    private static string BuildConfirmMessage(ImportPreviewResult preview)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"解析：{preview.Parsed}");
        sb.AppendLine($"新增：{preview.Inserted}");
        sb.AppendLine($"覆盖：{preview.Updated}");
        sb.AppendLine($"跳过重复：{preview.SkippedDuplicates}");
        sb.AppendLine($"数据库重复：{preview.ExistingDuplicates}");
        sb.AppendLine($"文本内重复：{preview.InputDuplicates}");
        sb.AppendLine($"格式错误：{preview.Errors.Count}");
        sb.AppendLine();
        sb.AppendLine("继续导入？");
        return sb.ToString();
    }
}
