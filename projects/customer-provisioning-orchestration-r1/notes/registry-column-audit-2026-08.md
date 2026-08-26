# Registry Column Audit — `sprk_dataverseenvironment` v3.3 Extension

> **Task**: `023-extend-registry-schema-11-columns.poml`
> **Wave**: C1 (parallel-dispatched)
> **Date**: 2026-08-17
> **Owner**: customer-provisioning-orchestration-r1

---

## 1. Purpose

Records the authoritative list of 12 columns added to `sprk_dataverseenvironment` by task 023 (registry schema v3.3), the deployment mechanism chosen, the deviation from the POML's Entity.xml expectation, and the count reconciliation ("11 new columns" → "12 new columns") across project docs.

## 2. Executed column list (12 new)

All columns are **Optional** (RequiredLevel = None) so existing 16-column rows migrate cleanly per FR-26 acceptance. Populated by later handlers (H2a Bicep composition, H0 preflight, H12c runtime refs).

| # | Schema Name | Type | Length | Purpose | FR / Section |
|---|---|---|---|---|---|
| 1 | `sprk_azuresubscriptionid` | String | 100 | Azure subscription hosting this environment | FR-26 v2 (design.md §6.1) |
| 2 | `sprk_resourcegroupname`   | String | 200 | Resource group `rg-spaarke-{customerId}-{env}` | FR-26 v2 (§7.1) |
| 3 | `sprk_appservicename`      | String | 200 | BFF App Service `sprk-{customerId}-{env}-api` | FR-26 v2 (§7.1) |
| 4 | `sprk_keyvaultname`        | String | 200 | Customer KV per canonical naming (§7.9) | FR-26 v2 (§7.1) |
| 5 | `sprk_containertypeid`     | String | 100 | SPE container-type ID (distinct from `sprk_specontainerid`) | FR-26 v2 (§6.1) |
| 6 | `sprk_provisionedon`       | DateTime | — | H13 first-Ready timestamp; non-null → upgrade-mode | FR-26 v2 (§6.1) — **missed by FR-26 "11" count per discovery §9** |
| 7 | `sprk_currentrunid`        | String | 40 | Active ProvisioningRun ID; L2 optimistic-concurrency guard | FR-26 v3 (§4D I5) |
| 8 | `sprk_tenancymodel`        | Choice | — | `Model1Shared` (0) / `Model2Dedicated` (1); local option-set | FR-26 v3 (§3A A1) |
| 9 | `sprk_tenantid`            | String | 40 | Entra tenant ID (Spaarke tenant Model 1; customer tenant Model 2 via H0.5) | FR-26 v3 (D18 / §4D I1) |
| 10 | `sprk_bffversion`         | String | 50 | BFF semver pinned to env; H0 upgrade-mode version-compat matrix | FR-26 v3.3 (§14A) |
| 11 | `sprk_solutionversion`    | String | 50 | Dataverse solution semver pinned to env; §14A companion | FR-26 v3.3 (§14A) |
| 12 | `sprk_ClientCacheBustToken` | String | 100 | Cache-bust token for client bundle invalidation after upgrade (PascalCase per §7.9 grandfather) | FR-26 v3.3 (§7.9 / §14A) |

**Total post-extension**: 16 (v2 baseline) + 12 (v3.3) = 28 columns.

**ADR-044 canonicalization** applies to columns 1, 7, 9 (GUID-shaped). Storage is `String` because Dataverse has no native GUID-as-string attribute type with case-normalization; canonicalization to bare-lowercase remains the caller's responsibility per ADR-044 ("normalize at every boundary"). Column descriptions cite ADR-044 for downstream tracing.

## 3. Deployment mechanism (deviation from POML step 3)

**POML expected**: `src/dataverse/solutions/spaarke_core/Entities/sprk_dataverseenvironment/Entity.xml` + `Other/Solution.xml` update, PAC CLI unpack/pack round-trip.

**Codebase reality**:

- `src/dataverse/solutions/spaarke_core/**` contains ONLY `.gitkeep` files. No Entity.xml, no Solution.xml has ever been unpacked for `sprk_dataverseenvironment` in this repository.
- The v2 baseline (16 columns) was authored as a **PowerShell Web API script** (`scripts/Create-DataverseEnvironmentSchema.ps1`, 197 LOC) — this IS the operational pattern for this entity in this repo.
- Solution XML lives in `src/dataverse/solutions/spaarke_containers/**` for `sprk_Container` and `sprk_Document`, not `spaarke_core`.

**Deviation chosen** (per POML `<steps mode="directional">` — "if step 3 is wrong for what you find, do the right thing and note the deviation"):

- **Author `scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1`** mirroring the v2 baseline pattern. Same effect on the customer environment as an Entity.xml import would produce; produced by exactly the same Web API contract PAC CLI would emit.
- **Not authored**: Entity.xml / Solution.xml under `spaarke_core/**`.

**Rationale**:

1. **Operational consistency** — extending the same script pattern as v2 keeps a single deployment mechanism for this entity end-to-end. A `.ps1` extension + `.xml` fabrication would fork the truth source.
2. **Constraint safety** — recreating full Entity.xml from scratch (~2,800 lines for 28 columns) risks incidental mutation of the existing 16 columns, violating the project MUST NOT rule (`MUST NOT change the schema name / display name / data type of any of the existing 16 columns`).
3. **Parallel-dispatch safety** — task 023 runs as one of 6 concurrent sub-agents in Wave 1 Batch 1 against a shared org; `pac solution unpack` against a live org from a sub-agent risks concurrent-write contention. The Web API `POST /Attributes` is per-column atomic and idempotent.
4. **Idempotence** — the script pre-checks each attribute via `EntityDefinitions.../Attributes(LogicalName='...')` and skips if present. Re-runnable without side-effects.

**Acceptance-criterion mismatch**: acceptance criterion 1 says "unpack/pack round-trip via PAC CLI succeeds with 0 validation errors" — not applicable here because there is no solution XML to round-trip. The equivalent validation is that the script parses cleanly under PowerShell + the Web API calls succeed against the target org. Criteria 2, 3, 4, 5 are satisfied on their own terms (12 columns declared with exact schema names / description-with-FR-cite / option-set exactly 2 values / no data path consumers).

**Follow-on obligation**: consumers land in later tasks per POML step-2 constraint — H0 preflight (§14A version compat), H2a Bicep composition (§3A A1 tenancy), H12c runtime refs. `DataverseEnvironmentRecord.cs` (BFF POCO) will be extended in one of those later tasks, not here.

## 4. Doc-reconciliation scope ("11 new columns" → "12 new columns")

Task 023 owns this reconciliation (discovery §9 explicitly assigned; task 005 reconciled a different string — "10 → 11 of 14 null" — in commit `c2f576e11`).

**Updated in this reconciliation commit**:

- `projects/customer-provisioning-orchestration-r1/spec.md` (4 sites): Scope In-Scope §5, Affected Areas table, FR-26, New Components §11 gate
- `projects/customer-provisioning-orchestration-r1/plan.md` (7 sites): overview §, dataverse-create-schema pointer, Phase A cross-ref, Phase A deliverables, Wave C1 header, Wave C1 checklist, Wave C1 summary
- `projects/customer-provisioning-orchestration-r1/README.md` (1 site): overview blurb
- `projects/customer-provisioning-orchestration-r1/notes/resource-discovery-2026-08-16.md` (1 site): dataverse-create-schema skill pointer at line 104
- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (1 site): §"11 new columns added by this project"

**Intentionally NOT changed**:

- `notes/resource-discovery-2026-08-16.md` §9 (line 250) — this IS the discovery-report entry that documents the discrepancy and explains "spec says 11, enumerates 12". Removing that would remove the audit trail. Same for line 343 (Phase A reconciliation summary).
- Task POML files — those are historical artifacts and reference "11 new columns" in the context of the discrepancy they explain. Rewriting them would erase the reconciliation history.
- Root `CLAUDE.md` / project `CLAUDE.md` — neither contains the string "11 new columns" (confirmed by grep 2026-08-17).

Verification post-commit: `grep -rn "11 new columns" projects/customer-provisioning-orchestration-r1 docs/guides` returns only the intentionally-preserved sites in `notes/resource-discovery-2026-08-16.md` §9 + §Phase A summary and task POML.

## 5. Consumers (deferred to later tasks per POML step-2 constraint)

| Column(s) | Consumer task | Purpose |
|---|---|---|
| `sprk_currentrunid` | 059 (H2a I5 concurrency guard) | Optimistic-concurrency `null → newRunId` write; 409 on conflict |
| `sprk_tenancymodel` + `sprk_tenantid` | 044 (H2a Bicep composition) | Model1/Model2 stack selection; tenant-scoped Bicep params |
| `sprk_bffversion` + `sprk_solutionversion` | 041 (H0 preflight upgrade-mode) | Query version-compatibility matrix; block red-cell pairs |
| `sprk_ClientCacheBustToken` | 072 (H12c runtime refs) | H7 writes new value on upgrade; clients invalidate cached bundles |
| `sprk_azuresubscriptionid` .. `sprk_containertypeid` | 044 (H2a) + 055 (H13) | Bicep-set + H13-verify per-customer resource identifiers |
| `sprk_provisionedon` | 055 (H13 acceptance gate) | Set on first Ready transition; upgrade-mode preflight marker |

`DataverseEnvironmentRecord.cs` POCO extension: whichever of the above tasks first requires it will add fields + `AllColumns` entries + `MapFromJson` cases. Not this task.

## 6. Verification checklist (executed inline)

- [x] All 12 new columns enumerated with exact schema-name / type / length / required-level / description matching design.md §6.1 + §14A + §7.9
- [x] `sprk_tenancymodel` option-set exactly 2 values: `Model1Shared=0`, `Model2Dedicated=1` (label + description on each)
- [x] Every new column description cites its FR + section (per project MUST constraint)
- [x] No column collides with existing 16 (cross-checked against `DataverseEnvironmentRecord.AllColumns` and `Create-DataverseEnvironmentSchema.ps1`)
- [x] Script is idempotent (`Test-AttributeExists` pre-check per column; DateTime + Choice guarded)
- [x] Script mirrors v2 baseline conventions (headers, `Invoke-DV` helper, `New-Label` helper, `PublishXml` at end)
- [x] Deviation documented in this note + surfaced to reviewer in task-execute Step 9.5 output
- [x] Doc-reconciliation commit is a SEPARATE commit from the schema commit (per dispatcher instruction)

## 7. Test / build impact

- **BFF publish size**: zero delta (no `.cs` change; script is `.ps1`; audit note is `.md`).
- **HIGH CVE**: no NuGet touched.
- **CI signal**: `.ps1` PowerShell parses cleanly (verified via PowerShell AST parse in the wave-1 dispatcher build-verification step).
