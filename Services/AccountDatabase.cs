using System.IO;
using AccountManager.Models;
using Microsoft.Data.Sqlite;

namespace AccountManager.Services;

public sealed class AccountDatabase
{
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public ISecretProtector? Protector { get; set; }

    public AccountDatabase()
    {
        DataDirectory = AppPaths.DataDirectory;
        DatabasePath = AppPaths.DatabasePath;
        Directory.CreateDirectory(DataDirectory);
        Initialize();
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = DatabasePath };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteConnection OpenConnectionTo(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = mode };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS app_settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS accounts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            email TEXT NOT NULL,
            email_norm TEXT NOT NULL,
            password TEXT NOT NULL,
            twofa TEXT NOT NULL DEFAULT '',
            category TEXT NOT NULL DEFAULT '',
            category_norm TEXT NOT NULL DEFAULT '',
            tags TEXT NOT NULL DEFAULT '',
            tags_norm TEXT NOT NULL DEFAULT '',
            remark TEXT NOT NULL DEFAULT '',
            remark_norm TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_accounts_email_norm ON accounts(email_norm);
        CREATE INDEX IF NOT EXISTS idx_accounts_category_norm ON accounts(category_norm);
        CREATE INDEX IF NOT EXISTS idx_accounts_tags_norm ON accounts(tags_norm);
        CREATE INDEX IF NOT EXISTS idx_accounts_remark_norm ON accounts(remark_norm);
        """;
        command.ExecuteNonQuery();
    }

    public string? GetSetting(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public string GetRequiredSetting(string key)
    {
        return GetSetting(key) ?? throw new InvalidDataException($"缺少安全配置：{key}");
    }

    public void SetSettings(IReadOnlyDictionary<string, string> settings)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var pair in settings)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
            INSERT INTO app_settings(key, value) VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
            command.Parameters.AddWithValue("$key", pair.Key);
            command.Parameters.AddWithValue("$value", pair.Value);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void DeleteSetting(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    public List<AccountRecord> GetAll()
    {
        EnsureProtector();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, email, password, twofa, category, tags, remark, created_at, updated_at
        FROM accounts
        ORDER BY updated_at DESC, id DESC;
        """;

        using var reader = command.ExecuteReader();
        var result = new List<AccountRecord>();
        while (reader.Read())
        {
            result.Add(ReadAccount(reader));
        }

        return result;
    }

    public List<string> GetCategories()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT DISTINCT category
        FROM accounts
        WHERE TRIM(category) <> ''
        ORDER BY category COLLATE NOCASE;
        """;
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public List<string> GetTags()
    {
        var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tags FROM accounts WHERE TRIM(tags) <> '';";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            foreach (var tag in SplitTags(reader.GetString(0))) tags.Add(tag);
        }
        return tags.ToList();
    }

    public long Insert(AccountRecord account)
    {
        EnsureProtector();
        using var connection = OpenConnection();
        return Insert(account, connection, null);
    }

    private long Insert(AccountRecord account, SqliteConnection connection, SqliteTransaction? transaction)
    {
        EnsureProtector();
        var now = DateTime.Now;
        account.CreatedAt = account.CreatedAt == default ? now : account.CreatedAt;
        account.UpdatedAt = account.UpdatedAt == default ? now : account.UpdatedAt;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
        INSERT INTO accounts
        (email, email_norm, password, twofa, category, category_norm, tags, tags_norm, remark, remark_norm, created_at, updated_at)
        VALUES
        ($email, $email_norm, $password, $twofa, $category, $category_norm, $tags, $tags_norm, $remark, $remark_norm, $created_at, $updated_at);
        SELECT last_insert_rowid();
        """;
        AddAccountParameters(command, account);
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public void Update(AccountRecord account)
    {
        EnsureProtector();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        account.UpdatedAt = DateTime.Now;
        command.CommandText = """
        UPDATE accounts SET
            email = $email,
            email_norm = $email_norm,
            password = $password,
            twofa = $twofa,
            category = $category,
            category_norm = $category_norm,
            tags = $tags,
            tags_norm = $tags_norm,
            remark = $remark,
            remark_norm = $remark_norm,
            updated_at = $updated_at
        WHERE id = $id;
        """;
        AddAccountParameters(command, account);
        command.Parameters.AddWithValue("$id", account.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM accounts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public DatabaseHealthResult CheckHealth()
    {
        EnsureProtector();
        var result = new DatabaseHealthResult
        {
            DatabasePath = DatabasePath,
            DatabaseExists = File.Exists(DatabasePath),
            DatabaseSizeBytes = File.Exists(DatabasePath) ? new FileInfo(DatabasePath).Length : 0
        };

        try
        {
            using var connection = OpenConnection();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA quick_check;";
                result.QuickCheck = command.ExecuteScalar()?.ToString() ?? string.Empty;
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                SELECT name FROM sqlite_master
                WHERE type = 'table' AND name IN ('app_settings', 'accounts')
                ORDER BY name;
                """;
                using var reader = command.ExecuteReader();
                while (reader.Read()) result.Tables.Add(reader.GetString(0));
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM app_settings;";
                result.SettingsCount = Convert.ToInt32(command.ExecuteScalar());
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM accounts;";
                result.AccountCount = Convert.ToInt32(command.ExecuteScalar());
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MAX(updated_at) FROM accounts;";
                result.LastAccountUpdate = command.ExecuteScalar()?.ToString() ?? string.Empty;
            }

            result.SecurityConfigured = GetSetting("security.version") == "1";

            var accounts = GetAll();
            result.DecryptedAccountCount = accounts.Count;
            result.CategoryCount = accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.Category))
                .Select(a => a.Category.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            result.TagCount = accounts
                .SelectMany(a => SplitTags(a.Tags))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            result.Ok = string.Equals(result.QuickCheck, "ok", StringComparison.OrdinalIgnoreCase)
                        && result.Tables.Contains("accounts")
                        && result.Tables.Contains("app_settings")
                        && result.DecryptedAccountCount == result.AccountCount
                        && result.SecurityConfigured;
        }
        catch (Exception ex)
        {
            result.Ok = false;
            result.Error = ex.Message;
        }

        return result;
    }

    public ImportResult ImportAccounts(IEnumerable<AccountRecord> accounts, DuplicateMode duplicateMode)
    {
        EnsureProtector();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var result = new ImportResult();

        foreach (var account in accounts)
        {
            result.Parsed++;
            var existingId = FindIdByEmailNorm(account.Email, connection, transaction);
            if (existingId is not null)
            {
                switch (duplicateMode)
                {
                    case DuplicateMode.Skip:
                        result.SkippedDuplicates++;
                        break;
                    case DuplicateMode.Overwrite:
                        account.Id = existingId.Value;
                        Update(account, connection, transaction);
                        result.Updated++;
                        break;
                    case DuplicateMode.KeepBoth:
                        Insert(account, connection, transaction);
                        result.Inserted++;
                        break;
                }
            }
            else
            {
                Insert(account, connection, transaction);
                result.Inserted++;
            }
        }

        transaction.Commit();
        return result;
    }

    private void Update(AccountRecord account, SqliteConnection connection, SqliteTransaction transaction)
    {
        EnsureProtector();
        account.UpdatedAt = DateTime.Now;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
        UPDATE accounts SET
            email = $email,
            email_norm = $email_norm,
            password = $password,
            twofa = $twofa,
            category = $category,
            category_norm = $category_norm,
            tags = $tags,
            tags_norm = $tags_norm,
            remark = $remark,
            remark_norm = $remark_norm,
            updated_at = $updated_at
        WHERE id = $id;
        """;
        AddAccountParameters(command, account);
        command.Parameters.AddWithValue("$id", account.Id);
        command.ExecuteNonQuery();
    }

    private static long? FindIdByEmailNorm(string email, SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM accounts WHERE email_norm = $email_norm ORDER BY id LIMIT 1;";
        command.Parameters.AddWithValue("$email_norm", Normalize(email));
        var scalar = command.ExecuteScalar();
        return scalar is null || scalar is DBNull ? null : (long)scalar;
    }

    private void AddAccountParameters(SqliteCommand command, AccountRecord account)
    {
        EnsureProtector();
        var category = account.Category.Trim();
        var tags = NormalizeTagsForStorage(account.Tags);
        var remark = account.Remark.Trim();

        command.Parameters.AddWithValue("$email", account.Email.Trim());
        command.Parameters.AddWithValue("$email_norm", Normalize(account.Email));
        command.Parameters.AddWithValue("$password", Protector!.Protect(account.Password));
        command.Parameters.AddWithValue("$twofa", Protector!.Protect(account.TwoFa.Trim()));
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$category_norm", Normalize(category));
        command.Parameters.AddWithValue("$tags", tags);
        command.Parameters.AddWithValue("$tags_norm", Normalize(tags));
        command.Parameters.AddWithValue("$remark", Protector!.Protect(remark));
        command.Parameters.AddWithValue("$remark_norm", Normalize(remark));
        command.Parameters.AddWithValue("$created_at", account.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", account.UpdatedAt.ToString("O"));
    }

    private AccountRecord ReadAccount(SqliteDataReader reader)
    {
        EnsureProtector();
        return new AccountRecord
        {
            Id = reader.GetInt64(0),
            Email = reader.GetString(1),
            Password = Protector!.Unprotect(reader.GetString(2)),
            TwoFa = Protector!.Unprotect(reader.GetString(3)),
            Category = reader.GetString(4),
            Tags = reader.GetString(5),
            Remark = Protector!.Unprotect(reader.GetString(6)),
            CreatedAt = DateTime.TryParse(reader.GetString(7), out var createdAt) ? createdAt : DateTime.MinValue,
            UpdatedAt = DateTime.TryParse(reader.GetString(8), out var updatedAt) ? updatedAt : DateTime.MinValue,
        };
    }

    public void CreateDatabaseBackup(string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path;";
        command.Parameters.AddWithValue("$path", destinationPath);
        command.ExecuteNonQuery();
    }

    public void RestoreDatabaseFromBackup(string backupPath)
    {
        ValidateBackupDatabase(backupPath);
        Directory.CreateDirectory(DataDirectory);

        if (File.Exists(DatabasePath))
        {
            var safetyCopy = Path.Combine(DataDirectory, $"accounts_before_restore_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            File.Copy(DatabasePath, safetyCopy, overwrite: false);
        }

        File.Copy(backupPath, DatabasePath, overwrite: true);
    }

    public static bool ValidateBackupDatabase(string backupPath)
    {
        if (!File.Exists(backupPath)) throw new FileNotFoundException("备份文件不存在。", backupPath);

        try
        {
            using var connection = OpenConnectionTo(backupPath, SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM app_settings WHERE key = 'security.version';";
            var version = command.ExecuteScalar() as string;
            if (version != "1") throw new InvalidDataException("备份文件缺少安全配置或版本不兼容。 ");

            using var accountCheck = connection.CreateCommand();
            accountCheck.CommandText = "SELECT COUNT(*) FROM accounts;";
            accountCheck.ExecuteScalar();
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidDataException)
        {
            throw new InvalidDataException("这不是有效的 AccountManager 备份数据库。", ex);
        }
    }

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    public static string NormalizeTagsForStorage(string tags)
    {
        return string.Join(", ", SplitTags(tags));
    }

    public static IEnumerable<string> SplitTags(string tags)
    {
        return (tags ?? string.Empty)
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureProtector()
    {
        if (Protector is null)
        {
            throw new InvalidOperationException("数据库未解锁，不能读取或写入账号数据。 ");
        }
    }
}

