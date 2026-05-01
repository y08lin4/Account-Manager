using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AccountManager;

public partial class BackupHistoryWindow : Window
{
    private readonly string _backupDirectory;
    private readonly ObservableCollection<BackupFileItem> _backups = new();

    public BackupHistoryWindow(string backupDirectory)
    {
        InitializeComponent();
        _backupDirectory = backupDirectory;
        DirectoryText.Text = backupDirectory;
        BackupsGrid.ItemsSource = _backups;
        Loaded += (_, _) => LoadBackups();
    }

    public string RestoreBackupPath { get; private set; } = string.Empty;

    private void LoadBackups()
    {
        _backups.Clear();
        Directory.CreateDirectory(_backupDirectory);

        foreach (var path in Directory
                     .EnumerateFiles(_backupDirectory, "AccountManager_Backup_*.db")
                     .Concat(Directory.EnumerateFiles(_backupDirectory, "*.ambak"))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Select(p => new FileInfo(p))
                     .OrderByDescending(f => f.LastWriteTime))
        {
            _backups.Add(new BackupFileItem(path));
        }
    }

    private BackupFileItem? SelectedBackup => BackupsGrid.SelectedItem as BackupFileItem;

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadBackups();
    }

    private void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_backupDirectory);
        Process.Start(new ProcessStartInfo(_backupDirectory) { UseShellExecute = true });
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup is not { } backup)
        {
            MessageBox.Show(this, "请先选择备份。", "备份历史", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RestoreBackupPath = backup.FullName;
        DialogResult = true;
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup is not { } backup)
        {
            MessageBox.Show(this, "请先选择备份。", "备份历史", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"确定删除这个备份？\n{backup.Name}", "删除备份", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(backup.FullName);
            LoadBackups();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class BackupFileItem
    {
        public BackupFileItem(FileInfo file)
        {
            FullName = file.FullName;
            Name = file.Name;
            LastWriteTime = file.LastWriteTime;
            SizeText = FormatSize(file.Length);
        }

        public string FullName { get; }
        public string Name { get; }
        public DateTime LastWriteTime { get; }
        public string SizeText { get; }

        private static string FormatSize(long bytes)
        {
            return bytes >= 1024 * 1024
                ? $"{bytes / 1024d / 1024d:N1} MB"
                : $"{bytes / 1024d:N1} KB";
        }
    }
}
