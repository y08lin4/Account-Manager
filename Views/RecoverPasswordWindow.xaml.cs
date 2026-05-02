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
            AppDialog.Warning(this, "校验失败", "两次输入的新主密码不一致。");
            return;
        }

        try
        {
            if (!_security.TryRecoverAndResetPassword(AnswerBox.Password, NewPasswordBox.Password, HintBox.Text))
            {
                AppDialog.Warning(this, "恢复失败", "保护答案不正确。");
                return;
            }

            AppDialog.Success(this, "恢复成功", "主密码已重置，数据库已解锁。");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppDialog.Warning(this, "恢复失败", ex.Message);
        }
    }
}
