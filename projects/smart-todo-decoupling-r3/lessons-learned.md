# smart-todo-decoupling-r3 — Lessons Learned

> **Date**: 2026-06-10
> **Status**: R3 complete (PR #373 squash-merged to master `e328beaf`)
> **Outcome**: 32/46 tasks closed end-to-end + 4 wrap-up tasks in follow-up PR. 8 tasks deferred or blocked by external action (AAD scope, SPE platform changes). Net: R3 scope shipped.

---

## What went right

1. **Audit task (001) was load-bearing.** Spending the first wave on `eventtodo-reference-audit.md` surfaced 4 escalation items before any code was written. Without it, the Phase 1 schema cut would have broken `ExternalDataService`, `external-spa`, `TodoGenerationService`, and the SmartTodo Code Page silently. The user's "include the SPA" decision happened because audit forced the question.

2. **Null-Object scaffolding (task 018, ADR-032) decoupled deploys from feature gating.** The Phase 7 sync engine (tasks 016, 061–066) didn't merge because the AAD `Tasks.ReadWrite` scope is still pending tenant admin. With Null-Object scaffolding in place, the BFF deployed cleanly anyway — flag stays off, no startup failures. Without ADR-032, R3 would have been gated on D-1 (AAD scope) before merge.

3. **Hoisting Kanban + TodoDetail to `@spaarke/ui-components` (NFR-02) paid off twice.** Once for SmartTodo Code Page, once when retiring LegalWorkspace's local `CreateTodo` (task 013 — discovered to be dead code, deleted vs. refactored). The hoist removed 4 LegalWorkspace components without any consumer needing change.

4. **Polymorphic-resolver-as-pattern (ADR-024).** Reusing `sprk_communication`'s 11-lookup + 4-resolver shape verbatim meant `sprk_todo` got `AssociateToStep`, `PolymorphicResolverService`, and `TodoRegardingUpdateBuilder` for free. The bytes-on-disk of the regarding-write logic is one place, used by two entities, and will be three when R4 lands the resolver PCF.

5. **`/design-to-spec` + `/project-pipeline` produced runnable plan in one pass.** 38 tasks → 41 tasks after audit-driven additions (006, 007, 008) → 46 with mid-project additions (013, 014, 070a, 070b). Re-decomposition during execution worked because tasks were small enough to add without reshuffling waves.

## What was harder than expected

1. **`sprk_eventtodo` entity delete blocked by 26 appmodulecomponent refs.** Task 005 turned into a multi-hour rabbit hole hitting RemoveAppComponents API, direct DELETE on appmodulecomponent, etc. User authorized deferral. **Lesson**: when an entity has been referenced by multiple model-driven apps over its lifetime, the cleanest delete path is maker portal cleanup per-app, not API. Direct API DELETE is reserved for fresh entities with no app references.

2. **`sprk_event` `aiskillconfig` blockers (task 004).** Power Platform Copilot Form Fill auto-creates `aiskillconfig` records as ComponentType=10314 in `solutioncomponents` for every field a user opts out of. These block attribute deletes with a generic "referenced by 1 other component" error. The `RetrieveDependenciesForDelete` response identifies them only by GUID + numeric componenttype — translating GUID→aiskillconfigs required experimentation. **Recurrence risk**: every future schema cut in environments where users have touched the Copilot opt-out toggle. Reusable script: `scripts/Remove-AiSkillConfigBlockers.ps1`.

3. **Outlook add-in: `.env` placeholder leaked into production bundle.** A copy of `.env.example` was committed as `.env` (in `src/client/office-addins/`) with all `your-bff-api-url-here` placeholders intact. Webpack DefinePlugin substituted these into the production bundle, breaking the Save email flow with `ERR_NAME_NOT_RESOLVED`. **Fix applied**: deploy script (`Deploy-OfficeAddins.ps1`) now refuses to build if `.env` contains any `your-…-here` placeholder. **Lesson**: never check in a `.env` (even with placeholders); `.env.example` is the only canonical template.

4. **Manifest version vs APP_VERSION drift.** Footer showed `v1.0.6 (Jun 9, 2026)` while manifest was at `1.0.19.0`. Two separate constants drifted because nothing tied them together. **Fix applied**: APP_VERSION bumped to match manifest. **Followup for R4**: drive APP_VERSION from manifest at build time, not as a hardcoded string.

5. **`AZURE_CLIENT_ID` env var not set on App Service (auth-v2 gap).** `DefaultAzureCredential()` with no explicit ClientId can't resolve a UAMI when no system-assigned MI is attached. This silently broke every BFF call that initialized a SecretClient or Graph client without explicitly passing the UAMI client ID. The SDAP SPE Admin app endpoints all 500'd until this was fixed mid-session. **Fix applied**: `AZURE_CLIENT_ID=5967251e-...` added to App Service settings. **Lesson**: when migrating to MI on App Service, ALWAYS set `AZURE_CLIENT_ID` env var even if only one UAMI is attached. `DefaultAzureCredential` defaults to system-assigned MI lookup first, which fails noiselessly when none exists.

6. **SPE container-type registration: BFF MI was missing.** The `sprk_event.sprk_todoflag` model used OBO (user-acting) for SPE upload, so each user's own container permissions sufficed. When auth-v2 swapped server-outbound to MI, the MI's client ID needed to be registered with the SPE container type — that step was missed and never documented. The SDAP SPE Admin App built to handle this has bit-rotted: SharePoint admin REST `/_api/v2.1/storageContainerTypes/{ctid}/applicationPermissions` now returns 401 invalidToken even with the owning app's `Container.Selected`-roled token; the Graph beta equivalent returns "API not found". **Status**: not blocking R3; needs Microsoft platform investigation. Deferred to platform/R6 ownership.

## Process notes

- **Wave 1 NOT auto-started after project-pipeline init.** User explicitly stopped after Step 4 (commit + push, no draft PR). Right call — Phase 1 is destructive Dataverse schema work that benefits from explicit human gating between waves.
- **Sub-agent budget hit org monthly cap mid-session** (waves 11–12). Inline execution continued without sub-agents and got everything to the merge line, but with slower iteration. **Lesson**: budget sub-agent use for the highest-leverage parallel work (Phase 5 11-form subgrids, Phase 8 Outlook add-in), not for serial tasks.
- **3 PRs eventually merged** for R3: PR #373 (main scope, squash), this wrap-up PR (cleanup tasks), and the previous CI-gate-fix commits absorbed into PR #373 before squash.
- **R4 scope grew during UAT.** Smart move to spin a new project early when the UAT feedback list crossed ~5 substantive items.

## Carry-forward items

| Item | Status | Owner |
|---|---|---|
| Task 005 — `sprk_eventtodo` entity delete | Deferred (orphan in dev, not in solution exports) | Maker portal cleanup, no urgency |
| Task 015 — AAD `Tasks.ReadWrite` delegated scope | Blocked on tenant admin | Tenant admin action |
| Tasks 016, 061–066 — Phase 7 MS To Do sync engine | Blocked on 015 | Future project (R5?) |
| 10 deferred parent-form ribbons | XMLs drafted, deploy pending | R4 OD-6 decision |
| SPE MI container-type registration | Platform issue, BFF endpoint broken | R6 / Microsoft support |
| R4 — SmartTodo UX enhancements | `design.md` draft, 6 open decisions pending | New project, blocked on user input |

## Decisions worth re-using in R4 and beyond

1. **Polymorphic resolver as PCF (R4 OD-1)** — if going PCF, follow the same shape that `sprk_communication` uses and `sprk_todo` now uses. The shared TypeScript service `PolymorphicResolverService.applyResolverFields` can be the core; PCF wraps it as a field control.

2. **Modal pattern with "To Do main form" (R4 OD-3)** — host via `Xrm.Navigation.navigateTo({pageType: "entityrecord"})` is cleaner than iframing the main form inside a Code Page. Test both during R4 design-to-spec.

3. **`.env.example` should NEVER become `.env` in repo.** Add a `.gitignore` enforcement: `**/office-addins/.env` already gitignored; verify no future product surface allows it.

4. **`AZURE_CLIENT_ID` is a binding setting** when using UAMI + `DefaultAzureCredential`. Add to BFF deployment runbook (auth-deployment-setup.md §3 explicit setting).

5. **SPE admin operations should be done via the SPE Admin Code Page UI**, not direct API calls — when Microsoft tightens APIs, the Code Page wraps the right pattern. R4 / R6 should verify the Code Page works against current Microsoft APIs before another project depends on it.
