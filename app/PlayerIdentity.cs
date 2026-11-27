using System.Security.Cryptography;
using System.Text;

namespace Ultima;

internal static class PlayerIdentity
{
    public static bool IsValid(string? value)
    {
        return value is { Length: 36 } && Guid.TryParseExact(value, "D", out var uuid) &&
            uuid.Version == 4 && value[19] is ('8' or '9' or 'a' or 'b' or 'A' or 'B');
    }

    // Legacy API clients can resume a stable identity with their existing high-entropy profile key.
    public static string FromProfileKey(string key)
    {
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes("ultima/player/v1:" + key));
        digest[6] = (byte)((digest[6] & 0x0f) | 0x40);
        digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
        return new Guid(digest.AsSpan(0, 16), bigEndian: true).ToString("D");
    }
}
