using System.Security.Cryptography;
using System.Text;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Crypto;

public sealed class ModernCryptographyDemo : IDemo
{
    private const int AesTagSize = 16;

    public DemoMetadata Metadata { get; } = new(
        "crypto.modern-primitives",
        "crypto",
        "现代密码学：哈希、认证加密与数字签名",
        "使用安全随机数、SHA-256/HMAC、PBKDF2、AES-GCM、RSA-PSS 和 ECDSA，区分摘要、认证、加密与签名的安全属性。",
        [21],
        ["RandomNumberGenerator", "SHA256", "HMACSHA256", "PBKDF2", "AesGcm", "RSA-PSS", "ECDSA"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var message = Encoding.UTF8.GetBytes("C# 14 / .NET 10 密码学边界");
        var digest = SHA256.HashData(message);
        var hmacKey = RandomNumberGenerator.GetBytes(32);
        var authenticationCode = HMACSHA256.HashData(hmacKey, message);
        var authenticationCodeCopy = HMACSHA256.HashData(hmacKey, message);

        var salt = RandomNumberGenerator.GetBytes(16);
        var encryptionKey = Rfc2898DeriveBytes.Pbkdf2(
            "learning-only-password",
            salt,
            iterations: 100_000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var associatedData = Encoding.UTF8.GetBytes("schema=v1");
        var ciphertext = new byte[message.Length];
        var tag = new byte[AesTagSize];
        var plaintext = new byte[message.Length];
        var rejectedPlaintext = new byte[message.Length];

        try
        {
            using (var aes = new AesGcm(encryptionKey, AesTagSize))
            {
                aes.Encrypt(nonce, message, ciphertext, tag, associatedData);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            }

            var tamperedTag = tag.ToArray();
            tamperedTag[0] ^= 0x01;
            var tamperingRejected = false;
            try
            {
                using var aes = new AesGcm(encryptionKey, AesTagSize);
                aes.Decrypt(nonce, ciphertext, tamperedTag, rejectedPlaintext, associatedData);
            }
            catch (AuthenticationTagMismatchException)
            {
                tamperingRejected = true;
            }

            using var rsa = RSA.Create(2048);
            var rsaSignature = rsa.SignData(
                message,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            var rsaValid = rsa.VerifyData(
                message,
                rsaSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);

            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var ecdsaSignature = ecdsa.SignData(message, HashAlgorithmName.SHA256);
            var ecdsaValid = ecdsa.VerifyData(message, ecdsaSignature, HashAlgorithmName.SHA256);

            context.WriteProperty("SHA-256", Convert.ToHexString(digest));
            context.WriteProperty("HMAC fixed-time equal", CryptographicOperations.FixedTimeEquals(authenticationCode, authenticationCodeCopy));
            context.WriteProperty("AES-GCM ciphertext/tag", $"{ciphertext.Length}/{tag.Length} bytes");
            context.WriteProperty("AES-GCM round trip", Encoding.UTF8.GetString(plaintext));
            context.WriteProperty("tampering rejected", tamperingRejected);
            context.WriteProperty("RSA-PSS / ECDSA verified", $"{rsaValid} / {ecdsaValid}");

            DemoAssert.True(digest.Length == 32, "SHA-256 摘要应为 32 字节");
            DemoAssert.True(
                CryptographicOperations.FixedTimeEquals(authenticationCode, authenticationCodeCopy),
                "同一密钥和消息应生成相同 HMAC，并以固定时间比较");
            DemoAssert.SequenceEqual(message, plaintext, "AES-GCM 应无损恢复明文");
            DemoAssert.True(tamperingRejected, "AES-GCM 必须拒绝被篡改的认证标签");
            DemoAssert.True(rsaValid && ecdsaValid, "RSA-PSS 与 ECDSA 签名应通过公钥验证");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmacKey);
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(rejectedPlaintext);
        }

        context.WriteLine("  教学示例只组合平台密码学原语；生产系统还需要密钥托管、轮换、协议设计与威胁建模。不要自创密码算法。");
        return ValueTask.CompletedTask;
    }
}
