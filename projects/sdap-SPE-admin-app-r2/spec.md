# SDAP SPE Admin App R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-21
> **Source**: [`design.md`](design.md) (re-scoped 2026-08-20) · [`notes/spe-platform-research-2026-08-20.md`](notes/spe-platform-research-2026-08-20.md)
> **Epic**: Code Quality (#427) · **Lineage**: follow-on to `sdap-SPE-admin-app-r1`
> **Next step**: `/project-pipeline projects/sdap-SPE-admin-app-r2`

---

## Executive Summary

The SPE Admin app gives Spaarke admins a UI for SharePoint Embedded, which Microsoft otherwise exposes
only through Postman, PowerShell, and CLI. **It has never been fully functional** — a live walkthrough
against the Spaarke Dev tenant on 2026-08-20 confirmed four of nine screens fail outright and one fails
silently, while the app reports success throughout.

R2 makes it work on the *current* SPE platform: it surfaces real errors, resolves the app-only auth model
that makes one screen architecturally impossible, corrects an API surface written against properties that
do not exist, builds the Graph test harness that R1 recommended and never delivered, and adds three GA
capabilities the app does not model.

R2 is explicitly **not** the behavior-preserving decomposition of `SpeAdminGraphService.cs` it was
originally scoped as. None of the observed defects are caused by file size and none are fixed by splitting
the file; the decomposition ships as a follow-on once the harness exists to make it safe (design D5).

---

## Scope

### In Scope

**Workstream A — Make failures visible** (gates everything else)
- Real Graph/Dataverse errors surfaced through ProblemDetails; hardcoded misleading messages removed
- Audit of the 70 `catch (ODataError)` sites for absent-vs-failed conflation
- Sync Status reflects actual per-concern outcomes
- Search and Audit Log diagnosed **and fixed** (owner decision OC-02)

**Workstream B — Resolve the auth model**
- Delegated (OBO) path for the calls Graph does not support app-only, performed by the **per-customer
  owning app** (owner decision OC-01, auth-v4-consistent)
- Empirical spike first: the existing OBO path has two defects that mean it cannot currently work (F-2)
- Operator role prerequisite surfaced as an actionable message
- `SecurityEvents.Read.All` grant (Azure config; unblocks Security screen with no code change)

**Workstream C — Correct the API surface**
- `/beta` → v1.0 for GA'd surfaces; Graph Endpoint setting wired or deleted
- Three wrong property names fixed, including the quota-ceiling-vs-consumption semantic split
- Container-type settings expanded from 4 to the real 9-property v1.0 shape
- Replication-pending + consuming-tenant override state surfaced
- Container-type owner management, container URL, Purview deep-link, billing status warning
- Container-type lifecycle constraints modeled in the UI (immutability, no-conversion, 25/1-trial quota,
  conditional delete)

**Workstream D — Build the harness**
- WireMock-backed Graph mapping tests (request shape, response mapping, error translation)
- `[Trait("Category", "LiveIntegration")]` suite against a dedicated dev container type
- Scaffolding-class tests retired per ADR-038

**Workstream E — New capabilities**
- Container archival (up to 75% storage cost reduction)
- Per-container storage quota **ceiling**
- Per-container item recycle bin with restore + permanent delete

**Workstream F — Hygiene only** (the decomposition itself is out of scope, per D5)
- Delete the dead 3-line stub; relocate the misfiled endpoints file

**Cross-cutting**
- `knowledge/sharepoint-embedded/` corpus refresh (curated 2026-05-14; predates every §4 finding)
- Billing-attach capability raised on `customer-provisioning-orchestration-r1`

### Out of Scope

| Excluded | Home |
|---|---|
| **Information barriers / ethical walls / conflict-of-interest screens** | **Owner decision OC-03: not needed.** Removed from scope entirely — this amends design D4 from four capabilities to three. No follow-on filed. |
| **`SpeAdminGraphService.cs` decomposition** (23 nested types, 14 banner seams, `ForConfig` facade) | Follow-on `speadmingraphservice-decomposition-r1`, entry-gated on A–E merged + the Workstream D harness green (design D5) |
| **Billing-profile attach** (`Add-SPOContainerTypeBilling`, `New-SPOContainerType`) | `customer-provisioning-orchestration-r1` — PowerShell, needs Azure subscription owner/contributor, once-per-customer provisioning act. SPE Admin **reads** billing state only |
| **Legal hold / retention / eDiscovery management** | Microsoft Purview. Building it here duplicates an audited compliance surface with a narrower, unauditable one (design §4.2c). R2 routes admins to Purview and exposes the container URL instead |
| **SPE knowledge source in Foundry** (`sharePointEmbedded` Copilot Retrieval data source) | Separate AI-architecture evaluation against the existing RAG stack. Only the admin-*visibility* slice (`isSearchEnabled` / `isDiscoverabilityEnabled` / `agent.chatEmbedAllowedHosts`) is in R2, under FR-C07 |
| **Admin/runtime SPE stack convergence** (`SpeFileStore` / `DriveItemOperations` vs the admin stack) | Named follow-on. R2 must not *deepen* the split (see New Components) |
| **SPE agent SDK (`ChatEmbedded`)** | Deprecated March 2026; verified zero usage in `src/`. No action — recorded so nobody reaches for it |

### Affected Areas

| Path | Description |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs` | 4,911 LOC — the Graph interaction surface; primary target of A, B, C, E |
| `src/server/api/Sprk.Bff.Api/Services/SpeAdmin/SpeAdminTokenProvider.cs` | OBO token exchange; primary target of B |
| `src/server/api/Sprk.Bff.Api/Api/SpeAdmin/**` | 18 endpoint files; error-surface changes (A) + new capability endpoints (E) |
| `src/server/api/Sprk.Bff.Api/Models/SpeAdmin/**` | DTOs; new/changed shapes for C + E |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpeAdminModule.cs` | DI registration; note the flagged inline `DefaultAzureCredential` at `:50` |
| `src/server/api/Sprk.Bff.Api/Api/Filters/SpeAdminAuthorizationFilter.cs` | ADR-008 filter; touched only if B changes the auth posture through the route group |
| `src/solutions/SpeAdminApp/src/components/**` | 9 screens (`audit`, `bulk`, `containers`, `container-types`, `dashboard`, `files`, `recycle-bin`, `search`, `security`, `settings`) |
| `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/**` + `Integration/SpeAdmin/**` | 14 files; harness target (D) + ADR-038 retirement |
| `knowledge/sharepoint-embedded/` | Corpus refresh (X-01) |

---

## Requirements

### Workstream A — Make failures visible

> **Ordering constraint**: A lands before B, C, and E. Until the app tells the truth, every subsequent
> diagnosis starts from a false premise (design §2.4).

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-A01** | Replace hardcoded error strings with the real underlying error (code, message, request id) surfaced through ProblemDetails per ADR-019. Preserve a friendly summary; stop asserting a cause the code has not established. | The Container Types failure no longer says *"Check the app registration credentials in the config"* — it reports the Graph error actually returned. No user-visible message names a cause not present in the caught exception. |
| **FR-A02** | Audit all **70** `catch (ODataError)` sites in `SpeAdminGraphService.cs` that return `null` / `false` / empty. Distinguish "legitimately absent" from "call failed"; a failed call MUST NOT render as an empty grid. | Every one of the 70 sites is classified in a written inventory as absent-tolerant or failure-propagating, and its code matches its classification. A forced Graph failure on a list endpoint produces an error state, not an empty list. |
| **FR-A03** | Sync Status reflects actual per-concern outcomes rather than a static `OK`. | With any concern failing, the Dashboard Sync Status does not read `OK`; it names which concern failed. |
| **FR-A04** | **Search** screen: diagnose using the FR-A01 error surface, then **fix**. Leading hypothesis is the §3.1 app-only root cause (Graph `/search/query` app-only coverage is narrow), to be confirmed empirically, not assumed. | `SearchContainers` returns results against the Spaarke Dev tenant. Root cause recorded in task notes. |
| **FR-A05** | **Audit Log** screen: diagnose (Dataverse-side, not Graph), then **fix**. | Audit Log renders entries against the Spaarke Dev tenant. Root cause recorded in task notes. |

> **Owner decision OC-02**: FR-A04 and FR-A05 are **uncapped commitments to fix**, not "surface and
> defer". If either root cause proves to lie outside the BFF (e.g. a Dataverse schema or permission
> issue), that is a finding to resolve, not a reason to descope.

### Workstream B — Resolve the auth model

> 🔔 **GATED.** No FR in this workstream is executable before the §6.5 ADR conflict check in
> [ADR Tensions](#adr-tensions-per-claudemd-65) is completed with a named path decision. This is
> binding, not advisory (design §5.1).

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-B01** | **Spike (do first).** Empirically establish that a delegated Microsoft Graph token carrying `FileStorageContainerType.Manage.All` can be obtained via the per-customer owning app against the Spaarke Dev tenant. Two known defects must be resolved or ruled out: (a) `SpeAdminTokenProvider.cs:142` requests scope `api://{OwningAppId}/.default` — an owning-app audience — while the token is sent to `graph.microsoft.com`; (b) `SpeAdminTokenProvider.cs:306` builds the confidential client as `Create(config.OwningAppId)`, but MSAL OBO requires the incoming user assertion to be audienced to that same client, whereas the code page authenticates against the BFF. | A real `GET /storage/fileStorage/containerTypes` returns 200 with a delegated token, with the working token-acquisition shape documented. **Escalation trigger**: if the owning-app shape cannot work, STOP and re-run the §6.5 gate — the alternative (BFF-identity OBO) lands in ADR-028 A4 territory and requires coordination with `spaarke-auth-v4-dataverse-MI`. |
| **FR-B02** | Route the admin-role-requiring calls through the delegated path proven in FR-B01. Keep app-only where it is supported and correct (container CRUD, drive items) — this is a **hybrid**, not a wholesale migration. | Container Types list and create succeed against Spaarke Dev. Container CRUD continues to work app-only with no regression. The two paths are selected by a single documented rule, not scattered conditionals. |
| **FR-B03** | Surface the operator prerequisite explicitly. A missing role produces an actionable message **naming the role**. Note `SpeAdminAuthorizationFilter` checks Spaarke `Admin`/`SystemAdmin` claims — a *different* thing from the Entra **SharePoint Embedded Administrator** role this requires. | Signing in without the required role yields a message naming "SharePoint Embedded Administrator or Global Administrator", not a generic 403. |
| **FR-B04** | Grant `SecurityEvents.Read.All` on the app registration (Azure config; no code change). | Security screen renders secure-score/alert data against Spaarke Dev. |

### Workstream C — Correct the API surface

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-C01** | Migrate GA'd container-type surfaces from `/beta` to `/v1.0`. Three hardcoded sites: `SpeAdminGraphService.cs:925` (hand-built nextLink), `:4195`, `:4212`. Container-type **create** may remain on beta (no v1.0 equivalent) as a deliberate, isolated, documented exception. | No `/beta` literal remains except the create path, which carries an inline comment stating why. |
| **FR-C02** | The Settings **Graph Endpoint** field either takes effect or no longer exists. No third option. `ContainerTypeConfig` carries no endpoint field today and nothing reads the setting. | Changing the field changes the endpoint called, **or** the field is gone from the UI and its storage. |
| **FR-C03** | Fix the recycle-bin `$select`: `deletedDateTime` is not declared on `fileStorageContainer` (`:4351`), as a comment 11 lines below already states (`:4362`). | Recycle Bin no longer returns *"Could not find a property named 'deletedDateTime'"*; deleted containers list against Spaarke Dev. |
| **FR-C04** | Fix `majorVersionLimit` → **`itemMajorVersionLimit`** in the PATCH body (`:3940`). | A settings write verifiably persists, confirmed by read-back against a live tenant. |
| **FR-C05** | Fix `storageUsedInBytes` → **`maxStoragePerContainerInBytes`** (`:3945`) **and split the semantics**: the real property is a quota **ceiling**, not consumption. A limit control and a usage metric are different features and MUST NOT share a field. | The quota ceiling is a distinct, labeled control. No code path treats `maxStoragePerContainerInBytes` as consumption. |
| **FR-C06** | **Storage reporting spike + branch.** `StorageUsedInBytes: null` is hardcoded at `:645`, `:976`, `:1060`, `:1110` with the comment *"Not always returned by Graph."* Establish empirically whether Graph v1.0 exposes per-container consumption. **If yes** → implement real reporting. **If no** → remove the Dashboard tile and the Containers column. | Either real values render, or the tile and column are gone. **Never a silent `0 B` / `—`.** Spike result recorded. |
| **FR-C07** | Expand container-type settings to the real v1.0 shape — all nine properties: `urlTemplate`, `isDiscoverabilityEnabled`, `isSearchEnabled`, `isItemVersioningEnabled`, `itemMajorVersionLimit`, `maxStoragePerContainerInBytes`, `isSharingRestricted`, `consumingTenantOverridables`, `agent.chatEmbedAllowedHosts`. The app exposes four today, two under wrong names. | All nine are readable and (where writable) writable, verified by read-back. |
| **FR-C08** | Show **replication state** ("pending, up to 24h") and consuming-tenant **override** state on container-type settings. Without this, correct saves look like failures — a direct §2.4 trap. | After a settings save, the UI indicates replication is pending with the 24h expectation. Overridden settings are visibly marked. |
| **FR-C09** | Adopt the `fileStorageContainerType.permissions` relationship for container-type **owner management** (up to three owners). Supersedes part of the current ContainerTypePermissions screen. | Owners can be listed, added, and removed against Spaarke Dev. |
| **FR-C10** | **Expose container URL** in the Containers grid/detail — the eDiscovery scoping key, surfaced nowhere today. | The URL is visible and copyable per container. |
| **FR-C11** | Add in-app guidance that SPE compliance is governed in Microsoft Purview, with a deep-link to the compliance portal. An admin looking for "legal hold" should be routed, not stonewalled. | The deep-link is present on a relevant surface and resolves. |
| **FR-C12** | Surface `billingClassification` + **`billingStatus`**, and warn when billing is not valid. Invalid billing is a live operational failure mode with zero visibility today. **Read-only** — attach is provisioning's job. | Both fields render per container type; a non-`valid` `billingStatus` produces a visible warning. |
| **FR-C13** | Model the container-type lifecycle constraints in the UI: 1:1 owning-app coupling; container-type ID and owning app ID **immutable** (no edit affordance); **no** trial→production or standard→pass-through conversion, stated *before* submit; max **25** per tenant with at most **one** trial (show remaining quota, block at limit with a real message); **only trial** types deletable (conditional delete affordance). | Each constraint is enforced or communicated in the UI; none is discoverable only by failing. |

### Workstream D — Build the harness that should have existed

> R1 recommended this and never acted on it. The 359 tests across 14 SpeAdmin files make **no HTTP call
> and stand up no host**; `Phase2IntegrationTests.cs:1315` contains a passing test whose stated purpose is
> *"only to make the manual test plan visible in the test runner."*

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-D01** | WireMock-backed Graph tests over the mapping surface — request shape, response mapping, error translation. CI-runnable. This is where most of the value sits: most of the code is mapping, and this catches the entire §3.2 wrong-property-name defect class. | **A wrong property name fails CI.** Demonstrated by a deliberate regression test. Uses the existing `WireMock.Net 1.5.45` already referenced in `tests/unit/Sprk.Bff.Api.Tests/` — **no new package**. |
| **FR-D02** | `[Trait("Category", "LiveIntegration")]` suite against a real dev container type, for operations involving consent, registration, permission, and role flows. Nightly or manual gate — not in the default CI run. | Suite exists, is excluded from the default run by category, and passes against Spaarke Dev. **Destructive paths (delete, permanent-delete, recycle-bin purge) provision and tear down their own throwaway container** — never the existing ones, which hold real working documents. |
| **FR-D03** | Retire the DTO-shape tests and the manual-test-plan-as-passing-test per ADR-038's scaffolding bans. `/test-diet` classifies at project close. | `/test-diet` report is clean; `Phase2IntegrationTests.cs:1315` and its siblings are gone or reclassified. |

### Workstream E — New capabilities

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-E01** | **Container archival** (GA Feb 2026) — highest ROI on the list: up to **75% storage cost reduction** plus improved Copilot relevance. Archive/restore per container; archived state visible in the Containers grid. Note the per-container-type opt-in is PowerShell (**`Set-SPOContainerTypeConfiguration -ContainerTypeId <guid> -IsArchiveEnabled $true`**, SPO module ≥ 16.0.27515.12000 — *corrected 2026-08-27 by task 050; this row previously named `Set-SPOContainerType -IsArchiveEnabled`, a parameter that does not exist on that cmdlet*) — the app manages archived *containers*, not the opt-in. | Archive and restore both succeed against a Spaarke Dev container; archived state renders in the grid. **⚠️ Partially met as of 2026-08-27**: implemented and contract-tested, but NOT live-verified — the Spaarke Dev container type has not opted in and the opt-in is an operator action on shared infrastructure. Task 050's escalation trigger fired. See `notes/task-050-findings.md` §7. |
| **FR-E02** | **Real quota management** — `maxStoragePerContainerInBytes` as a per-container **ceiling** control, cleanly separated from consumption reporting per FR-C05/FR-C06. Gives legal customers per-matter storage caps. | A ceiling can be set and read back; it is presented distinctly from any usage display. |
| **FR-E03** | **Per-container item recycle bin** — `/storage/fileStorage/containers/{containerId}/recycleBin/items` with restore and permanent delete. This is the likelier admin intent behind a screen called "Recycle Bin"; the deleted-**containers** view (FR-C03) is retained alongside it (design D3 = both). | List, restore, and permanent-delete all work against a throwaway container. **`207 Multi-Status` partial success is handled explicitly** — per-item outcomes reported, not collapsed to pass/fail. |

### Workstream F — Hygiene only

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-F01** | Delete the dead 3-line stub `Services/SpeAdmin/SpeAdminGraphService.cs` (comment-only; the real implementation is in `Infrastructure/Graph/`). | File gone; build green. |
| **FR-F02** | Move `Api/ContainerItemEndpoints.cs` into `Api/SpeAdmin/` — it serves `/api/spe/containers/{id}/items/*` and is merely misfiled. | File relocated; routes unchanged; build green. |

### Cross-cutting

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-X01** | Refresh `knowledge/sharepoint-embedded/` per `knowledge/REFRESH-PROCEDURE.md`. Curated 2026-05-14; predates every §4 platform finding. At minimum `learn-containertypes.md`, `learn-containers.md`, `learn-overview.md`, plus new coverage of archival and the admin-center Apps experience. | Corpus reflects the v1.0 GA surface, archival, and the July 2026 admin-center experience. `REFRESH-LOG.md` updated. |
| **FR-X02** | Raise `Add-SPOContainerTypeBilling` + `New-SPOContainerType` as a provisioning-tooling requirement on `customer-provisioning-orchestration-r1` **before R2 closes**, so the capability lands somewhere rather than falling between the two projects. | Cross-project handoff recorded in that project's notes and referenced here. |

---

## Non-Functional Requirements

- **NFR-01** — **Publish size ≤60 MB compressed** (hard ceiling). Current baseline **~44.96 MB incl. PDBs**
  (44.05 MB excl.). Verify on every BFF-touching task; report absolute + diff. ≥+5 MB single-task delta
  requires explicit justification; ≥55 MB cumulative triggers architecture review.
- **NFR-02** — **No new NuGet packages.** Workstream D's WireMock dependency already exists. Any proposed
  addition requires the `.claude/constraints/bff-extensions.md` decision criteria stated explicitly.
- **NFR-03** — **No new HIGH-severity CVE** from `dotnet list package --vulnerable --include-transitive`.
- **NFR-04** — `dotnet build -c Release` **0 errors / 0 warnings** under the analyzer gate; ArchTests green.
- **NFR-05** — `/conflict-check` clean before each PR. The BFF is a contended hot path (13 of 17 active
  worktrees touch it). **Prefer several small PRs (A, B, C, D, E) over one atomic mega-PR.**
- **NFR-06** — No screen may report success while returning no data. This is the systemic defect R2 exists
  to correct and applies to every FR, not just Workstream A.
- **NFR-07** — Live-tier tests MUST NOT run destructive operations against containers holding real content.

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-028** (Spaarke Auth v2, +A1–A4) | Canonical auth architecture. **Amendment A4 (2026-08-17)** and exception **E-1** are decisive for Workstream B — see [ADR Tensions](#adr-tensions-per-claudemd-65) |
| **ADR-008** (Endpoint filters for auth) | `SpeAdminAuthorizationFilter` guards the whole `/api/spe` route group. Introducing a second auth path through that group touches the pattern |
| **ADR-019** (API errors + ProblemDetails) | Workstream A's error surface contract |
| **ADR-007** (SpeFileStore facade) | No Graph SDK types leak above the facade. Constrains E's new capability surfaces |
| **ADR-038** (Testing strategy) | Workstream D. Integration-heavy pyramid; scaffolding bans; coverage is observation never gate |
| **ADR-001** (Minimal API) | Endpoint pattern for any new E surface |
| **ADR-029** (BFF publish hygiene) | NFR-01 size ceiling and baseline ratchet |
| **ADR-010** (DI minimalism) | Constrains new registrations in `SpeAdminModule.cs` |

### MUST Rules

- ✅ **MUST** surface the real underlying error through ProblemDetails (ADR-019); **MUST NOT** assert a
  cause the code has not established.
- ✅ **MUST** keep the delegated exchange on the **per-customer owning app** identity (ADR-028 E-1), not
  the BFF identity — see ADR Tensions.
- ✅ **MUST NOT** add a new `.WithClientSecret` site for any client authenticating as the **BFF identity**
  (ADR-028 A4; E-3 "does not license expansion"). Per-customer owning apps are explicitly outside this.
- ✅ **MUST** use endpoint filters for authorization; **MUST NOT** add global auth middleware (ADR-008).
- ✅ **MUST NOT** leak Graph SDK types above the facade boundary (ADR-007).
- ✅ **MUST NOT** introduce `Mock<HttpMessageHandler>`; mock at module boundaries. WireMock at the HTTP
  boundary is the sanctioned mechanism (ADR-038).
- ✅ **MUST** verify publish size on every BFF-touching task (NFR-01).

### Existing Patterns to Follow

- `tests/integration/contract/Eval/*.cs` — the `[Trait("Category", "...")]` convention for gated suites.
  **Use `[Trait("Category", "LiveIntegration")]`**, not the `[Category(...)]` attribute design.md writes.
- `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj:29` — WireMock already referenced.
- `Infrastructure/Errors/ProblemDetailsHelper.cs` — the existing ProblemDetails construction path.
- `.claude/constraints/bff-extensions.md` — binding pre-merge checklist for any BFF addition.

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- Infrastructure/Graph/SpeAdminGraphService.cs + Api/SpeAdmin/** -->
  <spaarkeai>N</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement justification**: all work lands in the BFF because it *already* owns the SPE admin Graph
surface. R2 adds no new service boundary — it repairs and extends an existing one. Publish-size ceiling
(NFR-01) applies per task.

### New Components (§11 three-question gate)

| New component | Existing overlap (verified) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| **Delegated auth path** (FR-B01/B02) | `GetClientForOwningAppAsync` (`:448`) + `SpeAdminTokenProvider` already implement OBO exchange with per-`(configId + sha256(userToken))` caching | **Yes — extend/repair.** Both known defects are in the existing code, not absent from it | `GET /storage/fileStorage/containerTypes` does not support application permissions **at all**; Container Types returns an error permanently regardless of credentials |
| **Container archival surface** (FR-E01) | None found — no Spaarke equivalent | No — the capability does not exist in any form | Cold matter containers accrue a standing, compounding storage bill with no lever. Up to 75% of that cost is addressable |
| **Per-container quota ceiling** (FR-E02) | None — the field exists in Graph but the code misuses it as consumption | No | No per-matter storage control exists at all; a single matter can consume unbounded storage |
| **Item recycle bin surface** (FR-E03) | `deletedContainers` path exists but is a **different feature** (deleted containers ≠ deleted items) | No — different Graph resource, different admin need | Deleted *items* are unrecoverable through the admin tool; admins must fall back to PowerShell |
| **WireMock Graph fixtures** (FR-D01) | `WireMock.Net 1.5.45` already referenced in the target test project | **Yes — reuse the package.** No new dependency | The entire §3.2 wrong-property-name defect class ships undetected, as it did in R1 |

**Explicitly rejected**: a `SpeAdminDriveService` interface. `ISpeFileOperations` and `DriveItemOperations`
already abstract drive operations for the runtime document stack; a third abstraction over the same Graph
calls fails the extension test. The admin/runtime duplication is real but is a separate convergence
question, deliberately not deepened here.

**Coordination note**: `SpeAdminModule.cs:50` carries an inline `DefaultAzureCredential` that bypasses the
shared credential factory (no UAMI pinning), flagged by auth-v4's credential inventory. R2 touches this
file. Fix it opportunistically **only** if it does not expand scope; otherwise leave it to auth-v4 and note
it. Do not silently diverge from auth-v4's target shape.

---

## ADR Tensions (per CLAUDE.md §6.5)

> **This section satisfies the binding gate in `design.md` §5.1.** It is completed, not a placeholder.

### 🔔 ADR Conflict — Resolution Required

- **ADR in question**: **ADR-028** (Spaarke Auth v2, Amendment A4) — with **ADR-008** touched
  consequentially.
- **Specific rule challenged**: ADR-028 A4 — *"**MUST NOT** call `.WithClientSecret(...)` for any client
  authenticating as the BFF identity, outside exception E-3 (transitional) and E-1 (per-customer owning
  apps, which are *other* applications' identities)."* Plus the base MUST: *"**MUST** authenticate every
  confidential client that acts as the BFF identity … with a secret-free confidential credential."*
- **Conflict**: `GET /storage/fileStorage/containerTypes` does not support application permissions **at
  all** (design §3.1). Container Types cannot function under the current app-only posture regardless of
  credentials. Restoring it requires a delegated (OBO) exchange, which requires a confidential client —
  and the question is *whose identity* performs it.

- **Proposed path**: **C — pivot to comply.**

- **Rationale**: The tension largely **dissolves on inspection**, and `design.md` §5.1's premise is stale.
  1. `design.md` §5.1 states *"ADR-028 has no OBO exception documented."* That was true when written, but
     **Amendment A4 (2026-08-17)** added the OBO shape *and* retained exception **E-1 "SpeAdmin per-tenant
     container-type ops"**, explicitly exempting *"per-customer owning apps, which are other applications'
     identities"* from the no-secret rule.
  2. **`spaarke-auth-v4-dataverse-MI` has already scoped this out on exactly that basis**
     (`projects/spaarke-auth-v4-dataverse-MI/design.md:149`): *"`SpeAdminTokenProvider` and
     `SpeAdminGraphService` are **out of scope** — they authenticate per-customer owning applications, not
     the BFF identity (ADR-028 E-1)."*
  3. Therefore performing the delegated exchange **as the per-customer owning app** is already-sanctioned
     shape. It requires no exception and no amendment — it is compliance. This is also the choice that
     keeps R2 **consistent with auth-v4** (owner decision OC-01).

- **Impact**: None on ADR-028 — no exception is created and no amendment is needed. E-1's scope is
  unchanged (it already covers SpeAdmin per-tenant container-type ops). ADR-008 is unaffected: the
  existing `SpeAdminAuthorizationFilter` continues to guard the route group; the delegated path changes
  which *downstream* credential is used, not how the *inbound* request is authorized.

- **Alternatives considered and rejected**:
  - **BFF-identity OBO** — the BFF app registration performs the exchange. **Rejected**: lands squarely in
    A4 territory (MUST use MI-FIC or KV certificate, never a secret), triggers E-3's *"does not license
    expansion"* clause, and would pull SpeAdmin **into** auth-v4's migration scope — directly contradicting
    auth-v4's own decision to exclude it. Materially more expensive and creates a cross-project dependency.
  - **Path A (project-scoped exception)** — **Rejected as unnecessary.** An exception is only warranted
    when compliance produces a worse outcome; here E-1 already sanctions the needed shape.
  - **Path B (ADR amendment)** — **Rejected as unnecessary.** A4 (four days before this spec) already
    states the rule this project needs. Amending it again would add nothing.
  - **Defer Container Types to the SharePoint admin center** (design D1(b)) — a genuine ADR-compliant way
    to meet the requirement without touching auth. **Rejected 2026-08-20 on cost grounds**: D2 pays for the
    delegated path anyway, and §4.2b established that list is ownership-filtered (not admin-gated) and
    Graph create needs no admin role, making the screen far cheaper to restore than first assumed.

### Reopen condition (binding)

**FR-B01's spike is the trigger to reopen this decision.** Two verified defects mean the owning-app OBO
path **cannot currently succeed as written**:

1. `SpeAdminTokenProvider.cs:142` requests scope `api://{OwningAppId}/.default` — an owning-app audience —
   but the resulting token is handed to a Graph client (`SpeAdminGraphService.cs:4212`). Graph rejects on
   audience. Corroborated independently by auth-v4's `notes/CREDENTIAL-INVENTORY.md:22`.
2. `SpeAdminTokenProvider.cs:306` builds the confidential client as `Create(config.OwningAppId)`, but MSAL
   OBO requires the incoming user assertion to be audienced to that same client — while the SPE Admin code
   page authenticates against the BFF (`api://{bff-client-id}/SDAP.Access`).

Together these indicate the SPE-084 multi-app OBO path has **likely never executed successfully**,
consistent with design §1's finding that no test makes a real Graph call. **If FR-B01 establishes that the
owning-app shape cannot be made to work, this §6.5 block MUST be re-run** — the fallback (BFF-identity OBO)
is a path A/B decision requiring coordination with `spaarke-auth-v4-dataverse-MI`, not a silent
implementation choice.

**Corollary for planning**: Workstream B is *repair + wire*, not *route*. `design.md` §3.1's
characterization of the delegated machinery as a mitigating factor where *"the work is routing … not
building"* understates it, and task sizing must not inherit that optimism.

---

## Success Criteria

**Functional**
1. [ ] All nine screens work against the Spaarke Dev tenant, or are deliberately removed with rationale recorded — *Verify: live walkthrough, same method as the 2026-08-20 diagnosis*
2. [ ] No screen reports success while returning no data; storage is real or absent, never a silent `0 B` — *Verify: live walkthrough + FR-A02 inventory*
3. [ ] Every failure surfaces the actual underlying error; no hardcoded message asserts an unestablished cause — *Verify: forced-failure test per screen*
4. [ ] Settings **Graph Endpoint** field either takes effect or no longer exists — *Verify: change it and observe, or confirm absence*
5. [ ] Container-type settings writes verifiably persist — *Verify: write then read back against live tenant*

**New capabilities**
6. [ ] Archive/restore per container; archived state visible in the grid — *Verify: LiveIntegration test*
7. [ ] Per-container storage **ceiling** settable and distinct from any consumption display — *Verify: LiveIntegration test + UI inspection*
8. [ ] Item recycle bin: list, restore (`207` partial success handled), permanent delete — *Verify: LiveIntegration test against a throwaway container*
9. [ ] Container-type owner management via `fileStorageContainerType.permissions` — *Verify: LiveIntegration test*
10. [ ] Container URL exposed; Purview deep-link present — *Verify: UI inspection*
11. [ ] `billingClassification` + `billingStatus` surfaced with invalid-billing warning — *Verify: UI inspection*
12. [ ] Container-type settings show replication-pending + consuming-tenant override state — *Verify: UI inspection after save*
13. [ ] Billing *attach* raised on `customer-provisioning-orchestration-r1` — *Verify: cross-project note exists*

**Platform currency**
14. [ ] GA'd container-type surfaces call **v1.0**; create-on-beta recorded as a deliberate isolated exception — *Verify: grep for `/beta` literals*
15. [ ] Property names verified against current v1.0 schema — no repeat of the §3.2 class — *Verify: FR-D01 fixtures*
16. [ ] `knowledge/sharepoint-embedded/` refreshed — *Verify: `REFRESH-LOG.md` entry*

**Governance**
17. [ ] 🔔 ADR-028 §6.5 conflict check completed with a **named path decision** before any Workstream B implementation task, and cited in the PR description — *Verify: this spec's ADR Tensions section + PR body*

**Quality**
18. [ ] WireMock Graph coverage over the mapping surface; **a wrong property name fails CI** — *Verify: deliberate-regression test*
19. [ ] `[Trait("Category", "LiveIntegration")]` suite exists and runs green against a dev container type — *Verify: manual/nightly run*
20. [ ] Scaffolding-class tests retired per ADR-038; `/test-diet` report clean — *Verify: `/test-diet` at project close*
21. [ ] `dotnet build -c Release` 0/0 under the analyzer gate; ArchTests green; publish size neutral vs ~44.96 MB baseline; no new NuGet; no new HIGH CVE — *Verify: build + `dotnet publish` + `dotnet list package --vulnerable`*
22. [ ] `/conflict-check` clean before each PR — *Verify: skill output*

---

## Dependencies

### Prerequisites

- ✅ **Dev SPE environment — CONFIRMED 2026-08-21.** `spaarkedev1` ("Spaarke Dev", config "Spaarke PAYGO 1")
  with its container type and containers. Unblocks the live tier of Workstreams B and D.
  > ⚠️ **Destructive tests must use a dedicated throwaway container.** The existing containers hold real
  > working documents (signed NDAs, Compose drafts, matter files). Delete / permanent-delete /
  > recycle-bin-purge / restore paths provision and tear down their own container. Read-only and additive
  > operations may use the existing ones.
- `SecurityEvents.Read.All` grant on the app registration (FR-B04; Azure config).
- Delegated `FileStorageContainerType.Manage.All` granted and consented on the owning app (FR-B01/B02).

### External

- Microsoft Graph v1.0 + beta (container-type create only).
- Azure AD role assignment for tenant-wide container-type listing — **not a hard dependency**: per §4.2b
  listing is ownership-filtered and Graph create needs no admin role, so R2 functions without the
  SharePoint Embedded Administrator role. It only widens the view.

### Coordination

- **BFF hot path**: 13 of 17 active worktrees touch BFF. `/conflict-check` before each PR; prefer small PRs.
- `speadmingraphservice-decomposition-r1` (follow-on) is entry-gated on A–E merged + D harness green.
- `chatendpoints-decomposition-r1` sequences **after** this project per `projects/INDEX.md`.
- `spaarke-auth-v4-dataverse-MI` — no dependency under path C, but the FR-B01 reopen condition would create
  one. Monitor.
- `customer-provisioning-orchestration-r1` — receives the FR-X02 billing-attach handoff.

---

## Owner Clarifications

*Captured during the 2026-08-21 `/design-to-spec` interview:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| **OC-01 · Auth identity** | Which app registration performs the delegated Container Types exchange — per-customer owning app, or the BFF app registration? | *"This needs to run consistent with the auth-v4 work — so whichever option ensures consistency."* | **Per-customer owning app.** auth-v4 explicitly scopes `SpeAdminTokenProvider`/`SpeAdminGraphService` out of its MI-FIC migration as ADR-028 E-1 territory. Resolves the §6.5 gate as **path C (comply)** and avoids a cross-project dependency |
| **OC-02 · Workstream A scope** | Does R2 commit to *fixing* Search + Audit Log, or only to surfacing their real errors? | *"Commit to fixing both, uncapped."* | FR-A04 + FR-A05 are hard requirements with live-tenant acceptance criteria. Task count is open-ended by design; neither root cause is isolated yet |
| **OC-03 · Information barriers** | D4 made these conditional on a beta-risk review at spec time. Include, defer, or read-only? | *"We don't need ethical walls or conflict of interest functionality."* | **Removed from scope entirely** — not deferred, no follow-on filed. Amends design D4 from four capabilities to three. Workstream E is now FR-E01..E03 |
| **OC-04 · Storage tile** | Pre-commit to implementing storage reporting, pre-commit to removing it, or let a spike decide? | *"Spike decides."* | FR-C06 is a two-branch requirement gated on an empirical spike. Acceptance ("never a silent `0 B`") holds on either branch |

---

## Assumptions

- **Screen enumeration** — "nine screens" is taken as: `audit`, `containers`, `container-types`,
  `dashboard`, `files`, `recycle-bin`, `search`, `security`, `settings`. The `src/components/` tree also
  holds `bulk` (assumed to fold into Containers) and `layout` (not a screen). *Affects success criterion 1.*
- **Search root cause** — assumed to share the §3.1 app-only root cause, per the design's leading
  hypothesis. **To be confirmed empirically by FR-A04, not designed around.**
- **Container-type create stays on beta** — assumed no v1.0 equivalent exists at implementation time.
  Re-verify during FR-C01; if v1.0 has since shipped, migrate and drop the exception.
- **Graph create needs no admin role** — the Graph API *reference* page carries boilerplate stating an
  admin role is required, while the *conceptual* doc contradicts it for create. The design treats the
  conceptual doc as authoritative. **FR-B01 verifies empirically before anything is designed around it.**

---

## Unresolved Questions

- [ ] **Can the owning-app OBO shape actually obtain a Graph-audienced token?** — *Blocks: all of Workstream
  B beyond FR-B01, and the §6.5 path-C resolution. This is the single highest-risk unknown in the project.*
- [ ] **Does Graph v1.0 expose per-container storage consumption?** — *Blocks: FR-C06 branch selection and
  the Dashboard storage tile's existence.*
- [ ] **Is per-container hold/retention state queryable** for a read-only posture column? — *Blocks:
  nothing (explicitly out of scope). Recorded because design §4.2c flags it as unverified; do not assume.*
- [ ] **Search and Audit Log root causes** — deliberately not pre-scoped; they fall out of Workstream A.
  *Blocks: task sizing for FR-A04/FR-A05, which cannot be estimated until A lands.*
- [ ] **Does the operator hold the SharePoint Embedded Administrator role?** — *Blocks: nothing. Per §4.2b
  R2 does not depend on it; it only widens container-type listing from owned to tenant-wide.*

---

*AI-optimized specification. Original design: [`design.md`](design.md). Generated by `/design-to-spec` 2026-08-21.*
