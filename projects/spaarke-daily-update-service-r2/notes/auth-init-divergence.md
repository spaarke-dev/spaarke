# auth-init-divergence.md

> **Task**: 052 — Create `createCodePageAuthInitializer` factory in `@spaarke/auth` (FR-20a)
> **Date**: 2026-06-18
> **Purpose**: Document the meaningful divergences between the 3 solution-local `authInit.ts` files and the design decisions taken to consolidate them.

---

## Files Compared

- `src/solutions/DailyBriefing/src/services/authInit.ts`
- `src/solutions/LegalWorkspace/src/services/authInit.ts`
- `src/solutions/SpaarkeAi/src/services/authInit.ts`

(Also exist in `Reporting/` and `SpeAdminApp/` — out of scope for R2 per FR-20 acceptance, which only names the three above.)

## Common Structure (Union)

All three implement the same skeleton:

```ts
let _initPromise: Promise<void> | null = null;

ensureAuthInitialized(): Promise<void>           // once-only init, retry on failure
authenticatedFetch(url, init?): Promise<Response> // awaits ensure then delegates to @spaarke/auth
getTenantId(): Promise<string>                    // awaits ensure then delegates to provider
```

All three:
- Call `initAuth({ clientId, bffBaseUrl, bffApiScope, ... })`.
- Use `console.info` on success, `console.warn` on failure.
- Re-throw + null out `_initPromise` on failure to allow retry.

## Meaningful Divergences

| Behavior | DailyBriefing | LegalWorkspace | SpaarkeAi | How factory handles |
|---|---|---|---|---|
| `proactiveRefresh` | `false` (short-lived dialog) | `true` | `true` | Config option (default `true`) |
| `tenantId` passed to `initAuth` | NO (relies on Xrm fallback) | YES (`getRuntimeTenantId()`) | YES (`getRuntimeTenantId()`) | Optional config field — passed through only if provided |
| `await waitForConfig()` before `initAuth` | YES | NO | NO | Optional `beforeInit` async hook on config |
| Console log prefix | `[DailyBriefing]` | `[authInit]` | `[SpaarkeAi:authInit]` | Required `logLabel` config field |

## Design Decision

All three divergences are reconcilable via the factory `config` shape. The factory signature accepts:

```ts
interface CodePageAuthInitConfig {
  clientId: string;
  bffBaseUrl: string;
  bffApiScope: string;
  tenantId?: string;                  // optional — Xrm fallback when omitted (DailyBriefing case)
  proactiveRefresh?: boolean;         // default true; pass false for short-lived dialogs
  logLabel: string;                   // required — surfaces caller identity in console
  beforeInit?: () => Promise<void>;   // optional — e.g. await waitForConfig() (DailyBriefing case)
}
```

**Rationale for `beforeInit` hook**: `waitForConfig()` is part of DailyBriefing's `runtimeConfig` module, NOT part of `@spaarke/auth`. FR-21 (a separate task) consolidates `runtimeConfig.ts` into the shared library — at that point the hook may become unnecessary. For R2 task 052, keeping `beforeInit` as an opt-in hook avoids cross-task coupling and preserves DailyBriefing's exact call sequence.

**Rationale for `logLabel` required**: Every existing copy emits a labeled log line; the factory must preserve that for debuggability. Defaulting to a generic label would hide the caller's identity in production logs.

## Out-of-Scope Files

`Reporting/` and `SpeAdminApp/` are NOT named in FR-20 acceptance criteria and are NOT migrated in tasks 053 + 054. They retain their solution-local `authInit.ts`. A future hygiene project (`spaarke-shared-lib-hygiene-r1`) may pick them up.

## Conclusion

No semantic divergence requires per-solution forking. All three solution-local copies will be replaceable by a single factory invocation in their respective `main.tsx` files (tasks 053 + 054 + a future task for SpaarkeAi). The factory preserves the union of behavior with config-driven knobs.
