namespace ANTS;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

internal static class HarnessDigest
{
    public static string Sha256Hex(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        StringBuilder hex = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }
        return hex.ToString();
    }
}
