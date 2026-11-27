# Security

`Nordstein.Core.Common.Security` holds the product-agnostic building blocks of sign-in and credential
handling, and the master key ring that wraps a product's data keys and stored secrets. The cryptographic primitives they build on (AEAD, key wrap, password hashing) are described in
[`cryptography.md`](cryptography.md), secret-file custody in [`io.md`](io.md). Every service here is a
singleton registered in `Nordstein.Core.Common.Module`; none holds an entity or touches storage.

## The one rule this area lives by

Core never knows what an account, a user, a session or a stored record is. The caller keeps every piece of
state (failure counts, lock ends, token hashes, wrapped values) beside its own records and hands Core only
values: a count, an instant, a prefix, the strings that identify an account, the bytes that bind a wrapped
value to its slot. Core decides nothing about persistence except the master key ring's own file.

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

## Master key ring

`IMasterKeyRing` holds the process's master keys, loaded once from a custody-checked file, and wraps and
unwraps values (data keys, stored secrets) with them; master-key bytes never cross the interface.
`IMasterKeyRingInitializer` creates the file once. Both resolve only where the host registered
`MasterKeyRingOptions`: `KeyDirectory`, `FileName` (`master-key-ring.json`) and `ExcludedRoots`, the
directories the ring must never be inside (anything backed up, served or shared).

### The file

```json
{
  "format": "nordstein-keyring-v1",
  "ringId": "3f2b…-…",
  "keys": [
    { "version": 1, "createdAt": "2026-…+00:00", "status": "active", "key": "<32 bytes, base64>",
      "canary": { "nonce": "<12 bytes, base64>", "ciphertext": "<base64>" } }
  ]
}
```

The canary is AES-256-GCM of `nordstein-keyring-canary-v1` under the key with empty associated data, so a
wrong or edited key is detected when the ring loads, not at the first unwrap. No `Wrap` produces empty
associated data, so a canary never unwraps as a value nor a value as a canary.

### Load: typed and fail-closed

Resolving does no I/O. The first access to any member loads exactly once (also under concurrent first
accesses) and throws `MasterKeyRingException` with a `MasterKeyRingFailure` from that access and every later
one; a repaired file is read only by a new process, and there is never a fallback to a new key. Touch
`RingId` at startup so a bad ring stops the host.

| Failure | When |
|---|---|
| `PathInExcludedRoot` | the configured ring path may lie inside an excluded root (`IPathContainment`: links followed in both readings, case and normalization ignored) |
| `FileRejected` | a path cannot be resolved, or `ISecretFileLoader` refuses (missing, a link, on Unix not `0600` in a `0700` directory), or the read fails; the cause is kept |
| `Malformed` | not JSON, an unknown or duplicated member, a wrong marker, a ring id that is missing, empty or not a 36-character GUID, a null entry, a version below 1 or used twice, a status other than `active` or `decrypt-only`, missing bytes, not base64, a canary of the wrong shape |
| `WrongKeyLength` | a key not exactly 32 bytes |
| `CanaryFailed` | a canary that does not authenticate or decrypts to another text, for any key, active or not |
| `NoActiveKey`, `MultipleActiveKeys` | not exactly one `active` key |

The file's bytes and every decoded key are zeroed on every path; the loaded keys are copied into pinned
arrays so the collector never leaves a moved copy.

### Wrap and unwrap

`Wrap(plaintext, associatedData)` is `IAeadKeyWrap` under the active key, fresh nonce; the bound associated
data is the caller's bytes followed by the key version as a big-endian 32-bit integer, so a record relabelled
to another version, or moved to another slot, fails. Start the caller's bytes with a constant naming the
kind of value (`acme-setting-v1`) and then identify the slot. `Unwrap` uses the version the record names,
active or `decrypt-only`: an unknown version is `UnknownKeyVersion`; a nonce not 12 bytes, a short record, a
wrong binding or a tampered record are all `UnwrapFailed`, deliberately not told apart. The returned buffer
is the caller's to zero.

### Initialization: create once, durably

`Precheck()` runs every refusal without writing: an excluded root (`PathInExcludedRoot`, `FileRejected`), a
file or link at the ring path or a non-empty key directory (`AlreadyInitialized`). `InitializeAsync(ringId)`
runs it again, then:

1. creates the key directory `0700` when missing (its parent must exist; Core creates one level) and flushes
   the parent, or sets an existing empty one to `0700`;
2. draws a 32-byte key from `RandomNumberGenerator` and its canary, and writes the document from a buffer that
   never grows, through an unbuffered stream opened `CreateNew` (`O_CREAT | O_EXCL`), mode `0600` set on the
   open handle whatever the umask; a ring that appeared since the check is `AlreadyInitialized` with the
   open's `IOException` inside and is never touched;
3. flushes the file to disk, re-runs the excluded-root check (a key directory swapped for a link into an
   excluded root is refused, and the ring found there deleted only if its ring id is the one just written),
   and flushes the key directory (`IDirectorySync`).

Any failure after the open deletes the ring this call wrote, and the key directory when this call created it.
`Remove(created)` undoes a completed call for a host whose later first-run step failed, after checking that
the file still is that ring (`InvalidOperationException` otherwise, nothing deleted).

Why not `IDurableFilePublisher`: its create-without-replace is a managed check followed by a rename that
would overwrite a ring published in between (see [`io.md`](io.md)); a replaced ring loses every value wrapped
under it. `CreateNew` is atomic; the price is that a crash during the write can leave a partial ring under
the final name, which fails to load and blocks a second initialization until an operator deletes it (nothing
was ever wrapped under it).

### Non-goals and known limits

- **Rotation is an explicit non-goal.** Core never adds, activates, demotes or retires a key and never
  re-wraps. The format already holds `decrypt-only` keys beside the active one, so a later Core version can
  add rotation without a format change. No key-management service or hardware module is supported.
- **Windows.** Production custody targets Linux and containers. On Windows no mode is set or checked; the file
  inherits the directory's access control list.
- **Owner.** The file owner is not compared with the process user ([`io.md`](io.md)); mode `0600` restricts
  reads to the owner.
- **Zeroization.** The loaded keys stay in pinned memory until the process ends; a memory dump or swapped
  page exposes them.
- **Check then read.** `ISecretFileLoader` checks the file and then reads it; an entry swapped for a link in
  between is read. The `0700` directory leaves that to the owner alone.
- **Placement.** Not detected: a link inside an excluded root that points at the key directory (a backup that
  follows links copies the ring), bind mounts, hard links. The check is point-in-time; it runs at every load
  and after the initialization write.
- **One load per process.** A ring edited on disk is seen only by a new process.
- The ring id is not secret; binding other durable artefacts to it is the host's job.

## Review focus (Standard of Care #3)

For the master key ring the adversarial pass hunts for: a load path that falls back to a new key or returns
a partially verified ring; a failure that is not typed or not cached; key bytes in a message, a log line, a
`ToString`, a string or an unzeroed buffer; a canary that is not checked for every key; a nonce not from the
CSPRNG or a binding without the version; an initialization that can overwrite, or delete, a ring it did not
write; a mode applied by path where it must be on the handle; and an excluded-root check run on a normalized
instead of the configured path.

For the credential primitives the adversarial pass hunts for: a lockout count that can be negative,
overflow or loop with the count; a lock end from anything but `IClock`; a token secret not from the
CSPRNG or not zeroed; a token shape check that accepts a second spelling; a policy check on a
non-normalized form; a password, identity or token in an exception message, a log line or `ToString`;
and any refusal path that returns before one key derivation.
