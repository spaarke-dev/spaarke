# Plan — Retire the Dataverse Advisory Checkout / Checkin Model

> **Drafted**: 2026-07-30 (spaarkeai-compose-r5, task 052 follow-on) · **Status**: PROPOSAL — needs its own project (`/design-to-spec`).
> **Owner ask**: remove the checkout/in service and whatever friction it causes; it's an outdated model.
> **Scope note**: this is **cross-cutting** (touches non-Compose surfaces) — do NOT execute inside a Compose UAT task. Promote to a standalone project.

## Why retire it
The **Dataverse advisory checkout** (`DocumentCheckoutService` + `sprk_checkedoutdate`/`sprk_lastheartbeatutc`, a heartbeat, and a 15-min stale-sweeper) is an app-level "someone is editing this" soft-lock. It is:
- **Low value**: a courtesy presence signal, not a data-safety mechanism.
- **Redundant**: lost-update safety is already provided by the FR-08 **stale-base re-anchor** on save (if the base moved, the op-log is re-anchored, not overwritten) + tracked-changes + SPE version history.
- **High friction**: checkout/checkin prompts ("check out?" / "check in?" — users pick "without"), the misleading "checked out" wording that confused UAT #10/#11, and a stale-flag **"can't save"** window if the sweeper lags/doesn't run in an environment.
- **Orthogonal to the real Word lock**: it does NOT cause and cannot fix the Word-for-web co-authoring 423 (that's SharePoint/WOPI, no programmatic release).

## What replaces it (nothing new to build)
- **Concurrent-edit safety**: FR-08 stale-base re-anchor (already shipped) + SPE driveItem versioning (auto per save) + tracked changes.
- **"Someone else is editing" awareness** (if still wanted): Word-for-web co-authoring already shows presence; for Compose-vs-Compose, an optional lightweight last-writer-wins + re-anchor is already the behavior. No checkout needed.

## Consumer inventory (grep 2026-07-30 — 10 files)
| File | Role | Retirement action |
|---|---|---|
| `Services/DocumentCheckoutService.cs` | the service | delete (after consumers migrated) |
| `Services/Compose/StaleCheckoutSweeperHostedService.cs` | 15-min stale-flag sweeper | delete |
| `Api/DocumentOperationsEndpoints.cs` | R1 `/checkout` `/checkin` `/discard` endpoints | remove endpoints (or 410 Gone) — **audit external callers first** |
| `Api/ComposeEndpoints.cs` | Compose `checkout`/`checkin` **stubs** (already no-op pointers) | remove the stub routes + `checkoutStatus` plumbing |
| `Api/FileAccessEndpoints.cs` | **non-Compose** consumer | **AUDIT** — confirm what it uses checkout for before removing |
| `Services/Identity/SystemUserIdentityResolver.cs` | system-user identity for sweeper release | drop the checkout usage |
| `Services/Compose/SpeSyncOrchestrator.cs` | references checkout state | decouple |
| `Infrastructure/Graph/SpeFileStore.cs` | facade surface | remove any checkout methods (confirm none are the real Graph checkout) |
| `Infrastructure/DI/DocumentsModule.cs` | DI registration | remove registration (ADR-032 kill-switch if a phased flag is wanted) |
| `client/.../ComposeConflictDialog.tsx` + `useComposeCheckoutLifecycle.ts` + `checkoutStatus` in `ComposeWorkspace.types.ts`/`ComposeBannerStack.tsx` | client checkout UX (conflict dialog, "force-close other session", banners) | remove the checkout UX; keep the honest Word-lock bar (task 052) |

## Phased approach (each phase its own PR)
1. **Audit + decision (spike)** — confirm every consumer's real dependency, especially `FileAccessEndpoints` (why does file access touch checkout?) and any EXTERNAL callers of the R1 `/checkout`/`/checkin`/`/discard` endpoints (Office add-ins? PCFs? ribbons?). Output: a go/no-go per consumer + a compatibility decision for the public endpoints. **HARD GATE** — do not proceed until external-caller surface is known.
2. **Client removal** — delete the checkout conflict dialog + lifecycle hook + `checkoutStatus` banners/plumbing; Compose keeps only the task-052 honest Word-lock bar. (Client-only; reversible.)
3. **Endpoint deprecation** — make `/checkout`/`/checkin`/`/discard` return **410 Gone** (or remove) once no caller remains; remove the Compose stub routes.
4. **Service + sweeper removal** — delete `DocumentCheckoutService`, `StaleCheckoutSweeperHostedService`, DI registration; decouple `SpeSyncOrchestrator` / `SystemUserIdentityResolver`.
5. **Schema** — decide whether to retire `sprk_checkedoutdate`/`sprk_lastheartbeatutc` columns (data-model change — owner + Dataverse gate) or leave them dormant.
6. **Docs + tests** — update any checkout references; delete checkout tests (KEEP-path check: they're not regression-protectors of a retained behavior); add a regression test that a doc open in Word surfaces the honest 052 bar (already added).

## Risks / open questions
- **External endpoint callers**: the R1 `/checkout` endpoints may be consumed by Office add-ins / ribbons / other clients not visible in `src/` — Phase 1 must confirm before any removal (breaking-change gate, root §6).
- **`FileAccessEndpoints` usage**: unclear why file access references checkout — must understand before removing.
- **BFF Hygiene §10 / publish size**: net removal (should reduce size); still verify.
- **ADR-032 kill-switch**: if a reversible rollout is wanted, gate the service off behind a flag first (Phase 4 becomes flag-flip then delete).

## Recommendation
Promote to a standalone project via `/design-to-spec` (e.g. `document-checkout-retirement-r1`), starting with the Phase-1 audit spike (external-caller surface is the key unknown). It is **not** in scope for compose-r5 — this doc is the hand-off.
