using AccountManager.Models;

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
            (Name: "邮箱", Value: account.Email),
            (Name: "2FA", Value: account.TwoFa),
            (Name: "分类", Value: account.Category),
            (Name: "标签", Value: account.Tags),
            (Name: "备注", Value: account.Remark),
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

        return value.Contains(query, StringComparison.OrdinalIgnoreCase)
               || query.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
