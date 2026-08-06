# Task 012 — Outbox Service — Deviations & Notes

> Author: task-execute (sub-agent), 2026-07-21

## Summary

Implemented `OutboxService` in `src/server/api/Sprk.Bff.Api/Services/Notifications/OutboxService.cs`
over task 011's `sprk_notificationoutbox` table. No schema gaps found — all columns needed
(`ownerid`, `sprk_kind`, `sprk_envelope`, `sprk_regardingrecordid`, `sprk_regardingrecordtype`,
`sprk_delivered`, `sprk_dismissed`, `sprk_expiresat`) exist exactly as documented in
`docs/data-model/sprk_notificationoutbox.md`. The escalation trigger did NOT fire.

## Expiry mechanism: READ-TIME FILTER (chosen, documented in class XML doc)

`GetPendingAsync` excludes rows whose `sprk_expiresat` is in the past directly in the Dataverse
`QueryExpression` (an OR-filter: `sprk_expiresat IS NULL OR sprk_expiresat > now`). No sweep/mark
job was introduced. Rationale: keeps the write path single-shot (no second "expire" write),
and "pending" stays a pure function of the three timestamp columns at read time — matching the
lifecycle model in the data-model doc §3, which explicitly reserves the sweep-vs-filter decision
for this task.

## Seam test double: Dataverse boundary via Moq-free in-memory fake (not live spaarkedev1)

The task prompt instructed: if the local runner lacks Dataverse connectivity, author the test
correctly, document the gap, and report the true state rather than fabricating a pass. On
investigating the **existing** `tests/integration/seam/**` corpus (this project's own established
precedent, not an invention for this task), the pattern for Dataverse-backed seam tests is NOT
"hit the live tenant" — it's "double the Dataverse boundary, drive the real production service/logic
end-to-end." See `tests/integration/seam/Communication/AssociationSpineSeamTests.cs`: it Moqs
`IDataverseService` and wires the REAL `AssociationStatusMapper` / `AutoFileGate` /
`IncomingAssociationResolver` production types around it. The seam README's stated rule
("only external boundaries doubled... mocking the production logic defeats the category") is
satisfied by this shape — Dataverse itself is the external system being doubled, same as the LLM
boundary is doubled in the AI seam tests.

`OutboxServiceSeamTests.cs` follows this exact established shape:
- `OutboxService` (the real, unmodified production class) is the system under test.
- `FakeGenericEntityService` (test double, not Moq — a small in-memory store) doubles
  `IGenericEntityService`. Critically, it does not stub canned answers: it **interprets the
  actual `QueryExpression`/`FilterExpression` tree** `OutboxService.GetPendingAsync` builds
  (owner-equality condition, dismissed-is-null condition, expiry OR-filter with
  Null/GreaterThan), so the test proves the real query shape is correct — not just that some
  precomputed result flows through.
- `FixedTimeProvider` (inline `TimeProvider` subclass, matching
  `PortfolioServiceTests.FixedTimeProvider` — NOT the `Microsoft.Extensions.TimeProvider.Testing`
  NuGet, per ADR-029 publish-hygiene / bff-extensions §B "no new package without justification")
  gives deterministic "now" for the expiry scenario.

Both scenarios ran and PASSED locally (`dotnet test ... --filter FullyQualifiedName~OutboxServiceSeamTests`
→ 2/2 passed). This was NOT run against the live `spaarkedev1` test tenant — no such run was
possible or attempted from this sandboxed execution context, and per the established seam-test
precedent in this exact folder, a live-tenant run was not the pattern being followed for
Dataverse-backed seams. If a future task wants a live-tenant-only class of seam test, that is a
new precedent to establish deliberately, not an implicit expectation of this KEEP category.

## Placement Justification (per `.claude/constraints/bff-extensions.md`)

`OutboxService` lives in `Sprk.Bff.Api/Services/Notifications/` — colocated with the task-013
envelope contracts it serializes and consistent with `Services/NotificationService.cs`'s sibling
CRUD-only pattern. It has zero AI-internal or SignalR/delivery-layer dependencies (only
`IGenericEntityService`, `ILogger`, `TimeProvider`), so it is registered UNCONDITIONALLY (ADR-032
P1 — same B1 pattern already applied to `NotificationService`) in
`AnalysisServicesModule.AddUnconditionalChatAndNotificationServices` rather than a new DI module,
per CLAUDE.md §11 (default to reuse — this method already exists specifically to host
"CRUD-only deps that happen to live near AI/chat consumers," which is exactly `OutboxService`'s
shape).

## BFF Hygiene

- Build (`dotnet build ... -c Release`): 0 errors.
- Publish size incl-PDB: **47.39 MB** vs the task-stated ~47.38 MB SignalR-adjusted baseline —
  **delta ≈ +0.01 MB** (rounding noise; only `.cs` files added, no new package).
- `dotnet list package --vulnerable --include-transitive`: only the known pre-existing
  `System.Security.Cryptography.Xml 8.0.3` HIGH baseline finding. No new HIGH CVE.
- `deploy/api-publish-012/` + zip removed after measurement (deploy/ is gitignored).
