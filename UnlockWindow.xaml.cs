using AccountManager.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AccountManager;

public partial class UnlockWindow : Window
{
    private const int UnlockLength = 6;
    private readonly SecurityService _security;
    private bool _checking;
    private bool _showPassword;

    public UnlockWindow(SecurityService security)
    {
        InitializeComponent();
        _security = security;
        HintText.Text = string.IsNullOrWhiteSpace(security.PasswordHint) ? "提示词：未设置" : $"提示词：{security.PasswordHint}";
        Loaded += (_, _) => HiddenPasswordBox.Focus();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        TryUnlock();
    }

    private void HiddenPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryUnlock();
    }

    private void HiddenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        UpdateDots();
        UpdateVisiblePassword();

        if (!_checking && HiddenPasswordBox.Password.Length == UnlockLength)
        {
            TryUnlock();
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HiddenPasswordBox.Focus();
    }

    private void ToggleVisible_Click(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;
        UpdateVisiblePassword();
        HiddenPasswordBox.Focus();
    }

    private void TryUnlock()
    {
        if (_checking) return;
        _checking = true;

        if (_security.TryUnlock(HiddenPasswordBox.Password))
        {
            DialogResult = true;
            return;
        }

        HiddenPasswordBox.Clear();
        UpdateDots();
        ErrorText.Text = "密码错误";
        HiddenPasswordBox.Focus();
        _checking = false;
    }

    private void UpdateDots()
    {
        var length = HiddenPasswordBox.Password.Length;
        var dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5, Dot6 };
        for (var i = 0; i < dots.Length; i++)
        {
            dots[i].Fill = i < length ? Brushes.Black : Brushes.Transparent;
            dots[i].Stroke = i < length ? Brushes.Black : Brushes.Gray;
        }
    }

    private void UpdateVisiblePassword()
    {
        DotsPanel.Visibility = _showPassword ? Visibility.Collapsed : Visibility.Visible;
        VisiblePasswordText.Visibility = _showPassword ? Visibility.Visible : Visibility.Collapsed;
        VisiblePasswordText.Text = HiddenPasswordBox.Password;
        ToggleVisibleButton.Opacity = _showPassword ? 1.0 : 0.72;
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
