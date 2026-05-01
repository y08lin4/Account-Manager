using AccountManager.Models;
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

    public ImportTextWindow(string initialText = "", string defaultCategory = "", string defaultTags = "")
    {
        InitializeComponent();
        ImportTextBox.Text = initialText;
        CategoryBox.Text = defaultCategory;
        TagsBox.Text = defaultTags;
        Loaded += (_, _) => ImportTextBox.Focus();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ImportTextBox.Text))
        {
            MessageBox.Show(this, "请粘贴或加载要导入的账号文本。", "没有内容", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
