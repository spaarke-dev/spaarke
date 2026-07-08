# ADR-040: Session Ledger

- **Status**: **Accepted** (2026-07-05, at gate G-P0 of `spaarke-ai-architecture-redesign-r1`, per this ADR's own promotion condition — "moves to Accepted when migration phase P0 ships"). Evidence: typed ledger model + Redis/Cosmos persistence deployed to `spaarke-bff-dev`; ledger round-trip incl. file references test-proven; boot reconciliation live. Full package: `projects/spaarke-ai-architecture-redesign-r1/notes/g-p0-evidence.md`. (Originally Proposed 2026-07-05, accepted-in-principle by operator with the v0.4 converged target.)
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
