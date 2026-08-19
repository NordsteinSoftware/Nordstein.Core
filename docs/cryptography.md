# Cryptography

`Nordstein.Core.Common.Cryptography` provides two product-agnostic AEAD primitives built on the
BCL only (`System.Security.Cryptography.AesGcm` and `HKDF`): a chunked stream codec and an AEAD key
wrap. Both are registered as singletons in `Nordstein.Core.Common.Module`.

## The one rule this area lives by

Core knows nothing about any product's on-disk format. Everything product-specific — a magic
marker, a format version, audience or tenant identifiers, and even the salt's placement — is
assembled by the **caller** and handed to the codec as opaque `associatedData` (and, for the stream
codec, an opaque `salt` and `info`). The codec authenticates whatever bytes it is given; it never
frames a header of its own. This is the "opaque bytes handed in" seam shape from
[`architecture.md`](architecture.md).

## CSPRNG, never `IRandom`

Nonces and salts here come from `System.Security.Cryptography.RandomNumberGenerator` only. `IRandom`
(`Random/`) is a seeded, deterministic generator for test and demo data — a build-enforced test in
`Nordstein.Core.Common.Tests` fails if a production type takes an `IRandom` dependency. Never route
key material, nonces, or salts through it.

## `IAeadStreamCodec` — chunked AES-256-GCM STREAM

Encrypts an arbitrarily large stream as a sequence of AES-256-GCM chunks so neither direction buffers
the whole payload. AES-GCM's BCL surface is single-shot span-based (there is no streaming AEAD API,
and one call is capped near 2 GiB), which is *why* chunking is mandatory rather than optional.

The construction:

- **Per-message subkey.** `subkey = HKDF-SHA256(ikm = key, salt, info)`. Because a fresh random salt
  makes the subkey unique per message, the per-chunk nonce is a plain counter plus a final-block
  flag rather than a random value — there is no birthday bound to reason about across a corpus.
- **Nonce.** 12 bytes: the high 64 bits are zero; the low 32 bits are a big-endian word whose top
  bit is the final-chunk flag and whose remaining 31 bits are the chunk index. The chunk count is
  bounded to `2^31 - 1`; exceeding it throws `ChunkCountExceededException` rather than letting the
  nonce wrap.
- **Records.** Each chunk is `ciphertext ‖ tag(16)`. A full chunk carries exactly `ChunkSize`
  plaintext bytes; the final chunk carries `1..ChunkSize` (or zero for an empty payload). Every
  chunk binds `associatedData` into its tag.
- **Reorder / truncation.** Reordering or duplicating a chunk changes the nonce and fails the tag.
  Truncation is told apart from tampering: when the last present record fails to authenticate as the
  final chunk but *does* authenticate as a non-final chunk, the real final chunk is missing, so the
  reader throws `CiphertextTruncatedException`; otherwise it throws
  `AeadAuthenticationFailedException`. No plaintext is released for a chunk that has not
  authenticated.

### The salt is a required input, not generated inside `Encrypt`

Because the caller's own header both *contains* the salt and *is* the associated data,
the salt must exist before the header is sealed. So `EncryptAsync` takes the salt as a required
parameter; `NewSalt()` is the convenience the caller calls first. Generating-and-returning the salt
inside `Encrypt` would be unsound — the header would already be built when the salt appeared.

### Verify-without-retaining (the caller's integrity pass)

To check a stored payload against a known plaintext hash and length without keeping the plaintext,
read the decrypt stream through a hashing/counting sink and discard the bytes:

```csharp
await using Stream plain = codec.OpenRead(ciphertext, key, salt, info, header);
using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
long length = 0;
byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
int read;
while ((read = await plain.ReadAsync(buffer)) > 0)
{
    hash.AppendData(buffer, 0, read);
    length += read;
}
// Reaching here means every chunk authenticated AND the final flag was seen.
```

## `IAeadKeyWrap` — AES-256-GCM key wrap with AAD

Wraps a data key under a 32-byte key-encryption key with AES-256-GCM and a caller-supplied
associated data. AES-GCM is used rather than the standard RFC 3394/5649 AES key-wrap specifically
because AES key-wrap carries **no associated data**: binding a slot- or context-identifying value
means a wrapped key moved to a different slot fails to unwrap. A fresh CSPRNG nonce is generated per
wrap. Core defines no on-disk layout for the `WrappedKey` (nonce + ciphertext-with-tag) — the caller
stores the two fields however it likes.

## Review focus (Standard of Care #3)

When changing this area, the adversarial pass hunts specifically for: nonce reuse under one subkey;
associated data or the final flag not bound into a chunk's tag; a truncation that is reported as
success; the chunk counter wrapping; any code path that releases unauthenticated plaintext; and any
use of a non-CSPRNG source for a nonce or salt.
