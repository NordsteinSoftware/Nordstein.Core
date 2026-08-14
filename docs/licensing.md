# Nordstein.Core.Licensing — the generic license engine

The product-agnostic half of licensing: JWT verification, activation, the resolved license
snapshot, and the periodic server check with offline grace. What a tier, feature, or limit
*means* is entirely the product's business — the engine only enforces the mechanics.

## The split

| Core (this package) | Product |
|---|---|
| `JwtLicenseValidator` — signature/issuer/audience/expiry verification of a license JWT | Issuer, audience, and trusted public keys (`LicensingConfiguration`) |
| `LicenseService` / `ILicenseService` — the current `LicenseSnapshot`, `HasFeature`/`GetLimit` queries (string vocabulary), `Changed` event, `ForceRefreshAsync` | A strongly-typed façade over the string vocabulary (feature/limit enums) |
| `ILicenseActivator`, `ConfiguredLicenseResolver`, `LicenseSource`/`LicenseStatus`, `InvalidLicenseException` | Where the JWT comes from (environment, storage) and the operator UX around it |
| The background server check (revocation, offline grace), gated by `ServerCheckEnabled` | Which process owns the heartbeat vs merely consumes the snapshot |
| `ILicenseTierPolicy` — the seam through which the product's vocabulary enters the engine | `ILicenseTierPolicy` implementation: canonical tier/feature/limit names and per-tier entitlements |

Two contract details worth internalizing before touching anything here:

- **The canonical names are a wire format.** Tier/feature/limit names double as the JWT claim
  values (`tier`, `feat`, `lim`) — renaming one invalidates licenses already issued. Raw values
  are matched case-insensitively via `TryResolve*`; unknown tiers resolve to the policy's
  `FallbackTier` rather than throwing, so a newer license never crashes an older deployment.
- **The engine fails toward the fallback tier, never toward "unlicensed crash".**
  `ILicenseService.Current` is never null; missing limits read as 0 and `long.MaxValue` means
  unlimited. `OverrideSnapshot` bypasses verification entirely for kiosk/demo deployments —
  treat that path as security-sensitive when reviewing.

## Consuming it

The product's composition root supplies a `LicensingConfiguration` (issuer, audience, server URL,
public keys, optional JWT/override) and an `ILicenseTierPolicy`, and decides `ServerCheckEnabled`
per host: in a multi-process deployment exactly **one** process owns the license-server heartbeat
and the offline-grace cache; secondary hosts (e.g. a standalone proxy) run with the check
disabled and only consume the stored snapshot.

## Review focus for this package

Licensing is an enforcement boundary — apply extra adversarial scrutiny to: signature/claims
validation order, clock handling around expiry and grace (always `DateTimeOffset`, always through
the `IClock` seam so tests can travel in time), the behavior when the license server is
unreachable, and any change that could widen what `OverrideSnapshot` accepts.
