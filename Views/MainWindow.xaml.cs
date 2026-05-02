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
    private const string CompactWindowMode = "compact";
    private const string ExpandedWindowMode = "expanded";
    private const string NoCategoryFilter = "__NO_CATEGORY__";

    private readonly AccountDatabase _database;
    private readonly SecurityService _security;
    private readonly LocalApiServer _apiServer;
    private readonly ObservableCollection<AccountRecord> _visibleAccounts = new();
    private readonly ObservableCollection<FilterItem> _categoryFilters = new();
    private readonly ObservableCollection<FilterItem> _tagFilters = new();
    private readonly DispatcherTimer _clipboardTimer;
    private readonly DispatcherTimer _totpTimer;
    private readonly DispatcherTimer _autoLockTimer;

    private List<AccountRecord> _allAccounts = new();
    private long _editingId;
    private string _searchText = string.Empty;
    private bool _refreshingFilters;
    private bool _compactMode = true;
    private bool _syncingSelection;
    private bool _isSoftLocked;
    private DateTime _lastActivityAt = DateTime.Now;
    private int _autoLockMinutes;
    private bool _editingEmail;
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
    private string _apiStatus = string.Empty;

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
        _apiServer = new LocalApiServer(_database, _security, RequestDataRefresh);
        DataContext = this;
        Title = $"AccountManager - {AppPaths.BuildMode}";
        AccountsGrid.ItemsSource = _visibleAccounts;
        CompactAccountsList.ItemsSource = _visibleAccounts;
        CategoryListBox.ItemsSource = _categoryFilters;
        TagListBox.ItemsSource = _tagFilters;
        ApplySavedViewMode();

        _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ClipboardClearSeconds) };
        _clipboardTimer.Tick += ClipboardTimer_Tick;
        _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _totpTimer.Tick += (_, _) => RefreshTotpDisplay();
        _totpTimer.Start();
        _autoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autoLockTimer.Tick += AutoLockTimer_Tick;
        _autoLockTimer.Start();

        PreviewMouseDown += (_, _) => MarkActivity();
        PreviewKeyDown += (_, _) => MarkActivity();

        Loaded += (_, _) =>
        {
            RefreshAutoLockSettings();
            LoadData();
            StartLocalApi();
            SearchBox.Focus();
        };

        Closed += (_, _) => _apiServer.Dispose();
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
            AppDialog.Error(this, "加载失败", ex.Message);
        }
    }

    private void RefreshFilters()
    {
        _refreshingFilters = true;
        var currentCategory = (CategoryListBox.SelectedItem as FilterItem)?.Name ?? string.Empty;
        var currentTag = (TagListBox.SelectedItem as FilterItem)?.Name ?? string.Empty;

        _categoryFilters.Clear();
        _categoryFilters.Add(new FilterItem("", "全部", _allAccounts.Count));
        var noCategoryCount = _allAccounts.Count(a => string.IsNullOrWhiteSpace(a.Category));
        if (noCategoryCount > 0)
        {
            _categoryFilters.Add(new FilterItem(NoCategoryFilter, "无分组", noCategoryCount));
        }

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
        var source = string.Equals(category, NoCategoryFilter, StringComparison.Ordinal)
            ? _allAccounts.Where(a => string.IsNullOrWhiteSpace(a.Category))
            : _allAccounts;
        var categoryFilter = string.Equals(category, NoCategoryFilter, StringComparison.Ordinal) ? string.Empty : category;
        var filtered = AccountSearch.Filter(source, SearchText, categoryFilter, tag);

        _visibleAccounts.Clear();
        foreach (var account in filtered) _visibleAccounts.Add(account);

        EmptyHintText.Visibility = _visibleAccounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var apiSuffix = string.IsNullOrWhiteSpace(_apiStatus) ? string.Empty : $" · {_apiStatus}";
        StatusText.Text = $"{_visibleAccounts.Count}/{_allAccounts.Count} · {AppPaths.BuildMode} · {AppPaths.DataDirectory}{apiSuffix}";
    }

    private void StartLocalApi()
    {
        if (!IsApiEnabled())
        {
            _apiServer.Stop();
            _apiStatus = "API已关闭";
            ApplyFilters();
            return;
        }

        if (_apiServer.Start())
        {
            _apiStatus = $"API {_apiServer.BaseUrl}";
        }
        else
        {
            _apiStatus = $"API未启动：{_apiServer.LastError}";
        }

        ApplyFilters();
    }

    private bool IsApiEnabled()
    {
        return !string.Equals(_database.GetSetting(AppSettingKeys.ApiEnabled), "false", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshRuntimeSettings()
    {
        RefreshAutoLockSettings();
        StartLocalApi();
    }

    private void RefreshAutoLockSettings()
    {
        _autoLockMinutes = int.TryParse(_database.GetSetting(AppSettingKeys.AutoLockMinutes), out var minutes) && minutes > 0 ? minutes : 0;
        MarkActivity();
    }

    private void MarkActivity()
    {
        if (!_isSoftLocked) _lastActivityAt = DateTime.Now;
    }

    private void AutoLockTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSoftLocked || _autoLockMinutes <= 0 || !IsActive) return;
        if (DateTime.Now - _lastActivityAt >= TimeSpan.FromMinutes(_autoLockMinutes))
        {
            SoftLock();
        }
    }

    private void RequestDataRefresh()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_security.IsUnlocked) LoadData();
        }), DispatcherPriority.Background);
    }

    private void ApplySavedViewMode()
    {
        var mode = _database.GetSetting(AppSettingKeys.WindowMode);
        var compact = !string.Equals(mode, ExpandedWindowMode, StringComparison.OrdinalIgnoreCase);
        ApplyViewMode(compact, persist: false, resizeWindow: true);
    }

    private void ViewModeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyViewMode(!_compactMode, persist: true, resizeWindow: true);
        SearchBox.Focus();
    }

    private void ApplyViewMode(bool compact, bool persist, bool resizeWindow)
    {
        _compactMode = compact;
        ViewModeButton.Content = compact ? "展开" : "收起";
        ViewModeButton.ToolTip = compact ? "切换到大窗口管理模式" : "切换到小窗日常模式";

        if (compact)
        {
            MinWidth = 380;
            MinHeight = 560;
            if (resizeWindow)
            {
                Width = 440;
                Height = 760;
            }

            FilterColumn.Width = new GridLength(0);
            FirstSeparatorColumn.Width = new GridLength(0);
            ListColumn.MinWidth = 0;
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            SecondSeparatorColumn.Width = new GridLength(0);
            DetailColumn.Width = new GridLength(0);

            MainListRow.Height = new GridLength(1, GridUnitType.Star);
            MainHorizontalSeparatorRow.Height = new GridLength(1);
            MainDetailRow.Height = new GridLength(286);

            SidebarPanel.Visibility = Visibility.Collapsed;
            FirstVerticalSeparator.Visibility = Visibility.Collapsed;
            SecondVerticalSeparator.Visibility = Visibility.Collapsed;
            HorizontalSeparator.Visibility = Visibility.Visible;
            AccountsGrid.Visibility = Visibility.Collapsed;
            CompactAccountsList.Visibility = Visibility.Visible;

            Grid.SetRow(ListPanel, 0);
            Grid.SetColumn(ListPanel, 2);
            ListPanel.Margin = new Thickness(0);

            Grid.SetRow(HorizontalSeparator, 1);
            Grid.SetColumn(HorizontalSeparator, 2);

            Grid.SetRow(DetailPanel, 2);
            Grid.SetColumn(DetailPanel, 2);
            DetailPanel.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            MinWidth = 820;
            MinHeight = 560;
            if (resizeWindow)
            {
                Width = Math.Max(980, Width);
                Height = Math.Max(680, Height);
            }

            FilterColumn.Width = new GridLength(128);
            FirstSeparatorColumn.Width = new GridLength(1);
            ListColumn.MinWidth = 360;
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            SecondSeparatorColumn.Width = new GridLength(1);
            DetailColumn.Width = new GridLength(270);

            MainListRow.Height = new GridLength(1, GridUnitType.Star);
            MainHorizontalSeparatorRow.Height = new GridLength(0);
            MainDetailRow.Height = new GridLength(0);

            SidebarPanel.Visibility = Visibility.Visible;
            FirstVerticalSeparator.Visibility = Visibility.Visible;
            SecondVerticalSeparator.Visibility = Visibility.Visible;
            HorizontalSeparator.Visibility = Visibility.Collapsed;
            AccountsGrid.Visibility = Visibility.Visible;
            CompactAccountsList.Visibility = Visibility.Collapsed;

            Grid.SetRow(ListPanel, 0);
            Grid.SetColumn(ListPanel, 2);
            ListPanel.Margin = new Thickness(8, 0, 8, 0);

            Grid.SetRow(DetailPanel, 0);
            Grid.SetColumn(DetailPanel, 4);
            DetailPanel.Margin = new Thickness(8, 0, 0, 0);
        }

        if (persist)
        {
            _database.SetSettings(new Dictionary<string, string>
            {
                [AppSettingKeys.WindowMode] = compact ? CompactWindowMode : ExpandedWindowMode
            });
        }
    }

    private void ToolbarMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null) return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
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
        if (_syncingSelection) return;

        _syncingSelection = true;
        CompactAccountsList.SelectedItem = account;
        SelectAccount(account);
        _syncingSelection = false;
    }

    private void CompactAccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompactAccountsList.SelectedItem is not AccountRecord account) return;
        if (_syncingSelection) return;

        _syncingSelection = true;
        AccountsGrid.SelectedItem = account;
        SelectAccount(account);
        _syncingSelection = false;
    }

    private void SelectAccount(AccountRecord account)
    {
        _editingId = account.Id;
        _editingEmail = false;
        EmailBox.IsReadOnly = true;
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

    private void CompactAccountsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

    private void CompactAccountsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualParent<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item is not null)
        {
            item.IsSelected = true;
            item.Focus();
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
        _editingEmail = true;
        EmailBox.IsReadOnly = false;
        EmailBox.Clear();
        SetSecrets(string.Empty, string.Empty);
        CategoryBox.Clear();
        TagsBox.Clear();
        RemarkBox.Clear();
        _syncingSelection = true;
        AccountsGrid.SelectedItem = null;
        CompactAccountsList.SelectedItem = null;
        _syncingSelection = false;
        EmailBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editingEmail) CommitEmailEdit();
        CommitSecretEdits();

        var email = EmailBox.Text.Trim();
        var password = GetPasswordText();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            AppDialog.Warning(this, "校验失败", "请输入有效邮箱。");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            AppDialog.Warning(this, "校验失败", "密码不能为空。");
            return;
        }

        var account = new AccountRecord
        {
            Id = _editingId,
            Email = email,
            Password = password,
            TwoFa = TotpService.NormalizeSecretForStorage(GetTwoFaText()),
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
            AppDialog.Error(this, "保存失败", ex.Message);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = GetSelectedAccount()?.Id ?? _editingId;
        if (selectedId == 0)
        {
            AppDialog.Info(this, "提示", "请先选择账号。");
            return;
        }

        if (!AppDialog.Confirm(this, "删除", "确定删除选中的账号？")) return;

        try
        {
            _database.Delete(selectedId);
            LoadData();
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "删除失败", ex.Message);
        }
    }

    private void SelectAllVisible_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleAccounts.Count == 0)
        {
            AppDialog.Info(this, "全选", "当前列表没有账号。");
            return;
        }

        _syncingSelection = true;
        AccountsGrid.SelectedItems.Clear();
        CompactAccountsList.SelectedItems.Clear();
        foreach (var account in _visibleAccounts)
        {
            AccountsGrid.SelectedItems.Add(account);
            CompactAccountsList.SelectedItems.Add(account);
        }
        _syncingSelection = false;

        SelectAccount(_visibleAccounts[0]);
        StatusText.Text = $"已全选当前列表 {_visibleAccounts.Count} 个账号";
    }

    private void BatchCategoryTagsSelected_Click(object sender, RoutedEventArgs e)
    {
        ApplyBatchCategoryTags(GetSelectedAccounts(), "选中账号");
    }

    private void BatchCategoryTagsVisible_Click(object sender, RoutedEventArgs e)
    {
        ApplyBatchCategoryTags(_visibleAccounts.ToList(), "当前筛选结果");
    }

    private void BatchCategoryTagsAll_Click(object sender, RoutedEventArgs e)
    {
        ApplyBatchCategoryTags(_allAccounts.ToList(), "全部账号");
    }

    private List<AccountRecord> GetSelectedAccounts()
    {
        var selected = AccountsGrid.SelectedItems
            .OfType<AccountRecord>()
            .Concat(CompactAccountsList.SelectedItems.OfType<AccountRecord>())
            .GroupBy(account => account.Id)
            .Select(group => group.First())
            .ToList();

        if (selected.Count == 0 && GetSelectedAccount() is { } account)
        {
            selected.Add(account);
        }

        return selected;
    }

    private void ApplyBatchCategoryTags(IReadOnlyCollection<AccountRecord> accounts, string scopeName)
    {
        if (accounts.Count == 0)
        {
            AppDialog.Info(this, "批量分类/标签", $"{scopeName}为空。");
            return;
        }

        var window = new BatchCategoryTagWindow(scopeName, accounts.Count) { Owner = this };
        if (window.ShowDialog() != true) return;

        var category = window.Category;
        var tags = AccountDatabase.NormalizeTagsForStorage(window.Tags);
        var details = new StringBuilder()
            .AppendLine($"范围：{scopeName}")
            .AppendLine($"账号数：{accounts.Count}")
            .AppendLine($"分类：{(string.IsNullOrWhiteSpace(category) ? "不修改" : category)}")
            .AppendLine($"追加标签：{(string.IsNullOrWhiteSpace(tags) ? "不修改" : tags)}")
            .ToString();

        if (!AppDialog.Confirm(this, "批量分类/标签", "确认应用到这些账号？", details, AppDialogKind.Info)) return;

        try
        {
            foreach (var account in accounts)
            {
                if (!string.IsNullOrWhiteSpace(category))
                {
                    account.Category = category;
                }

                if (!string.IsNullOrWhiteSpace(tags))
                {
                    account.Tags = MergeTags(account.Tags, tags);
                }

                _database.Update(account);
            }

            LoadData();
            StatusText.Text = $"已更新 {accounts.Count} 个账号";
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "批量分类/标签失败", ex.Message);
        }
    }

    private static string MergeTags(string existingTags, string newTags)
    {
        return string.Join(", ",
            AccountDatabase.SplitTags(existingTags)
                .Concat(AccountDatabase.SplitTags(newTags))
                .Distinct(StringComparer.OrdinalIgnoreCase));
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
            AppDialog.Warning(this,
                "导入提示",
                "部分文件读取失败。",
                string.Join("\n", errors));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            AppDialog.Warning(this, "导入", "没有内容。");
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
        var window = new ImportTextWindow(
            initialText,
            sourceDescription: sourceDescription,
            existingEmails: _allAccounts.Select(a => a.Email),
            existingCategories: _database.GetCategories(),
            existingTags: _database.GetTags())
        { Owner = this };
        if (window.ShowDialog() != true) return;

        ImportParsedText(window.ImportText, window.DefaultCategory, window.DefaultTags, window.DuplicateMode);
    }

    private void OpenQuickImportWindow(string initialText)
    {
        var window = new QuickImportWindow(
            initialText,
            existingEmails: _allAccounts.Select(a => a.Email),
            existingCategories: _database.GetCategories(),
            existingTags: _database.GetTags())
        { Owner = this };
        if (window.ShowDialog() != true) return;

        ImportParsedText(window.ImportText, window.DefaultCategory, window.DefaultTags, window.DuplicateMode);
    }

    private void ImportParsedText(string text, string defaultCategory, string defaultTags, DuplicateMode duplicateMode)
    {
        var parsed = AccountImportExport.ParsePlainText(text, defaultCategory, defaultTags);
        if (parsed.Accounts.Count == 0)
        {
            AppDialog.Warning(this, "导入", "没有可导入的账号。", BuildImportResultMessage(parsed.TotalLines, 0, 0, 0, 0, parsed.Errors));
            return;
        }

        try
        {
            var result = _database.ImportAccounts(parsed.Accounts, duplicateMode);
            result.TotalLines = parsed.TotalLines;
            result.Errors.AddRange(parsed.Errors);
            LoadData();
            var details = BuildImportResultMessage(result.TotalLines, result.Parsed, result.Inserted, result.Updated, result.SkippedDuplicates, result.Errors);
            if (result.Errors.Count > 0)
            {
                AppDialog.Warning(this, "导入完成", "导入已完成，但存在部分错误。", details);
            }
            else
            {
                AppDialog.Success(this, "导入完成", $"新增 {result.Inserted}，覆盖 {result.Updated}，跳过 {result.SkippedDuplicates}。", details);
            }
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "导入失败", ex.Message);
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
            foreach (var error in errors) sb.AppendLine(error);
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
            AppDialog.Success(this, "导出完成", "文件已导出。", dialog.FileName);
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "导出失败", ex.Message);
        }
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var backupDirectory = EnsureBackupDirectory();
        if (string.IsNullOrWhiteSpace(backupDirectory)) return;

        var backupPath = Path.Combine(backupDirectory, AccountImportExport.CreateBackupFileName());
        try
        {
            Directory.CreateDirectory(backupDirectory);
            _database.CreateDatabaseBackup(backupPath);
            AppDialog.Success(this, "备份完成", "完整数据库备份已创建。", backupPath);
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "备份失败", ex.Message);
        }
    }

    private string? EnsureBackupDirectory()
    {
        var configured = _database.GetSetting(AppSettingKeys.BackupDirectory);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        Directory.CreateDirectory(AppPaths.SuggestedBackupDirectory);
        var dialog = new OpenFolderDialog
        {
            Title = "选择默认备份目录",
            InitialDirectory = AppPaths.SuggestedBackupDirectory
        };

        if (dialog.ShowDialog(this) != true) return null;

        _database.SetSettings(new Dictionary<string, string>
        {
            [AppSettingKeys.BackupDirectory] = dialog.FolderName
        });
        return dialog.FolderName;
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择备份",
            Filter = "AccountManager backup (*.db;*.ambak)|*.db;*.ambak|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        RestoreBackupFromPath(dialog.FileName);
    }

    private void RestoreBackupFromPath(string backupPath)
    {
        if (!AppDialog.Confirm(this, "导入备份", "将替换当前数据库并重启。继续？", backupPath)) return;

        try
        {
            _apiServer.Stop();
            _database.RestoreDatabaseFromBackup(backupPath);
            AppDialog.Success(this, "导入备份", "已导入，程序将重启。");
            RestartApplication();
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "恢复失败", ex.Message);
        }
    }

    private void SecuritySettings_Click(object sender, RoutedEventArgs e)
    {
        var window = CreateSettingsWindow("security");
        window.ShowDialog();
        RefreshRuntimeSettings();
    }

    private void ApiInfo_Click(object sender, RoutedEventArgs e)
    {
        var window = CreateSettingsWindow("api");
        window.ShowDialog();
        RefreshRuntimeSettings();
    }

    private void BackupHistory_Click(object sender, RoutedEventArgs e)
    {
        ShowBackupHistory();
    }

    private void DatabaseCheck_Click(object sender, RoutedEventArgs e)
    {
        CheckDatabase();
    }

    private void ShowBackupHistory()
    {
        var backupDirectory = EnsureBackupDirectory();
        if (string.IsNullOrWhiteSpace(backupDirectory)) return;

        var window = new BackupHistoryWindow(backupDirectory) { Owner = this };
        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.RestoreBackupPath))
        {
            RestoreBackupFromPath(window.RestoreBackupPath);
        }
    }

    private void CheckDatabase()
    {
        try
        {
            var health = _database.CheckHealth();
            var backupDirectory = _database.GetSetting(AppSettingKeys.BackupDirectory) ?? string.Empty;
            var backupCount = Directory.Exists(backupDirectory)
                ? Directory.EnumerateFiles(backupDirectory, "AccountManager_Backup_*.db").Count()
                : 0;

            var details = new StringBuilder()
                .AppendLine($"数据库：{health.DatabasePath}")
                .AppendLine($"文件存在：{(health.DatabaseExists ? "是" : "否")}")
                .AppendLine($"文件大小：{health.DatabaseSizeBytes:N0} 字节")
                .AppendLine($"SQLite quick_check：{health.QuickCheck}")
                .AppendLine($"表：{string.Join(", ", health.Tables)}")
                .AppendLine($"安全配置：{(health.SecurityConfigured ? "正常" : "缺失")}")
                .AppendLine($"设置数：{health.SettingsCount}")
                .AppendLine($"账号数：{health.AccountCount}")
                .AppendLine($"可解密账号数：{health.DecryptedAccountCount}")
                .AppendLine($"分类数：{health.CategoryCount}")
                .AppendLine($"标签数：{health.TagCount}")
                .AppendLine($"最后更新：{health.LastAccountUpdate}")
                .AppendLine($"备份目录：{(string.IsNullOrWhiteSpace(backupDirectory) ? "未设置" : backupDirectory)}")
                .AppendLine($"备份数量：{backupCount}");

            if (!string.IsNullOrWhiteSpace(health.Error))
            {
                details.AppendLine().AppendLine($"错误：{health.Error}");
            }

            if (health.Ok)
            {
                AppDialog.Success(this, "数据库检查", "数据库检查通过。", details.ToString());
            }
            else
            {
                AppDialog.Warning(this, "数据库检查", "数据库检查发现问题。", details.ToString());
            }
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "数据库检查失败", ex.Message);
        }
    }

    private SettingsWindow CreateSettingsWindow(string tab)
    {
        return new SettingsWindow(
            _database,
            _security,
            _apiServer,
            tab,
            RefreshRuntimeSettings,
            ShowBackupHistory,
            CheckDatabase)
        { Owner = this };
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        SoftLock();
    }

    private void SoftLock()
    {
        if (_isSoftLocked) return;
        _isSoftLocked = true;
        Hide();

        var unlock = new UnlockWindow(_security);
        var unlocked = unlock.ShowDialog() == true;
        if (unlocked)
        {
            _isSoftLocked = false;
            Show();
            Activate();
            MarkActivity();
            SearchBox.Focus();
            return;
        }

        System.Windows.Application.Current.Shutdown();
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

    private void EmailBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            BeginEmailEdit();
            e.Handled = true;
            return;
        }

        if (!_editingEmail)
        {
            CopyText(EmailBox.Text, "邮箱已复制");
            e.Handled = true;
        }
    }

    private void BeginEmailEdit()
    {
        _editingEmail = true;
        EmailBox.IsReadOnly = false;
        EmailBox.Focus();
        EmailBox.SelectAll();
        StatusText.Text = "正在编辑邮箱";
    }

    private void EmailBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_editingEmail) CommitEmailEdit();
    }

    private void CommitEmailEdit()
    {
        if (_editingId == 0)
        {
            _editingEmail = true;
            EmailBox.IsReadOnly = false;
            return;
        }

        _editingEmail = false;
        EmailBox.IsReadOnly = true;
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

    private void EditorBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_editingEmail) CommitEmailEdit();
            CommitSecretEdits();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _editingEmail = false;
            EmailBox.IsReadOnly = true;
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
        var original = TwoFaCodeBox.Text.Trim();
        _twoFaSecret = TotpService.NormalizeSecretForStorage(original);
        if (!string.Equals(original, _twoFaSecret, StringComparison.Ordinal))
        {
            StatusText.Text = "2FA密钥已规范化";
        }
        _editingTwoFa = false;
        RefreshTotpDisplay();
    }

    private AccountRecord? GetSelectedAccount()
    {
        return (_compactMode ? CompactAccountsList.SelectedItem : AccountsGrid.SelectedItem) as AccountRecord
               ?? AccountsGrid.SelectedItem as AccountRecord
               ?? CompactAccountsList.SelectedItem as AccountRecord;
    }

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
