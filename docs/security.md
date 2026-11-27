# Security

`Nordstein.Core.Common.Security` holds the product-agnostic building blocks of sign-in and credential
handling. The cryptographic primitives they build on (AEAD, key wrap, password hashing) are described in
[`cryptography.md`](cryptography.md), secret-file custody in [`io.md`](io.md). Every service here is a
singleton registered in `Nordstein.Core.Common.Module`; none holds an entity or touches storage.

## The one rule this area lives by

Core never knows what an account, a user or a session is. The caller keeps every piece of state
(failure counts, lock ends, token hashes) beside its own credential and hands Core only values: a count,
an instant, a prefix, the strings that identify an account. Core decides nothing about persistence.

## Credentials

### `ILoginLockout` — failed-sign-in backoff

From `LoginLockoutOptions.Threshold` consecutive failures (5) an account is locked for
`min(MaxLockDuration, InitialLockDuration × 2^(failures − Threshold))`: by default 5 failures lock for
1 minute, 6 for 2, 7 for 4, 8 for 8, 9 and more for 15. `LockFor(count)` is the pure backoff,
`LockedUntilAfter(count)` adds it to the container's `IClock` now (saturating at
`DateTimeOffset.MaxValue`), `IsLocked(lockedUntil)` is `lockedUntil > now`.

- **The caller counts, atomically.** One statement that adds 1 and returns the new count (`UPDATE … SET
  failures = failures + 1 … RETURNING failures`), then the lock end of that count. A read-modify-write
  lets concurrent failures overwrite each other's increment, which is exactly what a guessing attack
  does. A success resets the count and clears the end.
- **Bounded work.** The doubling stops as soon as the next step would pass the maximum, so a huge count
  costs at most 63 steps and never overflows.
- **Fail closed.** A negative count (a corrupted row) throws instead of reading as "no lock".
- **Clock.** Ends are absolute instants. A clock that steps back keeps a lock longer; none unlocks
  before its stored instant passes.
- Options are validated when the lockout is resolved: a threshold below 1, a non-positive initial lock
  or a maximum below the initial lock fails resolution with a `ValidationException`.

### `ISecretTokenFormat` — prefixed bearer secrets

A token is a kind prefix plus 32 CSPRNG bytes as lowercase unpadded base32 (52 characters). The prefix
is the caller's constant (`acme_api_`, `acme_ses_`): 2 to 32 characters, a lowercase ASCII letter, then
lowercase letters, digits and underscores, ending with `_`. An invalid prefix is an `ArgumentException`
from every member, because it is a programming error, not input.

- `Generate(prefix)` draws the bytes from `RandomNumberGenerator` and zeroes them; `Compose(prefix,
  secret)` takes exactly 32 caller-supplied bytes.
- `Matches(prefix, candidate)` checks the shape only: exact length, ordinal prefix, base32 alphabet, and
  the canonical last character (`a` or `q`, since it carries one bit). Two prefixes of different length
  never match each other's tokens.
- `Last4Of(token)` is the display suffix for listings (16 bits of the secret).
- The format authenticates nothing: the caller stores a hash of the whole token (`ISecretHasher`, a
  fast hash is right for 256 random bits) and looks a presented token up by it.

### `IPasswordPolicy` — NIST SP 800-63B

`Normalize(password)` returns the NFKC form; check, hash and verify that form, never the raw one. A
string with an unpaired surrogate has none: `ArgumentException` with a fixed message.
`Check(normalized, identities)` throws `PasswordPolicyException` for the first broken rule:

| Rule | Refused when |
|---|---|
| `TooShort` (1) | fewer code points (`Rune`s) than `PasswordPolicyOptions.MinLength` (12, 0 to 256) |
| `TooLong` (2) | more than 256 code points |
| `MatchesIdentity` (3) | equal, after NFKC and `ToLowerInvariant` on both sides, to one of the identifying values the caller passes (a user name, an address); containing one is allowed |

- No composition rules, expiry, common-password list or breach lookup.
- `Check` refuses a password that is not in NFKC form (`ArgumentException`): it catches a caller that
  skipped `Normalize`.
- A `PasswordPolicyException` message is fixed per rule, names the minimum for `TooShort`, and never
  holds the password or an identifying value. The rule numbers are stable.
- A minimum below 12 is allowed (a demonstration host with empty passwords); a host that must not run
  with one in production enforces its own floor before it registers the options.

### `IDummyPasswordVerifier` — equal timing for refusals

A sign-in that refuses without a stored hash (unknown account, no password set, a running lock, a stored
hash the hasher rejects) calls `Verify(presentedPassword)`, so every refusal costs one PBKDF2 derivation.
The dummy hash is `IPasswordHasher.Hash` of 32 unknown CSPRNG bytes, computed when the singleton is
constructed: resolve it at startup. A `null` password or one with an unpaired surrogate verifies the
empty string, because the hasher would skip the derivation for it. The result is discarded; the method
returns nothing to act on.

### `IPasswordGenerator`

`Generate()` returns lowercase base32 from `RandomNumberGenerator.GetString`: 20 characters (100 bits),
or `PasswordPolicyOptions.MinLength` when longer, so the length always passes the policy. The policy's
identity rule can still refuse one (chance 2^-100); a caller that checks draws again.

## Review focus (Standard of Care #3)

For the credential primitives the adversarial pass hunts for: a lockout count that can be negative,
overflow or loop with the count; a lock end from anything but `IClock`; a token secret not from the
CSPRNG or not zeroed; a token shape check that accepts a second spelling; a policy check on a
non-normalized form; a password, identity or token in an exception message, a log line or `ToString`;
and any refusal path that returns before one key derivation.
