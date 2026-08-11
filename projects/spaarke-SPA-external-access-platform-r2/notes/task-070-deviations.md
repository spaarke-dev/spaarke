# Task 070 — Polymorphic external grant-WRITE (BFF) — deviations & live-UAT findings

> 2026-08-11 · FULL rigor · opus @ xhigh · Companion (write-side) to task 028's polymorphic reads.
> Branch `work/spaarke-SPA-external-access-platform-r2`. Deployed to spaarke-bff-dev from worktree +
> **live-verified end-to-end** against the real endpoint.

## Headline: the external grant-WRITE path was completely broken and had never worked

Live smoke-testing the deployed `/api/v1/external-access/grant` surfaced that **every** grant — matter,
work-assignment, AND the byte-identical legacy project path — returned Dataverse 400. Root-cause
investigation (via live `$metadata` + MCP reproduction, per §6.5 Empirical-Reproduction-FIRST) found
the grant path shipped by teams-app-r1 was never actually executed live (its E2E was owner-pending), so
multiple payload bugs were latent. Task 070 fixes all of them; grants now work.

### Bug 1 (PRIMARY) — `@odata.bind` navigation-property names were wrong

The single-valued navigation property for a lookup on `sprk_externalrecordaccess` is **PascalCase**
`sprk_X`, NOT the lowercase `sprk_xid` form the code used. Verified live via
`EntityDefinitions('sprk_externalrecordaccess')/ManyToOneRelationships`:

| Attribute | Code used (WRONG) | Actual nav property (verified) |
|---|---|---|
| `sprk_contact` | `sprk_contactid` | **`sprk_Contact`** |
| `sprk_project` | `sprk_projectid` | **`sprk_Project`** |
| `sprk_matter` | `sprk_matterid` | **`sprk_Matter`** |
| `sprk_workassignment` | `sprk_workassignmentid` | **`sprk_WorkAssignment`** |
| `sprk_grantedby` | `sprk_grantedby` | **`sprk_GrantedBy`** |

The bind VALUE (plural entity set — `/sprk_matters({id})`, `/contacts({id})`) was already correct.
Fix in `ExternalGrantRoot.BindFor` + `BuildGrantPayload`.

### Bug 2 — `sprk_grantedby` bound the caller's AAD oid as a systemuserid

`ResolveCallerSystemUserId` returns the Azure AD object id (oid), which was bound directly as
`/systemusers({oid})`. A Dataverse `systemuserid` is a DISTINCT GUID from the AAD oid (proven: caller
oid `c74ac1af…` ↔ systemuserid `1d02f31c…`) → invalid reference → 400. Fix: new
`ResolveGrantedBySystemUserIdAsync` maps oid → systemuserid via `systemuser.azureactivedirectoryobjectid`;
**omit `grantedby` if unresolved** (an audit field must never 400 the grant).

### Bug 3 — expiry field name

`sprk_expirydate` → **`sprk_expiresdate`** (verified live). Would 400 any grant carrying an expiry
(e.g. task 071's modal).

### Bug 4 — firm/org association (owner steer)

The prior `sprk_accountid@odata.bind → /accounts` is wrong twice: (a) `sprk_externalrecordaccess` has NO
account lookup, and (b) Spaarke models the firm/org as **`sprk_organization`**, not the OOB `account`
(owner, 2026-08-11). There is currently **no org/account lookup on the grant table** at all, so the bind
was removed (it 400'd every grant that sent `AccountId`, incl. `/invite-and-grant`). `AccountId` is now
accepted-but-not-persisted. **Follow-up (owner + 071/072)**: add a `sprk_organization` lookup to
`sprk_externalrecordaccess`, rename DTO `AccountId → OrganizationId`, and bind it. → /defer.

## Polymorphic generalization (the task's nominal scope)

- `ExternalGrantRoot` (enum + `BindFor` + `TryParse`) — no new interface (ADR-010).
- `GrantAccessRequest`/`InviteExternalUserRequest`: optional `{RecordType, RecordId}`; legacy `ProjectId`
  = back-compat shorthand.
- `ResolveGrantRoot` (fail-closed, NFR-08) + polymorphic `BuildGrantPayload` (binds exactly ONE typed
  root lookup per record type).
- `/invite-and-grant` threads the root (400 before onboarding side effects); `/revoke` + `/invite` no
  longer require `ProjectId`.
- `ProjectClosureEndpoint`: bug fix `_sprk_projectid_value` → `_sprk_project_value` (the invalid field
  matched zero rows → close-project silently revoked nothing). `BuildActiveProjectGrantsFilter` extracted
  for regression test.

## Live UAT — verified against deployed spaarke-bff-dev

| Case | Result |
|---|---|
| POST /grant `{recordType:matter}` | **200** → row with `sprk_matter` set, project/WA null |
| POST /grant `{recordType:workassignment}` | **200** → `sprk_workassignment` set, matter/project null |
| POST /grant `{projectId}` (legacy) | **200** → `sprk_project` set, matter/WA null |
| POST /grant `{}` (no root) | **400** fail-closed (exact message) |

Each grant bound exactly ONE typed root lookup — no cross-binding. Test rows cleaned up.

## Escalation trigger — fired, resolved (not silently bypassed)

The armed trigger ("live @odata.bind nav name differs from owner-provided `sprk_matterid`/
`sprk_workassignmentid`") **fired**: the owner-provided names (enhancement-note locked-decision #5) were
INCORRECT. Resolution: determined the authoritative names from live `$metadata` (Path C — pivot to the
correct implementation), applied + live-verified. Surfaced to the owner in the session + this note.

## Minor / non-blocking observations

- `sprk_grantedby` resolved to null under the CLI (`az account get-access-token`) smoke-test token — the
  grant correctly proceeded without it (proving the non-blocking design). Confirm it populates under a
  real workforce SSO token in the task-073 UAT. Non-blocking either way.
- `/revoke` (ProjectId no longer required) and `close-project` cascade-revoke (`_sprk_project_value` fix)
  are unit-verified (incl. a regression test) but not live-smoke-tested here — covered by the task-073
  wave UAT.

## Verification summary

- External-access unit suite **225 pass / 0 fail** (+ polymorphic-write + regression tests). Full BFF
  suite green pre-merge (10316).
- Publish **48.45 MB** compressed (≤60); no vulnerable packages.
- Quality gates (code-review + adr-check): PASS (no ADR violations; §10 Placement Justification =
  generalizes existing external-access endpoints, no new endpoint/service/package/DI).
- Deployed to spaarke-bff-dev from worktree (health + SHA verified); worktree updated from master
  (0 behind), `/conflict-check` clean.

## /defer items (need owner intent — file to notes/defer-issues.md + GitHub)

1. **Org-scoping**: add `sprk_organization` lookup to `sprk_externalrecordaccess`; DTO `AccountId →
   OrganizationId`; bind it. (Currently accepted-but-not-persisted.)
2. **grantedby under SSO**: confirm systemuser resolution populates `sprk_grantedby` with a real
   workforce token (073 UAT).
