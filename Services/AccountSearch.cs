using AccountManager.Models;
using System.Text.RegularExpressions;

namespace AccountManager.Services;

public static class AccountSearch
{
    public static List<AccountRecord> Filter(IEnumerable<AccountRecord> source, string query, string categoryFilter, string tagFilter)
    {
        var q = query.Trim();
        var category = categoryFilter.Trim();
        var tag = tagFilter.Trim();

        return source
            .Where(a => string.IsNullOrWhiteSpace(category) || string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(a => string.IsNullOrWhiteSpace(tag) || AccountDatabase.SplitTags(a.Tags).Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            .Where(a => MatchAndMark(a, q))
            .OrderByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.Id)
            .ToList();
    }

    private static bool MatchAndMark(AccountRecord account, string query)
    {
        account.MatchField = string.Empty;
        account.MatchValue = string.Empty;

        if (string.IsNullOrWhiteSpace(query)) return true;

        var fields = new[]
        {
            (Name: "\u90ae\u7bb1", Value: account.Email),
            (Name: "2FA", Value: account.TwoFa),
            (Name: "\u5206\u7c7b", Value: account.Category),
            (Name: "\u6807\u7b7e", Value: account.Tags),
            (Name: "\u5907\u6ce8", Value: account.Remark),
        };

        foreach (var field in fields)
        {
            if (ContainsEitherWay(field.Value, query))
            {
                account.MatchField = field.Name;
                account.MatchValue = field.Value;
                return true;
            }
        }

        return false;
    }

    public static bool ContainsEitherWay(string value, string query)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query)) return false;

        if (value.Contains(query, StringComparison.OrdinalIgnoreCase)
            || query.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var valueVariants = BuildSearchVariants(value);
        var queryVariants = BuildSearchVariants(query);
        return valueVariants.Any(v => queryVariants.Any(q =>
            v.Contains(q, StringComparison.OrdinalIgnoreCase)
            || q.Contains(v, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyCollection<string> BuildSearchVariants(string input)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        void Add(string value)
        {
            value = NormalizeSearchText(value);
            if (string.IsNullOrWhiteSpace(value) || result.Count > 48) return;
            if (result.Add(value)) queue.Enqueue(value);
        }

        Add(input);
        while (queue.Count > 0)
        {
            var value = queue.Dequeue();

            if (value.StartsWith("codex-", StringComparison.OrdinalIgnoreCase))
            {
                Add(value["codex-".Length..]);
            }

            foreach (var suffix in new[] { "-plus", "-team" })
            {
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    Add(value[..^suffix.Length]);
                }
            }

            foreach (Match match in Regex.Matches(value, @"[a-z0-9._%+\-]+@[a-z0-9.\-]+(?:\.[a-z]{2,})?", RegexOptions.IgnoreCase))
            {
                Add(match.Value);
            }

            if (TrySplitEmail(value, out var localPart, out var domain))
            {
                Add($"{localPart}_{domain}");
                var lastDot = domain.LastIndexOf('.');
                if (lastDot > 0)
                {
                    Add($"{localPart}@{domain[..lastDot]}");
                    Add($"{localPart}_{domain[..lastDot]}");
                }
            }

            var underscoreEmail = Regex.Match(value, @"^(.+)_([a-z0-9.-]+(?:\.[a-z]{2,})?)$", RegexOptions.IgnoreCase);
            if (underscoreEmail.Success)
            {
                Add($"{underscoreEmail.Groups[1].Value}@{underscoreEmail.Groups[2].Value}");
            }

            var underscoreDomain = Regex.Match(value, @"^(.+)_([a-z0-9-]+)_([a-z]{2,})$", RegexOptions.IgnoreCase);
            if (underscoreDomain.Success)
            {
                Add($"{underscoreDomain.Groups[1].Value}@{underscoreDomain.Groups[2].Value}.{underscoreDomain.Groups[3].Value}");
            }
        }

        return result;
    }

    private static string NormalizeSearchText(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Trim('\'', '"', '`')
            .Replace('\uFF20', '@')
            .Replace('\uFF3F', '_')
            .ToLowerInvariant();
    }

    private static bool TrySplitEmail(string value, out string localPart, out string domain)
    {
        localPart = string.Empty;
        domain = string.Empty;
        var at = value.IndexOf('@');
        if (at <= 0 || at >= value.Length - 1) return false;

        localPart = value[..at];
        domain = value[(at + 1)..];
        return true;
    }
}
