namespace Nordstein.Core.Common.Io;

/// <summary>
/// Loads the bytes of a secret file (a key file, a token file) only if the file passes custody
/// checks, so a mis-permissioned or link-swapped secret is refused rather than trusted.
/// </summary>
/// <remarks>
/// <para>
/// The checks are: the file exists and is not a symbolic link, and — on Unix — has mode
/// <c>0600</c> with a parent directory of mode <c>0700</c>. On Windows those Unix permission bits do
/// not apply and are not evaluated; existence and symlink refusal still hold. Owner-versus-process
/// verification is not performed here — mode <c>0600</c> already restricts reads to the owner — and
/// remains a possible later addition.
/// </para>
/// <para>Implementations are thread-safe and may be shared as singletons.</para>
/// </remarks>
public interface ISecretFileLoader
{
    /// <summary>
    /// Loads the bytes of the file at <paramref name="path"/>, throwing if any custody check fails.
    /// </summary>
    /// <param name="path">The path of the secret file.</param>
    /// <returns>The file's bytes.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="SecretFileException">A custody check failed; the bytes are not returned.</exception>
    byte[] Load(string path);

    /// <summary>
    /// Attempts to load the bytes of the file at <paramref name="path"/> without throwing on a
    /// failed custody check.
    /// </summary>
    /// <param name="path">The path of the secret file.</param>
    /// <param name="bytes">On success, the file's bytes; otherwise an empty array.</param>
    /// <param name="rejection">
    /// On failure, the check that failed; <see cref="SecretFileRejection.None"/> on success.
    /// </param>
    /// <returns><c>true</c> if the file passed every check and was loaded; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    bool TryLoad(string path, out byte[] bytes, out SecretFileRejection rejection);
}
