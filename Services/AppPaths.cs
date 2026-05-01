using System.IO;
namespace AccountManager.Services;

public static class AppPaths
{
#if INSTALLED
    public const bool IsPortable = false;
    public const string BuildMode = "安装版";
#else
    public const bool IsPortable = true;
    public const string BuildMode = "绿色便携版";
#endif

    public static string DataDirectory => IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AccountManager");

    public static string DatabasePath => Path.Combine(DataDirectory, "accounts.db");

    public static string ApiTokenPath => Path.Combine(DataDirectory, "api-token.txt");

    public static string SuggestedBackupDirectory
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "AccountManager Backups");
        }
    }
}

