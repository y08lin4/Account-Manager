using AccountManager.Services;
using System.Windows;
using System.Windows.Input;

namespace AccountManager;

public partial class UnlockWindow : Window
{
    private readonly SecurityService _security;
    private bool _showPassword;
    private bool _syncingPassword;

    public UnlockWindow(SecurityService security)
    {
        InitializeComponent();
        _security = security;
        HintText.Text = string.IsNullOrWhiteSpace(security.PasswordHint) ? "提示词：未设置" : $"提示词：{security.PasswordHint}";
        Loaded += (_, _) =>
        {
            PasswordBox.Focus();
            UpdateCapsLockState();
        };
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        TryUnlock();
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryUnlock();
    }

    private void PasswordInput_KeyUp(object sender, KeyEventArgs e)
    {
        UpdateCapsLockState();
    }

    private void PasswordInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateCapsLockState();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword || _showPassword) return;
        _syncingPassword = true;
        PasswordTextBox.Text = PasswordBox.Password;
        _syncingPassword = false;
        ErrorText.Text = string.Empty;
    }

    private void PasswordTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_syncingPassword || !_showPassword) return;
        _syncingPassword = true;
        PasswordBox.Password = PasswordTextBox.Text;
        _syncingPassword = false;
        ErrorText.Text = string.Empty;
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;
        _syncingPassword = true;
        if (_showPassword)
        {
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordTextBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            TogglePasswordButton.Content = "隐藏";
            PasswordTextBox.Focus();
            PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordTextBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            TogglePasswordButton.Content = "显示";
            PasswordBox.Focus();
        }
        _syncingPassword = false;
    }

    private void TryUnlock()
    {
        var password = _showPassword ? PasswordTextBox.Text : PasswordBox.Password;
        if (_security.TryUnlock(password))
        {
            DialogResult = true;
            return;
        }

        PasswordBox.Clear();
        PasswordTextBox.Clear();
        ErrorText.Text = "主密码错误";
        if (_showPassword) PasswordTextBox.Focus();
        else PasswordBox.Focus();
    }

    private void UpdateCapsLockState()
    {
        CapsLockText.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;
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
