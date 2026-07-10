# Seam-Publication Ordering + Cross-Project Obligation — Task 017

> **Task**: AIR2-017 (Phase A0, gate G-R2-A) · **Spec**: FR-A0-08 · **Date**: 2026-07-10
> **Role**: verification + filing only — this task does NOT author contracts (010–016 do). It runs
> after all seven A0 contracts land, and closes the FR-A0-08 seam-publication-ordering obligation.

---

## 1. The §3.1 / FR-A0-08 seam set — verification result

Per spec FR-A0-08 (+ POML acceptance criterion 1, which adds `JobAwareCompletionState` to the
spec's five-item list): the seam set Compose r2 depends on for its **A0-blocked tasks** is:

| # | Seam | Publishing task(s) | Contract file (`Services/Ai/PublicContracts/`) | Contract test | Versioned / tolerant-reader | Status |
|---|---|---|---|---|---|---|
| 1 | `ComposeDisposition v1` (+ SSE frame, provenance, supersession) | 010 | `ComposeDisposition.cs` | `tests/integration/contract/Api/Ai/ComposeDispositionContractTests.cs` (7/7) | ✅ `Version = "1.0"`, `JsonExtensionData UnknownMembers`, additive-only doc'd | ✅ **published + consumable** |
| 2 | `OutcomeCard v1` slice | 011 | `OutcomeCard.cs` | `tests/integration/contract/Api/Ai/OutcomeCardContractTests.cs` (10/10) | ✅ `SchemaVersion` const + tolerant-reader deserialize (unknown fields ignored) | ✅ **published + consumable** |
| 3 | `ContextEnvelope v1` (workspace slice) | 015 | `ContextEnvelope.cs` | `tests/integration/contract/Api/Ai/ContextEnvelopeContractTests.cs` (12/12) | ✅ `SchemaVersionValue = "context-envelope/v1"`, additive-only unknown-slice tolerance | ✅ **published + consumable** |
| 4 | Ledger provenance `{bindingId}@t{n}` | 010 (carries it as `LedgerRef`) | `SessionLedgerEntries.cs:39` (`SessionLedger.BuildOutputKey`) + consumed by `ComposeDisposition.BuildFrame`/`Materialize`/`ResolveCurrent` | Covered by `ComposeDispositionContractTests.cs` + `ChatSessionLedgerRoundTripTests.cs` | ✅ stable key format, store-before-render enforced (throws if ledger entry absent) | ✅ **published + consumable** |
| 5 | `GateDecision v2` / Policy v2 tier table | 012 (contract) + 032 (engine) | `GateDecisionV2.cs` | `tests/integration/contract/Api/Ai/GateDecisionV2ContractTests.cs` (34/34 contract; 138/138 gate suite for the 032 engine) | ✅ `CurrentSchemaVersion = 2`, tolerant-reader accepts any payload whose version is compatible | ✅ **published + consumable** (contract AND producer engine — `ConfirmationPolicyEngine`, 7 tiers + E-1..E-6) |
| 6 | `JobAwareCompletionState v1` (consumer-declared ordered steps) | 014 | `JobAwareCompletionState.cs` | `tests/integration/contract/Api/Ai/JobAwareCompletionStateContractTests.cs` (22/22) | ✅ `CurrentVersion = 1`, tolerant-reader tolerates unknown future members | ✅ **published + consumable** |

**Result: ALL SIX FR-A0-08 seams are published under `Services/Ai/PublicContracts/`, each has a
passing contract test, and each is explicitly versioned + tolerant-reader.** No seam is reachable
via any other path (verified by grep — each type's only production definition is under
`Services/Ai/PublicContracts/`; no duplicate/forked shape exists elsewhere in `src/`).

### Bonus (not in the strict FR-A0-08 five/six-item list, but part of "all seven A0 contracts" per this task's `<dependencies>` — also verified green)

| Seam | Task(s) | Status |
|---|---|---|
| `MemoryItem v1` (structured memory object + governance envelope) | 016 | ✅ contract green (10/10) — `MemoryItem.cs` + `MemoryItemContractTests.cs` |
| `TraceEvent v1` + D-F4 host-embeddable view | 013 (contract) + 038 (view/read-surface) | ✅ contract green (7/7) + view published (`ISessionTraceReader` facade, `GET /sessions/{id}/trace`, host-embeddable `ExecutionTraceWidget`) |
| Triple-twin description hoist | 020 | ✅ published (Model 1 GitOps + Option C parity test) |
| D-F3 UI-ack (ack tokens over `correlationId`) | 037 | ✅ published (`IUiActionAckCoordinator` facade, 12/12 tests) |

**Conclusion for step 0–1**: the FR-A0-08 seam-publication-ordering obligation ("Compose r2 can
consume every seam before core implements its own dependent features") is **satisfied**. All seven
A0 contracts (010–016) are merged, contract-tested, and consumed only through the
`Services/Ai/PublicContracts/` facade (ADR-013 compliant).

---

## 2. One remaining, HONESTLY-FLAGGED gap outside this task's scope: `memory.write` (task 057)

This task's `<escalation><trigger>` and `<dependencies>` scope the blocking condition to **tasks
010–016** (the seven A0 contract SHAPES) — all done. **Task 057 (`memory.write` — AI-initiated,
provenance-tagged write mechanism) is NOT one of 010–016**; it is a separate, later Phase-M/G-R2-B
deliverable (dependency: 050, which is done; 057 itself is still 🔲 not-started per
`tasks/TASK-INDEX.md`).

However, three cross-project artifacts materially disagree about whether 057 gates task 017's
"Compose UNBLOCKED" declaration, and — critically — **compose-r2's own `CLAUDE.md` (§ "Core Phase
A0 dependency") independently lists `memory.write` (057) as a live STILL-BLOCKED item** for its own
task 063 (FR-30, persist AI-derived insights): *"FR-30 memory.write (063) — MemoryItem shape
present, but the `memory.write` tool impl (core task 057) 🔲 pending."* That is Compose's own
tracking, not drift in this project's docs — it is a genuine, currently-active cross-project
dependency.

**Resolution (Path C — pivot to the accurate, narrower claim, per CLAUDE.md §6.5)**: this note
draws a hard line between the two obligations rather than conflating them:

1. **FR-A0-08 seam-publication-ordering obligation (this task's actual scope)** — ✅ **CLOSED**. All
   six named seams (+ the bonus MemoryItem/TraceEvent contract shapes) are published and
   consumable. Compose r2 is unblocked for every task gated on a *contract shape*.
2. **Full "ALL SEAMS PUBLISHED" milestone on `notes/SEAM-STATUS.md`** — **NOT flipped**. That
   dashboard tracks one additional row (`MemoryItem v1 + memory.write`) whose second half
   (`memory.write`, task 057) is a real, currently-blocking dependency for Compose r2 task 063. Per
   this task's NEGATIVE acceptance criterion ("if any seam is missing... the SEAM-STATUS header is
   NOT flipped, and the UNBLOCKED notice is NOT posted — rather than declaring completion"), the
   header is **not** flipped to "ALL SEAMS PUBLISHED" while task 057 is outstanding.

This is a narrower and more honest claim than either (a) flipping the full header now (would
overclaim that Compose's memory-write dependency is ready, when Compose's own docs say it isn't),
or (b) blocking this entire task on 057 (which contradicts the escalation trigger's explicit
010–016 scope and would delay filing an obligation that IS genuinely ready). See
`notes/SEAM-STATUS.md` for the corrected header text.

**Also note (informational, not a gap)**: compose-r2's `CLAUDE.md` snapshot in this worktree is
stale on one point — it lists `TraceEvent`/D-F4 (task 038) as still-pending, but 038 is ✅ done per
this project's `tasks/TASK-INDEX.md` (2026-07-09). The reciprocal filing in
`projects/spaarkeai-compose-r2/notes/` (§3 below) surfaces this correction so Compose r2's own
tracking can be refreshed.

---

## 3. Core-owes obligation (filed here + pointed to from `projects/INDEX.md`)

**What the core (spaarke-ai-architecture-redesign-r2) owes `spaarkeai-compose-r2`**, per FR-A0-08 +
the negotiated deltas in `notes/HANDOFF-response-to-compose-r2.md`:

1. **The seven A0 contract shapes**, frozen, additive-only, consumed ONLY via
   `Services/Ai/PublicContracts/` — **DELIVERED** (verified §1 above).
2. **No-forked-seam rule (FR-D-03)**: Compose r2 MUST consume these seams as published — never fork
   a local variant of an AI-internal seam. The one negotiated Path-A exception is
   `AnchoredAnnotation` (document-positional UI state, explicitly NOT a `MemoryItem`, per the
   HANDOFF response §"Item changed by owner ruling"). **Task 072 (Cross-satellite seam-fork
   verification, gate G-R2-D)** is the enforcement mechanism — it will fail if Compose forks any of
   these six-plus-two contract shapes.
3. **Ack-plumbing (D-F3 / task 037)** — delivered: `IUiActionAckCoordinator` facade, honest 8s
   timeout, 12/12 tests.
4. **Tier-2c / association-picker duty (GateDecision v2 + task 032 engine)** — delivered: the one
   gate dialog hosts an optional parent-association picker (matter/project/invoice/work-assignment/
   none), producer-side unblocked.
5. **OutcomeCard duties** — delivered: `CompletionEngine` composes an `OutcomeCard` on every
   side-effect disposition path (task 035); job-aware completion (task 036) guarantees a doc-create
   card can't render "done" while indexing is still queued (NFR-12).
6. **Outstanding**: `memory.write` (task 057) — tracked separately, NOT part of the FR-A0-08
   ordering obligation, but flagged here for transparency because Compose's task 063 needs it.

**`projects/INDEX.md` pointer**: added to this project's row (see diff in that file) — states the
core-owes obligation is filed here and points back to this note + `notes/SEAM-STATUS.md`.

---

## 4. Compose-consumes obligation (reciprocal filing)

Filed into `projects/spaarkeai-compose-r2/notes/` — see
`HANDOFF-to-compose-r2-task-017-seam-ordering-closed.md` in that project's notes folder. It:

- Confirms Compose r2 may now treat all six FR-A0-08 seams (+ MemoryItem/TraceEvent contract
  shapes) as frozen and consumable — no local variant permitted (no-fork rule, FR-D-03).
- Corrects the stale TraceEvent/D-F4 status in compose-r2's `CLAUDE.md` (038 is done).
- Reaffirms the ONE outstanding item: `memory.write` (057) — Compose's task 063 remains gated on it;
  the "ALL SEAMS PUBLISHED" milestone will flip when 057 lands, tracked at
  `notes/SEAM-STATUS.md`.
- Reaffirms `/conflict-check` before every BFF PR (§5 below).

---

## 5. Conflict-check + no-fork reaffirmation

- **`/conflict-check` before every BFF PR**: both `spaarke-ai-architecture-redesign-r2` (core; sole
  owner of `Services/Ai/` internals) and `spaarkeai-compose-r2` (satellite; consumer) are listed in
  `projects/INDEX.md`'s BFF hot-path overlap section. Any PR touching `Services/Ai/**` from either
  project MUST run `/conflict-check` first per `projects/INDEX.md` "Coordination action" and root
  CLAUDE.md §10.
- **No-forked-seam rule (FR-D-03)**: reaffirmed. Task **072** (Cross-satellite seam-fork
  verification, gate G-R2-D, this project) is the binding enforcement point — it checks that Compose
  r2 never reimplements `ComposeDisposition`/`OutcomeCard`/`GateDecisionV2`/`JobAwareCompletionState`/
  `ContextEnvelope`/`MemoryItem`/`TraceEvent` locally instead of consuming
  `Services/Ai/PublicContracts/` directly.

---

## 6. Acceptance-criteria disposition

| Criterion | Status |
|---|---|
| All six FR-A0-08 seams published + contract-tested under `Services/Ai/PublicContracts/` | ✅ MET (§1) |
| Core-owes obligation filed in this project's notes + pointed to from `projects/INDEX.md` | ✅ MET (§3, `projects/INDEX.md` updated) |
| Reciprocal Compose-consumes obligation filed into `projects/spaarkeai-compose-r2/` | ✅ MET (§4) |
| No-forked-seam rule reaffirmed + `/conflict-check` restated | ✅ MET (§5) |
| SEAM-STATUS milestone: every row verified, header flip, UNBLOCKED notice posted | ⚠️ **PARTIAL BY DESIGN** — the FR-A0-08 six-seam obligation (this task's actual scope) is fully green and is declared closed; the broader dashboard header is deliberately **NOT** flipped to "ALL SEAMS PUBLISHED" because one dashboard row (`memory.write`, task 057 — outside 010–016) remains outstanding and is a live Compose r2 (task 063) dependency. See §2 for the reasoning. This is the NEGATIVE-criterion path, applied narrowly to the one incomplete row rather than to the whole task. |
| NEGATIVE: missing/non-consumable seam recorded as BLOCKER, header not flipped, notice not posted | ✅ **APPLIED** for the `memory.write` row specifically (§2) |

**No STOP-level escalation is raised** for this task's actual acceptance criteria (1–4), since the
escalation trigger explicitly scopes blocking conditions to tasks 010–016, all of which are done.
The `memory.write` gap is surfaced transparently (§2) rather than silently glossed over, consistent
with CLAUDE.md §6.5 — flagged for operator visibility, not treated as a task-017 failure.
