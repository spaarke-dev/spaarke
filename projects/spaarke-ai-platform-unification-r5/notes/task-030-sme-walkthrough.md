# Task 030 — SME Walkthrough: Insights tool integration (D2-20)

> **Status**: 🔄 **PENDING — operator to schedule + complete in a future session**
> **Scaffold authored**: 2026-06-04
> **SME walkthrough date**: TBD
> **Task**: D2-20 — `projects/spaarke-ai-platform-unification-r5/tasks/030-insights-tool-smoke-tests.poml`
> **Binding**: spec SC-18 — UX walkthrough with ≥1 legal-ops SME on Spaarke Dev tenant; signoff captured here
> **Companion evidence**: `projects/spaarke-ai-platform-unification-r5/notes/task-030-smoke-evidence.md` (15-question structural matrix outcomes)

---

> ## ⚠️ NO-VERBATIM-CONTENT RULE (ADR-018 + integration brief §5.2)
>
> When filling this file at walkthrough time:
> - ✅ **Verbatim OK**: question text, citation source display names (e.g., "Acme APA.pdf"), correlationId values, SME name + role + date, signoff statement
> - ❌ **Paraphrase ONLY**: response `Answer` text, citation `Excerpt` content, document fragments, LLM-generated prose, SME observations that reference specific document content
> - Synthetic entities are designed to make this discipline easy (no real customer data). The rule applies regardless — train muscle memory for future tasks that may touch real data.

---

## 1. SME session metadata (to be filled at walkthrough time)

| Field | Value |
|---|---|
| SME name | TBD |
| SME role | TBD (e.g., "Legal-ops practitioner, contracts area" / "IP attorney") |
| SME organization | TBD |
| Walkthrough date | TBD |
| Walkthrough duration | ~30–45 min (target) |
| Tenant | Spaarke Dev (`https://spaarke-bff-dev.azurewebsites.net`) |
| Operator (driver) | TBD |
| Wave F deployment status at walkthrough | v1.0 / v1.1 (record at session start; consume `notes/insights-r2-coordination.md` §8) |

---

## 2. Walkthrough script (operator-led, ~30–45 min)

### Phase A — Orientation (5 min)

Operator explains to the SME:

1. **Purpose**: validate that the Spaarke Assistant's `insights.query` tool — exposed in the SpaarkeAi chat — produces responses a legal-ops practitioner would find usable for the Phase 1.5 acceptance bar.
2. **Scope**: 15 natural-language questions across CTRNS / IPPAT / BNKF practice areas, scoped to three synthetic test entities (Wave D7 GUIDs reserved by Insights Engine r2 team):
   - Matter `da116923-d65a-f111-a825-3833c5d9bcb1`
   - Project `27845394-8e5f-f111-a825-70a8a59455f4`
   - Invoice `05c8ef8d-8e5f-f111-a825-70a8a59455f4`
3. **What the tool does**: routes server-side between a structured playbook path (predictive numeric envelopes) and a citation-grounded RAG path (cited prose). Both paths return uniform-shape citations.
4. **What the SME judges (qualitative)**: would this response be USEFUL in actual legal-ops work? Categorical verdict per question (`usable` / `partial` / `not-usable`) + brief paraphrased note. Aggregate per practice area at session end.
5. **What the SME does NOT judge**: HTTP status, JSON shape, contract conformance — those are asserted independently by the integration test suite (see `task-030-smoke-evidence.md` Section 6).
6. **No-verbatim-content rule reminder**: SME observations are paraphrased into the notes; specific document content is NEVER pasted. Synthetic entities make this easy.

### Phase B — 15-question walkthrough (~25–30 min)

Operator drives the SpaarkeAi chat-agent UI (https://spaarke-dev tenant). For each of the 15 questions (text in `tests/integration/Spe.Integration.Tests/fixtures/insights-smoke-matrix.json`):

1. Operator selects the appropriate active entity in SpaarkeAi (matter / project / invoice per the `subject` column).
2. Operator types the question text into the chat composer (or uses `/ask-insights` slash command if explicit forceMode is intended).
3. Together with the SME, observe:
   - Did the response render in the expected widget (playbook structured envelope vs RAG citation-grounded prose)?
   - Are citations clickable (v1.1) or display-name-only (v1.0)? (Either is contract-acceptable per NFR-11.)
   - Is the confidence badge calibrated reasonably? (Task 028 floor: `< 0.6` shows "low-confidence" badge.)
   - If decline path: are `SuggestedActions` surfaced reasonably? (Per integration brief §4.5.)
   - If empty-results: is the "couldn't find anything" hint shown clearly? (NOT verbatim empty `answer` — anti-hallucination per FR-04.)
4. SME assigns categorical verdict per question (see Section 4 below).

Pace: ~2 min per question + ~3s inter-request gap (the chat-agent's natural rhythm handles rate limits). 15 × 2 min ≈ 30 min.

### Phase C — Aggregate verdict per practice area (~5 min)

After all 15 questions, SME synthesizes:

- CTRNS aggregate: `usable` / `partially usable` / `not usable` + paraphrased rationale
- IPPAT aggregate: `usable` / `partially usable` / `not usable` + paraphrased rationale
- BNKF aggregate: `usable` / `partially usable` / `not usable` + paraphrased rationale

### Phase D — Signoff (~5 min)

SME provides one of:
- ✅ **Phase 1.5 signoff**: "Responses are usable for the intended Phase 1.5 acceptance bar" (records name + date here)
- ⚠️ **Conditional signoff**: "Usable with the following gaps to address in Phase 2 / R6" — list Sev-2/3 findings
- ❌ **Blocker**: "Sev-1 blocker(s) identified" — STOP, escalate per task 030 POML Step 9

---

## 3. SME credentials gate (operator pre-check)

Before the walkthrough, confirm:

| Pre-check | Status |
|---|---|
| SME has SpaarkeAi user account on Spaarke Dev tenant | ☐ |
| SME has read access to the 3 Wave D7 synthetic entities (matter / project / invoice) | ☐ |
| SME has access to the SpaarkeAi chat-agent UI (not just BFF Swagger) | ☐ |
| SME briefed on no-verbatim-content rule | ☐ |
| SME briefed that synthetic entities are NOT real customer data | ☐ |

---

## 4. Per-question SME categorical verdicts (PARAPHRASED ONLY)

For each row: paraphrase the SME's observation; do NOT paste verbatim answer text or citation excerpts. Question text is OK (synthetic).

| ID | Practice area | Subject | Question (verbatim OK) | Observed path (playbook/rag) | Confidence badge shown? | SME verdict (`usable` / `partial` / `not-usable`) | SME paraphrased note |
|---|---|---|---|---|---|---|---|
| ctrns-001 | CTRNS | matter | "What are the closing conditions in this matter?" | TBD | TBD | TBD | TBD (paraphrase only) |
| ctrns-002 | CTRNS | matter | "What is the predicted cost of this matter?" | TBD | TBD | TBD | TBD |
| ctrns-003 | CTRNS | matter | "Who are the parties to this transaction?" | TBD | TBD | TBD | TBD |
| ctrns-004 | CTRNS | project | "What's the tail policy obligation here?" | TBD | TBD | TBD | TBD |
| ctrns-005 | CTRNS | matter | "Is there a regulatory approval requirement in this matter?" | TBD | TBD | TBD | TBD |
| ippat-001 | IPPAT | matter | "What patents are claimed in this matter?" | TBD | TBD | TBD | TBD |
| ippat-002 | IPPAT | project | "What's the priority date for the parent application?" | TBD | TBD | TBD | TBD |
| ippat-003 | IPPAT | project | "Who are the named inventors on this project?" | TBD | TBD | TBD | TBD |
| ippat-004 | IPPAT | project | "What's the prosecution strategy here?" | TBD | TBD | TBD | TBD |
| ippat-005 | IPPAT | project | "Are there any pending office actions on this project?" | TBD | TBD | TBD | TBD |
| bnkf-001 | BNKF | invoice | "What's the total billed on this invoice?" | TBD | TBD | TBD | TBD |
| bnkf-002 | BNKF | matter | "What's the average matter cost across similar transactions?" | TBD | TBD | TBD | TBD |
| bnkf-003 | BNKF | matter | "What collateral securitization is documented here?" | TBD | TBD | TBD | TBD |
| bnkf-004 | BNKF | matter | "What's the loan covenant compliance status?" | TBD | TBD | TBD | TBD |
| bnkf-005 | BNKF | invoice | "Are there any outstanding fee disputes on this invoice?" | TBD | TBD | TBD | TBD |

---

## 5. Aggregate per-practice-area verdict

| Practice area | SME aggregate verdict (1–5 usability scale) | Paraphrased rationale |
|---|---|---|
| CTRNS | TBD / 5 | TBD |
| IPPAT | TBD / 5 | TBD |
| BNKF | TBD / 5 | TBD |

**Usability scale**:
- 5 = "Responses consistently useful in actual practice; no blockers"
- 4 = "Usable; minor calibration gaps that don't block Phase 1.5"
- 3 = "Mixed; specific gaps to address in Phase 2 / R6 (file Sev-2 findings)"
- 2 = "Partially usable; significant gaps (file Sev-1 if it blocks Phase 2 close)"
- 1 = "Not usable in current state (Sev-1 blocker; STOP + escalate)"

Target for Phase 1.5 acceptance: ≥ 3/5 per practice area; aggregate ≥ 4/5 across the three.

---

## 6. Cross-tool disambiguation observations (informational; informs task 031)

During or after the matrix, operator + SME exercise prompts that COULD route to either `/summarize` or `insights.query`. Paraphrased observations:

| Prompt | Expected routing | Observed routing | SME note (paraphrased) |
|---|---|---|---|
| "summarize this matter" | TBD (could be either per NFR-12 tool description discipline) | TBD | TBD |
| "what's the cost of this matter" + uploaded files | TBD (`insights.query` predict-matter-cost playbook OR `/summarize` of files — both plausible) | TBD | TBD |
| "tell me about this invoice" + no upload | TBD (`insights.query` RAG on invoice) | TBD | TBD |

These are **NOT pass/fail for task 030** — they feed task 031's design of the consolidated cross-tool verification suite.

---

## 7. Sev-ranked findings backlog

| Sev | Finding (paraphrased) | Affected question(s) | Cross-link (R5 artifact / R6 backlog item) | Recommended owner |
|---|---|---|---|---|
| TBD | TBD | TBD | TBD | TBD |

**Sev-1 (blocking)**: contract violation, no-leakage failure, SC-11 or SC-18 not met. BLOCKS task 031 + Phase 2 close. STOP + escalate.

**Sev-2 (Phase 3 / R6 candidate)**: UX rough edges, calibration gaps, SME-flagged usability issues. Feeds task 044 (D3-05 lessons-learned + R6 backlog).

**Sev-3 (nice-to-have)**: polish, observation-only patterns. Feeds task 044.

---

## 8. SME signoff (binding per spec SC-18)

When SME completes the walkthrough, paste signoff verbatim:

```
SME signoff — Spaarke Insights Tool Phase 1.5

I, ______________________________ (name + role), have walked through the
15-question Insights tool smoke matrix on the Spaarke Dev tenant on
______________________________ (date) with operator ______________________________ (name).

My aggregate verdict: ____________________________________________________
(usable / usable-with-conditions / not-usable + paraphrased rationale)

CTRNS aggregate: ____ / 5
IPPAT aggregate: ____ / 5
BNKF aggregate: ____ / 5

Sev-1 blockers identified: ____________________________________________________
(NONE expected; if any → STOP + escalate to operator per task 030 POML Step 9)

Sev-2/3 findings recorded in Section 7 above for R6 backlog.

Phase 1.5 acceptance signoff: [ ] yes  [ ] yes-with-conditions  [ ] no

Signed: ______________________________ (SME signature / typed name)
Date:   ______________________________
```

Without this signoff, task 030 does NOT close. Task 031 (Phase 2 verification) and Phase 2 close gate on this signoff being captured.

---

## 9. Status as of scaffold authoring (2026-06-04)

This file is a **TEMPLATE** authored by the R5 task 030 sub-agent on 2026-06-04. The SME walkthrough has been **DEFERRED** by operator decision pending future session scheduling.

**State**:
- ✅ Scaffold complete (this file + companion `task-030-smoke-evidence.md` + integration test class + 15-question JSON matrix)
- ☐ SME identified, scheduled, briefed — PENDING
- ☐ 15-question walkthrough executed — PENDING
- ☐ SME signoff captured in Section 8 — PENDING
- ☐ POML status updated `complete-code-scaffold-pending-sme` → `complete` — PENDING
- ☐ TASK-INDEX 030 `🔄 → ✅` — PENDING

**Re-opens** when operator schedules SME. At that point, fill Sections 1, 4, 5, 6, 7, 8 above; update `task-030-smoke-evidence.md` Sections 6, 7, 8 in parallel; close out task 030 + cascade to task 031.

---

*Template authored 2026-06-04 by R5 task-execute (task 030 sub-agent). DO NOT modify the no-verbatim-content rule or the signoff template without operator approval.*
