using CoreSRE.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 凭据加密服务实现。使用 ASP.NET Core Data Protection API 进行加密/解密。
/// </summary>
public class CredentialEncryptionService : ICredentialEncryptionService
{
    private const string Purpose = "CoreSRE.Infrastructure.CredentialEncryption.v1";
    private readonly IDataProtector _protector;

    public CredentialEncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc/>
    public string Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext, nameof(plaintext));
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc/>
    public string Decrypt(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext, nameof(ciphertext));
        return _protector.Unprotect(ciphertext);
    }

    /// <inheritdoc/>
    public string Mask(string ciphertext, int visibleChars = 4)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        try
        {
            var plaintext = Decrypt(ciphertext);
            if (plaintext.Length <= visibleChars)
                return new string('*', plaintext.Length);

            return new string('*', plaintext.Length - visibleChars) + plaintext[^visibleChars..];
        }
        catch
        {
            // If decryption fails, mask the ciphertext itself
            if (ciphertext.Length <= visibleChars)
                return new string('*', ciphertext.Length);

            return new string('*', ciphertext.Length - visibleChars) + ciphertext[^visibleChars..];
        }
    }
}
