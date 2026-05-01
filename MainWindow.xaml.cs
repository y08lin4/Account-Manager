using AccountManager.Models;
using AccountManager.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace AccountManager;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AccountDatabase _database;
    private readonly SecurityService _security;
    private readonly ObservableCollection<AccountRecord> _visibleAccounts = new();
    private List<AccountRecord> _allAccounts = new();
    private long _editingId;
    private string _searchText = string.Empty;
    private bool _refreshingFilters;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
        }
    }

    public MainWindow(AccountDatabase database, SecurityService security)
    {
        InitializeComponent();
        _database = database;
        _security = security;
        DataContext = this;
        Title = $"AccountManager - {AppPaths.BuildMode}";
        AccountsGrid.ItemsSource = _visibleAccounts;
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        try
        {
            _allAccounts = _database.GetAll();
            RefreshFilters();
            ApplyFilters();
            ClearEditor();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshFilters()
    {
        _refreshingFilters = true;
        var currentCategory = CategoryFilterBox.SelectedItem as string;
        var currentTag = TagFilterBox.SelectedItem as string;

        CategoryFilterBox.Items.Clear();
        CategoryFilterBox.Items.Add("全部分类");
        foreach (var category in _database.GetCategories()) CategoryFilterBox.Items.Add(category);
        CategoryFilterBox.SelectedItem = CategoryFilterBox.Items.Contains(currentCategory) ? currentCategory : "全部分类";

        TagFilterBox.Items.Clear();
        TagFilterBox.Items.Add("全部标签");
        foreach (var tag in _database.GetTags()) TagFilterBox.Items.Add(tag);
        TagFilterBox.SelectedItem = TagFilterBox.Items.Contains(currentTag) ? currentTag : "全部标签";

        _refreshingFilters = false;
    }

    private void ApplyFilters()
    {
        if (_refreshingFilters) return;
        var category = GetSelectedFilter(CategoryFilterBox, "全部分类");
        var tag = GetSelectedFilter(TagFilterBox, "全部标签");
        var filtered = AccountSearch.Filter(_allAccounts, SearchText, category, tag);

        _visibleAccounts.Clear();
        foreach (var account in filtered) _visibleAccounts.Add(account);

        StatusText.Text = $"共 {_allAccounts.Count} 个账号，当前显示 {_visibleAccounts.Count} 个 · {AppPaths.BuildMode} · 数据目录：{AppPaths.DataDirectory}";
    }

    private static string GetSelectedFilter(System.Windows.Controls.ComboBox comboBox, string allText)
    {
        var value = comboBox.SelectedItem as string;
        return string.IsNullOrWhiteSpace(value) || value == allText ? string.Empty : value;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchText = SearchBox.Text;
        ApplyFilters();
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRecord account) return;
        _editingId = account.Id;
        EmailBox.Text = account.Email;
        PasswordBox.Text = account.Password;
        TwoFaBox.Text = account.TwoFa;
        CategoryBox.Text = account.Category;
        TagsBox.Text = account.Tags;
        RemarkBox.Text = account.Remark;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        ClearEditor();
    }

    private void ClearEditor()
    {
        _editingId = 0;
        EmailBox.Clear();
        PasswordBox.Clear();
        TwoFaBox.Clear();
        CategoryBox.Clear();
        TagsBox.Clear();
        RemarkBox.Clear();
        AccountsGrid.SelectedItem = null;
        EmailBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            MessageBox.Show(this, "请输入有效邮箱。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(PasswordBox.Text))
        {
            MessageBox.Show(this, "密码不能为空。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var account = new AccountRecord
        {
            Id = _editingId,
            Email = email,
            Password = PasswordBox.Text,
            TwoFa = TwoFaBox.Text,
            Category = CategoryBox.Text,
            Tags = TagsBox.Text,
            Remark = RemarkBox.Text
        };

        try
        {
            if (_editingId == 0)
            {
                _database.Insert(account);
            }
            else
            {
                _database.Update(account);
            }

            LoadData();
            MessageBox.Show(this, "已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId == 0)
        {
            MessageBox.Show(this, "请先选择要删除的账号。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, "确定删除当前账号吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            _database.Delete(_editingId);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportTxt_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择账号 TXT 文件",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            var text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            OpenImportWindow(text);
        }
    }

    private void PasteImport_Click(object sender, RoutedEventArgs e)
    {
        var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        OpenImportWindow(text);
    }

    private void OpenImportWindow(string initialText)
    {
        var window = new ImportTextWindow(initialText) { Owner = this };
        if (window.ShowDialog() != true) return;

        var parsed = AccountImportExport.ParsePlainText(window.ImportText, window.DefaultCategory, window.DefaultTags);
        if (parsed.Accounts.Count == 0)
        {
            MessageBox.Show(this, BuildImportErrorMessage(parsed.TotalLines, 0, 0, 0, 0, parsed.Errors), "没有可导入账号", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = _database.ImportAccounts(parsed.Accounts, window.DuplicateMode);
            result.TotalLines = parsed.TotalLines;
            result.Errors.AddRange(parsed.Errors);
            LoadData();
            MessageBox.Show(this,
                BuildImportErrorMessage(result.TotalLines, result.Parsed, result.Inserted, result.Updated, result.SkippedDuplicates, result.Errors),
                "导入完成",
                MessageBoxButton.OK,
                result.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string BuildImportErrorMessage(int total, int parsed, int inserted, int updated, int skipped, IReadOnlyList<string> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"总行数：{total}");
        sb.AppendLine($"成功解析：{parsed}");
        sb.AppendLine($"新增：{inserted}");
        sb.AppendLine($"覆盖更新：{updated}");
        sb.AppendLine($"跳过重复：{skipped}");
        sb.AppendLine($"格式错误：{errors.Count}");

        if (errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("前几条错误：");
            foreach (var error in errors.Take(8)) sb.AppendLine(error);
            if (errors.Count > 8) sb.AppendLine($"……还有 {errors.Count - 8} 条");
        }

        return sb.ToString();
    }

    private void ExportTxt_Click(object sender, RoutedEventArgs e)
    {
        Export("导出 TXT", "Text files (*.txt)|*.txt", ".txt", path => AccountImportExport.WriteTextExport(path, _visibleAccounts));
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        Export("导出 CSV", "CSV files (*.csv)|*.csv", ".csv", path => AccountImportExport.WriteCsvExport(path, _visibleAccounts));
    }

    private void Export(string title, string filter, string extension, Action<string> writer)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = extension,
            FileName = $"AccountManager_Export_{DateTime.Now:yyyyMMdd_HHmmss}{extension}"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            writer(dialog.FileName);
            MessageBox.Show(this, $"已导出：{dialog.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.SuggestedBackupDirectory);
        var dialog = new OpenFolderDialog
        {
            Title = "选择备份保存位置",
            InitialDirectory = AppPaths.SuggestedBackupDirectory
        };

        if (dialog.ShowDialog(this) != true) return;

        var backupPath = Path.Combine(dialog.FolderName, AccountImportExport.CreateBackupFileName());
        try
        {
            _database.CreateDatabaseBackup(backupPath);
            MessageBox.Show(this, $"备份已创建：\n{backupPath}", "备份完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 AccountManager 备份文件",
            Filter = "AccountManager backup (*.db;*.ambak)|*.db;*.ambak|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        var confirm = MessageBox.Show(this,
            "导入备份会替换当前数据库。程序会先在数据目录生成一份恢复前安全副本，然后重启。\n\n恢复后需要输入该备份对应的主密码。确定继续吗？",
            "确认导入备份",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _database.RestoreDatabaseFromBackup(dialog.FileName);
            MessageBox.Show(this, "备份已导入。程序将重启，请输入该备份对应的主密码。", "恢复完成", MessageBoxButton.OK, MessageBoxImage.Information);
            RestartApplication();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "恢复失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SecuritySettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SecuritySetupWindow(true, _security.PasswordHint, _security.RecoveryQuestion) { Owner = this };
        if (window.ShowDialog() != true) return;

        try
        {
            _security.ChangeSecurity(window.MasterPassword, window.PasswordHint, window.RecoveryQuestion, window.RecoveryAnswer);
            MessageBox.Show(this, "安全设置已更新。下次解锁使用新主密码。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _security.Lock();
        RestartApplication();
    }

    private void CopyEmail_Click(object sender, RoutedEventArgs e) => CopyText(EmailBox.Text, "邮箱已复制。 ");
    private void CopyPassword_Click(object sender, RoutedEventArgs e) => CopyText(PasswordBox.Text, "密码已复制。 ");
    private void CopyTwoFa_Click(object sender, RoutedEventArgs e) => CopyText(TwoFaBox.Text, "2FA 已复制。 ");

    private void CopyLine_Click(object sender, RoutedEventArgs e)
    {
        CopyText($"{EmailBox.Text}--{PasswordBox.Text}--{TwoFaBox.Text}", "整行已复制。 ");
    }

    private void CopyText(string text, string status)
    {
        if (string.IsNullOrEmpty(text)) return;
        Clipboard.SetText(text);
        StatusText.Text = status;
    }

    private static void RestartApplication()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }
}


