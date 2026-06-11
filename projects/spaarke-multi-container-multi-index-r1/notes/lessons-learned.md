# Lessons Learned — Spaarke Multi-Container Multi-Index Routing (r1)

> **Project**: `spaarke-multi-container-multi-index-r1`
> **Date**: 2026-06-07
> **Outcome**: Operationally complete. 39 of 43 tasks ✅, 1 deferred, 3 UAT pending in-browser verification.

---

## What worked well

### Sub-agent parallel boundary held across 11 waves
The CLAUDE.md "Sub-Agent Write Boundary" rule (sub-agents cannot write to `.claude/`) AND the file-overlap discipline (each agent told exactly which files to touch) worked cleanly across ~30 sub-agent dispatches. The most notable example: when 5 wizard agents ran concurrently in Wave 5, one agent's report observed sibling agents' modifications to neighboring files and correctly chose not to touch them. Zero merge conflicts across the entire project. The pattern is reproducible.

### Skill-driven deploy ordering paid off
The `pcf-deploy`, `bff-deploy`, `code-page-deploy` skills each surfaced critical pre-deploy gotchas (npm install in worktree; mandatory clean-rebuild of shared lib `dist/`; 5-location version bump; XSD validation rules). Following them verbatim caught issues we would have wasted hours diagnosing otherwise.

### Wave structure with explicit dependencies
Decomposing the 43 tasks into 11 numbered waves (each with explicit prerequisite tasks) made dispatch decisions mechanical. No "what should I do next?" moments. The TASK-INDEX.md parallel-group annotations were load-bearing.

---

## What surprised us

### Caller-wiring gaps recur — task-create needs a "wire the caller" sub-task pattern

This bit us **three times**:
1. **CreateProjectWizard main.tsx**: Task 022 added a new `resolveUserBuDefaults?` optional prop to the component, but no agent wired the host (`src/solutions/CreateProjectWizard/src/main.tsx`) to pass it. Discovered post-Wave-5; fixed inline before the wave commit.
2. **DocumentUploadWizard upload orchestrator**: Task 026 created the resolver chain helper; task 027 extended `createDocuments` to accept the resolved value — but no agent wired the call site in `uploadOrchestrator.ts`. Discovered in UAT; fixed post-deploy.
3. **Matter wizard cascade silently failing**: Task 021 added internal cascade in `matterService.ts`, but it used a slightly different code path than the working WorkAssignment pattern. The pattern divergence caused silent runtime failure that unit tests didn't catch (mocks were too clean). Discovered in UAT; aligned to the proven pattern.

**Root cause pattern**: when a task adds an OPTIONAL parameter or a NEW helper, sub-agents tend to interpret their scope narrowly ("just add the optional param") and treat caller wiring as "main session will pick that up". But if no explicit caller-wiring task is filed, the wiring slips. Recommend that future projects either:
- Decompose "add helper" + "wire caller" as two explicit tasks
- Or instruct each implementation task to also produce the caller diff (single PR, two file scopes)

### Spec assumptions can be wrong (CreateInvoiceWizard doesn't exist)
Spec.md Assumption A1 stated that `CreateInvoiceWizard` follows the same pattern as `CreateMatterWizard`. Task 023's agent correctly verified empirically that the wizard doesn't exist at all in the codebase, and chose to produce a handoff note rather than fabricate one. **Lesson**: agents should be encouraged to do the empirical verification step explicitly (the agent did), and unblocked to deviate from the spec when reality contradicts assumptions (the agent did, gracefully).

### TS2786 latent issue exposed by fresh install (`@types/react` cross-version)
The PCF references shared components via `.d.ts` from `@spaarke/ui-components`. That shared lib was bumped (by an unrelated security-CVE PR) from `@types/react@16` to `@types/react@19`. The lib's emitted `.d.ts` declares components as `React.FC<P>`; React 19 widens `FC` return to `ReactNode | Promise<ReactNode>`, which is invalid as a React-16-JSX `Element | null`. v1.1.73 deployed fine because its build env had stale lockfile-resolved types. Our fresh worktree install surfaced the conflict; cost us ~30 minutes investigating + applying the `paths` mapping fix in PCF tsconfig.json.

**Lesson**: when bumping `@types/X` in a shared lib that's consumed by multiple React-version targets, run a build against each consumer immediately to catch the cross-version mismatch.

### Dataverse XSD rejects apostrophes in `description-key`
PCF solution import failed on `description-key="...the scope record's sprk_searchindexname..."`. Dataverse XSD `noAposStringType` rejected the apostrophe. **Lesson**: PCF manifest text fields are conservatively constrained — no apostrophes, no certain unicode quotes. Worth a constraint or lint rule.

### Two-overload design > optional parameter for NFR-02
Task 010's agent chose to add a NEW 3-arg `GetSearchClientAsync(tenantId, indexName, ct)` overload alongside the existing 2-arg overload, instead of adding `indexName` as a new optional parameter. Reason: optional parameters with default values break Moq expression-tree setups (CS0854) in existing tests, which would have forced test modifications and violated "existing tests pass UNMODIFIED" (NFR-02). The two-overload design is more verbose but rigorously preserves backward compat. Adopt this pattern for future BFF interface extensions.

### Adapter wrapper introduced silent failure (Matter cascade)
The Matter wizard's cascade used `_toWebApiLike(this._dataService)` to convert `IDataService` into `IWebApiLike` before calling `resolveUserBuDefaults`. WorkAssignment passed `this._dataService` directly (structural superset). Same shape on paper, but Matter failed in production while WA succeeded. Root cause was never fully diagnosed (no browser console access); pragmatic fix: align both wizards on the proven pattern. **Lesson**: when two near-identical code paths give different runtime results, the divergence isn't free — eliminate it.

---

## What was out of scope (surfaced for follow-up)

### AI Search indexer pipeline (separate project)
UAT confirmed files land in SharePoint Embedded (`sprk_graphdriveid` populated) but are NOT being indexed by Azure AI Search. This project routes search REQUESTS to the correct index; it doesn't configure the indexer's datasource or schedule. **Follow-up**: operator inspects each AI Search index's indexer config; consider whether the BFF should trigger an indexer run after document creation (architectural decision, not a bug fix). Documented in `notes/handoffs/post-uat-fixes-and-indexer-finding.md`.

### Drift audit script has a schema-assumption bug
Task 052's `Audit-MultiContainerMultiIndex-Drift.ps1` hardcodes `sprk_name` as the entity name attribute for ALL six entities. `sprk_matter` actually uses `sprk_matternumber`. Documented in `notes/handoffs/053-backfill-dryrun.md`. ~10-line fix in the script's `$entityConfigs` block.

### Inconsistent parameter naming across backfill scripts
`Backfill-...-ParentRecords.ps1` takes `-EnvironmentUrl`; `Backfill-...-Documents.ps1` takes `-Environment`. Two different sub-agents made different choices. Cosmetic; suggest aligning on `-EnvironmentUrl` in a polish pass.

---

## What we'd do differently next time

1. **File a "wire the caller" task explicitly** whenever a task adds an optional parameter or new public helper. The implementation agent stops at the helper; the caller-wiring agent picks it up. Don't leave it to "main session figures it out" — it slips.
2. **Run a fresh-clone build verification gate** when bumping shared dependency types (`@types/X`). The TS2786 issue would have surfaced in CI rather than mid-deploy.
3. **Encourage empirical-verification-first** for spec assumptions. Task 023's agent set the gold-standard pattern: verified, found contradiction, produced handoff instead of fabricating. Codify this as a standard sub-agent instruction.
4. **Test patterns should match production patterns**, not stylized alternatives. Task 021's INV-5 unit test mocked `IWebApiLike` directly, which masked the runtime failure mode in production where the real `IDataService.retrieveRecord` path was used.
5. **For cross-React-version shared libs**: consider making shared lib components accept their own JSX-typed wrapper, or pin `@types/react` at the consumer level (PCF) and avoid hoisting the shared lib's types into resolution. The `paths` mapping fix works but is a workaround.

---

## Statistics

- **43 tasks**: 39 ✅ + 1 🚫 deferred (Invoice wizard doesn't exist) + 3 🔲 UAT pending
- **11 waves**: 5 sequential serial + 6 parallel (max 6 concurrent agents)
- **~30 sub-agent dispatches** across the project
- **Wave 1-7**: code work; Wave 8-11: deploys; post-UAT: 2 bug fixes + 1 finding
- **15 commits** on `work/spaarke-multi-container-multi-index-r1`
- **Test additions**: ~190 new unit tests across BFF (Sprk.Bff.Api.Tests 6121/0/109) + shared lib (EntityCreationService + per-wizard cascade tests) + code-page (168 passing) + PCF (54 passing)
- **Code surfaces touched**: BFF Services/Ai (3 services + endpoint), 5 wizard service files, DocumentUploadWizard orchestrator, SemanticSearchControl PCF (manifest + service + nav + control), SemanticSearch code page (App + hooks + types + utils), 3 PowerShell backfill scripts, 1 operator runbook, 1 arch doc update
- **No regressions in pre-existing test suite** (NFR-02 verified across all changes)
