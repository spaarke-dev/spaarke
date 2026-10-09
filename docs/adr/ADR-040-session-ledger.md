# ADR-040: Session Ledger

- **Status**: **Accepted** (2026-07-05, at gate G-P0 of `spaarke-ai-architecture-redesign-r1`, per this ADR's own promotion condition — "moves to Accepted when migration phase P0 ships"). Evidence: typed ledger model + Redis/Cosmos persistence deployed to `spaarke-bff-dev`; ledger round-trip incl. file references test-proven; boot reconciliation live. Full package: `projects/spaarke-ai-architecture-redesign-r1/notes/g-p0-evidence.md`. (Originally Proposed 2026-07-05, accepted-in-principle by operator with the v0.4 converged target.)
- **Amended**: 2026-07-08 (inline size-cap enforcement) · **2026-10-03** (a durable, matter-scoped decision ledger — `sprk_decisionrecord` — is a **sibling** of `SessionGate`, linked by `sprk_decisionrecord.sprk_gatesessionid`; `spaarke-ontology-platform-r1` D-9, path B per root `CLAUDE.md` §6.5)
- **Deciders**: Operator + `spaarke-ai-code-audit-r1` convergence review (2026-07-05)
- **Concise version**: [`.claude/adr/ADR-040-session-ledger.md`](../../.claude/adr/ADR-040-session-ledger.md) (the operational MUST/MUST-NOT surface — binding)

## Context

Spaarke AI's differentiating product bet is **composition**: capabilities chaining in one flowing session (upload → summarize → chat → create matter → draft letter — canonical doc §3.0, walkthrough §3.10). The 2026-07-05 audit found the composition carrier simply did not exist: capability outputs were streamed to the UI and forgotten. A later capability could not reference an earlier one's result (walkthrough proposition P4 had no server-side mechanism); the session store carried conversation + files + widget tabs, but no addressable outputs and no tool-chain audit. As with dispatch (ADR-039), no ADR governed session semantics — a governance vacuum in exactly the load-bearing spot.

What DOES exist and works (audit-verified): a 3-tier persistence stack (`ChatSessionManager`: Redis hot 24h-sliding → Cosmos warm write-through → Dataverse cold), history compaction (summarize@15/archive@50), cleanup signals, restore. The ledger rides this stack unchanged — it widens WHAT persists, not WHERE.

## Decision

Every AI session has an **append-only, addressable, typed ledger** — the only carrier of cross-capability context. Entry types:

| Entry | Carries | Fulfilled by today |
|---|---|---|
| `Doc` | uploaded/mounted documents + extracted text + enrichment | `ChatSessionFile` (keep) |
| `Turn` | conversation turns + the compacted session digest | `ChatHistoryManager` (generalize compaction to cover outputs) |
| `Output` | every capability output: `{bindingId}@t{n}` key, `uc_id`, schema-validated payload, `disposition`, `source_refs`, optional `widget_id` | **new** — the P4 carrier |
| `ToolChain` | text-turn tool-call chains (identifiers/filters/counts + citations; never verbatim content) | **new** — replayable audit |
| `WidgetEvent` | widget user-actions (selection, highlight, edit) as consumable session events | extends tab persistence + PaneEventBus emissions |
| `Gate` | pending confirmations + in-flight elicitation markers | `PendingPlanManager` store, generalized (D12) |

Core rules (full binding surface in the concise version): **storage precedes rendering** — universal ledger write before any surface sees the output (D2/D8); **reads are by reference** — Action input schemas declare `ledger_resolution`s, no capability reads screen state (P10); **disposition is the only rendering contract**; append-only within a session; inline payloads size-capped with blob pointers; entry classes mapped to ADR-015 tiers (ledger = Tier 3 user-owned/GDPR-erasable; ToolChain = Tier-2-compatible metadata); document references survive warm-store restore (fixes the audited Cosmos mapping that dropped the file manifest); work-product outputs additionally persist to the host Dataverse record when the Binding declares it (the shipped widgets-r1 pattern).

### Inline size-cap enforcement (amended 2026-07-08, task 055 per operator ruling 2026-07-07)

The size-cap rule is **ENFORCED at the ledger write seam**, not merely observed. Cap: **128 KB (`SessionLedger.InlinePayloadCapBytes`, UTF-8 bytes of the payload's raw JSON text; inclusive — enforcement fires strictly above the cap)**. Task 021 originally shipped a warn-only threshold and deferred the blob/SPE-pointer offload (building an unprescribed storage path would have been scope creep); the task-047 escalation was ruled by the operator on 2026-07-07: enforce inline at P4, keep the pointer offload as the designed upgrade path.

Enforced behavior (`SessionLedger.CapInlinePayload`, applied by BOTH Output writers — `OutputRouter` and the gate-resume writer in `TypedHandlerResumeExecutor`):

- Over-cap payloads are deterministically replaced BEFORE the ledger write by a truncation marker: `{ "$truncated": true, "original_bytes": n, "cap_bytes": 131072, "preview": "<first 16K chars of raw text>" }`. The entry is still written, still addressable — truncation never drops the storage-precedes-rendering write.
- A Warning is logged with sizes and identifiers only (NFR-07); the preview lives in the ledger (Tier 3), never in logs.
- Structured dispositions (`email`, `work_product`) fail LOUDLY on a truncated payload — the envelope is gone from the stored marker, so delivery/persistence throws rather than delivering from pre-store state. Capabilities MUST keep routed payloads under the cap.
- Readers distinguish markers via `SessionLedger.IsTruncationMarker`; a `ledger_resolution` that resolves to a marker must fail loudly rather than feed the lossy preview to a capability.
- When the blob/SPE-pointer offload lands, the marker becomes a pointer and the content stops being lossy; the enforcement seam and cap constant are unchanged by that upgrade.

Memory model: in-turn context = digest + last-N turns + referenced entries; beyond-window recall is a tool call (`session.recall`, `memory.*` over existing pins) — memory scales by retrieval, not by prompt growth.

### Sibling durable decision ledger (amended 2026-10-03, `spaarke-ontology-platform-r1` D-9, path B)

**What prompted the amendment.** `spaarke-ontology-platform-r1` needed a record answering *"who decided what about this matter, under which policy version, on what evidence — and why did nothing happen?"* The Decision Record it designed (`sprk_decisionrecord`, built 2026-10) visibly overlapped ADR-040's `Gate` entry, and root CLAUDE.md §6.5 forbids proceeding past that kind of overlap silently. The owner deferred the path on 2026-09-30 with an explicit condition — *"evaluate/revise this ADR once we get the solution actually decided"* — and chose **path B (amendment)** on 2026-10-03 once the Decision Record's shape (D-2) was settled and the table existed.

**The actual relationship.** This ledger is **session-scoped**: it answers *what happened in this chat*. `SessionGate` carries `GateId`, `Kind`, `Status`, `Turn`, `BindingId`, `SideEffectClass`, `MissingFields` and `OutputKey` — and **no authority, no policy version, no evidence snapshot, no confirmer identity and no record scope**. That is not an oversight in ADR-040; a gate marker exists to run a turn's approval state machine, and it does that correctly. But it means the session ledger **structurally cannot** answer an object-scoped question, and no amount of querying it will produce *"everything ever decided about Matter 4471."* Its hot tier has also expired by the time anyone asks.

So the two are **siblings over one decision**, each owning a different half: `SessionGate` owns the **gate state machine** (`pending → confirmed | rejected | expired | superseded`, keyed by session and turn, TTL-bounded); `sprk_decisionrecord` owns **decision authority** (`sprk_authority`, `sprk_policyversion`, `sprk_factsnapshot`, `sprk_confirmedby`, `sprk_recordclass`, keyed by matter, durable). Neither owns the other's half, and neither is a projection of the other. The join is `sprk_decisionrecord.sprk_gatesessionid` → `SessionGate.GateId`, **nullable by design**: most Decision Records originate in the worklist rather than in chat, and a decision with no gate is a normal decision, not a broken one.

The operational MUST / MUST NOT surface is in the [concise version](../../.claude/adr/ADR-040-session-ledger.md#amendment-2026-10-03--a-durable-decision-ledger-is-a-sibling-not-a-competitor) and is binding. In summary: authority fields go to the durable record and never to `SessionGate`; `sprk_gatesessionid` is set when a gate did originate the decision; the Decision Record is never read as session context (the `ledger_resolution` seam does not reach it); and the existing "no second session-state store" MUST NOT does not reach a matter-scoped durable record, which has a different key, lifetime and consumer.

**Deliberately out of scope.** This amendment does not make `SessionGate` durable, does not add any field to any ledger entry type, does not touch storage-precedes-rendering / addressability / `disposition` / the 128 KB inline cap / the ADR-015 tier mapping, and does not generalize ADR-040 into a ledger ADR. It names **one** sibling and **one** link. A second durable-ledger consumer should extend this section rather than re-argue the principle.

**Alternatives considered for this amendment.** (A) A project-scoped exception under §6.5 — rejected because a second durable-ledger consumer is foreseeable and an exception would have to be re-argued each time it appeared. (C) Comply as written — rejected because complying means writing decision authority into a chat-session ledger that has no fields for it and expires, which is a worse system than the documented deviation. Broadening ADR-040 into a general ledger ADR was also rejected: the project needs one named sibling, and a wide ADR change made to unblock one project is the failure mode §6.5 exists to prevent.

**Precedent, not invention.** `sprk_emailreviewlog` has shipped as a durable, append-only, per-decision authority record since the email proposal-apply path landed — a durable sibling already exists in production, written by code that re-validates at apply time and resolves the caller server-side. This amendment names an established pattern rather than introducing one, which is also why its blast radius is documentation rather than migration: **no existing consumer depends on `SessionGate` being the only record of a decision**, because it already is not.

## Alternatives considered

- **Per-capability output plumbing** (each pair of capabilities wires its own handoff): rejected — this is the status quo's implicit design and it produced pairwise, inconsistent, mostly-absent composition. The ledger makes composition free once, for every pair.
- **A new dedicated store** (separate Cosmos container/service for outputs): rejected — the 3-tier session stack is audited-working; a second store adds consistency and lifecycle problems the existing TTL/cleanup/restore machinery already solves.
- **Screen-state as context** (capabilities read what's rendered): rejected — violates P10, breaks on restore, and couples capabilities to surfaces.

## Consequences

- Positive: composition (chips pre-filling from prior outputs, task records citing source analyses, email drafts referencing summaries) becomes reference-passing; the text path gains a replayable audit trail; `ExecutionTraceWidget` gets its missing data source; session restore recovers full working context.
- Negative / accepted: session payloads grow (mitigated by size caps + pointers + compaction); the `ChatSession` model change is a P0 migration prerequisite for nearly everything else.
- Enforcement: code review flags any capability that renders before writing, resolves inputs from anything but the ledger/args, or introduces a parallel session cache.

## References

- Canonical target: `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` v0.4 §4.3, §5.2
- Session-state schema origin: canonical doc §3.10.5 + mechanisms M1/M7
- Greenfield rationale (Q6/Q7 memory + multi-surface answers): `projects/spaarke-ai-code-audit-r1/GREENFIELD-CONCEPTUAL-DESIGN.md` §9
- Audit evidence (missing outputs store; Cosmos file-manifest drop): `projects/spaarke-ai-code-audit-r1/SPAARKE-AI-CODE-INVENTORY.md` §1, §10
