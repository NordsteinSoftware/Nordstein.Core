# ADR 0005: Workflow graph format reader in Core.Workflows, kernel keys only

- Status: proposed
- Date: 2026-11-28
- Issue: [#76](https://github.com/NordsteinSoftware/Nordstein.Core/issues/76) (part of #54, ADR 0001 decision 6)

## Context

ADR 0001 decision 6 asks for a design before the graph JSON reader moves from nordstein-dms
(`Application/Workflows/Internal/WorkflowGraphJson.cs`, source at `1f8456ee`). The issue names
three open questions:

- the JSON wire shape and its versioning,
- how product keys and unknown keys are handled,
- how errors are reported, and how the reader relates to the validator.

In dms one parser reads the kernel's keys (`nodes`, `edges`, `id`, `type`, `config`, `retry`)
together with the product's keys (`layout`, `pin`, `disabled`). It returns a
`WorkflowDocument` with both. A missing id, type, `from` or `to` throws `WorkflowInvalidException`
at once; a bad `retry` or `pin` is collected per node. No error carries a JSON path.

The port merged in PR #82 (`cc353cc`) before this ADR was written. It is described in
`docs/workflows.md` ("Graph format") and in the XML docs of `IWorkflowGraphReader`. This ADR
records the decisions that commit made, so that later changes have a reference. Core has
published no release yet, so the public shape can still change without a major version.

## Decision

1. **Core reads the kernel's keys only.** The format is `nodes` (required) and `edges`
   (optional). A node has `id`, `type`, `config` (`{}` when absent) and `retry`
   (`attempts`, `backoff`). An edge has `from`, `to` and `branch`. `layout`, `pin` and
   `disabled` stay in dms.
2. **The format is a public contract that only grows.** What the reader accepts today, it
   accepts in every later version. The format has no version key. A new optional key can be
   added, because older readers ignore it (decision 3). A change that refuses or reinterprets
   an accepted graph is a breaking change of `Nordstein.Core.Workflows`: it needs a new ADR
   and a major version.
3. **A key the format does not define is ignored at every level.** This is the seam for
   product keys. A product that has its own keys parses the document once, reads its keys
   from the `JsonElement`, and hands the same element to `Read(JsonElement)`. The reader
   copies the configurations and keeps no reference to the element. A misspelled optional
   key is therefore ignored. A misspelled required key is reported as missing.
4. **The reader checks shape, the validator checks meaning.** The reader never consults the
   node catalogue. An unknown or malformed `type`, a bad id, an edge to a missing node and an
   empty `nodes` array are read, and `IWorkflowValidator` refuses them. Inside `config` the
   reader checks only that names decode, that a name appears once per object, and that the
   nesting is at most 64 levels. The node type's input schema judges the rest.
5. **Strict JSON, strict values.** One RFC 8259 document. Keys match ordinally, so they are
   case-sensitive. `null` for a defined key is the wrong type, not absent. A duplicate name is
   an error. `attempts` is a whole number from 2, written without a fraction or exponent.
   `backoff` is an ISO 8601 `PnDTnHnMnS` duration. The reader sets no limit on length or on
   the number of nodes and edges: the caller bounds the input.
6. **Every error is reported with its JSON path.** `WorkflowGraphFormatException :
   FormatException` carries `Errors`, a list of `WorkflowGraphFormatError(Path, Message)` in
   document order (`$.nodes[2].retry.attempts`). A text that is not JSON gives one error with
   the parser's line and byte, and has the parser's exception as the inner exception. The
   messages are written for a person or an agent that fixes the graph.

## Alternatives considered

- **Port dms's `WorkflowDocument` with layout, pins and disabled.** Rejected: these are
  canvas and test-run concepts of one product. ADR 0001 keeps product concepts out of Core.
- **A hook (`IGraphKeyReader`) that the reader calls for each unknown key.** Rejected: it adds
  a public seam for something `Read(JsonElement)` already allows. The product reads its own
  keys in its own types.
- **Reject unknown keys.** Rejected: every product key would then need a kernel change, and
  an older reader could not read a graph with a newer optional key.
- **A `version` key now.** Rejected: an additive-only contract needs none. If a breaking
  change is ever needed, the new major can add a version key, and graphs without it read as
  the old format.
- **Stop at the first error, as dms does for required keys.** Rejected: an agent that edits a graph then needs
  one round trip per mistake.
- **One shared error type with the validator.** Rejected: a format error has a JSON path, and
  a validation error names a node. The two run at different stages, and a caller handles them
  apart.

## Consequences

- dms can drop the kernel half of `WorkflowGraphJson`. It keeps a small reader for `layout`,
  `pin` and `disabled` that runs on the same `JsonElement`, then calls
  `IWorkflowGraphReader.Read(JsonElement)`.
- The behaviour changes for dms. A graph is refused with every error and its path, not the
  first error only. `"branch": null`, a duplicate key, `attempts` as `3.0` and a fraction of a
  second beyond seven decimals are now errors. Graphs that dms has stored must be read once
  against the new reader before the switch. That check is the dms switch's work, not Core's.
- Adding a kernel key later can collide with a product key of the same name. A product should
  give its own keys names that the kernel is unlikely to take.
- No follow-up tasks: the code, tests and `docs/workflows.md` are on `main`.
