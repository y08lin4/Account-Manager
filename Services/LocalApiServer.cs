using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AccountManager.Models;

namespace AccountManager.Services;

public sealed class LocalApiServer : IDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 20 * 1024 * 1024;
    private const string ApiTokenSettingKey = "api.token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AccountDatabase _database;
    private readonly SecurityService _security;
    private readonly Action _onDataChanged;
    private readonly object _databaseGate = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _apiToken = string.Empty;

    public LocalApiServer(AccountDatabase database, SecurityService security, Action onDataChanged)
    {
        _database = database;
        _security = security;
        _onDataChanged = onDataChanged;
    }

    public int Port { get; } = 17878;
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public bool IsRunning { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public string TokenFilePath => AppPaths.ApiTokenPath;

    public bool Start()
    {
        if (IsRunning) return true;

        try
        {
            _apiToken = EnsureApiToken();
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start(64);
            IsRunning = true;
            LastError = string.Empty;
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        IsRunning = false;

        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => ProcessClientAsync(client, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(200, cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken serverToken)
    {
        using var _ = client;

        try
        {
            client.NoDelay = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, timeout.Token);
            if (request is null) return;

            var response = HandleRequest(request);
            await WriteJsonAsync(stream, response.StatusCode, response.Body, timeout.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiHttpException ex)
        {
            try
            {
                await WriteJsonAsync(client.GetStream(), ex.StatusCode, Error(ex.Message, ex.Detail), serverToken);
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(client.GetStream(), 500, Error("服务器内部错误", ex.Message), serverToken);
            }
            catch
            {
            }
        }
    }

    private ApiResponse HandleRequest(ApiRequest request)
    {
        if (request.Method == "GET" && PathEquals(request.Path, "/api/v1/health"))
        {
            return Ok(new
            {
                name = "AccountManager",
                api = "v1",
                unlocked = _security.IsUnlocked,
                baseUrl = BaseUrl
            });
        }

        if (!_security.IsUnlocked)
        {
            return new ApiResponse(423, Error("软件已锁定，请先在界面输入主密码解锁"));
        }

        if (!IsAuthorized(request))
        {
            return new ApiResponse(401, Error("未授权，请使用 Authorization: Bearer <token>"));
        }

        if (request.Method == "GET" && PathEquals(request.Path, "/api/v1/accounts"))
        {
            return HandleListAccounts(request);
        }

        if (request.Method == "POST" && PathEquals(request.Path, "/api/v1/accounts"))
        {
            return HandleCreateAccount(request);
        }

        if (TryMatchAccountId(request.Path, out var id))
        {
            return request.Method switch
            {
                "GET" => HandleGetAccount(request, id),
                "PATCH" => HandlePatchAccount(request, id),
                "DELETE" => HandleDeleteAccount(id),
                _ => new ApiResponse(405, Error("方法不支持"))
            };
        }

        if (request.Method == "POST" && PathEquals(request.Path, "/api/v1/import"))
        {
            return HandleImport(request);
        }

        return new ApiResponse(404, Error("接口不存在"));
    }

    private ApiResponse HandleListAccounts(ApiRequest request)
    {
        var includeSecrets = QueryBool(request, "includeSecrets");
        var query = QueryValue(request, "query");
        var category = QueryValue(request, "category");
        var tag = QueryValue(request, "tag");

        List<AccountRecord> filtered;
        lock (_databaseGate)
        {
            filtered = AccountSearch.Filter(_database.GetAll(), query, category, tag);
        }

        return Ok(new
        {
            count = filtered.Count,
            accounts = filtered.Select(account => ToDto(account, includeSecrets)).ToList()
        });
    }

    private ApiResponse HandleGetAccount(ApiRequest request, long id)
    {
        var includeSecrets = QueryBool(request, "includeSecrets");
        AccountRecord? account;
        lock (_databaseGate)
        {
            account = _database.GetAll().FirstOrDefault(item => item.Id == id);
        }

        return account is null
            ? new ApiResponse(404, Error("账号不存在"))
            : Ok(ToDto(account, includeSecrets));
    }

    private ApiResponse HandleCreateAccount(ApiRequest request)
    {
        var body = ReadJson<AccountCreateRequest>(request);
        var validationError = ValidateCreate(body);
        if (!string.IsNullOrEmpty(validationError)) return new ApiResponse(400, Error(validationError));

        var account = new AccountRecord
        {
            Email = body.Email.Trim(),
            Password = body.Password,
            TwoFa = body.TwoFa.Trim(),
            Category = body.Category.Trim(),
            Tags = body.Tags,
            Remark = body.Remark.Trim()
        };

        lock (_databaseGate)
        {
            account.Id = _database.Insert(account);
        }

        _onDataChanged();
        return new ApiResponse(201, new { ok = true, data = new { account = ToDto(account, includeSecrets: true) } });
    }

    private ApiResponse HandlePatchAccount(ApiRequest request, long id)
    {
        var body = ReadJson<AccountPatchRequest>(request);

        AccountRecord? account;
        lock (_databaseGate)
        {
            account = _database.GetAll().FirstOrDefault(item => item.Id == id);
        }

        if (account is null) return new ApiResponse(404, Error("账号不存在"));

        if (body.Email is not null) account.Email = body.Email.Trim();
        if (body.Password is not null) account.Password = body.Password;
        if (body.TwoFa is not null) account.TwoFa = body.TwoFa.Trim();
        if (body.Category is not null) account.Category = body.Category.Trim();
        if (body.Tags is not null) account.Tags = body.Tags;
        if (body.Remark is not null) account.Remark = body.Remark.Trim();

        var validationError = ValidateAccount(account);
        if (!string.IsNullOrEmpty(validationError)) return new ApiResponse(400, Error(validationError));

        lock (_databaseGate)
        {
            _database.Update(account);
            account = _database.GetAll().FirstOrDefault(item => item.Id == id) ?? account;
        }

        _onDataChanged();
        return Ok(ToDto(account, includeSecrets: true));
    }

    private ApiResponse HandleDeleteAccount(long id)
    {
        lock (_databaseGate)
        {
            if (_database.GetAll().All(item => item.Id != id))
            {
                return new ApiResponse(404, Error("账号不存在"));
            }

            _database.Delete(id);
        }

        _onDataChanged();
        return Ok(new { deleted = id });
    }

    private ApiResponse HandleImport(ApiRequest request)
    {
        var body = ReadJson<ImportApiRequest>(request);
        if (string.IsNullOrWhiteSpace(body.Text)) return new ApiResponse(400, Error("text 不能为空"));

        var parsed = AccountImportExport.ParsePlainText(body.Text, body.Category, body.Tags);
        var result = new ImportResult { TotalLines = parsed.TotalLines };
        result.Errors.AddRange(parsed.Errors);

        if (parsed.Accounts.Count > 0)
        {
            lock (_databaseGate)
            {
                var importResult = _database.ImportAccounts(parsed.Accounts, ParseDuplicateMode(body.DuplicateMode));
                result.Parsed = importResult.Parsed;
                result.Inserted = importResult.Inserted;
                result.Updated = importResult.Updated;
                result.SkippedDuplicates = importResult.SkippedDuplicates;
            }

            _onDataChanged();
        }

        return new ApiResponse(200, new
        {
            ok = result.Errors.Count == 0,
            data = new
            {
                totalLines = result.TotalLines,
                parsed = result.Parsed,
                inserted = result.Inserted,
                updated = result.Updated,
                skippedDuplicates = result.SkippedDuplicates,
                errors = result.Errors
            }
        });
    }

    private string EnsureApiToken()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var token = _database.GetSetting(ApiTokenSettingKey);

        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("am_", StringComparison.Ordinal))
        {
            token = "am_" + Base64Url(RandomNumberGenerator.GetBytes(32));
            _database.SetSettings(new Dictionary<string, string>
            {
                [ApiTokenSettingKey] = token
            });
        }

        File.WriteAllText(AppPaths.ApiTokenPath, token + Environment.NewLine, new UTF8Encoding(false));
        return token;
    }

    private bool IsAuthorized(ApiRequest request)
    {
        var provided = ExtractToken(request);
        if (string.IsNullOrWhiteSpace(provided)) return false;

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(_apiToken);
        return providedBytes.Length == expectedBytes.Length
               && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static string ExtractToken(ApiRequest request)
    {
        if (request.Headers.TryGetValue("Authorization", out var authorization)
            && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization[7..].Trim();
        }

        return request.Headers.TryGetValue("X-AccountManager-Token", out var token)
            ? token.Trim()
            : string.Empty;
    }

    private static AccountApiDto ToDto(AccountRecord account, bool includeSecrets)
    {
        return new AccountApiDto
        {
            Id = account.Id,
            Email = account.Email,
            Password = includeSecrets ? account.Password : null,
            TwoFa = includeSecrets ? account.TwoFa : null,
            Category = account.Category,
            Tags = account.Tags,
            Remark = account.Remark,
            MatchField = account.MatchField,
            MatchValue = account.MatchValue,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt
        };
    }

    private static string ValidateCreate(AccountCreateRequest body)
    {
        if (body is null) return "请求 JSON 无效";
        return ValidateAccount(new AccountRecord
        {
            Email = body.Email,
            Password = body.Password
        });
    }

    private static string ValidateAccount(AccountRecord account)
    {
        if (string.IsNullOrWhiteSpace(account.Email) || !account.Email.Contains('@'))
        {
            return "email 不能为空，并且必须像邮箱";
        }

        if (string.IsNullOrEmpty(account.Password))
        {
            return "password 不能为空";
        }

        return string.Empty;
    }

    private static DuplicateMode ParseDuplicateMode(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "overwrite" or "update" or "replace" or "覆盖" => DuplicateMode.Overwrite,
            "keepboth" or "keep-both" or "keep_both" or "both" or "保留重复" => DuplicateMode.KeepBoth,
            _ => DuplicateMode.Skip
        };
    }

    private static T ReadJson<T>(ApiRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Body))
            {
                throw new ApiHttpException(400, "请求体不能为空");
            }

            return JsonSerializer.Deserialize<T>(request.Body, JsonOptions)
                   ?? throw new ApiHttpException(400, "请求 JSON 无效");
        }
        catch (JsonException ex)
        {
            throw new ApiHttpException(400, "请求 JSON 解析失败", ex.Message);
        }
    }

    private static async Task<ApiRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var received = new List<byte>(8192);
        var buffer = new byte[8192];
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) return null;

            received.AddRange(buffer.AsSpan(0, read).ToArray());
            if (received.Count > MaxHeaderBytes)
            {
                throw new ApiHttpException(431, "请求头过大");
            }

            headerEnd = FindHeaderEnd(received);
        }

        var bodyStart = headerEnd + 4;
        var headerText = Encoding.ASCII.GetString(received.GetRange(0, headerEnd).ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines.FirstOrDefault() ?? string.Empty;
        var requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (requestParts.Length < 2)
        {
            throw new ApiHttpException(400, "请求行无效");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var lengthValue)
            && (!int.TryParse(lengthValue, out contentLength) || contentLength < 0))
        {
            throw new ApiHttpException(400, "Content-Length 无效");
        }

        if (contentLength > MaxBodyBytes)
        {
            throw new ApiHttpException(413, "请求体过大");
        }

        var body = new byte[contentLength];
        var existingBodyLength = Math.Max(0, received.Count - bodyStart);
        if (existingBodyLength > 0)
        {
            Array.Copy(received.ToArray(), bodyStart, body, 0, Math.Min(existingBodyLength, contentLength));
        }

        var offset = Math.Min(existingBodyLength, contentLength);
        while (offset < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset), cancellationToken);
            if (read == 0) throw new ApiHttpException(400, "请求体不完整");
            offset += read;
        }

        var target = requestParts[1];
        var (path, query) = SplitTarget(target);

        return new ApiRequest(
            requestParts[0].ToUpperInvariant(),
            path,
            ParseQuery(query),
            headers,
            contentLength == 0 ? string.Empty : Encoding.UTF8.GetString(body));
    }

    private static (string Path, string Query) SplitTarget(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))
        {
            return (absolute.AbsolutePath, absolute.Query);
        }

        var question = target.IndexOf('?');
        return question < 0
            ? (Uri.UnescapeDataString(target), string.Empty)
            : (Uri.UnescapeDataString(target[..question]), target[question..]);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        query = query.TrimStart('?');
        if (string.IsNullOrEmpty(query)) return result;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? string.Empty : pair[(equals + 1)..];
            result[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(value);
        }

        return result;
    }

    private static int FindHeaderEnd(IReadOnlyList<byte> bytes)
    {
        for (var i = 0; i <= bytes.Count - 4; i++)
        {
            if (bytes[i] == '\r'
                && bytes[i + 1] == '\n'
                && bytes[i + 2] == '\r'
                && bytes[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static async Task WriteJsonAsync(NetworkStream stream, int statusCode, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {ReasonPhrase(statusCode)}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {json.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(json, cancellationToken);
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(left.TrimEnd('/'), right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMatchAccountId(string path, out long id)
    {
        id = 0;
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
               && string.Equals(parts[0], "api", StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[1], "v1", StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[2], "accounts", StringComparison.OrdinalIgnoreCase)
               && long.TryParse(parts[3], out id)
               && id > 0;
    }

    private static string QueryValue(ApiRequest request, string key)
    {
        return request.Query.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static bool QueryBool(ApiRequest request, string key)
    {
        return request.Query.TryGetValue(key, out var value)
               && (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }

    private static ApiResponse Ok(object body)
    {
        var value = body;
        return new ApiResponse(200, value is not null && HasOkProperty(value) ? value : new { ok = true, data = value });
    }

    private static bool HasOkProperty(object value)
    {
        return value.GetType().GetProperty("ok") is not null
               || value.GetType().GetProperty("Ok") is not null;
    }

    private static object Error(string message, string? detail = null)
    {
        return new ApiEnvelope<object> { Ok = false, Error = message, Detail = detail };
    }

    private static string ReasonPhrase(int statusCode)
    {
        return statusCode switch
        {
            200 => "OK",
            201 => "Created",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            423 => "Locked",
            431 => "Request Header Fields Too Large",
            _ => "Internal Server Error"
        };
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record ApiRequest(
        string Method,
        string Path,
        Dictionary<string, string> Query,
        Dictionary<string, string> Headers,
        string Body);

    private sealed record ApiResponse(int StatusCode, object Body);

    private sealed class ApiHttpException : Exception
    {
        public ApiHttpException(int statusCode, string message, string? detail = null) : base(message)
        {
            StatusCode = statusCode;
            Detail = detail;
        }

        public int StatusCode { get; }
        public string? Detail { get; }
    }
}
