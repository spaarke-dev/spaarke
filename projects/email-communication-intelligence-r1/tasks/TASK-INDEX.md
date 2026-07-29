# TASK-INDEX — email-communication-intelligence-r1 (Phase 1)

> **Generated**: 2026-07-28 (planning) · **Status**: task POMLs **not yet generated**
> **Total tasks**: 22 (19 work + 001 verify + 060 deploy + 061 UAT + 090 wrap)
> **Legend**: 🔲 not-started · 🔄 in-progress/needs-retry · ✅ complete · ⛔ blocked/gated

---

## Task Registry

| # | Title | Phase | Tags | FR | Deps | Parallel-group | Parallel-safe | Rigor | Model/Effort | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 001 | Verify operator schema inputs in `spaarkedev1` (`sprk_regardingreportcard`; `sprk_recordtype_ref` RPTC row + `sprk_reportcardnumber`; `sprk_emailupdatefield`) + data-hygiene check of `sprk_recordtype_ref` `sprk_regardingfield` typos | P0 | dataverse, verify, read-only | FR-11 (verify) | — | P0 | true | MINIMAL | sonnet/low | ✅ |
| 010 | Add `sprk_reportcard`→`sprk_regardingreportcard` to `RegardingFieldMap.cs` (+ send-time `RegardingLookupMap`) | P1 | bff-api, communication | FR-02 | 001 | P1 | true | FULL | sonnet/high | ✅ |
| 011 | Triage fields on `sprk_communication`: category, priority, summary, obligations (lean JSON), riconfidence, reviewoutcome | P1 | dataverse, schema | FR-07 | 001 | P1 | true | STANDARD | sonnet/medium | ✅ |
| 012 | `sprk_emailreviewlog` append-only audit entity | P1 | dataverse, schema | FR-08 | 001 | P1 | true | STANDARD | sonnet/medium | ✅ |
| 013 | Category taxonomy + priority-weight config seed | P1 | dataverse, config | FR-16 | 001 | P1 | true | STANDARD | sonnet/medium | 🔲 |
| 020 | 7-entity identifier rung — catalog-driven (`sprk_recordtype_ref`), value-based reverse lookup, reinforcement-gated, auto-file per C-1 | P2 | bff-api, communication | FR-01 | 001 | P2-assoc | **false** (shared `Engine/`) | FULL | opus/xhigh | ✅ |
| 021 | Auto-file policy narrowing C-1 in `AssociationStatusMapper`/`AutoFileGate` — rung 0+1 auto-file, 2/3 → `Suggested` | P2 | bff-api, communication | FR-03 | 020 | P2-assoc | **false** (shared `Engine/`) | FULL | sonnet/high | ✅ |
| 022 | `TRIAGE-EMAIL` Action authoring via `jps-action-create` — `{category, summary, obligations[], priority, reviewOutcome}` reusing `AiClassificationRung` signal, no 2nd full LLM pass | P2 | catalog, jps | FR-05 | 001 | P2-triage | true | STANDARD | sonnet/high | ✅ |
| 023 | `TRIAGE-EMAIL` Binding + input/output schema (mirror-first) + golden-utterance eval case + RAG grounding + enrichment/event trigger via `PublicContracts` facade | P2 | catalog, bff-api, jps | FR-05, FR-06, NFR-07 | 022 | P2-triage | **false** (shared enrichment) | FULL | sonnet/high | ✅ |
| 024 | RI-confidence scorer — compute (urgency × deterministic-rung agreement) + wire into `CommunicationAssessedSignal` (`RunAssessmentEmissionAsync`); lights up `CommunicationRiActionService` | P2 | bff-api, communication | FR-04 | 022 (reads 020) | P2-triage | **false** (shared enrichment) | FULL | sonnet/high | ✅ |
| 025 | Persist triage output to `sprk_communication` triage fields on enrichment path | P2 | bff-api, communication | FR-07 | 011, 022 | P2-triage | **false** (shared enrichment) | FULL | sonnet/high | ✅ |
| 030 | Job B propose — Action proposes allow-listed field updates (reads `sprk_emailupdatefield`), old→new, cited, confidence, stored as pending | P3 | bff-api, catalog, communication | FR-09 | 020, 022, 001 (`sprk_emailupdatefield`) | P3 | **false** (shared) | FULL | opus/high | ✅ |
| 031 | Job B apply endpoint → `IActionSeam.UpdateRecordAsync` under OBO + `sprk_emailreviewlog` audit row | P3 | bff-api, communication | FR-10 | 030, 012 | P3 | **false** (shared endpoints) | FULL | opus/high | ⛔ (ESCALATED — owner picks write-identity path; see `BLOCKED-031.md`) |
| 032 | Job B queue-feed endpoint — ranked exceptions feed for r5 | P3 | bff-api, communication | FR-17 | 030 | P3 | **false** (shared endpoints) | FULL | sonnet/high | ✅ |
| 040 | Job C email-triggered tasks/events via create-task pattern (`CREATE-TASK@v1`), cited | P4 | bff-api, catalog | FR-14 | 020, 022 | P4 | **false** (shared) | FULL | sonnet/high | 🔲 |
| 041 | Attachment-grounded action extraction — ground Action on extracted attachment text, gated to action-triggers | P4 | bff-api, catalog | FR-13 | 040 | P4 | **false** (shared) | FULL | opus/high | 🔲 |
| 042 | Regarding-vs-related intent — classify file/update/new-related; demote identifier on "new filing based on X"; propose create-record linked as related | P4 | bff-api, catalog, communication | FR-12 | 020, 022 | P4 | **false** (shared) | FULL | opus/xhigh | 🔲 |
| 050 | SPIKE: shared vs M365-group mailbox Graph subscription + Exchange `ApplicationAccessPolicy` model; `GraphSubscriptionManager` delta (FR-15 sizing) | P5 | investigation, spike | FR-15 | — | P5 | true | STANDARD | opus/high | ✅ (escalation FIRED) |
| 051a | Shared-mailbox capture coverage — verify `SharedAccount` subscription + exactly-once + operator runbook line (XS, no code) | P5 | bff-api, communication | FR-15 | 050 | P5 | **false** (capture path) | FULL | sonnet/high | ✅ (no code; verified + runbook) |
| 051b | M365-group-mailbox capture — forked pipeline (**BLOCKED: owner decision A-descope / B-build / C-defer; needs `Group.Read.All` + security sign-off**) | P5 | bff-api, communication | FR-15 | 050 | P5 | **false** (capture path) | FULL | sonnet/high | ⛔ |
| 060 | Deploy BFF + Dataverse to `spaarkedev1` | P6 | deploy | — | 010–051 (all impl) | P6 | **false** | STANDARD | sonnet/medium | 🔲 |
| 061 | Operator browser UAT — success criteria 1–9 | P6 | ui-test | (all) | 060 | P6 | true | MINIMAL | sonnet/low | 🔲 |
| 090 | Project wrap-up — status Complete, `/test-diet`, lessons-learned | Wrap | wrapup | — | 061 | P6 | **false** (main-session) | MINIMAL | sonnet/low | 🔲 |

> **Note**: 22 tasks total — 001 (verify) + 010–013 (Phase 1 ×4) + 020–025 (Phase 2 ×6) + 030–032 (Phase 3 ×3) + 040–042 (Phase 4 ×3) + 050–051 (Phase 5 ×2) + 060–061 (Phase 6 ×2) + 090 (wrap). **50 (spike) carries an escalation trigger; 051 is gated on its finding.**

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **P0** | 001 | — | Read-only prerequisite verification; **gates Phase 1** |
| **P1** | 010, 011, 012, 013 | 001 | Distinct surfaces (010 = 1 BFF file; 011/012/013 = schema/config) — parallel |
| **P2-assoc** | 020 → 021 | 001 | Shared `Engine/`; serial (021 narrows the rung's mapper) |
| **P2-triage** | 022 → 023 → {024, 025} | 001 (022); 011 (025) | 022 catalog (parallel-safe); 023/024/025 enrichment `.cs` serial. **Runs parallel to P2-assoc** (different files) |
| **P3** | 030 → {031, 032} | 020, 022, 001 (`sprk_emailupdatefield`) | Job B; endpoints shared → 031/032 serial after 030 |
| **P4** | 040 → 041; 042 | 020, 022 | 041 grounds on 040's create-task path; 042 parallel |
| **P5** | 050 → 051 | — | **Fully parallel to P1–P4** (independent capture track); 051 gated on 050 finding |
| **P6** | 060 → 061 → 090 | P1–P5 complete | Deploy → UAT → wrap (serial; 090 main-session) |

**Max concurrency**: 6 agents/wave. **BFF writers to shared `Services/Communication/` are `parallel-safe: false`** among each other — never concurrent; `/conflict-check` before each PR. P2-assoc and P2-triage touch **different file areas** (Engine/rung+mapper vs enrichment path) → the two tracks run in parallel; within each track, `.cs` edits serialize.

**Model tiers (per CLAUDE.md §8.5)**: default **sonnet @ high**. **opus** on 020, 030, 031, 041, 042, 050. **effort xhigh** on 020, 042. Schema/config **sonnet @ medium**; 001/061/090 low/MINIMAL.

---

## Critical Path

```
001 → 020 → 021                          (association + C-1 auto-file)
001 → 022 → 023 → 024 → 025              (triage spine + RI-confidence + persist)   [parallel to assoc]
{020,022} → 030 → 031 → 060 → 061 → 090  (Job B FULL — the deepest serial track)
{020,022} → 040 → 041 ; 042              (Job C + intent — parallel to Job B)
[parallel throughout] 050 → 051          (FR-15 capture — independent)
```

**Genuine serial spine**: `001 → 020 → 030 → 031 → 060 → 061 → 090` (Job B is the deepest track). The triage track (022→023→024→025), the association-mapper (021), Job C + intent (040/041/042), and the FR-15 capture track (050→051) all run in parallel after their prerequisites. **FR-15 (Phase 5) is fully independent of Phases 1–4.**

---

## High-Risk / Watch Items

- **020** — high blast radius on association correctness; bare-numeric never auto-files alone; multi-entity → `Ambiguous`; reads `sprk_recordtype_ref` defensively (typos); report per-message query count (NFR-08).
- **030/031** — record-mutating; human-confirm + cite + audit + allow-list + OBO (NFR-05/06; ADR-015); verify cited text exists.
- **041** — highest-difficulty AI; heaviest eval-case obligation (NFR-07).
- **050** — escalation trigger; 051 gated on the finding.
- Every BFF-touching task: `/conflict-check` before PR + publish-size (≤60 MB; baseline ~49.63 MB incl. PDBs) + CVE + tests obligation (root §10). NFR-04: triage/RI/proposal MUST NOT fail capture or send.
- **Out of r1 scope (coordination, not tasks)**: r5 surface work (Exceptions Queue UI, confirm cards). r1 supplies feed + apply endpoints only (C-3).
