using AccountManager.Services;
using System.Windows;
using System.Windows.Media;

namespace AccountManager;

public partial class AppDialogWindow : Window
{
    private readonly bool _confirmMode;

    public AppDialogWindow(string title, string message, string details = "", AppDialogKind kind = AppDialogKind.Info, bool confirmMode = false)
    {
        InitializeComponent();
        _confirmMode = confirmMode;
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        AccentBar.Background = new SolidColorBrush(AccentColor(kind));

        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsBox.Text = details;
            DetailsBox.Visibility = Visibility.Visible;
            CopyPanel.Visibility = Visibility.Visible;
        }

        if (confirmMode)
        {
            CancelButton.Visibility = Visibility.Visible;
            OkButton.Content = "继续";
        }
    }

    private static Color AccentColor(AppDialogKind kind)
    {
        return kind switch
        {
            AppDialogKind.Success => Color.FromRgb(36, 122, 82),
            AppDialogKind.Warning => Color.FromRgb(177, 113, 22),
            AppDialogKind.Error => Color.FromRgb(184, 48, 64),
            _ => Color.FromRgb(30, 90, 150)
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmMode) DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if (await NativeClipboardService.SetTextAsync(DetailsBox.Text))
        {
            OkButton.Focus();
        }
    }
}

public enum AppDialogKind
{
    Info,
    Success,
    Warning,
    Error
}

public static class AppDialog
{
    public static void Info(Window? owner, string title, string message, string details = "")
    {
        Show(owner, title, message, details, AppDialogKind.Info);
    }

    public static void Success(Window? owner, string title, string message, string details = "")
    {
        Show(owner, title, message, details, AppDialogKind.Success);
    }

    public static void Warning(Window? owner, string title, string message, string details = "")
    {
        Show(owner, title, message, details, AppDialogKind.Warning);
    }

    public static void Error(Window? owner, string title, string message, string details = "")
    {
        Show(owner, title, message, details, AppDialogKind.Error);
    }

    public static bool Confirm(Window? owner, string title, string message, string details = "", AppDialogKind kind = AppDialogKind.Warning)
    {
        var dialog = new AppDialogWindow(title, message, details, kind, confirmMode: true);
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    private static void Show(Window? owner, string title, string message, string details, AppDialogKind kind)
    {
        var dialog = new AppDialogWindow(title, message, details, kind);
        if (owner is not null) dialog.Owner = owner;
        dialog.ShowDialog();
    }
}
