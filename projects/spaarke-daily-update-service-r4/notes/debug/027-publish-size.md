# Task 027 — Publish-Size Verification

> **Date**: 2026-06-25
> **Task**: 027 — Add structured `member_skipped` warning logging for Contact-only members
> **Constraint**: CLAUDE.md §10 BFF Hygiene — ≤60 MB ceiling, ≤+5 MB single-task delta
> **Baseline**: 47.14 MB (compressed, from task 021 checkpoint — measured with `tar -czf`)
> **Prior baseline (Compress-Archive)**: 46.31 MB (from task 020 checkpoint)

---

## What task 027 changed

**Logging-site decision**: `MembershipResolverService.BuildFetchXml` (in
`src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs`).

The `LookupUserMembershipNodeExecutor` is a thin wrapper that delegates to
`IMembershipResolverService.ResolveAsync`. The Contact ↔ SystemUser cross-ref
gap (per ADR-028) is detected one layer deeper, inside the resolver's per-descriptor
FetchXml builder where the `case "Contact":` branch was silently skipping the
descriptor when `identity.ContactId is null`. The log MUST emit at the precise
skip point so structured-field bindings reflect the actual descriptor + role.

### Code delta

- `MembershipResolverService.BuildFetchXml` was `static` — refactored to instance
  method so it can access the existing `_logger` (an `ILogger<MembershipResolverService>`
  injected via constructor — no new DI dependencies).
- Added `systemUserId` parameter (already available in `ResolveAsync`) for the
  `ContactId` field binding (literal NULL is the trigger; SystemUserId is the
  traceable identifier).
- `case "Contact":` branch: when `identity.ContactId` is NULL, emit:

  ```csharp
  _logger.LogWarning(
      "member_skipped: matter={MatterId} contact={ContactId} role={Role} reason={Reason} field={Field}",
      entityType,         // sprk_matter in the canonical case
      systemUserId,       // the caller whose Contact cross-ref is missing
      d.Role,             // e.g., "assignedAttorney"
      "no_systemuser_mapping",
      d.Field);           // e.g., "sprk_assignedattorney1" (extra diagnostic)
  ```

- Behavior unchanged: the descriptor is still skipped (no FetchXml condition
  emitted for it). Only observability is added.

### Field-binding rationale (FR-11 vs generic resolver context)

FR-11 specifies fields `matterid`, `contactid`, `role`, `reason`. The
`MembershipResolverService` is a generic per-user resolver (not matter-specific):

| FR-11 field | Bound to | Why |
|---|---|---|
| `MatterId` | `entityType` | The entity being resolved; equals `sprk_matter` in the canonical assignedAttorney case |
| `ContactId` | `systemUserId` | ContactId itself is the literal `null` trigger; emitting SystemUserId gives traceability |
| `Role` | `descriptor.Role` | e.g., `assignedAttorney`, `secondaryContact` |
| `Reason` | literal `"no_systemuser_mapping"` | per FR-11 exact value |
| `Field` | `descriptor.Field` | extra diagnostic — not required by FR-11 |

The literal token `member_skipped` lives in the message template so App Insights
`traces | where message contains "member_skipped"` queries match. Same pattern
as task 003's `hallucination_detected` event.

### Tests added (3 new methods)

In `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/MembershipResolverServiceTests.cs`:

| Test | Asserts |
|---|---|
| `ResolveAsync_ContactDescriptorWithNullContactId_EmitsMemberSkippedWarning` | The warning fires exactly once with all 4 FR-11 fields bound when ContactId is NULL |
| `ResolveAsync_ContactDescriptorWithContactId_DoesNotEmitMemberSkipped` | Inverse: no warning when ContactId is present (guards false positives) |
| `ResolveAsync_MultipleContactDescriptorsAllNull_EmitsOneWarningPerDescriptor` | Per-descriptor emission semantics — two unresolvable Contact descriptors → exactly two warnings |

All 3 new tests pass; the full `MembershipResolverServiceTests` fixture stays
green (26 of 26 passing — was 23, added 3).

---

## Measurement

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
Compress-Archive -Path deploy/api-publish/* -DestinationPath deploy/api-publish.zip -Force
```

| Metric | Value |
|---|---|
| Compressed publish output | **46.31 MB** (PowerShell `Compress-Archive`) |
| Uncompressed publish size | 140.07 MB |
| Delta vs task 020 baseline (46.31 MB, same tool) | **0.00 MB** |
| Delta vs task 021 baseline (47.14 MB, different tool — `tar -czf`) | **-0.83 MB** (tooling artifact, not a code regression) |
| Distance from §10 ceiling (60 MB) | **13.69 MB headroom** |
| Single-task delta threshold (≤+5 MB) | ✅ within limit |
| Cumulative ceiling check (≤55 MB) | ✅ within limit |

The change adds ~20 LoC (12 lines of executable code in the production path +
the rest is comments). Publish-size impact is effectively zero — well within
the §10 §F-3 threshold.

---

## CVE check

```
dotnet list package --vulnerable --include-transitive
```

| CVE | Severity | Pre-existing? |
|---|---|---|
| Microsoft.Kiota.Abstractions 1.21.2 — GHSA-7j59-v9qr-6fq9 | High | ✅ Pre-existing in master (carried across all R4 tasks since task 020) — NOT introduced by task 027 |

**No NEW HIGH CVE introduced by this task.** Task 027 adds zero NuGet packages.

---

## Justification (per CLAUDE.md §10 + §11)

| Question | Answer |
|---|---|
| Existing — what does this overlap with? | `_logger` is already injected into `MembershipResolverService`; no new types or DI registrations. The `case "Contact":` branch already exists; we only extend it with an `else`-arm log emission. |
| Extension — can I extend the existing instead? | Yes — extending the existing service is what we did. No new service or interface introduced. |
| Cost-of-doing-nothing — concrete failure mode? | Without the warning, ops cannot detect when Contact-only members are silently dropped from matter membership resolution. AC-11's integration test (filed for task 028) cannot pass without this emission. FR-11 is a binding R4 deliverable. |

**Placement decision**: in-place inside the resolver — emitted at the precise
descriptor-skip site so structured fields reflect the actual skip. Not in the
node executor (that layer wraps the resolver and loses descriptor-level context).

**Asymmetric-registration check (§F.1)**: not applicable — no DI changes, no
`*Module.cs` modifications, no feature-gated services introduced. The
`MembershipResolverService` registration in `MembershipModule.cs` is unchanged.

**Test obligation (§F)**: 3 new xUnit tests added covering the positive case
(warning fires with correct field bindings), the negative case (no warning when
ContactId is present), and the per-descriptor multiplicity invariant.

---

## Downstream unblocked

This task UNBLOCKS:
- Task 028 — customData schema conformance xUnit fixture (will assert AC-11 via
  mock-`ILogger` capturing the structured `member_skipped` event)
