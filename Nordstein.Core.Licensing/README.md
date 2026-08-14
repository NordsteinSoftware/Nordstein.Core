# Nordstein.Core.Licensing

Product-agnostic licensing engine for Nordstein applications.

- `JwtLicenseValidator` (internal) — ES256 (and legacy RS256) license JWT verification against
  injected base64-SPKI public keys, issuer, and audience; projects the `tier`, `feat`, and `lim`
  claims onto a `LicenseSnapshot`
- `ILicenseService` — the current `LicenseSnapshot` (never null; falls back to the free tier),
  feature/limit queries, a `Changed` event, and forced refresh
- `ILicenseActivator` — validate/activate license JWTs at runtime without a restart, including
  the never-throwing degradation paths
- The background revocation check — a periodic license-server call with a persisted two-stage
  offline-grace state machine (Active → Grace → fallback), plus offline-only (air-gapped) license
  support where the JWT's `exp` is enforced locally and the server is never contacted
- `LicensingModule` — Autofac wiring for all of the above (requires the Nordstein.Core.Common
  module)

The engine knows no product concepts. The consuming product supplies its identity and policy:

- `LicensingConfiguration` — issuer, audience, trusted public keys, the configured JWT or a
  pre-resolved override snapshot, server URL, and check/grace tuning
- `ILicenseTierPolicy` — the canonical tier/feature/limit vocabulary (also the JWT claim values,
  so it is a wire-format contract) and the entitlements each tier grants

```csharp
builder.RegisterModule(new LicensingModule(configuration, new MyProductTierPolicy()));
```

Products typically keep enum-typed façades over `ILicenseService`/`ILicenseActivator` so call
sites stay strongly typed while the engine stays generic.
