# IO

`Nordstein.Core.Common.Io` provides product-agnostic filesystem primitives: durable atomic file
publishing, directory flushing, exclusive lock files, filesystem capability probing, owner-only spooling, mode-checked secret-file loading and real-path containment. All are registered as
singletons in `Nordstein.Core.Common.Module`.

## `IDurableFilePublisher` — stage, flush, atomic publish

Writes a file so that a crash leaves either the old state or the fully written new file, never a
torn one:

1. `BeginWrite(destinationDirectory)` creates a staging file **inside that directory** (a
   `.{guid}.tmp` sibling), so publishing is an intra-volume rename rather than a cross-device copy.
   Same-volume staging is a correctness requirement, not an optimization — this is why the publisher
   does not reuse `ITempDirectory`, which stages under the system temp directory.
2. The caller writes content through `IFileWriteHandle.Content`.
3. `PublishAsync(destinationPath)` flushes the content to disk (`FileStream.Flush(flushToDisk: true)`),
   then atomically publishes with create-without-replace (`File.Move` without overwrite, which throws
   if the destination exists — surfaced as `DestinationAlreadyExistsException`).
   `PublishReplacingAsync` is the same but replaces an existing destination. After the move, both
   flush the destination's directory through `IDirectorySync`.
4. Disposing the handle without publishing aborts the write and deletes the staging file
   (best-effort); a leaked staging file is recoverable and never a final artifact.

### Durability scope (the file and its directory entry)

The publisher flushes the **file's own data** before the rename, so a crash never yields a
half-written file, and flushes the **containing directory** after the rename, so the new entry
survives a crash too. If that directory flush fails, the publish throws `IOException`, but the file
is already in place under its final name. On a filesystem that cannot sync directories, or off
Linux, the flush reports `Unsupported` and the publish still succeeds: the entry is then as durable
as the platform makes it.

## `IDirectorySync` — flush a directory

`Flush(directory)` makes created, renamed or removed entries in a directory durable. There is no
BCL API for it, so on Linux it calls `open(O_RDONLY | O_DIRECTORY | O_CLOEXEC)`, `fsync` and
`close` through `[LibraryImport("libc")]` (`O_DIRECTORY` is 0200000 on x86 and x86-64 and the
asm-generic 040000 everywhere else, chosen by process architecture). `EINVAL` from `fsync` means
the filesystem cannot sync directories and returns `DirectorySyncResult.Unsupported`; any other
errno throws `IOException` naming the path and the errno. On other operating systems it does
nothing and returns `Unsupported`. Callers that create directories (a product's day directories,
for example) flush each new directory's parent: `CreateDurably` below does both.

### Known residual: create-no-replace is not race-free

`File.Move` without overwrite is a managed existence check followed by a rename; `rename(2)`
overwrites atomically on POSIX, so the no-replace guarantee rests on the check and is not immune to a
concurrent creator of the destination (a TOCTOU). Publishers are expected to have one writer per
destination path (unique-per-object paths), under which this cannot bite. A genuinely race-free
`link()`-based publish is a possible later hardening.

### `CreateDurably` — create directories whose entries survive a crash

`sync.CreateDurably(directory, unixMode = null)` (extension on `IDirectorySync`) makes the path
absolute (`Path.GetFullPath`, trailing separator dropped), creates every missing directory one at a
time, outermost first (each with `unixMode`, filtered by the umask, on Unix; `Directory.CreateDirectory`
alone would give it to the last one only), then flushes the parent of each, outermost first. It
returns the created paths, outermost first; an existing directory is neither created nor flushed. A
directory a concurrent caller created in between is kept as it is and flushed anyway, so concurrent
callers all succeed. A failed create throws before any flush; a failed flush throws after all exist.

## `IExclusiveFileLock` — one holder per lock file

`TryAcquire(path)` opens the file with `FileMode.OpenOrCreate` and `FileShare.None` and returns the open
stream as an `IAsyncDisposable` lease, or `null` when the file is held. On Unix .NET backs `FileShare.None`
with `flock(LOCK_EX | LOCK_NB)`; a held lock surfaces as a plain `IOException` whose `HResult` is the raw
`EWOULDBLOCK` (11 on Linux, 35 on macOS and FreeBSD), on Windows as a sharing or lock violation. Only that
case becomes `null`: a missing directory, a directory at `path` or a permission failure throws. The lock
belongs to the open file description, so a second `TryAcquire` in the same process is refused too, and the
kernel drops it with the process. The file is never written or deleted. Advisory on Unix,
`DOTNET_SYSTEM_IO_DISABLEFILELOCKING` switches it off, and an NFS or SMB server may not enforce it between
hosts.

## `IFilesystemProbe` — what does this directory guarantee?

`Probe(directory)` returns `FilesystemCapabilities`:

- `FilesystemType`: the directory is resolved with `realpath(3)` (links included), then the longest
  whole-segment mount point in `/proc/self/mountinfo` names the type; of two mounts on one point the later
  wins, `\040`-style escapes are decoded. Off Linux, or if the table cannot be read, `unknown`.
- `RefusesReplacingRename`: two scratch files, `File.Move` without overwrite from one onto the other; true
  when the move failed and both files are intact (the same call `IDurableFilePublisher` relies on).
- `DirectorySync`: the result of `IDirectorySync.Flush(directory)`; any other failure throws.
- `RefusesSecondExclusiveOpen`: a second `IExclusiveFileLock.TryAcquire` on a scratch file is refused
  while the first lease is held.

Scratch files are named `.{guid}.probe-*` and deleted on every path. One host cannot see whether another
host honours its lock, so a caller treats network and FUSE types (`nfs*`, `cifs`, `smb3`, `fuse.*`) as
unguaranteed whatever the results say.

## `IPrivateFileSpool` — owner-only spool with hash and cap

`SpoolAsync(source, path, maxBytes, ct)` copies a stream into a new file and returns `SpooledContent` (SHA-256 and
length, both computed during the one copy). On Unix a missing directory is created `0700` by the `mkdir` itself and the
file `0600` through `UnixCreateMode`, so neither is ever open to others; an existing directory keeps its mode. The file is
`FileMode.CreateNew`: an entry already at the path throws `DestinationAlreadyExistsException` and is never touched. The
cap is checked per read: the read that crosses it throws `SpoolLimitExceededException`, nothing more is read or written.
Any failure after the create (cap, I/O, source, cancellation) deletes the partial file, best effort, before it
surfaces. The spool is not flushed to disk; a durable file goes through `IDurableFilePublisher`.

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

## `IPathContainment` — may this path be inside that root?

`MayBeWithin(path, root)` answers whether `path` may be `root` or lie below it, for guards of the form "this
must never live under that" (a secret outside a directory that is backed up or served). It errs towards
"within" on purpose; the name says so, and it never answers an allow decision.

- **Two readings, either counts.** Each path is resolved twice: the way the operating system walks it
  (made absolute against the current directory, `.` dropped, an existing or dangling symbolic link or
  Windows junction replaced by its target via `FileSystemInfo.LinkTarget`, relative targets from the
  link's directory, `..` applied to what is resolved so far, so `link/..` is the parent of the link's
  *target*), and the way .NET file APIs open it (`Path.GetFullPath` first, which collapses `..` as text
  and normalizes Windows forms such as `C:keys` or a trailing dot, then the links in what remains). A
  `File.ReadAllBytes(<root>/esc/../keys/k)` opens `<root>/keys/k` even when `esc` links out, while a
  shell or backup tool walks the link: the check is `true` if any pairing of the two readings of `path`
  and of `root` is within. Below a segment that does not exist the tail is kept as written. Nothing is
  created or written.
- **Whole segments.** `/x/data2` is not inside `/x/data`; a trailing separator on `root` changes nothing.
- **Case and normalization ignored everywhere** (NFC, then `OrdinalIgnoreCase`). NTFS, SMB and a NAS share
  mounted on Linux alias `/x/Data` and `/x/data`; APFS and some SMB or ZFS servers alias a composed and a
  decomposed `ä`. The check cannot know which filesystem a not-yet-created path lands on, so on a
  case-sensitive one it reports containment for two directories that differ only in case or form.
- **Failures fail closed.** `LinkTarget` answers "not a link" for every `readlink` error, `EACCES`
  included, so each non-link segment is also `stat`ed: a directory that cannot be searched is an
  `UnauthorizedAccessException` (a hidden link behind it would otherwise pass), more than 40 links for one
  path (a loop) an `IOException`, a U+0000 an `ArgumentException`. A guard catches them and refuses.
- **Not detected.** A link inside `root` pointing out (a tool that follows links, `tar -h` or `rsync -L`,
  copies its target), bind mounts, hard links, Windows 8.3 short names.
- **Point in time.** A link created or swapped after the call is not seen; the caller re-checks where it
  matters (after a write, on each load).

## `PortableSegment` — is this path segment portable?

`segment.PortableSegmentViolation(maxLength)` returns the first broken `PortableSegmentRule` or `null`: `Length` (empty or
over the cap), `DotSegment` (`.`, `..`), `Charset` (outside `[a-z0-9_-]`), `ReservedName` (`con`, `nul`, `aux`, `prn`,
`com1`-`com9`, `lpt1`-`lpt9`). A pure extension: callers composing paths from operator input check each literal segment.

## `StreamTextExtensions` — bounded text read

`stream.ReadAllTextAsync(maxCharacters, leaveOpen, ct)`: UTF-8 (a BOM picks its encoding, stripped), `\r\n` and `\r` to
`\n` (also across reads), `TextLimitExceededException` once the normalized text would pass the cap, unread rest.

## Review focus (Standard of Care #3)

When changing this area: confirm staging always lands on the destination's volume; confirm every
error and abort path deletes the staging file; confirm `flushToDisk` runs before the move and the
directory flush after it, both before a publish is reported successful; and, for the loader, confirm bytes are never returned
for a file that failed any check. For `IPrivateFileSpool`, confirm a file found at the path is never deleted, every failure after the create deletes the partial file, and no byte past the cap is written. For `IExclusiveFileLock`, confirm only a held lock maps to `null` and the file is never deleted; for
`IFilesystemProbe`, confirm every scratch file is deleted on every path. For `IPathContainment`, confirm no path through a link, a `..` after a link (both readings), a
case or normalization variant, a common-prefix sibling or an unsearchable directory turns a "within" into
`false`, and that resolution never writes.
