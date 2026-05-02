using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AccountManager;

public partial class SecuritySetupWindow : Window
{
    private const int PasswordLength = 6;
    private const string CustomQuestionOption = "自定义问题";

    private static readonly string[] PresetQuestions =
    [
        "我的第一只宠物叫什么？",
        "我最喜欢的一本书是什么？",
        "我的第一所学校叫什么？",
        "我第一次旅行去过哪里？",
        "我的童年昵称是什么？",
        "我的备用提示词是什么？",
        CustomQuestionOption
    ];

    private readonly bool _isChangeMode;
    private readonly string _existingHint;
    private readonly string _existingQuestion;
    private SetupStep _step = SetupStep.MasterPassword;
    private bool _showPassword;
    private bool _updatingPassword;
    private bool _advancing;
    private string _masterPassword = string.Empty;
    private string _passwordHint = string.Empty;
    private string _recoveryQuestion = string.Empty;
    private string _recoveryAnswer = string.Empty;

    public string MasterPassword => _masterPassword;
    public string PasswordHint => _passwordHint;
    public string RecoveryQuestion => _recoveryQuestion;
    public string RecoveryAnswer => _recoveryAnswer;

    public SecuritySetupWindow(bool isChangeMode = false, string existingHint = "", string existingQuestion = "")
    {
        InitializeComponent();

        _isChangeMode = isChangeMode;
        _existingHint = existingHint;
        _existingQuestion = existingQuestion;
        _passwordHint = existingHint;
        _recoveryQuestion = existingQuestion;

        Title = isChangeMode ? "修改安全保护" : "设置安全保护";
        QuestionComboBox.ItemsSource = PresetQuestions;
        HintBox.Text = existingHint;
        PrepareQuestionSelection(existingQuestion);

        Loaded += (_, _) => FocusCurrentInput();
        SetStep(SetupStep.MasterPassword);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        Advance();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == SetupStep.MasterPassword) return;
        SetStep((SetupStep)((int)_step - 1));
    }

    private void HiddenPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Advance();
            e.Handled = true;
        }
    }

    private void TextInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Advance();
            e.Handled = true;
        }
    }

    private void HiddenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        UpdateDots();
        UpdateVisiblePassword();

        if (_updatingPassword || _advancing) return;
        if ((_step == SetupStep.MasterPassword || _step == SetupStep.ConfirmPassword) && HiddenPasswordBox.Password.Length == PasswordLength)
        {
            Advance();
        }
    }

    private void ToggleVisible_Click(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;
        UpdateVisiblePassword();
        FocusCurrentInput();
    }

    private void QuestionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuestionComboBox.SelectedItem?.ToString() == CustomQuestionOption)
        {
            CustomQuestionBox.Visibility = Visibility.Visible;
            CustomQuestionBox.Focus();
        }
        else
        {
            CustomQuestionBox.Visibility = Visibility.Collapsed;
        }

        ErrorText.Text = string.Empty;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        FocusCurrentInput();
    }

    private void Advance()
    {
        if (_advancing) return;
        _advancing = true;

        try
        {
            ErrorText.Text = string.Empty;

            switch (_step)
            {
                case SetupStep.MasterPassword:
                    if (!ValidatePasswordInput(HiddenPasswordBox.Password)) return;
                    _masterPassword = HiddenPasswordBox.Password;
                    SetStep(SetupStep.ConfirmPassword);
                    break;

                case SetupStep.ConfirmPassword:
                    if (!ValidatePasswordInput(HiddenPasswordBox.Password)) return;
                    if (!string.Equals(_masterPassword, HiddenPasswordBox.Password, StringComparison.Ordinal))
                    {
                        ErrorText.Text = "两次主密码不一致";
                        SetPasswordValue(string.Empty);
                        HiddenPasswordBox.Focus();
                        return;
                    }
                    SetStep(SetupStep.Hint);
                    break;

                case SetupStep.Hint:
                    _passwordHint = HintBox.Text.Trim();
                    SetStep(SetupStep.Question);
                    break;

                case SetupStep.Question:
                    var question = GetSelectedQuestion();
                    if (string.IsNullOrWhiteSpace(question))
                    {
                        ErrorText.Text = "请选择或填写保护问题";
                        FocusCurrentInput();
                        return;
                    }
                    _recoveryQuestion = question.Trim();
                    SetStep(SetupStep.Answer);
                    break;

                case SetupStep.Answer:
                    var answer = AnswerInputBox.Password.Trim();
                    if (answer.Length < 2)
                    {
                        ErrorText.Text = "保护答案至少需要 2 个字符";
                        AnswerInputBox.Focus();
                        return;
                    }
                    _recoveryAnswer = answer;
                    SetStep(SetupStep.ConfirmAnswer);
                    break;

                case SetupStep.ConfirmAnswer:
                    var confirmAnswer = AnswerInputBox.Password.Trim();
                    if (!string.Equals(_recoveryAnswer, confirmAnswer, StringComparison.OrdinalIgnoreCase))
                    {
                        ErrorText.Text = "两次保护答案不一致";
                        AnswerInputBox.Clear();
                        AnswerInputBox.Focus();
                        return;
                    }
                    DialogResult = true;
                    break;
            }
        }
        finally
        {
            _advancing = false;
        }
    }

    private bool ValidatePasswordInput(string password)
    {
        if (password.Length == PasswordLength) return true;

        ErrorText.Text = $"请输入 {PasswordLength} 位主密码";
        HiddenPasswordBox.Focus();
        return false;
    }

    private void SetStep(SetupStep step)
    {
        _step = step;
        ErrorText.Text = string.Empty;

        PasswordPanel.Visibility = step is SetupStep.MasterPassword or SetupStep.ConfirmPassword ? Visibility.Visible : Visibility.Collapsed;
        HintPanel.Visibility = step == SetupStep.Hint ? Visibility.Visible : Visibility.Collapsed;
        QuestionPanel.Visibility = step == SetupStep.Question ? Visibility.Visible : Visibility.Collapsed;
        AnswerPanel.Visibility = step is SetupStep.Answer or SetupStep.ConfirmAnswer ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = step != SetupStep.MasterPassword;
        StepCountText.Text = $"{(int)step + 1} / 6";
        NextButton.Content = step == SetupStep.ConfirmAnswer ? "保存" : "下一步";

        switch (step)
        {
            case SetupStep.MasterPassword:
                TitleText.Text = _isChangeMode ? "设置新主密码" : "设置主密码";
                DescriptionText.Text = "输入 6 位主密码，满 6 位自动进入下一步。";
                SetPasswordValue(_masterPassword);
                break;

            case SetupStep.ConfirmPassword:
                TitleText.Text = "确认主密码";
                DescriptionText.Text = "再次输入 6 位主密码。";
                SetPasswordValue(string.Empty);
                break;

            case SetupStep.Hint:
                TitleText.Text = "设置提示词";
                DescriptionText.Text = "解锁时会显示提示词，用来提醒自己。";
                HintBox.Text = string.IsNullOrEmpty(_passwordHint) ? _existingHint : _passwordHint;
                break;

            case SetupStep.Question:
                TitleText.Text = "设置保护问题";
                DescriptionText.Text = "忘记主密码时，通过保护问题重置。";
                PrepareQuestionSelection(string.IsNullOrEmpty(_recoveryQuestion) ? _existingQuestion : _recoveryQuestion);
                break;

            case SetupStep.Answer:
                TitleText.Text = "设置保护答案";
                DescriptionText.Text = "答案会加密保存，验证时忽略大小写。";
                AnswerTitleText.Text = "保护答案";
                AnswerTipText.Text = "用于重置主密码，至少 2 个字符。";
                AnswerInputBox.Clear();
                break;

            case SetupStep.ConfirmAnswer:
                TitleText.Text = "确认保护答案";
                DescriptionText.Text = "再次输入保护答案。";
                AnswerTitleText.Text = "确认答案";
                AnswerTipText.Text = "保护答案验证时忽略大小写。";
                AnswerInputBox.Clear();
                break;
        }

        FocusCurrentInput();
    }

    private void SetPasswordValue(string value)
    {
        _updatingPassword = true;
        HiddenPasswordBox.Password = value;
        _updatingPassword = false;
        UpdateDots();
        UpdateVisiblePassword();
    }

    private void UpdateDots()
    {
        var length = HiddenPasswordBox.Password.Length;
        var dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5, Dot6 };
        var filledBrush = (Brush)(TryFindResource("TextBrush") ?? Brushes.Black);
        var emptyBrush = (Brush)(TryFindResource("BorderBrushSoft") ?? Brushes.Gray);

        for (var i = 0; i < dots.Length; i++)
        {
            dots[i].Fill = i < length ? filledBrush : Brushes.Transparent;
            dots[i].Stroke = i < length ? filledBrush : emptyBrush;
        }
    }

    private void UpdateVisiblePassword()
    {
        DotsPanel.Visibility = _showPassword ? Visibility.Collapsed : Visibility.Visible;
        VisiblePasswordText.Visibility = _showPassword ? Visibility.Visible : Visibility.Collapsed;
        VisiblePasswordText.Text = HiddenPasswordBox.Password;
        ToggleVisibleButton.Opacity = _showPassword ? 1.0 : 0.72;
    }

    private void PrepareQuestionSelection(string question)
    {
        var matched = PresetQuestions.FirstOrDefault(q => q != CustomQuestionOption && string.Equals(q, question, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(matched))
        {
            QuestionComboBox.SelectedItem = matched;
            CustomQuestionBox.Text = string.Empty;
            CustomQuestionBox.Visibility = Visibility.Collapsed;
            return;
        }

        if (!string.IsNullOrWhiteSpace(question))
        {
            QuestionComboBox.SelectedItem = CustomQuestionOption;
            CustomQuestionBox.Text = question;
            CustomQuestionBox.Visibility = Visibility.Visible;
            return;
        }

        QuestionComboBox.SelectedIndex = 0;
        CustomQuestionBox.Text = string.Empty;
        CustomQuestionBox.Visibility = Visibility.Collapsed;
    }

    private string GetSelectedQuestion()
    {
        return QuestionComboBox.SelectedItem?.ToString() == CustomQuestionOption
            ? CustomQuestionBox.Text
            : QuestionComboBox.SelectedItem?.ToString() ?? string.Empty;
    }

    private void FocusCurrentInput()
    {
        switch (_step)
        {
            case SetupStep.MasterPassword:
            case SetupStep.ConfirmPassword:
                HiddenPasswordBox.Focus();
                break;
            case SetupStep.Hint:
                HintBox.Focus();
                HintBox.CaretIndex = HintBox.Text.Length;
                break;
            case SetupStep.Question:
                if (CustomQuestionBox.Visibility == Visibility.Visible)
                {
                    CustomQuestionBox.Focus();
                    CustomQuestionBox.CaretIndex = CustomQuestionBox.Text.Length;
                }
                else
                {
                    QuestionComboBox.Focus();
                }
                break;
            case SetupStep.Answer:
            case SetupStep.ConfirmAnswer:
                AnswerInputBox.Focus();
                break;
        }
    }

    private enum SetupStep
    {
        MasterPassword,
        ConfirmPassword,
        Hint,
        Question,
        Answer,
        ConfirmAnswer
    }
}
