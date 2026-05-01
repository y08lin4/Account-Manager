using AccountManager.Services;
using System.Windows;
using System.Windows.Input;

namespace AccountManager;

public partial class UnlockWindow : Window
{
    private readonly SecurityService _security;

    public UnlockWindow(SecurityService security)
    {
        InitializeComponent();
        _security = security;
        ModeText.Text = $"{AppPaths.BuildMode} · 数据目录：{AppPaths.DataDirectory}";
        HintText.Text = string.IsNullOrWhiteSpace(security.PasswordHint) ? "未设置提示词" : security.PasswordHint;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        TryUnlock();
    }

    private void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryUnlock();
    }

    private void TryUnlock()
    {
        if (_security.TryUnlock(PasswordBox.Password))
        {
            DialogResult = true;
            return;
        }

        PasswordBox.Clear();
        ErrorText.Text = "主密码错误。";
        PasswordBox.Focus();
    }

    private void Recover_Click(object sender, RoutedEventArgs e)
    {
        var window = new RecoverPasswordWindow(_security) { Owner = this };
        if (window.ShowDialog() == true)
        {
            DialogResult = true;
        }
    }
}
