# Nordstein.Core.AI.OpenAiCompatible

An `IEmbeddingClient` for endpoints that speak the OpenAI embeddings wire format
(`POST {base}/embeddings`): OpenAI, Scaleway Generative APIs, Ollama, vLLM, TEI, llama.cpp.
HTTP and `System.Text.Json` only; no provider SDK.

```csharp
builder.RegisterModule<Nordstein.Core.Common.Module>();
builder.RegisterModule<Nordstein.Core.AI.OpenAiCompatible.Module>();
builder.Register(context => context.Resolve<OpenAiCompatibleEmbeddingClientFactory>()(new OpenAiCompatibleEmbeddingOptions
{
    BaseUrl = new Uri("https://api.example.com/v1"),
    ApiKey = apiKey,
    Model = "example-embed-1",
    Dimension = 1024,
})).As<IEmbeddingClient>().SingleInstance();
```

- Requests reach only the configured host: no proxy, no redirect.
- An outage is `InferenceUnavailableException`; a rejected request or a reply of the wrong shape
  is `InferenceContractException`. No message carries the key, the inputs or the reply.

See [`docs/ai.md`](../docs/ai.md#the-openai-compatible-embedding-client).
