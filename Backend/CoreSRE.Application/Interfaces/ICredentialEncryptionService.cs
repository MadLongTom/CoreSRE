namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 凭据加密服务接口。使用 ASP.NET Core Data Protection API 进行加密/解密。
/// Application 层定义接口，Infrastructure 层提供实现。
/// </summary>
public interface ICredentialEncryptionService
{
    /// <summary>
    /// 加密明文凭据。
    /// </summary>
    /// <param name="plaintext">明文凭据</param>
    /// <returns>Base64 编码的密文</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// 解密已加密的凭据。
    /// </summary>
    /// <param name="ciphertext">Base64 编码的密文</param>
    /// <returns>明文凭据</returns>
    string Decrypt(string ciphertext);

    /// <summary>
    /// 对密文进行遮盖处理，仅显示最后 N 个字符。
    /// </summary>
    /// <param name="ciphertext">Base64 编码的密文</param>
    /// <param name="visibleChars">末尾可见字符数</param>
    /// <returns>遮盖后的字符串，如 "****abcd"</returns>
    string Mask(string ciphertext, int visibleChars = 4);
}
