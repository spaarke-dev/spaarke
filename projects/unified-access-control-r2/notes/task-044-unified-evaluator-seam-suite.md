# Task 044 — unified-evaluator seam suite: delegated matrix rows

New file: `tests/integration/seam/ExternalAccess/UnifiedEvaluatorSeamTests.cs` (29 tests, all green
alongside the existing `tests/integration/seam/ExternalAccess/**` suites; 417/417 green across the full
`*ExternalAccess*` namespace — unit + seam combined — with zero `src/server/**` changes).

## What the suite composes

Production `AccessibleRecordSetService` (unmocked) + production `SubjectStandingGrantReader` (unmocked,
over a substituted `IDataverseService`) + production `NoAccessListReader` (unmocked, doubled only at its
own `internal virtual QueryChunkAsync` wire seam — the same seam `NoAccessListReaderTests.cs` uses). The
remaining doubled boundaries — `IMembershipResolverService` and `ExternalParticipationService`'s virtual
data reads (grants / veto flags / active-org membership / referenced-org lookups) — are the same
boundaries `StandingGrantRuntimeUnionSeamTests.cs` (task 051) already substitutes; this suite generalizes
that pattern across the whole Phase 1+2 contract instead of one term.

## Matrix rows delegated to a unit test instead of pinned at this seam

| Acceptance-criterion fragment | Why not expressible here | Pinned instead in |
|---|---|---|
| FR-24: "a registry-listed column confers; a non-registry `sprk_assigned*` column does not" | The access-conferring column registry (task 041, `MembershipOptions.AccessConferringRoles`) is applied entirely INSIDE `MembershipResolverService`'s FetchXml-shape construction — a layer this seam substitutes away via `IMembershipResolverService`, exactly as the precedent seam substitutes it for the standing-grant flag. Composing the real `MembershipResolverService` would require doubling a deeper Dataverse query-execution boundary and re-implementing FetchXml-shape assertions this suite doesn't own. | `MembershipResolverServiceTests.cs` (task 041) — asserts the FetchXml shape per registry entry, including the retired `sprk_assigned*` naming-convention negative case. |

What IS pinned at this seam for FR-24 instead: `FR24_SystemUserMembershipWalk_AlwaysRequestsTheAccessConferringOnlyFilter` — a Strict-mock wiring proof that the composer always requests the registry-filtered view (`AccessConferringOnly: true`) on the systemuser plane, since the registry is meaningless unless the caller actually asks for it. `ResolveByContactAsync` needs no equivalent proof — the resolver applies the registry unconditionally there per its own contract, so there is nothing for the composer to get wrong.

The other half of FR-24 in the matrix — "opposing-counsel org reference confers nothing [[via the additive
path]] ... [[yet]] still denies" — IS expressed at this seam, on the DENY side, where the composer's own
behavior (not the registry) is what's being pinned:
`FR23_ContactSubjectOrgObjectDenyEntry_DeniesARecordReferencingThatOrganization_EvenANonConferringReference`.

## Impersonation (FR-20)

Deliberately excluded per this task's own POML notes. Its contract lives in
`tests/integration/auth/ImpersonationNegativeCanaryTests.cs` (task 034) against live Dataverse, which is
the appropriate boundary for that concern — this seam's substituted `IDataverseService`/
`IMembershipResolverService` boundaries would only prove the double behaves as configured, not that
impersonation is live.

## Phase 3 note

This suite is the characterization baseline the POML names for Phase 3 (child inheritance): the world
(orgs, contacts, systemusers, records with flags/lookups, grants, standing flags, deny entries) and the
harness (`ParticipationWorld`, `SeamNoAccessListReader`, `BuildDataverse`, `BuildSut`) are built to extend
with child records without restructuring.
