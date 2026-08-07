# Access-model amendment — system-user ∪ own-contact grants ("parallel workforce/contact access")

> §6.5 **Path-B amendment** of the accessible-record-set composition rule (teams-app-r1 design §5 / spec FR-06).
> Origin: external-access-r2 UAT, 2026-08-07 — **owner directive**. Security-sensitive (broadens system-user visibility); owner-approved.

## The prior rule (what we changed)
`AccessibleRecordSetService` composed the Tier-2 accessible set per plane, with the system-user plane being **membership-only**:

> *"systemuser principal = ADR-034 membership ONLY (automatic). No grants/standing term."*

Consequence surfaced in UAT: an internal person who signs in with **workforce/system-user** credentials (`ralph.schroeder@spaarke.com`) resolves to a **SystemUser** principal, so their **contact's** `sprk_externalrecordaccess` grants were **never consulted** — the external-access grids came back empty even though the person's contact held a grant.

## The requirement (owner)
> "A system user … will want to be able to log in to [the external access SPA] to 'see what's there' and potentially direct an external user through the system. There needs to be some path for a system user — through a parallel workforce/contact access."

## The new rule
For a **SystemUser** principal, the accessible set is now:

    accessible(systemuser) = ADR-034 membership  ∪  the caller's OWN contact grants   (project-scoped)

- **Project-scoped**: grants are `sprk_project`-scoped in R1 (design §5 gap #2), so the union term applies only when enumerating projects. Non-project entities (e.g. `sprk_matter`) remain membership-only — unchanged.
- **The caller's OWN grants only**: the contact is the system-user's **derived** contact (`sprk_primarycontact`), with a **verified-email fallback** (`ResolveExternalContactAsync(oid:null, email)`) when no `sprk_primarycontact` link exists. Never "all projects" — NFR-08 preserved.
- **Standing-grant is still never consulted for a system-user** (unchanged).
- Fail-safe: no derived contact **and** no email ⇒ membership-only (no union term).

## Why Path-B (not A)
This is a general revision of the composition rule — it should apply everywhere the accessible set is composed for a system-user (data reads + any future record∈set gate), not a one-project exception. It changes a delivered design-§5 rule, so it is recorded here as an amendment and cited in the PR. It does **not** touch any auth ADR (ADR-028 A1/A2/A3 unchanged — still broker-only, no OBO, plane selected by validated iss/tid).

## Implementation
- `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` — `ComposeForSystemUserAsync` unions the derived-contact (or email-resolved) project grants; sets `Sources.ContactGrants` for audit.
- `Infrastructure/ExternalAccess/ExternalCallerContext.cs` — `WorkforcePrincipal.Email` added; `WorkforcePrincipalResolution.ForSystemUser(..., email)` optional param.
- `Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs` — passes `ExtractVerifiedEmail(user)` at the system-user branch.
- Tests: `AccessibleRecordSetServiceTests` — (1b) systemuser+linked-contact-grant union, email-fallback resolution, and the membership-only fail-safe (no contact/no email). 176 ExternalAccess unit tests green.

## Downstream (P2)
P2 tasks 024/025 (workforce-plane auth policy + role→level grading) inherit this rule. If a per-project level for a system-user's contact-granted projects is later desired, refine there. Consider whether the same union should extend to non-project grant scopes if grants are generalized beyond projects (design §5 gap #2).

## Verification
`dotnet build` 0-err; ExternalAccess unit suite 176 pass. Redeployed to `spaarke-bff-dev` from the worktree (see task-019-deployment-record.md).
