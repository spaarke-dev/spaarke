# Deferred Work / Issues — code-quality-and-assurance-r3

> Two-write tracking (per `/project-defer-issue-tracking`): source of truth here + GitHub Issue on Epic #427.

| ID | Title | GitHub Issue | Status |
|----|-------|--------------|--------|
| DEF-1 | `TodoGenerationService` — 2 of 5 rules (Overdue events, Deadline) silently generate zero To Dos (event query → SDK silent-empty stub) | {URL — to file} | ROUTED → smart-todo-r5 |
| DEF-2 | `DataverseWebApiService.GetEntitySetNameAsync` **throws `NotImplementedException`**, yet 3 LIVE field-mapping methods call it — field-mapping read/write via the WebApi path throws (correctness) | {URL — to file} | OPEN — decision needed (owner) |

## DEF-2 — WebApi field-mapping throws via unimplemented `GetEntitySetNameAsync` (LATENT CORRECTNESS BUG)

**Discovered**: 2026-08-16 during the RED-4 "B" hardening (verified against code at HEAD).
**Severity**: latent correctness — throws (not silent) when the WebApi field-mapping read/write path executes.
**Partially mitigated already**: the compose cold-session variant was hit in owner UAT (spaarke-bff-dev) and
fixed narrowly by removing ONE caller's dead call — see regression `tests/integration/regression/Compose/ComposeOutputsColdSessionTests.cs`. The underlying stub was left throwing.

**The bug**: `DataverseWebApiService.GetEntitySetNameAsync` (now `DataverseWebApiService.cs:176`) is a stub
that unconditionally `throw new NotImplementedException(...)`. But three **live** `IFieldMappingDataverseService`
methods on the same class call it as their first operation:
- `RetrieveRecordFieldsAsync` (`:785`) → `await GetEntitySetNameAsync(...)`
- `QueryChildRecordIdsAsync` (`:839`) → `await GetEntitySetNameAsync(...)`
- `UpdateRecordFieldsAsync` (`:1050`, the WebApi half of the split-brain, RED-4 routing-doc trap #2) → `GetEntitySetNameAsync(...)`

`IFieldMappingDataverseService` resolves to `DataverseWebApiService` (`GraphModule.cs:78`). So every
field-mapping read/child-query/write routed to the WebApi impl throws before issuing its request. **6 live
callers** inject the WebApi path for `UpdateRecordFieldsAsync` alone (ScorecardCalculatorService,
SignalEvaluationService, InvoiceReviewService, DataverseUpdateHandler, FieldMappingEndpoints,
UpdateRecordActionCore); `FinanceRollupService` alone hits the SDK impl (working `GetEntitySetNameAsync`).
That is the "split-brain `UpdateRecordFieldsAsync`" trap, now shown to be a **throwing** stub on the WebApi side,
not merely a duplicate.

**Why this is a decision, not a mechanical fix** (routed, not auto-fixed — mirrors DEF-1): the fix must choose
how the WebApi field-mapping methods resolve logical-name → entity-set-name without regressing the
impersonation/POA row-level-security model that put field-mapping on the WebApi impl in the first place. Options:
- **A — implement `GetEntitySetNameAsync` on the WebApi impl** (real Dataverse metadata query, cached; the SDK
  impl already does this) so all three live methods work. Bounded, keeps impersonation.
- **B — resolve the set-name via a shared convention/cache helper** injected into both impls (no per-call
  metadata round-trip).
- **C — fold into RED-4 "C" unification** (single impl) — the set-name problem disappears when there is one path.

**Recommendation**: A or B as a bounded fix now (correctness), independent of C. Needs owner decision + a
regression test that exercises a WebApi field-mapping write end-to-end (the current regression only covers the
compose cold-session read path).

**Coordination with the stub→throw step**: the RED-4 B "convert the SDK silent-empty stubs → throw" step is
gated on DEF-1 (smart-todo-r5). DEF-2 is on the OTHER impl (WebApi) and is independent of that gate, but both
should land before RED-4 "C" so unification starts from a loud, correct baseline.

**Evidence**: `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:176,509,839` (+ `UpdateRecordFieldsAsync`);
`docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md` trap #2; `tests/integration/regression/Compose/ComposeOutputsColdSessionTests.cs`.

## DEF-1 — event-sourced To Do generation silently broken (LATENT BUG → routed to smart-todo-r5)

**Discovered**: 2026-08-15 during the RED-4 Dataverse access-layer hardening investigation.
**Owner**: **smart-todo-r5** (the active To Do project) — see the detailed handoff
`projects/smart-todo-r5/notes/INBOUND-event-sourced-todo-generation-broken.md`.

**Defect**: `TodoGenerationService` outputs `sprk_todo` correctly (r3 decoupling), but **2 of its 5 rules —
"Overdue events" (`ProcessOverdueEventsAsync:322`, `QueryEventsAsync:334`) and "Deadline proximity"
(`ProcessDeadlineProximityAsync:~460`) — query `sprk_event` via the composite `IDataverseService`
(→ SDK `DataverseServiceClientImpl`), whose `QueryEventsAsync` is a silent-empty stub** (returns empty +
`LogWarning`; the real impl is on `DataverseWebApiService`, reached only via `IEventDataverseService`). So
those two rules always produce zero To Dos. Rules 2/4/5 (budget, invoices, tasks) use FetchXML via the SDK
and work.

**Architectural context (why it's a To Do-domain call)**: the r3 decoupling made To Dos independent records
(regarding via RegardingResolver, ADR-024) — no longer "part of Events." So the fix is a **decision**:
(A) event-sourcing still wanted → inject `IEventDataverseService` for Rules 1 & 3 (behavior change — validate
volume/dedupe before enabling); or (B) legacy → remove Rules 1 & 3. Small change either way; the decision is
the To Do team's.

**Coordination**: the `dataverse-access-hardening` stub→throw step is sequenced AFTER this resolves (else the
throw crashes the generator pass). Resolve in smart-todo-r5, then ping the hardening owner.

**Evidence**: `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md` §Known traps #1;
`projects/smart-todo-r5/notes/INBOUND-event-sourced-todo-generation-broken.md`.
