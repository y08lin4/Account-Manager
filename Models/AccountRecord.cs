namespace AccountManager.Models;

public sealed class AccountRecord
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string TwoFa { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string MatchField { get; set; } = string.Empty;
    public string MatchValue { get; set; } = string.Empty;
}

public enum DuplicateMode
{
    Skip,
    Overwrite,
    KeepBoth
}

public sealed class ImportResult
{
    public int TotalLines { get; set; }
    public int Parsed { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int SkippedDuplicates { get; set; }
    public List<string> Errors { get; } = new();
}

