# Task 015 — FR-22 module/widget-data framework generalization — notes & deviations

> Date: 2026-08-06 · Rigor FULL (opus @ xhigh) · BFF-touching (bff-api, auth)

## /conflict-check result
✅ SAFE (silent pass). Branch `work/spaarke-SPA-external-access-platform-r2`. No open PR touches
`Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `CallerPrincipalAuthorizationFilter`, or
`AuthPolicies`. The one non-dependabot BFF PR (#743 assistant-r2) is the AI cluster — no overlap.
teams-app-r1 is merged (stable base, its FR-22 files present on this branch), not a concurrent editor.

## §10 BFF Hygiene — Placement Justification (cite .claude/constraints/bff-extensions.md)
- **Where**: all new server code is in the external-access corner (`Infrastructure/ExternalAccess/`,
  `Api/ExternalAccess/`, `Infrastructure/DI/ExternalAccessModule.cs`) — the isolated external corner, not
  the AI/Compose/Communication cluster. Endpoints registered via the existing
  `MapExternalAccessEndpoints` path (not `Program.cs`).
- **No new AI dependency**: no `IOpenAiClient`/`IPlaybookService`/AI-internal types injected; no
  `Services/Ai/PublicContracts` needed (this is a Dataverse-read seam, not AI). No CRUD→AI edge added.
- **Feature-module DI (ADR-010)**: `ExternalModuleRegistry` registered as a **concrete singleton** (no
  interface — single impl; the pluggable seam is the per-module descriptor delegates). Reuses the existing
  app-only Dataverse read services (`FetchService`/`RecordService`/`MetadataService`/`SavedQueryService`)
  and `IFetchXmlEntityExtractor` — no new business services.
- **Authz (ADR-008)**: no global middleware. The read-data group is mounted UNDER `/api/v1/external`, so it
  inherits the group-level `RequireAuthorization(ExternalCollaboration)` (dual-scheme) +
  `CallerPrincipalAuthorizationFilter`. Per-record Tier-2 gate runs in-handler before any read.
- **No new package** → **no publish-size delta of note / no new CVE**. Publish (Release, incl PDBs,
  compressed) = **46.91 MB** vs 46.90 MB baseline → **+0.01 MB** (ceiling 60 MB; +5 MB escalation
  threshold not approached). `dotnet list package --vulnerable --include-transitive` → **no vulnerable
  packages**.
- **Tests (ADR-038)**: added unit + contract tests in the Sprk.Bff.Api.Tests assembly (see below); no
  `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests.

## How this GENERALIZES the shipped seam (does NOT redesign) — ADR-028 A3
The teams-app-r1 `CallerPrincipalResolver` + strategies + `CallerPrincipalAuthorizationFilter` +
`CallerPrincipal.ProjectAccess` + `AccessibleRecordSetService` are **unchanged** (CIAM byte-for-byte
preserved — the CIAM strategy/token validation are not touched; regression tests green). The task LIFTED
that seam into a per-module registration framework, purely additively:
- **`ExternalModuleDescriptor` + `ExternalModuleRegistry`** (new): a module registers
  `{ Name, RecordEntity, RecordIdAttribute, Tier-2 predicate }`. The predicate is a
  `Func<CallerPrincipal, IReadOnlySet<Guid>>` — the **plane-agnostic** NFR-08 record scope (A3: "per-module
  record-scope is a Tier-2 predicate composed into `CallerPrincipal.ProjectAccess`"). No plane branching.
- **`AddExternalModule(descriptor)`** (new DI extension): "add a module = one registration line", no
  route/filter/handler change (A3 canonical extension seam — the same shape as "add a plane = one
  `ICallerPrincipalStrategy` + one `DeterminePlane` branch"). The **collaboration/`sprk_project`** module is
  registered as the first module over the framework (its predicate = `CallerPrincipal.GetAccessibleProjectIds`).
  Task 016 registers matter/document/invoice/work-assignment the same way.
- **`ExternalModuleDataEndpoints`** (new): the read-data group satisfying the **BffDataverseClient**
  contract (fetch/record/metadata/savedquery/savedqueries), mounted at `/api/v1/external/api/dataverse/*`
  so a widget consuming it sets `bffBaseUrl = {host}/api/v1/external` with **no fork** of BffDataverseClient.
  All reads app-only (broker-only, no OBO, no Graph pointers). Data reads (fetch/record) are Tier-2-scoped
  by the module predicate; schema/view reads (metadata/savedquery) are fail-closed to registered entities.

## Quality gates (Step 9.5)
- **code-review**: 1 Critical + 4 Warnings + nits. **Critical C1 FIXED** — the fetch handler now rejects any
  FetchXml referencing an entity other than the module's own (via `IFetchXmlEntityExtractor`), closing the
  `<link-entity>` over-read hole (an external caller could otherwise join `systemuser`/`contact` to an
  accessible project and exfiltrate internal columns; the internal `/api/dataverse/fetch` defends this with
  a per-entity privilege filter that is unusable on the identity-less external plane). Warnings W1 (schema
  reads unscoped) and W2 (`RowMatchesEntity` fail-open) **FIXED** (fail-closed to registered entities /
  positive `@logicalName` confirmation). W3 (no endpoint tests) **FIXED** — added
  `ExternalModuleDataContractTests`. Nits N2 (logger category), N3 (assert via public surface), N4 (DI
  comment) **FIXED**. N1 (paging truncation) documented as intentional for R1 in code; N5 (500 detail wording)
  matches the sibling `ExternalProjectDataEndpoints` convention — left as-is.
- **adr-check**: **0 hard ADR violations.** COMPLIANT: ADR-001, ADR-008, ADR-010, ADR-019, ADR-028(+A1/A2/A3),
  ADR-038, bff-extensions §10 core. Warnings, resolved as follows:
  - **Tier-1 module-entitlement routability gate is not enforced (A3 MUST NOT)** → **Path A (documented
    project-scoped deferral).** A3 itself states the Tier-1 entitlement store + `/me` projection are **R2 P2
    deliverables** ("A3 fixes the invariant, not the schema"). No data over-exposure: Tier-2 is fail-closed
    and collaboration is currently the only module. Tier-1 direct-route deny lands in P2 (tasks 020–022).
  - **No rate limiting on the external read seam** → inherits the existing gap on the parent `/api/v1/external`
    group (not introduced here); flag for a follow-up rate-limit policy on the whole external group.
  - **ADR-019 `errorCode` vs `reasonCode` key inconsistency** → pre-existing in `ProblemDetailsHelper.Forbidden`
    (emits `reasonCode`); the new inline 400/404/500 use `errorCode` consistent with the sibling Dataverse
    endpoints. Left consistent-with-siblings; correlationId is carried via AuditEnrichmentMiddleware scope.

## Deviations
- **D-015-1 (over-read defense — stricter than the client contract):** the external fetch forbids ALL
  cross-entity `<link-entity>` joins (single-entity reads only). This is a deliberate security choice for the
  identity-less broker plane (Tier-2 row-scoping cannot vet joined columns). R1 grids get lookup labels from
  Dataverse formatted values without joins. A **per-module link-entity allow-list** is the named future
  extension seam if a widget needs a cross-entity column.
- **D-015-2 (BffDataverseClient path):** the group lives at `/api/v1/external/api/dataverse/*`; task 016 wires
  the widget's `BffDataverseClient` `bffBaseUrl` to `{host}/api/v1/external` (frontend concern; task 015 does
  not touch `src/client/external-spa/**`).
- **D-015-3 (fetch happy-path not contract-tested in-process):** `FetchService`/`RecordService` require a live
  `DataverseServiceClientImpl` ServiceClient, unavailable in `WebApplicationFactory`. The row-scoping happy
  path is covered by `ExternalModuleRegistryTests` (unit); the contract tests cover the security layer that
  runs before those services (auth gate, unregistered-entity fail-closed, single-entity restriction, Tier-2
  record deny).
- **D1 (workforce role→level grading) NOT implemented** — `WorkforcePrincipalStrategy.WorkforceProjectAccessLevel`
  (flat Collaborate) untouched; the grading extension point remains for F3/F5 (AC #6).

## Files
NEW:
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalModuleRegistry.cs`
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalModuleDataEndpoints.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalModuleRegistryTests.cs`
- `tests/integration/contract/Api/ExternalAccess/ExternalModuleDataContractTests.cs`
MODIFIED:
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs` (registry + AddExternalModule + collaboration module)
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalAccessEndpoints.cs` (+1 line: MapExternalModuleDataEndpoints)

## Verification
- `dotnet build src/server/api/Sprk.Bff.Api/` → **Build succeeded, 0 errors**.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/` → **9803 passed / 0 failed / 101 skipped** (was 9797; +6
  contract tests; +13 unit tests counted within). CIAM regression + all shipped external-access tests green.
- Publish (Release, compressed, incl PDBs) **46.91 MB** (+0.01 vs 46.90 baseline). No vulnerable packages.

## Acceptance criteria
1. Module registered with plane strategy + Tier-2 predicate → correct strategy resolves CallerPrincipal +
   module Tier-2 scopes the set; handlers/group filter unchanged — **MET** (registry + descriptor;
   ExternalModuleDataEndpoints; unit + contract tests).
2. CIAM byte-for-byte identical — **MET** (shipped CIAM strategy/resolver/filter untouched; regression tests green).
3. Negative Tier-2 (workforce authenticates but sees only composed set) — **MET** (ScopeRows/IsRecordAccessible
   fail-closed unit tests; `AccessibleRecordSetService` composition unchanged).
4. Widget read via BffDataverseClient path → resolver + Tier-2 scoped, app-only, no OBO/Graph pointers;
   non-participant → empty/denied; client not forked — **MET** (ExternalModuleDataEndpoints; contract tests:
   non-participant record → 403, link-entity → 400; ScopeRows non-participant → empty unit test).
5. Full suite green; no new HIGH CVE; publish ≤60 MB with delta reported — **MET** (9803/0; no vulnerable
   packages; 46.91 MB, +0.01).
6. D1 role→level grading NOT implemented; grading extension point left in place — **MET** (D-015 / D1 above).
