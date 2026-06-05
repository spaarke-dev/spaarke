# Task 017 — BFF Deploy DEFERRED

> **Status**: ⏸ Deferred (2026-06-01)
> **Reason**: Parallel-project conflict; not blocking Phase C/D/E
> **Resume condition**: insights-engine-r2 merged to master + our branch rebased

---

## What was found

Pre-deploy check (per user direction "ensure no conflict with insights-engine-r2 work before deploying") revealed:

**`spaarke-wt-ai-spaarke-insights-engine-r2` worktree state**:
- Branch `work/ai-spaarke-insights-engine-r2` at commit `1587b8f1`
- **11 commits ahead of origin/master**
- Touches `Sprk.Bff.Api` files we don't touch:
  - `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json`
- Has uncommitted `deploy/api-publish/Sprk.Bff.Api.dll` + `.pdb` + `.deps.json` artifacts staged — suggests they have a built deployment bundle ready or just deployed

**File-level overlap with our branch**: zero. Their changes are in `Services/Ai/`; ours are in `Services/Dataverse/` and `Api/Dataverse/`.

**Deployment overlap risk**: HIGH. DEV runs one BFF API instance. Last-deployed-branch wins. If we deploy our branch:
- DEV loses insights-engine-r2's `AnalysisActionService.cs` + `predict-matter-cost.playbook.json` changes (they're not on master, not on our branch — exist ONLY on their branch)
- Their active testing of `predict-matter-cost` dispatch breaks without warning

User direction (verbatim): "WE DO NOT WANT TO OVERWRITE THEIR WORK"

---

## Why deferral is acceptable

Phase C/D/E Custom Pages consume the DataGrid framework via:
```typescript
<DataGrid configId={...} />
// (defaults to: dataverseClient = new XrmDataverseClient())
```

`XrmDataverseClient` calls `Xrm.WebApi` directly (MDA platform). It does NOT call the BFF passthrough endpoints. Therefore:
- Task 023 (drill-through Custom Pages): runs fine without BFF deploy
- Task 024 (VisualHost chart-defs): no BFF dependency
- Task 026 (Phase C UAT): runs fine — Custom Pages render via XrmDataverseClient
- Task 031 (EventsPage rewrite): same — MDA Custom Page, no BFF
- Task 033 (SpaarkeAi Calendar widget migrate): **possible exception** — if the widget runs in a non-MDA workspace context, it may need BFF. Re-evaluate at task 033 dispatch.
- Task 041 (SemanticSearch migration): existing SS already runs; migration uses XrmDataverseClient by default.

**The BFF endpoints exist as source code** (committed in commit `2e3e32b1`, integration tests pass). They are deployable on demand. They are not consumed by any R1 host that lacks an Xrm context.

---

## Resume conditions (when 017 unblocks)

1. **insights-engine-r2 merges its work to master** (their PR lands)
2. **Our branch rebases on the updated master** — picks up their `AnalysisActionService.cs` changes
3. **Full build + integration tests pass** on the rebased branch
4. **No other parallel BFF project is mid-deploy** (re-run the pre-deploy worktree check)
5. **Then**: dispatch `/bff-deploy` skill from this project

---

## What to do at task 033 (SpaarkeAi Calendar widget)

The widget host is SpaarkeAi workspace. If that workspace runs OUTSIDE MDA (which it does — workspace widgets are separate React mounts), it lacks `Xrm` and CANNOT use `XrmDataverseClient`. It would need `BffDataverseClient`.

**Decision point at task 033 dispatch**:
- Confirm SpaarkeAi workspace consumption pattern (MDA or non-MDA?)
- If non-MDA: BFF deploy becomes prerequisite for task 033 UAT
- At that point: re-run the worktree conflict check; if clean, deploy then

This re-evaluates at task 033 — not earlier.

---

## Recorded by

Main session, 2026-06-01, after task 016 (integration tests) completed with 25/25 PASS and main-session fix to `SavedQueryEndpoints.cs` rate-limit policy bug (commit `82f114af`).
