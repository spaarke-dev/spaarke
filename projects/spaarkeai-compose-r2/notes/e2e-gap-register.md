# E2E Completeness Gap Register — spaarkeai-compose-r2

> **Created**: 2026-07-09 by a 5-slice parallel read-only audit (entry/create-on-save, AI dispatch, Word shuttle, memory/annotations, three-pane/deploy).
> **Why**: the owner caught that create-on-save's `containerId` was never wired end-to-end despite task 013 being ✅. This audit checked whether the same false-green class exists elsewhere. **It is systemic.**
> **Root cause**: every phase was marked ✅ on unit/build/orchestrator tests that never exercise the vertical slice (user action → client → HTTP body → server DTO → service → data/SPE/ledger → back). The feature is *built in halves* — server endpoints, client components, and service methods all exist and unit-pass, but the wires between them are missing.
> **Meta-gap (anti-recurrence)**: there is NO HTTP-boundary vertical-slice test for any compose flow (task 084 unstarted). That absence is *why* false-greens ship. Every fix below MUST land with a through-the-wire test.
> **Directive**: fix, do not defer (owner, 2026-07-09). This register is the fix backlog, replacing piecemeal DEF entries.

---

## Systemic pattern (one sentence)
Server/component/service halves are built and unit-green; the **client→HTTP→server wire**, the **component→workspace mount**, the **subscription/registration origin call**, the **catalog deploy**, and the **config provisioning** are systematically absent — and no through-the-wire test would catch it.

---

## CLUSTER 1 — Create-on-save (new file → first Save → `sprk_document`) is DEAD over HTTP
**Verdict: 0 of 5 pipeline steps run E2E for a Browse/Upload draft. Loads + replace-save are fine.**

| # | Gap | Broken hop | Sev | Marked ✅ by |
|---|---|---|---|---|
| 1.1 | Save route requires non-empty `documentSpeId`; transient has none → `/documents//save` 404/400 | `ComposeEndpoints.cs:70,995` | BLOCKER | 013 |
| 1.2 | `SaveComposeDocumentBody` has no `containerId` + hard-requires `driveId` (DEF-06, widened) | `ComposeEndpoints.cs:1235-1241,997,1008-1017` | BLOCKER | 013 |
| 1.3 | **Root**: no client code resolves BU containerId; `EntityCreationService`/`resolveBusinessUnitContainerId` never imported by compose; `App.tsx` sets `containerId: undefined` | `App.tsx:180`, `ComposeWorkspace.types.ts:201` | BLOCKER | 013 |
| 1.4 | `triggerSave` aborts locally — `effectiveDriveId` empty for transient mounts | `ComposeWorkspace.tsx:527` | BLOCKER | 013/015 |
| 1.5 | Save button double-locked: `!isDirty` (DEF-07) **and** `!hasDocument` (`documentId===''` for transient) | `ComposeToolbar.tsx:155,95`; `ComposeWorkspace.tsx:982` | BLOCKER | 010/012/015 |
| 1.6 | DEF-01: `docxBytes` effect forces `dirty=false` on every transient mount | `ComposeEditor.tsx:676-677` | HIGH | pre-existing |
| 1.7 | Client discards server-minted new SPE id on save → next save re-breaks (1.1) | `ComposeWorkspace.tsx:602-608`, `.types.ts:208-219` | HIGH | 013 |
| 1.8 | FR-05 parent-association never called from save-completion (014 split) | `useCreateOnSaveAssociation` called only in test | HIGH | 014 |

**Fix (one coordinated pass)**: add a container-scoped create route (or make `documentSpeId` optional); add `containerId` to the wire DTO; resolve BU container client-side via `EntityCreationService` (Path 1 / `Xrm.WebApi`, owner-approved) and thread prop→`mountTransient`→`triggerSave`; gate `triggerSave` + Save button on `containerId||isDirty||transientDraft`; carry `documentSpeId` on `saveSucceeded`; call `associate()` on completion; skip the dirty-reset for transient mounts. **Land with an HTTP-boundary create-on-save test.**

---

## CLUSTER 2 — AI catalog actions not dispatchable E2E
**The 422 false-green IS fixed (E-20). Dispatch plumbing (queue/apply-leg/orchestrator/ledger/materialize) is wired. But:**

| # | Gap | Broken hop | Sev | Marked ✅ by |
|---|---|---|---|---|
| 2.1 | 5 compose catalog rows are **mirror-only, never deployed** → `GetBindingByIdAsync` null → **404** | `sprk_playbookconsumer-rows.json` (proposed); **task 047 🔲** | BLOCKER | 040-044 (authored) |
| 2.2 | Toolbar buttons ship `bindingId:''` → disabled; `registerComposeAiToolbarAction` called only in tests | `ComposeAiToolbar.tsx:188-210,405,341-345` | BLOCKER | 030 (honest stub) |
| 2.3 | 2 of 5 actions (summarize-word-changes, defined-terms) have **no client trigger at all** | not in `DEFAULT_ACTIONS`; overflow empty | BLOCKER | 043/044 |
| 2.4 | **`/healthz` is Unhealthy on AI envs NOW** — 5 ConsumerTypes registered ahead of rows | `RoutingConsumerTypeHealthCheck.cs:257-260`; `RoutingModule.cs:84-88` | MED (current-state) | core E42 |

**Fix**: run **047** (deploy 5 Action + 5 Binding rows to spaarkedev1 — owner/live-env; flips `/healthz` green); then register the seeded GUIDs on the toolbar + wire the defined-terms (overflow) and summarize-word-changes (return-from-Word) triggers. **Land with the 084 HTTP-boundary compose-dispatch test using a real deployed row.**

---

## CLUSTER 3 — Word round-trip shuttle: endpoints + components built, none wired
| # | Gap | Broken hop | Sev | Marked ✅ by |
|---|---|---|---|---|
| 3.1 | "Push to Word" has **no client trigger**; endpoint orphaned | no `push-annotations` caller in `src/client`; `ComposeToolbar` has no button | BLOCKER | 050 |
| 3.2 | SPE webhook **subscription never created** — `EnsureSubscriptionAsync` has zero callers → renewal service renews an empty set forever | `SpeSyncOrchestrator.cs:87` uncalled | BLOCKER | 052 |
| 3.3 | `Compose:Webhook:{SigningKey,ClientState,NotificationUrl}` unprovisioned (DEF-03 + NotificationUrl) → filter fails closed; **poll fallback also uncalled** | no `Compose:Webhook:*` in appsettings/KV | BLOCKER (prod) | 053 |
| 3.4 | Pull-annotations endpoint orphaned (no client caller) | no `pull-annotations` caller | HIGH | 051 |
| 3.5 | Return-from-Word reanchor UI + hook built but **never mounted** in `ComposeWorkspace`; poll `check-changes` uncalled | reanchor symbols not imported by `ComposeWorkspace.tsx` | HIGH | 054 |
| 3.6 | FR-28 deterministic push/save orchestration + Tier-2c preview not built | absent from `Services/Compose` | HIGH | **055 🔲** |

**Fix**: add push-to-Word toolbar action; call `EnsureSubscriptionAsync` on document load; provision the 3 config keys; mount reanchor UI + a change signal (webhook or poll-on-focus); build 055. **Land with slice tests.**

---

## CLUSTER 4 — Memory / annotations / cross-version resume is E2E-inert
**A user's annotations & decisions CANNOT survive a reopen today.**

| # | Gap | Broken hop | Sev | Marked ✅ by |
|---|---|---|---|---|
| 4.1 | **Linchpin**: Load endpoint discards `sessionId`+`matterId` → resume branch never runs → **every reopen mints a new empty session** | `ComposeEndpoints.cs:908-917,931-938`; `ComposeService.cs:154-176` | BLOCKER | 062 |
| 4.2 | Load response omits `anchoredAnnotations`/`definedTerms`/`actionHistory` the service returns (DEF-04) | `ComposeEndpoints.cs:942-951,1271-1280` | BLOCKER | 060/061 |
| 4.3 | No save route for anchored annotations; client never persists them (DEF-04 write half) | `SaveComposeAnnotationsAsync` unmapped | BLOCKER | 060 |
| 4.4 | Action history (061) is an unreferenced method E2E — discarded before the wire | `ComposeService.cs:995-1050` → dropped at 4.2 | HIGH | 061 |
| 4.5 | Cosmos warm tier drops annotation collections (DEF-05) — survives Redis TTL only | `ChatSessionManager.cs:505-547`; `StoredSession.cs` | MED→HIGH | 060 |

**Fix**: bind `[FromQuery] sessionId,matterId` on Load + client sends `matterId`; add the 3 fields to `LoadComposeDocumentResponse`; map `GET/POST .../annotations` + client save-on-mutation; extend `StoredSession` + map methods. **Land with a reopen-restores-state slice test.**
**063/064 confirmed genuinely core-blocked (NOT half-built) — correctly deferred.**

---

## CLUSTER 5 — Three-pane coordination (the R2 differentiator) is stub-only
| # | Gap | Broken hop | Sev | Marked ✅ by |
|---|---|---|---|---|
| 5.1 | 4 of 6 flows stub/contract-only; Flows 1&2 receivers are **log-only on the wrong pane** (`ComposeWorkspace`, not Context/Assistant); only Flow 5 wired | `ComposeWorkspace.tsx:706-732`; `ContextPaneController.tsx:433-489` default:break | BLOCKER (FR-34) | **070 🔲** |
| 5.2 | None of the 6 `compose_*` discriminants exist in the typed bus union; all ride via `as any` (ADR-030 violation) | `ComposeEditor.tsx:554-565` | HIGH | 070 |

**Fix**: task 070 — real receivers on `ContextPaneController` + `ConversationPane`; build Flows 3/4/6; add typed discriminants. Note: 072 (Doc Q&A) inherits this (its Context surfacing is Flow-6).

---

## THE ANTI-RECURRENCE FIX (do this alongside every cluster)
**No HTTP-boundary vertical-slice test dispatches a real compose action.** The 3 existing test surfaces each stop one layer short: `DispatchSessionEndpointContractTests` (real HTTP but generic *informational* binding), `DispositionRoutabilitySeamTests` (compose but orchestrator-level, mocked routing, synthetic binding), `ComposeDispositionContractTests` (pure, no WebApplicationFactory). **Task 084** is the missing closure and MUST NOT be waived as "already covered" — it adds the two dimensions none of the others have: a real HTTP `/dispatch` route + a real deployed catalog row. Generalize the discipline: **each cluster fix lands with a WebApplicationFactory test that drives the full slice through the wire.**

---

## DEPLOY / CONFIG PREREQUISITES (nothing below is provisioned)
- **Dataverse (047)**: 5 `sprk_analysisaction` + 5 `sprk_playbookconsumer` rows → spaarkedev1. **Deploy BEFORE/atomic-with the first BFF deploy carrying the ConsumerTypes constants, or `/healthz` fails.**
- **Config/KV (056/DEF-03)**: `Compose:Webhook:SigningKey`, `:ClientState`, `:NotificationUrl`; confirm default Azure OpenAI deployment reachable for the 5 actions.
- **Deploy artifacts (017/056/081)**: BFF→App Service (≤60 MB + CVE), SpaarkeAi code page, confirm `SpeWebhookRenewalHostedService` starts. All 🔲.

---

## Load-bearing insights
1. **Task 047 (deploy) is load-bearing** — unblocks all AI actions AND flips `/healthz` green. Sequence it first among deploys.
2. **Task 084 (HTTP vertical-slice test) is the forcing function** — build the pattern early so subsequent fixes can't regress false-green.
3. **Loads work; creates/writes/coordination don't.** Read paths (1c, 1a-search, replace-save) are solid; every *write/create/coordinate* path has a broken wire.
4. **Project ✅ count (39/57) overstates completeness** — many ✅ tasks are E2E-inert. Real remaining work ≈ 5 wiring clusters + deploy + forcing-function tests.

## Correctly-deferred (do NOT count as gaps)
- 063 (memory.write) / 064 (D-F4 trace) — genuinely core-blocked, no half-built compose code.
- Fork-C profile-analysis facade — routed to core, emits a non-terminal deferred signal by design.
