# Nordstein.Core.Domain

Reusable domain foundations for Nordstein applications.

- Domain object and entity contracts
- Repository, archive, and transaction contracts
- Domain entity and test-data generator base classes
- Paging records and persistence exceptions
- In-process entity change notifications
- Autofac discovery for entities and generators in consuming assemblies

Register a product's domain assembly explicitly:

```csharp
builder.RegisterModule(new Nordstein.Core.Domain.Module(typeof(Product.Domain.Module).Assembly));
```

Core never scans all loaded assemblies and never assumes product types live beside the Core module.
