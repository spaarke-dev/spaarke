# Task 045 — Client consolidation (FR-P3-06) — Task Notes

> Date: 2026-07-06 · Wave W-P3-C · task-execute FULL rigor.
> Hard boundaries honored: no commit/push; no TASK-INDEX/current-task edits; no `.claude/` writes;
> no server-side files touched (task 047 owns OutputRouter/EngineOutputLedgerAdapter/persistence).

## 1. ConversationPane thin-host decomposition

**Measured line count: `ConversationPane.tsx` = 300 lines (budget ≤300 — MET, no exception needed; final count after the Step 9.5 review fix).**
Was 3,172 lines. Decomposed into focused modules in the same directory (semantics preserved
verbatim; SpaarkeAi conversation jest 216/216 green before AND after):

| Module | Lines | Concern |
|---|---|---|
| `ConversationPane.tsx` (host) | 298 | layout + session context + PaneEventBus wiring only |
| `summarizeRouting.ts` | 179 | pure /summarize tri-mode routing + message helpers (re-exported from host for test-import compat) |
| `useInjectionQueue.ts` | 57 | single-slot + ordered Assistant-message injection |
| `useEventBatch.ts` | 346 | Event-path count-complete upload batching state machine (**the G-P1-deferred "ConversationPane batch-state extraction" lands here**) + outbound-message tracking |
| `useAttachments.ts` | 445 | chip mirror, ready confirmations, held files, **promotion queue** (sequential /documents auto-promote + retry), sessionAttachmentCount, before-send interjection |
| `useConsumerChips.tsx` | 158 | Click path: binding_id chips → the ONE `dispatchConsumer`; transcriptFooterSlot node |
| `useContextEventBridge.ts` | 132 | context_event SSE → `context` bus (trace widget) + consumer_chips carrier |
| `usePlaybookSelection.ts` | 108 | gallery selection, header strip + toast |
| `usePlaybookOptions.ts` | 192 | playbook_options message + Library modal launch |
| `useCommandRouting.ts` | 243 | Pillar 8 hard slashes + reference resolution at the outbound-body seam |
| `useSelectionChip.ts` | 107 | "Refine this?" chip + focused-tab tracking |
| `ConversationPaneChrome.tsx` | ~395 | presentational strips/banners + all pane styles (ADR-021 tokens, moved verbatim) |

**Dead paths deleted during decomposition** (verified never-invoked; conversation jest green):
- `dispatchSummarizeIntent` (local useCallback, void-referenced, zero callers) + the
  `pendingSummarizeInterjection` prompt-first rendering surface it alone fed. The pure
  `routeSummarizeIntent` contract (incl. branch (c)) is unchanged and still test-covered.
- The welcome `pendingMessage` predefined-prompt entry — only ever set to `null` since task 068
  made WelcomePanel heading-only.
- The capture-only `parseCommandIntent` call in `onBeforeSendMessage` (R6 task 080 leftover —
  the live parse happens at the `onDecorateOutboundBody` seam).
- `handleOpenLibraryModal` ref/TDZ indirection (`openLibraryModalRef`) — hook ordering makes the
  stable callback directly wireable.

**Flagged (NOT deleted — 046/Track-B scope note)**: the `playbook_options` client leg
(`usePlaybookOptions`) is dormant wiring — the server emitter was retired at task 035 (FR-P2-06)
and `/api/ai/playbook-dispatch/execute` was never implemented. Kept for the SprkChat prop
contract; recorded as a Track-B / FR-P4-01 deletion candidate.

## 2. Compose `executeComposeSummarize` disposition

Already DELETED on 2026-07-02 by parallel project `spaarkeai-compose-r1` (commit `0420562e6`,
"retire Compose AI dispatch client (Phase A)") — the orchestrator, its SSE client, the Toolbar
Summarize button, and the ConversationPane subscription all died there. This task's contribution:
grep-zero re-verified (shown in transcript) and the stale `dispatchConsumer.ts` JSDoc promise
("P3 migrates … executeComposeSummarize") corrected.

## 3. LegalWorkspace `summarizeService` disposition (judgment call — documented)

- The LW-local `src/solutions/LegalWorkspace/src/components/SummarizeFiles/` cluster
  (summarizeService + dialog) is an **orphaned duplicate**: zero importers outside its own folder
  (grep shown in transcript); the live implementation is the shared-lib hoist
  `Spaarke.UI.Components/src/components/SummarizeFilesWizard/` (O-20). Disposition: **DELETED**
  (hard cutover, NFR-08) — this closes the "LegalWorkspace summarizeService" leg by removal.
- The live shared `SummarizeFilesWizard/summarizeService.ts` had a byte-identical hand-rolled SSE
  loop. Disposition: **parse loop consolidated onto canonical `readSseStream`/`parseSseEvent`**
  (ONE parse path client-wide holds).
- **Why NOT a full re-point onto `dispatchConsumer(bindingId, …)`** (the letter of the POML):
  the wizard's wire contract is *multipart local-file upload* to
  `POST /api/workspace/files/summarize`, where the server resolves the `summarize-file` Binding
  row (task-040-verified `271194cd-3670-f111-ab0e-70a8a590c51c`) by consumerType — ADR-039's
  server-owned resolution with the client carrying ZERO routing. `dispatchConsumer` targets the
  session-dispatch seam (`/sessions/{id}/dispatch`), which resolves **session-manifest files** and
  cannot carry multipart uploads. A faithful migration would require either (a) a server
  wire-contract change (file-carrying dispatch) — **out of this task's boundary ("STOP and note")**
  — or (b) the wizard minting throwaway chat sessions + sequential /documents promotion, which
  changes UX (loses the wizard's per-step progress stream, pollutes the History menu with phantom
  sessions) contra the task's own "identical behavior to pre-migration" acceptance. Escalated in
  the task report; the delivered state satisfies the FR's acceptance line ("one SSE parse path
  client-wide; ConversationPane under budget") with the Binding-addressed execution living
  server-side.

## 4. Slash → Click deterministic launchers (operator-deferred item)

The POML is silent on slash handling (its step 4 covers wizard/launcher binding ids only). Per the
task directive, nothing beyond the POML was implemented. Disposition recorded: the minimal
ADR-039-conform shape (capability-discovery READ mapping the closed soft-slash vocabulary to
per-environment Binding GUIDs, dispatched via `dispatchConsumer`) requires a **new server
endpoint** — out of boundary for this client task ("STOP and note"). Noted in
`useCommandRouting.ts` JSDoc + this file. Client-hardcoded Binding GUIDs were rejected
(per-environment values; ADR-039 keeps resolution vocabulary server-owned).

## 5. Wizard/launcher binding-id criterion — survey result

Every live Click-path invocation is Binding-addressed:
- ConsumerChips carry `binding_id` end-to-end (tasks 022/023) → `dispatchConsumer`.
- Elicitation-modal wizard completion contract is `dispatchConsumer(bindingId, {slots})` by design.
- `WorkspaceShell/wizardLaunchers.ts` + GetStarted cards launch Code Page **modals** via
  Xrm.Navigation (no capability dispatch on the client); the launched wizard's summarize execution
  is Binding-resolved server-side by consumerType.
- Widget-layer grep (Spaarke.AI.Widgets): no capability invocation outside the chip path
  (FeedbackButtons posts feedback; AiSessionProvider manages sessions).
- Prior-deletion grep-zero re-verified: `intentMatcher` / `executeSummarizeIntent` /
  `sseToPaneEventBridge` = zero hits in src.

## 6. Concurrency-safe manifest append (deferred-item check)

POML does not include it; the client leg shipped earlier (sequential promotion, G-P1 hardening,
preserved verbatim in `useAttachments.ts`); the server-side readiness probe landed in the G-P2 fix
wave (F4). Nothing further implemented here — noted per the task directive.

## 7. SSE parser consolidation inventory (NFR-08)

See task report for the full per-file table + grep-zero output. Documented keep-with-reason
escalation: `src/client/office-addins/shared/taskpane/services/SseClient.ts` — separate runtime
surface with NO `@spaarke/*` dependency and richer SSE-spec semantics (event/id/retry fields +
401-reconnect) that `readSseStream` does not provide; consolidation would be a functional
regression + a new cross-package dependency. Requires an operator ruling (accept keep-with-reason
or fund an office-addins dependency change); excluded from the grep-zero scope with this reason.

## 7b. Chat-hook triples — final state

- `useSseStream`: ONE implementation (`Spaarke.UI.Components/src/hooks/useSseStream.ts`);
  the SprkChat-local module is a re-export (AIPU2-082) — verified.
- `useChatSession` (AI.Context vs SprkChat): **justified deferral, NOT converted to a
  re-export** — genuinely divergent published contracts: (1) substrate mismatch —
  `@spaarke/auth.authenticatedFetch` THROWS ApiError on non-2xx while the SprkChat hook is
  written against response-returning fetch (its 404/staleSession probe would become dead
  code); (2) result-type divergence (`loadHistory` return shapes; `resumeSession` absent from
  AI.Context's `IUseChatSessionResult`); (3) AI.Context's `ChatApiClient` MUST use
  `buildBffApiUrl` per `.claude/constraints/auth.md` while the shared hook uses template
  literals; (4) delegation would add a heavy `@spaarke/ui-components` dep edge to
  `@spaarke/ai-context` (mammoth/pdfjs), contra the AIPU2-082 precedent (delete, don't
  delegate). Zero SSE code in the hook (NFR-08 unaffected). Future-migration recipe: make the
  shared hook substrate-agnostic, major-version the AI.Context result type (+`resumeSession`),
  migrate the one live consumer (`AnalysisWorkspace/AnalysisAiContext.tsx`) in the same PR.

## 8. Verification summary (2026-07-06)

- **SpaarkeAi**: conversation jest **216/216** (12 suites) green before AND after
  decomposition; full-package jest 342 passed / 13 failed in exactly the 5 KNOWN pre-existing
  /defer'd non-conversation suites (ContextPaneController, WorkspacePane.summary-tab,
  WorkspaceTabManagerComponent.hideTabBar, DocumentComposeLaunch ribbon, launch-resolver);
  production `npm run build` (vite + tsc-surface-gate + html-reset gate + ribbon) **exit 0**.
  Host = **298 lines**.
- **Spaarke.UI.Components**: package build (`tsc`) **exit 0**; targeted suites for every
  migrated call-site **344/344** green (incl. new readSseStream FormData/fetchImpl tests);
  full jest 272 suites pass / 7 fail — all 7 proven pre-existing by stash-baseline.
- **LegalWorkspace**: production `npm run build` (vite) **exit 0** after cluster deletion +
  matterService consolidation; `tsc --noEmit` 247 errors vs 249 on the pre-change stash
  baseline (net −2; zero errors in touched files).
- **PlaybookBuilder**: production webpack build **exit 0**; jest **122/122**; tsc 22 errors
  all pre-existing in untouched files (0 in aiPlaybookService.ts).
- **AnalysisWorkspace**: `npm install --legacy-peer-deps` UNBLOCKED the env-blocked package;
  production webpack build **exit 0**; jest 30 passed / 5 suites fail — empirically proven
  pre-existing (stash re-run identical; @spaarke/auth ESM jest-transform + missing-mock
  issues, zero references to the touched file); tsc 32 errors all pre-existing (0 in
  analysisApi.ts).
- **SSE grep-zero (NFR-08)**: `getReader()|new EventSource|new TextDecoder` over `src/`
  (excl. dist): remaining non-test SOURCE hits = `useSseStream.ts` (the canonical loop) and
  `office-addins SseClient.ts` (documented §7 exception) only; all other hits are jest
  fixtures or compiled PCF `Solution/**/bundle.js` artifacts.
- **Dispatch-migration grep**: `executeComposeSummarize` → 1 hit (historical-note comment in
  dispatchConsumer JSDoc); LW `summarizeService|streamSummarize` → 0 hits; deprecated
  `runSummarize` → deleted (1 comment hit).
- **Wire-level micro-deltas** (reviewer visibility): the migrated PlaybookBuilder /
  AnalysisWorkspace calls no longer send `Accept: text/event-stream` and now send
  `X-Tenant-Id` (inherent to readSseStream; the same BFF already serves SprkChat with these
  semantics; builds/tests green).

## 10. Step 9.5 quality gates (2026-07-06)

- **code-review + adr-check**: PASS, zero Critical. ADR verdicts all ✅ (ADR-039/040/037-am/
  021/028/013-am/030/015 + NFR-08 + ADR-038). Decomposition fidelity verified line-by-line
  against the pre-change monolith on all five scrutinized risk areas (event-batch membership/
  settlement, session reset [membership+openedAt KEPT], inject-vs-enqueue mapping,
  sessionAttachmentCount union, readSseStream extension).
- **Warning 1 (FIXED in-task)**: hook controllers returned fresh object literals each render,
  re-triggering the auto-promote effect per render. Fix: all 9 controller hooks memoize their
  return objects; `useAttachments` keys its callbacks/effect on the destructured stable
  eventBatch methods; host `handleSessionCreated` keys on the stable reset methods. Re-verified:
  conversation tsc clean, jest 216/216, production build exit 0; host recounted at 300 lines.
- **Warnings 2–5 (documented dispositions, no change)**: residual `/summarize` presentation-only
  prefix matcher (pre-existing R5; dispatch leg deleted THIS task — Track-B/FR-P4 disposition);
  dormant `playbook_options` wiring (046/Track-B deletion candidate); string-coupled
  `'Response body is empty'` match in SprkChat plan-approve (suggest typed error follow-up);
  ADR-015 identifier/filename logs carried over verbatim from the monolith (tighten in follow-up).

## 9. Gate-048 UAT additions (browser rule NFR-11 — operator on spaarkedev1)

1. SpaarkeAi Assistant: send a chat message → response streams token-by-token; console error-free.
2. Upload → classify → chips inline in transcript → click "Summarize this document" → summary
   renders; "Summarize again" re-arms (Click path through dispatchConsumer unchanged).
3. Upload 2–3 files → "N files attached" indicator + "(N indexed)"; combined-summary interjection
   on `/summarize`-style message with 2+ files.
4. `/help`, `/clear`, `/export`, `/playbooks` hard slashes still work (command routing seam moved).
5. "Refine this?" chip after selecting text in a workspace widget.
6. Summarize Files wizard (Get Started → Summarize Files): upload files → per-step progress →
   summary + highlights render (parser consolidated; endpoint unchanged).
7. Create Matter wizard AI draft-summary step streams (matterService loop consolidated).
8. Dark mode: repeat 1–2 in dark theme (chrome extraction moved styles verbatim — tokens only).
9. AnalysisWorkspace + PlaybookBuilder smoke: analysis execution streams; PlaybookBuilder AI
   assistant streams (their parsers consolidated onto readSseStream).
