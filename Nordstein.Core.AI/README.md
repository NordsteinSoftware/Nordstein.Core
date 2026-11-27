# Nordstein.Core.AI

Product-agnostic AI foundations for Nordstein applications.

- **Messages** — `SystemMessage`, `UserMessage`, `AssistantMessage`, `ToolMessage`,
  `Conversation`, tool requests/responses, and content parts.
- **Tools** — `ToolSpecification` and JSON-schema tool arguments.
- **Prompts** — `IPromptTemplate` / `IPrompt` with `{variable}` rendering.
- **Completions** — `ICompletion`, `TokenUsage`, `TypedCompletion<T>`, `IModelParameters`.
- **Clients** — the `IModelClient` contract (typed + streaming + request preview) and its
  option records, including the `response_format` a request carries (`JsonSchemaResponseFormat`),
  and its two transport errors: `InferenceUnavailableException` (transient) and
  `InferenceContractException` (permanent; products derive from it).
  Provider implementations live in the consuming product.
- **Embeddings** — the `IEmbeddingClient` contract: one model's output space (`ModelId`,
  `Dimension`), one vector per input in order. Provider implementations live in the product.
- **Agents** — the versionless `IAgent` contract: name, system prompt, tools, model
  parameters. Product concerns (tenancy, version history, endpoints) extend it product-side.
  `IAgentLoop` runs an agent on an `IModelClient` and executes its tool calls through a
  product's `IToolInvoker` until a final answer or `AgentLoopOptions.MaxSteps`.
- **Serialization** — structured model-output parsing: `IOutputFormat`, the strict
  `JsonSchemaOutputFormat` over a runtime JSON Schema (`OutputFormatException` on a truncated or
  non-conforming reply), `JsonSchemaExtensions.WithObjectsClosed` (the opt-in step that closes every
  object schema for a strict provider), `ITextSerializer`,
  truncated-JSON repair, and `ModelReplyExtensions` (`StripCodeFence`, `TryStripReasoning`) for
  cutting a code fence or a leading `<think>` block off a raw reply.

Register `Nordstein.Core.AI.Module` in the consuming container; it wires the domain-object
generators, JSON converters, serializer, output formats, and the agent loop.
