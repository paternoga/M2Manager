using System.Security.Cryptography;
using System.Text;

namespace M2Manager.Api.Services;

/// <summary>
/// PBKDF2-SHA256 w formacie „pbkdf2$iteracje$saltBase64$hashBase64”.
/// Hash generujesz poleceniem: dotnet run --project M2Manager.Api -- hash-password "twoje-haslo".
/// </summary>
public static class PasswordHasher
{
    private const int DefaultIterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Prefix = "pbkdf2";

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Derive(password, salt, iterations);

        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    /// <summary>Porównanie w stałym czasie — bez wycieku informacji przez czas odpowiedzi.</summary>
    public static bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Porównanie haseł jawnych (tryb awaryjny/lokalny) — również w stałym czasie.</summary>
    public static bool VerifyPlainText(string password, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password ?? string.Empty),
            Encoding.UTF8.GetBytes(expected ?? string.Empty));

    private static byte[] Derive(string password, byte[] salt, int iterations, int keySize = KeySize) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            keySize);
}
