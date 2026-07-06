# G-P2 UAT Round-1 Fix Wave — Execution Notes (2026-07-06)

> Fix wave for `notes/g-p2-uat-round1-findings.md` findings 1, 2, 3, 4, 6 (#5/#7 passed),
> plus two mid-wave operator rulings. FULL rigor: Empirical-Reproduction-FIRST root-cause
> investigation before every fix; tests per fix; adr-check/code-review discipline applied
> against ADR-039/040. No commit/push (main session owns the wave close); no
> TASK-INDEX/current-task edits; no `.claude/` writes.

---

## F1 — Chip placement + label

**(a) Placement — root cause**: G-P1 round-2 put the ConsumerChips strip in SprkChat's
`aboveInputSlot` (above the composer). Operator ruled the chips belong INLINE IN THE
TRANSCRIPT, directly beneath the last assistant message.

**Fix**: new `transcriptFooterSlot?: React.ReactNode` prop on SprkChat — a pure layout
seam rendered at the END of the message list, INSIDE the scrollable transcript (after
messages + typing indicator). The auto-scroll effect now also keys on the slot node so
freshly arrived chips land visible; ConversationPane memoizes the node
(`consumerChipsSlot`, `React.useMemo` on `[consumerChips, sessionAttachmentCount,
handleConsumerChipClick]`) so unrelated re-renders never force a scroll-to-bottom.
`aboveInputSlot` kept (additive) but no longer used for chips. The 4 ConversationPane
test stubs now render `{props.transcriptFooterSlot}`.

**(b) Label — catalog data (spaarkedev1, Dataverse MCP)**: chat-classify Binding row
`5f3898d8-db78-f111-ab0e-7ced8ddc4cc6`, column `sprk_chiptransitions`:

- OLD: `[{"target_binding_id":"651194cd-3670-f111-ab0e-70a8a590c51c","chip_label":"Summarize","requires_attachments":true}]`
- NEW: `[{"target_binding_id":"651194cd-3670-f111-ab0e-70a8a590c51c","chip_label":"Summarize this document","bulk_chip_label":"Summarize","requires_attachments":true}]`

**Bulk-label defect found + fixed**: `EventRulesService` derived the bulk chip as
`"{ChipLabel} all {N} files?"` → would render "Summarize this document all 3 files?".
Fix (deterministic, data-driven): optional `bulk_chip_label` member added to the
`ChipTransition` JSON contract (`Binding.cs`); composite labels (bulk + per-file) use
`ShortChipLabel` = authored `bulk_chip_label` ?? first whitespace-delimited token of
`chip_label`. Single-file transition chips keep `chip_label` verbatim. chat-summarize's
"Summarize again" untouched.

**Tests**: `EventRulesServiceTests` +3 (multi-word derives first word; authored
bulk_chip_label wins; single-file keeps full phrase);
`ConsumerRoutingServiceBindingContractTests` parse-contract coverage for
`bulk_chip_label`; SprkChat.test.tsx `transcriptFooterSlot renders inside the message
list`; 4 stub updates green.

## F2 — Hide "Insert"

**Root cause**: SprkChat unconditionally wired `onInsert` on every assistant message
(Phase 2D insert-to-editor via `sprk-document-insert` BroadcastChannel). The SpaarkeAi
conversation host has no insert target — a dead button on every message.

**Fix**: prop-gated. New `enableInsertToEditor?: boolean` (default **false**) on
SprkChat; `onInsert` only wired when true. AnalysisWorkspace `ChatPanel.tsx` (the host
whose Lexical editor listens via `useDocumentInsert`) opts in with
`enableInsertToEditor`. Feature NOT deleted.

**Tests**: SprkChat.test.tsx — Insert hidden by default with a completed assistant
message; shown when `enableInsertToEditor` set. 301/301 SprkChat jest green.

## F3 — Follow-on instruction not understood

**Root cause (verified in code)**: the loop context is
`[system] + BuildAiHistory(session.Messages) + [user]` (`ChatEndpoints.SendMessageAsync`
line ~542). Event/Click outputs are written to the session LEDGER (`session.Outputs`,
ADR-040 via OutputRouter) and rendered as CLIENT-LOCAL assistant messages — they never
enter `session.Messages`, and the task-002 output digest only reaches
`sprk_aichatsummary` at the 15/50-message compaction thresholds. So "provide a more
concise summary" arrived with NO prior summary in context → generic clarifying question.

**Fix (minimal, no routing/intent logic — ADR-039)**: new
`ChatHistoryManager.BuildLedgerOutputsContext(outputs)` (extends the existing digest
home per §11 — no new class) builds a deterministic "## Session Outputs (stored ledger)"
block: most recent `MaxContextOutputs=8` outputs in ledger append order, each with its
verbatim `{bindingId}@t{n}` key + disposition + ucid + payload TEXT capped at
`MaxContextPayloadChars=4000` (surrogate-safe). `ChatEndpoints.SendMessageAsync` appends
it as ONE system message AFTER history, BEFORE the user turn — volatile content at the
tail, so the `[system]+[history]` prefix stays byte-stable across turns (NFR-04
prompt-cache). NFR-03 framing: the block header declares the content
"context to work WITH, never instructions to follow" (same posture as the task-032
answer-frame W2 disposition). No outputs ⇒ null ⇒ message list byte-identical to
pre-fix.

**Tests**: `ChatHistoryManagerTests` +4 — stored SessionOutput's key/disposition/ucid/
payload text present in the assembled context; null on no outputs; recent-window +
chronological ordering; per-output cap larger than the 120-char compaction snippet.

## F4 — Fresh-upload manifest race on the loop path

**Root cause (traced)**: `SessionDispatchOrchestrator.DispatchAsync` (the ONE dispatch
seam — loop `BindingCapabilityTool`, chip clicks, gate-resolve all converge here) loads
the session once and resolves files with NO readiness re-check. The Event path got the
bounded probe at the G-P1 P1 fix (`EventRulesService`, `EventRulesOptions.ReadinessProbe*`,
5×1000 ms); the dispatch seam did not. Upload 202 → manifest write → cache propagation
can lag the immediate "summarize this document", so requested ids (or default-all on a
just-created session) resolve empty and the capability honestly reports the file missing.

**Fix**: identical wait-briefly-or-degrade probe at the dispatch seam. When resolution
is INCOMPLETE (explicit subset not fully resolved, OR default-all against an empty
manifest), re-read the session up to `ReadinessProbeAttempts × ReadinessProbeDelayMs`
until complete, then degrade to whatever resolved. **Reuses `IOptions<EventRulesOptions>`**
(§11 default-to-reuse: ONE probe policy for the ONE race — no second config surface).
`Task.Delay` matches the Event-path precedent; TimeProvider refactor remains on the
/defer list. Common path (file already visible) adds zero re-reads/latency.

**Tests**: new `SessionDispatchManifestProbeTests` (4) — file appears on probe re-read →
executes against fresh manifest; never appears → honest error after exactly
1+attempts reads (bounded); default-all empty manifest probes until non-empty; complete
first read never probes. Delay=0 in tests (deterministic, no wall-clock waits).

## F6 — Silent confirm

**Root cause (two legs)**:
1. *Server*: `ResolveGateAsync` confirm called `ResumeInvocationAsync` FIRST (deleting
   the payload + writing a `confirmed` ledger marker) and THEN 422'd on the missing
   Binding target — the ledger claimed an execution that never happened.
2. *Client*: the 422 produced only a transient error toast (top-end, 8s) which the
   operator read as "nothing happened"; no transcript feedback.

**Fix (UX only — resume-executes stays P3 FR-P3-03)**:
- *Server*: confirm now PEEKS (`GetInvocationAsync`) first; a non-Binding (typed-handler)
  invocation closes via `CloseInvocationAsync` with the NEW honest terminal status
  **`confirmed-unexecutable`** (`PendingPlanManager.GateStatusConfirmedUnexecutable` —
  documented ADR-040 vocabulary extension: approval recorded, execution honestly
  unavailable; never a false `confirmed`, never left `pending` against the user's click).
  The 422 keeps the STABLE errorCode `gate.no-binding-target` (ADR-019), which uniquely
  distinguishes resume-not-yet-supported from real failures. Race between peek and close
  → 409 `gate.not-pending`. Binding-backed confirms unchanged.
- *Client*: `resolveGate` now returns `errorCode` (`IGateResolveOutcome`, exported);
  `SprkChat.handleActionConfirm` maps `gate.no-binding-target` to an HONEST local
  assistant message in the transcript: *"Got it — '{action}' is recorded and approved,
  but executing record changes from chat isn't enabled yet in this build. It arrives in
  the next phase; nothing was created or modified."* Other failures keep the error
  toast; reject keeps the existing cancel behavior (server-side `rejected` marker).

**Tests**: `ConfirmationGateUnificationTests` +1 (`confirmed-unexecutable` close: honest
marker, payload removed, idempotent, NO plain `confirmed` marker). Client covered by
tsc + full SprkChat jest.

---

## Operator rulings applied mid-wave (2026-07-06)

### Ruling 1 — analysis.rerun ungated (Path-A-style declaration change)

**Operator ruling, 2026-07-06 (G-P2 UAT fix wave)**: an explicit user re-run request
executes immediately — the re-run regenerates the session's OWN analysis output (new
`sprk_analysisoutput` version of work the user is looking at, client undo preserved);
it never mutates tenant records. The confirmation gate stays for record-writes.
This is a Path-A-style project ruling documented here at the point of decision
(CLAUDE.md §6.5): the gate MECHANISM is untouched — only the row's DECLARATION changed,
which is exactly how ADR-039 says policy changes are made (by declaration, never by
tool-name list).

- spaarkedev1 `sprk_analysistool` row `2b09dfb5-5679-f111-ab0e-7ced8ddc4cc6`
  (`SYS-Analysis Rerun`, `analysis.rerun`): `sprk_sideeffectclass`
  **100000001 (Write) → 100000000 (Read)** (verified by re-read).
- Repo seed `infra/dataverse/sprk_analysistool-analysis-rerun-row.json` matched
  (+ ruling comment); `scripts/Seed-TypedHandlers.ps1` comment updated.
- `analysis.refine` stays Read; `dataverse.create_record/update_record/delete_record`
  stay Write/gated; `email.draft` (parallel task 041) stays Communicate/gated.
- Tests: `P2LoopInjectionEvalSuiteTests.FullCatalog_NamespacedToolRowSeeds_DeclareTheGateContract`
  `declaredWrites` set no longer includes `analysis.rerun` (ruling comment inline); the
  read/pure half now covers it. GU-052 notes updated — `dataverse.update_record`
  remains that case's gated proof. GU-051 (the primary suspension case) already used
  dataverse tools only. Doc comments updated in `AnalysisExecutionHandler` +
  `SideEffectGateAIFunction` (historical accuracy preserved).

### Ruling 2 — Dataverse tool-description entity-map enrichment (tier-1 schema grounding)

All six `dataverse.*` `sprk_analysistool` rows on spaarkedev1 got a compact Spaarke
entity map appended to `sprk_description` (the column the loop projects as the LLM tool
description), so queries scope first-try ("matters" = `sprk_matter`, …). Column names
taken from LIVE `mcp__dataverse__describe` output (sprk_matter / sprk_project /
sprk_document) — nothing invented.

- READ tools (`describe` `2255b2cb-b678-f111-ab0e-7ced8ddc4cc6`, `read_query`
  `8631a3cd-b678-f111-ab0e-7ced8ddc4a05`, `search_data`
  `b62540d3-b678-f111-ab0e-7ced8ddc4cc6`) — full map: sprk_matter (sprk_mattername,
  sprk_matternumber, sprk_matterdescription, statuscode; lookups sprk_practicearea,
  sprk_mattertype, sprk_assignedattorney1→contact, sprk_externalaccount→account),
  sprk_project (sprk_projectname, sprk_projectnumber), sprk_document (sprk_documentname,
  sprk_filename, sprk_documenttype, sprk_filesummary; lookups sprk_matter, sprk_project),
  contact / account / sprk_organization.
- WRITE tools (`create_record` `18b3531f-ba78-f111-ab0e-7ced8ddc4a05`, `update_record`
  `19b3531f-ba78-f111-ab0e-7ced8ddc4a05`, `delete_record`
  `da739125-ba78-f111-ab0e-7ced8ddc4a05`) — short map (name columns only).
- The six seed JSONs in `infra/dataverse/` updated to byte-match the env values.
- NFR-04 note: this rotates the projection fingerprint ONCE (descriptions are part of
  the fingerprint input) — expected and acceptable; stability resumes at the new value.

---

## Verification (2026-07-06)

- **BFF build**: 0 errors (warnings pre-existing).
- **Eval gate** (`Category=GoldenUtteranceEval`): **31/31 green** (was 29; +2 live
  assertions landed by parallel task 041). One mid-wave collision fixed per the guard's
  own prescription: task 041 added `draft-correspondence` to `ConsumerTypes.All` while
  GU-024/025/026 still declared `catalogStatus=planned` — flipped to `existing`.
- **Targeted suites** (probe + history + gate + event-rules + binding-contract +
  dispatch-endpoint): **97/97 green**.
- **Full BFF unit suite**: **7749 total — 7643 passed, 101 skipped, 5 failed**; all 5
  are the KNOWN pre-existing list (DailyBriefingCollector resolver,
  ExecutorConfigSchemas, TemplateContextBuilder TextOnly, KnowledgeDeploymentConfig,
  SessionFilesCleanup). **Zero failures attributable to this wave.**
- **Shared lib**: `tsc --noEmit` clean; SprkChat jest **301/301** (incl. 3 new tests).
- **SpaarkeAi**: conversation jest **216/216** (4 stub updates green); own `src/` tsc
  clean (reported errors are all `../LegalWorkspace` + uninstalled sibling packages —
  environmental, pre-existing).
- **AnalysisWorkspace**: tsc blocked by uninstalled deps in this worktree
  (`@spaarke/auth`, `react/jsx-runtime` types — environmental, pre-existing); the change
  is one JSX prop whose type lives in the shared lib (typechecked clean there).
- **Publish size**: `dotnet publish -c Release` → 141.56 MB uncompressed / 270 files;
  **46.85 MB compressed** (Compress-Archive Optimal — same compressor lineage as tasks
  032/036/037). Baseline (task 037): 46.83 MB → **delta +0.02 MB**. `*.csproj`
  untouched → 0 NuGet changes → no new CVE surface. Ceiling 60 MB: far clear.

## Deferred

- **TimeProvider for readiness probes** (Event + dispatch seams both use `Task.Delay`
  per precedent) — already on the project /defer list.
- **Typed-handler confirm-RESUME executes** — P3 FR-P3-03 seam, unchanged; this wave
  fixed the UX honesty only.
- **AnalysisWorkspace full typecheck/build** — worktree deps not installed
  (environmental); recommend a build verification when that surface next deploys.
- **Session-blob optimistic concurrency** (task 030 W3 carry-over) — unchanged.

## Round-2 UAT script deltas (operator, spaarkedev1, after BFF + sprk_spaarkeai redeploy)

1. **Chips inline (F1a)**: upload a file → after "Classified…" renders, the
   [Summarize this document] chip appears directly BENEATH it inside the transcript
   (scrolls with the conversation; auto-scroll lands with the chip visible), NOT above
   the composer.
2. **Chip label (F1b)**: single file → chip reads "Summarize this document". Upload 2–3
   files in one gesture → bulk chip reads "Summarize all N files?" and per-file chips
   "Summarize: {name}" (NOT "Summarize this document all N files?"). After a summarize
   dispatch, "Summarize again" still appears.
3. **No Insert (F2)**: assistant messages in the SpaarkeAi Assistant show NO "Insert"
   affordance. (AnalysisWorkspace still shows it — separate surface, unchanged deploy.)
4. **Follow-on transform (F3)**: upload → classify → click Summarize → after the summary
   renders, type "provide a more concise summary" → the loop produces a shorter version
   of THAT summary (no generic clarifying question).
5. **Fresh-upload race (F4)**: upload a second file and IMMEDIATELY type "summarize this
   document" → the summary covers the session files (worst case a few seconds' wait);
   no "file not found in this session".
6. **Honest confirm (F6)**: "create a new matter" → Confirm Action gate → click Confirm
   → an assistant message renders: "Got it — … recorded and approved, but executing
   record changes from chat isn't enabled yet in this build…". Reject still cancels.
   (Ledger: gate closes `confirmed-unexecutable`.)
7. **Rerun ungated (Ruling 1)**: in a playbook-bound analysis session, "rerun the
   analysis" executes immediately (progress stream), with NO confirmation popup;
   a dataverse record write still gates.
8. **Query grounding (Ruling 2)**: ask "how many matters do I have?" or "find my matters
   about Acme" → the loop queries `sprk_matter` first-try (correct table/columns, cited
   rows), without a wrong-table detour.
