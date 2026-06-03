# LegalWorkspace `sprk_corporateworkspace` Consumer Audit (2026-05-26)

> **Task**: R4 task 041 (W-6 / DR-03) — Risk R-6 mitigation per `plan.original.md` §8
> **Author**: Claude Code, R4 sub-agent run
> **Status**: Complete — audit recorded clean for repo-tracked artifacts; Dataverse-side audit deferred to operator
> **Linked from**: [`docs/architecture/LEGALWORKSPACE-RETIREMENT.md`](../../../docs/architecture/LEGALWORKSPACE-RETIREMENT.md)

---

## 1. Purpose

Before retiring the standalone `sprk_corporateworkspace` web resource deploy (operator decision OC-R4-05, 2026-05-25), this audit enumerates all known consumers of the web resource so that retirement does not silently break an unanticipated caller (Risk R-6 in `plan.original.md` §8).

The retirement is **deploy-only**: the LegalWorkspace components, `LegalWorkspaceApp` renderer, and all library code at `src/client/shared/Spaarke.UI.Components/`, `src/client/shared/Spaarke.AI.Widgets/`, etc. continue. Only the Dataverse web resource named `sprk_corporateworkspace` (which loads the bundled `corporateworkspace.html`) stops being deployed going forward.

## 2. Audit methodology

Three search vectors were used inside the repo at branch `work/spaarke-ai-platform-unification-r4`:

1. **Grep for the literal `corporateworkspace`** across `scripts/`, `src/`, `projects/`, `docs/`, `.claude/`.
2. **Grep for `sprk_corporateworkspace`** (Dataverse logical name) across all paths.
3. **Glob + grep over Dataverse solution XML** at `src/dataverse/solutions/**/*.xml` (Customizations.xml, Solution.xml, FormXml, RibbonDiff.xml, SavedQueries) — covers the only Dataverse-XML-tracked entities (`sprk_Container`, `sprk_Document`).

**Limitation (acknowledged gap)**: The repo does **NOT** track the Dataverse Default Solution forms / ribbons / sitemap / model-driven-app navigation for the Spaarke environments. Audits of those artifacts (e.g., a Matter form embedding the web resource directly, or a sitemap SubArea targeting the web resource) cannot be done from local checkout. This audit therefore covers code-tracked consumers only. The operator MUST validate the Dataverse-side gap before declaring the retirement closed.

Recommended Dataverse-side validation (operator):
- In `spaarkedev1`, navigate to Solutions → Default Solution → Web Resources, locate `sprk_corporateworkspace`, click "Show Dependencies" → confirm no forms, ribbons, sitemap entries, or processes list it.
- Run a sitemap export of any Spaarke model-driven app and grep for `sprk_corporateworkspace`.
- Run `pac solution check --path <DefaultSolution.zip>` and look for missing-dependency warnings after the web resource is delisted.

## 3. Findings — code-tracked consumers

### 3.1 Deploy scripts (4 files)

| File | Line(s) | Reference type | Action |
|---|---|---|---|
| `scripts/Deploy-CorporateWorkspace.ps1` | 37-44, 54 | Direct deploy script for `sprk_corporateworkspace` | **Retire** — add early-exit guard (Step 5 of task) |
| `scripts/Deploy-WizardCodePages.ps1` | 56 | Includes `sprk_corporateworkspace` in deploy-loop array | **Retire** — remove from array or guard the entry |
| `scripts/Deploy-AllWebResources.ps1` | 7, 64-68 | Orchestrator references `CorporateWorkspace` component (calls `Deploy-CorporateWorkspace.ps1`) | **Retire** — remove from `$components` array or guard skip |
| `scripts/README.md` | 539 | Documentation table listing the deploy | **Update** — add retirement note pointing to retirement doc |

### 3.2 Source code (1 file)

| File | Line | Reference type | Status |
|---|---|---|---|
| `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx` | 432 | `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_corporateworkspace", data: "mode=todo" }, ...)` — opens self in 90%×90% dialog for the "open To Do dialog" handler | **SELF-REFERENCE — needs migration** (see §4.1) |

This is the WorkspaceGrid's `handleOpenTodoDialog` handler. When the user clicks "Open To Do Dialog" inside an embedded LegalWorkspace (i.e. inside SpaarkeAi after the retirement), the code tries to navigate Xrm to `sprk_corporateworkspace?mode=todo`. After retirement, this navigateTo will fail (404 — web resource not deployed).

This is a code-level consumer of the soon-retired web resource, surfaced by this audit. See §4.1 for migration guidance.

### 3.3 Solution / form / ribbon XML (0 matches)

Glob over `src/dataverse/solutions/**/*.xml`: **0 matches** for `corporateworkspace` or `sprk_corporateworkspace`. The repo's tracked solution XML covers `sprk_Container` and `sprk_Document` entities only — neither references the web resource.

### 3.4 Documentation / project notes (informational only — no remediation needed)

References in `projects/` and `.claude/` are documentation/planning artifacts and do not affect runtime behavior. The retirement doc supersedes them via cross-link. No edits required.

## 4. Triage classification

Per the task POML §3 (Triage audit findings):

### 4.1 (b) needs-migration — 1 finding

**`WorkspaceGrid.tsx` "Open To Do Dialog" handler** — when LegalWorkspace runs embedded inside SpaarkeAi after the retirement, the `handleOpenTodoDialog` callback's `Xrm.Navigation.navigateTo` to `sprk_corporateworkspace?mode=todo` will fail.

**Severity**: Medium — the To Do dialog is reachable from the embedded dashboard's WorkspaceGrid. It's the only known runtime-breakable code-tracked consumer.

**Migration options** (NOT in scope for task 041; surface to operator):

- **Option A — Use SpaarkeAi as the dialog target.** Replace `webresourceName: "sprk_corporateworkspace"` with `webresourceName: "sprk_spaarkeai"` (and pass equivalent query params). Risk: confirms the SpaarkeAi web resource supports a `?mode=todo` deep link.
- **Option B — Open the To Do dialog as an in-page modal** (no Xrm navigation) using the existing modal infrastructure. Aligns better with the SpaarkeAi-as-host model. Likely the cleanest target.
- **Option C — Mount a To Do widget tab via `PaneEventBus` `widget_load`** in the same tab pattern that W-4 / W-5 establish. Aligns with the dashboard-as-engine framing. Likely the long-term answer.
- **Option D — Defer.** If the To Do dialog handler is rarely exercised, defer the fix to a follow-up task and accept a known regression. Operator-only call.

**Recommendation**: Operator picks A/B/C/D as a separate task. Until then, the retirement doc warns the consumer.

### 4.2 (c) needs-operator-input — 1 finding

**Dataverse Default Solution dependencies cannot be enumerated from the repo.** Operator MUST validate `sprk_corporateworkspace` has no live dependencies in Dataverse `Default Solution > Web Resources > Show Dependencies` (and sitemap export) before declaring W-6 closed. See §2 limitations.

### 4.3 (a) safe-to-retire

- All four deploy-script references (§3.1).
- All zero Dataverse solution XML references (§3.3).
- All documentation/planning references (§3.4).

## 5. Decision

Proceed with the deploy-script retirement as planned, with the following caveats recorded in the retirement doc:

1. The `WorkspaceGrid.tsx` "Open To Do Dialog" handler (§4.1) becomes a known broken path post-retirement and requires a follow-up migration task (operator decision: when + which option).
2. Dataverse-side dependency validation (§4.2) is deferred to operator; result must be recorded in the retirement doc before W-6 is marked Complete.

## 6. Audit reproduction commands

For traceability, the exact searches that produced this audit:

```powershell
# In repo root:
# Grep deploy scripts
rg -l "corporateworkspace" scripts/
rg -l "sprk_corporateworkspace" scripts/

# Grep source code
rg -l "corporateworkspace" src/
rg -l "sprk_corporateworkspace" src/

# Grep solution XML
rg --type xml "corporateworkspace" src/dataverse/solutions/
rg --type xml "sprk_corporateworkspace" src/dataverse/solutions/
```

(In this audit, Grep tool was used — same regex semantics as ripgrep.)

---

*Audit complete 2026-05-26. Cross-linked to retirement doc.*
