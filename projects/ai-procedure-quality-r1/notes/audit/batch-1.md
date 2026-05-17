# Phase 2a Audit — Batch 1 (Skills 1–6 alphabetically)

> **Task**: 020-audit-skills-batch-1
> **Date**: 2026-05-15
> **Scope**: `add-reference-to-index`, `adr-aware`, `adr-check`, `ai-procedure-maintenance`, `azure-deploy`, `bff-deploy`
> **Method**: Read-only structural assessment against the 7 best practices (spec.md F-1 + F-15.x) plus Light reference check (F-15.3 mandatory) plus Medium reference check where the skill is ops-heavy.
> **Honesty**: Findings below reflect the actual state of each skill. No problems fabricated.

---

## 1. `add-reference-to-index` — 171 lines

### Structural Assessment

- **description-quality**: PASS. The single-line description ("Index golden reference documents into the spaarke-rag-references AI Search index") is specific and verb-leading. However, the trigger phrases (lines 16–20: "add reference to index", "index reference document", "add golden reference", "index knowledge source") are only inside the body — the frontmatter `appliesTo:` repeats them but `description:` doesn't lead with them. Minor.
- **body-line-count**: **PASS** (171 lines, under 200). Well-scoped.
- **goal-oriented vs prescriptive**: Mostly prescriptive — gives step-by-step "Step 1, Step 2, Step 3" plus exact PowerShell invocations. For an indexing operation this is appropriate; not over-prescribed.
- **Gotchas section**: **MISSING**. No `## Gotchas` heading. There is an "Error Handling" table (lines 158–165) that partially covers this surface but does not match the canonical Gotchas convention.
- **overlap with other skills**: None significant. The skill is the only one that talks about `spaarke-rag-references` AI Search indexing. No conflict surface.
- **deterministic-rules**: None obvious that would belong in settings.json/hooks. The "ensure `az login`" prerequisite is operational, not deterministic.
- **references current**: References to `scripts/ai-search/*.ps1` files, `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs`, and `infrastructure/ai-search/spaarke-rag-references.json` all resolve (verified below).
- **frontmatter shape oddity**: The H1 (`# add-reference-to-index`) appears on line 1 BEFORE the YAML frontmatter delimiters. Most other skills have `---` first. Not broken, but inconsistent with template.

### Light reference check (F-15.3 mandatory)

- `scripts/ai-search/Add-ReferenceToIndex.ps1` — **PASS** (exists)
- `scripts/ai-search/Index-AllReferences.ps1` — **PASS**
- `scripts/ai-search/Setup-KnowledgeDeliveryType.ps1` — **PASS**
- `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs` — **PASS**
- `scripts/seed-data/Deploy-Knowledge.ps1` — **PASS**
- `infrastructure/ai-search/spaarke-rag-references.json` — **PASS**
- AI Search REST URL (`https://spaarke-search-dev.search.windows.net/...`) — not HEAD-checked (record-only per F-15.3)
- `references-verified`: **pass**

### Medium reference check

Skill is moderately ops-heavy (prescribes PowerShell commands + curl/REST). Spot-check: `az search admin-key show` syntax matches current Azure CLI; `az cognitiveservices account keys` syntax current; PowerShell `-DryRun` flag usage is plausible (script not opened — assumed correct). No red flags. **Not** flagged `needs-substantive-rewrite`.

### Cross-reference impact

`add-reference-to-index` is the **sole orphan** in the inventory: `total_references: 0`. No skill, no `CLAUDE.md` trigger map, no settings.json, no project CLAUDE.md, no docs, no task POML references this skill. It is reachable only by direct user invocation.

This is the only flagged orphan in the entire inventory. Implications:
- Removal cost: Zero. Nothing breaks.
- Retention cost: Maintenance burden continues for a skill nobody calls.
- The skill is genuinely useful for a real, narrow workflow (RAG index seeding). Decision is: do we expect this workflow to recur enough to keep the skill?

### Exemplar recommendation

`exemplar: none-too-volatile` with rationale — the AI Search index schema (`spaarke-rag-references`) is a live infrastructure resource. Maintaining a known-good rebuild loop would require keeping a stable test document + expected chunk count + expected embedding dimensions in sync with whatever the OpenAI deployment is producing. Maintenance cost > value.

### Recommended action

**`refine-in-place`** — Two minor fixes belong in Phase 2b: (a) add `## Gotchas` heading (move/expand Error Handling), (b) add `last-reviewed:` frontmatter and `> **Last Reviewed**: YYYY-MM-DD` stamp, (c) standardize frontmatter to put YAML at top before H1.

**Do NOT remove**: Orphan status is a function of low call frequency, not low value. The RAG-index seeding workflow is real and used episodically. Removing it would simply mean re-deriving the workflow next time it's needed.

---

## 2. `adr-aware` — 270 lines

### Structural Assessment

- **description-quality**: PASS. "Automatically load and apply relevant ADRs when creating or modifying resources" — clear, action-leading.
- **body-line-count**: **OVER-TARGET-justified** (270 lines, between 200 and 400). The body is mostly reference tables (resource-type → ADR mapping, ADR quick-reference). Information density is high; splitting into subdirs would harm utility because the mapping is consulted as a single lookup table. Justified.
- **goal-oriented vs prescriptive**: Good balance. Rules 1–4 describe what to load and when; the actual loading is the agent's job. Example sections (Examples 1–3) are pedagogical rather than prescriptive.
- **Gotchas section**: **MISSING**. The skill has "ADR Violation Prevention" (Rule 4) which is gotcha-adjacent but not a dedicated Gotchas heading.
- **overlap with other skills**: Has explicit BEFORE/AFTER pairing with `adr-check` (line 17–18). The boundary is clean and intentional, not an overlap to merge.
- **deterministic-rules**: The resource-type → ADR mapping (Rule 1) is deterministic and could theoretically be a YAML data file consumed by a hook. However, it's read by the agent as context, not as automation, so it's correctly inline. Not a candidate for extraction.
- **references current**: All constraint files (`api.md`, `pcf.md`, `auth.md`, `ai.md`, `data.md`, `plugins.md`, `testing.md`) verified present. All pattern directories (`api/`, `pcf/`, `auth/`, `ai/`, `dataverse/`, `caching/`, `testing/`) verified present. ADR-001 through ADR-021 in the quick-reference table — not individually verified for existence in `.claude/adr/` (out of scope of Light check; reasonable to trust the inventory).
- **Note**: The `jobs.md` constraint is referenced in the second mapping table (line 90) but NOT in the first (line 57–63). Minor inconsistency.

### Light reference check (F-15.3 mandatory)

- All 8 constraint file paths — **PASS**
- All 7 pattern directories — **PASS**
- Referenced skill `adr-check` — **PASS** (exists)
- Referenced skill `design-to-project` (line 169) — **FAIL**: no such skill exists in inventory. Should be `design-to-spec` (renamed long ago). Stale reference.
- Referenced skill `task-create` — **PASS**
- Referenced skill `code-review` — **PASS**
- `references-verified`: **fail** (one stale skill name: `design-to-project` → `design-to-spec`)

### Medium reference check

Not heavily ops-prescriptive — most of the body is tables and rules, not commands. The C# code example (lines 99–111) is a documentation pattern, not a runnable command. The example in lines 200–211 uses `MapGet` + `AddEndpointFilter<DocumentAuthorizationFilter>` — this matches the current BFF pattern in `Program.cs`. No drift detected.

### Cross-reference impact

`adr-aware` has 26+ see-also references (truncated in JSON output) from across the skill set — task-execute, task-create, project-setup, project-pipeline, ribbon-edit, pcf-deploy, dataverse-deploy, design-to-spec, project-continue, ai-procedure-maintenance, and itself. It is a **hub-class** skill though not in the top-5 hubs list. Any rename/restructure must update all 26+ sites. Refine-in-place is safer than restructure.

### Exemplar recommendation

`exemplar: none-too-volatile` with rationale — `adr-aware` doesn't prescribe a build/deploy command; it prescribes a context-loading discipline. No artifact to rebuild and compare. Quarterly manual review of the mapping table is the right cadence.

### Recommended action

**`refine-in-place`** — Three improvements: (a) fix stale `design-to-project` reference (line 169) to `design-to-spec`, (b) add `## Gotchas` heading (include the ADR-conflict prevention rule formalization), (c) reconcile the two mapping tables (Rule 1 vs Resource Type to Context Files Mapping) so `jobs.md` appears in both, (d) add `last-reviewed:` frontmatter + audit stamp.

---

## 3. `adr-check` — 270 lines

### Structural Assessment

- **description-quality**: PASS. Clear and specific.
- **body-line-count**: **OVER-TARGET-justified** (270 lines). Most of the over-200 content is the CI/CD Integration section (lines 198–260) which is genuinely useful, ops-relevant, and not duplicated elsewhere. Splitting would hurt.
- **goal-oriented vs prescriptive**: Mixed — the 5-step Workflow at the top is prescriptive (appropriate for a validation skill), the Output Format section is template-spec (appropriate). Acceptable.
- **Gotchas section**: **MISSING**. The "Error Handling" table (lines 181–186) covers two situations, not gotchas-class issues.
- **overlap with other skills**: Intentional BEFORE/AFTER pairing with `adr-aware`. Clean.
- **deterministic-rules**: The grep patterns for each ADR are deterministic and could be extracted into a script (`adr-validation-rules.md` in references/ is already a beginning). The skill already has the right structure to push more detail into references/.
- **references current**: `docs/adr/README-ADRs.md` — **verified present**. `tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` — **verified present**. `.github/workflows/sdap-ci.yml` and `.github/workflows/adr-audit.yml` — **verified present**.
- **frontmatter shape**: Minimal (only `description` + `alwaysApply`). No tags, techStack, appliesTo. Inventory flagged this. Normalization is a Phase 2b candidate.
- **Tip duplication bug**: Lines 265–266 say the same thing twice ("Be thorough: check all ADRs in the ADR index even when changes seem small" then immediately "Be thorough: check all ADRs in the ADR index, even when changes seem small"). Copy-paste error.

### Light reference check (F-15.3 mandatory)

- `docs/adr/README-ADRs.md` — **PASS**
- `references/adr-validation-rules.md` — **PASS** (verified file exists in subdirectory)
- `tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` — **PASS**
- `.github/workflows/sdap-ci.yml` — **PASS**
- `.github/workflows/adr-audit.yml` — **PASS**
- Referenced skill `code-review` — **PASS**
- Referenced skill `push-to-github` — **PASS**
- Referenced skill `ci-cd` — **PASS** (skill exists, though ci-cd itself has frontmatter issues per inventory)
- `references-verified`: **pass**

### Medium reference check

`gh pr checks | grep -i "code-quality"` — current syntax valid. `dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` — verified the csproj exists; haven't run it, but path resolves. `gh workflow run adr-audit.yml` — valid CLI syntax. No anomalies.

### Cross-reference impact

`adr-check` is a **top-5 hub** (209 references). Used by code-review, push-to-github, ci-cd, doc-drift-audit, repo-cleanup, project-setup, spaarke-conventions, task-create, plus many CLAUDE.md and docs references. Any structural change must coordinate broadly. Refine-in-place is strongly preferred.

### Exemplar recommendation

`exemplar: tests/Spaarke.ArchTests/` — the NetArchTest project IS the canonical exemplar of "what an ADR check actually enforces." A skill that runs against it is auditable. Naming this exemplar gives the skill a verifiable anchor.

### Recommended action

**`refine-in-place`** — Improvements: (a) add `## Gotchas` heading, (b) fix the line 265–266 duplication, (c) normalize frontmatter (add tags, techStack, appliesTo to match other skills), (d) add `exemplar: tests/Spaarke.ArchTests/`, (e) add `last-reviewed:` field + audit stamp.

---

## 4. `ai-procedure-maintenance` — 363 lines

### Structural Assessment

- **description-quality**: PASS. "Maintain and update AI coding procedures when new elements are added" — explicit trigger framing.
- **body-line-count**: **OVER-TARGET-justified** (363 lines, under 400). The body is 6 checklists (A–F) for 6 different change scenarios. Each checklist is small; combined they're long. Splitting each checklist into a `references/` file would force the agent to load 6 files instead of 1 to do its job (the skill is invoked when the user says "I added an ADR, propagate changes"). Keeping inline is correct.
- **goal-oriented vs prescriptive**: Highly prescriptive (numbered checkboxes). Appropriate — this is exactly the kind of procedure that benefits from explicit checklists, and this skill is the canonical "checklist-driven approach."
- **Gotchas section**: **MISSING**. There's a "Cross-Reference Verification" section (lines 275–301) which catches consistency drift; that's effectively a gotchas-prevention section but isn't titled as such.
- **overlap with other skills**: None — this is the meta-skill that maintains the others. Unique surface.
- **deterministic-rules**: The path-consistency rules (lines 282–286) are exactly the kind of check that should be automated. Spec F-15.4 already plans `Find-SkillReferenceDrift.ps1`. Once that script exists, this section in the skill can shorten to "run `scripts/quality/Find-SkillReferenceDrift.ps1`." Real consolidation opportunity, but **not** until Phase 4.
- **references current**: All `.claude/adr/INDEX.md`, `.claude/constraints/INDEX.md`, `.claude/patterns/INDEX.md`, `.claude/protocols/INDEX.md`, `.claude/TAGGING-EXAMPLES.md` — **verified present**. `CROSS-REFERENCE-MAP.md` at repo root — **verified present**. `.claude/skills/SKILL-INTERACTION-GUIDE.md` — **verified present**.
- **Non-ASCII character bug**: Line 79 contains `Pointer format � When/Read/Constraints/Key Rules` — that `�` is a U+FFFD replacement character (a corrupted em dash). Should be `—` or `:`. Minor but real.

### Light reference check (F-15.3 mandatory)

- All referenced index files — **PASS**
- `CROSS-REFERENCE-MAP.md` (repo root) — **PASS**
- `.claude/skills/SKILL-INTERACTION-GUIDE.md` — **PASS**
- Referenced skills: `adr-aware`, `task-execute`, `task-create`, `repo-cleanup` — all exist (verified in inventory)
- `references-verified`: **pass**

### Medium reference check

The skill prescribes documentation changes, not commands. The "Automation Opportunity" section (lines 343–350) mentions a future script (`scripts/Validate-AIProcedures.ps1`) — note: spec F-15.4 names this `Validate-SkillReferences.ps1`. Inconsistency between skill and spec; not load-bearing but worth aligning in Phase 2b.

### Cross-reference impact

`ai-procedure-maintenance` has 5 references total: 1 see-also (doc-drift-audit), 2 trigger-map entries in root CLAUDE.md, 2 references from `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`. Low-volume but it's a checklist-class skill — its callers are the human user, not other skills. Cross-reference impact is small.

### Exemplar recommendation

`exemplar: none-too-volatile` with rationale — the skill produces no artifact (it's a propagation checklist). The canonical "use" is "an ADR was added, every cross-reference is now consistent" — verifiable by `Find-SkillReferenceDrift.ps1` (F-15.4) once it exists. Until then, no exemplar applies.

### Recommended action

**`refine-in-place`** — (a) fix the U+FFFD character on line 79, (b) add `## Gotchas` heading (move Cross-Reference Verification's anti-drift rules into it), (c) align "Automation Opportunity" script name with spec F-15.4 (`Validate-SkillReferences.ps1`), (d) add `last-reviewed:` field + audit stamp. Once F-15.4 ships, return here in a follow-up pass to replace the inline path-consistency checks with a script reference.

---

## 5. `azure-deploy` — 615 lines

### Structural Assessment

- **description-quality**: PASS. "Deploy Azure infrastructure, BFF API, Static Web Apps, and configure App Service settings" is clear and verb-leading. Reasonable.
- **body-line-count**: **SPLIT-required** (615 lines, over 400). This is the largest skill in the batch and significantly over the cap. The skill covers FOUR distinct deployment types (Infrastructure, BFF API, Office Add-ins SWA, Key Vault Secrets) plus verification, error handling, naming, and CI/CD integration. Each of the four types is a candidate for either its own skill OR an `examples/<type>.md` reference file under this skill.
- **goal-oriented vs prescriptive**: Heavily prescriptive — exact `az` CLI commands for every operation. For infrastructure deployment this is appropriate; the prescription density is the right pattern.
- **Gotchas section**: **MISSING**. There's a sprawling "Error Handling" section (lines 463–499) plus a "Tips for AI" section (lines 587–615). Neither is named Gotchas.
- **overlap with other skills**:
  - **Overlap with `bff-deploy`**: Both skills document BFF API deployment via `scripts/Deploy-BffApi.ps1`. `azure-deploy`'s BFF section (lines 152–278) is now superseded by the much-more-detailed `bff-deploy` skill (which includes the May 2026 silent-failure hardening). `azure-deploy`'s BFF section is **stale on the silent-failure issue** — it still says "Restart the App Service / Try alternative CLI command / Use Kudu" without mentioning hash-verify. This is the same class of bug as the 2026-05-14 `/pcf-deploy` "NEVER use build:prod" issue: a skill that confidently prescribes something that is now wrong.
  - Office Add-ins SWA section: not duplicated elsewhere. Could become its own skill (`office-addins-deploy`).
- **deterministic-rules**: The endpoint table (lines 76–84) is data, not procedure. Could be in a shared inventory file. Low priority.
- **references current**: Most references verified. **TWO BROKEN REFERENCES**: line 225 references `.github/workflows/deploy-staging.yml` and line 537/546/553/559/566 reference `deploy-to-azure.yml`. Neither exists in `.github/workflows/`. Actual workflows are `deploy-bff-api.yml`, `deploy-platform.yml`, `deploy-promote.yml`, `deploy-infrastructure.yml`, `deploy-office-addins.yml`, `deploy-slot-swap.yml`. The skill prescribes commands referencing non-existent workflows. **This will mislead an agent that trusts the skill.**

### Light reference check (F-15.3 mandatory)

- `.claude/constraints/azure-deployment.md` — **PASS**
- `scripts/Deploy-BffApi.ps1` — **PASS**
- `scripts/Deploy-OfficeAddins.ps1` — **PASS**
- `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md` — **PASS**
- `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md` — **PASS**
- `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` — **PASS**
- `infrastructure/bicep/stacks/ai-foundry-stack.bicep` / `model1-shared.bicep` / `model2-full.bicep` — **PASS**
- `.github/workflows/deploy-staging.yml` — **FAIL** (does not exist)
- `.github/workflows/deploy-to-azure.yml` — **FAIL** (does not exist)
- Referenced skills `dataverse-deploy`, `ribbon-edit`, `ci-cd` — all exist
- `references-verified`: **fail** (two missing workflow files mid-skill)

### Medium reference check

The BFF deploy guidance in this skill is **stale relative to `bff-deploy`** (no hash-verify, no Linux cold-start tolerance, no "silent success but files not replaced" warning). This makes `azure-deploy`'s BFF section actively dangerous to follow today. Per the spec's F-15.3 logic, this skill is a candidate to flag — but the structural problem (overlap + size) is the bigger issue than just content drift. The Office Add-ins section content appears internally consistent.

### Cross-reference impact

`azure-deploy` has 17 references total: 6 see-also (bff-deploy, ci-cd ×2, deploy-new-release, dev-cleanup, pcf-deploy), 3 root CLAUDE.md trigger entries, 2 settings.local.json permissions, 5 docs references, and 7 task POML references in two projects. Any restructure must update these 17 sites. The good news: most see-also references treat it as "for Azure infrastructure work" — i.e., they point at the *infrastructure* aspect, not the BFF or Office Add-ins aspects. So a split that keeps infrastructure inside `azure-deploy` and routes BFF callers to `bff-deploy` (already happening) and Office Add-ins to a new home would preserve most existing references.

### Exemplar recommendation

`exemplar: infrastructure/bicep/stacks/ai-foundry-stack.bicep` — the canonical exemplar for the **Infrastructure** deployment type (the part of this skill that should remain after split). For BFF and Office Add-ins exemplars, defer to the focused skills (`bff-deploy` for BFF; new `office-addins-deploy` if split).

### Recommended action

**`split`** — Recommended split structure:
1. Reduce `azure-deploy/SKILL.md` to the Infrastructure (Bicep) + Key Vault Secrets sections only. Target body ≤250 lines.
2. Remove the BFF API section entirely — defer to `bff-deploy` (the BFF section here is duplicated AND stale on silent-failure).
3. Move Office Add-ins deployment to a new skill `office-addins-deploy` (or to `azure-deploy/examples/office-addins.md` if a new skill is overkill).
4. Fix the two broken workflow filename references as part of the split.
5. Update the 6 see-also references and 3 CLAUDE.md trigger entries to point callers at the right destination (most go to `bff-deploy` already; just need to verify after split).

**Phase 2b plan**: This is the single highest-impact action in this batch. Treat it as an explicit Phase 2b task rather than a quick refine.

---

## 6. `bff-deploy` — 298 lines

### Structural Assessment

- **description-quality**: PASS. "Deploy the BFF API (Sprk.Bff.Api) to Azure App Service using the deployment script" — specific and trigger-leading.
- **body-line-count**: **OVER-TARGET-justified** (298 lines, under 400). The skill body is dense because it documents the May 2026 silent-failure hardening, the Linux cold-start behavior, the hash-verify recovery protocol, AND a planned WEBSITE_RUN_FROM_PACKAGE migration. The "Future Migration" section (lines 206–278, ~70 lines) is the single largest block; it's a forward-looking plan rather than current operational guidance and is a legitimate candidate to move to `references/run-from-package-migration.md`. After that split, the skill would be ~225 lines — still over target but much cleaner. Marking justified because the operational density is real and the planned migration content is part of the deploy story.
- **goal-oriented vs prescriptive**: Heavily prescriptive but justifiably so. The 2026-05-14 incident demonstrated that "trust the script" is the right discipline; the skill correctly reinforces this by enumerating the dangers of manual deploys.
- **Gotchas section**: **Effectively present, not titled.** Lines 16–20 (silent deploy failures + Linux cold-start) + the "Troubleshooting" table + the "Anti-Patterns (DO NOT)" section + "Why deploy succeeded can be a lie" together cover gotchas comprehensively. They're stronger than most skills' gotchas content. The fix is to title them as `## Gotchas` per the template, not to rewrite the content.
- **overlap with other skills**: Overlaps `azure-deploy` on BFF deployment (see above). The split recommendation for `azure-deploy` (remove its BFF section) resolves this.
- **deterministic-rules**: The "Forbidden Paths" (lines 49–55) and "Anti-Patterns" (lines 281–287) are MUST-NOTs that benefit from being in the skill (agent context), not a hook. Correctly inline.
- **references current**: All paths verified. The `deploy/api-publish/` directory referenced in the manual verification snippet (line 191) is consistent with what git status shows (modified files in `deploy/api-publish/`).

### Light reference check (F-15.3 mandatory)

- `src/server/api/Sprk.Bff.Api/` and `Sprk.Bff.Api.csproj` — **PASS**
- `scripts/Deploy-BffApi.ps1` — **PASS**
- App Service `spe-api-dev-67e2xz` — referenced repeatedly; matches CLAUDE.md root inventory
- Referenced skills `azure-deploy`, `dataverse-deploy`, `pcf-deploy`, `code-page-deploy` — all exist
- `references-verified`: **pass**

### Medium reference check

The PowerShell manual verification snippet (lines 187–199) uses current `az account get-access-token` and `Invoke-WebRequest` syntax. The `curl -X POST "https://{app}.scm.azurewebsites.net/api/zipdeploy?isAsync=true"` Kudu pattern (line 174) is the documented OneDeploy API and current. No staleness detected.

The 2026-05-14 hardening (default 24 × 5s = 120s health-check window, the hash-verify-then-auto-recover pattern) matches the spec's Issue 3 narrative exactly. This skill is currently the **gold standard** in the batch for incident-driven documentation: it cites the date, the symptom, the root cause hypothesis, the resolution, and the long-term plan.

### Cross-reference impact

`bff-deploy` has 24+ references across see-also (code-page-deploy, deploy-new-release, pcf-deploy, power-page-deploy), the root CLAUDE.md trigger map, settings.local.json, two project CLAUDE.md files, multiple docs (DEPLOYMENT-VERIFICATION-GUIDE, AI-CODING-PROCEDURES-GUIDE, ANTI-PATTERNS), and several task POMLs. As the canonical BFF deploy authority, it's already where everyone routes; the `azure-deploy` split will reinforce this rather than create new references.

### Exemplar recommendation

`exemplar: scripts/Deploy-BffApi.ps1` — the deploy script itself is the canonical, opted-in operational exemplar (per spec Reference Exemplars table). It's already tested every time someone deploys the BFF. Pairing it with a recent known-good deploy log (per the spec) makes the harness story complete.

### Recommended action

**`leave-alone-justified`** for the body content (the skill is high-quality, incident-grounded, and currently accurate as of 2026-05-14).

For Phase 2b: minor housekeeping — (a) add explicit `## Gotchas` heading (re-title existing content; no rewrite), (b) move the "Future Migration: WEBSITE_RUN_FROM_PACKAGE" section to `bff-deploy/references/run-from-package-migration.md` (drops body from 298 → ~225 lines and separates "current operations" from "future plan"), (c) add `last-reviewed: 2026-05-14` frontmatter + audit stamp, (d) add `exemplar: scripts/Deploy-BffApi.ps1`.

---

## Batch 1 Summary

### Count by recommended action

| Action | Count | Skills |
|---|---|---|
| `refine-in-place` | 4 | `add-reference-to-index`, `adr-aware`, `adr-check`, `ai-procedure-maintenance` |
| `split` | 1 | `azure-deploy` |
| `leave-alone-justified` | 1 | `bff-deploy` |
| `leave-alone` | 0 | — |
| `merge-with-X` | 0 | — |
| `remove` | 0 | — |
| `needs-substantive-rewrite` | 0 | — |

### Top 2 concerns

1. **`azure-deploy` BFF section is stale AND has broken workflow filename references.** Lines 225, 537, 546, 553, 559, 566 reference `deploy-staging.yml` and `deploy-to-azure.yml` — neither exists in `.github/workflows/`. The BFF deploy guidance (lines 152–278) predates the 2026-05-14 silent-failure hardening and does NOT mention hash-verify, the 120-s Linux cold-start window, or the "deploy succeeded but files not replaced" failure mode that `bff-deploy` documents. An agent following `azure-deploy`'s BFF section today would be following 2025-era guidance that has been superseded. This is the same class of failure that motivated this project (a skill confidently prescribing something that is now wrong). The split recommendation removes both problems at once.

2. **No skill in this batch has an explicit `## Gotchas` heading.** This is consistent with the inventory's finding that 0 of 44 skills have one. `bff-deploy` has the strongest gotchas content (multiple incident-grounded warnings with dates and root-cause analysis); `adr-check` has a stub-class Error Handling table. The Phase 2b refinement pass should add the heading wherever incident-grounded content already exists, and stub it ("No gotchas surfaced yet") where it doesn't. This is mechanical and low-risk.

### Minor observation (not a top concern)

Three skills in this batch have stale or anomalous frontmatter: `add-reference-to-index` puts H1 before YAML (inconsistent with template); `adr-check` has only `description` + `alwaysApply` (missing tags/techStack/appliesTo per inventory anomaly #6); `adr-aware` references a renamed skill (`design-to-project` → `design-to-spec`) inline. None are blocking; all are part of the Phase 2b normalization pass.
