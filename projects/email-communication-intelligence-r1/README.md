# Email Communication Intelligence — R1 (Phase 1)

> **Status**: 📝 **Planning** — plan authored; task POMLs not yet generated.
> **Created**: 2026-07-28 · **Branch**: `work/email-communication-intelligence-r1`
> **Portfolio**: Epic [#431 EMAIL & MESSAGING](https://github.com/spaarke-dev/spaarke/issues/431) · [Board #2](https://github.com/users/spaarke-dev/projects/2)
> **Follows / builds on (shipped, merged)**: `email-communication-solution-r4` (Association Engine + enrichment) · `spaarke-notification-spine-r1` (action/notification delivery) · `email-communication-solution-r5` (Outlook-style review surfaces — **complete; owns all UI**).
> **Source of truth**: [`spec.md`](spec.md) (17 FRs, 8 NFRs) + [`design.md`](design.md) **§0 (authoritative)**. §0 supersedes the v0.1/rev-2 charter (§1–§13) on all mechanism + scope claims.

---

## What R1 is

r1 is the **intelligence and record-currency layer** over Spaarke's shipped communication engine (r4). It **activates the already-produced-but-dark AI classification** (category / urgency / obligations), computes a real **RI-confidence** score (closing the hardcoded-0 gap that leaves the notification path inert), extends deterministic **email-to-record association** to all 7 core record types, and makes matched records **current from email** (human-confirmed, cited, audited field updates — **Job B**).

All work is **code-directed (Action + Binding, ADR-039)** on shipped infrastructure — the node-graph playbook engine is **frozen and not used**. Review/reading surfaces are **r5's (complete)**; r1 *feeds* them via a shared feed + apply contract. It builds **no UI**.

### Phase-1 scope (summary)

- **7-entity deterministic identifier rung** — catalog-driven (`sprk_recordtype_ref`), value-based, reinforcement-gated; auto-file per **C-1**.
- **Auto-file policy narrowing (C-1)** — auto-file only on rung 0 (explicit ID) + rung 1 (thread inheritance); rung 2/3 → `Suggested`.
- **RI-confidence scorer** — email-specific (triage urgency × deterministic-rung agreement); unblocks `CommunicationRiActionService` (Task + appnotification + ping).
- **`TRIAGE-EMAIL` Action + Binding** — categorize / summarize / extract-obligations / priority, RAG-grounded in the matter's correspondence; reuses the existing classification signal (no 2nd full LLM pass).
- **Triage fields** on `sprk_communication` + append-only **`sprk_emailreviewlog`** audit entity.
- **Job B (FULL)** — propose → confirm → apply (`IActionSeam.UpdateRecordAsync`, OBO) → audit; allow-listed fields (`sprk_emailupdatefield`); cited; apply + queue-feed endpoints for r5.
- **Job C** — email-triggered tasks/events via the shipped `CREATE-TASK@v1` pattern (cited).
- **Regarding-vs-related intent** + **attachment-grounded action extraction**.
- **Shared + M365 group mailbox** capture coverage (spike-first, parallel track).

### Out of scope

IP Auto-Docketing (removed entirely) · reading/review UI (r5 owns) · SprkChat-over-mail (P2) · Daily Briefing triage channel (P2) · the node-graph playbook engine (frozen) · any new `sprk_triageitem` entity (triage hangs off `sprk_communication`).

---

## Documents

| File | Purpose |
|---|---|
| [`spec.md`](spec.md) | AI-optimized spec (17 FRs, 8 NFRs) — implementation source of truth |
| [`design.md`](design.md) | Design charter; **§0 authoritative** (code-directed reconciliation) |
| [`plan.md`](plan.md) | Phase/wave WBS + discovered resources + placement justification + critical path |
| [`current-task.md`](current-task.md) | Active task state (context recovery) |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Full task registry + parallel groups + critical path |
| [`notes/email-intelligence-r1-coordination.md`](notes/email-intelligence-r1-coordination.md) | r5 feed/apply contract coordination |

---

## Graduation Criteria (spec Success Criteria — operator browser UAT on dev)

> Legend: ✅ built + verified · 🔲 not started

1. [🔲] An email quoting a project / invoice / work-assignment / budget / service-request / report-card number associates to the right record (cross-tenant numbering).
2. [🔲] A thread reply inherits the parent's association (rung 1); a sender-only match lands `Suggested`, not auto-filed (C-1).
3. [🔲] "New filing based on X" does NOT auto-file onto X; it offers create-new / file-onto / link-related.
4. [🔲] Opening an email shows category, 2-line summary, obligations, priority (r5 surface).
5. [🔲] A high-signal email produces a Task + appnotification + real-time ping (RI-confidence fix).
6. [🔲] A fact-bearing email proposes an allow-listed record update, cited; on confirm the record updates and an audit row is written (r5 surface + record + `sprk_emailreviewlog`).
7. [🔲] An action stated only in an attachment is extracted and cited to the attachment.
8. [🔲] A shared/group-mailbox email is captured, associated, and triaged.
9. [🔲] Every AI proposal + human decision is queryable per matter (`sprk_emailreviewlog`).

---

## External prerequisites (operator-created — verified, not built, in Phase 0 task 001)

- `sprk_emailupdatefield` table (Job B allow-list; FR-11 schema).
- `sprk_regardingreportcard` lookup on `sprk_communication`.
- `sprk_recordtype_ref` **RPTC** row + `sprk_reportcardnumber` number field populated.
- `sprk_recordtype_ref` data-hygiene: known typos in some `sprk_regardingfield` values + a `contact`-row anomaly (read defensively or clean first).

---

## Execution

All task work MUST use the `task-execute` skill (root CLAUDE.md §4). Task POMLs are generated **after plan approval**. To begin once generated:

```
work on task 001
```

or `continue` to pick up the first 🔲 in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

**Coordination**: run `/conflict-check` at start and **before every BFF PR** — r1 edits shared `Services/Communication/` (identifier rung, enrichment, endpoints) and consumes `Services/Ai/PublicContracts/` seams (ADR-041/043/047 in-flight; pin to current shape).
