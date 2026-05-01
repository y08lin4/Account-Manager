using AccountManager.Services;
using System.Windows;

namespace AccountManager;

public partial class RecoverPasswordWindow : Window
{
    private readonly SecurityService _security;

    public RecoverPasswordWindow(SecurityService security)
    {
        InitializeComponent();
        _security = security;
        QuestionText.Text = string.IsNullOrWhiteSpace(security.RecoveryQuestion) ? "未设置保护问题" : security.RecoveryQuestion;
        HintBox.Text = security.PasswordHint;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            MessageBox.Show(this, "两次输入的新主密码不一致。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (!_security.TryRecoverAndResetPassword(AnswerBox.Password, NewPasswordBox.Password, HintBox.Text))
            {
                MessageBox.Show(this, "保护答案不正确。", "恢复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(this, "主密码已重置，数据库已解锁。", "恢复成功", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "恢复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
