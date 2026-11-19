using System.Security.Cryptography;
using System.Text;

namespace Kaon.Helpers;

public static class InviteTokens
{
    // 256 random bits: impossible to guess
    public static (string Token, string Hash) New()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
