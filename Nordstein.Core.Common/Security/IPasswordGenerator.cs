namespace Nordstein.Core.Common.Security;

/// <summary>
/// Generates passwords an operator hands out (an initial or a reset password): lowercase base32 characters
/// (<c>a</c>–<c>z</c>, <c>2</c>–<c>7</c>) drawn from the system CSPRNG, 5 bits each.
/// </summary>
/// <remarks>
/// <para>
/// The length is <see cref="DefaultLength"/> (100 bits), or <see cref="PasswordPolicyOptions.MinLength"/> when
/// that is longer, so a generated password always has a length <see cref="IPasswordPolicy"/> accepts. The
/// characters are ASCII, so normalization leaves the password unchanged. The policy's identity rule can
/// still refuse one, with a chance of 2^-100 for the default length; a caller that checks the policy draws
/// again.
/// </para>
/// <para>Stateless and thread-safe; <see cref="Module"/> registers one instance per container.</para>
/// </remarks>
public interface IPasswordGenerator
{
    /// <summary>
    /// The length of a generated password when the policy's minimum is not longer: 20 characters.
    /// </summary>
    const int DefaultLength = 20;

    /// <summary>
    /// Draws a fresh password.
    /// </summary>
    /// <returns>The password; the caller must never log it.</returns>
    string Generate();
}
