using AccountManager.Services;
using System.Windows;

namespace AccountManager;

public partial class PasswordConfirmWindow : Window
{
    private readonly SecurityService _security;

    public PasswordConfirmWindow(SecurityService security, string purpose)
    {
        InitializeComponent();
        _security = security;
        PurposeText.Text = purpose;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_security.TryUnlock(PasswordBox.Password))
        {
            DialogResult = true;
            return;
        }

        PasswordBox.Clear();
        ErrorText.Text = "密码错误";
        PasswordBox.Focus();
    }
}
