# Task 030 — Summarize Walkthrough (R5 Phase 1.5 acceptance, Solo SME mode)

> **Status**: 🔄 IN PROGRESS — operator-as-SME stand-in mode (Insights tool deferred — r2 Wave F not deployed)
> **Authored**: 2026-06-04 (post-PR-345-merge)
> **Companion**: `notes/task-030-sme-walkthrough.md` (full 15-question Insights matrix, deferred to Phase 3 with credentialed legal-ops SME)
> **Scope decision**: Per 2026-06-04 operator session — `/api/insights/assistant/query` returns HTTP 404 on Spaarke Dev (r2 Wave F unblocked separately). Summarize half of R5 acceptance is validated here; Insights half is held pending Wave F deploy.

---

## 1. Session metadata

| Field | Value |
|---|---|
| Operator / SME stand-in | TBD (fill in) |
| Walkthrough date | TBD (fill in) |
| Tenant | Spaarke Dev |
| BFF base URL | https://spaarke-bff-dev.azurewebsites.net |
| SpaarkeAi shell URL (sprk_SpaarkeAi Code Page) | TBD (fill in at session start) |
| R5 commit deployed | TBD (fill in — `gh pr view 345 --json mergeCommit` after deploy) |
| Insights Wave F status | NOT-DEPLOYED — `/api/insights/assistant/query` returns 404 |

---

## 2. What this walkthrough validates (R5 spec FRs in scope today)

Summarize-side coverage:

| FR / SC | Validated by |
|---|---|
| **FR-01** (convergence: same orchestrator for direct + agent-tool path) | Phase B + Phase C |
| **FR-04** (multi-file combined-summary interjection) | Phase C (≥2 files uploaded) |
| **FR-05** (single convergence method) | Implicit — both paths terminate in `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync` |
| **FR-08** (chat-session scope, not document-record scope) | All phases — sessions own files; no `sprk_document` involved |
| **NFR-02** (20-file cap, defense in depth) | Phase E |
| **SC-08** (byte-identical output for same `(TenantId, SessionId, FileIds, StyleHint)`) | Phase B + Phase C — same prompt should produce equivalent rendered output |
| **SC-18** (SME-attested usability for Phase 1.5) | Sections 4 + 5 below |

Out of scope today (deferred to Phase 3 / Wave F deploy):
- All 15 Insights-tool questions in `notes/task-030-sme-walkthrough.md`
- Tool-routing disambiguation (Summarize vs `insights.query` — requires both surfaces live)

---

## 3. Walkthrough phases

### Phase A — Smoke (~3 min)

1. Operator opens `sprk_SpaarkeAi` Code Page on Spaarke Dev (Power Apps maker portal)
2. Open a synthetic Matter (`da116923-d65a-f111-a825-3833c5d9bcb1`) or any test record
3. Confirm three-pane shell renders:
   - [ ] Assistant pane (chat composer + history)
   - [ ] Workspace pane (initially empty / Get Started card)
   - [ ] Context pane (entity context + file list)
4. Confirm no console errors in DevTools (F12 → Console tab)
5. Confirm theme matches host (Power Apps light/dark — visual sanity)

**Phase A outcome**: ☐ pass | ☐ fail (note observed issue → Section 6)

---

### Phase B — Direct `/summarize` slash command (~5 min)

1. In Assistant pane, type `/` → autocomplete menu appears
2. Select `/summarize` from the menu (or type full `/summarize`)
3. Note menu shows description text (R5 task 019 — slash command discoverability)
4. Before sending: upload 1 small document via Context pane file-upload affordance
   - Suggested: any PDF or DOCX (5-50 KB)
5. Send the `/summarize` message

**Observe in Workspace pane (StructuredOutputStreamWidget renders progressively)**:
   - [ ] `streaming_started` indicator appears (e.g., spinner / "thinking" state)
   - [ ] **TL;DR section streams first** (per task 005 FieldDelta + declaration-order contract)
   - [ ] **Summary section streams second**
   - [ ] **Keywords section streams third** (if present in output schema)
   - [ ] **Entities section streams fourth** (if present)
   - [ ] `streaming_complete` reached (final result rendered, no spinner)

**Observe in Assistant pane**:
   - [ ] Chat message acknowledges the summarize action (e.g., "Streaming summary…")
   - [ ] No raw JSON / token-by-token text leaks into the chat (the structured output goes to Workspace, not chat prose)
   - [ ] Final state shows summarize action completed (no error chip)

**Verdict**: ☐ usable | ☐ usable-with-conditions | ☐ not-usable | ☐ blocked-on-bug
**Paraphrased note**: TBD (no document content)

---

### Phase C — Agent-tool path / multi-file convergence (~7 min)

1. Same session (or new session) in Assistant pane
2. Upload 2-3 small documents via Context pane (different files)
3. Type natural-language ask (NOT `/summarize`): *"Can you summarize these files?"* or *"Give me a combined summary of the uploaded documents."*
4. Send.

**Observe**:
   - [ ] Agent invokes the tool (chat shows tool-call activity / streaming indicator)
   - [ ] **Combined-summary interjection appears in chat** (FR-04): the literal text *"I'll provide a combined summary for the files you uploaded."* (per `SessionSummarizeOrchestrator.CombinedSummaryInterjection` constant)
   - [ ] Workspace widget renders the streaming summary (same StructuredOutputStreamWidget as Phase B)
   - [ ] Output structure matches Phase B (TL;DR → Summary → ...)
   - [ ] Per-file highlights visible if the schema includes them

**Convergence sanity (SC-08)**: structure + section order should be visibly identical to Phase B. Don't compare verbatim text (LLMs vary across runs); compare shape + section order.

**Verdict**: ☐ usable | ☐ usable-with-conditions | ☐ not-usable | ☐ blocked-on-bug
**Paraphrased note**: TBD

---

### Phase D — Per-file affordance (~5 min)

1. In Context pane, with multiple files uploaded, look for per-file Summarize affordance:
   - Hover, right-click, or three-dot menu on a single file row
   - Expected affordance: "Summarize this file" (or equivalent)
2. Click the per-file affordance for ONE specific file
3. Observe:
   - [ ] FilePreviewContextWidget opens (or Workspace pane updates) — per-file view
   - [ ] Summarize streams for THAT file only (not the full session)
   - [ ] Result references only the selected file (per R5 task 021 — single-file Summarize hook)

**Verdict**: ☐ usable | ☐ usable-with-conditions | ☐ not-usable | ☐ blocked-on-bug
**Paraphrased note**: TBD

---

### Phase E — Error / cap behavior (~3 min)

This phase is structural smoke — we don't need to actually hit the cap, just confirm graceful UX:

1. Attempt to upload more than 20 files in one session (NFR-02 cap)
2. Observe:
   - [ ] Upload UI rejects file #21+ with a clear message (preferred); OR backend returns 400 with `errorCode: "summarize.too-many-files"` and the chat surfaces it as a ProblemDetails error chip (acceptable fallback)
   - [ ] No silent failure (no submit-without-feedback)
   - [ ] No partial corrupted state (session isn't bricked after the error)

If hitting the cap is impractical with available files, skip this phase and mark *deferred-structural-only*.

**Verdict**: ☐ usable | ☐ usable-with-conditions | ☐ not-usable | ☐ deferred | ☐ blocked-on-bug
**Paraphrased note**: TBD

---

### Phase F — Optional: Feature-disabled path (~2 min)

OPTIONAL — only run if you want to verify the kill-switch contract from PR #345 fix:

The ADR-030 P3 Fail-Fast pattern (added in this PR) means that when `Analysis:Enabled=false` or `DocumentIntelligence:Enabled=false`, the Summarize endpoint should return a 503 ProblemDetails with `errorCode: "ai.summarize.disabled"` instead of 500.

On Spaarke Dev these flags are TRUE, so this path isn't reachable here. Skip unless you have a stage where flags are flipped off.

---

## 4. Per-phase verdict summary

| Phase | Verdict | Brief paraphrased note |
|---|---|---|
| A — Smoke | ✅ usable | Three-pane shell renders cleanly on Spaarke Dev (`sprk_SpaarkeAi` Code Page). Standalone-launch mode shows Quick Start in Context (no entity scope) — correct behavior since Code Page wasn't launched from a Matter/Project form. MSAL auth bootstraps, Corporate Workspace auto-installs. Two console warnings noted as Sev-3 in §6. |
| B — Direct /summarize | ❌ blocked-on-bug | Partial. File upload via `[action:upload]` worked after Sev-2 fix #2 (live-patched + redeployed mid-session). However: (a) the `/summarize` slash command does NOT invoke the R5 direct endpoint — frontend sends literal text to `/messages` (Sev-1 finding #3); (b) the chat agent reports it cannot see the uploaded document despite successful upload (Sev-1 finding #4). The R5 backend SessionSummarizeOrchestrator + endpoint are deployed and reachable via curl with bearer (401 from auth middleware, not 404 — verified), but no UI path actually invokes them. The convergence promise (FR-01 / FR-08 / SC-08) is not testable in the deployed UI. |
| C — Agent-tool / multi-file | ❌ blocked-on-bug | Blocked on the same Sev-2 upload bugs. |
| D — Per-file affordance | ❌ blocked-on-bug | Blocked on the same Sev-2 upload bugs. |
| E — Cap behavior | ⏭️ deferred | Cannot exercise the 20-file cap when 1 file won't upload. |

---

## 5. Aggregate Phase 1.5 acceptance

Summarize vertical aggregate verdict (1–5 scale per the canonical SC-18 rubric):

**Aggregate score**: TBD / 5

- 5 = "Streaming UX consistent + convergent + per-file works; no blockers"
- 4 = "Usable; minor calibration gaps that don't block Phase 1.5"
- 3 = "Mixed; specific gaps to address in Phase 2 / R6"
- 2 = "Partially usable; significant gaps"
- 1 = "Not usable (Sev-1 blocker)"

Target for Phase 1.5 acceptance: **≥ 3/5** (Summarize-only scope).

---

## 6. Sev-ranked findings

| Sev | Finding (paraphrased) | Phase | Recommended owner | R6 backlog entry |
|---|---|---|---|---|
| Sev-2 | **Composer paperclip upload affordance is a silent no-op.** Clicking the paperclip in the Assistant composer opens the file picker; selecting a file produces no network call, no console error, no UI feedback. Frontend handler appears unwired. Blocks the entire Phase 1.5 Summarize vertical (Phases B/C/D/E all depend on session-file upload). | A → B (failed at Step 2) | R5 frontend owner (chat composer file-attach UX) | R6 candidate |
| Sev-2 | **`[action:upload]` button returned 401 from `POST /api/ai/chat/sessions/{id}/documents`** with ProblemDetails `"Tenant identity not found in token claims"`. ROOT CAUSE: `ChatDocumentEndpoints.cs` only checked the short `tid` claim form; Microsoft.Identity.Web renames `tid` → `http://schemas.microsoft.com/identity/claims/tenantid` on the ClaimsPrincipal in this environment. Other endpoints (e.g., `ChatEndpoints.cs:2012`, `SummarizeSessionEndpoint.cs`) check both forms. **FIX APPLIED LIVE TO DEV 2026-06-04** via direct edit + Deploy-BffApi.ps1 — added schema URL fallback at both occurrences (lines 151 + 443). MUST be re-applied as a proper PR to master (otherwise next deploy overwrites). | B (Step 2) | Backend owner (pre-existing R3 bug in ChatDocumentEndpoints) | Follow-up PR to master required |
| **Sev-1** | **R5 Summarize UX broken end-to-end: chat agent does not see uploaded session files, and no auto-trigger fires when an upload completes.** Operator UX expectation (clarified 2026-06-04): (a) user says "summarize" (or types `/summarize` as shorthand); (b) if no document present, Assistant prompts for upload via chat icon or inline `[action:upload]` link; (c) when upload completes, summary RUNS AUTOMATICALLY. Actual observed behavior: file upload succeeds (HTTP 200) and Assistant renders "Document added to context", but no summary runs. When user then re-issues `/summarize`, agent responds: *"I don't see the document uploaded yet."* This is one root cause (uploaded session files invisible to the agent's tool-call decision-making layer) with two symptoms (no auto-trigger + agent claims no file present). Likely involves some combination of: (1) `InvokeSummarizePlaybookTool` not registered for session's playbook (capability-manifest gap — known forwarded item §6.5 in current-task.md), (2) `PlaybookChatContextProvider` does not include `ChatSession.UploadedFiles[]` in agent context, (3) no upload-completion event handler triggers a Summarize call. The `/summarize` slash command going to `/messages` (verified via Network tab) is the CORRECT routing — slash is shorthand for natural-language intent. The bug is the agent's state/tool-availability, not the slash routing. **Violates spec FR-01 / FR-08 / SC-08** — both convergence paths (direct + agent-tool) are effectively unreachable as a user UX, even though backend endpoints exist and respond correctly to direct curl. | B (Step 4) | R5 backend owner (chat-context integration with session-files manifest + capability-manifest gate + upload-completion hook) | R5 closeout work required before Phase 2 close |
| Sev-3 | `GET/PATCH /api/ai/chat/sessions/{id}/tabs → 404` on shell load. Pre-existing R3/R4 surface; frontend still references an endpoint not deployed. Non-blocking; no user-visible impact. | A | R3/R4 owner (chat-tabs cleanup) | R6 candidate |
| Sev-3 | `GET blob:.../{guid} → ERR_FILE_NOT_FOUND` on shell load. Transient blob URL race during React mount/unmount cleanup. No user-visible impact. | A | Frontend (defensive cleanup) | R6 candidate |

**Sev-1**: contract violation, no-leakage failure, stream never terminates, kill-switch leaks 500 not 503. BLOCKS task 030 close → STOP + escalate.
**Sev-2**: UX rough edges, calibration gaps, latency-too-slow (>15s p95). Feeds task 044.
**Sev-3**: polish. Feeds task 044.

---

## 7. Operator-as-SME signoff (Phase 1.5)

```
Phase 1.5 Summarize-vertical acceptance — operator-as-SME stand-in mode

I, ______________________________ (operator name), walked through the
Summarize vertical (Phases A-E above) on the Spaarke Dev tenant on
______________________________ (date), in solo operator + SME stand-in
mode (no credentialed legal-ops SME present this session).

Aggregate verdict: ____ / 5

Phase 1.5 acceptance: [ ] yes  [ ] yes-with-conditions  [ ] no

If yes-with-conditions: list Sev-2/3 findings in Section 6 above; queue for
Phase 3 task 044 (lessons-learned + R6 backlog) re-validation by a
credentialed legal-ops SME.

If no: list Sev-1 blockers in Section 6 above; STOP + escalate.

Signed: ______________________________
Date:   ______________________________
```

---

## 8. Insights-side validation (DEFERRED)

The 15-question Insights tool matrix in `notes/task-030-sme-walkthrough.md` is held until:
- r2 Wave F deploys `/api/insights/assistant/query` to Spaarke Dev (currently 404)
- AND a credentialed legal-ops SME is scheduled for a ~45-min session

Until then, the Insights tool integration (R5 Phase 2 Wave E1-E5 code) ships dormant — backend wired, frontend renderer ready, error-code handling in place — and lights up automatically when Wave F is live.

Cross-link: `notes/insights-r2-coordination.md` §8 changelog (operator updates when Wave F lands).

---

*Walkthrough designed 2026-06-04. Fill Sections 1, 3 (checkboxes + verdicts), 4, 5, 6, 7 at session time. Status flips 🔄 → ✅ when Section 7 signoff is captured.*
