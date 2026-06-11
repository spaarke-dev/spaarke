# Task 023 — Playbook Deploy Handoff

> **Environment**: `https://spaarkedev1.crm.dynamics.com` (Spaarke Dev)
> **Date**: 2026-06-11
> **Rigor level**: STANDARD (per POML)
> **Status**: ✅ Deployed

---

## Outcome summary

The `matter-health-single` playbook (Insights Engine Widgets r1, Wave 1B) is deployed to Spaarke Dev:

| Artifact | Value |
|---|---|
| `sprk_analysisplaybook` row Guid | **`a0d49d0d-4a65-f111-ab0c-70a8a590c51c`** |
| `sprk_name` | `matter-health-single` (BARE — NO `@v` suffix per Q-U1) |
| `sprk_issystemplaybook` | `true` |
| `sprk_playbooktype` | `0` (AiAnalysis) |
| Nodes deployed | 9 (all `sprk_isactive = true`) |
| Dependencies wired | 8 (queryMatterContext → 2 downstream; converging at checkSufficiency; branching to synthesize / declineInsufficient; persistEnvelope tail after ReturnInsightArtifactNode) |
| Canvas layout | saved (9 nodes, 9 edges) |

---

## Reused r2 INS-* action codes — resolution decision (option b)

The playbook references 6 r2 Wave B action rows: `INS-FACT`, `INS-IDXR`, `INS-EVID`, `INS-GRND`, `INS-RART`, `INS-DECL`.

**Discovery via Web API query** (2026-06-11): in `spaarkedev1`, the r2 rows exist ONLY with `@v1` suffix as their `sprk_actioncode`. There are no bare-code variants:

| r2 row Guid | sprk_actioncode (in Dataverse) |
|---|---|
| `5137365a-825e-f111-a825-6045bdebafa9` | `INS-FACT@v1` (Insights — Live Fact Resolver) |
| `23939266-825e-f111-a825-6045bdebafa9` | `INS-IDXR@v1` (Insights — Index Retrieve) |
| `6139aa6c-825e-f111-a825-6045bdebafa9` | `INS-EVID@v1` (Insights — Evidence Sufficiency) |
| `32eafa72-825e-f111-a825-6045bdebafa9` | `INS-GRND@v1` (Insights — Grounding Verify) |
| `96d52e7f-825e-f111-a825-6045bdebafa9` | `INS-RART@v1` (Insights — Return Insight Artifact) |
| `d1121079-825e-f111-a825-6045bdebafa9` | `INS-DECL@v1` (Insights — Decline to Find) |

**Decision: option (b) — re-key the playbook JSON to reference the `@v1` form.**

Per Q-U1: "the ban applies only to NEW identifiers authored by r1 — INS-FACT@v1 is r2's existing row and may be referenced as-is". This is the cleaner path because:

1. It avoids creating 6 redundant alias rows in `sprk_analysisaction` purely to support a single playbook's naming convention.
2. The `Deploy-Playbook.ps1` script does exact-match lookup (line 396–404), so re-keying is the safest way to wire the existing rows.
3. The Q-U1 ban (bare-code form) is preserved for NEW identifiers authored by r1: `matter-health-synthesis` (Task 020), `INS-FETCH-KPI` and `INS-UPDR` (this task 023).
4. The playbook's `sprk_name = "matter-health-single"` remains BARE in Dataverse — that is the artifact Q-U1 most cares about (verified post-deploy; `@v` count on `sprk_name` = 0).

**Edits to `matter-health-single.playbook.json`** (6 nodes' `actionCode` field; `$comment-actionCode` updated to document the deploy-time decision):

- queryMatterContext: `INS-FACT` → `INS-FACT@v1`
- retrieveObservations: `INS-IDXR` → `INS-IDXR@v1`
- checkSufficiency: `INS-EVID` → `INS-EVID@v1`
- groundCitations: `INS-GRND` → `INS-GRND@v1`
- ReturnInsightArtifactNode: `INS-RART` → `INS-RART@v1`
- declineInsufficient: `INS-DECL` → `INS-DECL@v1`

The `matter-health-synthesis`, `INS-FETCH-KPI`, and `INS-UPDR` action codes remain bare (no `@v` suffix) — they are r1-authored new identifiers and stay in compliance with Q-U1.

---

## New action codes seeded (r1 net-new — 2 rows)

| sprk_actioncode | sprk_analysisactionid | sprk_executoractiontype | Purpose |
|---|---|---|---|
| `INS-FETCH-KPI` | `8ed3e378-4965-f111-ab0c-70a8a590c51c` | `51` (QueryDataverse) | Fetch `sprk_kpiassessment` rows for a Matter (queryKpiAssessments node) |
| `INS-UPDR` | `62a1687d-4965-f111-ab0c-7ced8ddc4a05` | `22` (UpdateRecord) | Persist FR-14 envelope to `sprk_matter.sprk_performancesummary` (persistEnvelope node) |

Both rows have `sprk_name` set with proper em-dash (U+2014), bare action codes (Q-U1 compliant), and `sprk_executoractiontype` set explicitly (deviates from r2 pattern which left this NULL on the INS-* rows; rationale: r1 sets it so future Designer/UI tooling can show the executor type without traversing the FK).

### Action-type FK rows also seeded (sprk_analysisactiontype)

The r2 schema requires (or strongly conventions) an `sprk_ActionTypeId` lookup to a row in `sprk_analysisactiontype`. r2 had types 70/80/90/100/110/120 seeded but NOT 22 or 51. I seeded:

| sprk_name | sprk_executoractiontype | sprk_analysisactiontypeid |
|---|---|---|
| `22 - Update Record` | `22` | `ded404f1-4865-f111-ab0c-70a8a590c51c` |
| `51 - Query Dataverse` | `51` | `f9dd8bf5-4865-f111-ab0c-7ced8ddc4a05` |

These are referenced via `sprk_ActionTypeId@odata.bind` from the two new action rows above. (Web API gotcha: the navigation property name is Pascal-case `sprk_ActionTypeId`, not snake-case `sprk_actiontypeid` — the latter is the lookup attribute logical name; the binding endpoint requires the nav name. Verified via `EntityDefinitions(LogicalName='sprk_analysisaction')/ManyToOneRelationships`.)

---

## BFF config map update

Added the playbook Guid mapping to `src/server/api/Sprk.Bff.Api/appsettings.template.json`:

```json
"Insights": {
  "Playbooks": {
    "Map": {
      "matter-health-single": "a0d49d0d-4a65-f111-ab0c-70a8a590c51c"
    }
  }
}
```

This is the **dev-environment seed** (the template file is the dev source after token replacement on deploy; the bare `matter-health-single` key is preserved for local-dev file-source binding). For Azure App Service Application Settings, the corresponding key (per `InsightsPlaybookNameMapOptions.cs` XML doc) MUST use snake_case_only form: `Insights:Playbooks:Map:matter_health_single` (no `-`, no `@`) due to Linux POSIX env-var rules. This handoff documents that constraint; if a follow-up infra task rolls the Guid into App Service Settings (e.g., via Bicep parameter or Key Vault reference), use the snake_case key + the Guid above.

No BFF restart was triggered as part of this task because `IOptionsMonitor<InsightsPlaybookNameMapOptions>` (used by `InsightsOrchestrator`) is reactive to config file changes (or App Service settings reload). When this branch is merged + deployed to dev BFF, the map will be picked up on first request after restart.

---

## Acceptance criteria verification

| Criterion | Status | Evidence |
|---|---|---|
| `sprk_playbook` row queryable by name 'matter-health-single' | ✅ | Web API GET `sprk_analysisplaybooks?$filter=sprk_name eq 'matter-health-single'` returned 1 row, Guid `a0d49d0d-4a65-f111-ab0c-70a8a590c51c` |
| BFF config map contains the playbook Guid under key matter-health-single | ✅ | `appsettings.template.json` line 318 |
| No `@v` suffix in sprk_playbook.sprk_name | ✅ | Verified value is exactly `matter-health-single` (0 occurrences of `@v` substring) |
| Lint check (Deploy-Playbook.ps1): all 9 nodes have actionCode wiring | ✅ | "Lint    : ? all 9 nodes have actionCode wiring" in deploy output |
| All referenced actionCodes resolve at pre-flight | ✅ | All 9 codes resolved (see deploy log step 3) |
| 9 sprk_playbooknodes rows created with `sprk_isactive = true` and execution order 1-9 | ✅ | Web API GET confirmed 9 rows, all active, ordered 1-9 |
| All 9 nodes have `sprk_actionid` FK set | ✅ | `$filter=_sprk_actionid_value ne null` returned `@odata.count: 9` |
| Canvas layout saved | ✅ | "Canvas layout saved" in deploy output (9 nodes, 9 edges) |

---

## Files modified (this task)

| Path | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` | Re-keyed 6 r2 actionCodes from bare → `@v1` form (option b) + updated `$comment-actionCode` strings with the deploy-time decision documentation |
| `src/server/api/Sprk.Bff.Api/appsettings.template.json` | Added `Insights:Playbooks:Map.matter-health-single` entry (dev Guid `a0d49d0d-4a65-f111-ab0c-70a8a590c51c`) |
| `projects/ai-spaarke-insights-engine-widgets-r1/notes/handoffs/playbook-deploy.md` | This handoff (new) |

## Dataverse changes (Spaarke Dev)

| Entity set | Row type | Count | Identifier(s) |
|---|---|---|---|
| `sprk_analysisactiontype` | New | 2 | `ded404f1-4865-f111-ab0c-70a8a590c51c` (22), `f9dd8bf5-4865-f111-ab0c-7ced8ddc4a05` (51) |
| `sprk_analysisaction` | New | 2 | `INS-FETCH-KPI` = `8ed3e378-4965-f111-ab0c-70a8a590c51c`, `INS-UPDR` = `62a1687d-4965-f111-ab0c-7ced8ddc4a05` |
| `sprk_analysisplaybook` | New | 1 | `matter-health-single` = `a0d49d0d-4a65-f111-ab0c-70a8a590c51c` |
| `sprk_playbooknode` | New | 9 | (queryMatterContext, queryKpiAssessments, retrieveObservations, checkSufficiency, synthesize, groundCitations, ReturnInsightArtifactNode, declineInsufficient, persistEnvelope) |

---

## Open follow-up considerations (NOT blocking task 023)

1. **App Service Application Settings seed (if needed for staging/prod promotion)**: per `InsightsPlaybookNameMapOptions.cs` XML doc, when this map is configured via Azure App Service Application Settings, the key MUST use snake_case_only form. A future infra task (Bicep parameter) will set `Insights:Playbooks:Map:matter_health_single = <env-specific-Guid>` per environment. Spaarke Dev currently uses the file-source binding (hyphens accepted).
2. **r2 INS-* row migration consideration**: r1 chose option (b) to avoid creating 6 alias rows for the matter-health-single playbook. If r2 patterns proliferate further (more r1+ playbooks reusing the same nodes), consider whether r2's `@v1` rows should be renamed (or aliased) to bare form during a future schema cleanup. Not in scope for r1.
3. **`sprk_executoractiontype` divergence**: r2 INS-* rows have `sprk_executoractiontype = null`; r1 set this on the 2 new rows to `22` / `51` explicitly. This may produce inconsistent UI behavior in the Designer (which reads either the FK or the scalar). Document in a future r1 retro if it surfaces.
