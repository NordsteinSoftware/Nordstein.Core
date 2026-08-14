# Nordstein.Core.AI — the AI/agent foundation

`Nordstein.Core.AI` holds the generic AI vocabulary every Nordstein AI product would otherwise
reimplement: messages, tools, prompts, completions, the agent and model-client contracts, and
structured model-output parsing. It depends on `Nordstein.Core.Common` and
`Nordstein.Core.Domain` only; everything else is BCL (`System.Text.Json`, plus the
Microsoft-owned `System.Memory.Data` for `BinaryData` content payloads).

## What is in the package

| Namespace | Contents |
|-----------|----------|
| `Nordstein.Core.AI.Agents` | `IAgent` — the versionless agent contract: `Name`, `SystemPrompt`, `Tools`, `ModelParameters`, `CreateSystemMessage` |
| `Nordstein.Core.AI.Messages` | `Message` hierarchy (`SystemMessage`, `UserMessage`, `AssistantMessage`, `ToolMessage`), `Conversation`, `Content`/`ContentKind`/`Role`, `ToolRequest`/`ToolResponse`, `ToolRequestMatch` (canonical-JSON tool-call comparison) |
| `Nordstein.Core.AI.Tools` | `ToolSpecification`, `ToolArguments` (JSON-schema generation), `IToolArgument`/`JsonToolArgument` |
| `Nordstein.Core.AI.Prompts` | `IPromptTemplate`/`IPrompt` — `{variable}` templates and their rendered form. Persistence (a template repository) is deliberately absent — products own storage |
| `Nordstein.Core.AI.Completions` | `ICompletion`, `TokenUsage`, `TypedCompletion<TOutput>`, `IModelParameters` |
| `Nordstein.Core.AI.Clients` | `IModelClient` (typed + streaming completion, request preview), `ModelOptions`, `ModelSamplingParameters`, `ModelStreamUpdate` variants, request-preview records |
| `Nordstein.Core.AI.Serialization` | `IOutputFormat` (parse + validate model output against a type, plus the prompt instruction telling the model how to format), `ITextSerializer`, truncated-JSON repair |

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
`JsonOutputFormat` for the requested type).

## Testing

`Nordstein.Core.AI.Tests` follows [testing.md](testing.md): value types get plain MSTest +
AwesomeAssertions classes; container-backed tests (prompt factories, content serialization,
output formats) run on `BaseTest<Nordstein.Core.AI.Module>`.

## The boundary (One Rule, AI edition)

Generic AI vocabulary lives here. Product concepts never do: traces/call records, projects and
tenancy, agent version history, endpoints/providers/pricing, evaluators, test runs, ingestion,
search. When a product needs both, the product extends the Core contract — the arrow never
points back.
