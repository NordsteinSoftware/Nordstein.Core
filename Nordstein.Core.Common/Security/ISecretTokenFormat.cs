namespace Nordstein.Core.Common.Security;

/// <summary>
/// The syntax of a bearer secret: a kind prefix chosen by the caller followed by <see cref="SecretBytes"/>
/// CSPRNG bytes as lowercase, unpadded RFC 4648 base32 (<see cref="SecretLength"/> characters, Core's
/// <c>ToBase32</c>), for example <c>acme_api_</c> followed by 52 characters.
/// </summary>
/// <remarks>
/// <para>
/// The prefix names the kind of secret (an API token, a session), so a secret of one kind is never taken
/// for another and secret scanners can recognise a leaked one. A prefix is 2 to <see cref="MaxPrefixLength"/>
/// characters: a lowercase ASCII letter, then lowercase ASCII letters, digits and underscores, ending with an
/// underscore. Every member refuses any other prefix with an <see cref="ArgumentException"/>: a prefix is the
/// caller's constant, so a bad one is a programming error, never input.
/// </para>
/// <para>
/// The format only spells and recognises tokens. It does not authenticate them: the caller stores a hash of
/// the whole token (<see cref="ISecretHasher"/>, never a password hash) and looks a presented token up by that
/// hash. A token is a credential: never log it, put it in an exception message or store it in the clear.
/// </para>
/// <para>Stateless and thread-safe; <see cref="Module"/> registers one instance per container.</para>
/// </remarks>
public interface ISecretTokenFormat
{
    /// <summary>
    /// The bytes of entropy in every token: 32 (256 bits).
    /// </summary>
    const int SecretBytes = 32;

    /// <summary>
    /// The base32 characters that spell the secret: 52.
    /// </summary>
    const int SecretLength = 52;

    /// <summary>
    /// The longest accepted prefix: 32 characters.
    /// </summary>
    const int MaxPrefixLength = 32;

    /// <summary>
    /// Draws <see cref="SecretBytes"/> fresh bytes from the system CSPRNG and composes the token. The secret
    /// buffer is zeroed before the method returns.
    /// </summary>
    /// <param name="prefix">The kind prefix.</param>
    /// <returns>The plaintext token: <paramref name="prefix"/> followed by <see cref="SecretLength"/> base32 characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> breaks the prefix rule.</exception>
    string Generate(string prefix);

    /// <summary>
    /// Composes the token for an explicit secret, for a caller that drew or derived the bytes itself.
    /// </summary>
    /// <param name="prefix">The kind prefix.</param>
    /// <param name="secret">Exactly <see cref="SecretBytes"/> bytes; the caller zeroes its buffer.</param>
    /// <returns>The plaintext token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prefix"/> breaks the prefix rule, or <paramref name="secret"/> is not
    /// <see cref="SecretBytes"/> long.
    /// </exception>
    string Compose(string prefix, ReadOnlySpan<byte> secret);

    /// <summary>
    /// Whether <paramref name="candidate"/> has exactly the shape of a token of this kind: the length, the
    /// prefix (ordinal) and the canonical base32 spelling of <see cref="SecretBytes"/> bytes, whose last
    /// character carries one bit and so is <c>a</c> or <c>q</c>. It checks the shape only, never whether the
    /// token was issued.
    /// </summary>
    /// <param name="prefix">The kind prefix.</param>
    /// <param name="candidate">The presented credential, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a well-formed token of this kind; <see langword="false"/> for anything else.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> breaks the prefix rule.</exception>
    bool Matches(string prefix, string? candidate);

    /// <summary>
    /// The last four characters of a token, which a listing or an audit record may show to tell tokens
    /// apart. Of a well-formed token they reveal 16 bits of the secret.
    /// </summary>
    /// <param name="token">A non-empty token.</param>
    /// <returns>The last four characters, or the whole value when it is four characters or shorter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty.</exception>
    string Last4Of(string token);
}
