using System.Security.Cryptography;

namespace AccountManager.Services;

public static class TotpService
{
    private const int TimeStepSeconds = 30;

    public static bool TryGenerateCode(string secret, out string code, out int secondsRemaining)
    {
        code = string.Empty;
        secondsRemaining = TimeStepSeconds - (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % TimeStepSeconds);

        if (!TryDecodeBase32(secret, out var key) || key.Length == 0)
        {
            return false;
        }

        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / TimeStepSeconds;
        Span<byte> counterBytes = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
                     | ((hash[offset + 1] & 0xff) << 16)
                     | ((hash[offset + 2] & 0xff) << 8)
                     | (hash[offset + 3] & 0xff);

        code = (binary % 1_000_000).ToString("D6");
        return true;
    }

    private static bool TryDecodeBase32(string input, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(input)) return false;

        var clean = new string(input
            .Where(c => !char.IsWhiteSpace(c) && c != '=' && c != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());

        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in clean)
        {
            var value = c switch
            {
                >= 'A' and <= 'Z' => c - 'A',
                >= '2' and <= '7' => c - '2' + 26,
                _ => -1
            };

            if (value < 0) return false;

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
            }
        }

        bytes = output.ToArray();
        return bytes.Length > 0;
    }
}
