namespace Nordstein.Core.Common.Security;

/// <summary>
/// Keeps a refused sign-in as slow as a checked one: a sign-in that refuses without a stored hash to verify
/// (an unknown account, an account without a password, a running lock, a stored hash the hasher rejects)
/// calls <see cref="Verify"/> with the presented password, so every refusal costs one key derivation and its
/// timing does not tell an unknown account from a wrong password.
/// </summary>
/// <remarks>
/// <para>
/// The dummy hash is <see cref="IPasswordHasher.Hash"/> of 32 CSPRNG bytes nobody knows, computed under the
/// hasher's current parameters when the single instance is constructed (about 0.2 seconds at the current
/// parameters): resolve it during startup so no sign-in pays for it. A stored hash with other parameters
/// than the current ones costs a different time to verify; the rehash signal of
/// <see cref="IPasswordHasher.Verify"/> moves accounts to the current parameters at their next sign-in.
/// </para>
/// <para>Thread-safe; <see cref="Module"/> registers one instance per container.</para>
/// </remarks>
public interface IDummyPasswordVerifier
{
    /// <summary>
    /// Runs exactly one <see cref="IPasswordHasher.Verify"/> of <paramref name="password"/> against the dummy
    /// hash and discards the result, which is never a success to act on. A <see langword="null"/> password,
    /// or one with an unpaired surrogate (for which the hasher would skip the derivation), verifies the empty
    /// string instead, so the derivation always runs.
    /// </summary>
    /// <param name="password">The presented password, in the form a real check would verify, or <see langword="null"/>.</param>
    void Verify(string? password);
}
