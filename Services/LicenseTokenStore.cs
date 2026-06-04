using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SandboxTimeline;

/// <summary>
/// Persists premium activation tokens with AES-256-GCM encryption (machine-bound key).
/// </summary>
internal sealed class LicenseTokenStore
{
    private const string LicenseFileName = "premium.license.enc";
    private static readonly byte[] FileMagic = "STLT"u8.ToArray();
    private const byte FileVersion = 1;

    private readonly string _filePath;

    public LicenseTokenStore()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SandboxTimeline");
        Directory.CreateDirectory(appData);
        _filePath = Path.Combine(appData, LicenseFileName);
    }

    public bool TryLoad(out StoredLicenseToken? token)
    {
        token = null;
        if (!File.Exists(_filePath))
        {
            return false;
        }

        try
        {
            var envelope = File.ReadAllBytes(_filePath);
            var json = Decrypt(envelope);
            token = JsonSerializer.Deserialize<StoredLicenseToken>(json);
            return token != null;
        }
        catch
        {
            return false;
        }
    }

    public void Save(StoredLicenseToken token)
    {
        var json = JsonSerializer.Serialize(token);
        var envelope = Encrypt(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(_filePath, envelope);
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private static byte[] Encrypt(byte[] plainBytes)
    {
        var key = DeriveAes256Key();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        using var output = new MemoryStream();
        output.Write(FileMagic);
        output.WriteByte(FileVersion);
        output.Write(nonce);
        output.Write(tag);
        output.Write(cipher);
        return output.ToArray();
    }

    private static string Decrypt(byte[] envelope)
    {
        if (envelope.Length < FileMagic.Length + 1 + 12 + 16)
        {
            throw new CryptographicException("License envelope is too short.");
        }

        for (var i = 0; i < FileMagic.Length; i++)
        {
            if (envelope[i] != FileMagic[i])
            {
                throw new CryptographicException("License envelope magic mismatch.");
            }
        }

        var offset = FileMagic.Length;
        var version = envelope[offset++];
        if (version != FileVersion)
        {
            throw new CryptographicException($"Unsupported license file version: {version}.");
        }

        var nonce = envelope.AsSpan(offset, 12);
        offset += 12;
        var tag = envelope.AsSpan(offset, 16);
        offset += 16;
        var cipher = envelope.AsSpan(offset);

        var key = DeriveAes256Key();
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveAes256Key()
    {
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes("SandboxTimeline.LicenseToken.v1"));
        var passphrase = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion.VersionString}";
        return Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);
    }
}
