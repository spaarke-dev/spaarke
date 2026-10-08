# T218 — the complete Dataverse solution package (plan)

> **Created** 2026-10-07 (SESSION 43). Scope: plan §7 T218 (D7, D8, G11, G15) + the original T217/T218 scope
> (SESSION 22: (a)–(i)). This note holds the facts, the design and the split; the POMLs `218a`–`218e` execute it.

## Facts (checked 2026-10-07)

| # | Fact | Source |
|---|---|---|
| F1 | H6 imports a **9-solution catalog** (SpaarkeCore, SpaarkeWebResources, CalendarSidePane, DocumentUploadWizard, EventRibbons, EventDetailSidePane, EventsPage, LegalWorkspace, SpaarkeCorporateCounselApp). **6 of the 9 exist nowhere** (not in dev, not as source); `SpaarkeCorporateCounselApp` has no folder. | `SolutionImport/CanonicalSolutionCatalog.cs:51-107`; dev `solution` table |
| F2 | The catalog has **no managed/unmanaged flag** and H6 has no per-run choice; it imports whatever bytes the zip holds. The verifier checks presence only, not `ismanaged`. | `ISolutionCatalog.cs:107-111`; `DataverseWebApiSolutionVerifier.cs` |
| F3 | H6 reads `dataverse-solutions-latest.json` (`{"solutions":{"<name>":{"blobName","version"}}}`) from `sprkcpartifactsdev/provisioning-artifacts`. That store holds a **hand-uploaded `SpaarkeMaster.zip` + a hand-written manifest** (2026-08-21). | `DataverseWebApiSolutionImporter.cs:411-441`; handoff audit 2026-10-06 |
| F4 | `publish-dataverse-solutions-manifest.yml` lists **8** solutions, has no pack step (zips are gitignored), and cannot succeed on a clean checkout. No workflow packs a Dataverse solution. | workflow `:146-219`; `.gitignore:6,63` |
| F5 | `Deploy-DataverseSolutions.ps1` mirrors the 9-catalog; its "prefer managed" filter accepts every zip (`unmanaged` contains `managed`). H6 no longer calls it. | `:170-191`, `:443` |
| F6 | **This project already chose one managed solution, `SpaarkeMaster`** (2026-08-20, Wave H-3; Microsoft ALM pattern "single solution"), with three scripts: `Get-SpaarkeComponents` (inventory), `Assemble-SpaarkeMasterSolution` (adds missing components in dev, exports managed), `Test-SolutionCompleteness` (drift). v1.1.0.0 was exported 2026-08-21 and is installed managed in `spaarke-model1-prod`. H6 and the workflow never moved to it. | `docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md`; commit `88c330dc7` |
| F7 | **The membership-based scope misses content.** `Get-SpaarkeComponents` collected only components already inside a Spaarke-publisher solution, while most deploy scripts create components in the Default solution only. **CORRECTED 2026-10-07 (218c):** the first statement here — "`sprk_spaarkeai` and `sprk_dailyupdate` are in no Spaarke solution" — was WRONG: it came from a Dataverse query capped at 20 rows; both are in SpaarkeMaster (added 2026-08-21). The real evidence is the first rule run against dev: **44 in-scope components missing from SpaarkeMaster** — 11 PCF controls (incl. RecordHeader), the AI Setup app + site map, `sprk_assignedaccess`, the noaccessentry/assignedaccess/secure-child scripts, the Console User and Ontology roles, 17 OOB-table columns — and one excluded role packaged (Spaarke Provisioning Registry). | `Test-SolutionCompleteness.ps1` run 2026-10-07 (218c) |
| F8 | The repo holds **no deployable schema**: SpaarkeCore source = 7 environment-variable definitions; tables, roles, app module, forms, views exist only in dev or in ~28 hand-run scripts. | `src/solutions/SpaarkeCore/` |
| F9 | Roles: none in source. Dev has unmanaged `SpaarkeSecurityRoles` 1.0.0.1. H11's guest default `Spaarke Basic User` is not verified to exist; "Spaarke Office Add In User" and "Secure Record Owner" are created by hand per guides. | `H11UserProvisioningOptions.cs:51`; `office-addins-admin-guide.md:560-583` |
| F10 | Environment variables: H7 writes 7 values and fails `DefinitionNotFound` when H6 did not ship a definition. Dev has 21 `sprk_*` definitions. The 2026-08-21 SpaarkeMaster **packaged dev values** (component type 381) — dev URLs would land in a customer environment. | `H7DataverseEnvVarValuesHandler.cs:143-149`; `Build-SpaarkeMaster.ps1:295-306` |
| F11 | `sprk_noaccessentry` was created by hand in dev (SpaarkeCore); no script creates it; if missing, every secure read denies (fails closed). T256 / #1364. | `entity-schema.md:18-24,177-196` |
| F12 | ADR-027 §3 says "unmanaged everywhere; do not enforce managed" — contradicts D8. Docs disagree: release process = managed SpaarkeMaster; `production-release.md` = unmanaged SpaarkeMaster; deployment guide = 9 and 8 solutions. | `.claude/adr/ADR-027…md:53-64` |
| F13 | `src/solutions/CopilotAgent` is an **M365 declarative agent / Teams package** (`Deploy-CopilotAgent.ps1`), not Dataverse content. | — |

## Design (one recommendation each — implement unless the owner objects)

1. **One managed solution, `SpaarkeMaster`.** Reuse F6. The 9-solution catalog, `Deploy-DataverseSolutions.ps1` and the
   8-solution workflow retire. One solution keeps dependencies inside one import, one version, one upgrade.
2. **Scope by rule, not by membership (F7: 44 components missing on the first run).** The package = every unmanaged component with the `sprk` prefix in
   the authoring environment (spaarkedev1) — tables, columns, option sets, relationships, forms, views, web resources
   (incl. every code page), PCF controls, roles, app module + site map, environment-variable **definitions** — plus the
   `sprk_` columns on OOB tables (`docs/data-model/oob-customizations.yaml`), **minus** a committed exclusion list
   (`docs/data-model/package-scope.json`, one reason + date per entry; seeds: the PCFs excluded 2026-08-21,
   `sprk_SpaarkePlatform` + its site map, three legacy site maps, the Provisioning Registry role). The drift check fails both ways: a `sprk` component in dev that is
   neither in the package nor excluded, and a package component gone from dev.
3. **Git is the source of record.** Each release exports SpaarkeMaster from dev (managed + unmanaged) and unpacks it
   (`pac solution unpack --packagetype Both`) into `src/dataverse/solutions/SpaarkeMaster/`; the diff is reviewed in a
   PR. CI packs both zips from git — reproducible, no hand-built artifact. **No environment-variable values in
   source** (F10): the unpack step strips `environmentvariablevalues.json`, and a test fails if one appears; H7 owns values.
4. **Managed by default, unmanaged on explicit instruction (D8).** Intake `solutionPackageType` = `managed` (default) |
   `unmanaged`; stored on the run and the registry row. H6 imports the matching zip; the verifier checks `ismanaged`;
   H6 refuses (named, Resumable, nothing written) to switch type on an environment that already holds the other one,
   and refuses a downgrade (installed version > package version). ADR-027 §3 amended (§6.5 path B, owner-decided D8).
5. **Upgrade = re-run H6.** Existing → `StageAndUpgrade` (managed: components removed from the package are deleted in
   the customer environment — every release note lists removals); equal version → skip; values persist (never shipped).
   Ordering: **today H9 does not wait for H6** (`DagAdvancer.cs:162`), so on an upgrade run a new BFF can start
   against the old schema and fail on a new column until H6 finishes. 218b adds `H9 ← H6` (no cycle: H6 ← H5, H3, H10;
   H7 already waits for both). **The `sprk_bffversion` compat gate (old scope (i)) is dropped**: with H6 before H9 and
   a refused downgrade, no concrete failure is left for it.
6. **IAM.** H6 imports as the identity that is System Administrator in the customer environment (H10); the runbook
   states it. Roles ship in the package; H11's guest role default must name a shipped role; the hand-created roles
   (Office Add In User, Secure Record Owner) move into the package.
7. **Code pages** all ship inside SpaarkeMaster (rule 2). The per-page `Deploy-*.ps1` scripts stay as dev-iteration
   tools and never touch a customer environment. Folders with no runtime reference (`DemoRegistration` — two .md files;
   `sprk_communicationconversationpage` — comment-only reference) are confirmed with their owning projects, then
   excluded or removed.
8. **Per-customer Copilot agent (§9 Q2) is not Dataverse content (F13)** → its own task **T257** (an M365 package per
   customer, beside the add-in/Teams discussion in T240c), not part of the solution package.

## Split

| Task | Content | Live actions (owner OK each) |
|---|---|---|
| **218a** | ADR-027 §3 amendment (path B) + "Managed solution IAM + upgrade runbook" (replaces the contradicting docs: release process, production-release, deployment guide §H6, ci-cd-workflow) | none |
| **218b** | H6 → single package + D8 (intake `solutionPackageType`, run + registry row, manifest schema with managed/unmanaged blobs, verifier `ismanaged`, type-switch + downgrade refusals); DAG `H9 ← H6`; retire the 9-catalog, `Deploy-DataverseSolutions.ps1` and its mirror test | none |
| **218c** | Authoring scripts: prefix rule + exclusions file + two-way drift; `Export-SpaarkeMasterSource.ps1` (export both, unpack, strip values); env-value guard test | reading dev (read-only) — OK; adding components to SpaarkeMaster in dev — **ask** |
| **218d** | CI: rewrite `publish-dataverse-solutions-manifest.yml` → pack both zips from `src/dataverse/solutions/SpaarkeMaster`, upload + manifest to `provisioning-artifacts` | first publish run — **ask** |
| **218e** | Content gaps: roles into the package; H11 role default; `sprk_noaccessentry` (with T256); unused code pages; first source export (v1.2) committed | adding components in dev + export — **ask** |
| **T257** | Per-customer M365 Copilot agent | later |

Order: 218a → 218b (code, no live dependency) → 218c → 218e → 218d. T186 waits on all five.
