using System.IO;
using System.Security.Cryptography;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// 敏感凭据存储（对应 macOS Keychain）：DPAPI 加密后落盘到 %APPDATA%\LyricFever\credentials\。
/// </summary>
public static class CredentialStore
{
    private static readonly string CredentialDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LyricFever", "credentials");

    public static string? Get(string name)
    {
        try
        {
            var path = Path.Combine(CredentialDir, name + ".bin");
            if (!File.Exists(path)) return null;
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Credential] read failed: {ex.Message}");
            return null;
        }
    }

    public static void Set(string name, string value)
    {
        try
        {
            Directory.CreateDirectory(CredentialDir);
            var encrypted = ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(CredentialDir, name + ".bin"), encrypted);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Credential] write failed: {ex.Message}");
        }
    }

    public static void Delete(string name)
    {
        try
        {
            var path = Path.Combine(CredentialDir, name + ".bin");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* ignore */ }
    }
}
