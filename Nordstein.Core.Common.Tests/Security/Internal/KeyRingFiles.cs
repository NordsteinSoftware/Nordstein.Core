using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nordstein.Core.Common.Tests.Security.Internal;

/// <summary>
/// Writes master key ring files by hand, independently of the code under test, so the tests pin the file format:
/// <c>{format, ringId, keys: [{version, createdAt, status, key, canary: {nonce, ciphertext}}]}</c>, the canary
/// being AES-256-GCM of <see cref="CanaryPlaintext"/> under the key with empty associated data.
/// </summary>
internal static class KeyRingFiles
{
    public const string FormatMarker = "nordstein-keyring-v1";
    public const string CanaryPlaintext = "nordstein-keyring-canary-v1";
    public const string FileName = "master-key-ring.json";

    public const UnixFileMode Mode0600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    public const UnixFileMode Mode0700 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Sets the Unix mode of <paramref name="path"/>; nothing on Windows, whose tests skip mode custody.
    /// </summary>
    public static void SetMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    /// <summary>
    /// The Unix mode of <paramref name="path"/>, or <see langword="null"/> on Windows.
    /// </summary>
    public static UnixFileMode? ModeOf(string path) => OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path);

    /// <summary>
    /// Creates <paramref name="path"/> (and its missing ancestors) with mode <c>0700</c> on Unix.
    /// </summary>
    public static string CreateKeyDirectory(string path)
    {
        string created = Directory.CreateDirectory(path).FullName;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(created, Mode0700);
        }

        return created;
    }

    /// <summary>
    /// Writes a ring with the given keys into <paramref name="keyDirectory"/> at mode <c>0600</c>.
    /// </summary>
    public static void Write(string keyDirectory, Guid ringId, params (int Version, string Status, byte[] Key)[] keys)
    {
        var document = new JsonObject
        {
            ["format"] = FormatMarker,
            ["ringId"] = ringId.ToString("D"),
            ["keys"] = new JsonArray([.. keys.Select(key => (JsonNode)Key(key.Version, key.Status, key.Key))]),
        };
        WriteText(keyDirectory, document.ToJsonString());
    }

    /// <summary>
    /// Writes <paramref name="text"/> as the ring file, mode <c>0600</c> on Unix.
    /// </summary>
    public static void WriteText(string keyDirectory, string text)
    {
        string path = Path.Combine(keyDirectory, FileName);
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, Mode0600);
        }
    }

    /// <summary>
    /// Applies <paramref name="edit"/> to the ring file's JSON; an overwrite keeps the file's mode.
    /// </summary>
    public static void Edit(string keyDirectory, Action<JsonObject> edit)
    {
        string path = Path.Combine(keyDirectory, FileName);
        JsonObject ring = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidOperationException("The ring is null.");
        edit(ring);
        File.WriteAllText(path, ring.ToJsonString());
    }

    /// <summary>
    /// The first key entry of a ring document.
    /// </summary>
    public static JsonObject FirstKey(JsonObject ring)
        => ring["keys"]?.AsArray()[0]?.AsObject() ?? throw new InvalidOperationException("The ring has no first key.");

    /// <summary>
    /// The canary of a key entry.
    /// </summary>
    public static JsonObject CanaryOf(JsonObject key)
        => key["canary"]?.AsObject() ?? throw new InvalidOperationException("The key has no canary.");

    /// <summary>
    /// The 32 key bytes of the entry with <paramref name="version"/> in the ring file.
    /// </summary>
    public static byte[] ReadKey(string keyDirectory, int version)
    {
        using JsonDocument ring = JsonDocument.Parse(File.ReadAllText(Path.Combine(keyDirectory, FileName)));
        JsonElement key = ring.RootElement.GetProperty("keys").EnumerateArray()
            .Single(entry => entry.GetProperty("version").GetInt32() == version);
        return key.GetProperty("key").GetBytesFromBase64();
    }

    /// <summary>
    /// One key entry with an authenticating canary over <paramref name="canaryText"/>.
    /// </summary>
    public static JsonObject Key(int version, string status, byte[] key, string canaryText = CanaryPlaintext)
    {
        (byte[] nonce, byte[] ciphertext) = Seal(key, Encoding.ASCII.GetBytes(canaryText), []);
        return new JsonObject
        {
            ["version"] = version,
            ["createdAt"] = "2026-08-19T12:00:00.0000000+00:00",
            ["status"] = status,
            ["key"] = Convert.ToBase64String(key),
            ["canary"] = new JsonObject
            {
                ["nonce"] = Convert.ToBase64String(nonce),
                ["ciphertext"] = Convert.ToBase64String(ciphertext),
            },
        };
    }

    /// <summary>
    /// AES-256-GCM with a fresh nonce: the nonce and <c>ciphertext ‖ tag</c>.
    /// </summary>
    public static (byte[] Nonce, byte[] Ciphertext) Seal(byte[] key, byte[] plaintext, byte[] associatedData)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        using (var gcm = new AesGcm(key, 16))
        {
            gcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        return (nonce, [.. ciphertext, .. tag]);
    }

    /// <summary>
    /// Reverses <see cref="Seal"/>.
    /// </summary>
    public static byte[] Open(byte[] key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertextWithTag, byte[] associatedData)
    {
        byte[] plaintext = new byte[ciphertextWithTag.Length - 16];
        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(nonce, ciphertextWithTag[..^16], ciphertextWithTag[^16..], plaintext, associatedData);
        return plaintext;
    }
}
