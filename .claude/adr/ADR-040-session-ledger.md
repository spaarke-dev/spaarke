# ADR-040: Session Ledger (Concise)

> **Status**: Proposed (2026-07-05) — accepted-in-principle by operator with the
> v0.4 converged target; moves to Accepted when migration P0 ships.
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
- **MUST** cap inline payload size (blob/SPE pointer beyond the cap).
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

## Integration
ADR-009/014 (tiers + caching rules) · ADR-015 (governance tiers — binding
mapping above) · ADR-030 (PaneEventBus carries ledger-keyed events; widget
user-actions append `WidgetEvent` entries) · ADR-039 (tool chains + gates are
ledger entries) · ADR-028 (restore contract).

**Full ADR**: [docs/adr/ADR-040-session-ledger.md](../../docs/adr/ADR-040-session-ledger.md)
