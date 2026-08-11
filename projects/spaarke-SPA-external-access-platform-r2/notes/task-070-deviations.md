# Task 070 — Polymorphic external grant-WRITE (BFF) — deviations & decisions

> 2026-08-10 · FULL rigor · opus @ xhigh · Companion (write-side) to task 028's polymorphic reads.
> Branch `work/spaarke-SPA-external-access-platform-r2`. `/conflict-check` clean (no open PR touches
> `Api/ExternalAccess/**` or `Infrastructure/ExternalAccess/**`; teams-app-r1 merged/stable).

## What shipped (code)

- **New**: `Infrastructure/ExternalAccess/ExternalGrantRoot.cs` — `ExternalGrantRootType` enum
  (Project/Matter/WorkAssignment) + static `ExternalGrantRoot` helper: `BindFor(type)` → the
  `@odata.bind` nav-property + entity set, and `TryParse(raw)` for the wire `recordType` token
  (case/hyphen/underscore tolerant, fail-closed on unknown). No new interface (ADR-010).
- **`Dtos/GrantAccessRequest.cs`** — added optional `RecordType` (string) + `RecordId` (Guid?).
  Legacy `ProjectId` retained as back-compat shorthand for a project root.
- **`Dtos/InviteExternalUserRequest.cs`** — added optional `RecordType` + `RecordId` (threaded to
  the grant core by `/invite-and-grant`; unused by `/invite`).
- **`GrantExternalAccessEndpoint.cs`** — `ResolveGrantRoot(request)` (precedence: explicit
  recordType+recordId > legacy projectId; fail-closed otherwise) + `BuildGrantPayload` generalized to
  bind exactly ONE typed root lookup per record type. `CreateGrantAsync` now takes the resolved
  `(rootType, rootId)`. `BuildGrantPayload`/`ResolveGrantRoot` made `internal` (InternalsVisibleTo) for
  direct payload-contract tests.
- **`InviteAndGrantExternalUserEndpoint.cs`** — resolves the root up-front (400 before any onboarding
  side effect), threads it through `CreateGrantAsync`.
- **`RevokeExternalAccessEndpoint.cs` / `InviteExternalUserEndpoint.cs`** — dropped the
  `ProjectId`-required 400 (revoke deactivates by AccessRecordId; invite only onboards). DTO field kept
  for back-compat.
- **`ProjectClosureEndpoint.cs`** — **bug fix**: cascade-revoke filter `_sprk_projectid_value` →
  `_sprk_project_value` (the prior field name is invalid → matched zero rows, so close-project silently
  revoked nothing). Extracted `BuildActiveProjectGrantsFilter(projectId)` (internal) for regression test.

## Escalation trigger — did NOT fire (verification record)

Trigger: "if the live @odata.bind nav-property name for matter/WA differs from the owner-provided
`sprk_matterid`/`sprk_workassignmentid`, or a matter/WA grant write fails live — STOP."

- **Schema existence** verified live via `mcp__dataverse describe tables/sprk_externalrecordaccess`:
  lookups `sprk_project` / `sprk_matter` / `sprk_workassignment` all present, correct target tables.
- **Nav-property convention** proven TWICE on this exact table by the shipped grant code:
  `sprk_contact` → `sprk_contactid@odata.bind` and `sprk_project` → `sprk_projectid@odata.bind`
  (attribute `sprk_X` → nav property `sprk_Xid`). Owner independently confirmed `sprk_matterid` /
  `sprk_workassignmentid` in `polymorphic-grant-authoring-enhancement.md` (locked decision #5).
- **Definitive faithful check** = a live matter + WA grant write through the deployed 070 endpoint
  (MCP `create_record` normalizes lookups and would NOT faithfully test the raw `@odata.bind` string,
  so it was not used). This is the Step-8 deploy smoke test; the trigger stays ARMED for it — a live
  matter/WA grant failure STOPS and escalates before marking 070 complete.

## Discovered pre-existing latent bugs (OUT OF SCOPE — not fixed here)

The back-compat constraint requires the **project grant payload to stay byte-identical**, so these
pre-existing field-name issues in `BuildGrantPayload` were left untouched and are recorded for `/defer`:

1. **`sprk_expirydate`** written, but the live field is **`sprk_expiresdate`** (DATE ONLY). A grant
   with `ExpiryDate` set would 400. (Only fires when expiry is supplied — teams-app-r1 evidently never
   exercised it.)
2. **`sprk_accountid@odata.bind`** written, but `sprk_externalrecordaccess` has **no account lookup**
   at all (describe confirms). A grant with `AccountId` set would 400. Needs owner intent — is
   `AccountId` meant to persist somewhere, or vestigial? Cannot be a mechanical rename.

Recommend filing both via `/defer` (concrete failing behavior each). Not fixed in 070 to honor scope +
byte-identical back-compat.

## Verification

- `dotnet build` clean (0 errors). Full BFF test project: **10293 passed / 0 failed / 101 skipped**
  (+34 new in `PolymorphicGrantWriteTests.cs`, 3 existing updated for the relaxed contracts).
- Publish (Release, linux-x64): **48.44 MB compressed incl. PDBs** — Δ0.00 vs task-028 baseline; ≤60 MB.
- `dotnet list package --vulnerable --include-transitive`: **no vulnerable packages**.
- Quality gates (Step 9.5): code-review PASS (0 Critical), adr-check PASS (0 violations).

## §10 Placement Justification

No new endpoint/service/package/DI registration. The change GENERALIZES the existing external-access
grant-write endpoints (project-only → Project/Matter/WorkAssignment) and fixes a filter bug — the
write-side mirror of task 028. It belongs in the BFF alongside the reads it complements. Publish-size
Δ0, no CVE, tests added → all §10 pre-merge checks satisfied.

## Deploy status

BFF deploy from worktree (`scripts/Deploy-BffApi.ps1` → spaarke-bff-dev) + live matter/WA grant
smoke test (escalation gate) is the remaining Step-8 action — owner-gated (outward-facing Azure deploy
+ live Dataverse writes to shared dev). See current-task.md NEXT ACTION.
