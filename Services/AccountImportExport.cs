using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AccountManager.Models;

namespace AccountManager.Services;

public static class AccountImportExport
{
    public static (List<AccountRecord> Accounts, List<string> Errors, int TotalLines) ParsePlainText(string text, string defaultCategory, string defaultTags)
    {
        var normalizedText = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (LooksLikeDelimitedTable(normalizedText))
        {
            return ParseDelimitedText(normalizedText, defaultCategory, defaultTags);
        }

        return ParsePlainTextLines(normalizedText, defaultCategory, defaultTags);
    }

    private static (List<AccountRecord> Accounts, List<string> Errors, int TotalLines) ParsePlainTextLines(string text, string defaultCategory, string defaultTags)
    {
        var accounts = new List<AccountRecord>();
        var errors = new List<string>();
        var lines = text.Split('\n');
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

    private static (List<AccountRecord> Accounts, List<string> Errors, int TotalLines) ParseDelimitedText(string text, string defaultCategory, string defaultTags)
    {
        var accounts = new List<AccountRecord>();
        var errors = new List<string>();
        const char delimiter = ',';
        var rows = ParseDelimitedRows(text, delimiter)
            .Where(row => !IsEmptyRow(row.Fields))
            .ToList();
        var totalLines = text.Split('\n').Length;

        if (rows.Count == 0) return (accounts, errors, totalLines);

        var headerMap = BuildHeaderMap(rows[0].Fields);
        var hasHeader = headerMap.HasRequiredColumns;
        var map = hasHeader
            ? headerMap
            : new DelimitedColumnMap
            {
                Email = 0,
                Password = 1,
                TwoFa = 2,
                Remark = 3
            };

        for (var index = hasHeader ? 1 : 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (hasHeader && BuildHeaderMap(row.Fields).HasRequiredColumns) continue;

            var account = ParseDelimitedRow(row.Fields, map, defaultCategory, defaultTags, out var error);
            if (account is null)
            {
                errors.Add($"第 {row.LineNumber} 行：{error} | {FormatDelimitedErrorSample(row.Fields, delimiter)}");
                continue;
            }

            accounts.Add(account);
        }

        return (accounts, errors, totalLines);
    }

    private static AccountRecord? ParseDelimitedRow(
        IReadOnlyList<string> fields,
        DelimitedColumnMap map,
        string defaultCategory,
        string defaultTags,
        out string error)
    {
        error = string.Empty;
        var email = NormalizeEmailForImport(GetField(fields, map.Email));
        var password = GetField(fields, map.Password);
        var twofa = map.TwoFa >= 0 ? TotpService.NormalizeSecretForStorage(GetField(fields, map.TwoFa)) : string.Empty;
        var remark = map.Remark >= 0 ? GetField(fields, map.Remark).Trim() : string.Empty;
        var category = map.Category >= 0 ? GetField(fields, map.Category).Trim() : string.Empty;
        var tags = map.Tags >= 0 ? GetField(fields, map.Tags).Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(category)) category = defaultCategory.Trim();

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
            Category = category,
            Tags = MergeTags(defaultTags, tags),
            Remark = remark,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    private static bool LooksLikeDelimitedTable(string text)
    {
        var rows = ParseDelimitedRows(text, ',')
            .Where(row => !IsEmptyRow(row.Fields))
            .Take(5)
            .ToList();
        if (rows.Count == 0) return false;

        if (BuildHeaderMap(rows[0].Fields).HasRequiredColumns) return true;

        return rows[0].Fields.Count >= 3
               && LooksLikeEmail(NormalizeEmailForImport(rows[0].Fields[0]))
               && !string.IsNullOrEmpty(rows[0].Fields[1]);
    }

    private static List<DelimitedRow> ParseDelimitedRows(string text, char delimiter)
    {
        var rows = new List<DelimitedRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var lineNumber = 1;
        var rowLineNumber = 1;

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (inQuotes)
            {
                if (ch == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    if (ch == '\n') lineNumber++;
                    field.Append(ch);
                }
                continue;
            }

            if (ch == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                rows.Add(new DelimitedRow(rowLineNumber, fields.ToList()));
                fields.Clear();
                lineNumber++;
                rowLineNumber = lineNumber;
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || fields.Count > 0 || text.Length == 0 || !text.EndsWith('\n'))
        {
            fields.Add(field.ToString());
            rows.Add(new DelimitedRow(rowLineNumber, fields.ToList()));
        }

        return rows;
    }

    private static DelimitedColumnMap BuildHeaderMap(IReadOnlyList<string> fields)
    {
        var map = new DelimitedColumnMap();
        for (var index = 0; index < fields.Count; index++)
        {
            switch (NormalizeHeader(fields[index]))
            {
                case "email":
                case "mail":
                case "account":
                case "username":
                case "user":
                case "login":
                case "邮箱":
                case "账号":
                case "帐号":
                    map.Email = index;
                    break;
                case "password":
                case "pass":
                case "pwd":
                case "passwd":
                case "密码":
                    map.Password = index;
                    break;
                case "2fa":
                case "twofa":
                case "totp":
                case "otp":
                case "secret":
                case "totpsecret":
                case "twofasecret":
                case "2fasecret":
                case "密钥":
                case "二步验证":
                case "双重验证":
                    map.TwoFa = index;
                    break;
                case "note":
                case "notes":
                case "remark":
                case "remarks":
                case "comment":
                case "memo":
                case "备注":
                    map.Remark = index;
                    break;
                case "category":
                case "group":
                case "type":
                case "分类":
                case "分组":
                    map.Category = index;
                    break;
                case "tag":
                case "tags":
                case "label":
                case "labels":
                case "标签":
                    map.Tags = index;
                    break;
            }
        }

        return map;
    }

    private static string NormalizeHeader(string value)
    {
        return Regex.Replace((value ?? string.Empty).Trim().Trim('\ufeff').ToLowerInvariant(), @"[\s_\-：:]+", string.Empty);
    }

    private static bool IsEmptyRow(IReadOnlyList<string> fields)
    {
        return fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace);
    }

    private static string GetField(IReadOnlyList<string> fields, int index)
    {
        return index >= 0 && index < fields.Count ? fields[index].Trim() : string.Empty;
    }

    private static string FormatDelimitedErrorSample(IReadOnlyList<string> fields, char delimiter)
    {
        return string.Join(delimiter, fields.Select(field => field.Trim()));
    }

    private static string MergeTags(string defaultTags, string rowTags)
    {
        return AccountDatabase.NormalizeTagsForStorage(string.Join(", ", new[] { defaultTags, rowTags }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static bool CanStartThreeLineAccount(string line)
    {
        var email = NormalizeEmailForImport(TrimTrailingSeparators(line));
        return LooksLikeEmail(email) && !HasSeparator(email);
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

        var email = NormalizeEmailForImport(TrimTrailingSeparators(lines[startIndex].Text));
        var password = TrimTrailingSeparators(lines[startIndex + 1].Text);
        var twofa = TrimTrailingSeparators(lines[startIndex + 2].Text);

        if (!LooksLikeEmail(email))
        {
            error = "邮箱为空或格式不像邮箱";
            return false;
        }

        if (LooksLikeEmail(password) || HasSeparator(password))
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

        var email = NormalizeEmailForImport(parts[0]);
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

    private static string NormalizeEmailForImport(string value)
    {
        var email = (value ?? string.Empty).Trim();
        if (email.Contains('@')) return email;

        var match = Regex.Match(email, @"^(.+)_([A-Za-z0-9.-]+\.[A-Za-z]{2,})$");
        return match.Success ? $"{match.Groups[1].Value}@{match.Groups[2].Value}" : email;
    }

    private static string TrimTrailingSeparators(string value)
    {
        return Regex.Replace((value ?? string.Empty).Trim(), "-{2,}\\s*$", string.Empty).Trim();
    }

    private static bool HasSeparator(string value)
    {
        return Regex.IsMatch(value ?? string.Empty, "-{2,}");
    }

    private sealed record ImportLine(int LineNumber, string Text);

    private sealed record DelimitedRow(int LineNumber, IReadOnlyList<string> Fields);

    private sealed class DelimitedColumnMap
    {
        public int Email { get; set; } = -1;
        public int Password { get; set; } = -1;
        public int TwoFa { get; set; } = -1;
        public int Remark { get; set; } = -1;
        public int Category { get; set; } = -1;
        public int Tags { get; set; } = -1;
        public bool HasRequiredColumns => Email >= 0 && Password >= 0;
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
