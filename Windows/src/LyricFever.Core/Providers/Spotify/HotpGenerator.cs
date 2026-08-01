using System.Security.Cryptography;
using System.Text;

namespace LyricFever.Core.Providers.Spotify;

/// <summary>
/// HOTP（HMAC-SHA1 计数器型 OTP），对应 macOS 版 SwiftOTP HOTP 的用法。
/// </summary>
public static class HotpGenerator
{
    /// <summary>
    /// 由 UTF-8 字节 key 与 8 字节大端 counter 生成 6 位数字 OTP。
    /// （macOS 版流程：secret 数字串 UTF8 字节 → Base32 编码 → 解码 —— 恒等变换，直接作为 HMAC key。）
    /// </summary>
    public static string Generate(byte[] key, ulong counter, int digits = 6)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        byte[] hash;
        using (var hmac = new HMACSHA1(key))
        {
            hash = hmac.ComputeHash(counterBytes);
        }

        // dynamic truncation
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];
        var otp = binary % (long)Math.Pow(10, digits);
        return otp.ToString().PadLeft(digits, '0');
    }

    /// <summary>生成 OTP 的便捷入口（counter = 秒级时间戳 / 30）。</summary>
    public static string GenerateForTime(byte[] key, long unixSeconds)
    {
        var counter = (ulong)(unixSeconds / 30);
        return Generate(key, counter);
    }
}
