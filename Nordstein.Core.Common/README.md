# Nordstein.Core.Common

Product-agnostic building blocks shared across Nordstein applications.

| Area | What it gives you |
|------|-------------------|
| `Time` | `IClock` / `SystemClock` — the seam that makes time-dependent logic testable; `TruncateToMicroseconds` (UTC, the precision `timestamptz` keeps); `ShiftedTimeProvider` — a `TimeProvider` and `IClock` reading an inner provider plus a fixed offset (a demo host's fixed "today") |
| `Random` | `IRandom` / `SeededRandom` — the same seam for randomness |
| `Async` | `IAsyncLock` / `AsyncLock`, `TaskExtensions` |
| `Banking` | `IbanExtensions.TryNormalizeIban` — an IBAN as printed to its electronic form, valid only with the ISO 7064 MOD 97-10 check (pure, culture-invariant, never throws) |
| `Validation` | `Validation` helpers (`InPast`/`InFuture` take the caller's clock as `now`), `ValidatorExtensions`, string guards |
| `Conversion` | `ITypeConverter` and the conversion extensions; `ToBase32` / `IsBase32` (lowercase unpadded RFC 4648 base32) |
| `Collections` | `CollectionEquality` — order-independent set and dictionary equality with consistent hash codes |
| `Culture` | `CulturalValueParser` (numbers and dates under a declared separator pair and `DateOrder`, strict) — see [`docs/values.md`](../docs/values.md) |
| `Cryptography` | `IAeadStreamCodec` (chunked AES-256-GCM stream encryption with a per-message HKDF subkey) and `IAeadKeyWrap` (AES-256-GCM key wrap with AAD binding) |
| `DependencyInjection` | Autofac registration helpers, including `RegisterServiceCollection` |
| `Http` | `DirectHttpHandlerFactory` — the outbound `SocketsHttpHandler`: no proxy, no redirect, pooled connections recycled every two minutes |
| `Hosting` | `AddResilientBackgroundServices`, `IAppVersion`, `NullHostedService`, and `IPeriodicJob` with `RegisterPeriodicJob` (recurring work, one lifetime scope per pass, on the container's `TimeProvider`) |
| `Io` | `IDurableFilePublisher` (staging + flush + atomic no-replace publish + directory fsync), `IDirectorySync` (directory fsync on Linux), `IExclusiveFileLock` (process-lifetime exclusive lock file), `IFilesystemProbe` (filesystem type and guarantees of a directory), `IPrivateFileSpool` (owner-only create-no-replace spool with SHA-256, length and a mid-stream cap), `ISecretFileLoader` (mode-checked secret-file loading) and `IPathContainment` (may a path be inside a root, through links, ignoring case and normalization) |
| `Lifecycle` | `Disposable`, `ITempDirectory` |
| `Net` | Endpoint URL parsing and validation; `ToDisplayString` (a URI without userinfo, query or fragment, for messages and logs) |
| `Security` | `ISecretProtector`, `ISecretHasher` (default: upper-hex SHA-256, yields to a product registration), `Sha256`, and `IPasswordHasher` (salted PBKDF2-HMAC-SHA256 password hashes as PHC strings, with a rehash signal); the credential primitives `ILoginLockout` (failed-sign-in backoff on `IClock`), `ISecretTokenFormat` (prefixed base32 bearer secrets), `IPasswordPolicy` (NIST SP 800-63B, NFKC), `IDummyPasswordVerifier` (equal timing for refusals) and `IPasswordGenerator`; `IMasterKeyRing` (master keys from a mode-checked key file with canaries, typed fail-closed load, version-bound wraps) and `IMasterKeyRingInitializer` (create-once, durable) — see [`docs/security.md`](../docs/security.md) |
| `Serialization` | `ISerializer` and a `System.Text.Json` implementation; `CanonicalJson` (sorted, compact JSON for stable hashes) |
| `Settings` | `[Setting]`, `[Protected]`, `[Sink]` on configuration records; `ISettingRegistry`, built from the registered `SettingRoot`s and the one `SettingNaming`, lists, finds and names settings in three scopes — see [`docs/settings.md`](../docs/settings.md) |
| `Tabular` | `ICsvReader` (dialect detection: encoding and delimiter; lenient RFC 4180 records), `ICsvRowWriter` (RFC 4180 rows), `CsvDialect`, `SpreadsheetNumberFormatExtensions` (spreadsheet built-in number formats, locale tags) — see [`docs/tabular.md`](../docs/tabular.md) |
| `Text` | `LogSafeExtensions`, `SlugExtensions` |

## Registration

Everything is wired through one Autofac module:

```csharp
builder.RegisterModule<Nordstein.Core.Common.Module>();
```

Recurring work runs as an `IPeriodicJob`, one lifetime scope per pass:

```csharp
builder.RegisterPeriodicJob<CleanupJob>(_ => new PeriodicJobSchedule { Interval = TimeSpan.FromMinutes(10) });
```

## Notes

`AddResilientBackgroundServices` sets `HostOptions.BackgroundServiceExceptionBehavior` to
`Ignore`. .NET's default is `StopHost`, which means one throwing `BackgroundService` stops the
whole host and exits with code 0 — a clean shutdown that no restart policy treats as a failure.
Call it from every host that runs long-running loops.
