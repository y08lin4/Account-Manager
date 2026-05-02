using System.Windows;

namespace AccountManager;

public partial class BatchCategoryTagWindow : Window
{
    public string Category => CategoryBox.Text.Trim();

    public string Tags => TagsBox.Text.Trim();

    public BatchCategoryTagWindow(string scopeName, int count)
    {
        InitializeComponent();
        ScopeText.Text = $"{scopeName} · {count} 个账号";
        Loaded += (_, _) => CategoryBox.Focus();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CategoryBox.Text) && string.IsNullOrWhiteSpace(TagsBox.Text))
        {
            AppDialog.Warning(this, "批量分类/标签", "请填写分类或标签。");
            return;
        }

        DialogResult = true;
    }
}
