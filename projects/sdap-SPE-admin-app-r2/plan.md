# Implementation Plan — SDAP SPE Admin App R2

> **Status**: Ready for execution · **Created**: 2026-08-21 · **Source**: [`spec.md`](spec.md)
> **Branch**: `work/sdap-SPE-admin-app-r2` · **Draft PR**: [#811](https://github.com/spaarke-dev/spaarke/pull/811)
> **Epic**: Code Quality (#427) · **Surface**: BFF (`Sprk.Bff.Api`) + `src/solutions/SpeAdminApp/`

---

## 1. Objective

Make the SPE Admin app work against the current SharePoint Embedded platform. It has never been fully
functional: 4 of 9 screens fail outright, 1 fails silently, and the app reports success throughout.
**31 FRs** across six workstreams, ordered so each is independently shippable.

Explicitly **not** a decomposition of `SpeAdminGraphService.cs` — that ships as
`speadmingraphservice-decomposition-r1` once the Workstream D harness exists to make it safe (spec D5).

---

## 2. Architecture Context

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

### Discovered Resources (pipeline Step 2)

**ADRs — load per task via `<knowledge>`**

| ADR | Concise path | Relevance |
|---|---|---|
| **ADR-028** | `.claude/adr/ADR-028-spaarke-auth-architecture.md` | Auth v2 + **Amendments A1–A4**. **A4 + exception E-1 are decisive for Workstream B** — see spec ADR Tensions |
| **ADR-008** | `.claude/adr/ADR-008-endpoint-filters.md` | `SpeAdminAuthorizationFilter` guards the `/api/spe` group; a second auth path touches the pattern |
| **ADR-019** | `.claude/adr/ADR-019-problemdetails.md` | Workstream A error-surface contract |
| **ADR-007** | `.claude/adr/ADR-007-spefilestore.md` | No Graph SDK types leak above the facade |
| **ADR-038** | `docs/adr/ADR-038-testing-strategy.md` | Workstream D; scaffolding bans; mock at module boundaries |
| **ADR-001** | `.claude/adr/ADR-001-minimal-api.md` | Endpoint pattern for new Workstream E surfaces |
| **ADR-029** | `.claude/adr/ADR-029-bff-publish-hygiene.md` | NFR-01 publish-size ceiling |
| **ADR-010** | `.claude/adr/ADR-010-di-minimalism.md` | Constrains new `SpeAdminModule.cs` registrations |

**Constraints**

- `.claude/constraints/bff-extensions.md` — **binding** pre-merge checklist for every BFF addition
- `.claude/constraints/auth.md` · `.claude/constraints/api.md` · `.claude/constraints/testing.md`
- `.claude/constraints/azure-deployment.md` — publish-size per-task rule

**Patterns**

- `.claude/patterns/auth/obo-flow.md` · `oauth-scopes.md` · `token-caching.md` — Workstream B
- `.claude/patterns/auth/graph-sdk-v5.md` · `graph-endpoints-catalog.md` — Workstreams B/C/E
- `.claude/patterns/api/endpoint-definition.md` · `endpoint-filters.md` · `error-handling.md`
- `.claude/patterns/testing/integration-tests.md` · `mocking-patterns.md` · `unit-test-structure.md`

**Knowledge corpus**

- `knowledge/sharepoint-embedded/docs/` — `learn-containers.md`, `learn-containertypes.md`,
  `learn-overview.md`. ⚠️ Curated **2026-05-14**; predates every spec §4 finding. FR-X01 refreshes it.

**Canonical implementations to read before writing**

| Path | Use as |
|---|---|
| `Infrastructure/Graph/SpeAdminGraphService.cs` | The surface under repair (4,911 LOC) |
| `Services/SpeAdmin/SpeAdminTokenProvider.cs` | OBO exchange — the Workstream B target |
| `Infrastructure/Errors/ProblemDetailsHelper.cs` | Error construction path for Workstream A |
| `tests/integration/contract/Eval/*.cs` | `[Trait("Category", …)]` gated-suite convention |
| `CiamGraphClientFactory` | Reference for a certificate-based confidential client |

**Scripts**

- `scripts/Validate-TaskPoml.ps1` — task completeness lint
- `scripts/report-large-server-files.ps1` — non-blocking complexity observation

**Reuse wins (CLAUDE.md §11)**

- ✅ **`WireMock.Net 1.5.45` already referenced** in `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj:29`
  — Workstream D adds **no new package** (NFR-02).
- ✅ Repo convention is `[Trait("Category", "…")]`, **not** `[Category(…)]` as design.md wrote.

---

## 3. Scheduling reality — the god-file constrains parallelism

**This is the single most important planning fact.** Nearly every task in Workstreams A, B, C, and E
modifies `Infrastructure/Graph/SpeAdminGraphService.cs` (4,911 LOC, 90 public methods). Two agents editing
that file concurrently will conflict.

**Wave rule applied throughout**: *at most ONE task per wave may modify `SpeAdminGraphService.cs`.*
Every wave is built by pairing that one task with tasks that own **different** files — endpoint files,
the client app, test projects, Azure config, or notes-only work.

This caps realistic concurrency at 2–3 agents per wave rather than the 6-agent maximum. It is also a
concrete argument that the decomposition would have paid for itself here — but D5 resolved to defer it,
and reversing that mid-project would be worse. Recorded so the follow-on project inherits the evidence.

---

## 4. Phase Breakdown

### Phase 1 — Workstream A: Make failures visible (gates everything)

**Rationale**: until the app tells the truth, every later diagnosis starts from a false premise.
Task 001 establishes the error surface the other four consume.

| Task | Title | FR | Files owned |
|---|---|---|---|
| 001 | Real error surface via ProblemDetails | FR-A01 | `SpeAdminGraphService.cs`, `Api/SpeAdmin/**`, client error display |
| 002 | Audit 70 `catch (ODataError)` sites | FR-A02 | `SpeAdminGraphService.cs` |
| 003 | Sync Status reflects real per-concern outcomes | FR-A03 | `SpeDashboardSyncService.cs`, `DashboardEndpoints.cs`, client dashboard |
| 004 | Diagnose + fix Search | FR-A04 | `SearchContainersEndpoints.cs`, `SpeAdminGraphService.cs` |
| 005 | Diagnose + fix Audit Log | FR-A05 | `AuditLogEndpoints.cs`, `SpeAuditService.cs` |

**Deliverable**: no screen asserts an unestablished cause; the ODataError classification inventory exists.

### Phase 2 — Workstream B: Auth (🔔 GATED) + harness infrastructure

**Gate**: no task past 010 starts until the spec's §6.5 ADR Tensions decision is on the record.
It is (**path C — comply under ADR-028 E-1**), so the gate is satisfied *unless task 010 reopens it*.

**Deliberate deviation from spec §5 ordering**: task 040 (WireMock fixture infrastructure) is pulled
forward from Workstream D into this phase. It has no dependency on auth, and it is the mechanism that
catches the wrong-property-name defect class that Phase 3 exists to fix. Building it after Phase 3 would
mean fixing the properties unprotected. Rationale recorded per spec Workstream D.

| Task | Title | FR | Notes |
|---|---|---|---|
| 010 | **Spike**: prove owning-app delegated Graph token | FR-B01 | ⚠️ Carries an escalation trigger. Notes-only output |
| 011 | Wire hybrid delegated path for `containerTypes` | FR-B02 | Depends on 010 |
| 012 | Operator role prerequisite — actionable message | FR-B03 | Filter + client |
| 013 | Grant `SecurityEvents.Read.All`; verify Security screen | FR-B04 | Azure config, no code |
| 040 | WireMock Graph fixture infrastructure | FR-D01 | Test project only — parallel-safe with everything |

**Deliverable**: Container Types and Security work; a wrong property name can now fail CI.

### Phase 3 — Workstream C: Correct the API surface

Largest phase. Every task lands with WireMock coverage on the 040 infrastructure.

| Task | Title | FR |
|---|---|---|
| 020 | Migrate GA'd surfaces `/beta` → v1.0 | FR-C01 |
| 021 | Graph Endpoint setting — wire or delete | FR-C02 |
| 022 | Fix recycle-bin `$select` (`deletedDateTime`) | FR-C03 |
| 023 | Fix property names + quota-vs-consumption split | FR-C04, FR-C05 |
| 024 | **Spike + branch**: storage consumption | FR-C06 |
| 025 | Expand container-type settings to 9-property v1.0 shape | FR-C07 |
| 026 | Replication-pending + consuming-tenant override state | FR-C08 |
| 027 | Container-type owner management via `permissions` | FR-C09 |
| 028 | Container URL + Purview deep-link | FR-C10, FR-C11 |
| 029 | Surface `billingClassification` + `billingStatus` warning | FR-C12 |
| 030 | Model container-type lifecycle constraints in UI | FR-C13 |

### Phase 4 — Workstream D: Complete the harness

| Task | Title | FR |
|---|---|---|
| 041 | `[Trait("Category","LiveIntegration")]` suite + throwaway-container fixture | FR-D02 |
| 042 | Retire scaffolding-class tests per ADR-038 | FR-D03 |

### Phase 5 — Workstream E: New capabilities

| Task | Title | FR |
|---|---|---|
| 050 | Container archival (archive/restore + grid state) | FR-E01 |
| 051 | Per-container storage ceiling | FR-E02 |
| 052 | Per-container item recycle bin (`207` handled) | FR-E03 |

### Phase 6 — Hygiene + cross-cutting

| Task | Title | FR |
|---|---|---|
| 060 | Delete dead stub; relocate misfiled endpoints file | FR-F01, FR-F02 |
| 061 | Refresh `knowledge/sharepoint-embedded/` | FR-X01 |
| 062 | Raise billing-attach on `customer-provisioning-orchestration-r1` | FR-X02 |

### Phase 7 — Close

| Task | Title |
|---|---|
| 090 | Project wrap-up (+ mandatory `/test-diet` gate) |

---

## 5. Estimated Effort

| Phase | Tasks | Estimate |
|---|---|---|
| 1 — Workstream A | 5 | 3–5 days (004/005 open-ended — root causes not isolated) |
| 2 — Workstream B + D-infra | 5 | 3–4 days (010 spike is the risk) |
| 3 — Workstream C | 11 | 6–9 days |
| 4 — Workstream D | 2 | 2–3 days |
| 5 — Workstream E | 3 | 3–4 days |
| 6 — Hygiene | 3 | 1 day |
| 7 — Wrap-up | 1 | 0.5 day |
| **Total** | **30** | **~19–27 days** |

⚠️ **The estimate is provisional in two places**: FR-A04/A05 are uncapped fix commitments whose root
causes are not yet isolated, and FR-B01 is a spike that can reopen the auth decision. Both are called out
in spec Unresolved Questions.

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| **FR-B01 spike fails** — owning-app OBO shape proves unworkable | Escalation trigger on task 010. Re-run the §6.5 gate; the fallback (BFF-identity OBO) needs coordination with `spaarke-auth-v4-dataverse-MI`. Do **not** implement silently |
| **God-file serializes the project** (§3) | One-GraphService-task-per-wave rule; accept 2–3 agent concurrency |
| **BFF merge contention** — `customer-provisioning-orchestration-r1` (PR #779) and `spaarkeai-compose-r8` (PR #806) are live on BFF | `/conflict-check` before each PR. Ship per-phase PRs, not one mega-PR |
| **Destructive live tests hit real documents** | Task 041 fixture provisions and tears down its own throwaway container. Read-only/additive may use existing |
| **No safety net exists today** for Phases 1–3 | 040 pulled forward into Phase 2 (§ Phase 2 deviation) |
| **Storage spike returns "not exposed"** (FR-C06) | Two-branch FR — removal is a valid, pre-authorized outcome |

---

## 7. Coordination

- **`customer-provisioning-orchestration-r1`** (PR #779, BFF=Y, active) — receives the FR-X02 billing-attach
  handoff. Coordinate rather than merely avoid.
- **`spaarkeai-compose-r8`** (PR #806, BFF=Y, active) — pure merge-contention risk.
- **`chatendpoints-decomposition-r1`** — sequenced *after* this project per `projects/INDEX.md`.
- **`spaarke-auth-v4-dataverse-MI`** — no dependency under path C; task 010's reopen condition would create one.
- **`speadmingraphservice-decomposition-r1`** — follow-on; entry-gated on A–E merged + 040/041 green.

---

## 8. References

- [`spec.md`](spec.md) — 31 FRs, completed §6.5 ADR Tensions block
- [`design.md`](design.md) — verified current state, root causes
- [`notes/spe-platform-research-2026-08-20.md`](notes/spe-platform-research-2026-08-20.md)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task registry, waves, critical path
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding BFF checklist
