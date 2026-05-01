using AccountManager.Models;
using AccountManager.Services;
using System.Text;
using System.Windows;

namespace AccountManager;

public partial class ImportTextWindow : Window
{
    public string ImportText => ImportTextBox.Text;
    public string DefaultCategory => CategoryBox.Text;
    public string DefaultTags => TagsBox.Text;
    public DuplicateMode DuplicateMode => DuplicateBox.SelectedIndex switch
    {
        1 => DuplicateMode.Overwrite,
        2 => DuplicateMode.KeepBoth,
        _ => DuplicateMode.Skip
    };

    public ImportTextWindow(string initialText = "", string defaultCategory = "", string defaultTags = "", string sourceDescription = "")
    {
        InitializeComponent();
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

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ImportTextBox.Text))
        {
            MessageBox.Show(this, "没有内容。", "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdatePreview();
        DialogResult = true;
    }

    private void UpdatePreview()
    {
        var parsed = AccountImportExport.ParsePlainText(ImportTextBox.Text, DefaultCategory, DefaultTags);
        PreviewText.Text = $"总 {parsed.TotalLines} · 可导入 {parsed.Accounts.Count} · 错误 {parsed.Errors.Count}";

        if (parsed.Errors.Count == 0)
        {
            PreviewErrorsBox.Text = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        foreach (var error in parsed.Errors.Take(20)) sb.AppendLine(error);
        if (parsed.Errors.Count > 20) sb.AppendLine($"……还有 {parsed.Errors.Count - 20} 条");
        PreviewErrorsBox.Text = sb.ToString();
    }
}
