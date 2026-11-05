using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace AccountServer;

/// <summary>Password hashing: PBKDF2-SHA256 with a random salt; the stored text carries its own iteration count.</summary>
public sealed class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private readonly int _iterations;
    private readonly Lazy<string> _dummy;

    public PasswordHasher(AccountOptions options)
    {
        _iterations = options.PasswordIterations;
        _dummy = new Lazy<string>(() => Hash("dummy-password-for-timing"));
    }

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, 32);
        return $"{Prefix}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out int iterations) || iterations < 1) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Spends the same time as a real check, so an unknown e-mail address cannot be told apart by the response time.</summary>
    public void BurnTime() => Verify("wrong", _dummy.Value);
}

public static class Tokens
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O/1/I/L: easy to type from an e-mail

    /// <summary>A long random secret for sessions and e-mail links.</summary>
    public static string New() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>A short code a player types into the game (password reset).</summary>
    public static string NewCode()
    {
        var sb = new StringBuilder(8);
        for (int i = 0; i < 8; i++) sb.Append(CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]);
        return sb.ToString();
    }

    public static string NormalizeCode(string code) => new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>Only the hash of a secret is stored, so a stolen database does not hand out working sessions.</summary>
    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}
