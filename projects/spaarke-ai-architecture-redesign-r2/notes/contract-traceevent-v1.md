# TraceEvent v1 — contract note (task 013)

> **Seam**: `TraceEvent v1` + D-F4 view (host-embeddable). **Publishing task**: 013 (this) + 038.
> **Unblocks**: Compose r2 **FR-32** (Context-pane trace hosting).
> **Spec**: FR-A0-05. **Design**: D-F4. **Constraints**: ADR-040 (read projection, no parallel store), NFR-07 (no-content), ADR-013 (PublicContracts facade), ADR-038 (KEEP-path contract test).

## What this is

A versioned, tolerant-reader **READ projection** over the ADR-040 session ledger. It NAMES the
existing ledger markers as a stable, ordered decision-trace stream — request → context slices
used → tools selected → gate/approval path → outcome — so the FR-A1-09 traceability view
(task 038) and live plan narration bind to THIS shape rather than reaching into ledger internals.

There is **no parallel trace store** (ADR-040 MUST NOT). A stream is materialized on demand by
`TraceEventProjection.Project(...)` from markers a caller already loaded via the session API.

## Files

- Contract + producer + consumer + guard: `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/TraceEvent.cs`
- Contract test (self-contained, no DI): `tests/integration/contract/Api/Ai/TraceEventContractTests.cs`

## Marker → TraceEvent mapping (no duplicate event types invented)

| Ledger marker (`Models/Ai/Chat/SessionLedgerEntries.cs`) | TraceEvent `kind` | Fields carried (identifiers/counts only) |
|---|---|---|
| `SessionToolChain` (:170) | `tool_chain` | `turn`, `toolCallCount` |
| `SessionToolCall` (:186, per call) | `tool_call` | `toolId`, `argsSummary`, `resultCount`, `citations`, `durationMs` |
| `SessionGate` (:242) | `gate` | `gateId`, `gateKind`, `status`, `sideEffectClass`, `bindingId`, `missingFields`, `outputKey` |
| `TraceContextFingerprint` (**NEW**, this contract) | `context` | `fingerprintId`, `contextSliceCount` |

### The one NEW type — justification (CLAUDE.md §11)

`context` / `TraceContextFingerprint` is the only new surface:
- **Existing** — no ledger marker records "which context slices grounded this turn" (ToolChain = tools, Gate = approvals, Output = results).
- **Extension** — cannot fold into ToolChain/Gate without conflating context selection with tool execution.
- **Cost-of-doing-nothing** — without it the traceability view (task 038) cannot show WHY a turn was grounded as it was, and the ContextEnvelope fingerprint (FR-A0-01 / task 015) has no trace anchor. Stays id + count only → does not weaken NFR-07. When ContextEnvelope v1 lands, its fingerprint id binds to `fingerprintId`.

## Versioning & tolerant reader

- Every event stamps `version = "trace-event/v1"` (`TraceEventContract.SchemaVersion`).
- Consumers deserialize with `TraceEventContract.SerializerOptions` — camelCase, `UnmappedMemberHandling.Skip` → **unknown properties ignored**, so a v1 reader survives a future additive v1.x.
- **Evolution is additive-only**: new optional fields + new `TraceEventKind` members; never a rename or a field-type change.

## No-content guard (NFR-07)

Intentional asymmetry:
- **Consumers** ignore unknown fields (forward-compat / tolerant reader).
- **Emission** asserts a CLOSED sanctioned set: `TraceEventContract.CarriesOnlySanctionedFields(json)` returns false if any property is outside the curated `SanctionedFieldNames`. The allow-list is hand-curated (not derived from the type) so a new `TraceEvent` field is caught by the guard until reviewed. The negative test proves a smuggled `content` field is rejected.

## Ordering & partial states

- Total order: `(turn asc, kind-order asc, input order asc)` with a monotonic `sequence` assigned at projection. Kind-order = context(0) → tool_chain(1) → tool_call(2) → gate(3). Consumers order by `sequence` alone.
- Partial: a `pending` gate projects with no `outputKey` (no outcome yet); a `tool_chain` with `toolCallCount = 0` is valid. Consumers MUST render, not drop, partial events.

## Example payload (one turn: context → chain of 2 calls → confirmed gate)

```json
[
  { "version": "trace-event/v1", "sequence": 0, "turn": 1, "kind": "context", "timestamp": "2026-07-08T12:00:00+00:00", "fingerprintId": "ctx-fp-abc123", "contextSliceCount": 4 },
  { "version": "trace-event/v1", "sequence": 1, "turn": 1, "kind": "tool_chain", "timestamp": "2026-07-08T12:00:01+00:00", "toolCallCount": 2 },
  { "version": "trace-event/v1", "sequence": 2, "turn": 1, "kind": "tool_call", "timestamp": "2026-07-08T12:00:01+00:00", "toolId": "sprk_analysistool:document_search", "argsSummary": "matterId=123; top=5", "resultCount": 5, "citations": ["doc-1","doc-2"], "durationMs": 42 },
  { "version": "trace-event/v1", "sequence": 3, "turn": 1, "kind": "tool_call", "timestamp": "2026-07-08T12:00:01+00:00", "toolId": "sprk_analysistool:knowledge_retrieval", "argsSummary": "topic=indemnity", "resultCount": 3, "durationMs": 30 },
  { "version": "trace-event/v1", "sequence": 4, "turn": 2, "kind": "gate", "timestamp": "2026-07-08T12:00:02+00:00", "gateId": "gate-1", "gateKind": "confirmation", "status": "confirmed", "sideEffectClass": "write", "bindingId": "create-task", "outputKey": "create-task@t2" }
]
```

## Placement Justification (BFF Hygiene — root CLAUDE.md §10)

- **Placement**: `Services/Ai/PublicContracts/` — the ADR-013 facade for CRUD/consumer code reaching AI capability. Correct home for a read contract Compose r2 consumes.
- **New surface**: pure DTO records + static projection/renderer/guard helpers. **No new endpoint, no new DI registration, no new NuGet package, no new Dataverse column.**
- **Publish-size**: additive `.cs` only, no new dependencies → sub-0.1 MB; well under the 60 MB ceiling and below the +5 MB single-task escalation threshold. Full `dotnet publish` not run to avoid contention with 5 concurrent contract agents sharing this worktree; delta is bounded by "no new package / no new DI" (nothing that grows the closure).
- **CVE**: no package change → no new HIGH CVE.
- **Test obligation**: contract test added at KEEP path `tests/integration/contract/` (ADR-038); self-contained, no `Mock<HttpMessageHandler>`, no DI-registration assertions.

## Wiring needed by downstream (described, NOT done here — parallel-safety)

Task 013 does not touch shared files. Follow-ons:
- **SEAM-STATUS.md**: main session flips the `TraceEvent v1` row → ✅ published (commit ref) once merged; records FR-32 unblocked.
- **Task 038 (FR-A1-09)**: builds the traceability view + server ToolChain read surface ON this contract, and replaces the reference `TraceEventRenderer` stub with the rich `ExecutionTraceWidget` view; binds the `context` event's `fingerprintId` to ContextEnvelope v1 (task 015).
- No DI registration is required for v1 (static helpers); if task 038 wants an injectable projector it can wrap `TraceEventProjection` behind an interface then.
