> ## ⚠️ STATUS: SUPERSEDED (2026-09-03) — do not act on the fix below
> Live verification against `spaarkedev1` found the AI routing catalog **is now seeded, enabled, and wired**
> (all 3 `sprk_analysisaction` + 3 `sprk_playbookconsumer` rows present, `enabled=true`, valid action lookups),
> and **triage works** on real captures (priority + rich summary + reviewoutcome + riconfidence populate). The
> "seed the catalog" fix below is a **no-op** — it was already applied after this doc was written. The *real*
> residual was **triage CATEGORY resolution** (100% miss), root-caused + fixed 2026-09-03 (a `y→ies` entity-set
> pluralization bug in `LookupChoicesResolver` blanked the `$choices` taxonomy lookup). See
> **`email-matching-and-triage-go-forward-plan.md` → "P1 FINDING"**. This doc is retained for its accurate
> flow description (routing table → ActionResolver → NFR-04 swallow), not its remedy.

# DEFECT — Triage/Job-B/Job-C AI fields blank on real captures — ROOT CAUSE FOUND 2026-08-13

> UAT Fix #4. The handoff hypothesis (**"`ICommunicationTriageAi` is Null in dev — DI feature-gate OFF"**) is **WRONG**. Corrected root cause below. Severity: Medium (triage/propose/create-task never populate on real captures; only the 14 seeded rows have triage because the seed script wrote those fields directly).

## Symptom (verified live)
Every REAL email capture (10 recent inspected) has `sprk_associationprovenance` populated **with a valid `ai-classify:category=` signal** (rung-5 classification works) and a set `sprk_associationstatus` (association engine works) — **yet every triage field is blank**: `sprk_triagepriority`, `sprk_triagecategory`, `sprk_reviewoutcome`, `sprk_riconfidence`, `sprk_triagesummary` all null.

## Why the DI-gate hypothesis is wrong
- Dev app settings: `Analysis__Enabled = true` **and** `DocumentIntelligence__Enabled = true` → the compound AI gate is **ON** → the real `CommunicationTriageAi` **is** registered (not `NullCommunicationTriageAi`). (`AnalysisServicesModule.cs:154` gate; real impl at `:1299` in `AddPublicContractsFacade`.)
- Rung-5 classification (`CommunicationClassificationAi`) works because it calls Azure OpenAI **directly** (`_openAi.GetStructuredCompletionAsync`, `CommunicationClassificationAi.cs:144`) — **no routing table**. That's why the ai-classify signal is present while triage is not.

## Actual root cause — the AI-catalog rows were never seeded to spaarkedev1
`CommunicationTriageAi.TriageAsync` (and the Job-B `CommunicationProposeAi` / Job-C `CommunicationCreateTaskAi` siblings) resolve their Action via the **routing table** — `ActionResolver.ResolveAsync(consumerType)` → `sprk_playbookconsumer` row (`sprk_consumertype`, `sprk_enabled=true`) → `sprk_action` lookup → `sprk_analysisaction` row. **None of these rows exist in dev:**

| consumerType | actionCode | `sprk_playbookconsumer` in dev? | `sprk_analysisaction` in dev? |
|---|---|---|---|
| `email-triage` | `triage-email` | ❌ absent | ❌ absent |
| `email-propose` | `propose-field-updates` | ❌ absent | ❌ absent |
| `email-create-task` | `create-task-from-email` | ❌ absent | ❌ absent |

(Verified: `SELECT ... FROM sprk_playbookconsumer WHERE sprk_consumertype IN (...)` → `[]`; same for `sprk_analysisaction WHERE sprk_actioncode IN (...)` → `[]`. The compose/create-matter/nda rows DO exist — only the email-intelligence catalog is missing.) `ResolveAsync("email-triage")` throws `InvalidOperationException("... has no Action routed")` → caught in `CommunicationTriageAi.cs:82` → returns null → triage never persists (NFR-04 best-effort swallow). R2 deployed BFF **code** this session but never seeded the R1-authored AI catalog data.

## The fix (data-only, dev-only, reversible)
Seed **3 `sprk_analysisaction` rows + 3 `sprk_playbookconsumer` routing rows** into spaarkedev1.

**Action rows** — required columns (per `AnalysisActionService.cs` `$select`): `sprk_actioncode`, `sprk_name`, `sprk_description`, `sprk_systemprompt`, `sprk_temperature`, `sprk_modeltier`, `sprk_allowsknowledge`, **`sprk_outputschemajson`**. Source = `infra/dataverse/actions/{triage-email,propose-field-updates,create-task-from-email}.action.json`.
- `sprk_systemprompt` = the **classic-JPS root** of the .action.json (whole doc minus `$comment*` keys + the deploy-row scalars actionCode/name/description/actionType/modelTier/temperature). `PromptSchemaRenderer.IsJpsFormat()` detects leading `{`+`$schema` and renders it (resolving `$choices` via `LookupChoicesResolver` at render time — no second LLM classify pass).
- **`sprk_outputschemajson` is MANDATORY** even for JPS actions — `ActionRunner.RunAsync:122-127` throws if empty ("linear consumers require a constrained-decoding schema"); it's the constrained-decoding schema (`:154`). The JPS `output` section renders only as *prompt text*. There is **no** `outputschemas/{triage-email,...}.schema.json` mirror → derive draft-07 from each action's `output.fields[]`: `$choices` fields (category/priority/reviewOutcome) become free `{"type":"string","maxLength":N}` (dynamic option sets/taxonomy — NOT a static enum, per FR-16), arrays/strings mirror the field caps, object-level `required` lists all fields, `additionalProperties:false`.
- `sprk_temperature` = 0.2 (triage), `sprk_modeltier` = 100000000 (Fast) per the action JSONs. `sprk_allowsknowledge` = false (no scopes section authored).

**Routing rows** — run `scripts/dataverse/Seed-PlaybookConsumers.ps1` (default Seed mode); the mirror `infra/dataverse/sprk_playbookconsumer-rows.json` already carries the 3 email rows (lines 454-513), and the seed resolves `actionCode → sprk_analysisaction` per-env (so seed the Actions FIRST).

## Verify
Re-send a Mode-C email (`POST /api/communications/send`) → confirm the new capture's triage fields populate (category mapped to a `sprk_triagecategory` taxonomy row, priority set, 2-line summary, riconfidence). If garbage → delete the 3 action rows + 3 routing rows and re-derive.

## Downstream unblocked
Triage columns fill the reconciliation grid (Fix #4 proper). Job-B proposals + Job-C create-task feed the reconciliation Fields/Tasks tabs (relates to Fix #2/#5 UX).
