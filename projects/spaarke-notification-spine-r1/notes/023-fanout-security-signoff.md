# Task 023 — Fan-Out Targeting Security Sign-Off (R-5)

> Spec FR-08 / NFR-07. Security-critical: a leak of a private-thread or internal-only signal to an
> unauthorized user is a **compliance incident**.
>
> **Status of this document**: engineering evidence package prepared by an autonomous task-execute run.
> **An autonomous run CANNOT self-certify security.** See the sign-off block at the bottom — a **named human
> security reviewer MUST attribute and confirm** before this targeting is trusted in production.

---

## 1. What was built

`CommunicationFanOutTargetingService.GetEligibleRecipientsAsync(Entity message, Entity? thread, CancellationToken)`
→ `IReadOnlyCollection<Guid>` (Dataverse **systemuserids** eligible for a Layer-C ping).

- **File**: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationFanOutTargetingService.cs`
- **DI home**: `Infrastructure/DI/CommunicationModule.cs` (ADR-010 feature module — its dependencies are all
  Communication-flavored: `ICommunicationAccessFilter` + `IThreadPrivateGrantProvider` + the
  `sprk_communicationparticipant` junction via `IGenericEntityService`; NOT `NotificationsModule`, which owns the
  SignalR delivery leg). Registered as a Singleton.
- **Consumed by** the future task-024 producer, which loops the returned ids into
  `SignalRDeliveryService.PingUserAsync(outboxRowId, systemuserid, kind)` (the ping resolves systemuserid→oid
  internally). This task exposes the recipient set only; it does NOT touch SignalRDeliveryService.

## 2. Zero new access logic — every decision traces to an existing primitive

| # | Targeting rule | Source primitive (existing) | How it is composed | Seam test |
|---|---|---|---|---|
| 1 | Candidate set = the message's participants **only** | `sprk_communicationparticipant` junction, ADR-048 (message-grain) | app-only `IGenericEntityService.RetrieveMultipleAsync` filtered by `sprk_communication == messageId`; NEVER a security-role query / "record-visible" widening / "recent participants" | (a), (c) |
| 2 | Internal-only exclusion, fail-closed on unreadable flag | `ICommunicationAccessFilter.EvaluateMessage` (real) incl. its `IsInternalOnlyOrUnknown` | per candidate, build `CommunicationAccessContext` (IsInternalUser from junction identity type: systemuser⇒true, contact⇒false) and require `EvaluateMessage(...).IsVisible`; the internal-only check is **not re-implemented** | (b), (f) |
| 3 | Private-thread gate, fail-closed | `IThreadPrivateGrantProvider.GetActiveGrantsAsync` + `DenyAllThreadPrivateGrantProvider` (real default) | private (or **privacy-unknown**, fail-closed) thread ⇒ candidate eligible only with an active point-forward grant; DenyAll returns none ⇒ empty; default-deny is **not** bypassed | (a), (c) |
| 4 | Unresolved external → no recipient | junction `sprk_isresolved=false` / both person lookups null | natural exclusion — resolves to no systemuserid | (d) |
| — | Output grain = systemuserid | task-020 `PingUserAsync` signature | only a systemuser-typed candidate yields a pingable systemuserid; contacts (external) never appear | (e) positive |

**Confirmation**: there are **ZERO new authorization primitives** in this service. It reads two message/thread
flags and one junction, and delegates every access DECISION to `ICommunicationAccessFilter` or
`IThreadPrivateGrantProvider`. It never queries security roles, never unions membership, never calls AI.

## 3. Fail-closed discipline (mirrors CommunicationAccessFilter)

- Missing/unreadable `sprk_isinternalonly` ⇒ internal-only for a non-internal candidate ⇒ excluded (via the real filter).
- Missing/non-Open `sprk_privacystate`, or a **null thread** ⇒ treated as **PRIVATE** ⇒ grant-gated ⇒ empty under DenyAll.
- Missing `createdon` cannot satisfy a grant's point-forward `EffectiveFrom` ⇒ excluded.
- Consequence: a producer that forgets to project a flag **under-fans (safe)**, never over-fans (leak).

## 4. Honest finding — the internal-only filter is defense-in-depth for R1 systemuserid fan-out

In R1, every junction candidate that yields a systemuserid is a **systemuser**, which the access model treats as
**internal**. Therefore the internal-only filter, applied to systemuser candidates, always returns *visible* — it
never changes the R1 systemuserid output on its own. External parties are excluded **twice**: (i) by the reused
internal-only filter (the load-bearing rule the moment R2/R3 makes contacts pingable via a contact-scoped push
channel), and (ii) by the systemuserid projection (load-bearing today, since a contact has no systemuserid). Both
point the same, fail-safe way. The filter is applied unconditionally so the security contract is explicit and
tested, not incidental. **This is not a gap; it is intentional redundancy** — but a human reviewer should confirm
the framing is acceptable and note that if the internal/external distinction ever becomes finer-grained than
"systemuser vs contact", the fan-out MUST source `IsInternalUser` from that same finer primitive (which does not
exist today — its absence would be the escalation trigger, not a silent guess).

## 5. Negative-access test results

`tests/integration/seam/Communication/FanOutTargetingSecuritySeamTests.cs` — REAL `CommunicationAccessFilter` +
REAL `DenyAllThreadPrivateGrantProvider`; only the Dataverse junction read (`IGenericEntityService`) doubled
(ADR-038 seam; NOT a unit-mock of the access primitives).

| Case | Scenario | Expected | Result |
|---|---|---|---|
| (a) | private thread, external non-participant | not targeted (empty fan-out) | ✅ PASS |
| (b) | internal-only message, external participant | external excluded, internal included | ✅ PASS |
| (c) | private thread, non-participant record-visible elsewhere | not targeted (empty) | ✅ PASS |
| (d) | unresolved external (`sprk_isresolved=false`) | contributes no recipient | ✅ PASS |
| (e) | **positive**: internal participant, open, non-internal-only | targeted (no over-exclusion) | ✅ PASS |
| (f) | fail-closed: unreadable `sprk_isinternalonly` | external excluded, internal kept | ✅ PASS |

**`dotnet test` (Release, filter `FanOutTargetingSecuritySeamTests`): Passed 6 / Failed 0 / Skipped 0.**
**`dotnet build src/server/api/Sprk.Bff.Api` (Release): 0 errors** (21 pre-existing warnings, none in the new files).

## 6. Escalation trigger — NOT fired

The targeting answer was fully expressible by composing the three existing primitives. No heuristic, no broad
security-role query, and no new access logic were required. The `<escalation><trigger>` did not fire.

---

## 🔔 REQUIRES NAMED HUMAN SECURITY SIGN-OFF

An autonomous task-execute run **cannot self-certify** a security-critical fan-out. Before this targeting is
trusted in production, a **named human security reviewer** MUST:

1. Confirm the composition in §2 introduces no new access primitive and that each rule traces as claimed.
2. Confirm the §4 defense-in-depth framing (internal-only filter as R1 redundancy + R2/R3 load-bearing) is acceptable.
3. Confirm the fail-closed choices in §3 (unknown privacy ⇒ private ⇒ deny) are the desired posture.
4. Re-read the 6 negative-access seam cases and confirm coverage is sufficient for FR-08 / NFR-07 / R-5.

**Named reviewer**: ______________________  **Date**: __________  **Attestation**: I have reviewed the above and
confirm the fan-out targeting composition is sound and leak-free for the stated model. ☐

*(Unattributed = NOT signed off. Do not treat this document as security clearance until a named reviewer completes the block above.)*
