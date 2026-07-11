using System.Security.Cryptography;
using System.Text;

namespace Rushframe.Desktop.Services;

public static class SecretProtectionService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Rushframe.Editor.Settings.v1");

    public static string Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(value.Trim());
        return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(protectedValue);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
