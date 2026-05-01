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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AccountManager;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int ClipboardClearSeconds = 30;

    private readonly AccountDatabase _database;
    private readonly SecurityService _security;
    private readonly ObservableCollection<AccountRecord> _visibleAccounts = new();
    private readonly ObservableCollection<FilterItem> _categoryFilters = new();
    private readonly ObservableCollection<FilterItem> _tagFilters = new();
    private readonly DispatcherTimer _clipboardTimer;
    private readonly DispatcherTimer _totpTimer;

    private List<AccountRecord> _allAccounts = new();
    private long _editingId;
    private string _searchText = string.Empty;
    private bool _refreshingFilters;
    private bool _showPassword;
    private bool _editingPassword;
    private bool _editingTwoFa;
    private string _passwordValue = string.Empty;
    private string _twoFaSecret = string.Empty;
    private string _lastTotpSecret = string.Empty;
    private long _lastTotpStep = -1;
    private string _lastTotpCode = string.Empty;
    private bool _lastTotpValid;
    private string? _lastCopiedText;

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
        CategoryListBox.ItemsSource = _categoryFilters;
        TagListBox.ItemsSource = _tagFilters;

        _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ClipboardClearSeconds) };
        _clipboardTimer.Tick += ClipboardTimer_Tick;
        _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _totpTimer.Tick += (_, _) => RefreshTotpDisplay();
        _totpTimer.Start();

        Loaded += (_, _) =>
        {
            LoadData();
            SearchBox.Focus();
        };
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
        var currentCategory = (CategoryListBox.SelectedItem as FilterItem)?.Name ?? string.Empty;
        var currentTag = (TagListBox.SelectedItem as FilterItem)?.Name ?? string.Empty;

        _categoryFilters.Clear();
        _categoryFilters.Add(new FilterItem("", "全部", _allAccounts.Count));
        foreach (var item in _allAccounts
                     .Where(a => !string.IsNullOrWhiteSpace(a.Category))
                     .GroupBy(a => a.Category.Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            _categoryFilters.Add(new FilterItem(item.Key, item.Key, item.Count()));
        }
        CategoryListBox.SelectedItem = _categoryFilters.FirstOrDefault(i => string.Equals(i.Name, currentCategory, StringComparison.OrdinalIgnoreCase))
                                       ?? _categoryFilters.FirstOrDefault();

        _tagFilters.Clear();
        _tagFilters.Add(new FilterItem("", "全部", _allAccounts.Count));
        foreach (var item in _allAccounts
                     .SelectMany(a => AccountDatabase.SplitTags(a.Tags))
                     .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            _tagFilters.Add(new FilterItem(item.Key, item.Key, item.Count()));
        }
        TagListBox.SelectedItem = _tagFilters.FirstOrDefault(i => string.Equals(i.Name, currentTag, StringComparison.OrdinalIgnoreCase))
                                  ?? _tagFilters.FirstOrDefault();

        _refreshingFilters = false;
    }

    private void ApplyFilters()
    {
        if (_refreshingFilters) return;
        var category = (CategoryListBox.SelectedItem as FilterItem)?.Name ?? string.Empty;
        var tag = (TagListBox.SelectedItem as FilterItem)?.Name ?? string.Empty;
        var filtered = AccountSearch.Filter(_allAccounts, SearchText, category, tag);

        _visibleAccounts.Clear();
        foreach (var account in filtered) _visibleAccounts.Add(account);

        EmptyHintText.Visibility = _visibleAccounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"{_visibleAccounts.Count}/{_allAccounts.Count} · {AppPaths.BuildMode} · {AppPaths.DataDirectory}";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchText = SearchBox.Text;
        ApplyFilters();
    }

    private void SidebarFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRecord account) return;
        _editingId = account.Id;
        EmailBox.Text = account.Email;
        SetSecrets(account.Password, account.TwoFa);
        CategoryBox.Text = account.Category;
        TagsBox.Text = account.Tags;
        RemarkBox.Text = account.Remark;
    }

    private void AccountsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GetSelectedAccount() is not null) CopySelectedLine_Click(sender, e);
    }

    private void AccountsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);
        if (row is not null)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private static T? FindVisualParent<T>(DependencyObject source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target) return target;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        ClearEditor();
    }

    private void ClearEditor()
    {
        _editingId = 0;
        EmailBox.Clear();
        SetSecrets(string.Empty, string.Empty);
        CategoryBox.Clear();
        TagsBox.Clear();
        RemarkBox.Clear();
        AccountsGrid.SelectedItem = null;
        EmailBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var password = GetPasswordText();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            MessageBox.Show(this, "请输入有效邮箱。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "密码不能为空。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var account = new AccountRecord
        {
            Id = _editingId,
            Email = email,
            Password = password,
            TwoFa = GetTwoFaText(),
            Category = CategoryBox.Text,
            Tags = TagsBox.Text,
            Remark = RemarkBox.Text
        };

        try
        {
            if (_editingId == 0) _database.Insert(account);
            else _database.Update(account);

            LoadData();
            StatusText.Text = "已保存";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = GetSelectedAccount()?.Id ?? _editingId;
        if (selectedId == 0)
        {
            MessageBox.Show(this, "请先选择账号。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, "确定删除？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            _database.Delete(selectedId);
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
            Title = "选择 TXT",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true) OpenImportFiles(dialog.FileNames);
    }

    private async void QuickImport_Click(object sender, RoutedEventArgs e)
    {
        var text = await NativeClipboardService.GetTextAsync();
        OpenQuickImportWindow(text);
    }

    private void PasteImport_Click(object sender, RoutedEventArgs e)
    {
        QuickImport_Click(sender, e);
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = ((string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>())
                .Where(File.Exists)
                .ToArray();

            if (files.Length > 0)
            {
                OpenImportFiles(files);
                e.Handled = true;
                return;
            }
        }

        if (e.Data.GetDataPresent(DataFormats.Text))
        {
            OpenImportWindow(e.Data.GetData(DataFormats.Text) as string ?? string.Empty, "拖拽文本");
            e.Handled = true;
        }
    }

    private void OpenImportFiles(IReadOnlyCollection<string> fileNames)
    {
        var (text, errors) = ReadImportFiles(fileNames);
        if (errors.Count > 0)
        {
            MessageBox.Show(this,
                string.Join("\n", errors.Take(10)) + (errors.Count > 10 ? $"\n……还有 {errors.Count - 10} 个" : string.Empty),
                "导入提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "没有内容。", "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OpenImportWindow(text, $"{fileNames.Count} 个文件");
    }

    private static (string Text, List<string> Errors) ReadImportFiles(IEnumerable<string> fileNames)
    {
        var text = new StringBuilder();
        var errors = new List<string>();

        foreach (var fileName in fileNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(fileName))
                {
                    errors.Add($"{fileName}：文件不存在");
                    continue;
                }

                text.AppendLine(File.ReadAllText(fileName, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(fileName)}：{ex.Message}");
            }
        }

        return (text.ToString(), errors);
    }

    private void OpenImportWindow(string initialText, string sourceDescription = "")
    {
        var window = new ImportTextWindow(initialText, sourceDescription: sourceDescription) { Owner = this };
        if (window.ShowDialog() != true) return;

        ImportParsedText(window.ImportText, window.DefaultCategory, window.DefaultTags, window.DuplicateMode);
    }

    private void OpenQuickImportWindow(string initialText)
    {
        var window = new QuickImportWindow(initialText) { Owner = this };
        if (window.ShowDialog() != true) return;

        ImportParsedText(window.ImportText, window.DefaultCategory, window.DefaultTags, window.DuplicateMode);
    }

    private void ImportParsedText(string text, string defaultCategory, string defaultTags, DuplicateMode duplicateMode)
    {
        var parsed = AccountImportExport.ParsePlainText(text, defaultCategory, defaultTags);
        if (parsed.Accounts.Count == 0)
        {
            MessageBox.Show(this, BuildImportResultMessage(parsed.TotalLines, 0, 0, 0, 0, parsed.Errors), "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = _database.ImportAccounts(parsed.Accounts, duplicateMode);
            result.TotalLines = parsed.TotalLines;
            result.Errors.AddRange(parsed.Errors);
            LoadData();
            MessageBox.Show(this,
                BuildImportResultMessage(result.TotalLines, result.Parsed, result.Inserted, result.Updated, result.SkippedDuplicates, result.Errors),
                "导入完成",
                MessageBoxButton.OK,
                result.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string BuildImportResultMessage(int total, int parsed, int inserted, int updated, int skipped, IReadOnlyList<string> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"总行数：{total}");
        sb.AppendLine($"成功解析：{parsed}");
        sb.AppendLine($"新增：{inserted}");
        sb.AppendLine($"覆盖：{updated}");
        sb.AppendLine($"跳过：{skipped}");
        sb.AppendLine($"错误：{errors.Count}");

        if (errors.Count > 0)
        {
            sb.AppendLine();
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
            MessageBox.Show(this, dialog.FileName, "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
            Title = "备份位置",
            InitialDirectory = AppPaths.SuggestedBackupDirectory
        };

        if (dialog.ShowDialog(this) != true) return;

        var backupPath = Path.Combine(dialog.FolderName, AccountImportExport.CreateBackupFileName());
        try
        {
            _database.CreateDatabaseBackup(backupPath);
            MessageBox.Show(this, backupPath, "备份完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
            Title = "选择备份",
            Filter = "AccountManager backup (*.db;*.ambak)|*.db;*.ambak|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        if (MessageBox.Show(this, "将替换当前数据库并重启。继续？", "导入备份", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            _database.RestoreDatabaseFromBackup(dialog.FileName);
            MessageBox.Show(this, "已导入，程序将重启。", "导入备份", MessageBoxButton.OK, MessageBoxImage.Information);
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
            StatusText.Text = "安全设置已更新";
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

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        CommitSecretEdits();
        _showPassword = !_showPassword;
        RefreshPasswordDisplay();
    }

    private void SetSecrets(string password, string twoFa)
    {
        _passwordValue = password;
        _twoFaSecret = twoFa;
        _editingPassword = false;
        _editingTwoFa = false;
        RefreshPasswordDisplay();
        RefreshTotpDisplay();
    }

    private void RefreshPasswordDisplay()
    {
        if (_editingPassword) return;

        PasswordBox.IsReadOnly = true;
        PasswordBox.Text = _showPassword ? _passwordValue : MaskSecret(_passwordValue);
        TogglePasswordButton.Opacity = _showPassword ? 1.0 : 0.72;
    }

    private void RefreshTotpDisplay()
    {
        if (_editingTwoFa) return;

        TwoFaCodeBox.IsReadOnly = true;
        if (string.IsNullOrWhiteSpace(_twoFaSecret))
        {
            SetTotpTextIfChanged(string.Empty);
            _lastTotpSecret = string.Empty;
            _lastTotpStep = -1;
            _lastTotpCode = string.Empty;
            _lastTotpValid = false;
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var step = now / 30;
        var secondsRemaining = 30 - (int)(now % 30);

        if (step == _lastTotpStep && string.Equals(_twoFaSecret, _lastTotpSecret, StringComparison.Ordinal))
        {
            if (_lastTotpValid)
            {
                SetTotpTextIfChanged($"{_lastTotpCode}  ({secondsRemaining}s)");
            }
            else
            {
                SetTotpTextIfChanged("2FA密钥无效");
            }
            return;
        }

        if (TotpService.TryGenerateCode(_twoFaSecret, out var code, out _))
        {
            _lastTotpSecret = _twoFaSecret;
            _lastTotpStep = step;
            _lastTotpCode = code;
            _lastTotpValid = true;
            SetTotpTextIfChanged($"{code}  ({secondsRemaining}s)");
        }
        else
        {
            _lastTotpSecret = _twoFaSecret;
            _lastTotpStep = step;
            _lastTotpCode = string.Empty;
            _lastTotpValid = false;
            SetTotpTextIfChanged("2FA密钥无效");
        }
    }

    private void SetTotpTextIfChanged(string value)
    {
        if (!string.Equals(TwoFaCodeBox.Text, value, StringComparison.Ordinal))
        {
            TwoFaCodeBox.Text = value;
        }
    }

    private static string MaskSecret(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : new string('●', Math.Min(Math.Max(value.Length, 6), 18));
    }

    private string GetPasswordText()
    {
        if (_editingPassword) CommitPasswordEdit();
        return _passwordValue;
    }

    private string GetTwoFaText()
    {
        if (_editingTwoFa) CommitTwoFaEdit();
        return _twoFaSecret;
    }

    private string GetCurrentTotpCode()
    {
        return TotpService.TryGenerateCode(GetTwoFaText(), out var code, out _) ? code : string.Empty;
    }

    private void PasswordBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            BeginPasswordEdit();
            e.Handled = true;
            return;
        }

        if (!_editingPassword)
        {
            CopyText(_passwordValue, "密码已复制");
            e.Handled = true;
        }
    }

    private void TwoFaCodeBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            BeginTwoFaEdit();
            e.Handled = true;
            return;
        }

        if (!_editingTwoFa)
        {
            var code = GetCurrentTotpCode();
            if (!string.IsNullOrEmpty(code)) CopyText(code, "2FA验证码已复制");
            e.Handled = true;
        }
    }

    private void BeginPasswordEdit()
    {
        _editingPassword = true;
        PasswordBox.IsReadOnly = false;
        PasswordBox.Text = _passwordValue;
        PasswordBox.Focus();
        PasswordBox.SelectAll();
        StatusText.Text = "正在编辑密码";
    }

    private void BeginTwoFaEdit()
    {
        _editingTwoFa = true;
        TwoFaCodeBox.IsReadOnly = false;
        TwoFaCodeBox.FontFamily = new FontFamily("Consolas");
        TwoFaCodeBox.Text = _twoFaSecret;
        TwoFaCodeBox.Focus();
        TwoFaCodeBox.SelectAll();
        StatusText.Text = "正在编辑2FA密钥";
    }

    private void PasswordBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_editingPassword) CommitPasswordEdit();
    }

    private void TwoFaCodeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_editingTwoFa) CommitTwoFaEdit();
    }

    private void SecretEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSecretEdits();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _editingPassword = false;
            _editingTwoFa = false;
            RefreshPasswordDisplay();
            RefreshTotpDisplay();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void CommitSecretEdits()
    {
        if (_editingPassword) CommitPasswordEdit();
        if (_editingTwoFa) CommitTwoFaEdit();
    }

    private void CommitPasswordEdit()
    {
        _passwordValue = PasswordBox.Text;
        _editingPassword = false;
        RefreshPasswordDisplay();
    }

    private void CommitTwoFaEdit()
    {
        _twoFaSecret = TwoFaCodeBox.Text.Trim();
        _editingTwoFa = false;
        RefreshTotpDisplay();
    }

    private AccountRecord? GetSelectedAccount() => AccountsGrid.SelectedItem as AccountRecord;

    private void CopyEmail_Click(object sender, RoutedEventArgs e) => CopyText(EmailBox.Text, "邮箱已复制");
    private void CopyPassword_Click(object sender, RoutedEventArgs e) => CopyText(GetPasswordText(), "密码已复制");
    private void CopyTwoFa_Click(object sender, RoutedEventArgs e) => CopyText(GetTwoFaText(), "2FA 已复制");

    private void CopyLine_Click(object sender, RoutedEventArgs e)
    {
        CopyText($"{EmailBox.Text}--{GetPasswordText()}--{GetTwoFaText()}", "整行已复制");
    }

    private void CopySelectedEmail_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAccount() is { } account) CopyText(account.Email, "邮箱已复制");
    }

    private void CopySelectedPassword_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAccount() is { } account) CopyText(account.Password, "密码已复制");
    }

    private void CopySelectedTwoFa_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAccount() is { } account) CopyText(account.TwoFa, "2FA 已复制");
    }

    private void CopySelectedLine_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAccount() is { } account) CopyText($"{account.Email}--{account.Password}--{account.TwoFa}", "整行已复制");
    }

    private async void CopyText(string text, string status)
    {
        if (string.IsNullOrEmpty(text)) return;
        StatusText.Text = "复制中…";

        if (!await NativeClipboardService.SetTextAsync(text))
        {
            StatusText.Text = "剪贴板被占用，请稍后重试";
            return;
        }

        _lastCopiedText = text;
        _clipboardTimer.Stop();
        _clipboardTimer.Start();
        StatusText.Text = $"{status} · {ClipboardClearSeconds}s 后清空剪贴板";
    }

    private async void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        _clipboardTimer.Stop();
        var copiedText = _lastCopiedText;
        _lastCopiedText = null;
        if (string.IsNullOrEmpty(copiedText)) return;

        if (await NativeClipboardService.ClearIfTextEqualsAsync(copiedText))
        {
            StatusText.Text = "剪贴板已清空";
        }
    }

    private static void RestartApplication()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    private sealed class FilterItem
    {
        public string Name { get; }
        public string DisplayName { get; }

        public FilterItem(string name, string label, int count)
        {
            Name = name;
            DisplayName = $"{label}  {count}";
        }
    }
}
