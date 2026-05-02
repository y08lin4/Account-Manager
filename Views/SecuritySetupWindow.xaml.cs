using System.Windows;

namespace AccountManager;

public partial class SecuritySetupWindow : Window
{
    public string MasterPassword => PasswordBox.Password;
    public string PasswordHint => HintBox.Text;
    public string RecoveryQuestion => QuestionBox.Text;
    public string RecoveryAnswer => AnswerBox.Password;

    public SecuritySetupWindow(bool isChangeMode = false, string existingHint = "", string existingQuestion = "")
    {
        InitializeComponent();
        TitleText.Text = isChangeMode ? "修改主密码和保护问题" : "首次使用：设置主密码";
        Title = isChangeMode ? "修改安全保护" : "设置安全保护";
        HintBox.Text = existingHint;
        QuestionBox.Text = existingQuestion;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Password != ConfirmPasswordBox.Password)
        {
            AppDialog.Warning(this, "校验失败", "两次输入的主密码不一致。");
            return;
        }

        if (AnswerBox.Password != ConfirmAnswerBox.Password)
        {
            AppDialog.Warning(this, "校验失败", "两次输入的保护答案不一致。");
            return;
        }

        if (string.IsNullOrWhiteSpace(HintBox.Text))
        {
            if (!AppDialog.Confirm(this, "提示", "没有设置提示词。解锁时将不会显示提示，确定继续吗？", kind: AppDialogKind.Info)) return;
        }

        DialogResult = true;
    }
}
