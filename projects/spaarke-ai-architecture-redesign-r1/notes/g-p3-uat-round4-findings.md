# G-P3 Browser UAT — Round 4 + Round-5 Addendum Findings (2026-07-07, operator on spaarkedev1)

> Deployed build under test: `65b896fdc` (round-3 fix wave). Round-4 fix wave executed 2026-07-07
> (this document), extended mid-wave by the operator's ROUND-5 ADDENDUM (R5-A..D) from continued
> testing on the same build. Companions: [`g-p3-uat-round1-findings.md`](g-p3-uat-round1-findings.md)
> · [`g-p3-uat-round2-findings.md`](g-p3-uat-round2-findings.md) ·
> [`g-p3-uat-round3-findings.md`](g-p3-uat-round3-findings.md).

## Operator round-4 results (2026-07-07, ~14:59–15:04 local)

- ✅ summarize; ✅ multi-file combined summary follows instructions; ✅ email draft FULL
  end-to-end (dialog → executed → ✅ transcript + record id + honest no-send); ✅ create-matter
  renders honest actionable ❌.
- ❌ R4-1 create-matter payload composition (invented `sprk_practicearea@odata.bind`).
- ❌ R4-3 no record links; model INVENTED a `/WebResources/tables/...` URL when asked for one.
- ❌ R4-6 raw handler-instruction text leaked verbatim into the ✅ transcript.
- ❌ R4-4 "send this as an email" drafted a generic email REFERENCING AN ATTACHMENT.
- ❌ R4-5 no way to start a new session (resume-by-design with no user control).
- 🎯 R4-2 Compose document pre-seed (operator's #1 feature) — implemented this wave.

## Operator round-5 addendum results (same build, continued)

- ✅ create-task E2E (record `9c9de352-3b7a-f111-ab0e-70a8a590c51c` created with provenance);
  ✅ save-to-matter; ✅ patent count; ✅ injection catch; ✅ rerun-ungated; ✅ dark mode.
- ❌ R5-A "due date tomorrow" → 6/13/2024 (model has no clock; hallucinated the YEAR).
- ❌ R5-B created sprk_event had NO assignee and NO regarding.
- ❌ R5-C "distribution of matters by practice area" → fallback SELECT failed claiming
  `sprk_practicearea` does not exist on sprk_matter.
- ❌ R5-D ExecutionTraceWidget (Context pane) stays EMPTY after tool-invoking turns.
- 📋 UX ruling RE-CONFIRMED: explicit user request should NOT require confirmation (dialog + the
  model's extra chat-ask both count as friction). Recorded, cheap steering pin applied (below);
  the full explicit⇒no-dialog policy is **r2 D-F1 scope (Confirmation Policy v2)** — NOT built.

---

## Defects, root causes, fixes

### R4-1 — create-matter payload composition (guidance layer) — FIXED

**Root cause**: the model invented an `sprk_practicearea@odata.bind` OData annotation. The write
mapper rejects `@`-keys by design (injection hardening, round-1) — but nothing TOLD the model the
annotation form is banned, and it had no sprk_matter column contract.

**Empirical correction to the round-4 brief**: `mcp describe tables/sprk_matter` shows
`sprk_practicearea` is a **LOOKUP to `sprk_practicearea_ref`** and `sprk_mattertype` a **LOOKUP to
`sprk_mattertype_ref`** — NOT multi-select choice columns as the brief assumed. The shipped
contract reflects the live metadata (resolve the `*_ref` row's GUID via read_query → the
`{relatedTable, recordId}` object form, or omit + describe in `sprk_matterdescription`).

Fixes (three mirrors, as rounds 1–3):
- **Catalog row** `sprk_analysistool` `18b3531f-ba78-f111-ab0e-7ced8ddc4a05` `sprk_description`
  (verified by re-read): + *"NEVER use OData annotations as item keys — no '@odata.bind', no '@'
  or '.' in any key…"* + the sprk_matter write contract (sprk_mattername REQUIRED;
  sprk_matternumber; sprk_matterdescription; sprk_mattertype/sprk_practicearea = lookups to the
  `*_ref` tables) + describe-first reinforced. Old→new: old text (round-3 state, captured in
  round-3 note) had lookup/choice/omit/document-ban rules but NO annotation ban and NO per-table
  contract.
- **Handler** `DataverseCreateRecordHandler.Metadata` description + `item` parameter description
  mirror the row (parity).
- **Seed mirror** `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` updated
  (+ history comment documenting the metadata-vs-brief discrepancy).
- **Pinned**: `DataverseCreateRecordHandlerTests.Metadata_Description_CarriesODataBindBan_AndSprkMatterWriteContract`.

**Cataloged create-matter capability check (operator ask)**: full active `sprk_playbookconsumer`
sweep — NO chat create-matter Binding exists. "Wizard New Matter Create"
(`e5f37faa…`, consumerType `matter-pre-fill`) is the Doc-Upload-wizard pre-fill consumer, not a
chat capability. Conversational create-matter stays on the generic `dataverse.create_record`
path with the hardened contract. **A cataloged create-matter capability (like create-task) is a
named operator candidate — NOT created unprescribed.**

### R4-3 — record links in the ✅ outcome (operator-explicit, HIGH VALUE) — FIXED

**(a) Clickable link.** New chain:
- Handlers (create/update record, email draft) emit `ToolResultMetadataKeys.CreatedRecord`
  (`ToolCreatedRecord(entityLogicalName, recordId)`).
- `TypedHandlerResumeExecutor.ResumeOutcome` gains `UserSummary` / `RecordEntityLogicalName` /
  `RecordId` / `RecordUrl`; the executor composes
  `{Dataverse:EnvironmentUrl}/main.aspx?pagetype=entityrecord&etn={etn}&id={guid}` (env URL via
  `IOptions<DataverseOptions>`, resolved in `TryCreate`; trailing slash trimmed).
- `ChatEndpoints.BuildGateOutcomeMessage` appends ` [Open record]({url})` on success; the
  PERSISTED ✅ transcript message carries the markdown link (renderMarkdown opens links in a new
  tab with `noopener noreferrer`).
- `GateResolveResult` gains additive `RecordUrl`/`RecordEntityLogicalName`/`RecordId`; the client
  (`useActionHandlers` → `SprkChat.handleActionConfirm`) renders the same `[Open record](url)`
  link in the local ✅ message.

**SEAM DECISION (documented per operator ask)**: URL composed **SERVER-side, without appid**.
Why server: the ✅ transcript message is persisted server-side and must carry the link durably
across reloads, and the MODEL needs a real link in its history to relay (R4-3(b)). Why no appid:
the server never sees the client's MDA appid; `main.aspx?pagetype=entityrecord&etn=…&id=…`
without appid resolves in the user's current app (operator-sanctioned). **With-appid upgrade
path** (named candidate, not built): thread appid through session-create HostContext (client
reads `Xrm.Utility.getCurrentAppProperties()`); the executor would then prefer the session's
appid. Documented on `ResumeOutcome.RecordUrl`.

**(b) URL fabrication.** `SprkChatAgentFactory.SideEffectHonestyDirective` + bullet: *"NEVER
compose, guess, or reconstruct record URLs or deep links. Only relay links that appear verbatim
in tool results or earlier messages… If no link was provided, say you do not have one."* With (a),
the model now HAS truth to relay.

Pinned: `ConfirmationGateUnificationTests` (+1 link fact incl. failure-leg-never-links),
`TypedHandlerResumeExecutorTests` (+3: extraction+URL composition; graceful degradation;
tolerant JsonElement envelope), `SprkChatAgentFactoryInvalidSchemaProjectionTests` (+1 directive
pin), `useActionHandlers.gateResolve.test.ts` (+2 client contract facts).

### R4-6 — instruction-to-model text leaked into the transcript — FIXED

**Root cause**: `EmailDraftToolHandler`'s `ToolResult.Summary` is MODEL-facing ("…tell the user
the draft is ready…") — correct for the loop path where a model paraphrases, but the gate-resume
path persists the outcome verbatim with NO model turn between (R2-C persistence).

**Fix — split the audiences**: new `ToolResultMetadataKeys.UserSummary` — a user-facing outcome
sentence emitted by ALL side-effecting typed handlers:
- email.draft → *"Draft email created ({n} recipient(s)) — ready for your review in
  Communications. No email was sent."*
- dataverse.create_record → *"Record created in '{table}' (id {guid})."*
- dataverse.update_record → *"Record updated in '{table}' ({n} column(s) changed)."*
- dataverse.delete_record → *"Record deleted from '{table}'."*
`TypedHandlerResumeExecutor` extracts it; `ChatEndpoints` prefers it for BOTH the persisted
transcript message and `GateResolveResult.Summary`. Model-facing `Summary` unchanged for the
loop path. Handlers without the key keep the pre-R4-6 fallback. Pinned in
`EmailDraftToolHandlerTests` (copy + no-"tell the user") and the executor/gate suites.

### R4-4 — email body composition (attachment fabrication) — FIXED (guidance) + VERIFIED GAP

**Root cause (verified)**: `SessionDispatchOrchestrator` feeds ONLY session-file extracted text
into the prompted Action (`DocumentText.ExtractedText` ← `SessionFileTextSource.FetchAsync`).
**Session ledger outputs do NOT reach DRAFT-CORR's input** — so "send this [summary]" re-drafted
from raw file text and the inner LLM invented a "please find attached" reference.

Fixes (guidance, four mirrors):
- **DRAFT-CORR@v1 system prompt** (`sprk_analysisaction` `4b8b50f4-6a79-f111-ab0e-7ced8ddc4cc6`
  `sprk_systemprompt`, verified by re-read): +2 constraints after body-grounding — *"NEVER
  reference attachments or enclosures ('please find attached', 'enclosed is', 'attached hereto')
  … include the relevant material INLINE"* and *"If the request implies sending a document along
  with the letter, state inside the body that the material is summarized below (not attached) and
  inline it"*. Metadata.author notes the round-4 amendment. Repo mirror
  `notes/jps/DRAFT-CORR-v1.jps.json` updated identically.
- **SYS-Email Draft row** (`bc11e90d-6b79-f111-ab0e-7ced8ddc4cc6` `sprk_description`): +the
  no-attachments rule + inline-content steering. Handler `Metadata` description mirrors. Seed
  mirror `infra/dataverse/sprk_analysistool-email-draft-row.json` updated.
- **draft-correspondence Binding** (`f7dc4a00-6b79-f111-ab0e-7ced8ddc4cc6`
  `sprk_tooldescription`): + *"NOTE: this capability drafts ONLY from the session FILES' text —
  it cannot see chat outputs… when the user asks to email content already produced in this
  conversation, do NOT invoke this capability: compose the email subject/body YOURSELF from that
  conversation content (INLINE; never 'please find attached') and call email.draft directly."*
  Mirror `infra/dataverse/sprk_playbookconsumer-rows.json` updated.
- **Eval**: NEW fixture case **GU-063** (draft-correspondence family, clarify via email.draft)
  documenting the verified gap + correct behavior. Suite green.
- **Named candidate (NOT built)**: thread addressed ledger outputs into the dispatch input
  (e.g. optional `outputRefs` arg on DRAFT-CORR resolving `{binding}@t{n}` entries into the
  rendered `## Input`) — a dispatch-contract change requiring operator verdict.

### R4-5 — session management affordance — FIXED

New **"New session"** icon button (Fluent v9 `ChatAddRegular`, `appearance=subtle size=small`,
tooltip + aria-label, ADR-021 token-compliant) in the Assistant `PaneHeader` rightSlot beside
History (`ConversationPane.tsx`). Click = `clearChatSession()` (AiSessionProvider — removes
`sprk_ai2_chatSessionId` from localStorage AND sessionStorage, never a bare removeItem) + a
SprkChat remount via new `useCommandRouting.startNewSession()` (remount-key bump) → SprkChat
mounts with `sessionId=undefined`, mints a fresh session, `onSessionCreated` resets
attachments/chips/refinement + clears the R5-D trace buffer. Deliberately scoped: NO
`session_reset` dispatch (workspace tabs/context survive; `/new-session` slash remains the
full-shell reset) and NO history browsing/deletion (**r2 memory scope** — History menu untouched).
Jest: NEW `ConversationPane.new-session.test.tsx` (3 facts: render-beside-History; clear+remount;
propagation).

### R4-2 — Compose document pre-seed (operator's #1 feature) — IMPLEMENTED (with honest gaps)

**Reality check (investigated, per operator ask)**:
- `ChatSessionFile` (session-uploaded chat files) carries **NO SPE pointer** — FileId (chat-doc
  GUID), MIME, size, AI-Search chunk ids, extracted text only. The upload path never captures an
  `sprk_document`/drive-item id. **A session-uploaded file can NEVER pre-seed Compose** (the
  Compose Load endpoint needs `speDriveItemId`). This is handled HONESTLY, not papered over.
- A real `sprk_document` row CAN pre-seed: `sprk_graphitemid` (drive item) + `sprk_graphdriveid`
  + `sprk_documentname`/`sprk_filename` resolve server-side under user OBO.

**What was built**:
- **Server** (`SendWorkspaceArtifactHandler`, + `IDataverseUserClient` ctor dep): Workspace
  variant accepts optional `widgetData.documentId` (sprk_document GUID; validation rejects
  non-GUID ids with an instructive session-file message). Resolves the SPE pointer under the
  calling user's token; on success the SSE frame's widgetData gains
  `compose: {sprkDocumentId, speDriveItemId, speDriveId, fileName}` (field names mirror the
  ribbon launch params) and the summary says "pre-seeded". **Fail-honest legs**: document not
  found/not accessible, or fileless row (`sprk_graphitemid` empty), or transport error → the tab
  STILL opens (empty) and the summary states it explicitly ("…opened EMPTY; tell the user
  honestly").
- **Client threading** (the round-2 gap "widgetData → workspace widget → compose section props"
  closed): bridge passes widgetData verbatim (no change needed) → tab widgetData →
  `WorkspaceLayoutWidget` forwards `data.compose` OPAQUELY as new
  `WorkspaceRendererProps.launchData` (`@spaarke/ui-components` contract — compose-agnostic
  because `@spaarke/compose-components` depends on `@spaarke/ai-widgets`, so the widget cannot
  import ComposeLaunchContext) → SpaarkeAi `main.tsx` renderer wrapper translates
  `launchData.compose` into a tab-scoped `ComposeLaunchContext.Provider` (nests INSIDE the
  ThreePaneShell URL-param provider; the tab's seed wins for the embedded tree only) →
  `ComposeSectionMount.useComposeLaunch()` → `ComposeWorkspace initialDocumentRef` → loads
  instead of empty state. Bonus: the seed persists in the client tab store, so the pre-seeded
  tab survives refresh.
- **Catalog row** `SYS-Send Workspace Artifact` (`a2c9589d…`): `sprk_description` +
  `sprk_jsonschema` gained the documentId contract + the session-file honesty rule (verified by
  re-read); seed mirror updated.
- **Tests**: `SendWorkspaceArtifactHandlerTests` +4 (seed carried; fileless-row honest-empty;
  not-accessible honest-empty; non-GUID documentId validation), `WorkspaceLayoutWidget.test.tsx`
  +1 (launchData forwarding/omission).

**What can't work (documented)**: pre-seeding from a session-UPLOADED chat file — no stored
document exists to load. The model is steered (row description + validation message) to open
empty + say so. Closing it for real = the round-3 "document-creation capability" candidate
(SPE upload of session content → promoted sprk_document).

---

## Round-5 addendum — defects, root causes, fixes

### R5-A — relative-date resolution / no current-date context — FIXED

**Root cause**: nothing in the system prompt carries a clock; "tomorrow" resolved to 6/13/2024.

Fixes:
- `SprkChatAgentFactory.BuildCurrentDateDirective(utcNow)` — new `## Current Date` block appended
  UNCONDITIONALLY at the end of the system prompt (stable position; rotates once per UTC day —
  accepted daily prompt-cache rotation, NFR-04 note): *"Today's date is yyyy-MM-dd (dddd, UTC).
  Resolve EVERY relative date… against THIS date — never guess the year… state the absolute date
  in your proposal text"*. Time via scope-resolved `TimeProvider` (fallback `TimeProvider.System`).
  **User timezone is NOT available server-side** (JWT carries no tz) — the near-midnight
  ambiguity is handled by instruction (state the absolute date so the user can correct it);
  threading a client tz is a follow-up candidate.
- **create-task Binding** guidance gained the RELATIVE DATE RULE (below).
- Pinned: factory suite +2 (`…CarriesCurrentDateContext`, `BuildCurrentDateDirective` format).

### R5-B — assignee + regarding mapping — FIXED (guidance; column verified)

**Verified via `mcp describe tables/sprk_event`**: `sprk_assignedto1` EXISTS as a **contact
lookup** (per operator ruling; `sprk_assignedto`/`sprk_assignedto2`/`sprk_todoassigned` also
exist — `sprk_assignedto1` is the form's "Assigned To 1"). Regarding family confirmed:
`sprk_regardingmatter` (sprk_matter), `sprk_regardingproject`, `sprk_regardinginvoice`,
`sprk_regardingaccount`, `sprk_regardingcontact`, etc.

**Catalog row** create-task Binding `3d9724e5-8279-f111-ab0e-7ced8ddc4cc6` `sprk_tooldescription`
rewritten (verified by re-read; old→new = round-3 text → adds):
- **RELATIVE DATE RULE** (resolve against `## Current Date`; state the absolute date).
- **ASSIGNEE RULE (updated)**: the assignee column is `sprk_assignedto1` (contact lookup,
  recordId REQUIRED). Someone else → resolve contact GUID first. **"Assign to me" self-assignment
  gap handled honestly**: the server does not inject the caller's contact; the model is told to
  TRY resolving the user's own contact from name/email present in the conversation, else OMIT the
  column and note the assignee in `sprk_description` (ownership is automatic regardless).
  Deterministic self-contact injection (claims→contact resolution server-side) = follow-up
  candidate.
- **REGARDING RULE (new)**: when the chat is record-hosted (the H7 `Context: This chat is hosted
  on…` line), set the matching `sprk_regarding*` lookup with the HOST record's id.
- **POST-CONFIRMATION RULE tightened** (operator's "model still chat-asked once more"): the
  proposal+ask is the ONLY chat question allowed; on affirmation invoke the write tool **IN THAT
  SAME TURN — do NOT announce that you are about to proceed and ask again**. (The full
  explicit-request⇒no-dialog policy remains **r2 D-F1 Confirmation Policy v2** scope.)
- Mirror `infra/dataverse/sprk_playbookconsumer-rows.json` updated to match.

### R5-C — distribution/aggregate queries fail on lookup columns — FIXED (real translator gap)

**Root cause (investigated)**: NOT a multi-select-choice issue — `sprk_practicearea` is a LOOKUP
(see R4-1). The task-008 SQL→OData translator passed column logical names VERBATIM into
`$select`/`$filter`/`$orderby`; the Web API addresses lookup columns as **`_{name}_value`**, so
Dataverse rejected the whole query with "property does not exist" — which the model relayed as
the column being broken.

Fixes:
- `DataverseSqlQueryTranslator`: `TranslationResult` gains additive `RawFilter`/`RawOrderBy`;
  new shared `AssembleODataQuery(...)` (the ONE assembly site). Translator stays pure/metadata-free.
- `DataverseReadQueryHandler`: metadata GET now `$expand=Attributes($select=LogicalName,AttributeType)`;
  referenced Lookup/Customer/Owner columns are rewritten to `_{name}_value` in `$select`,
  `$filter` (quote-aware — string literals never rewritten), and `$orderby`; response row keys
  map BACK to the requested logical names; a warning tells the model the values are
  referenced-record GUIDs (map via the referenced table). No-lookup queries keep the exact
  pre-R5-C query shape.
- **Description guidance — three mirrors updated**: handler `Metadata.Description` + live
  catalog row `sprk_analysistool` SYS-Dataverse Read Query `8631a3cd-b678-f111-ab0e-7ced8ddc4a05`
  `sprk_description` (verified by re-read) + seed mirror
  `infra/dataverse/sprk_analysistool-dataverse-read-query-row.json`: aggregate-the-values-YOURSELF
  for distributions; lookup columns selectable → referenced-record GUIDs (map via the `*_ref`
  table); choice columns numeric; the row's entity map corrected to name the `*_ref` lookup
  targets for sprk_practicearea / sprk_mattertype.
- **"Portfolio" context-bias**: NOT cheap to pin deterministically — noted for **r2 memory
  scope** (portfolio-level questions should query fresh rather than extrapolate from the prior
  turn's patent-count result).
- Pinned: `DataverseReadQueryHandlerTests` +5 (rewrite+map-back on the exact operator query
  shape; WHERE/ORDER BY rewrite with literal-safety; quote-safety round-trip; no-lookup
  unchanged; description guidance).

### R5-D — ExecutionTraceWidget empty — FIXED (mount-lifecycle replay) + honest limitation

**Root cause (investigated end-to-end)**: the wire contract is CORRECT at every hop (server
`tool_chain` ContextSseEventDto fields ↔ bridge mapping ↔ widget `context`-channel subscription
— field-by-field verified). The break: the widget mounts ONLY when the user selects "Execution
Trace" from the Context-pane Tools menu (default tool `quick-start`) — i.e. AFTER the streaming
turn emitted the events — and `PaneEventBus` is fire-and-forget (no-subscriber dispatches are
silently dropped). No backfill existed: `SessionRestoreResponse` carries NO ToolChains and no
endpoint exposes the trace ledger.

Fixes:
- NEW `@spaarke/ai-widgets` `executionTraceBuffer` (bounded module FIFO, 50 events):
  `recordExecutionTraceEvent` / `getExecutionTraceBuffer` / `clearExecutionTraceBuffer`.
- `useContextEventBridge` (always mounted in the conversation pane) records every dispatched
  `tool_chain` event into the buffer alongside the bus dispatch.
- `ExecutionTraceWidget` replays the buffer ONCE on mount through the SAME handler its live
  subscription uses (identical NFR-07 narrowing); live events append after.
- Buffer is session-scoped: `ConversationPane.handleSessionCreated` clears it on a fresh session.
- **Honest limitation (named follow-up)**: the buffer is per page load — a HARD REFRESH still
  loses prior turns' trace because no server ToolChain read surface exists. Closing that =
  add `ToolChains` to the restore payload (server contract change; operator verdict).
- Jest evidence: `ExecutionTraceWidget.test.tsx` +2 (backfill of pre-mount events — the exact
  real-app ordering; replay+live append without duplication); NEW
  `useContextEventBridge.tool-chain.test.ts` (3 facts: the previously-untested field-mapping
  contract; buffer records the identical event; empty-call frames drop entirely).

---

## Fix inventory (code)

| Fix | Files |
|---|---|
| R4-6/R4-3 metadata contract | `Services/Ai/ToolResult.cs` (+`UserSummary`/`CreatedRecord` keys, `ToolCreatedRecord`) |
| R4-6/R4-3 handler emissions | `DataverseCreateRecordHandler.cs` · `DataverseUpdateRecordHandler.cs` · `DataverseDeleteRecordHandler.cs` · `EmailDraftToolHandler.cs` |
| R4-3 link seam | `Services/Ai/Chat/TypedHandlerResumeExecutor.cs` (ResumeOutcome + extraction + `BuildRecordUrl`; `IOptions<DataverseOptions>` in TryCreate) |
| R4-3/R4-6 endpoint | `Api/Ai/ChatEndpoints.cs` (`BuildGateOutcomeMessage` +recordUrl; success leg prefers UserSummary; `GateResolveResult` +3 additive fields) |
| R4-3(b)+R3 directive | `Services/Ai/Chat/SprkChatAgentFactory.cs` (+URL-relay-only bullet) |
| R5-A date context | `SprkChatAgentFactory.cs` (`BuildCurrentDateDirective` + unconditional append) |
| R4-1/R4-4 handler guidance | `DataverseCreateRecordHandler.cs` (descriptions) · `EmailDraftToolHandler.cs` (description) |
| R4-2 server | `SendWorkspaceArtifactHandler.cs` (+`IDataverseUserClient`; documentId validation; `ResolveComposeSeedAsync`; compose seed in widgetDataJson; honest summaries) |
| R4-2 client | `Spaarke.UI.Components/src/workspace/WorkspaceRenderer.ts` (+`launchData`) · `Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.tsx` (+compose passthrough) · `SpaarkeAi/src/main.tsx` (renderer wrapper → ComposeLaunchContext) |
| R4-3 client | `SprkChat/hooks/useActionHandlers.ts` (+record fields) · `SprkChat.tsx` (✅ message + markdown link) |
| R4-5 client | `SpaarkeAi/.../ConversationPane.tsx` (header button + handler) · `useCommandRouting.ts` (+`startNewSession`) |
| R5-C translator | `Services/Ai/Handlers/Dataverse/DataverseSqlQueryTranslator.cs` (RawFilter/RawOrderBy + `AssembleODataQuery`) |
| R5-C handler | `DataverseReadQueryHandler.cs` ($expand attributes; lookup rewrite select/filter/orderby; row-key map-back; warning; description guidance) |
| R5-D buffer | NEW `Spaarke.AI.Widgets/src/widgets/context/executionTraceBuffer.ts` (+barrel exports) |
| R5-D wiring | `ExecutionTraceWidget.tsx` (replay-on-mount) · `useContextEventBridge.ts` (record into buffer) · `ConversationPane.tsx` (clear on new session) |

## Fix inventory (data — spaarkedev1, ALL verified by post-write re-read; old→new above)

| Row | Change |
|---|---|
| `sprk_analysistool` dataverse.create_record `18b3531f-ba78-f111-ab0e-7ced8ddc4a05` | `sprk_description`: +@odata.bind ban + sprk_matter write contract (lookups to `*_ref`, from live metadata) |
| `sprk_analysistool` SYS-Email Draft `bc11e90d-6b79-f111-ab0e-7ced8ddc4cc6` | `sprk_description`: +no-attachments + inline-content steering |
| `sprk_playbookconsumer` draft-correspondence `f7dc4a00-6b79-f111-ab0e-7ced8ddc4cc6` | `sprk_tooldescription`: +files-only limitation + compose-body-yourself→email.draft-direct steering |
| `sprk_analysisaction` DRAFT-CORR@v1 `4b8b50f4-6a79-f111-ab0e-7ced8ddc4cc6` | `sprk_systemprompt`: +2 attachment-ban constraints; author metadata notes the amendment |
| `sprk_analysistool` SYS-Send Workspace Artifact `a2c9589d-ec6d-f111-ab0e-7ced8ddc4cc6` | `sprk_description` + `sprk_jsonschema`: +Compose pre-seed documentId contract + session-file honesty |
| `sprk_playbookconsumer` create-task `3d9724e5-8279-f111-ab0e-7ced8ddc4cc6` | `sprk_tooldescription`: +RELATIVE DATE RULE; ASSIGNEE RULE→`sprk_assignedto1` (+self-resolution attempt, honest omit fallback); +REGARDING RULE (host-context `sprk_regarding*`); POST-CONFIRMATION tightened (same-turn, no proceed-announcement) |
| `sprk_analysistool` SYS-Dataverse Read Query `8631a3cd-b678-f111-ab0e-7ced8ddc4a05` | `sprk_description`: +aggregate-yourself distributions + lookup-GUID contract + choice-numeric; entity map names the `*_ref` lookup targets |

Repo mirrors updated: `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json`,
`…-email-draft-row.json`, `…-send-workspace-artifact-row.json`, `…-dataverse-read-query-row.json`,
`sprk_playbookconsumer-rows.json` (create-task + draft-correspondence),
`notes/jps/DRAFT-CORR-v1.jps.json`.

## Test evidence (2026-07-07)

- Targeted BFF (ConfirmationGateUnification + TypedHandlerResumeExecutor + CreateRecord +
  EmailDraft + SendWorkspaceArtifact + InvalidSchemaProjection + Update/Delete handlers):
  **113/113 green**; R5 wave (ReadQuery + SqlTranslator + factory suites): **99/99 green**.
- **Eval gate (`Category=GoldenUtteranceEval`): 35/35 green** (fixture now 63 cases incl. GU-063).
- **Full BFF unit suite: 7653 total — 7547 passed, 101 skipped, 5 failed.** The 5 are the KNOWN
  pre-existing list (ExecutorConfigSchemas placeholder, KnowledgeDeploymentConfig defaults,
  PlaybookTemplateContextBuilder TextOnly, SessionFilesCleanup orphan-eviction, AuditLogService
  flake; DailyBriefingCollector did not fire this run; one additional parallel-run flake —
  `AnalysisToolDtoTests.MapJsonSchema_SemanticInvalid_LogsWarning` — passes 68/68 in isolation).
  Total grew 7636 → 7653 = exactly this wave's +17 new facts. **Zero failures attributable.**
- Client: shared-lib `tsc` clean + `npm run build` clean; **SprkChat jest 22 suites / 314
  green**. AI.Widgets: build clean; WorkspaceLayoutWidget 5/5 + ExecutionTraceWidget 20/20
  green (full AI.Widgets run has 8 failing suites — **verified pre-existing at HEAD via
  git-stash A/B** on 4 of them incl. register-workspace-widgets (34/34 fail at HEAD too);
  zero overlap with this wave — cleanup candidate). SpaarkeAi: conversation dir **15 suites /
  226 green** (incl. new-session + tool-chain bridge suites); `tsc` zero errors in touched
  files (pre-existing cross-package noise unchanged); **vite build green** (surface-owned tsc
  errors: 0).

## Publish size (ADR-029 / NFR-01)

Clean-rebuild (obj/bin removed) `dotnet publish -c Release` into a fresh dir +
`Compress-Archive -CompressionLevel Optimal`: **270 files | 141.53 MB uncompressed | 45.50 MB
compressed** (round-3 reported 46.83 MB — this wave adds only description strings + one small
class; zero `*.csproj` changes → 0 NuGet changes → no new CVE surface by construction; the
−1.33 MB vs round-3's reading is compressor/toolchain variance, not a real content change).
Ceiling 60 MB: far clear.

## Round-5 UAT script deltas (G-P3)

Deploy this branch: **BFF + shared-lib (`Spaarke.UI.Components`) + `@spaarke/ai-widgets` +
`sprk_spaarkeai` code page** (client changes span all three). Catalog rows already updated live.

1. **Record link (R4-3)**: create-task E2E → after Confirm, the ✅ message shows
   "…Record created in 'sprk_event' (id …). **[Open record]**(…) (ledger: loop@tN)" — the link
   opens the record in MDA. Refresh the page → the persisted ✅ message still carries the working
   link. Then ask "do you have a link to the record?" → the model relays THE SAME link, never a
   /WebResources path.
2. **User-facing outcome (R4-6)**: email-draft confirm → ✅ message reads "Draft email created
   (1 recipient(s)) — ready for your review in Communications. No email was sent. [Open record]…"
   — NO "tell the user…" text anywhere in the transcript.
3. **Create-matter guidance (R4-1)**: "create a matter from this file, practice area <label>,
   type <label>" → watch for read_query on sprk_practicearea_ref / sprk_mattertype_ref BEFORE
   the proposal → confirm → record created with resolved lookups (or honest omit + description
   note). NO @odata.bind anywhere; if it still fails, the ❌ must carry the mapper reason.
4. **Email content inline (R4-4)**: combined summary → "send this summary as an email to the
   client" → the drafted body CONTAINS the summary content inline; ZERO "please find attached" /
   "enclosed" phrasing; email.draft dialog → confirm → ✅ + link.
5. **New session (R4-5)**: click the new header button beside History → transcript clears; next
   message starts a FRESH session (new sessionId in the network tab); hard refresh does NOT
   resurrect the old conversation. Workspace tabs survive the new-session click (scoped reset).
6. **Compose pre-seed (R4-2)**: (a) from a chat that found a REAL document (search result /
   host-context document), "open this document in compose" with the model passing documentId →
   Compose tab opens WITH the document loaded (not empty). (b) session-UPLOADED file: "open this
   file in compose" → tab opens EMPTY and the assistant says so honestly (offers Browse/Search).
7. **Relative dates (R5-A)**: "create a task due tomorrow" → the PROPOSAL states the correct
   absolute date (today+1); the created sprk_event's due date matches.
8. **Assignee + regarding (R5-B)**: on a matter-hosted chat, "create a task assigned to <real
   contact>" → created record has `sprk_assignedto1` set AND `sprk_regardingmatter` = the host
   matter. "assign to me" → either resolved self-contact or honest omit + name in description.
9. **Distribution (R5-C)**: "what's the distribution of matters by practice area" → read_query
   selects sprk_practicearea across rows successfully (no "column does not exist"), model
   aggregates counts itself and (ideally) maps GUIDs via sprk_practicearea_ref.
10. **Execution trace (R5-D)**: run a tool-invoking turn FIRST, then open Context pane → Tools →
    Execution Trace → the prior turn's tool calls are listed (replay). Further turns append live.
    KNOWN LIMIT: a hard refresh clears the trace (server read surface = named follow-up).
11. **Regression sweep**: rounds 1–4 ✅ items (summarize, combined summary, email E2E, honest
    create-matter ❌, tab refresh persistence, documents-refusal, host context, injection catch).

## New-scope candidates for the operator (NOT built this wave)

1. **Cataloged create-matter capability** (like create-task) — conversational matter creation
   deserves a Binding + prompted Action instead of riding generic create_record guidance.
2. **With-appid record links** — thread MDA appid via session-create HostContext.
3. **Ledger-outputs → DRAFT-CORR input** — dispatch-contract change so "send this summary"
   re-drafts from the actual summary.
4. **Server ToolChain read surface** (restore payload or GET) — Execution Trace across reloads.
5. **Deterministic self-contact resolution** for "assign to me" (claims→contact server-side).
6. **Explicit-request ⇒ no-confirmation policy** — r2 D-F1 Confirmation Policy v2 (operator
   ruling re-confirmed this round; only the cheap same-turn steering pin shipped).
7. **User timezone threading** for date resolution (client tz → session context).
8. Carried: document-creation capability (r3), FR-P4-01 legacy workspace tools verdict (r2),
   SpaarkeAi + AI.Widgets pre-existing failing jest suites (test-repair task; AI.Widgets adds
   8 suites verified failing at HEAD).

## For the main session (.claude write boundary)

- `.claude/skills/jps-action-create/examples/draft-correspondence.json` needs the SAME two
  constraints inserted after the "body grounding" constraint (exact text in the R4-4 section
  above / `notes/jps/DRAFT-CORR-v1.jps.json` lines 14–15).
- Round-1's `jps-action-create` checklist items still pending (property-level `required` ban +
  `infra/dataverse/inputschemas/` mirror pointer).
- `projects/INDEX.md` hot-path note: this wave touched BFF + SpaarkeAi + shared-lib
  (`Spaarke.UI.Components` + `Spaarke.AI.Widgets`).

---

# ROUND-5 FINDING R5-E + targeted fix wave (2026-07-07, ~21:46 local; build under test 0bbb1fed9)

## R5-E — "create a record from this document" → bare sprk_document row — FIXED (HARD enforcement)

**Defect (operator)**: the model satisfied an entity-AMBIGUOUS request (the operator meant a
MATTER) by GUESSING `sprk_document` and calling `dataverse.create_record` — creating a bare row
with **no SPE file** ("No file is attached to this document yet"), erroring Similar Documents
widget, and empty profile fields. Spaarke documents require the full ingestion pipeline (file
upload → SPE storage → document profile analysis → indexing) that only the Document Upload
wizard drives.

**🧹 ORPHAN RECORD FOR OPERATOR CLEANUP (not deleted — delete tools are operator-gated)**:
`sprk_document` **`dd97bad5-6e7a-f111-ab0e-7ced8ddc4cc6`** ("Written Opinion International
Searching Authority PCT/US2024/039292", createdon 2026-07-07T21:46:33Z, `sprk_graphitemid`
empty — confirmed still present at fix time).

**Root cause**: round-3's R3-4 fix banned fileless `sprk_document` creation at the
DESCRIPTION level only — the model ignored the guidance. **Guidance ≠ enforcement.**

**Fixes**:
1. **HARD enforcement — `DataverseCreateRecordHandler`**: `sprk_document` creates are now
   REJECTED outright with `VALIDATION_FAILED` **before any Dataverse call**, in BOTH legs —
   `ValidateChat` (the gate-resume path runs it first via `TypedHandlerResumeExecutor`) and
   `ExecuteChatAsync` (defense in depth; fires before the metadata GET; `TryParseArgs`
   lower-cases so casing variants can't slip past). One dual-audience rejection message
   (`SprkDocumentCreateBlockedMessage`): user-facing honesty ("can't be created from chat…
   Document Upload wizard, or upload the file into this chat") + model-facing remediation
   ("Do NOT retry… no argument change makes it valid; offer an alternative"). The gate
   failure leg persists this text verbatim into the ❌ transcript AND the model's history.
2. **Ambiguity steering (cheap)**: `SprkChatAgentFactory.SideEffectHonestyDirective` +1
   bullet — when the user asks to create "a record" WITHOUT naming the record type, do NOT
   guess the table: ask which type (task, matter, project, document, …) in the SAME
   clarifying turn.
3. **Catalog row** `sprk_analysistool` `18b3531f-ba78-f111-ab0e-7ced8ddc4a05`
   `sprk_description` (verified by post-write re-read): the R3-4 sentence "Do NOT create
   sprk_document rows from chat…" REPLACED by the HARD RULE ("…this tool REJECTS any create
   on the sprk_document table before anything is written (VALIDATION_FAILED)… Never attempt
   or retry… point the user to the Document Upload wizard…") + the entity-map document entry
   annotated "creation BLOCKED on this tool, Document Upload wizard only". Old→new: old text
   = round-4 state (captured verbatim in the pre-write read; identical to the pre-edit seed
   mirror). Handler `Metadata` description mirrors; seed mirror
   `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` updated (+ history
   comment).
4. **Eval**: NEW fixture case **GU-064** (family clarify, "create a record from this
   document" → ask which type, never guess; documents the two enforcement layers).

**Census (per fix brief)**: `DataverseCreateRecordHandler` is reachable ONLY via (a) the
IToolHandler assembly-scan DI registration consumed by the chat-tool projection (the LLM tool
leg, gated by declared side-effect class) and (b) `TypedHandlerResumeExecutor` resume (same
`ValidateChat`→`ExecuteChatAsync` stack). No CRUD/server path constructs or injects it
(grep: only tests + eval harness + seed/catalog references). **No legitimate server path
creates sprk_document through this handler — the block breaks nothing.**

**Pinned tests** (`DataverseCreateRecordHandlerTests` +4 facts, `…InvalidSchemaProjectionTests`
+1 assertion): `ValidateChat_SprkDocumentTable_RejectsWithHardBlockMessage_AndZeroDataverseCalls`;
`ExecuteChatAsync_SprkDocumentTable_FailsValidation_WithZeroWireCalls` (×2 casing theory —
verifies ZERO GetAsync/PostAsync); `Metadata_Description_CarriesSprkDocumentHardBlock`; R5-E
directive bullet pinned in `CreateAgentAsync_ToolsProjected_AppendsSideEffectHonestyDirective`.

**Known residual (accepted, candidate)**: the confirmation DIALOG still shows before the
rejection on the loop path — `SideEffectGateAIFunction` suspends by declared class without
pre-validating (by design; no tool-name/table logic belongs in the gate per ADR-039). With the
hardened row description the model should refuse before invoking; if it invokes anyway, the
user sees Confirm → honest ❌. Pre-suspend validation is a gate-architecture candidate, not
this wave.

## R5-E wave test evidence (2026-07-07)

- Targeted (CreateRecord + InvalidSchemaProjection + TypedHandlerResumeExecutor +
  ConfirmationGateUnification): **59/59 green**.
- **Full BFF unit suite: 7657 total — 7551 passed, 101 skipped, 5 failed** — the 5 are
  exactly the KNOWN pre-existing list (ExecutorConfigSchemas placeholder,
  KnowledgeDeploymentConfig defaults, DailyBriefingCollector, PlaybookTemplateContextBuilder
  TextOnly, SessionFilesCleanup orphan-eviction; AuditLogService flake + NetArchTest did not
  fire this run). Total grew 7653 → 7657 = exactly this wave's +4 new facts. **Zero failures
  attributable.**
- **Eval gate (`Category=GoldenUtteranceEval`): 35/35 green** (fixture now 64 cases incl.
  GU-064).
- **Publish (ADR-029/NFR-01)**: `dotnet publish -c Release` + `Compress-Archive Optimal`:
  **270 files | 141.53 MB uncompressed | 46.84 MB compressed** — file count and uncompressed
  size IDENTICAL to round-4's reading (270 / 141.53); the ±1.3 MB compressed swing vs
  round-4's 45.50 is the same compressor variance round-4 documented (it read −1.33 vs
  round-3's 46.83). Zero `*.csproj` changes → zero NuGet/CVE surface change. Ceiling 60 MB:
  far clear.

## R5-E wave — r2 inherited-backlog check (READ ONLY)

`projects/spaarke-ai-architecture-redesign-r2/design.md` **§7 row 2** carries the candidate:
"Document-creation capability (R3-4 named candidate: SPE upload + `sprk_document` row) →
Area 3 — absorbed into D-C2 save-back leg"; D-C2's save-back row spells out SPE upload +
`sprk_document` promotion + container wiring + provenance (compose-r1 §8 reuse). ✅ Covered.
*Wording nit for the r2 spec pass (not edited)*: the row says "SPE upload + sprk_document
row" — R5-E establishes the bar as **full ingestion parity** (profile analysis + indexing
too, matching what the Document Upload wizard drives), which D-C2/compose-r1 §8 should make
explicit at spec time.

## Round-6 UAT script addition (G-P3)

12. **sprk_document hard block + ambiguity clarify (R5-E)**: (a) "create a record from this
    document" → the model ASKS which record type (task/matter/project/document…) in the same
    turn — it does NOT guess and does NOT invoke create_record. (b) Force the block: "create
    a document record for this file" (or answer "document" to the clarify) → the model
    refuses citing the Document Upload wizard WITHOUT invoking the tool; if it does invoke,
    the outcome is an honest ❌ "Spaarke document records can't be created from chat…" with
    ZERO record created (verify no new sprk_document row). 🧹 And delete orphan
    `dd97bad5-6e7a-f111-ab0e-7ced8ddc4cc6` (operator cleanup from this finding).
