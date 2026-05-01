using System.Security.Cryptography;
using System.Text;

namespace AccountManager.Services;

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public sealed class SecurityService : ISecretProtector
{
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DerivedBytesSize = 64;
    private const int DefaultIterations = 250_000;
    private const string ProtectedPrefix = "enc:v1:";

    private readonly AccountDatabase _database;
    private byte[]? _dataKey;

    public SecurityService(AccountDatabase database)
    {
        _database = database;
    }

    public bool IsConfigured => _database.GetSetting("security.version") == CurrentVersion.ToString();

    public bool IsUnlocked => _dataKey is { Length: KeySize };

    public string PasswordHint => _database.GetSetting("security.passwordHint") ?? string.Empty;

    public string RecoveryQuestion => _database.GetSetting("security.recoveryQuestion") ?? string.Empty;

    public void SetupNewVault(string masterPassword, string passwordHint, string recoveryQuestion, string recoveryAnswer)
    {
        ValidatePassword(masterPassword);
        ValidateRecovery(recoveryQuestion, recoveryAnswer);

        var dataKey = RandomNumberGenerator.GetBytes(KeySize);
        SaveSecuritySettings(dataKey, masterPassword, passwordHint, recoveryQuestion, recoveryAnswer);
        _dataKey = dataKey;
    }

    public bool TryUnlock(string masterPassword)
    {
        if (!IsConfigured || string.IsNullOrEmpty(masterPassword)) return false;

        var salt = ReadRequiredBytes("security.masterSalt");
        var expectedHash = ReadRequiredBytes("security.masterHash");
        var iterations = ReadIterations();
        var material = DeriveMaterial(masterPassword, salt, iterations);

        if (!CryptographicOperations.FixedTimeEquals(material.VerificationHash, expectedHash))
        {
            CryptographicOperations.ZeroMemory(material.VerificationHash);
            CryptographicOperations.ZeroMemory(material.WrappingKey);
            return false;
        }

        try
        {
            var wrapped = _database.GetRequiredSetting("security.masterWrap");
            _dataKey = DecryptBytes(wrapped, material.WrappingKey);
            return true;
        }
        catch
        {
            _dataKey = null;
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.VerificationHash);
            CryptographicOperations.ZeroMemory(material.WrappingKey);
        }
    }

    public bool TryRecoverAndResetPassword(string recoveryAnswer, string newMasterPassword, string newPasswordHint)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(recoveryAnswer)) return false;
        ValidatePassword(newMasterPassword);

        var salt = ReadRequiredBytes("security.recoverySalt");
        var expectedHash = ReadRequiredBytes("security.recoveryHash");
        var iterations = ReadIterations();
        var material = DeriveMaterial(NormalizeRecoveryAnswer(recoveryAnswer), salt, iterations);

        if (!CryptographicOperations.FixedTimeEquals(material.VerificationHash, expectedHash))
        {
            CryptographicOperations.ZeroMemory(material.VerificationHash);
            CryptographicOperations.ZeroMemory(material.WrappingKey);
            return false;
        }

        try
        {
            var wrapped = _database.GetRequiredSetting("security.recoveryWrap");
            var dataKey = DecryptBytes(wrapped, material.WrappingKey);
            var question = RecoveryQuestion;
            SaveSecuritySettings(dataKey, newMasterPassword, newPasswordHint, question, recoveryAnswer);
            _dataKey = dataKey;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.VerificationHash);
            CryptographicOperations.ZeroMemory(material.WrappingKey);
        }
    }

    public void ChangeSecurity(string newMasterPassword, string passwordHint, string recoveryQuestion, string recoveryAnswer)
    {
        EnsureUnlocked();
        ValidatePassword(newMasterPassword);
        ValidateRecovery(recoveryQuestion, recoveryAnswer);
        SaveSecuritySettings(_dataKey!, newMasterPassword, passwordHint, recoveryQuestion, recoveryAnswer);
    }

    public string Protect(string plainText)
    {
        EnsureUnlocked();
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plainText);
        return EncryptBytes(bytes, _dataKey!);
    }

    public string Unprotect(string protectedText)
    {
        EnsureUnlocked();
        if (string.IsNullOrEmpty(protectedText)) return string.Empty;
        if (!protectedText.StartsWith(ProtectedPrefix, StringComparison.Ordinal)) return protectedText;
        var bytes = DecryptBytes(protectedText, _dataKey!);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Lock()
    {
        if (_dataKey is not null)
        {
            CryptographicOperations.ZeroMemory(_dataKey);
            _dataKey = null;
        }
    }

    private void SaveSecuritySettings(byte[] dataKey, string masterPassword, string passwordHint, string recoveryQuestion, string recoveryAnswer)
    {
        var masterSalt = RandomNumberGenerator.GetBytes(SaltSize);
        var recoverySalt = RandomNumberGenerator.GetBytes(SaltSize);
        var masterMaterial = DeriveMaterial(masterPassword, masterSalt, DefaultIterations);
        var recoveryMaterial = DeriveMaterial(NormalizeRecoveryAnswer(recoveryAnswer), recoverySalt, DefaultIterations);

        var settings = new Dictionary<string, string>
        {
            ["security.version"] = CurrentVersion.ToString(),
            ["security.iterations"] = DefaultIterations.ToString(),
            ["security.masterSalt"] = Convert.ToBase64String(masterSalt),
            ["security.masterHash"] = Convert.ToBase64String(masterMaterial.VerificationHash),
            ["security.masterWrap"] = EncryptBytes(dataKey, masterMaterial.WrappingKey),
            ["security.passwordHint"] = passwordHint.Trim(),
            ["security.recoveryQuestion"] = recoveryQuestion.Trim(),
            ["security.recoverySalt"] = Convert.ToBase64String(recoverySalt),
            ["security.recoveryHash"] = Convert.ToBase64String(recoveryMaterial.VerificationHash),
            ["security.recoveryWrap"] = EncryptBytes(dataKey, recoveryMaterial.WrappingKey)
        };

        _database.SetSettings(settings);

        CryptographicOperations.ZeroMemory(masterMaterial.VerificationHash);
        CryptographicOperations.ZeroMemory(masterMaterial.WrappingKey);
        CryptographicOperations.ZeroMemory(recoveryMaterial.VerificationHash);
        CryptographicOperations.ZeroMemory(recoveryMaterial.WrappingKey);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            throw new ArgumentException("主密码至少需要 6 个字符。 ");
        }
    }

    private static void ValidateRecovery(string question, string answer)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("请设置密码保护问题。 ");
        }

        if (string.IsNullOrWhiteSpace(answer) || answer.Trim().Length < 2)
        {
            throw new ArgumentException("密码保护答案至少需要 2 个字符。 ");
        }
    }

    private int ReadIterations()
    {
        return int.TryParse(_database.GetSetting("security.iterations"), out var iterations) && iterations > 0
            ? iterations
            : DefaultIterations;
    }

    private byte[] ReadRequiredBytes(string key) => Convert.FromBase64String(_database.GetRequiredSetting(key));

    private static string NormalizeRecoveryAnswer(string answer) => answer.Trim().ToLowerInvariant();

    private static (byte[] VerificationHash, byte[] WrappingKey) DeriveMaterial(string secret, byte[] salt, int iterations)
    {
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            DerivedBytesSize);

        var verification = derived[..KeySize];
        var wrapping = derived[KeySize..DerivedBytesSize];
        CryptographicOperations.ZeroMemory(derived);
        return (verification, wrapping);
    }

    private static string EncryptBytes(byte[] plain, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
        return ProtectedPrefix + Convert.ToBase64String(payload);
    }

    private static byte[] DecryptBytes(string protectedText, byte[] key)
    {
        if (!protectedText.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("加密数据格式无效。 ");
        }

        var payload = Convert.FromBase64String(protectedText[ProtectedPrefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("加密数据长度无效。 ");
        }

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var cipher = payload[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private void EnsureUnlocked()
    {
        if (!IsUnlocked)
        {
            throw new InvalidOperationException("当前数据库未解锁。 ");
        }
    }
}
