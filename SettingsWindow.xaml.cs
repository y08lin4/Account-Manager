using AccountManager.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AccountManager;

public partial class SettingsWindow : Window
{
    private readonly AccountDatabase _database;
    private readonly SecurityService _security;
    private readonly LocalApiServer _apiServer;
    private readonly Action? _settingsChanged;
    private readonly Action? _showBackupHistory;
    private readonly Action? _checkDatabase;
    private string _visibleToken = string.Empty;
    private bool _loading;

    public SettingsWindow(
        AccountDatabase database,
        SecurityService security,
        LocalApiServer apiServer,
        string initialTab = "security",
        Action? settingsChanged = null,
        Action? showBackupHistory = null,
        Action? checkDatabase = null)
    {
        InitializeComponent();
        _loading = true;
        _database = database;
        _security = security;
        _apiServer = apiServer;
        _settingsChanged = settingsChanged;
        _showBackupHistory = showBackupHistory;
        _checkDatabase = checkDatabase;

        ApiAddressBox.Text = _apiServer.BaseUrl;
        ApiTokenFileBox.Text = _apiServer.TokenFilePath;
        ApiEnabledBox.IsChecked = IsApiEnabled();
        SelectAutoLockMinutes(ReadAutoLockMinutes());
        RefreshApiTokenDisplay();
        RefreshBackupDirectoryDisplay();
        SelectInitialTab(initialTab);
        _loading = false;
    }

    private void SelectInitialTab(string initialTab)
    {
        SettingsTabs.SelectedItem = initialTab.Trim().ToLowerInvariant() switch
        {
            "api" => ApiTab,
            "path" or "paths" => PathTab,
            _ => SecurityTab
        };
    }

    private void RefreshApiTokenDisplay()
    {
        var token = _apiServer.GetToken();
        ApiEnabledBox.IsChecked = IsApiEnabled();
        ApiStatusText.Text = !IsApiEnabled()
            ? "已关闭"
            : _apiServer.IsRunning
            ? (string.IsNullOrWhiteSpace(token) ? "已启动，未创建 Token" : "已启动")
            : $"未启动{(string.IsNullOrWhiteSpace(_apiServer.LastError) ? string.Empty : "：" + _apiServer.LastError)}";

        ApiTokenBox.Text = string.IsNullOrWhiteSpace(token)
            ? "未创建"
            : string.IsNullOrEmpty(_visibleToken)
                ? MaskToken(token)
                : _visibleToken;
    }

    private void RefreshBackupDirectoryDisplay()
    {
        BackupDirectoryBox.Text = GetBackupDirectory();
    }

    private string GetBackupDirectory()
    {
        return _database.GetSetting(AppSettingKeys.BackupDirectory) ?? string.Empty;
    }

    private bool IsApiEnabled()
    {
        return !string.Equals(_database.GetSetting(AppSettingKeys.ApiEnabled), "false", StringComparison.OrdinalIgnoreCase);
    }

    private int ReadAutoLockMinutes()
    {
        return int.TryParse(_database.GetSetting(AppSettingKeys.AutoLockMinutes), out var minutes) && minutes > 0 ? minutes : 0;
    }

    private void SelectAutoLockMinutes(int minutes)
    {
        foreach (ComboBoxItem item in AutoLockBox.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out var value) && value == minutes)
            {
                AutoLockBox.SelectedItem = item;
                return;
            }
        }

        AutoLockBox.SelectedIndex = 0;
    }

    private void ApiEnabledBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var enabled = ApiEnabledBox.IsChecked == true;
        _database.SetSettings(new Dictionary<string, string>
        {
            [AppSettingKeys.ApiEnabled] = enabled ? "true" : "false"
        });

        if (enabled) _apiServer.Start();
        else _apiServer.Stop();

        _visibleToken = string.Empty;
        RefreshApiTokenDisplay();
        _settingsChanged?.Invoke();
    }

    private void AutoLockBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AutoLockBox.SelectedItem is not ComboBoxItem item) return;

        var minutes = int.TryParse(item.Tag?.ToString(), out var value) ? value : 0;
        _database.SetSettings(new Dictionary<string, string>
        {
            [AppSettingKeys.AutoLockMinutes] = minutes.ToString()
        });
        _settingsChanged?.Invoke();
    }

    private void ChangeSecurity_Click(object sender, RoutedEventArgs e)
    {
        var window = new SecuritySetupWindow(true, _security.PasswordHint, _security.RecoveryQuestion) { Owner = this };
        if (window.ShowDialog() != true) return;

        try
        {
            _security.ChangeSecurity(window.MasterPassword, window.PasswordHint, window.RecoveryQuestion, window.RecoveryAnswer);
            AppDialog.Success(this, "设置", "安全设置已更新。");
        }
        catch (Exception ex)
        {
            AppDialog.Warning(this, "更新失败", ex.Message);
        }
    }

    private void RevealToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_apiServer.GetToken()))
        {
            AppDialog.Info(this, "API", "还没有创建 API Token。");
            return;
        }

        if (!ConfirmPassword("查看 API Token")) return;
        _visibleToken = _apiServer.GetToken();
        RefreshApiTokenDisplay();
    }

    private async void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        var token = _apiServer.GetToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            AppDialog.Info(this, "API", "还没有创建 API Token。");
            return;
        }

        if (!ConfirmPassword("复制 API Token")) return;

        if (await NativeClipboardService.SetTextAsync(token))
        {
            AppDialog.Success(this, "API", "Token 已复制。");
        }
        else
        {
            AppDialog.Warning(this, "API", "剪贴板被占用，请稍后重试。");
        }
    }

    private void NewToken_Click(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.Confirm(this, "新建 Token", "新建 Token 后，旧 Token 会立即失效。继续？"))
        {
            return;
        }

        if (!ConfirmPassword("新建 API Token")) return;

        _visibleToken = _apiServer.RegenerateToken();
        RefreshApiTokenDisplay();
        AppDialog.Success(this, "API", "新 Token 已创建，旧 Token 已失效。");
    }

    private void DeleteToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_apiServer.GetToken()))
        {
            AppDialog.Info(this, "API", "当前没有 API Token。");
            return;
        }

        if (!AppDialog.Confirm(this, "删除 Token", "删除后 API 认证接口将不可用，直到重新新建 Token。继续？"))
        {
            return;
        }

        if (!ConfirmPassword("删除 API Token")) return;

        _apiServer.DeleteToken();
        _visibleToken = string.Empty;
        RefreshApiTokenDisplay();
        AppDialog.Success(this, "API", "Token 已删除。");
    }

    private void ChooseBackupDirectory_Click(object sender, RoutedEventArgs e)
    {
        var current = GetBackupDirectory();
        var dialog = new OpenFolderDialog
        {
            Title = "选择备份目录",
            InitialDirectory = Directory.Exists(current) ? current : AppPaths.SuggestedBackupDirectory
        };

        if (dialog.ShowDialog(this) != true) return;

        _database.SetSettings(new Dictionary<string, string>
        {
            [AppSettingKeys.BackupDirectory] = dialog.FolderName
        });
        RefreshBackupDirectoryDisplay();
    }

    private void OpenBackupDirectory_Click(object sender, RoutedEventArgs e)
    {
        var directory = GetBackupDirectory();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            AppDialog.Info(this, "路径", "备份目录不存在，请先修改。");
            return;
        }

        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void BackupHistory_Click(object sender, RoutedEventArgs e)
    {
        _showBackupHistory?.Invoke();
    }

    private void DatabaseCheck_Click(object sender, RoutedEventArgs e)
    {
        _checkDatabase?.Invoke();
    }

    private bool ConfirmPassword(string purpose)
    {
        var window = new PasswordConfirmWindow(_security, $"请输入主密码以{purpose}") { Owner = this };
        return window.ShowDialog() == true;
    }

    private static string MaskToken(string token)
    {
        if (token.Length <= 10) return new string('●', token.Length);
        return $"{token[..5]}{new string('●', 18)}{token[^5..]}";
    }
}
