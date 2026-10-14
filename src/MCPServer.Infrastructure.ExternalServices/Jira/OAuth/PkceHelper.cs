using System.Security.Cryptography;
using System.Text;

namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

public static class PkceHelper
{
    public static string CreateVerifier() => RandomToken(32);

    public static string CreateS256Challenge(string verifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Constant-time check that <paramref name="verifier"/> matches an S256 <paramref name="challenge"/>.</summary>
    public static bool VerifyS256(string verifier, string challenge) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(CreateS256Challenge(verifier)),
            Encoding.ASCII.GetBytes(challenge));

    public static string RandomToken(int bytes) => Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
