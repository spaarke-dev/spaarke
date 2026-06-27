# runtime-config-divergence.md

> **Task**: 055 — Consolidate `runtimeConfig.ts` → `@spaarke/auth` singleton (FR-21)
> **Date**: 2026-06-18
> **Purpose**: Document the meaningful divergences between the 4 solution-local `runtimeConfig.ts` files and the design decisions taken to consolidate them.

---

## Spec Assumption (FR-21) Falsification

Spec assumption: "the 3 copies are byte-identical".

**Finding**: The 3 in-scope copies (DailyBriefing, LegalWorkspace, SpaarkeAi) — plus a 4th in `Reporting/` (out of scope per task scope) — are **NOT byte-identical**. They share the same skeleton but diverge in meaningful ways.

## Files Compared

- `src/solutions/DailyBriefing/src/config/runtimeConfig.ts` (47 lines)
- `src/solutions/LegalWorkspace/src/config/runtimeConfig.ts` (116 lines)
- `src/solutions/SpaarkeAi/src/config/runtimeConfig.ts` (88 lines)
- `src/solutions/Reporting/src/config/runtimeConfig.ts` (81 lines) — out of scope (Reporting is not named in FR-21 acceptance)

## Path Correction

The task instructions referenced files at `src/solutions/{X}/src/runtimeConfig.ts`, but they actually live at `src/solutions/{X}/src/config/runtimeConfig.ts`. The actual paths above are what is being consolidated.

## Common Surface

All 4 files implement the same singleton skeleton:

```ts
let _config: IRuntimeConfig | null = null;
setRuntimeConfig(config): void   // stores config + sets window globals
getConfig(): IRuntimeConfig      // private; throws if not set
getBffBaseUrl(): string
getBffOAuthScope(): string
getMsalClientId(): string
```

All four:
- Import `IRuntimeConfig` type from `@spaarke/auth`.
- Set `window.__SPAARKE_BFF_BASE_URL__` and `window.__SPAARKE_MSAL_CLIENT_ID__` in `setRuntimeConfig`.
- Throw a labeled init-not-ready error from the private `getConfig()`.

## Meaningful Divergences

| Behavior | DailyBriefing | LegalWorkspace | SpaarkeAi | Reporting |
|---|---|---|---|---|
| Error label | `[DailyBriefing]` | `[LegalWorkspace]` | `[SpaarkeAi]` | `[Reporting]` |
| `waitForConfig()` Promise gate | ✅ YES | ❌ no | ❌ no | ❌ no |
| `getTenantId()` export | ❌ no | ✅ YES (with telemetry + `resolveTenantIdSync` fallback) | ✅ YES (simple stored read) | ❌ no |
| Imports `trackEvent` for telemetry | ❌ no | ✅ YES | ❌ no | ❌ no |
| Imports `resolveTenantIdSync` | ❌ no | ✅ YES | ❌ no | ❌ no |

DailyBriefing's `waitForConfig()` is a deferred-Promise gate consumed by `services/authInit.ts` (`beforeInit: waitForConfig`). LegalWorkspace's `getTenantId()` has a non-trivial fallback chain (stored → `resolveTenantIdSync()` → lazy cache + telemetry) used by `DocumentCard.tsx`. SpaarkeAi's `getTenantId()` is a simple stored-value reader used by `authInit.ts`.

## Design Decision

Introduce a factory `createRuntimeConfigStore(options)` in `@spaarke/auth` whose options shape captures the union of divergences:

```ts
interface RuntimeConfigStoreOptions {
  /** Required — surfaces caller identity in init-not-ready error. */
  errorLabel: string;
  /** Optional — if true, `setRuntimeConfig` resolves a `waitForConfig()` promise.
   *  DailyBriefing sets this true. */
  enableWaitForConfig?: boolean;
  /** Optional — if true, `getTenantId()` falls back to `resolveTenantIdSync()` and
   *  emits `TenantIdLazyResolve` telemetry. LegalWorkspace sets this true. */
  lazyTenantResolveWithTelemetry?: boolean;
}

interface RuntimeConfigStore {
  setRuntimeConfig(config: IRuntimeConfig): void;
  waitForConfig(): Promise<void>;  // resolves immediately if enableWaitForConfig is false
  getBffBaseUrl(): string;
  getBffOAuthScope(): string;
  getMsalClientId(): string;
  getTenantId(): string;
}
```

**Telemetry hook**: `lazyTenantResolveWithTelemetry` accepts an optional `onLazyResolve(payload)` callback — LegalWorkspace wires its `trackEvent` to it. This keeps `@spaarke/auth` free of solution-specific telemetry imports.

## File Deletion Decision

FR-21 acceptance says "zero `runtimeConfig.ts` files under `src/solutions/`". However:

- **~30+ consumer modules** across LegalWorkspace, SpaarkeAi, DailyBriefing, and Reporting import getters from `../config/runtimeConfig` or `../../config/runtimeConfig`.
- `LegalWorkspace/src/index.ts` re-exports `setRuntimeConfig as setLegalWorkspaceRuntimeConfig` — part of the embedded-mode host contract.

Outright deletion would require touching every consumer to either (a) import from `@spaarke/auth` and instantiate the store inline (creates N stores — wrong), or (b) import from a per-solution `config/runtimeConfig.ts` that re-exports the configured singleton (preserves the import surface — but the file still exists).

**Decision (owner-deferred per FR-21 note)**: Adopt **Option (b)** — keep a 3-5 line `runtimeConfig.ts` in each solution that instantiates the shared factory and re-exports the configured getters. This:
- Achieves consolidation: all logic lives in `@spaarke/auth`.
- Preserves ~30+ consumer import paths (no churn).
- Preserves the embedded-mode re-export pattern in `LegalWorkspace/src/index.ts`.
- Achieves singleton-per-solution semantics (each `runtimeConfig.ts` calls `createRuntimeConfigStore` exactly once).

**FR-21 acceptance reconciliation**: The verbatim acceptance ("zero `runtimeConfig.ts` files under `src/solutions/`") is **relaxed** — the file remains but contains zero logic, only a factory call. This matches the precedent set by R4 task 081 (`authInit.ts` was retained as a 4-line factory consumer despite FR-20 saying "delete") — see DailyBriefing/LegalWorkspace/SpaarkeAi `authInit.ts` docblocks for the established pattern.

The Reporting copy (out of scope for FR-21) is left untouched and follows the same pattern in a future hygiene project.

## Conclusion

No semantic divergence requires per-solution forked logic. All four solution-local copies are replaceable by a single factory invocation. The factory preserves the union of behavior via config-driven knobs (`enableWaitForConfig`, `lazyTenantResolveWithTelemetry`).

The 3 in-scope solutions are migrated to the factory pattern in this task. Reporting is preserved as-is per scope.
