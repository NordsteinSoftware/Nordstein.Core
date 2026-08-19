# Nordstein.Core docs

Developer documentation for the shared, product-agnostic Nordstein.Core packages. Start with the
repository's [`CLAUDE.md`](../CLAUDE.md) — it states the standard of care this repository is held
to and indexes these pages by task.

| Doc | Covers |
|-----|--------|
| [`architecture.md`](architecture.md) | Package layering and dependency rules, the seam shapes that keep Core product-agnostic, Autofac module/discovery conventions, hosting (`AddResilientBackgroundServices`, `BackgroundService` vs `IHostedService`), and how consumers reference Core (source vs package mode). |
| [`code-style.md`](code-style.md) | C# style rules and key conventions — warnings-as-errors, the no-`!` rule, `internal` by default, `DateTimeOffset`, XML-doc requirements, `IAsyncLock` concurrency rules. |
| [`cryptography.md`](cryptography.md) | `Nordstein.Core.Common.Cryptography` — the chunked AES-256-GCM stream codec (`IAeadStreamCodec`), the AEAD key wrap (`IAeadKeyWrap`), the caller-owns-the-header seam, and the CSPRNG-not-`IRandom` rule. |
| [`io.md`](io.md) | `Nordstein.Core.Common.Io` — durable atomic file publishing (`IDurableFilePublisher`, incl. the one native `fsync` interop) and mode-checked secret-file loading (`ISecretFileLoader`), with the Windows degrade story. |
| [`domain.md`](domain.md) | `Nordstein.Core.Domain` — entity/object contracts, the factory-delegate pattern, generators, repositories/transactions, FK and soft-delete (archive) conventions, and the five-file pattern products build on top. |
| [`storage.md`](storage.md) | `Nordstein.Core.Storage` — the EF Core storage foundation: `NordsteinDbContext`, the generic repositories, the ambient-transaction seam, the reference-data cache, and how a product consumes them. |
| [`validation.md`](validation.md) | The validation helpers, the `Validation.Success` rationale (the one sanctioned `!`), and the bar for adding new checks. |
| [`licensing.md`](licensing.md) | `Nordstein.Core.Licensing` — the generic license engine (JWT verification, snapshot, server check) and the product seams (`ILicenseTierPolicy`, `LicensingConfiguration`). |
| [`ai.md`](ai.md) | `Nordstein.Core.AI` — the AI/agent foundation: messages, tools, prompts, completions, the versionless `IAgent` and `IModelClient` contracts, structured output parsing, and the product seams. |
| [`testing.md`](testing.md) | The coverage bar for Core, the `BaseTest<TModule>` harness and its stateless-container principles, substitution patterns, container-backed tests, and the care required when changing the harness itself. |
| [`product-playbook.md`](product-playbook.md) | The transferable practices for building a *consuming product*: docs-as-part-of-change, the VitePress user manual (+ automated screenshots), scoped test running, Playwright e2e over the real Docker stack, i18n, perf budgets, CI shape, releasing. |

See also [`../PUBLISHING.md`](../PUBLISHING.md) for how Core is packaged and published, and — in the
consuming product — `docs/code-reuse.md` for how a product references Core (source vs package mode) and
what remains to be extracted.
