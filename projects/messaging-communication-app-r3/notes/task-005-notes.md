# Task 005 — FR-19: honor `request.ThreadId` on the EMAIL send branch

> **Status**: implemented — build/test/publish/CVE green. Pending PR + `/conflict-check` (shared `CommunicationService.cs`).
> **Rigor**: FULL · **Model tier**: opus · **Date**: 2026-07-20

---

## Change summary (minimal — pure modification, no new surface)

Brought the EMAIL outbound branch to parity with the MESSAGE branch by adding an early explicit-thread
stamp to `ResolveOutboundThreadAsync` in
`src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs`:

```csharp
// (1) FR-19 explicit target — mirror the Message path; reuse the SAME helper.
if (request.ThreadId.HasValue)
{
    await AssignExplicitThreadAsync(communicationId, request.ThreadId.Value, correlationId, ct);
    return;
}

// (2) Find-or-create (unchanged pre-FR-19 behavior).
if (_threadResolver is null)
    return;
...
```

- The explicit branch is placed **before** the `_threadResolver is null` guard — an exact mirror of the
  Message path's `ResolveOutboundMessageThreadAsync` (line ~816), so an explicit `ThreadId` is honored even
  when no find-or-create resolver is wired.
- **`AssignExplicitThreadAsync` is reused AS-IS** — no signature/semantic change, no fork, no duplication.
  It is the same helper the Message path calls. Its existing behavior (stamp `sprk_communicationthread`,
  best-effort self-contained try/catch, and — only when `_directThreadAccess is not null` — chain the
  task-043 grant) is left untouched. **No grant was added by this task** (task 043 has not run; the helper's
  current behavior is simply inherited).
- Updated the method's XML doc comment to describe the new two-path contract (explicit target vs
  find-or-create).

## Both call sites covered by a single edit

Both EMAIL outbound call sites route through the **same** private `ResolveOutboundThreadAsync`, so the one
edit covers both:

| Call site | Location | Path |
|---|---|---|
| Shared-mailbox send | `SendAsync` → line ~1072 | `await ResolveOutboundThreadAsync(communicationId.Value, request, correlationId, cancellationToken)` |
| OBO send (`SendMode.User`) | `SendAsUserAsync` → line ~1378 | `await ResolveOutboundThreadAsync(communicationId.Value, request, correlationId, ct)` |

Neither call site bypasses the method; no second guard was needed. Both are exercised by tests (below).

## Regression + best-effort preservation

- **Regression (correctness-critical)**: an email with **no** `ThreadId` falls through unchanged to
  `_threadResolver.ResolveAndAssignThreadAsync` (find-or-create). Only the `ThreadId.HasValue` case changed.
  Pinned by `SendAsync_ForEmailType_WithNoThreadId_UsesFindOrCreateThreadResolver`.
- **Best-effort / non-fatal (NFR-02)**: `AssignExplicitThreadAsync` swallows its own write exceptions
  (logs a warning, returns null). A stamp failure therefore never fails an already-sent + persisted email.
  Pinned by `SendAsync_ForEmailType_WithThreadId_WhenStampWriteFails_StillSucceeds`.
- **ADR-046**: the Message/ACS branch (`SendMessageAsync` / `ResolveOutboundMessageThreadAsync`) was not touched.

## Tests added / updated

File: `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationServiceEmailSendThreadTests.cs`
(supersedes the task-001 PRE-FR-19 characterization baseline — the "email silently ignores ThreadId" pin
is the behavior FR-19 intentionally flips).

1. `SendAsync_ForEmailType_WithNoThreadId_UsesFindOrCreateThreadResolver` — regression: no ThreadId ⇒
   find-or-create resolver runs, no direct stamp. (retained)
2. `SendAsync_ForEmailType_WithThreadId_StampsThreadLookupDirectly_NoFindOrCreate` — **flipped** the old
   baseline: shared-mailbox WithThreadId now stamps `sprk_communicationthread = T` directly; strict
   `IThreadResolver` proves find-or-create is never called.
3. `SendAsUser_ForEmailType_WithThreadId_StampsThreadLookupDirectly_NoFindOrCreate` — OBO (`SendMode.User`)
   call site also stamps directly (covers the second call site).
4. `SendAsync_ForEmailType_WithThreadId_WhenStampWriteFails_StillSucceeds` — stamp write throws ⇒ send still
   succeeds (best-effort / NFR-02).

ADR-038 compliance: boundary doubles only (`IThreadResolver`, `IGenericEntityService` mocked; a hand-written
`ICommunicationChannelSender` recording double for the Graph transmit — NOT `Mock<HttpMessageHandler>`).
Behavior assertions, not wiring/DI/constructor tests.

## Verification results

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors** (19 pre-existing warnings) |
| `dotnet test … --filter Services.Communication` | **555 passed, 0 failed, 5 pre-existing skips** |
| Email-send thread tests (isolated) | **4 passed, 0 failed** |
| Publish compressed size | **~45.71 MB** (zlib est.) — under the 60 MB ceiling; delta **≈0** (no packages added; R4 baseline ~45.30–46 MB) |
| `dotnet list package --vulnerable --include-transitive` | **0 NEW HIGH CVE** — only the pre-existing `System.Security.Cryptography.Xml 8.0.3` (not introduced by R3) |
| `git diff --name-only` | `CommunicationService.cs`, `CommunicationServiceEmailSendThreadTests.cs`, + this notes file |

## Placement Justification (cite `.claude/constraints/bff-extensions.md`)

- **Placement decision**: stays in the BFF `Services/Communication/CommunicationService.cs`. This is a pure
  modification of an existing private method on the canonical send engine — the natural, only home. No new
  endpoint, service, DI registration, package, background worker, or config field. Decision-criteria table
  (bff-extensions.md §"Does this belong in the BFF?"): latency-coupled to the in-request send lifecycle
  (YES→BFF), writes to BFF-managed record state in the same request (YES→BFF); not event-driven.
- **No new CRUD→AI dependency**; no new surface; **size delta ≈0** (< 2 MB, no owner ack needed).
- **Boundary preservation**: reuses the existing `AssignExplicitThreadAsync` helper and the existing
  `CommunicationChannelDispatcher`; adds no second send path or thread-assignment mechanism (ADR-045).
- **Test-update obligation (§F)**: satisfied — the `Services/Communication/` behavior change ships with
  updated unit tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/`.
- **No `<justification>` element required** — pure modification of an existing method (adds no new component).

## ADR compliance (Step 9.5 self-check)

- **ADR-045**: ✅ no new send/assignment path; reuses `AssignExplicitThreadAsync`; dispatch unchanged.
- **ADR-046**: ✅ Message/ACS branch untouched.
- **ADR-038**: ✅ behavior tests, module-boundary doubles only, no banned antipatterns.
- **Escalation trigger (POML)**: did NOT fire — parity was achieved by reusing the existing helper without
  changing its signature/semantics or the message path.

Spec references: FR-19 + Success Criterion 4.
