using System.Text.Json.Serialization;

namespace AccountManager.Services;

public sealed class AccountApiDto
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? TwoFa { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string MatchField { get; set; } = string.Empty;
    public string MatchValue { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AccountCreateRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string TwoFa { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
}

public sealed class AccountPatchRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? TwoFa { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public string? Remark { get; set; }
}

public sealed class ImportApiRequest
{
    public string Text { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string DuplicateMode { get; set; } = "skip";
}

public sealed class ApiEnvelope<T>
{
    public bool Ok { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }
}
