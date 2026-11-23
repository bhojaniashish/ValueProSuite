using System.Security.Cryptography;

namespace ValuPro.Infrastructure.Identity;

/// <summary>Random, easy-to-read passwords handed to a user once by an admin.</summary>
public static class TemporaryPassword
{
    // No look-alikes (0/O, 1/l/I) so it can be read out over the phone.
    private const string Upper = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghjkmnpqrstuvwxyz";
    private const string Digits = "23456789";

    public static string Generate(int length = 12)
    {
        var all = Upper + Lower + Digits;
        var chars = new char[length];
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        for (var i = 3; i < length; i++) chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
