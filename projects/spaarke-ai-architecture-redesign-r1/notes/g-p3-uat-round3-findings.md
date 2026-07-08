# G-P3 Browser UAT — Round 3 Findings (2026-07-07, operator on spaarkedev1)

> Deployed build under test: `88c123f82` (round-2 fix wave). Round-3 fix wave executed 2026-07-07
> (this document). Empirical-Reproduction-FIRST (bff-extensions §F.3): every defect pinned with
> App Insights telemetry (`spe-insights-dev-67e2xz`, rg `spe-infrastructure-westus2`) BEFORE any
> fix. Companions: [`g-p3-uat-round1-findings.md`](g-p3-uat-round1-findings.md) ·
> [`g-p3-uat-round2-findings.md`](g-p3-uat-round2-findings.md).

## Operator round-3 results (2026-07-07, ~12:34–13:00 PM local ≈ 16:34–17:00Z)

- ✅ Layout opening works end-to-end (unknown name → honest list of real layouts; Corporate
  Workspace + Calendar tabs opened live). ✅ email-from-file works. ✅ create-matter failure
  rendered the HONEST ❌ transcript message with the exact reason (R2-A(ii)/R2-C proven live).
- ❌ R3-1: create-task confirm LOOP — model kept asking to confirm, never invoked the write tool.
- ❌ R3-2: gate-resolve returned **502** for validation failures (honest ❌ rendered, wrong status).
- ❌ R3-3: chat-opened workspace tabs (Compose etc.) do NOT survive a page refresh.
- ❌ R3-4: "add to documents" → promise loop; eventually a broken `sprk_document` create attempt.

## App Insights evidence (session `1790b15a03984de7b0e244dbe6c2906f`, tenant `a221a95e…`)

| UTC | Event | Evidence |
|---|---|---|
| 16:40:34 / 16:40:51 / 16:41:15 / 16:41:31 | `Invoking capability_create-task` ×4 (drafting, 2.8–5.1 s each) | **ZERO** `SYS-Dataverse_Create_Record` invocations in this window — every user "confirm" re-ran the DRAFTING capability (R3-1) |
| 16:45:26 | `gate_suspended` SYS-Dataverse_Create_Record (write, turn 3, gate `…5fee68c7`) | create-MATTER **did** bridge to the write tool in the same session — plumbing works; the miss is steering |
| 16:45:45 | `POST …/gates/confirmation-5fee68c7…/resolve` → **502** | `[dataverse.create_record][ADR-015] entity=sprk_matter outcome=VALIDATION_FAILED durationMs=928` — `sprk_assignedattorney1` lookup composed WITHOUT recordId (R3-2); `dispatch-failed` gate marker + honest ❌ persisted (R2-C working) |
| 16:48:10 | `SendWorkspaceArtifactHandler … workspace tab opened layoutId=c09d26be… tabId=80f9f006…` then **`SaveTabs: tabCount=2, activeTabId=wstab-2-workspace`** | the chat bridge path **DOES** persist (round-2's "write-through only fires on the menu path" hypothesis DISPROVEN) |
| 16:49:05 | refresh: `GET /sessions/{id}/tabs` → 200 (2 tabs returned) | restore fetch ran |
| 16:49:07 | **`SaveTabs: tabCount=1, activeTabId=wstab-1-workspace`** | the store was OVERWRITTEN 2 s after the refresh with ONE freshly-numbered tab (`wstab-1-…` = a new post-mount `addTab`, not a restored id) — the R3-3 smoking gun |
| 16:51:00 → 16:51:25 | `workspace layout not found … availableCount=11` → retry → `tab opened layoutId=00000000-…-0001` (Corporate) | round-2's unknown-layout fail-honest leg proven live; tabs grew to 5 by 16:53 |
| 16:55:44 → 16:55:46 | SYS-Email_Draft gate suspended → resolve **200** | email confirm leg still green |
| 16:57:52 → 16:57:55 | SYS-Dataverse_Create_Record gate (turn 4) → confirm → **502**, `entity=sprk_document outcome=DATAVERSE_BAD_REQUEST durationMs=439` | the "add to documents" endgame — a fileless `sprk_document` row create rejected by Dataverse (R3-4) |

---

## Defects, root causes, fixes

### R3-1 — create-task confirmation loop (CRITICAL) — FIXED (steering, 3 layers)

**Root cause**: after the round-2 drafting/creating reframe, the model correctly stopped claiming
"created" — but on every user confirmation it RE-INVOKED `capability_create-task` (4× telemetry
above) and asked to confirm AGAIN, never bridging to `dataverse.create_record`. Nothing anywhere
said "a chat confirmation converts IMMEDIATELY into the write-tool invocation, exactly once".
Create-matter in the same session DID invoke the write tool — the projection/gate plumbing is
proven; the hole was capability-flow steering.

Fixes:
- **Directive** (`SprkChatAgentFactory.SideEffectHonestyDirective`, +2 bullets): (a) *"Ask for
  confirmation in chat AT MOST ONCE per action … the ONLY correct next step is to IMMEDIATELY
  invoke the corresponding write tool — the platform's CONFIRMATION DIALOG is the real approval
  step … NEVER re-run a capability_* drafting tool instead of invoking the write tool"*; (b) the
  R3-2 resolve-before-proposing bullet (below). Pinned in
  `SprkChatAgentFactoryInvalidSchemaProjectionTests` (+3 wording pins).
- **Capability result** (`BindingCapabilityTool`): result text now ends *"If the user has ALREADY
  confirmed, invoke the write tool NOW — do NOT invoke this capability again for the same request,
  and do NOT ask the user to confirm again in chat: the write tool's confirmation dialog IS the
  approval step."* Pinned in `LoopElicitationTests` (+2 pins).
- **Catalog data (spaarkedev1, verified by re-read)** — create-task Binding
  `3d9724e5-8279-f111-ab0e-7ced8ddc4cc6` `sprk_tooldescription`: appended the **POST-CONFIRMATION
  RULE** (*"ask for confirmation in chat AT MOST ONCE. The moment the user affirms … IMMEDIATELY
  invoke dataverse.create_record with the sprk_event contract above — do NOT re-invoke this
  capability … The CONFIRMATION DIALOG presented by dataverse.create_record is the approval
  step"*). Old→new: old text carried the full sprk_event column contract + ASSIGNEE RULE (round 2)
  but STOPPED at "after approval the created record's id and link are reported back" — no
  once-only / confirmed→invoke-now bridge.

### R3-2 — gate-resolve 502 on validation failures — FIXED (422 + resolve-first steering)

**Root cause (status)**: both `ResolveGateAsync` failure legs returned
`StatusCodes.Status502BadGateway` for handler-reported failures — but a write-mapper validation
rejection or a Dataverse 400 is a REQUEST-CONTENT problem, not a gateway fault.

**Root cause (payload)**: the model composed `sprk_assignedattorney1` as a lookup WITHOUT a
recordId despite round-2's description — it resolved nothing before proposing, so the confirmed
write was doomed from composition time.

Fixes:
- `ChatEndpoints.BuildGateDispatchFailedProblem(detail)` — NEW single construction site for both
  resolve legs: **422 Unprocessable Entity**, stable `gate.dispatch-failed` errorCode (ADR-019),
  handler detail preserved verbatim. 5xx now reserved for genuinely unexpected exceptions (global
  handler). Pinned in `ConfirmationGateUnificationTests` (+1: 422 + errorCode + detail + fallback).
- **Client**: ZERO changes needed — `useActionHandlers.resolveGate` handles any non-OK status
  identically (409 special-cased; otherwise ProblemDetails errorCode+detail extraction). Verified
  by reading `useActionHandlers.ts` §341–409; the existing
  `useActionHandlers.gateResolve.test.ts` contract is status-agnostic.
- **Resolve-lookups-BEFORE-proposing** (three mirrors): directive bullet (*"resolve each reference
  to its record GUID FIRST using the available search/read tools — in the same turn you draft the
  proposal, not after the user confirms"*); `dataverse.create_record` row description
  (*"…IN THE SAME TURN you draft the proposal, BEFORE asking the user to confirm"*);
  `DataverseCreateRecordHandler.Metadata` description (same text). The retry path after a failed
  confirm remains live: the honest ❌ transcript message carries the mapper's instructive reason,
  the next turn's model sees it (R2-C persistence), and a corrected re-proposal creates a fresh
  gate — end-to-end retry is a round-4 script item (live-LLM behavior can't be offline-proven).

### R3-3 — workspace tabs don't survive refresh — FIXED (client sequencing + id-collision)

**Round-2's hypothesis was wrong**: telemetry proves the chat-bridge path DID persist
(SaveTabs tabCount=2 immediately after the 16:48:10 bridge open). The loss is on the RESTORE path:

**Root cause**: `WorkspacePane`'s auto-install-default effect (Wave 2b task 109) races the async
NFR-09 restore effect. The default-layout `addTab` lands first (its layouts fetch resolves faster
than restore's GET + widget resolution), which (a) makes `restoreFromPersistence`'s
`hasNonHomeTab` guard silently no-op — dropping every persisted tab — and (b) fires the debounced
PATCH write-through, **overwriting the server store** with only the fresh default tab (the
tabCount 2→1 + fresh `wstab-1-workspace` id in the evidence table). The pin auto-open effect had
the identical race.

**Fixes** (`src/solutions/SpaarkeAi/src/components/workspace/`):
- `WorkspacePane.tsx`: new `tabRestoreSettled` state — the restore effect settles it on EVERY
  terminal path (success / 404 / error via `finally`; immediately when no chat session exists);
  the auto-install-default AND pin auto-open effects gate on it. Their existing
  `alreadyOpen`-by-layoutId dedup checks now see the restored tabs, exactly as designed.
- `WorkspaceTabManager.ts` (follow-on bug caught by the new regression test): restored tabs keep
  their original `wstab-{seq}-{type}` ids whose seq can EXCEED the restored count; the old
  per-tab `_nextSeq++` left the counter below those seqs, so the next `addTab()` minted a
  DUPLICATE id (React key collision; close/activate act on the wrong tab).
  `restoreFromPersistence` now advances `_nextSeq` past each restored id's own seq.
- Tests: NEW `WorkspacePane.tab-restore-race.test.tsx` (restored tab survives a slow restore GET
  vs fast layouts; every write-through PATCH still carries it; ids unique; default-already-restored
  ⇒ no duplicate) + `WorkspaceTabManager.test.ts` +1 (seq-collision pin).
- Repaired 2 PRE-EXISTING broken suites in the same dir (both verified failing at HEAD via stash):
  `WorkspacePane.summary-tab.test.tsx` (bare `@spaarke/ui-components` mock broke on the registry's
  `safeRegister` import — now spreads requireActual) and
  `WorkspaceTabManagerComponent.hideTabBar.test.tsx` (component now requires
  `PaneEventBusProvider` — wrapped). Workspace dir: **5 suites / 80 tests green**.

### R3-4 — "add to documents" promise loop → broken sprk_document create — FIXED (honest refusal path)

**Catalog verification (empirical)**: full active `sprk_analysistool` sweep (36 SYS rows + 4
analysis rows enumerated via Dataverse MCP) — **NO document-creation / SPE-file-upload tool
exists in the closed catalog**. The model's only reachable move was `dataverse.create_record` on
`sprk_document`, which creates a fileless metadata row — exactly what it attempted at 16:57:52
(→ DATAVERSE_BAD_REQUEST). The correct behavior is refusal-with-alternatives.

Fixes:
- **Catalog data (spaarkedev1, verified by re-read)** — `dataverse.create_record` row
  `18b3531f-ba78-f111-ab0e-7ced8ddc4a05` `sprk_description`: added *"Do NOT create sprk_document
  rows from chat: document records require a file uploaded to SharePoint Embedded and no chat tool
  can upload file content — if asked to save chat output 'to documents', say honestly that you
  cannot create documents from chat and offer an alternative (create a task, draft an email, or
  open a workspace tab)."* (+ the R3-2 same-turn resolve wording). Old→new: old text had the
  round-2 lookup/choice/omit rules but no document ban and no same-turn-resolve timing.
- Handler `Metadata` description mirrors the row (parity, as in rounds 1–2). Repo seed mirror
  `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` updated to the live text
  (+ history comment).
- The R3-1 directive bullets cover the promise-loop half (confirmed → invoke; no tool → say so).
- **Eval**: NEW case `GU-062` (refusal family, fixture-only — `golden-utterances.json`):
  *"add this summaries table to the documents as a new document"* → `refuse` via REF-CHAT@v1.
  Absorbed by the existing refusal-family facts; suite green.
- **NOT built (named operator candidate)**: a real document-creation capability (SPE upload of
  session-generated content + `sprk_document` row). New scope — see candidates below.

---

## Fix inventory (code)

| Fix | Files |
|---|---|
| R3-1 directive | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (+2 bullets) |
| R3-1 result bridge | `Services/Ai/Chat/BindingCapabilityTool.cs` (confirmed→invoke-NOW + no-re-draft) |
| R3-2 422 mapping | `Api/Ai/ChatEndpoints.cs` (`BuildGateDispatchFailedProblem` — both resolve legs) |
| R3-2/R3-4 handler parity | `Services/Ai/Handlers/DataverseCreateRecordHandler.cs` (Metadata description) |
| R3-3 sequencing | `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` (`tabRestoreSettled` gate on auto-install + pin auto-open) |
| R3-3 id collision | `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts` (`restoreFromPersistence` seq advance) |
| Seed mirror | `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` |
| Tests | `ConfirmationGateUnificationTests` (+1 422 pin) · `SprkChatAgentFactoryInvalidSchemaProjectionTests` (+3 pins) · `LoopElicitationTests` (+2 pins) · `golden-utterances.json` (+GU-062) · NEW `WorkspacePane.tab-restore-race.test.tsx` (2) · `WorkspaceTabManager.test.ts` (+1) · repaired pre-existing `WorkspacePane.summary-tab.test.tsx` + `WorkspaceTabManagerComponent.hideTabBar.test.tsx` |

## Fix inventory (data — spaarkedev1, all verified by post-write re-read)

| Row | Change (old→new documented above) |
|---|---|
| `sprk_playbookconsumer` create-task `3d9724e5-8279-f111-ab0e-7ced8ddc4cc6` | `sprk_tooldescription` + POST-CONFIRMATION RULE (once-only chat confirm; affirm ⇒ IMMEDIATELY invoke dataverse.create_record; dialog is the approval step) |
| `sprk_analysistool` dataverse.create_record `18b3531f-ba78-f111-ab0e-7ced8ddc4a05` | `sprk_description` + same-turn resolve-before-confirm timing + sprk_document creation ban with alternatives |

## Test evidence (2026-07-07)

- Targeted BFF (ConfirmationGateUnification + LoopElicitation + InvalidSchemaProjection +
  DataverseCreateRecordHandler + SendWorkspaceArtifactHandler): **78/78 green**.
- **Eval gate (`Category=GoldenUtteranceEval`): 35/35 green** (fixture now 62 cases incl. GU-062).
- **Full BFF unit suite: 7636 total — 7530 passed, 101 skipped, 5 failed.** The 5 are the KNOWN
  pre-existing list VERBATIM (ExecutorConfigSchemas placeholder, KnowledgeDeploymentConfig
  defaults, DailyBriefingCollector resolver-routing, PlaybookTemplateContextBuilder TextOnly,
  SessionFilesCleanup orphan-eviction; AuditLogService flake did not fire). Total grew by exactly
  this wave's +1 new fact (7635 → 7636). **Zero failures attributable to this wave.**
- SpaarkeAi jest, workspace dir: **5 suites / 80 green** (incl. the new race suite; 2 pre-existing
  broken suites repaired). Full SpaarkeAi run: 21/24 suites green — the 3 failing suites
  (`ContextPaneController`, `DocumentComposeLaunch`, `launch-resolver`; 9 tests) were verified
  **pre-existing at HEAD via git-stash A/B** (context-pane + ribbon-launch surfaces; zero overlap
  with this wave — cleanup candidate below).
- SpaarkeAi `tsc --noEmit`: zero errors in touched files.
- Shared-lib (`Spaarke.UI.Components`): **no source changes this round** — no redeploy-driving diff.

## Publish size (ADR-029 / NFR-01)

Clean-rebuild (`obj/bin` removed) `dotnet publish -c Release` into a fresh dir +
`Compress-Archive -CompressionLevel Optimal`: **270 files | 141.50 MB uncompressed | 46.83 MB
compressed**. Isolated baseline (wave stashed, same method): 46.83 MB → **wave delta ±0.00 MB**
(+0.01 MB uncompressed = description strings). Zero `*.csproj` changes → 0 NuGet changes → no new
CVE surface by construction. Ceiling 60 MB: far clear.
**Method note**: an interim 49.96 MB reading came from publishing after `dotnet test` runs —
incremental build state inflated `Sprk.Bff.Api.pdb` 1.85 → 5.12 MB. Always measure from a clean
`obj/bin` (extends round-1's fresh-directory rule).

## Round-4 UAT script (G-P3)

Deploy this branch: **BFF + `sprk_spaarkeai` code page** (R3-3 is a SpaarkeAi client fix — the
code page MUST be redeployed; shared-lib unchanged this round). Catalog rows already updated live.

1. **Create-task single-confirm (R3-1)**: upload a document → "create a task to follow up on this
   file; due 7/10/2026; assigned to ralph.schroeder@spaarke.com" → proposal drafts → reply
   "yes create" ONCE → the model MUST immediately invoke the write tool → **CONFIRMATION DIALOG
   appears** (any second "please confirm" in chat = FAIL) → Confirm → ✅ transcript message with
   the record id → verify the `sprk_event` (eventtype Task, due 2026-07-10, provenance line, owned
   by you; assignee either omitted-with-name-in-description or a resolved contact lookup).
2. **422 + failure honesty (R3-2)**: "create a task assigned to Zebulon Nonexistent, due
   7/10/2026" → best outcome: the model searches, finds nothing, omits the assignee (notes the
   name in the description) and succeeds. If it still ships an unresolved lookup: browser console
   shows `POST …/resolve` → **422** (NOT 502), the ❌ transcript message carries the mapper
   reason, and the NEXT turn ("did that work?") answers from the failure.
3. **Resolve-before-proposing (R3-2)**: "create a matter from this file, assigned attorney
   <a real contact's name>" → watch for a `search_data`/`read_query` BEFORE the proposal → confirm
   → the `sprk_matter` creates with the resolved lookup (or an honest ❌ with the real reason).
4. **Tab refresh persistence (R3-3)**: "open the Compose workspace" → tab opens → open one more
   layout (e.g. Calendar) → **REFRESH the page** → BOTH tabs survive, the default layout is not
   duplicated, and closing/switching tabs still targets the right tab (id-collision fix). Refresh
   a second time → still intact (the store was not overwritten).
5. **Pin interplay (R3-3)**: pin a workspace → open another via chat → refresh → pinned + chat
   tab + default all present exactly once.
6. **Documents honesty (R3-4)**: "add this summaries table to the documents as a new document" →
   honest refusal naming what it CAN do (create a task / draft an email / open a workspace tab) —
   NO confirmation dialog, NO `sprk_document` attempt, no promise loop.
7. **Regression sweep**: email-from-file draft confirm (round-3 ✅); layout opening incl. unknown
   name → honest list; create-matter happy path; host context; chip summarize.

## New-scope candidates for the operator (NOT built this wave)

1. **Document-creation capability** — turn session/chat-generated content into a REAL document
   (SPE file upload + `sprk_document` row + container wiring). No chat tool can do this today;
   the catalog ban + refusal is the honest V1. Needs sizing (BFF handler + SPE upload path +
   gate class write + eval family).
2. **Compose document pre-seeding** — already sized in round-2 (§R2-D verdict input): 1 small
   client-only task threading `widgetData → workspace widget → compose section props`.
3. **FR-P4-01 verdict on the legacy workspace tools** (carried from round-2): Get/Update/Close
   Workspace Tab + the 4 legacy artifact variants still target the orphaned
   `IWorkspaceStateService` store.
4. **SpaarkeAi pre-existing failing suites** — `ContextPaneController.test.tsx`,
   `DocumentComposeLaunch.test.ts`, `launch-resolver.test.ts` (9 tests, verified failing at HEAD;
   unrelated surfaces). Candidate for a small test-repair task.

## For the main session (.claude write boundary)

- Round-1's `jps-action-create` checklist items still pending (property-level `required` ban +
  `infra/dataverse/inputschemas/` mirror pointer).
- `projects/INDEX.md` hot-path note: this wave touched BFF + SpaarkeAi (shared-lib untouched).
