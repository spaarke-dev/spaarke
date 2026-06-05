# Needs-a-Project Backlog

> **Purpose**: Items surfaced during "small fix" sessions that are too large for an in-session fix and warrant their own project. Each entry should have enough context that a future session (or `/design-to-spec` → `/project-pipeline`) can pick it up cold.
>
> **Convention**: Newest entries on top. Use the template at the bottom. When an entry is picked up as a real project, move it to `## Promoted` with a link to the project folder.

---

## Active Referrals

### 8. Document-relationship visualization for work-assignment records

**Surfaced**: 2026-06-04 while fixing SemanticSearch on work-assignment forms.

**Status**: not started — small backend fix + UI plumbing

#### Problem

[`VisualizationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Visualization/VisualizationService.cs) builds the "document relationship graph" feature with hardcoded branches for matter (line 367), project (line 377), invoice (line 386), and email-thread (line 394). No work-assignment branch — so if a user views the document-relationship visualizer rooted at a sprk_workassignment, sibling/related docs won't appear.

The interface gap that previously blocked this was closed today (2026-06-04 BFF deploy): `IDocumentDataverseService.GetDocumentsByWorkAssignmentAsync` now exists. The remaining work is wiring `VisualizationService` to call it for the work-assignment entity type, plus whatever UI/PCF surface invokes the visualizer needs to support work-assignment as a root entity.

#### Recommended approach

1. Add `GetDocumentsByWorkAssignmentAsync` consumer to `VisualizationService` mirroring the matter/project/invoice pattern at lines 404-466.
2. Add a workassignment branch to the upstream switch (line 367-394) that picks the right `GetDocumentsBy...` based on the source document's parent entity.
3. Check whatever PCF / Code Page renders the visualization — it needs to accept `sprk_workassignment` as a valid root entity type.
4. Same pattern for any future entity types (events, etc.) — consider parameterizing this rather than per-entity branches.

#### Impact / consumers

- `VisualizationService.cs` — 2 small additions
- The PCF / Code Page that renders the doc-relationship graph (need to confirm which one — likely `DocumentRelationshipViewer` per earlier session memory)
- The form / button that launches it from a work-assignment record (if it doesn't currently exist)

#### Scope estimate

1–2 tasks, ~half day. Smaller if no PCF UI changes needed.

#### Out of scope

- The interface + DataverseWebApiService/DataverseServiceClientImpl additions were shipped 2026-06-04 to unblock SemanticSearch. This backlog is the remaining VisualizationService consumer wiring.

---

### 7. Normalize Dataverse GUIDs at the Xrm navigation adapter

**Surfaced**: 2026-06-04 during CreateWorkAssignment "Error in query syntax" investigation.

**Status**: not started — small architectural fix, prevents bug-class recurrence

#### Problem

`Xrm.Utility.lookupObjects` returns selected record GUIDs **wrapped in curly braces** (e.g. `{39CDE3E3-9D15-…}`). OData `@odata.bind` URLs and `$ref` payloads reject braced GUIDs with HTTP 400 "Error in query syntax". Today's fix at [PolymorphicResolverService.ts:185-225](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts#L185-L225) and inline at [CreateMatterWizard.tsx:95-145](../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/CreateMatterWizard.tsx#L95-L145) cleans braces at the **sinks** — every `@odata.bind` consumer that touches lookup-derived GUIDs has to remember to call `.replace(/[{}]/g, '').toLowerCase()`. Forget it once, get a runtime HTTP 400.

The root cause is at the **source**: the Xrm adapter [`xrmNavigationServiceAdapter.ts`](../../src/client/shared/Spaarke.UI.Components/src/utils/adapters/xrmNavigationServiceAdapter.ts) returns `r.id` raw from `lookupObjects` results without normalization. Cleaning there once means every downstream consumer (matter wizard, work-assignment wizard, future wizards, future associate-to flows in non-wizard contexts) gets bare GUIDs by default, and the bug class can't recur.

Latent vulnerable site already identified during the 2026-06-04 audit: [`CreateEventWizard/eventService.ts:164,174`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/eventService.ts#L164) uses `formValues.regardingRecordId` raw in `@odata.bind`. Today the field is sourced from clean `createRecord` responses (matter/project IDs), but if any future caller wires a lookup result into it, the bug returns.

#### Recommended approach

In `xrmNavigationServiceAdapter.openLookup`, normalize the `id` field of each `LookupResult` before returning:

```typescript
return results.map(r => ({
  id: r.id.replace(/[{}]/g, '').toLowerCase(),  // <-- add
  name: r.name,
  entityType: r.entityType,
}));
```

Also audit other adapter entry points (e.g., `getGlobalContext().userSettings.userId` which is also braced — used in [`themeStorage.ts:494`](../../src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts#L494) and elsewhere) and normalize consistently. The `INavigationService` / `IDataService` interfaces should document the contract: **"GUIDs returned to consumers are always bare-lowercase format"**.

#### Impact / consumers

- `xrmNavigationServiceAdapter.ts` — one-line change.
- Every wizard solution that uses the adapter — needs rebuild + redeploy for the change to take effect (`Deploy-WizardCodePages.ps1` deploys whatever `dist/` exists, so stale builds keep the old behavior).
- Tests / mocks may need updating if any assert on the braced format.
- Defensive cleaning at sinks (today's PolymorphicResolverService + CreateMatterWizard fix) can stay as belt-and-suspenders — costs nothing and protects against future consumers that don't go through the adapter.

#### Risks / considerations

- Lowercase normalization changes the casing in `name`-equality matches if any code compares GUIDs case-sensitively. Audit grep for `=== r.id` / `=== recordId` literal comparisons.
- The braced format is what Dataverse uses internally in some surfaces (system jobs, audit logs). Make sure no downstream code re-introduces braces by passing the cleaned GUID back into a system that re-wraps.

#### Scope estimate

1–2 tasks: adapter fix, audit other lookup adapter surfaces (userId etc.), rebuild + redeploy all wizards. Probably half a day if rebuild/deploy is mechanical.

#### Out of scope (already done 2026-06-04)

- Sink-level cleaning at `PolymorphicResolverService.applyResolverFields` and inline at `CreateMatterWizard.associateToRecord`. These stay as defense in depth.

---

### 6. Auto-generate project numbers in CreateProjectWizard

**Surfaced**: 2026-06-04 during Create New Project testing.

**Status**: not started — small fix, candidate for next session

#### Problem

`CreateProjectWizard` does not populate the `sprk_projectnumber` field on the created project. Matter wizard does — generates `{sprk_mattertypecode}-{6-digit random}` client-side at [matterService.ts:218-237](../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts#L218-L237). Test matter from this session showed `CMRCL-144988`.

`sprk_projectnumber` exists in Dataverse and is referenced as the project's stable reference identifier by the sync jobs ([DataverseIndexSyncService.cs:46-47](../../src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs#L46-L47), [RecordSyncJob.cs:169-170](../../src/server/api/Sprk.Bff.Api/Services/Jobs/RecordSyncJob.cs#L169-L170)). It's just being left null by the wizard, so newly-created projects can't be referenced by number downstream.

#### Recommended approach

Mirror the matter pattern in `projectService.ts`:

```typescript
// Generate project number: {projectTypeCode}-{6 random digits}
if (form.projectTypeId) {
  try {
    const projectTypeRecord = await this._dataService.retrieveRecord(
      'sprk_mattertype_ref',  // verify — project may reuse sprk_mattertype_ref or have its own ref table
      form.projectTypeId,
      '?$select=sprk_mattertypecode'  // verify field name on whichever ref table
    );
    const typeCode = (projectTypeRecord?.sprk_mattertypecode as string) ?? '';
    if (typeCode) {
      const random6 = String(Math.floor(100000 + Math.random() * 900000));
      entity['sprk_projectnumber'] = `${typeCode}-${random6}`;
    }
  } catch (err) {
    console.warn('[ProjectService] Could not look up project type code for numbering:', err);
  }
}
```

#### Open questions before implementation

1. **Project type ref table**: does project use `sprk_mattertype_ref` (shared with matter) or a separate `sprk_projecttype_ref`? Looking at the playbook JSON's `extractionSchema` for `projectTypeName` it says *"Must match a value in sprk_mattertype_ref"* — suggests shared. Confirm.
2. **Code field name**: if project type uses a separate ref table, does it have `sprk_projecttypecode` (parallel to `sprk_mattertypecode`)?
3. **Collision avoidance**: 6-digit random has a 1-in-900k collision rate per type code. Matter currently lives with this. Acceptable for project too, or upgrade both to use a sequenced counter / GUID-tail / timestamp suffix?

#### Impact / consumers

- `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectService.ts` — primary change.
- Anywhere downstream that expects `sprk_projectnumber` to be populated — BFF sync jobs already reference it; if it's been null until now, sync jobs may be skipping projects silently.

#### Scope estimate

~1 task, ~1 hour if questions 1+2 resolve cleanly (just mirror the matter pattern). Grows to 2–3 tasks if the type-code field is missing on the ref table (schema change needed) or if collision avoidance gets re-opened across both wizards.

#### Out of scope

- Server-side auto-number plugin: out of scope for this entry — the existing pattern is client-side and works fine. If we ever want to make this server-authoritative (e.g., for projects created via BFF, Office add-in, email-to-project), that's a separate referral.

---

### 5. Migrate pre-fill playbooks to JPS-native definitions

**Surfaced**: 2026-06-04 while diagnosing referral #3 (project pre-fill all-null).

**Status**: not started

#### Problem

Both pre-fill playbooks ([`create-new-project-pre-fill.json`](../x-ai-json-prompt-schema-system/notes/playbook-definitions/create-new-project-pre-fill.json) and the matter equivalent) use Spaarke's `playbook-definition/v1` schema and declare `actionCode: "ACT-008"` on their AIAnalysis node — but the **real** behavior is encoded inline in the node's `configJson.extractionSchema` + `extractionRules` + `parameters`. ACT-008 in the JPS catalog is *"General Legal Document Review"* — outputs `executiveSummary`, parties, risk flags. It has nothing to do with project / matter field extraction. So the action code is metadata that lies; the inline `configJson` completely overrides what ACT-008 would normally do via its JPS definition.

Consequences:
- **Audit / discovery is broken**: catalog / AI search shows ACT-008 = General Document Review, but in this context it's actually doing pre-fill field extraction. Anyone running a "what playbooks use ACT-008?" query gets wrong answers.
- **Schema not reusable**: extraction schema lives inline in the playbook node, not in a cataloged JPS action. Can't reuse across matter / project / future entity wizards.
- **No JPS validation**: `/jps-validate` doesn't see these. Typos and shape errors only surface at runtime as silent all-null responses.
- **No `$choices` resolution**: project type and practice area lookups go out as free-form strings; the frontend fuzzy-matches against Dataverse. JPS native `$choices` would constrain decoding at the LLM call so the model emits a Dataverse GUID directly — better accuracy, no fuzzy match needed.
- **Not indexed in `spaarke-rag-references`** AI Search — invisible to playbook discovery tooling.
- **Likely contributes to referral #3** (project pre-fill returns all-null). Free-form prompts without JPS examples, constraints, and validation are exactly the kind of prompt that produces empty output when input drifts from the expected shape.

#### Recommended approach

1. Use [`/jps-action-create`](../../.claude/skills/jps-action-create/SKILL.md) to author proper JPS actions:
   - `ACT-PROJECT-PREFILL` (or whatever the next sequential code is) — extracts the same fields, but with `$choices: {$ref: "sprk_mattertype_ref"}` for projectTypeName/practiceAreaName lookups, and full JPS examples to seed determinism.
   - `ACT-MATTER-PREFILL` — analogous for matter.
2. Validate with [`/jps-validate`](../../.claude/skills/jps-validate/SKILL.md).
3. Use [`/jps-playbook-design`](../../.claude/skills/jps-playbook-design/SKILL.md) to wire them into playbooks (one node each, no orchestration needed for a single-action pre-fill).
4. Deploy via the JPS pipeline (NOT `Deploy-Playbook.ps1`, which targets the older schema — confirm).
5. Update `MatterPreFillService` / `ProjectPreFillService` to deserialize the new response shape if it differs (the `$choices` path may emit GUID directly instead of name, simplifying the frontend).
6. Retire the inline `configJson.extractionSchema` workaround.
7. Audit any other playbook node that declares an `actionCode` but overrides via inline configJson — same anti-pattern likely exists elsewhere.

#### Impact / consumers

- `MatterPreFillService` + `ProjectPreFillService` (BFF) — response-shape changes possible.
- `CreateMatterWizard/CreateRecordStep.tsx` + `CreateProjectWizard/CreateProjectStep.tsx` — `useAiPrefill` `fieldExtractor` / `lookupResolvers` config; if `$choices` resolves GUIDs directly, fuzzy matching becomes unnecessary.
- Playbook deploy tooling: confirm `/jps-playbook-design` deploys correctly to Dataverse; if `Deploy-Playbook.ps1` is JPS-aware, can keep using it; otherwise replace.
- AI search index (`spaarke-rag-references`) — re-index after deploy.

#### Risks / considerations

- Changing the response contract requires coordinated BFF + frontend updates; deploy in lockstep.
- `$choices` constrained decoding requires the lookup table values to be indexed (or queried) — verify the JPS runtime can handle the project type / practice area cardinality (probably small, no issue).
- Need to compare accuracy of fuzzy-match-on-name vs $choices-GUID-direct on a test corpus before flipping over. Possible to keep both paths during a migration window.

#### Scope estimate

5–8 tasks: author 2 JPS actions, validate, design 2 JPS playbooks, deploy, update BFF deserialization, update frontend `useAiPrefill` config, end-to-end test, retire the old non-JPS playbooks. ~1 week.

#### Out of scope

- Phase 1 unblock of referral #3 (redeploying the existing non-JPS JSON to spaarkedev1) is a separate, immediate fix. This referral is the architectural correction.

---

### 4. BFF pre-fill services should resolve playbooks by name, not by GUID

**Surfaced**: 2026-06-04 while diagnosing referral #3 (project pre-fill all-null).

**Status**: not started

#### Problem

`MatterPreFillService` and `ProjectPreFillService` (and `WorkspaceAiService` for AI summary) reference target playbooks by **hardcoded GUID** with optional config overrides:

- [`MatterPreFillService.cs:43-44`](../../src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs#L43-L44) → `2d660cad-d418-f111-8343-7ced8d1dc988`
- [`ProjectPreFillService.cs:37-38`](../../src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs#L37-L38) → `3f21cec1-7d19-f111-8343-7ced8d1dc988`
- [`WorkspaceOptions.cs:43`](../../src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs#L43) → `18cf3cc8-02ec-f011-8406-7c1e520aa4df` for AI summary

Dataverse mints a new GUID every time a playbook record is created. [`Deploy-Playbook.ps1`](../../scripts/Deploy-Playbook.ps1) looks up by `sprk_name` and recreates with a fresh GUID when `-Force` is used. So:

- **New environment deploys** require updating either the source defaults or the per-environment `Workspace:*` config override. Anyone who forgets gets silent all-null responses (the BFF's empty-result fallback path), looks like a feature outage with no obvious error.
- **Within the same environment**, anyone re-running `Deploy-Playbook.ps1 -Force` invalidates the GUID. This is almost certainly how referral #3 happened — the hardcoded GUID `3f21cec1-…` no longer corresponds to any playbook in spaarkedev1's library.
- The hardcoded GUIDs are dev-environment values baked into source. They'll never match prod / staging / a customer environment unless the override config is meticulously managed per-env.

#### Recommended approach

Switch from GUID-by-config to **name-by-config** (or stable code-by-config). Resolve the playbook at service startup (or on first request, cached) by querying Dataverse for `sprk_name eq '{configured-name}'`:

```csharp
// Pseudo-shape
public class ProjectPreFillService
{
    private const string DefaultPlaybookName = "Create New Project Pre-Fill";
    private readonly Lazy<Task<Guid>> _resolvedPlaybookId;

    public ProjectPreFillService(IDataverseClient dv, IOptions<WorkspaceOptions> opts, ...)
    {
        var name = opts.Value.ProjectPreFillPlaybookName ?? DefaultPlaybookName;
        _resolvedPlaybookId = new(() => ResolveByName(dv, name));
    }
}
```

Better still: if `sprk_analysisplaybook` has (or could have) a stable code column like `sprk_systemcode` or `sprk_uniquename`, use that — it doesn't change if someone renames the display label. If no such column exists, this referral could include a small schema change to add one.

#### Impact / consumers

- `Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs`
- `Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs`
- `Sprk.Bff.Api/Services/Workspace/WorkspaceAiService.cs` (AI summary)
- `Sprk.Bff.Api/Configuration/WorkspaceOptions.cs` — rename keys (Name not Id), keep both during migration.
- Probably other services that hold hardcoded playbook GUIDs — audit broadly. (Quick grep: search for `Guid.Parse(".*-.*-.*-.*-.*")` across `Services/Ai/` and `Services/Workspace/`.)
- Deploy guides / operator runbooks — drop the "update playbook GUIDs per env" step entirely.

#### Risks / considerations

- Resolving by name on every cold start adds one Dataverse query. Cache aggressively (Lazy<Task<Guid>> as sketched, or IMemoryCache with long TTL). Invalidation only matters if names change, which is rare.
- Name collisions: enforce uniqueness on `sprk_name` for `sprk_analysisplaybook`, or add a code column with unique constraint. Without it, name lookup is ambiguous.
- During migration, support both GUID-by-config and Name-by-config — let operators flip over at their pace.
- Same anti-pattern almost certainly exists in other services (ScopeResolverService, knowledge-source lookups, etc.). Audit and fix together if the cost is comparable.

#### Scope estimate

3–5 tasks: audit hardcoded GUIDs across BFF AI services, design the resolution helper (probably a small `IPlaybookResolver` service), migrate the 3 known callers, update config + runbook docs, optional schema change for a stable code column. ~3–5 days.

#### Out of scope

- The immediate fix for referral #3 (redeploy the existing JSON, point the existing config override at the new GUID) doesn't need this referral resolved. This is the durable architectural correction so the bug class can't recur.

---

### 3. BFF project AI pre-fill endpoint returns all-null

**Surfaced**: 2026-06-04 during CreateProjectWizard hang investigation.

**Status**: not started — UI hang fixed (see `useAiPrefill` 2026-06-04 change); BFF behavior remains

#### Problem

The `POST /api/workspace/projects/pre-fill` BFF endpoint accepts the uploaded file(s), returns HTTP 200, and responds with every extractable field set to `null`:

```js
{ projectTypeName: null, practiceAreaName: null, projectName: null,
  description: null, assignedAttorneyName: null, ... }
```

Compare with the matter equivalent (`/api/workspace/matters/pre-fill`), which returns populated fields for the same kind of input — so this is project-side specific.

**Established 2026-06-04** (post-code-read, not speculation):
- The endpoint DOES invoke a playbook via `RequireAi().ExecutePlaybookAsync(...)` per refined ADR-013 — see [`ProjectPreFillService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs).
- Default playbook ID (hardcoded): `3f21cec1-7d19-f111-8343-7ced8d1dc988` — labeled *"Create New Project Pre-Fill" (Extract Project Fields, gpt-4o)*. Overrideable via `_workspaceOptions.ProjectPreFillPlaybookId`.
- The service emits 3 distinct log lines for the failure modes ([line 314 RunFailed](../../src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs#L314), [line 326 completed-but-empty](../../src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs#L326), [line 336 timeout](../../src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs#L336)). The all-null symptom matches **line 326**: *"Playbook completed but no pre-fill data was produced"* — i.e. the playbook executed without error, just produced no extractable output.

So the playbook IS being invoked and IS completing. The remaining question is whether its instruction / output schema is wrong, or whether the playbook simply isn't deployed correctly in the target environment (which would also produce empty output if the BFF's fallback behavior is permissive).

The UI is no longer harmed by this — `useAiPrefill` now always emits a completion signal so the wizard advances even with all-null fields — but the user-visible feature ("upload a file, get the project form pre-filled") is silently broken.

#### Recommended approach

1. Pull BFF App Insights logs for the test request — look for `"Playbook completed but no pre-fill data was produced. RequestId=…"` (vs the timeout/RunFailed sibling messages). Confirms which failure mode you're in.
2. Verify playbook `3f21cec1-7d19-f111-8343-7ced8d1dc988` exists and is published in spaarkedev1's Dataverse. If missing → that's the fix.
3. If deployed: diff its instruction + output-field schema against the matter playbook (`MatterPreFillService` uses an analogous default ID; check). Likely either the project playbook's output field names don't match what `ProjectPreFillService` deserializes into `ProjectPreFillResponse`, or the prompt is too narrow.
4. Add an integration test asserting the endpoint returns ≥1 non-null field for a known-good fixture file (a "project charter" sample document). Use a fixture text that seeds determinism rather than depending on free-form LLM behavior.
5. Consider making the endpoint return a structured `"reason"` field when nothing was extracted, so the UI can show *"AI couldn't extract fields from this file"* instead of a silent empty form.

#### Impact / consumers

- `src/solutions/CreateProjectWizard/` — primary symptom.
- `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectStep.tsx` — calls the endpoint; gracefully handles empty after today's fix.
- BFF endpoint definition + service implementation in `Sprk.Bff.Api`.
- Whatever AI playbook system loads project playbooks (per `.claude/skills/jps-playbook-design/` workflow).

#### Risks / considerations

- Adding a real playbook may require model-deployment changes (Azure OpenAI quota, etc.) — not a small-fix task.
- Test fixtures need to live somewhere stable; reuse the matter wizard's fixture pattern if one exists.

#### Scope estimate

3–5 tasks: trace endpoint, audit playbook config, fix/deploy missing playbook, add integration test, add structured "no extraction" reason. ~2–4 days.

#### Out of scope (already done 2026-06-04)

- `useAiPrefill` always calls `onApply` now (even with empty fields), so the wizard never hangs regardless of BFF response.
- CreateMatterWizard rebuilt with the same hook for resilience parity (matter wasn't hanging, but the bug class is identical and would surface if matter pre-fill ever returns all-null).

---

### 2. Wizard solution `package.json` drift + root-barrel import fragility

**Surfaced**: 2026-06-04 while deploying the CreateMatterWizard / CreateProjectWizard fixes — neither solution could be built at master.

**Status**: not started — partial band-aid applied (deep-import switch in two `main.tsx` files)

#### Problem

Vite wizard solutions under `src/solutions/{Name}/` were unbuildable at master because:

1. `main.tsx` in both `CreateMatterWizard` and `CreateProjectWizard` imported `resolveCodePageTheme` and `setupCodePageThemeListener` from the **root barrel** `@spaarke/ui-components` instead of from a deep path. The root barrel re-exports `./services` and `./components`, which transitively reaches:
   - `services/SprkChatBridge.ts` → `components/SprkChat/...` → `useChatFileAttachment.ts` → `import('pdfjs-dist')` (dynamic, but `vite-plugin-singlefile` still crawls it)
   - `services/AppInsightsService.ts` → `@microsoft/applicationinsights-web`
2. Neither `pdfjs-dist` nor `@microsoft/applicationinsights-web` is declared in either solution's `package.json`. The custom `resolveSharedLibDeps` Vite plugin in `vite.config.ts` tries to resolve bare imports from the solution's own `node_modules` — when the dep isn't there, Rollup errors out and the build fails completely.
3. The band-aid I applied today switches those two `main.tsx` files to import from `@spaarke/ui-components/utils/themeStorage`. This unblocks Matter + Project but does not fix the underlying fragility — every other Vite wizard solution that ever touches the root barrel from `main.tsx` will hit this same failure as soon as the shared lib gains a new transitive dep.

This explains why the wizards were running with weeks-old deployed bundles: the build had been silently broken; nobody could redeploy.

#### Recommended approach

Pick one (or both) of:

- **A. Lint rule / pre-commit hook**: forbid root-barrel imports of `@spaarke/ui-components` (and `@spaarke/auth`, `@spaarke/ai-widgets`, `@spaarke/events-components`, `@spaarke/legal-workspace`) inside `src/solutions/*/src/**`. Enforce deep imports only. Cheap, high signal.
- **B. Re-shape the root barrel into a "lite" surface**: have `@spaarke/ui-components` export _only_ context-agnostic primitives at the root (theme utils, formatters, type definitions, the small set of components that every consumer needs). Heavy chunks like `SprkChat`, `AppInsightsService`, etc. become deep-import-only. Reduces the cost of accidental root-barrel imports.

A is the smaller, faster fix. B is the proper architectural correction. Probably both: A immediately, B as a separate pass.

Either way, also: **audit every `src/solutions/*/package.json` against what the shared lib actually requires**, add missing peer deps (`pdfjs-dist`, `@microsoft/applicationinsights-web`, `mammoth`, etc.) where the solution genuinely needs them, and confirm the `Build-AllClientComponents.ps1` canonical build script can build every Vite solution from a clean checkout.

#### Impact / consumers

- Every solution under `src/solutions/` (currently ~16 — see `Build-AllClientComponents.ps1` `$ViteSolutions` list).
- `Build-AllClientComponents.ps1` and `Deploy-WizardCodePages.ps1` — the deploy pipeline assumes all solutions can build; today's session is evidence that assumption is silently false.
- Any CI pipeline that runs the build (`/ci-cd` workflow); confirm whether CI currently catches this and we just missed it locally, or whether CI is also silently broken.

#### Risks / considerations

- Adding deps to wizard `package.json` files inflates bundle size if not tree-shaken. Verify the bundle stays under any size budget after fixing.
- Some "missing deps" may be solution-specific (e.g. `pdfjs-dist` legitimately belongs only where chat attachments are needed). Use option B (lite root barrel) to avoid every solution paying the cost of every transitive dep.
- A lint-rule fix needs to be added to the shared ESLint config or a repo-level rule; either way it shouldn't break CI on the next PR.

#### Scope estimate

- A (lint rule): 1 task, ~half a day.
- Audit each solution package.json + add genuinely-needed deps: 1–2 tasks per solution × ~16 = ~2–3 days.
- B (lite root barrel refactor): 3–5 tasks, ~1 week (touches every consumer of `@spaarke/ui-components`).
- Total if doing both: ~1–2 weeks.

#### Out of scope (already done in the 2026-06-04 small fixes)

- Deep-import band-aid applied to `CreateMatterWizard/src/main.tsx` and `CreateProjectWizard/src/main.tsx` so those two could build + deploy today.
- The 14 other wizard solutions were _not_ rebuilt today; their deployed `dist/index.html` was re-uploaded as-is by `Deploy-WizardCodePages.ps1`. If their source changed recently and the deployed copy is also stale, they need the same band-aid + rebuild.

---

### 1. Lift wizard follow-on actions into shared `CreateRecordWizard`

**Surfaced**: 2026-06-04 during fixes to CreateMatterWizard / CreateProjectWizard work-assignment + N:N association bugs.

**Status**: not started — seed entry only

#### Problem

Each entity-specific wizard (`CreateMatterWizard`, `CreateProjectWizard`, and any future `CreateInvoiceWizard` / `CreateBudgetWizard` / etc.) re-implements ~50 lines of identical "post-create follow-on" plumbing inside its `onFinish` handler. Each implementation:

1. Reads `context.followOn.*` fields shaped for the user's selected follow-on actions (`assign-counsel`, `create-event`, `send-email`).
2. Maps those fields into `ICreateWorkAssignmentFormState` + `IAssignWorkState` and calls `WorkAssignmentService.createWorkAssignment(...)`.
3. Maps the same `context.followOn.*` fields into `IEventFormValues` and calls `EventService.createEvent(...)`.
4. Maps email fields and calls `EntityCreationService.sendEmail(...)`.
5. Accumulates per-action warnings into the result.

Today this code lives at:
- [CreateMatterWizard.tsx#L348-L460](../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/CreateMatterWizard.tsx#L348-L460) — `assign-counsel`, `create-event`, N:N association
- [CreateProjectWizard.tsx#L390-L500](../../src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectWizard.tsx#L390-L500) — `assign-counsel`, `create-event`

The duplication directly caused two production bugs (2026-06-04):
- Both wizards hardcoded the relationship-style name `sprk_workassignment_Regarding{Matter|Project}_sprk_{matter|project}_n1@odata.bind` instead of doing nav-prop discovery → Dataverse rejected the payload with "undeclared property" error. Fixed by routing both wizards through the canonical `WorkAssignmentService` (which already does nav-prop discovery + applies ADR-024 Polymorphic Resolver fields).
- CreateMatterWizard's N:N `@odata.id` used a service-relative URI (`/api/data/v9.0/...`) which Dataverse rejects in `$ref` bodies. Fixed by reading the Dataverse origin from `Xrm.Utility.getGlobalContext().getClientUrl()`.

Both fixes shipped, but the underlying duplication remains — the same bug pattern will recur the next time someone adds a new entity wizard and copy-pastes the `onFinish` block from an existing one.

#### Recommended approach

Lift the follow-on action handling into `CreateRecordWizard` itself. Entity wizards should declare _which_ follow-on actions they support and provide the parent record context once their primary record is created; `CreateRecordWizard` should orchestrate the rest.

Sketch of the contract change:

```typescript
// CreateRecordWizard config — new fields
interface ICreateRecordWizardConfig {
  // ... existing fields ...

  /**
   * Which follow-on actions are available in this wizard.
   * Drives NextStepsSelectionStep + the dynamic step injection.
   */
  followOnActions?: Array<'assign-work' | 'create-event' | 'send-email'>;

  /**
   * Called by CreateRecordWizard after the user clicks Finish but before any
   * follow-on actions run. Implementations create their primary record
   * (sprk_matter, sprk_project, etc.) and return the IDs CreateRecordWizard
   * needs to wire up the follow-on actions.
   */
  createPrimaryRecord: (context: IFinishContext) => Promise<{
    parentEntityLogicalName: string;  // e.g. 'sprk_matter'
    parentRecordId: string;
    parentRecordName: string;
    parentEntitySet: string;          // e.g. 'sprk_matters'
    warnings?: string[];
  }>;

  /**
   * Optional N:N association after primary record exists (replaces the
   * inline associateToRecord helper currently duplicated in each wizard).
   */
  applyAssociation?: (parentId: string, association: AssociationResult) => Promise<{ success: boolean; warning?: string }>;

  /**
   * Builds the success screen IWizardSuccessConfig. Replaces the inline
   * success-screen builder, which is largely identical across wizards.
   */
  buildSuccessScreen: (parentId: string, parentName: string, warnings: string[]) => IWizardSuccessConfig;
}
```

`CreateRecordWizard.onFinish` would then:
1. Call `config.createPrimaryRecord(context)` → get parent ID/name/etc.
2. If `assign-work` selected and `context.followOn.assignWorkName` is set: build `ICreateWorkAssignmentFormState` (with `recordType` derived from `parentEntityLogicalName`) + `IAssignWorkState` from `context.followOn.*` and call `WorkAssignmentService`.
3. If `create-event` selected: same pattern with `EventService`.
4. If `send-email` selected: same pattern with `EntityCreationService.sendEmail`.
5. If `context.association?.recordId`: call `config.applyAssociation` (or a centralized default).
6. Aggregate warnings and call `config.buildSuccessScreen`.

#### Impact / consumers

- [`src/solutions/CreateMatterWizard/`](../../src/solutions/CreateMatterWizard/) — code page consumer
- [`src/solutions/CreateProjectWizard/`](../../src/solutions/CreateProjectWizard/) — code page consumer (verify path)
- LegacyWorkspace standalone wizard copies in `src/solutions/LegalWorkspace/src/components/CreateMatter/` and `CreateProject/` — may already be deprecated; verify before touching
- The CreateRecordWizard contract change is **not** consumed by `CreateWorkAssignmentWizard` (which has its own different shell) or `CreateEventWizard`. So 2 active consumers + 0–2 legacy.

#### Risks / considerations

- The `IFinishContext` → `onFinish` contract is the seam between `CreateRecordWizard` and every entity wizard. Breaking it is a one-time migration cost across all consumers; non-trivial but bounded.
- ADR-024 (Polymorphic Resolver) mandates the resolver-field population pattern. `WorkAssignmentService` already implements it; centralizing follow-on handling makes it harder for future wizards to accidentally bypass ADR-024 (a feature, not a bug).
- `recordType` in `ICreateWorkAssignmentFormState` currently only supports `'matter' | 'project' | 'invoice' | 'event' | ''`. Adding a new entity wizard (e.g. Budget) requires extending the type — but the central registration in `CreateRecordWizard` would make the gap visible at compile time across all consumers.
- Need to decide where `getDataverseClientUrl()` lives. Currently inlined in `CreateMatterWizard.tsx`; should move into a small shared util (e.g. `src/client/shared/Spaarke.UI.Components/src/utils/dataverseUrl.ts`) since both N:N associate calls and any future absolute-URI needs will want it.

#### Scope estimate

Probably 5–8 tasks: contract change in `CreateRecordWizard`, central follow-on handler implementation, shared `dataverseUrl` util, migration of `CreateMatterWizard` + `CreateProjectWizard` to the new contract, retirement of duplicated code in legacy LegalWorkspace copies (or confirmation they're already dead code), update consumer code-page solutions if any imports change, test coverage for the central handler. Probably 1–2 weeks.

#### Out of scope (already done in the 2026-06-04 small fixes)

- Both wizards already route through `WorkAssignmentService` — the bug-fix path is closed.
- N:N absolute-URI fix is in place in `CreateMatterWizard`; `CreateProjectWizard` already used `window.location.origin`.
- This referral is purely about removing duplication and tightening the contract; no user-visible behavior change expected.

---

## Promoted

_(Empty — entries that became real projects get moved here with a link.)_

---

## Template for new entries

```markdown
### N. Short title

**Surfaced**: YYYY-MM-DD during {what work surfaced it}

**Status**: not started

#### Problem
What's wrong, what's the evidence, what's the cost of leaving it.

#### Recommended approach
What the fix should look like at the design level.

#### Impact / consumers
Which files / packages / projects are affected.

#### Risks / considerations
Contracts that change, ADRs that constrain, migration costs.

#### Scope estimate
Rough task count + duration.

#### Out of scope
What's already fixed, what we explicitly aren't doing.
```
