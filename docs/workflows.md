# Workflows (`Nordstein.Core.Workflows`)

A product-agnostic workflow kernel: a workflow is a directed acyclic graph of typed nodes from a
product's closed catalogue. Data flows between nodes through path references; there is no
expression language and no user code. The kernel validates graphs and runs them; nodes, triggers,
persistence and scheduling are the product's.

## Public surface

| Type | Role |
|---|---|
| `WorkflowGraph`, `NodeDefinition`, `EdgeDefinition`, `RetryPolicy` | The definition: nodes (`id`, `type`, `config` as `JsonElement`, optional `Retry`: `Attempts` ≥ 2 and a `Backoff`) and edges (`from`, `to`, optional `branch`). Value equality, configs compared by JSON content. |
| `INodeType`, `ISuspendingNodeType`, `IIteratingNodeType`, `LoopBranches` | A node type of the product's catalogue: `Type` (the name), `Major`, `IsTrigger`, `HasSideEffects`, `ReadOnly` (default `false`; a read-only type has no side effects and never suspends, else the catalogue is refused), `Branches`, `InputSchema`, `GetOutputSchema(config)`, `ValidateConfig(config)`, `ExecuteAsync`; a suspending type adds `ResumeAsync`; an iterating type (branches `body` and `done`) adds `ItemsProperty`, `MaxItems(config)` and `Collect(context, results)`, and the kernel runs its loop. |
| `NodeTypeReference`, `NodeTypeExtensions` | How a graph names a type: `name@major` (`condition@1`), parsed with `TryParse`/`Parse`; `Names(text, name)` and `NodeDefinition.HasName(name)` match a name at any major; `INodeType.Reference()` is a type's own reference. |
| `NodeContext`, `NodeResult` | What a node executes with (resolved input, trigger input, `DryRun`) and how it ends: `Completed(output, branch?)`, `Suspended(bookmark)`, `Failed(reason)`. |
| `IWorkflowValidator`, `WorkflowValidationError` | Checks a graph against a catalogue and a node limit; every problem with the node it concerns. `Warnings` names each node on a superseded major. |
| `IWorkflowInterpreter`, `WorkflowExecution`, `PinnedStep`, `WorkflowSuspension`, `CompletedStep`, `WorkflowOutcome` | Runs and resumes a graph; `WorkflowExecution.Pins` names the nodes that complete with a given output instead of executing. |
| `IStepJournal`, `WorkflowFailure`, `WorkflowFailureKind` | The product's record of a run, step by step; `StepPinnedAsync` has a default that records a pinned step as completed, `StepRecoveredAsync` one that records a failed node taking its error edge as completed on `error`, `StepRetryingAsync` one that records nothing, and the `StepStartedAsync` overload with an `iteration` one that records the start without it. |
| `ReservedBranches` | The branch names the kernel reserves: `Error` (`error`) and its fixed output schema `ErrorOutputSchema` (`{ message, nodeType }`). |
| `INodeTrial`, `NodeTrialResult`, `OutputProblem`, `IterationStub` | Tries one node against supplied upstream outputs, without a run, and checks its output against its output schema; a body node takes its item and index from an `IterationStub`. |
| `IWorkflowGraphReader`, `WorkflowGraphFormatException`, `WorkflowGraphFormatError` | Reads a graph from the kernel's graph format (JSON text or a `JsonElement`); every format error with its JSON path. See [Graph format](#graph-format). |
| `IConditionNodeType`, `IForEachNodeType` | The kernel's own node types, `condition@1` and `for-each@1`, for a product to put in its catalogue. See [Kernel node types](#kernel-node-types). |
| `Module` | Registers the validator, the interpreter, the node trial, the graph reader and the two kernel node types as stateless singletons; a kernel node type is registered as itself, never as `INodeType`. |

## Node majors

A type's `Type` is its name; `Major` (1 or more) is its version. A graph's node names both as
`name@major`, so a saved graph pins the major it was written for. A catalogue may hold several majors
of one name (each (name, major) once; a name with `@`, a major below 1 or a duplicate throws
`ArgumentException`). A breaking change to a type's configuration, output or behaviour is a new major
under the same name; an additive change stays in the major. The validator refuses a node type that is
not of the form, an unknown name, or an unknown major (naming the majors the catalogue offers);
`Warnings` lists each node whose major a newer major of the same name supersedes, so a product can show
it without blocking the save. The interpreter and the node trial bind the major the node names.

## Data references

A configuration string may hold `{{trigger.<path>}}` (the trigger node's output),
`{{steps.<nodeId>.output.<path>}}`, or, inside a loop body, `{{item.<path>}}` (the current item) and `{{index}}` (its
position from 0). A path is dot-separated property names or array indexes, and
may be empty. A string that is exactly one reference takes the referenced value with its JSON type;
a reference inside longer text is replaced by the value's text (an object or array by its JSON).
A value missing at run time resolves to `null` (inside text: nothing). A `{{` or `}}` that is not a
reference is a validation error.

## Schemas

Input and output schemas use the JSON Schema keywords `type` (a name or an array of names),
`properties`, `required`, boolean `additionalProperties`, `items` and `enum`; others are
annotations. The validator checks a configuration against its input schema, except that a
whole-string reference stands for a value of the type its place expects; it does not reuse
`Nordstein.Core.AI`'s output validator because that one knows no references. In validation and in
a run, output schemas only resolve reference paths and types: a node's actual output is not checked
against it. A node trial (below) checks the output with the same subset, where no string is a
reference.

## Validation

`IWorkflowValidator.Validate(graph, nodeTypes, maxNodes)` returns every problem, graph-level first,
then per node in definition order. It checks: 1 to `maxNodes` nodes; ids
`[A-Za-z][A-Za-z0-9_-]{0,63}`, unique; types of the form `name@major` the catalogue holds at that
name and major; exactly one trigger and no edge into it; edges
between existing nodes, no self-edge, no duplicate; a branch exactly on the edges leaving a
branching node, one it declares, or the reserved `error` on an edge leaving any node but the trigger (a type that declares
`error` is refused); a `Retry` only on a non-trigger, non-iterating node whose type has no side effects; an iterating type declaring exactly `body` and
`done`; each loop body (the nodes reached through the loop's `body` edges) reached only through its loop, holding no
loop and no suspending node, and reading neither the loop's output nor, outside a body, `{{item…}}` or `{{index}}`;
no cycle; every node reachable from the trigger; in a read-only graph (its trigger's type is `ReadOnly`) only read-only nodes; each
configuration against its input schema, then `ValidateConfig`; each reference well-formed, naming
a node that **dominates** the referencing node (runs on every path from the trigger to it, so its
output exists), a path its output schema has (the fixed error output for a node reached only through the source's
`error` edges), and a type the place accepts (`integer` fits `number`; inside text only scalars fit).

## Execution

- **Order.** Kahn's topological order, the lowest definition position first among ready nodes:
  the same graph and the same node results visit the same nodes in the same order. Execution is
  sequential.
- **Reach.** The trigger runs first. Another node runs when at least one edge into it is taken: an
  edge from a node that completed, naming the branch it took, or naming no branch while the node did
  not complete on `error`. A node not reached is skipped and not journaled; a join after a condition runs once.
- **Journal.** `StepStarted` (with the resolved input), then exactly one of `StepCompleted`,
  `StepSuspended` or `StepFailed`. A cancellation of the run's token while a node runs journals
  `Cancelled` with `CancellationToken.None`, then the `OperationCanceledException` propagates;
  cancellation is also checked before each node.
- **Failures.** A node's `Failed`, an exception (other than the run's own cancellation), a branch
  its type does not declare, or a `Suspended` from a non-suspending type fail the step
  (`NodeFailed`) and the run. A node starting beyond `MaxSteps` is started and failed at once
  (`StepBudgetExhausted`). An invalid graph throws `ArgumentException` before anything runs.
- **Loops.** An `IIteratingNodeType` node starts with its resolved input; the array under `ItemsProperty` (not an array,
  or more than `MaxItems`: the step fails) is iterated in order, sequentially: per item the body nodes run in the
  graph's order with `{{item}}` and `{{index}}` resolved, each a journaled step (`StepStartedAsync` with the
  iteration) counted against `MaxSteps`, the body's outputs forgotten after each iteration; a body step that fails
  without an error edge fails the loop's step with `iteration <n>, step '<id>': <reason>` (the loop's error edge may
  take it), a body budget failure fails the loop with its kind, and a cancellation in the body journals `Cancelled` on the
  body step, then on the loop's step, before the `OperationCanceledException` propagates. The loop completes on `done` with
  `Collect(context, results)`, `results` the last executed body node's output per item. A pinned loop completes with its
  pin and runs no body; a pinned body node is pinned per iteration. A replay names the loop's collected step, never body
  steps.
- **Retries.** A node with a `Retry` runs up to `Attempts` times: a try ending in `Failed` (an exception included) short of
  the last is journaled through `StepRetryingAsync`, the `Backoff` is awaited on the run's token (one longer than a timer
  holds, about 49 days, is awaited in parts, never skipped), and the node runs again
  with the same input; the last try's result is the step's, so the error branch and the run failure apply after it. One
  step, one `StepStarted`. A cancellation during a try or a backoff never retries: `Cancelled`, then the exception. A pinned
  node and a node trial execute once, whatever the policy.
- **Error branch.** A node with an edge on `ReservedBranches.Error` does not fail the run on `NodeFailed`: the step is
  journaled through `StepRecoveredAsync` with the failure and the output `{ message, nodeType }`, the node completes on
  `error`, and only its error edges are taken. A budget, timeout or cancellation failure never takes it. A pin may name
  `error` on such a node, stubbing the failure.
- **Suspend and resume.** A suspending node returns `Suspended(bookmark)`; the run stops. The
  product stores the completed steps and calls `ResumeAsync` with a `WorkflowSuspension` and the
  answer: the completed steps are replayed without executing (they must be exactly the steps the
  order reaches before the suspended node, else `ArgumentException`), the node's `ResumeAsync`
  runs, and the run continues. The step budget counts replayed and suspended steps.
- **Dry run.** `WorkflowExecution.DryRun` reaches every node as `NodeContext.DryRun`; a node with
  `HasSideEffects` must then change nothing and describe what it would do.
- **Pins.** A node in `WorkflowExecution.Pins` is started with its resolved input and journaled through
  `StepPinnedAsync` with the pin's output and branch instead of executing (its type's `ExecuteAsync` is never
  called, a suspending type never suspends); downstream references and the reach rule use the pin, and the step
  counts towards `MaxSteps`. A pinned branch the type does not declare fails the step. A pinned trigger's output is the
  pin; `NodeContext.TriggerInput` stays the run's input.

## Node trial

`INodeTrial.TryAsync(graph, nodeTypes, nodeId, triggerOutput, outputs, dryRun, answer, ct)` runs
one node of a graph under construction without a journal or a run, for a product that lets a person
try a node against stubbed upstream data.

- **Checks.** Only what the try needs: the node has a known type at its major; its configuration passes the input
  schema and `ValidateConfig`; every reference names the trigger (read from `triggerOutput`), a
  node present in `outputs`, with a path its output schema has and a type the place accepts, or, with an
  `IterationStub`, the item or index (without one such a reference is a problem). A loop node is not executed: the
  try reports its `Collect` of no results on `done`. The
  rest of the graph may be invalid. Problems come back in `NodeTrialResult.Problems` with
  `ResolvedInput` and `Result` null; a `nodeId` not in the graph throws `ArgumentException`.
- **Execution.** The configuration is resolved as in a run, `ExecuteAsync` runs with `dryRun`, and
  the run's rules apply: an exception, an undeclared branch or a suspension of a non-suspending type
  becomes `Failed`; the token's cancellation propagates. A `Suspended` from an `ISuspendingNodeType`
  is reported with its bookmark; with `answer`, `ResumeAsync` runs at once and its result is reported.
- **Output check.** A `Completed` output is checked against `GetOutputSchema(config)`; every mismatch
  is an `OutputProblem` with its JSON path (`$.items[1].name`). A mismatch is a finding, not a
  failure.

## Graph format

`IWorkflowGraphReader.Read(string)` and `Read(JsonElement)` read a graph from the kernel's JSON format. The format is a
public contract: what it accepts today it accepts in every later version.

```json
{
  "nodes": [
    { "id": "start", "type": "manual@1" },
    { "id": "check", "type": "condition@1", "config": { "all": [{ "left": "{{trigger.total}}", "op": "gt", "right": 100 }] } },
    { "id": "fetch", "type": "fetch@2", "config": { "url": "{{trigger.url}}" }, "retry": { "attempts": 3, "backoff": "PT5S" } }
  ],
  "edges": [{ "from": "start", "to": "check" }, { "from": "check", "to": "fetch", "branch": "true" }]
}
```

- **Keys.** `nodes` (required, an array), `edges` (optional, an array; absent is none). A node: `id` and `type` (required,
  strings), `config` (optional, any JSON value, `{}` when absent), `retry` (optional, an object). A retry: `attempts`
  (required, a whole number from 2 to 2147483647 written without fraction or exponent, so `3.0` and `3e0` are refused) and
  `backoff` (optional, zero when absent). An edge: `from` and `to` (required, strings), `branch` (optional, a string).
- **Backoff.** An ISO 8601 duration `PnDTnHnMnS`: days, hours, minutes and seconds, each optional but at least one
  present, digits `0`-`9` only, seconds with up to seven decimals after a `.`. No sign, weeks, months or years (their
  length is not fixed), no lowercase designators, no `,` decimal sign, at most `TimeSpan.MaxValue` (10675199 days).
- **Case and order.** Keys match ordinally, so `Nodes` is not `nodes`. Key order does not matter; node and edge order is
  kept (it breaks the interpreter's ties).
- **Unknown keys.** A key the format does not define is ignored at every level (the graph, a node, an edge, a retry). It
  is the product's to read: a canvas layout, a node's test pin, a switch-off flag. A product that needs its own keys reads
  them from the same document and hands the kernel the element (`Read(JsonElement)`). A misspelled optional key is
  therefore ignored too; a misspelled required key is reported missing.
- **Null.** A key whose value is `null` is not absent: `"branch": null` is the wrong type. A `config` of `null` is the
  JSON value `null`, which the node type's input schema then judges.
- **Duplicates.** The same property name twice in the graph object, a node, an edge, a retry, or any object inside a
  `config` is an error at that object. Inside a key the reader ignores, the product judges.
- **Text.** Every string the reader reads, and every name and string inside a `config`, decodes to valid text: an escaped
  unpaired surrogate (`"\uD800"`) is an error, so the validator and the interpreter never meet a string they cannot read.
- **Limits.** The graph nests at most 64 levels deep, the graph object counting as the first. Text is one JSON document
  under RFC 8259: no comments, no trailing commas, nothing after the document, no byte order mark. The reader sets no limit
  on length, nodes or edges; the caller bounds the input, and the validator's `maxNodes` bounds a graph it accepts.
- **Meaning.** The reader never consults a catalogue: any `type` text is read, and an unknown or malformed type, an
  invalid id, an edge to a missing node and an empty `nodes` array are the validator's to refuse.
- **Errors.** A text that is not JSON is one `WorkflowGraphFormatError` with the parser's line, byte and message, and the
  parser's exception as the inner exception. Otherwise every error found, in document order, each with a JSON path from
  `$` (`$.nodes[2].retry.attempts`, `$.nodes[0].config['odd key']`) and a message naming the place:
  `$.nodes[0].id is missing; it is a string.`, `$.edges[1].to is a number, not a string.`,
  `$.nodes[0].config names the property 'k' twice.` `WorkflowGraphFormatException : FormatException` carries them
  (`Errors`); its message is `The text is not a workflow graph:` followed by every error's message.

## Kernel node types

The kernel ships two node types, resolved from the container as `IConditionNodeType` and `IForEachNodeType`. A product
adds them to its catalogue itself (`[.. productTypes, conditionType, forEachType]`); the module never registers them as
`INodeType`, so a product's own catalogue never changes behind its back. Both are stateless singletons, and their
interfaces carry the names as constants (`IConditionNodeType.TypeName`, `IForEachNodeType.TypeName`).

### `condition@1`

- **Configuration.** Exactly one of `all` (every rule holds) and `any` (one rule holds): a non-empty array of rules
  `{ "left", "op", "right" }` with no other keys. `all`, `any`, each rule and each `op` are literal; `left` and `right` may
  be or hold data references.
- **Output.** `{ "result": true | false }`; the node completes on the branch `true` or `false`. It is `ReadOnly`.
- **Evaluation.** In order, stopping at the first rule that decides (a false one under `all`, a true one under `any`).
- **Values.** A number is a JSON number that fits a `decimal`, or a string that parses as one in invariant notation
  (sign, `.` and exponent; never a thousands separator, so `"1,50"` stays text). A value's text: a string's own text,
  nothing for `null`, `true` or `false`, the JSON text otherwise. A missing value resolves to `null`.
- **Operators.** `eq` and `neq`: numbers numerically (`"5"` equals `5.0`); otherwise, when either side is an object or
  an array, by JSON content; otherwise by text, ignoring case ordinally (culture-independent). `contains`: on an array
  `left`, whether an item equals `right`; otherwise text containment, ignoring case. `gt` and `lt`: numbers only, else
  false. `in`: `right` an array holding `left`, else false. `matches`: `right` a .NET regular expression
  (culture-invariant, one second per match) matching `left`'s text; a `right` that is not a string is false.
- **Validation.** `ValidateConfig` refuses neither or both of `all` and `any`, no rule, a reference in a literal place, a
  literal pattern that does not compile, and a literal `in` right side that is not an array (a whole-string reference is
  accepted). A pattern from data that does not compile, and a match beyond one second, fail the step:
  `config.all[0].right is not a regular expression.` Wrongly typed data never fails a step; it compares false.

### `for-each@1`

- **Configuration.** `items` (required): the array, as a rule a whole-string reference; a literal array is allowed.
  `maxItems` (optional): a literal whole number from 1 to 100, default 20 (`IForEachNodeType.DefaultMaxItems`,
  `MaxItemsLimit`). `MaxItems` throws `ArgumentException` for a configuration `ValidateConfig` refuses.
- **Run.** The kernel runs the loop ([Execution](#execution), Loops); `ExecuteAsync` only reports a failure. An `items`
  that is not an array once resolved (a missing value included) or longer than `maxItems` fails the step.
- **Output.** `{ "count", "results" }`, `results` the body's last executed node's output per item (`null` for an
  iteration whose body reached no node). Not `ReadOnly`: a read-only graph cannot loop (a later version may allow it; the
  reverse would break graphs).
- **Nesting and waiting.** The validator refuses a loop in a loop's body and a suspending node in it. A suspending node
  before or after the loop suspends and resumes as anywhere else; a resume replays the loop's collected output and never
  the body.
