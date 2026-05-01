using AccountManager.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AccountManager;

public partial class SettingsWindow : Window
{
    private readonly AccountDatabase _database;
    private readonly SecurityService _security;
    private readonly LocalApiServer _apiServer;
    private string _visibleToken = string.Empty;

    public SettingsWindow(AccountDatabase database, SecurityService security, LocalApiServer apiServer, string initialTab = "security")
    {
        InitializeComponent();
        _database = database;
        _security = security;
        _apiServer = apiServer;

        ApiAddressBox.Text = _apiServer.BaseUrl;
        ApiTokenFileBox.Text = _apiServer.TokenFilePath;
        RefreshApiTokenDisplay();
        RefreshBackupDirectoryDisplay();
        SelectInitialTab(initialTab);
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
        ApiStatusText.Text = _apiServer.IsRunning
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

    private void ChangeSecurity_Click(object sender, RoutedEventArgs e)
    {
        var window = new SecuritySetupWindow(true, _security.PasswordHint, _security.RecoveryQuestion) { Owner = this };
        if (window.ShowDialog() != true) return;

        try
        {
            _security.ChangeSecurity(window.MasterPassword, window.PasswordHint, window.RecoveryQuestion, window.RecoveryAnswer);
            MessageBox.Show(this, "安全设置已更新。", "设置", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RevealToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_apiServer.GetToken()))
        {
            MessageBox.Show(this, "还没有创建 API Token。", "API", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(this, "还没有创建 API Token。", "API", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ConfirmPassword("复制 API Token")) return;

        if (await NativeClipboardService.SetTextAsync(token))
        {
            MessageBox.Show(this, "Token 已复制。", "API", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, "剪贴板被占用，请稍后重试。", "API", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NewToken_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "新建 Token 后，旧 Token 会立即失效。继续？", "新建 Token", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!ConfirmPassword("新建 API Token")) return;

        _visibleToken = _apiServer.RegenerateToken();
        RefreshApiTokenDisplay();
        MessageBox.Show(this, "新 Token 已创建，旧 Token 已失效。", "API", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_apiServer.GetToken()))
        {
            MessageBox.Show(this, "当前没有 API Token。", "API", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, "删除后 API 认证接口将不可用，直到重新新建 Token。继续？", "删除 Token", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!ConfirmPassword("删除 API Token")) return;

        _apiServer.DeleteToken();
        _visibleToken = string.Empty;
        RefreshApiTokenDisplay();
        MessageBox.Show(this, "Token 已删除。", "API", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(this, "备份目录不存在，请先修改。", "路径", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
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
