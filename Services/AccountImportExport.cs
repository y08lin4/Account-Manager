using System.IO;
using System.Text;
using AccountManager.Models;

namespace AccountManager.Services;

public static class AccountImportExport
{
    public static (List<AccountRecord> Accounts, List<string> Errors, int TotalLines) ParsePlainText(string text, string defaultCategory, string defaultTags)
    {
        var accounts = new List<AccountRecord>();
        var errors = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lineNumber = 0;

        foreach (var raw in lines)
        {
            lineNumber++;
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var account = ParseLine(line, defaultCategory, defaultTags, out var error);
            if (account is null)
            {
                errors.Add($"第 {lineNumber} 行：{error} | {line}");
                continue;
            }

            accounts.Add(account);
        }

        return (accounts, errors, lines.Length);
    }

    public static ImportPreviewResult PreviewPlainText(
        string text,
        string defaultCategory,
        string defaultTags,
        DuplicateMode duplicateMode,
        IEnumerable<string> existingEmails)
    {
        var parsed = ParsePlainText(text, defaultCategory, defaultTags);
        var result = new ImportPreviewResult
        {
            TotalLines = parsed.TotalLines,
            Parsed = parsed.Accounts.Count
        };
        result.Errors.AddRange(parsed.Errors);

        var initialExisting = new HashSet<string>(existingEmails.Select(AccountDatabase.Normalize), StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(initialExisting, StringComparer.OrdinalIgnoreCase);
        var inputSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateSamples = new List<string>();

        foreach (var account in parsed.Accounts)
        {
            var normalized = AccountDatabase.Normalize(account.Email);
            var existsBeforeThisLine = known.Contains(normalized);
            if (initialExisting.Contains(normalized)) result.ExistingDuplicates++;
            if (!inputSeen.Add(normalized)) result.InputDuplicates++;

            if ((existsBeforeThisLine || initialExisting.Contains(normalized)) && duplicateSamples.Count < 20)
            {
                duplicateSamples.Add(account.Email);
            }

            if (existsBeforeThisLine)
            {
                switch (duplicateMode)
                {
                    case DuplicateMode.Skip:
                        result.SkippedDuplicates++;
                        break;
                    case DuplicateMode.Overwrite:
                        result.Updated++;
                        break;
                    case DuplicateMode.KeepBoth:
                        result.Inserted++;
                        break;
                }
            }
            else
            {
                result.Inserted++;
                known.Add(normalized);
            }
        }

        result.DuplicateSamples.AddRange(duplicateSamples.Distinct(StringComparer.OrdinalIgnoreCase));
        return result;
    }

    private static AccountRecord? ParseLine(string line, string defaultCategory, string defaultTags, out string error)
    {
        error = string.Empty;
        var parts = line.Split("--", StringSplitOptions.None);
        if (parts.Length < 2)
        {
            error = "缺少分隔符 --，格式应为 邮箱--密码--2FA";
            return null;
        }

        var email = parts[0].Trim();
        var password = parts[1];
        var twofa = parts.Length >= 3 ? parts[2].Trim() : string.Empty;
        var remark = parts.Length >= 4 ? string.Join("--", parts.Skip(3)).Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            error = "邮箱为空或格式不像邮箱";
            return null;
        }

        if (string.IsNullOrEmpty(password))
        {
            error = "密码为空";
            return null;
        }

        return new AccountRecord
        {
            Email = email,
            Password = password,
            TwoFa = twofa,
            Category = defaultCategory.Trim(),
            Tags = AccountDatabase.NormalizeTagsForStorage(defaultTags),
            Remark = remark,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    public static void WriteTextExport(string path, IEnumerable<AccountRecord> accounts)
    {
        var sb = new StringBuilder();
        foreach (var account in accounts)
        {
            sb.Append(account.Email)
                .Append("--")
                .Append(account.Password)
                .Append("--")
                .Append(account.TwoFa);
            if (!string.IsNullOrWhiteSpace(account.Remark))
            {
                sb.Append("--").Append(account.Remark.ReplaceLineEndings(" "));
            }
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static void WriteCsvExport(string path, IEnumerable<AccountRecord> accounts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("email,password,twofa,category,tags,remark,created_at,updated_at");
        foreach (var account in accounts)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Csv(account.Email),
                Csv(account.Password),
                Csv(account.TwoFa),
                Csv(account.Category),
                Csv(account.Tags),
                Csv(account.Remark),
                Csv(account.CreatedAt.ToString("O")),
                Csv(account.UpdatedAt.ToString("O"))
            }));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static string CreateBackupFileName()
    {
        return $"AccountManager_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db";
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        value = value.Replace("\"", "\"\"");
        return mustQuote ? $"\"{value}\"" : value;
    }
}
