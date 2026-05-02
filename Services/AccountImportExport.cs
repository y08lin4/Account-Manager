using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AccountManager.Models;

namespace AccountManager.Services;

public static class AccountImportExport
{
    public static (List<AccountRecord> Accounts, List<string> Errors, int TotalLines) ParsePlainText(string text, string defaultCategory, string defaultTags)
    {
        var accounts = new List<AccountRecord>();
        var errors = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var nonEmptyLines = lines
            .Select((raw, index) => new ImportLine(index + 1, raw.Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .ToList();

        for (var index = 0; index < nonEmptyLines.Count; index++)
        {
            var line = nonEmptyLines[index];
            var account = ParseLine(line.Text, defaultCategory, defaultTags, out var error);
            if (account is null)
            {
                if (CanStartThreeLineAccount(line.Text))
                {
                    if (TryParseThreeLineAccount(nonEmptyLines, index, defaultCategory, defaultTags, out account, out error))
                    {
                        accounts.Add(account!);
                        index += 2;
                        continue;
                    }

                    errors.Add($"第 {line.LineNumber} 行：{error} | {line.Text}");
                    continue;
                }

                errors.Add($"第 {line.LineNumber} 行：{error} | {line.Text}");
                continue;
            }

            accounts.Add(account);
        }

        return (accounts, errors, lines.Length);
    }

    private static bool CanStartThreeLineAccount(string line)
    {
        return !line.Contains("--", StringComparison.Ordinal) && LooksLikeEmail(line);
    }

    private static bool TryParseThreeLineAccount(
        IReadOnlyList<ImportLine> lines,
        int startIndex,
        string defaultCategory,
        string defaultTags,
        out AccountRecord? account,
        out string error)
    {
        account = null;
        error = string.Empty;

        if (startIndex + 2 >= lines.Count)
        {
            error = "三行格式不完整，格式应为 邮箱 / 密码 / 2FA";
            return false;
        }

        var email = lines[startIndex].Text.Trim();
        var password = lines[startIndex + 1].Text;
        var twofa = lines[startIndex + 2].Text;

        if (!LooksLikeEmail(email))
        {
            error = "邮箱为空或格式不像邮箱";
            return false;
        }

        if (LooksLikeEmail(password) || password.Contains("--", StringComparison.Ordinal))
        {
            error = "三行格式不完整，第二行应为密码";
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            error = "密码为空";
            return false;
        }

        account = new AccountRecord
        {
            Email = email,
            Password = password,
            TwoFa = TotpService.NormalizeSecretForStorage(twofa),
            Category = defaultCategory.Trim(),
            Tags = AccountDatabase.NormalizeTagsForStorage(defaultTags),
            Remark = string.Empty,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        return true;
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
        var parts = Regex.Split(line, "-{2,}");
        if (parts.Length < 2)
        {
            error = "缺少分隔符，格式应为 邮箱--密码--2FA，连续 2 个及以上 - 都可识别";
            return null;
        }

        var email = parts[0].Trim();
        var password = parts[1];
        var twofa = parts.Length >= 3 ? TotpService.NormalizeSecretForStorage(parts[2]) : string.Empty;
        var remark = parts.Length >= 4 ? string.Join("--", parts.Skip(3)).Trim() : string.Empty;

        if (!LooksLikeEmail(email))
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

    private static bool LooksLikeEmail(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains('@');
    }

    private sealed record ImportLine(int LineNumber, string Text);

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
