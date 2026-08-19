# IO

`Nordstein.Core.Common.Io` provides two product-agnostic filesystem primitives: durable atomic file
publishing and mode-checked secret-file loading. Both are registered as singletons in
`Nordstein.Core.Common.Module`.

## `IDurableFilePublisher` — stage, flush, publish, fsync

Writes a file so that a crash leaves either the old state or the fully written new file, never a
torn one:

1. `BeginWrite(destinationDirectory)` creates a staging file **inside that directory** (a
   `.{guid}.tmp` sibling), so publishing is an intra-volume rename rather than a cross-device copy.
   Same-volume staging is a correctness requirement, not an optimization — this is why the publisher
   does not reuse `ITempDirectory`, which stages under the system temp directory.
2. The caller writes content through `IFileWriteHandle.Content`.
3. `PublishAsync(destinationPath)` flushes the content to disk (`FileStream.Flush(flushToDisk: true)`),
   atomically publishes with create-without-replace (`File.Move` without overwrite, which throws if
   the destination exists — surfaced as `DestinationAlreadyExistsException`), then flushes the
   containing directory. `PublishReplacingAsync` is the same but replaces an existing destination.
4. Disposing the handle without publishing aborts the write and deletes the staging file
   (best-effort); a leaked staging file is recoverable and never a final artifact.

### The one native interop in Core

No public BCL API flushes a *directory*, yet without that flush a crash can lose the rename even
after the file's data is durable — you get a durable file at no name. So this is the single place
Core uses native interop: a source-generated `LibraryImport` of `open`/`fsync`/`close` on libc
(`Io/Internal/NativeFileApi.cs`), opening the directory read-only and `fsync`-ing it. It is a no-op
on Windows, where NTFS metadata journaling and the rename semantics make a directory handle
unnecessary. The interop is struct-free (no `stat`) and adds no package dependency; it uses
source-generated `LibraryImport`, whose generated marshalling stub requires `AllowUnsafeBlocks` on
the assembly (there is no hand-written `unsafe` code). This is a deliberate, reviewed exception to
Core's otherwise pure-managed posture,
justified by the fact that the alternative — skipping the flush — silently breaks crash-consistency
in every consuming product.

### Known residual: create-no-replace is not race-free

`File.Move` without overwrite is a managed existence check followed by a rename; `rename(2)`
overwrites atomically on POSIX, so the no-replace guarantee rests on the check and is not immune to a
concurrent creator of the destination (a TOCTOU). Publishers are expected to have one writer per
destination path (unique-per-object paths), under which this cannot bite. A genuinely race-free
`link()`-based publish is a possible later hardening.

## `ISecretFileLoader` — mode-checked secret loading

Loads a file's bytes only if it passes custody checks, so a mis-permissioned or link-swapped secret
is refused rather than trusted:

- The file exists and is **not a symbolic link** (`FileInfo.LinkTarget`) — links are refused to
  prevent link-swap attacks.
- On Unix, the file is mode `0600` and its parent directory is mode `0700`
  (`File.GetUnixFileMode`).

Failures surface as `SecretFileException` (or the `SecretFileRejection` out-value of `TryLoad`); the
message never contains the file's contents.

### Windows and owner checks

On Windows the Unix permission bits do not apply and are not evaluated — existence and symlink
refusal still hold, but mode custody is not enforced. Linux/container deployments are the intended
production custody target. Owner-versus-process verification is intentionally **not** performed: it
would require reading the file owner via `stat`, whose struct layout differs across architectures
(x86-64 vs arm64) and is not worth an unportable interop when mode `0600` already restricts reads to
the owner. It remains a possible later addition, guarded behind the same platform checks.

## Review focus (Standard of Care #3)

When changing this area: confirm staging always lands on the destination's volume; confirm every
error and abort path deletes the staging file; confirm `flushToDisk` and the directory flush both
run before a publish is reported successful; and, for the loader, confirm bytes are never returned
for a file that failed any check.
