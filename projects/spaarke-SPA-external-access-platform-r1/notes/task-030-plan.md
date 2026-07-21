# Task 030 — CIAM external-access test suite: execution plan

> **Status**: NOT started (planned). 030 is the one Phase-2 task that needs a dedicated fresh-context
> session — it is an **integration/contract** effort through the HTTP surface, not quick unit tests.

## Why no unit-test shortcut (ADR-038 17 bans, tests/CLAUDE.md)
- `CiamUserProvisioningService.BuildCiamUser` is `internal static` → testing it via `[InternalsVisibleTo]`/reflection = **B8**.
- ctor-null tests = **B4**; DI-registration tests = **B3**; mocking the handler's own collaborators = **B5**;
  `Mock<HttpMessageHandler>` for the Dataverse/Graph HTTP = **B1**.
- ⇒ Tests MUST run through the HTTP surface via `WebApplicationFactory<Program>`, mocking only at
  **module-boundary interfaces**, placed under `tests/integration/contract/Api/ExternalAccess/**`
  (the test csproj auto-includes `../../integration/contract/**`). Model on
  `tests/integration/contract/Api/Compose/ComposeEndpointsContractTests.cs` + its `ComposeContractFixture`
  (WebApplicationFactory + test AuthenticationHandler + config keys; `ConfigureTestServices` + `RemoveAll`/`Replace`).

## Production testability seams needed FIRST (small, ADR-038-sanctioned "mock at module boundary")
1. **Download endpoint** (`ExternalProjectDataEndpoints.DownloadDocumentContent`, task 027): inject
   `ISpeFileOperations` instead of concrete `SpeFileStore` (interface already exists + is registered;
   `DownloadFileAsync` is on it) — so a spy can assert it is **never called** when unauthorized.
2. **`ExternalParticipationService`** (task 023): extract `IExternalParticipationService` OR make
   `ResolveExternalContactAsync` + `GetParticipationsAsync` `virtual` — so the fixture can control the
   `ExternalCallerContext` the filter builds (authorized vs no-project-access caller). Update the filter's
   `GetRequiredService<...>` + DI registration accordingly.
3. **`ExternalDataService`** (task 027): make `GetDocumentProjectAndNameAsync` `virtual` (or seam) — so the
   POSITIVE download test can control document→project scoping.
4. `IDocumentStorageResolver` is already an interface (spy-able) — no change.

## Fixture
- `ExternalAccessContractFixture : WebApplicationFactory<Program>` with:
  - The canonical config-key set (copy from `ComposeContractFixture`/`CustomWebAppFactory`) **plus the `Ciam:*`
    keys** (`Instance`, `TenantId`, `ClientId`, `Audience`, `Domain`, `GraphProvisioner:*`) so the `Ciam`
    JwtBearer scheme + `CiamGraphClientFactory` construct without throwing at startup (bff-extensions §F.2).
  - A test `AuthenticationHandler` issuing claims for BOTH the workforce default scheme (internal
    `/external-access` group) and a way to satisfy the `CiamExternal` policy for the external group
    (register a test scheme as `AuthSchemes.Ciam`, or add a per-test auth handler).
  - `CreateUnauthenticatedClient()` + `CreateAuthenticatedClient(claims)`.

## The 6 tests (KEEP path: endpoint-contract / security-auth)
1. `ExternalGroup_WhenUnauthenticated_Returns401` — GET `/api/v1/external/me` (+ download route) with no token → 401 (CiamExternal policy). *(No seams needed.)*
2. `Download_WhenCallerLacksProjectAccess_Returns403_AndNeverResolvesPointersOrReadsGraph` — **THE centerpiece**:
   caller context has no participation for `{projectId}` → 403; assert the `IDocumentStorageResolver` spy
   AND `ISpeFileOperations` spy were **NEVER** invoked (`Verify(..., Times.Never())`).
3. `Download_WhenDocumentNotInProject_Returns403` — caller has project access but the doc's `_sprk_project_value` ≠ URL project → 403 (scoping).
4. `Download_WhenAuthorized_Returns200_AndStreamsBytes` — access + doc-in-project + resolver returns pointers + `ISpeFileOperations` spy returns a byte stream → 200 with body bytes.
5. `InviteAndGrant_WhenReinvokedForBoundContact_CreatesNoSecondCiamAccount` — provisioner idempotency:
   fake the CIAM Graph seam / provisioner boundary; assert `POST /users` not called when the Contact's
   `sprk_externalobjectid` is already set. (May require an `ICiam...` seam on the provisioner — evaluate.)
6. `Grant_WritesAccessRecord_InvalidatesCache_AndNoSyntheticSpePermission` — `/grant` creates
   `sprk_externalrecordaccess`, invalidates the participation cache, and writes NO `contact_{guid}` SPE
   permission (broker-only, task 026).

## Bans checklist for every test authored
No `Mock<HttpMessageHandler>` (B1) · no DI/ctor tests (B3/B4) · no in-process-collaborator mocks except at
the module-boundary interfaces above (B5) · behavior-named `{Method}_{Scenario}_{Expected}` (B13) · assert
observable HTTP/side-effect behavior, not wiring (B6/B7/B9/B10). TEST-MODIFYING ⇒ Step 9.5 gates run
UNCONDITIONALLY. Do NOT extend the legacy `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalAccessEndpointTests.cs`
(it predates ADR-038 and is full of banned shapes).
