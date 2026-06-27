# task-053-factory-config-timing.md

> **Task**: 053 — Migrate DailyBriefing to `createCodePageAuthInitializer`
> **Date**: 2026-06-18
> **Status**: Resolved via lazy-factory wrapper (no factory change)
> **Affects**: Task 054 (LegalWorkspace + SpaarkeAi migrations)

---

## Finding: Eager vs. deferred config consumption

The `createCodePageAuthInitializer` factory (task 052) captures `clientId`,
`bffBaseUrl`, `bffApiScope`, `tenantId` from its `config` argument at
**factory construction time** (closure capture in `createCodePageAuthInitializer.ts:142-150`).

DailyBriefing's runtime config is NOT available at module load — it's populated
by `setRuntimeConfig(...)` in `main.tsx` AFTER `resolveRuntimeConfig()` resolves.
The original local `authInit.ts` worked because the credential getters
(`getMsalClientId()` etc.) were called INSIDE the once-only init IIFE — i.e.
after `await waitForConfig()`.

**Naive port** (factory call at module load, eagerly invoking the getters)
throws "Runtime config not initialized." at import time.

## Resolution (no factory change)

Wrap factory construction in a **lazy singleton** inside DailyBriefing's
`services/authInit.ts`:

```ts
let _initializer: CodePageAuthInitializer | null = null;

function getInitializer(): CodePageAuthInitializer {
  if (!_initializer) {
    _initializer = createCodePageAuthInitializer({
      clientId: getMsalClientId(),   // safe: by first method call, setRuntimeConfig has fired
      bffBaseUrl: getBffBaseUrl(),
      bffApiScope: getBffOAuthScope(),
      proactiveRefresh: false,
      logLabel: 'DailyBriefing',
      beforeInit: waitForConfig,
    });
  }
  return _initializer;
}

export function ensureAuthInitialized(): Promise<void> {
  return getInitializer().ensureAuthInitialized();
}
// ...etc
```

The exports remain `ensureAuthInitialized`, `authenticatedFetch`, `getTenantId`
— byte-identical surface from any consumer's perspective. `briefingService.ts`
needs no changes.

## Why not patch the factory (task 052)?

- R2 task 053 is forbidden from modifying `@spaarke/auth` per main-session
  parallel-execution constraints.
- LegalWorkspace + SpaarkeAi (task 054) consume `getRuntimeTenantId()` /
  `getMsalClientId()` from their respective runtime configs that ARE available
  at module load (they don't use a `waitForConfig` pattern). So they MAY be
  able to use the direct factory call without a lazy wrapper.
- A future hygiene project could add a `lazyConfig: () => CodePageAuthInitConfig`
  factory overload to standardize the lazy pattern across all consumers. Not
  in R2 scope.

## Impact on task 054

**Likely none for LegalWorkspace + SpaarkeAi:**
- Their `runtimeConfig` modules resolve synchronously at module load (no
  `waitForConfig` pattern). The naive port should work.
- Task 054 should still verify by reading each `main.tsx`'s bootstrap order
  and confirming no eager `getX()` call would fail before `setRuntimeConfig`.

**If task 054 hits the same issue**, the lazy-singleton wrapper above is the
proven pattern. Copy-paste with adjusted `logLabel` + `tenantId` (LW/SAi pass
tenantId explicitly).

## Files changed

- `src/solutions/DailyBriefing/src/services/authInit.ts` — replaced local
  divergent implementation with lazy-factory wrapper.

## Files NOT changed

- `src/solutions/DailyBriefing/src/main.tsx` — unchanged. Still imports
  `ensureAuthInitialized` from `./services/authInit`. The factory consumption
  is encapsulated in `authInit.ts`.
- `src/solutions/DailyBriefing/src/services/briefingService.ts` — unchanged.
  Still imports `authenticatedFetch` from `./authInit`.
- `src/client/shared/Spaarke.Auth/` — unchanged (forbidden by task scope).
