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
            MessageBox.Show(this, "没有内容。", "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var preview = UpdatePreview();
        if (preview.ExistingDuplicates > 0 || preview.InputDuplicates > 0 || preview.Errors.Count > 0)
        {
            var result = MessageBox.Show(this,
                BuildConfirmMessage(preview),
                "确认导入",
                MessageBoxButton.YesNo,
                preview.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
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
        foreach (var duplicate in preview.DuplicateSamples.Take(12)) sb.AppendLine($"重复：{duplicate}");
        if (preview.DuplicateSamples.Count > 12) sb.AppendLine($"……还有 {preview.DuplicateSamples.Count - 12} 个重复账号");
        foreach (var error in preview.Errors.Take(20)) sb.AppendLine(error);
        if (preview.Errors.Count > 20) sb.AppendLine($"……还有 {preview.Errors.Count - 20} 条错误");
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
