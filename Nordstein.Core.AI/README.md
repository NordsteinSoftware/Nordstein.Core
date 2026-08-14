# Nordstein.Core.AI

Product-agnostic AI foundations for Nordstein applications.

- **Messages** — `SystemMessage`, `UserMessage`, `AssistantMessage`, `ToolMessage`,
  `Conversation`, tool requests/responses, and content parts.
- **Tools** — `ToolSpecification` and JSON-schema tool arguments.
- **Prompts** — `IPromptTemplate` / `IPrompt` with `{variable}` rendering.
- **Completions** — `ICompletion`, `TokenUsage`, `TypedCompletion<T>`, `IModelParameters`.
- **Clients** — the `IModelClient` contract (typed + streaming + request preview) and its
  option records. Provider implementations live in the consuming product.
- **Agents** — the versionless `IAgent` contract: name, system prompt, tools, model
  parameters. Product concerns (tenancy, version history, endpoints) extend it product-side.
- **Serialization** — structured model-output parsing: `IOutputFormat`, `ITextSerializer`,
  truncated-JSON repair.

Register `Nordstein.Core.AI.Module` in the consuming container; it wires the domain-object
generators, JSON converters, serializer, and output formats.
