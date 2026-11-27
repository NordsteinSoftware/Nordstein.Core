# Nordstein.Core.AI — the AI/agent foundation

`Nordstein.Core.AI` holds the generic AI vocabulary every Nordstein AI product would otherwise
reimplement: messages, tools, prompts, completions, the agent and model-client contracts, and
structured model-output parsing. It depends on `Nordstein.Core.Common` and
`Nordstein.Core.Domain` only; everything else is BCL (`System.Text.Json`, plus the
Microsoft-owned `System.Memory.Data` for `BinaryData` content payloads).

## What is in the package

| Namespace | Contents |
|-----------|----------|
| `Nordstein.Core.AI.Agents` | `IAgent` — the versionless agent contract: `Name`, `SystemPrompt`, `Tools`, `ModelParameters`, `CreateSystemMessage`; `IAgentLoop` with `IToolInvoker`, `AgentLoopOptions`, `AgentLoopResult`, `AgentLoopOutcome` (see [The agent loop](#the-agent-loop)) |
| `Nordstein.Core.AI.Messages` | `Message` hierarchy (`SystemMessage`, `UserMessage`, `AssistantMessage`, `ToolMessage`), `Conversation`, `Content`/`ContentKind`/`Role`, `ToolRequest`/`ToolResponse`, `ToolRequestMatch` (canonical-JSON tool-call comparison) |
| `Nordstein.Core.AI.Tools` | `ToolSpecification`, `ToolArguments` (JSON-schema generation), `IToolArgument` (its JSON-schema implementation is the internal `Tools.Internal.JsonToolArgument`) |
| `Nordstein.Core.AI.Prompts` | `IPromptTemplate`/`IPrompt` — `{variable}` templates and their rendered form. Persistence (a template repository) is deliberately absent — products own storage |
| `Nordstein.Core.AI.Completions` | `ICompletion`, `TokenUsage`, `TypedCompletion<TOutput>`, `IModelParameters` |
| `Nordstein.Core.AI.Embeddings` | `IEmbeddingClient`: `ModelId` (keys the output space), `Dimension`, `EmbedAsync` (one vector per input, in order; empty inputs, no call) |
| `Nordstein.Core.AI.Clients` | `IModelClient` (typed + streaming completion, request preview), `ModelOptions` (with the optional `ResponseFormat`), `ModelSamplingParameters`, `ResponseFormat`/`JsonSchemaResponseFormat` (the OpenAI-compatible `response_format` member), `ModelStreamUpdate` variants, request-preview records, the transport errors every client throws: `InferenceUnavailableException` (transient: unreachable, timeout, error status) and `InferenceContractException` (permanent until the configuration changes: a reply of the wrong shape; not sealed, so a product derives its own reasons) |
| `Nordstein.Core.AI.Serialization` | `IOutputFormat` (parse + validate model output against a type, plus the prompt instruction telling the model how to format), `JsonSchemaOutputFormat` and `OutputFormatException`/`OutputFormatViolation` (strict parse against a runtime JSON Schema, see [Output formats](#output-formats)), `ITextSerializer`, truncated-JSON repair, `ModelReplyExtensions` (`StripCodeFence`, `TryStripReasoning`: cut a Markdown code fence or a leading `<think>` block off a raw reply) |

## The seams — what stays product-side, and why

- **Provider implementations.** `IModelClient` is a contract; the OpenAI (or any other) wrapper,
  its SDK dependency, and its transport live in the product. Core carries no provider SDKs —
  every Core dependency becomes every product's dependency.
- **Agent lifecycle.** `IAgent` is deliberately versionless and persistence-free. A product
  extends it with its own concerns — tenancy, version history, endpoint binding, archiving —
  by declaring `interface IAgent : Nordstein.Core.AI.Agents.IAgent, ...` and keeping its
  factories and repositories to itself. Version bookkeeping is one product's audit feature,
  not AI boilerplate.
- **Client acquisition.** How a client is created (per-agent factories, endpoint overrides,
  call-recording flags) is a product delegate over the Core contract. The contract itself only
  promises completion, typed completion, preview, and streaming.
- **Prompt persistence.** `IPromptTemplate` renders; where templates live (resources, database)
  is the product's choice.

## Output formats

Two ways to turn a reply into structured data, chosen by what the caller knows at compile time:

| Format | Schema from | On a broken reply | Obtained by |
|---|---|---|---|
| `JsonOutputFormat` (internal) | a .NET type, exported once | salvages truncated JSON (closes what the cut left open), strips prefixes such as `json:` | `IOutputFormat.Create(type)` from the container |
| `JsonSchemaOutputFormat` (public) | a `JsonNode` built at runtime | throws `OutputFormatException`; never a partial result | `new JsonSchemaOutputFormat(schema, name, strict: true)` |

`JsonSchemaOutputFormat` exists for schemas known only at runtime, where silently dropping the
fields a truncation cut off would be data loss:

```csharp
var format = new JsonSchemaOutputFormat(schema, "line_items");
var options = new ModelOptions(model, []) { ResponseFormat = format.ResponseFormat };
// a hand-built OpenAI-compatible body: body["response_format"] = format.ResponseFormat.ToOpenAiCompatibleJson();
JsonNode? reply = await format.ParseAsync<JsonNode>(text, cancellationToken);
```

- **Request.** `ResponseFormat` is a `JsonSchemaResponseFormat` (name, compact schema text,
  `Strict`, default `true`). `ToOpenAiCompatibleJson()` renders
  `{"type":"json_schema","json_schema":{"name":…,"schema":…,"strict":…}}`; a client writes it
  under `response_format` when `ModelOptions.ResponseFormat` is set and writes no such member when
  it is `null`. The name is 1 to 64 of `[A-Za-z0-9_-]`, the wire limit.
- **Schema as given.** The schema is never rewritten into a provider's strict dialect (every
  object with `additionalProperties: false`, every property in `required`, optional values as
  `["string","null"]`); the caller builds a schema the provider accepts.
- **Checked subset.** `type` (a name or an array of names; `integer` is a number without a
  fractional part, so `1.0` counts), `required`, `enum` (equality by JSON value, so `1` equals
  `1.0`), `properties`, `items` (one schema), and a boolean `additionalProperties` (`false`
  rejects an undeclared property; absent or `true` keeps it unchecked), recursively. The
  annotations `title`, `description`, `$comment`, `$schema`, `default` and `examples` are
  accepted. Any other keyword (`$ref`, `$defs`, combinators, conditionals, `const`, `format`,
  `pattern`, `minimum`, …), a boolean schema or an `additionalProperties` schema is an
  `ArgumentException` at construction naming the keyword and its JSON Pointer: a schema never
  promises a check the format would skip. So is a schema that can never match as written: an
  `enum` value its `type` excludes, an `enum` number whose exponent is outside the 32-bit range,
  or a `required` name that `additionalProperties: false` forbids. Value checks beyond the type
  are the caller's.
- **Strict parse.** The reply, after surrounding whitespace and one surrounding Markdown code
  fence, is exactly one JSON document. The fence's opening line may carry only a bare language
  tag (`json`, `JSON5`): anything else there, such as `` ```[1] ``, would be answer text the fence
  removal drops, so it is `Malformed`. No comments, trailing commas, trailing content, duplicate
  properties, nesting beyond 64 levels or escaped unpaired surrogates. `ParseAsync` accepts only
  `TOutput = JsonNode` (anything else is an `InvalidOperationException`) and returns a new node the
  caller owns, or `null` for a `null` document the schema allows. The `strict` flag affects only
  the request; the local parse is strict either way.
- **Errors without content.** `OutputFormatException : FormatException` carries `Path` (`$`,
  `$.total`, `$.lines[2].sku`, `$['line items']`) and `Violation` (`Empty`, `Truncated`,
  `Malformed`, `WrongType`, `MissingRequired`, `NotInEnum`, `UnexpectedProperty`). Its message is
  fixed per violation and names only the path; path names come from the schema, never from the
  reply, and there is no inner exception (the JSON parser's messages quote reply text).
- **Not registered.** The format has no dependencies and its schema is per call, so the module
  does not register it; `IOutputFormat.Create` still maps types to the two container formats.
  Instances are immutable and thread-safe.

## The agent loop

`IAgentLoop.RunAsync(agent, client, tools, conversation, options, ct)` runs one agent until the
model answers without tool calls (`Completed`) or `MaxSteps` model calls are made
(`StepLimitReached`, incomplete):

- **Step.** One `CompleteAsync` with `options.ModelName`, the agent's `Tools` and its
  `ModelParameters` as `Sampling` (`null` when empty; `N` is dropped), and
  `options.ResponseFormat` (optional, default none) as `ResponseFormat` on every step, tool-calling
  ones included; the loop never checks the answer against it. The loop prepends the
  agent's rendered system message; a conversation that already holds one is an
  `ArgumentException`.
- **Tools.** Each call runs in the order asked, through the product's `IToolInvoker`, and its
  result is appended as a `ToolMessage`. The last step's calls run too, so a "record this" call is
  never lost. A name the agent does not offer (ordinal) and an invoker exception become a failed
  tool result; the run goes on.
- **Errors.** Model client exceptions propagate. Cancellation is checked before every model and
  tool call; an `OperationCanceledException` for the run's token ends the run, one from a tool's
  own timeout is a failed tool result.
- **Result.** `AgentLoopResult` carries the outcome, the final answer, the full transcript, the
  step count and the summed `TokenUsage` (`null` when no step reported any).

The loop is a stateless singleton and never disposes the client. What a tool may touch, and in
whose name, is the invoker's (the product's) decision.

## `ITextSerializer` vs `Common`'s `ISerializer`

`Nordstein.Core.Common.Serialization.ISerializer` is the general stream-based serialization
seam. `Nordstein.Core.AI.Serialization.ITextSerializer` is string-based and LLM-flavored: it
exists to turn model output text — including truncated or slightly malformed JSON — into typed
values, and values into prompt-embeddable text. They are different contracts; the AI one was
renamed at extraction precisely so the two never collide in a consumer's usings.

## Registration

Register the module alongside the product's own domain module:

```csharp
builder.RegisterModule<Nordstein.Core.AI.Module>();
```

It wires: domain-object generator discovery for this assembly (via
`Nordstein.Core.Domain.Module` — the foundation guard makes double registration safe), the
`Content` and `ToolArguments` JSON converters (registered `As<JsonConverter>` for the
consumer's `JsonSerializerOptions` composition), `ITextSerializer`, the output formats, and the
`IOutputFormat.Create` factory (string → `StringOutputFormat`, everything else →
`JsonOutputFormat` for the requested type), and the `IAgentLoop` singleton. `JsonSchemaOutputFormat` is constructed directly, not
resolved.

## Testing

`Nordstein.Core.AI.Tests` follows [testing.md](testing.md): value types get plain MSTest +
AwesomeAssertions classes; container-backed tests (prompt factories, content serialization,
output formats, the agent loop on a substituted `IModelClient`) run on `BaseTest<Nordstein.Core.AI.Module>`.

## The boundary (One Rule, AI edition)

Generic AI vocabulary lives here. Product concepts never do: traces/call records, projects and
tenancy, agent version history, endpoints/providers/pricing, evaluators, test runs, ingestion,
search. When a product needs both, the product extends the Core contract — the arrow never
points back.
