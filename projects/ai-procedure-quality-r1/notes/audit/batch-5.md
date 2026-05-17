# Phase 2a Skills Audit — Batch 5 (skills 25–30)

> **Generated**: 2026-05-15
> **Task**: 024-audit-skills-batch-5
> **Scope**: 6 skills (alphabetic positions 25–30 of 44)
> **Mode**: READ-ONLY on `.claude/skills/`. No source code changes.
> **Honesty**: Findings reflect the files as-read. Skills already in good shape are recommended `leave-alone`.

Skills in this batch:

1. `jps-playbook-design`
2. `jps-scope-refresh`
3. `jps-validate`
4. `merge-to-master`
5. `pcf-deploy` (explicit AP-1 verification — see Skill 29)
6. `power-page-deploy`

This batch contains **3 JPS-family skills** (`jps-playbook-design`, `jps-scope-refresh`, `jps-validate`). Combined with the 2 in batch 4 (`jps-action-create`, `jps-playbook-audit`), the full JPS family is **5 skills**. Mutual overlap and consolidation potential are assessed in the Batch Summary. The batch also contains **3 operational/deployment skills** (`merge-to-master`, `pcf-deploy`, `power-page-deploy`) — Medium ref-check applied to all three. `pcf-deploy`'s AP-1 fix is explicitly verified.

---

## Skill 25 — `jps-playbook-design`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Design and deploy a complete AI playbook — from requirements through Dataverse deployment" — clear, trigger-leading. |
| Body line count (incl. frontmatter) | **516 lines** | **SPLIT** (F-15.1: >400 line cap). Content is operational density — 13 sequential steps, ASCII canvas, scope/model selection logic, error tables. Splitting candidates: Steps 4–5 scope/model selection details → `references/scope-model-selection.md`; node-graph patterns (Pattern 1–4) → `references/playbook-design-patterns.md`; error-handling table → `references/error-handling.md`. SKILL.md body would target ~280 lines after split. |
| Goal-oriented framing | ✅ Yes | Tier 2 Orchestrator framing is explicit; "Why This Skill Exists" + "Applies When" + "NOT for" present. |
| `## Gotchas` section (as heading) | ❌ Absent | No dedicated heading. "Tips for AI" (line 500–516) functions like gotchas; should be reshaped or supplemented. |
| Constraints / MUST/MUST-NOT block | ⚠️ Implicit | "Conventions" (line 397–407) and embedded rules inside steps function as MUSTs but lack explicit MUST/MUST NOT shape. |
| Deterministic rules | ✅ | Steps are concrete; validation rules in Step 8 are explicit; auto-layout positions are precise. |
| `Last Reviewed` stamp (R2 convention) | ❌ Absent | No `Last Updated` or `Last Reviewed` line at all. |
| `exemplar:` frontmatter | ❌ Absent | Strong candidate for an exemplar (a known-good playbook definition JSON). |
| Overlap with sibling skills | ⚠️ Material | See Batch Summary — calls `jps-action-create` (Step 4), `jps-scope-refresh` (Step 9.5), `jps-validate`. Distinct charter as the orchestrator, but the boundary with `jps-playbook-audit` (batch 4) is fuzzy. |
| References current | ✅ Mostly | All script and doc paths resolve (see Light check). |

### Light reference check (mandatory)

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | `.claude/catalogs/scope-model-index.json` — **exists**. `docs/architecture/playbook-architecture.md` — **exists**. `docs/guides/JPS-AUTHORING-GUIDE.md` — **exists**. `scripts/Deploy-Playbook.ps1` — **exists**. `projects/ai-json-prompt-schema-system/notes/jps-conversions/` — not verified file-by-file (directory expected). |
| URLs | ✅ Pass | Only URL is `https://spaarke.com/schemas/playbook-definition/v1` — schema identifier (not expected to resolve). |
| Dependent skill names | ✅ Pass | `jps-action-create`, `jps-scope-refresh`, `jps-validate`, `dataverse-deploy`, `dev-cleanup` — all exist in `.claude/skills/`. |

### Medium reference check (recommended — JPS-orchestrator skill)

| Check | Status | Notes |
|---|---|---|
| Command syntax | ✅ Pass | `powershell -ExecutionPolicy Bypass -File scripts/Deploy-Playbook.ps1 -DefinitionFile "{path}" -Environment dev [-DryRun]` — standard PS invocation; script exists. |
| MCP tool invocations | ✅ Pass | `mcp__dataverse__read_query` patterns in Step 8 are correctly named per root CLAUDE.md MCP tool list. |
| Dataverse entity names | ⏸ Not verified at depth | `sprk_analysisactions`, `sprk_analysisskills`, `sprk_analysisknowledges`, `sprk_analysistools`, `sprk_aimodeldeployments` referenced — would require Dataverse query to verify schema names exactly match. Within 10-min cap — flag as `verify-during-rewrite` rather than `needs-substantive-rewrite`. |
| Code refs | ✅ Pass | No specific code-file pointers (file:line) — all references are to scripts/docs. |

### Cross-ref impact

- **Referenced by** (per `skill-cross-refs.json`, lines 2034–2065): no direct skill-to-skill invocations FROM `jps-playbook-design`; **referenced BY** `jps-scope-refresh` (lines 117, 127), and indirectly from CLAUDE.md root trigger map + project POMLs. Network role: the JPS-family hub.
- **If split**: subdirectory files (`references/*.md`) are new — no cross-ref impact (nobody references them today).
- **If consolidated with `jps-playbook-audit`** (batch 4): consider but unlikely — design and audit are distinct verbs with different inputs. Recommendation: keep separate, sharpen the boundary in description.

### Exemplar recommendation

- `exemplar: projects/ai-json-prompt-schema-system/notes/playbook-definitions/<canonical-playbook>.json` — assuming a current canonical exists in that folder. If not, `exemplar: none-too-volatile` with rationale that playbook designs are project-specific (no single canonical reference holds steady). Phase 2b decision.

### Recommended action

**`needs-substantive-rewrite`** — combines two requirements:
1. **SPLIT** the body to under 400 lines per F-15.1 by extracting design patterns + error tables + scope-selection details into `references/`.
2. Add `Last Reviewed` stamp, add a real `## Gotchas` section (anti-pattern candidates: stale `scope-model-index.json`, scope-code-in-index-but-not-in-Dataverse drift), add `exemplar:` decision, normalize MUST/MUST NOT shape.

This is structurally OK but the volume + missing-canonical-elements warrants a dedicated session per F-15.3.

---

## Skill 26 — `jps-scope-refresh`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Refresh the scope & model index from Dataverse so Claude Code has the latest catalog" — clear. |
| Body line count | **127 lines** | **PASS** — well under 200. |
| Goal-oriented framing | ✅ Yes | "Tier 3 Operational Skill" + Why + Applies When all present. |
| `## Gotchas` section | ❌ Absent | No dedicated heading. |
| Constraints (MUST/MUST NOT) | ⚠️ Implicit | "Conventions" block (line 95–99) is the closest; no explicit MUST/MUST NOT framing. |
| Deterministic rules | ✅ | Steps are concrete: run script, verify update, conditional commit. |
| `Last Reviewed` stamp | ❌ Absent | No stamp at all. |
| `exemplar:` frontmatter | ❌ Absent | Reasonable candidate is `none-too-volatile` (script behavior is the contract; catalog content changes frequently). |
| Overlap with sibling skills | ⚠️ Yes — see Batch Summary | Called by `jps-action-create` and `jps-playbook-design`. Tight integration but distinct purpose. |
| References current | ✅ | Script and index file paths resolve. |

### Light reference check (mandatory)

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | `scripts/Refresh-ScopeModelIndex.ps1` — **exists**. `.claude/catalogs/scope-model-index.json` — **exists**. |
| URLs | n/a | No URLs. |
| Dependent skill names | ✅ Pass | `jps-action-create`, `jps-playbook-design`, `jps-validate`, `dev-cleanup` — all exist. |

### Medium reference check (recommended — operational)

| Check | Status | Notes |
|---|---|---|
| Command syntax | ✅ Pass | `powershell -ExecutionPolicy Bypass -File scripts/Refresh-ScopeModelIndex.ps1 -Environment dev` — valid; script exists. |
| MCP tool patterns | ✅ Pass | Step 0 `mcp__dataverse__read_query` calls reference table names (`sprk_analysisskill`, `sprk_analysistool`, `sprk_analysisknowledge`, `sprk_analysisaction`) — logical-name pluralization is consistent (singular table logical name, plural in WebAPI endpoint elsewhere in jps-playbook-design — minor inconsistency between JPS skills, see Batch Summary). |
| Code refs | n/a | No code-file pointers. |

### Cross-ref impact

- **Referenced by** (lines 2067–2092): `jps-action-create` (line 271 of that skill — chain), `jps-playbook-design` lines 493, 308 (Step 9.5). Removal would break two skills.
- **Merge candidate**: could be folded into `jps-playbook-design` as a sub-step but that would re-grow `jps-playbook-design` (already SPLIT-required). Better to keep small + atomic.

### Exemplar recommendation

- `exemplar: none-too-volatile` — the contract is "run the script, verify counts changed, commit if changed." There is no useful canonical reference output.

### Recommended action

**`refine-in-place`** — short, focused skill. Add `Last Reviewed` stamp, add `## Gotchas` heading (stub if no concrete gotchas yet), add `exemplar: none-too-volatile` with rationale, fix the singular vs plural table-name inconsistency vs `jps-playbook-design` (one uses `sprk_analysisaction` here vs `sprk_analysisactions` in `jps-playbook-design` Step 8 — verify which is the actual Dataverse logical name). Body grows ~15 lines, stays well under 200.

---

## Skill 27 — `jps-validate`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Validate a JPS JSON file against schema and test rendering" — clear. |
| Body line count | **265 lines** | **OVER-TARGET** (between 201 and 400). F-15.1 tier: justified by checklist density (23 checks, 2 worked examples). Borderline — could trim Example 2 to keep body under 200, but density is appropriate for validation skill. Recommend leaving as-is. |
| Goal-oriented framing | ✅ Yes | Tier 1 Component framing, "Why" + "Applies When" present. |
| `## Gotchas` section | ❌ Absent | No dedicated heading. |
| Constraints (MUST/MUST NOT) | ⚠️ Implicit | The check list itself is the constraint set, but not in canonical MUST shape. |
| Deterministic rules | ✅ | 23 numbered checks with explicit PASS/FAIL/WARN criteria. |
| `Last Reviewed` stamp | ❌ Absent | No stamp at all. |
| `exemplar:` frontmatter | ❌ Absent | Strong candidate for an exemplar: a known-good JPS file path. |
| Overlap with sibling skills | ⚠️ See Batch Summary | Called by `jps-playbook-design` Step 8 conceptually; distinct from `jps-action-create` (creates) and `jps-playbook-audit` (audits playbooks, not individual JPS files). |
| References current | ✅ | JPS file directory and code path resolve. |

### Light reference check (mandatory)

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | `projects/ai-json-prompt-schema-system/notes/jps-conversions/` — referenced as directory (not file-by-file verified, expected to exist). `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs` (line 129) — exists. |
| URLs | ✅ Pass | `https://spaarke.com/schemas/prompt/v1` is a schema identifier (not expected to resolve as URL). |
| Dependent skill names | ✅ Pass | `jps-action-create`, `jps-playbook-design`, `dataverse-deploy` — all exist. |

### Medium reference check (recommended — JPS validator)

| Check | Status | Notes |
|---|---|---|
| Code refs | ⏸ Spot-check | Line 129 references `PromptSchemaRenderer.cs` — file exists. The `IsJpsFormat()` claim (line 121–124) — needs a verification pass against the actual renderer code. Within 10-min cap: this is a `verify-during-rewrite` flag rather than full `needs-substantive-rewrite`. |
| Command syntax | ✅ Pass | `dotnet test ... --filter PromptSchemaRenderer` — standard dotnet syntax. |
| Test invocation pattern | ⏸ Spot-check | Recommends a render test but explicitly defers full test to dotnet — acceptable, no hidden failure mode. |

### Cross-ref impact

- **Referenced by** (lines 2093–2120): `jps-action-create` (line 254 of that skill — see_also), `jps-playbook-design` line 494 (see_also), `jps-scope-refresh` line 118 (see_also). Removal would break three skills' related-skills sections (low impact, see-also only).
- Distinct charter; not a merge candidate.

### Exemplar recommendation

- `exemplar: projects/ai-json-prompt-schema-system/notes/jps-conversions/document-profiler.json` (the example used in Example 1 already names this — promote to formal exemplar).

### Recommended action

**`refine-in-place`** — solid skill; needs `Last Reviewed` stamp, `## Gotchas` heading (anti-pattern candidate: "validator passes but renderer rejects due to format-detection edge case" — surfaced in spec F-12 prevention discussion), `exemplar:` pointing at the existing example JPS, normalize MUST/MUST NOT shape. No split required. Body grows ~10 lines, stays in OVER-TARGET tier but justified.

---

## Skill 28 — `merge-to-master`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Merge completed branch work into master with safety checks and build verification" — clear. |
| Body line count | **359 lines** | **OVER-TARGET** (between 201 and 400). F-15.1 tier: justified by operational density (3 modes, 6 steps, conflict-resolution strategy, error table, integration points). Recommend leaving as-is. |
| Goal-oriented framing | ✅ Yes | Three-mode structure (Audit / Single Merge / Full Reconciliation) is explicit and trigger-routed. |
| `## Gotchas` section | ❌ Absent | "Tips for AI" (line 346–355) functions similarly but isn't titled Gotchas. |
| Constraints (MUST/MUST NOT) | ✅ Mostly | "MANDATORY before pushing" (line 205), "Never push without build verification" — clear MUSTs scattered. Not in unified MUST/MUST NOT block. |
| Deterministic rules | ✅ | Step-by-step git commands; checklist for conflict resolution. |
| `Last Reviewed` stamp | ❌ Absent | Uses `Last Updated: February 2026` (line 12). |
| `exemplar:` frontmatter | ❌ Absent | Borderline candidate — `none-too-volatile` is reasonable since merge inputs change every project. |
| Overlap with sibling skills | ✅ Distinct | Distinct verb (merge to master) from `push-to-github` (push branch), `pull-from-github` (pull branch), `worktree-sync` (sync worktree). Cross-references to all three are intentional. |
| References current | ✅ Mostly | See Light + Medium checks. |

### Light reference check (mandatory)

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | `.claude/skills/worktree-sync/SKILL.md` (line 267) — exists. No other file paths. |
| URLs | n/a | No URLs. |
| Dependent skill names | ✅ Pass | `push-to-github`, `pull-from-github`, `conflict-check`, `repo-cleanup`, `worktree-sync`, `task-execute`, `project-pipeline`, `project-continue`, `dev-cleanup` — all exist in `.claude/skills/`. |

### Medium reference check (recommended — operational)

| Check | Status | Notes |
|---|---|---|
| Git command syntax | ✅ Pass | `git fetch origin`, `git rev-list --count`, `git merge-base`, `git log -1 --format=%ci`, `git merge --no-commit --no-ff`, `git merge --abort`, `git push origin master` — all standard. Conflict-preview pattern (line 142–151) is correct usage. |
| Build verification command | ✅ Pass | `dotnet build src/server/api/Sprk.Bff.Api/` — valid project path. |
| Branch-name pattern | ✅ Pass | `origin/work/{branch}` — matches Spaarke convention. |
| Reconciliation branch pattern | ✅ Pass | `reconcile/branch-cleanup` safety-net pattern is internally consistent. |

### Cross-ref impact

- **Referenced by** (lines 2121–2200): Heavily cross-linked from project-continue, project-pipeline, push-to-github, worktree-sync, repo-cleanup, task-execute, root CLAUDE.md trigger map (8 trigger phrases). High-impact skill — refinement must preserve all triggers.
- **No merge/split candidate** — distinct charter, appropriate size for operational density.

### Exemplar recommendation

- `exemplar: none-too-volatile` — every merge is a snapshot of branch state; no canonical reference holds.

### Recommended action

**`refine-in-place`** — solid, well-structured operational skill. Add `Last Reviewed` stamp, add a real `## Gotchas` heading (reuse content from "Tips for AI" — anti-pattern candidate: deleting branches before verifying build, or pushing master without conflict audit), add `exemplar: none-too-volatile`, lift the MUST rules into a unified MUST/MUST NOT block near the top. Body grows ~15 lines, stays in OVER-TARGET tier but justified.

---

## Skill 29 — `pcf-deploy` (explicit AP-1 verification)

### AP-1 verification (PRIMARY)

**STATUS: AP-1 FIX IS IN PLACE.** Verified the following at `c:/code_files/spaarke/.claude/skills/pcf-deploy/SKILL.md`:

1. **No "NEVER use build:prod" instruction remains.** Searched the file; the wrong "NEVER" claim from the pre-2026-05-14 state is absent.
2. **Correct prescription is present** (line 139): `✅ **MUST** use \`npm run build:prod\` for production deploys — runs \`pcf-scripts build --buildMode production\` which enables tree-shaking + minification. Default \`npm run build\` runs **development mode** and produces a bundle 10–15× larger because tree-shaking is disabled.`
3. **Dedicated section "Bundle Size & Production Mode (CRITICAL — verified 2026-05-14)"** (lines 144–174) explicitly:
   - Explains the failure mode (dev mode default, no tree-shaking)
   - Names the correct invocation: `"build:prod": "pcf-scripts build --buildMode production"`
   - Lists known-bad variants (`--production`, `-- --mode production`) that are silently ignored
   - Provides expected bundle sizes for known-good controls (SpeDocumentViewer 440 KB, SemanticSearchControl 539 KB, RelatedDocumentCount 433 KB)
4. **Step 2 build instruction** (line 220): `npm run build:prod         # MUST be build:prod — see Bundle Size & Production Mode` — operative command is correct.

**Conclusion**: AP-1 (per `.claude/FAILURE-MODES.md`) has been correctly remediated in this skill. The skill now prescribes the correct command and explains why.

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Build, pack, and deploy PCF controls to Dataverse via solution ZIP import" — clear. |
| Body line count | **410 lines** | **OVER-TARGET / SPLIT-borderline** (just over 400). F-15.1 cap: >400 requires split. Candidates to extract: Solution XML templates (lines 285–343, ~60 lines) → `references/solution-xml-templates.md`; Troubleshooting table (lines 372–384) → `references/troubleshooting.md`; Path Map (lines 178–202) → `references/semanticsearchcontrol-path-map.md`. Post-split body ~280–300 lines. |
| Goal-oriented framing | ✅ Yes | Clear "When to Use" + "When NOT" + "Critical Rules" sections. |
| `## Gotchas` section | ❌ Absent | No dedicated heading. "Critical Rules" + "Troubleshooting" function as gotcha-equivalent content but don't carry the heading. |
| Constraints (MUST/MUST NOT) | ✅ Strong | Lines 35–98 are explicit `❌ NEVER` / `✅ ONLY` / `✅ MUST` rules — well-shaped. Best-in-class for this batch. |
| Deterministic rules | ✅ | Path Map, version-bump-locations table, expected bundle sizes — all concrete. |
| `Last Reviewed` stamp | ❌ Absent | Uses `Last Updated: February 22, 2026` (line 12). |
| `exemplar:` frontmatter | ❌ Absent | Strong candidate — SemanticSearchControl is already named throughout. |
| Overlap with sibling skills | ✅ Distinct | Clean boundary against `dataverse-deploy`, `code-page-deploy`, `power-page-deploy`. |
| References current | ⚠️ See Light check | Two broken ADR file paths — see below. |

### Light reference check (mandatory)

| Reference type | Status | Notes |
|---|---|---|
| File paths | ❌ **Fail (2 broken ADR links)** | `docs/adr/ADR-006-pcf-over-webresources.md` (line 404) → **broken**, actual file is `docs/adr/ADR-006-prefer-pcf-over-webresources.md`. `docs/adr/ADR-021-fluent-design-system.md` (line 405) → **broken**, actual file is `docs/adr/ADR-021-fluent-ui-design-system.md`. `docs/adr/ADR-022-pcf-platform-libraries.md` (line 406) → **exists**. `docs/guides/PCF-DEPLOYMENT-GUIDE.md` (line 13) → **exists**. `.claude/patterns/pcf/control-initialization.md` (line 92) → **exists**. Source-tree paths (`src/client/pcf/SemanticSearchControl/...`) all resolve. |
| URLs | n/a | No external URLs. |
| Dependent skill names | ✅ Pass | `dataverse-deploy`, `task-execute`, `adr-aware`, `code-page-deploy`, `bff-deploy` — all exist. |

### Medium reference check (recommended — ops skill, AP-1 origin)

| Check | Status | Notes |
|---|---|---|
| `build:prod` invocation | ✅ Pass (verified) | Confirmed via cross-check against AP-1 in FAILURE-MODES.md; correct `pcf-scripts build --buildMode production` semantics are prescribed. |
| Pack/import commands | ✅ Pass | `powershell -ExecutionPolicy Bypass -File pack.ps1`, `pac solution import --path ... --publish-changes`, `pac solution list` — standard PAC CLI invocations. |
| Path Map vs reality | ⚠️ Inconsistency | Path Map says `Solution/Controls/sprk_Sprk.SemanticSearchControl/` (line 192) — verified actual subfolder name is `sprk_Sprk.SemanticSearchControl` (matches). However XML templates further down use placeholder `sprk_Spaarke.Controls.{ControlName}` (lines 257, 306, 333) — convention varies between controls. Minor — needs a clarifying note that schema-name prefix varies per control. |
| Shared lib compile step | ✅ Pass | Step 1.5 commands are correct: `npm run build` or fallback `tsc` with explicit flags. |
| Verify-size command | ✅ Pass | `stat -c '%s bytes' out/controls/{ControlName}/bundle.js` — valid Git Bash on Windows. |

### Cross-ref impact

- **Referenced by** (lines 2201–2257): root CLAUDE.md trigger map (4 phrases), POML tasks in projects, `power-page-deploy` see-also. Removal would break router; refinement must preserve all triggers.
- **If split**: subdirectory files (`references/...`) are new — no external cross-ref impact.
- **ADR link fixes** must update both ADR-006 and ADR-021 references.

### Exemplar recommendation

- `exemplar: src/client/pcf/SemanticSearchControl/` — already the named canonical throughout the skill. Spec F-15 explicitly lists this skill→exemplar mapping (spec line 203).

### Recommended action

**`needs-substantive-rewrite`** — combines:
1. **SPLIT** body to <400 lines per F-15.1 (extract XML templates, troubleshooting, path map).
2. **FIX broken ADR-006 and ADR-021 filenames** (both Light-check failures, must repair in same task per F-15.2).
3. **Add `Last Reviewed: 2026-05-14` stamp** (this was the AP-1 fix date — meaningful to stamp).
4. **Add `exemplar: src/client/pcf/SemanticSearchControl/` frontmatter** (already de facto canonical).
5. **Add `## Gotchas` heading** that explicitly cross-references `FAILURE-MODES.md#AP-1` per spec F-15.3 prevention discussion.
6. Resolve the `sprk_Sprk.SemanticSearchControl/` vs `sprk_Spaarke.Controls.{ControlName}/` schema-name convention drift across the skill body.

Note: this skill is the **source of AP-1**; the AP-1 fix landed correctly, but the broader skill quality (broken ADR links, missing `## Gotchas` cross-ref to FAILURE-MODES, body length) means a structural pass is warranted, not just leave-alone.

---

## Skill 30 — `power-page-deploy`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Build and deploy a Vite/React SPA to Power Pages as a Code Site" — clear. |
| Body line count | **278 lines** | **OVER-TARGET** (between 201 and 400). F-15.1 tier: justified by operational density (build/deploy procedure, two deploy targets to disambiguate, troubleshooting). |
| Goal-oriented framing | ✅ Yes | "When to Use" + "When NOT" + Quick Reference table at top. |
| `## Gotchas` section | ❌ Absent | No dedicated heading. Multiple `⚠️` warnings inline (lines 68, 96, 170) function as gotchas. |
| Constraints (MUST/MUST NOT) | ⚠️ Implicit | "Critical" notes inline; no unified MUST/MUST NOT block. |
| Deterministic rules | ✅ | All steps are concrete commands; Prerequisites enumerated. |
| `Last Reviewed` stamp | ❌ Absent | Uses `Last Updated: March 2026` (line 8). |
| `exemplar:` frontmatter | ❌ Absent | Strong candidate per spec F-15: `src/client/external-spa`. |
| Frontmatter completeness | ❌ Minimal | Only `description:` — missing `tags:`, `techStack:`, `appliesTo:`, `alwaysApply:`. Per inventory: among 8 skills with reduced frontmatter (a Phase 2b normalization target). |
| Overlap with sibling skills | ✅ Distinct | Clean boundary: PCF (pcf-deploy), Code Pages (code-page-deploy), BFF (bff-deploy), Corporate Workspace (out-of-scope per line 42), Code Site (this skill). |
| References current | ⚠️ See Light check | `.site-download/spaarke-external-workspace/` does NOT exist in worktree — see below. |

### Light reference check (mandatory)

| Reference type | Status | Notes |
|---|---|---|
| File paths | ⚠️ Partial pass | `scripts/Deploy-PowerPages.ps1` — **exists**. `src/client/external-spa/` — **exists**. `src/client/external-spa/dist/` — exists. `src/client/external-spa/.site-download/spaarke-external-workspace/` (lines 56, 98, 165, 270) — **does NOT exist** in this worktree (gitignore at `src/client/external-spa/.gitignore` lists `.powerpages-site/` which excludes the subfolder; not `.site-download/` directly but folder absent). Skill anticipates this on line 100 ("if missing on a fresh clone, the deploy script recreates it"). Acceptable per skill's own statement. `src/client/external-spa/.env.production.local` — referenced as required prerequisite (lines 88, 273). `scripts/Deploy-ExternalWorkspaceSpa.ps1` — **exists**. |
| URLs | ✅ Pass | `https://sprk-external-workspace.powerappsportals.com`, `https://spaarkedev1.crm.dynamics.com`, `https://spe-api-dev-67e2xz.azurewebsites.net` — all plausible (BFF URL matches root CLAUDE.md Quick Endpoints). |
| Dependent skill names | ✅ Pass | `pcf-deploy`, `code-page-deploy`, `bff-deploy` — all exist. |

### Medium reference check (recommended — ops skill)

| Check | Status | Notes |
|---|---|---|
| PAC CLI commands | ✅ Pass | `pac auth list`, `pac auth create --url ...`, `pac pages upload-code-site --rootPath ... --compiledPath ... --siteName ...`, `pac pages download-code-site --webSiteId ... --path ... --overwrite` — standard PAC CLI 2.4.x syntax. |
| Vite/npm commands | ✅ Pass | `npm run build`, `npm run dev` — standard. |
| Env var contract | ✅ Pass | `VITE_BFF_API_URL`, `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_TENANT_ID`, `VITE_MSAL_BFF_SCOPE`, `VITE_DEV_MOCK` — verified against actual `.env.example` (matches). |
| Website ID | ⏸ Not verified | `a79315b5-d91e-4e27-b016-439ad439babe` (line 28) and ID for Spaarke External Workspace — would require Dataverse query to verify. Treat as `verify-during-rewrite`. |

### Cross-ref impact

- **Referenced by** (lines 2258–2317): root CLAUDE.md trigger map (multiple phrases), `pcf-deploy` line 39 (see_also), POML references in `sdap-secure-project-module` project. Removal would break router; refinement preserves triggers.
- **No merge/split candidate** — distinct charter.

### Exemplar recommendation

- `exemplar: src/client/external-spa/` — already named as the canonical source throughout the skill. Spec F-15 explicitly lists this mapping (spec line 206).

### Recommended action

**`refine-in-place`** — solid operational skill with a clear charter. Required edits:
1. **Normalize frontmatter** — add `tags:`, `techStack:`, `appliesTo:`, `alwaysApply: false` to match canonical scaffold.
2. **Add `Last Reviewed` stamp** (replace `Last Updated`).
3. **Add `## Gotchas` heading** — promote the inline `⚠️` warnings (especially line 68 about `Deploy-ExternalWorkspaceSpa.ps1` deploying to a DIFFERENT target than the portal — this is exactly the kind of "looks-right-but-wrong" cross-cutting failure that warrants formal capture).
4. **Add `exemplar: src/client/external-spa/`** frontmatter.
5. **Lift Critical notes** into a unified MUST/MUST NOT block near the top.
6. **Add a one-line note** that `.site-download/spaarke-external-workspace/` may be absent on a fresh clone (already half-stated, can be sharper).

No split needed.

---

## Batch 5 Summary

### Line-count tier rollup (F-15.1)

| Skill | Lines | Tier |
|---|---:|---|
| jps-playbook-design | 516 | **SPLIT-required** |
| jps-scope-refresh | 127 | **PASS** |
| jps-validate | 265 | OVER-TARGET-justified |
| merge-to-master | 359 | OVER-TARGET-justified |
| pcf-deploy | 410 | **SPLIT-required (borderline; 10 lines over)** |
| power-page-deploy | 278 | OVER-TARGET-justified |

Two skills cross the 400-line `SPLIT` threshold: `jps-playbook-design` (clearly) and `pcf-deploy` (borderline; could be argued either way, recommend split given the natural extraction candidates).

### Reference check rollup (F-15.3)

| Skill | Light | Medium | Notes |
|---|---|---|---|
| jps-playbook-design | ✅ | ✅ (mostly) | Singular vs plural Dataverse table-name inconsistency vs jps-scope-refresh (`sprk_analysisaction` vs `sprk_analysisactions`) — verify which is the actual logical name. |
| jps-scope-refresh | ✅ | ✅ | Same singular/plural drift noted above; spot-check vs Dataverse. |
| jps-validate | ✅ | ⏸ Spot-check | `IsJpsFormat()` claim referenced in PromptSchemaRenderer.cs — verify during rewrite. |
| merge-to-master | ✅ | ✅ | Clean. |
| pcf-deploy | ❌ **FAIL** | ✅ | Two broken ADR file paths: ADR-006 (actual name `ADR-006-prefer-pcf-over-webresources.md`) and ADR-021 (actual name `ADR-021-fluent-ui-design-system.md`). MUST fix during Phase 2b refinement per F-15.2 (no dangling references). |
| power-page-deploy | ⚠️ | ✅ | `.site-download/spaarke-external-workspace/` referenced 4× — folder is absent on this worktree (gitignored / fresh-clone scenario the skill itself describes). Acceptable per the skill's own line-100 acknowledgement, but worth tightening. |

**Light-check failures: 1 skill (pcf-deploy, 2 broken ADR links). MUST be repaired before / during the refinement task for pcf-deploy.**

### pcf-deploy AP-1 verification status (PRIMARY)

**STATUS: AP-1 fix is correctly in place.** See Skill 29 details. The wrong "NEVER use build:prod" instruction has been removed; the correct `pcf-scripts build --buildMode production` is now prescribed with an explicit "Bundle Size & Production Mode (CRITICAL — verified 2026-05-14)" section that includes evidence (known-good bundle sizes for 3 PCFs) and warnings about silent-failure variants. Phase 2b should preserve this content and additionally add a `## Gotchas` heading with an explicit `See FAILURE-MODES.md#AP-1` cross-reference per the spec F-15.3 prevention pattern.

### JPS-family mutual overlap assessment (3 skills in batch 5 + 2 in batch 4 = 5 total)

| Skill | Tier | Charter | Verb | Relationship |
|---|---|---|---|---|
| `jps-action-create` (batch 4) | Tier 1 Component | Create a single JPS action definition | **create** (one) | Called by: jps-playbook-design Step 4 (offers to create new actions). Creates: JPS files + Dataverse scope record. Refreshes: jps-scope-refresh at Step 6. |
| `jps-playbook-audit` (batch 4) | (review needed) | Audit existing playbooks against scope catalog | **audit** (existing) | Distinct from design — operates on already-deployed playbooks. |
| `jps-playbook-design` (batch 5) | Tier 2 Orchestrator | Design + deploy a multi-node playbook | **design + deploy** (orchestrator) | Calls: jps-action-create (Step 4), jps-scope-refresh (Step 9.5), jps-validate (implicit Step 8). Family hub. |
| `jps-scope-refresh` (batch 5) | Tier 3 Operational | Refresh scope-model-index.json from Dataverse | **refresh** | Called by: jps-action-create, jps-playbook-design. Lightweight, single-purpose. |
| `jps-validate` (batch 5) | Tier 1 Component | Validate a JPS JSON file against schema | **validate** (one) | Used by: jps-playbook-design Step 8 (via validation logic, not always invoked directly). |

**Verdict: mutual overlap is LOW. Each skill has a distinct verb and charter:**
- `create` (one new action), `audit` (review existing playbooks), `design` (orchestrate new playbook), `refresh` (sync index), `validate` (check one JPS).
- The family is well-decomposed by single-responsibility. No merge candidates surface.
- The strongest pairing is `jps-playbook-design` ↔ `jps-playbook-audit` (both operate on playbooks), but their verbs (design vs audit) differ enough that merging would obscure intent.
- `jps-scope-refresh` could in principle fold into `jps-playbook-design` Step 9.5, but it's also called by `jps-action-create` independently — keeping it atomic prevents duplication.

**Recommended Phase 2b normalization (not consolidation)**:
1. Add a "## JPS Family at a Glance" pointer block to each JPS skill so the agent loading any one of them understands where it fits.
2. Resolve the singular vs plural Dataverse table-name inconsistency (`sprk_analysisaction` vs `sprk_analysisactions`) across all 5 — pick the correct logical name per actual Dataverse schema (verifiable via MCP).
3. Confirm `jps-playbook-audit` (deferred to batch 4 audit) doesn't overlap with `jps-validate` (likely doesn't — playbook-level vs JPS-file-level).

### Action recommendation rollup

| Skill | Recommended action |
|---|---|
| jps-playbook-design | **`needs-substantive-rewrite`** (split + add Gotchas + Last Reviewed + exemplar + normalize MUST shape) |
| jps-scope-refresh | `refine-in-place` (Last Reviewed + Gotchas + exemplar:none-too-volatile + table-name drift fix) |
| jps-validate | `refine-in-place` (Last Reviewed + Gotchas + exemplar pointing at existing example JPS) |
| merge-to-master | `refine-in-place` (Last Reviewed + Gotchas heading + exemplar:none-too-volatile + unified MUST block) |
| pcf-deploy | **`needs-substantive-rewrite`** (split + 2 broken ADR links repair + Last Reviewed + Gotchas with FAILURE-MODES.md#AP-1 link + exemplar:SemanticSearchControl + resolve schema-name convention drift) |
| power-page-deploy | `refine-in-place` (normalize frontmatter + Last Reviewed + Gotchas heading promoting inline ⚠️ warnings + exemplar:external-spa + unified MUST block) |

**Honesty note**: 4 of 6 skills in this batch are in good operational shape (correct prescriptions, distinct charters, current references). Two (`jps-playbook-design`, `pcf-deploy`) warrant `needs-substantive-rewrite` purely on **structural grounds** (length, broken refs) — not on correctness of operational guidance. Both prescribe the right behavior today. The AP-1 origin point (`pcf-deploy`) is correctly fixed.

### Phase 2b cross-skill action items

1. **Repair broken ADR links in `pcf-deploy`** (Light-check failure — F-15.2 forbids dangling references).
2. **Resolve singular/plural Dataverse table-name drift between `jps-scope-refresh` and `jps-playbook-design`** (consistency).
3. **Verify `jps-playbook-audit` (batch 4) charter does not overlap `jps-validate`** (use batch 4 audit findings).
4. **Add JPS family pointer block** to all 5 JPS skills during Phase 2b refinements.
5. **Add `## Gotchas` heading with explicit `See FAILURE-MODES.md#AP-1` cross-reference** to `pcf-deploy` (per spec F-12 bidirectional-link convention).

---

*Audit complete. Findings written read-only. No `.claude/skills/` files modified.*
