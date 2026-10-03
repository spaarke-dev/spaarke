# ADR-040: Session Ledger (Concise)

> **Amended 2026-10-03** — a durable, matter-scoped decision ledger (`sprk_decisionrecord`) is a
> **sibling** of `SessionGate`, not a competitor; the link is `sprk_decisionrecord.sprk_gatesessionid`.
> See [Amendment 2026-10-03](#amendment-2026-10-03--a-durable-decision-ledger-is-a-sibling-not-a-competitor).
> **Status**: **Accepted** (2026-07-05 at gate G-P0 of
> `spaarke-ai-architecture-redesign-r1` — P0 shipped; evidence:
> `projects/spaarke-ai-architecture-redesign-r1/notes/g-p0-evidence.md`).
> **Domain**: AI platform — session state, composition, audit
> **Source**: `spaarke-ai-code-audit-r1` (ADR review A-5); encodes ratified
> decisions D2/D8 (canonical AI architecture doc v0.4 §4.3/§5.2).
> **Why this ADR exists**: the audit found capability outputs were streamed
> and forgotten — no addressable store existed, so cross-capability
> composition (the platform's primary bet, §3.0) had no carrier. No ADR
> governed session semantics; this closes that vacuum at principle level.

---

## Decision

Every AI session has an **append-only, addressable, typed ledger** — the ONLY
carrier of cross-capability context. Entry types: `Doc | Output | ToolChain |
Turn | WidgetEvent | Gate`. Persistence rides the existing 3-tier store
(Redis hot → Cosmos warm → Dataverse cold); the ledger changes WHAT persists,
not where.

## Constraints

### ✅ MUST
- **MUST** write every capability output and every text-turn tool chain to the
  ledger BEFORE any rendering (storage precedes rendering — universal,
  automatic, not a capability choice).
- **MUST** make outputs addressable (`{bindingId}@t{n}`) and record
  `bindingId`, `uc_id`, `disposition`, `source_refs` on every Output entry.
- **MUST** resolve capability inputs from the ledger by reference
  (`ledger_resolution` in the Action's input schema) — no capability reads
  surface/screen state.
- **MUST** treat `disposition` (informational | work_product | overlay | email
  | record | notification) as the ONLY rendering contract.
- **MUST** enforce the inline payload cap at the write seam (128 KB —
  `SessionLedger.CapInlinePayload`, task 055 2026-07-08; over-cap payloads
  store a deterministic truncation marker `{"$truncated":true,...}`;
  disposition legs fail loud on truncated payloads — never partial delivery;
  blob/SPE pointer offload is the upgrade path).
- **MUST** map entry classes to ADR-015 tiers: ledger = Tier 3 (user-owned,
  GDPR-erasable, tenant-partitioned); ToolChain entries carry
  identifiers/filters/counts only (Tier-2-compatible — never verbatim content).
- **MUST** preserve document references across warm-store restore (a restored
  session that lost its file manifest violates walkthrough P2).
- **MUST** maintain a compacted session digest (rolling summary covering turns
  AND outputs) for in-turn context; beyond-window recall is a tool call, not a
  larger prompt.
- **MUST** persist work-product outputs to the host Dataverse record when the
  Binding declares record persistence (the widgets-r1 pattern).

### ❌ MUST NOT
- **MUST NOT** couple storage to rendering (an informational-disposition
  output is still stored and addressable).
- **MUST NOT** persist streaming tokens (ADR-014) — the ledger stores final
  validated outputs.
- **MUST NOT** create a second session-state store or per-surface session
  caches — surfaces read ledger projections via the session API.
- **MUST NOT** mutate or delete ledger entries within a session (append-only;
  corrections are new entries referencing the superseded key).

---

## Amendment 2026-10-03 — a durable decision ledger is a SIBLING, not a competitor

> Path **B** (ADR amendment) per root CLAUDE.md §6.5, chosen by the owner as decision **D-9** of
> `spaarke-ontology-platform-r1` (2026-10-03). Surfaced in that project's `design.md` §6 and
> `spec.md` §6; the owner had deferred the path on 2026-09-30 pending the Decision Record's shape
> being settled (D-2). D-2 is settled and the table is built, so the precondition is met.

**The gap this closes.** ADR-040's ledger is **session-scoped** — it answers *"what happened in this
chat."* `SessionGate` carries `GateId`/`Kind`/`Status`/`Turn`/`BindingId`/`SideEffectClass`/
`MissingFields`/`OutputKey` and **no authority, no policy version, no evidence, no confirmer identity
and no record scope**. It therefore structurally cannot answer the **object-scoped** question
*"everything ever decided about Matter 4471, and why."* Nothing in ADR-040 was wrong; it simply does
not speak to durable decision authority.

`sprk_decisionrecord` (Dataverse, append-only, built 2026-10) is that object-scoped answer. The two are
**siblings over one decision**:

| | `SessionGate` (ADR-040) | `sprk_decisionrecord` |
|---|---|---|
| Scope | one chat session | one matter, forever |
| Lifetime | Redis 24 h → Cosmos warm → Dataverse cold | durable row, no TTL |
| Answers | *was this turn's side effect approved?* | *who decided what, under which policy version, on what evidence?* |
| Queryable per matter | no | **yes** — this is the whole point |
| Owns | the gate **state machine** (`pending → confirmed \| rejected \| expired \| superseded`) | decision **authority** (`sprk_authority`, `sprk_policyversion`, `sprk_factsnapshot`, `sprk_confirmedby`, `sprk_recordclass`) |

**The link is named**: `sprk_decisionrecord.sprk_gatesessionid` carries the originating
`SessionGate.GateId`. It is **nullable by design** — most Decision Records come from the worklist, not
from chat, and a decision with no gate is not an error.

### ✅ MUST (added by this amendment)
- **MUST** write decision **authority** (who decided, under which policy version, on what evidence) to
  the durable Decision Record — never to `SessionGate`, which has no field for any of it.
- **MUST** set `sprk_gatesessionid` to the originating `SessionGate.GateId` when a decision **did**
  begin at a chat gate, so the two ledgers remain joinable after the hot tier expires.

### ❌ MUST NOT (added by this amendment)
- **MUST NOT** read the Decision Record as session context. It is not a ledger projection, the
  `ledger_resolution` seam does not reach it, and a capability needing decision history queries
  Dataverse.
- **MUST NOT** read the "no second session-state store" rule above as forbidding this. That rule
  governs **session** state; `sprk_decisionrecord` is **matter** state with a different key, lifetime
  and consumer.

### What this amendment does NOT do
- It does **not** make `SessionGate` durable. The 3-tier TTL model is unchanged.
- It does **not** move authority, policy version, evidence or confirmer **into** `SessionGate`. No
  field is added to any ledger entry type.
- It does **not** change storage-precedes-rendering, addressability, `disposition`, the 128 KB inline
  cap, or the ADR-015 tier mapping.
- It does **not** generalize ADR-040 into a general ledger ADR. It names **one** sibling and **one**
  link.

**Precedent, not invention**: `sprk_emailreviewlog` has shipped as a durable, append-only,
per-decision authority record since the email proposal-apply path landed. A durable sibling already
exists in production; this amendment names the pattern rather than creating it.

## Integration
ADR-009/014 (tiers + caching rules) · ADR-015 (governance tiers — binding
mapping above) · ADR-030 (PaneEventBus carries ledger-keyed events; widget
user-actions append `WidgetEvent` entries) · ADR-039 (tool chains + gates are
ledger entries) · ADR-028 (restore contract).

**Compose redline derived-views** ([docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md](../../docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md)) — render-follows-store applied to redlines: the ledger ships the opaque compose payload; the visual diff, `confidence_band`, and character offsets are **client-derived projections** recomputed on every materialize, never stored in the ledger.

**Full ADR**: [docs/adr/ADR-040-session-ledger.md](../../docs/adr/ADR-040-session-ledger.md)
