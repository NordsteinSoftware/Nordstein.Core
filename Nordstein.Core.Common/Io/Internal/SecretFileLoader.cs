namespace Nordstein.Core.Common.Io.Internal;

/// <summary>
/// Mode-checked secret-file loader. See <see cref="ISecretFileLoader"/>.
/// </summary>
internal sealed class SecretFileLoader : ISecretFileLoader
{
    private const UnixFileMode FileMode0600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode DirectoryMode0700 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public byte[] Load(string path)
    {
        if (!TryLoad(path, out byte[] bytes, out SecretFileRejection rejection))
        {
            throw new SecretFileException(rejection, path);
        }

        return bytes;
    }

    public bool TryLoad(string path, out byte[] bytes, out SecretFileRejection rejection)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        bytes = [];

        if (!File.Exists(path))
        {
            rejection = SecretFileRejection.Missing;
            return false;
        }

        if (new FileInfo(path).LinkTarget is not null)
        {
            rejection = SecretFileRejection.IsSymlink;
            return false;
        }

        if (!OperatingSystem.IsWindows() && !HasExpectedUnixModes(path))
        {
            rejection = SecretFileRejection.WrongMode;
            return false;
        }

        bytes = File.ReadAllBytes(path);
        rejection = SecretFileRejection.None;
        return true;
    }

    private static bool HasExpectedUnixModes(string path)
    {
        if (File.GetUnixFileMode(path) != FileMode0600)
        {
            return false;
        }

        string? parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (parent is not null && Directory.Exists(parent))
        {
            return File.GetUnixFileMode(parent) == DirectoryMode0700;
        }

        return true;
    }
}
