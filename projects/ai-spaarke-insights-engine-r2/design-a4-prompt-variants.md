# Wave A4 — Prompt-variant + versioning + per-tenant-override design

> **Status**: Decided (Wave A4 — task 013)
> **Authored**: 2026-06-02
> **Author**: Wave A4 task
> **Audience**: Wave C2 (prompt-content migration), Wave D2 (per-practice-area Layer 1 prompts), Wave D3 (per-(area, doc-type) Layer 2 schemas), Wave D4 (universal-ingest routing)
> **Reference**: spec.md PR-1, design.md D-P15-05, Q-A4-1
> **Reference row inspected**: `sprk_analysisaction` "Classify Document" (`sprk_actioncode = ACT-021`, id `ba356968-ebe9-f011-8406-7ced8d1dc988`) in Spaarke Dev — full `sprk_systemprompt` JSON content read 2026-06-02
> **Reference rows in scope**: 8 Insights `sprk_analysisaction` rows created Wave B2 (`INS-OBS`, `INS-FACT`, `INS-IDXR`, `INS-EVID`, `INS-GRND`, `INS-DECL`, `INS-RART`, `INS-AGNT`)

---

## 0. Scope + binding constraints

This design covers three decisions Wave A4 must lock so Waves C2 + D2 + D3 + D4 can author + migrate content without ambiguity:

1. **Variant pattern** — how a single logical prompt (e.g., "Layer 1 Classify") gets specialised per practice area
2. **Versioning model** — how prompt evolution + rollback work
3. **Per-tenant override** — how a single tenant can override a global prompt without forking the catalog

**Binding constraints (load-bearing)**:

- **PR-1 invariant**: **NO new `sprk_prompt` entity**. All prompt content lives in `sprk_analysisaction.sprk_systemprompt` (JPS-formatted JSON) or — for playbook-specific inline templates owned by exactly one playbook — `sprk_playbook.sprk_configjson`. The 8 Insights rows already exist (Wave B2). This design must work **on the existing `sprk_analysisaction` schema** without adding columns. Any future column add is out of scope for Phase 1.5.
- **JPS shape locked**: per the inspected "Classify Document" row, the JPS document has fields `$schema`, `$version`, `instruction { role, task, constraints, context }`, `input { document, parameters }`, `output { fields, structuredOutput }`, `scopes { $skills }`, `examples`, `metadata { author, authorLevel, createdAt, description, tags }`. New shape additions in this design (e.g., `metadata.practiceArea`, `metadata.tenantId`) extend `metadata` only.
- **Practice areas sourced from `sprk_practicearea_ref`** (PA-1): code values like `CTRNS`, `IPPAT`, `BNKF` are stable refs in Dataverse; never hardcoded.
- **JPS skills/action authoring goes through `/jps-action-create` + `/jps-playbook-design`** (per D-01 path-b decision). This design must produce a shape those skills can author.
- **No SME tooling for variant authoring is in scope for Phase 1.5** (SMEs edit prompt content via the Dataverse action row UI per FR-06; tooling lives in Phase 2+ if needed).

**Out of scope**:
- A general "prompt catalog" UI
- Multi-region / multi-language variants (use the same mechanism if needed later)
- Versioning beyond append-only row history (no merge / rebase semantics)
- Tenant-specific schema fork (override is content-level, not schema-level)

---

## 1. Decision summary

| Decision | Chosen option | One-sentence rationale |
|---|---|---|
| **1. Variant pattern** | **Hybrid: parametric injection by default; promote to variant action rows when content (not just data) diverges** | Single-row parametric works for ≥80% of cases (variation IS data: category list, doctype list, area context); variant rows exist as a documented escape hatch when a practice area needs structurally different instruction/constraints (rare) |
| **2. Versioning model** | **New row per version (immutable rows) with `actionCode` suffixed `@v1`, `@v2`, …; playbook nodes pin to a specific version** | Matches existing "Classify Document" immutable pattern; rollback = playbook rebind, A/B test = two playbooks pinned to different versions; no schema change required |
| **3. Per-tenant override** | **Tenant-scoped variant rows with fallthrough resolution at invocation: tenant-specific (`<actionCode>@v<n>.<tenantId>`) → global (`<actionCode>@v<n>`)** | Same primitive as variant rows + versioning (no new mechanism); resolver is one extra Dataverse query (acceptable for Phase 1.5 scale); tenant override never silently drifts because it's a discrete row with audit trail |

These three choices compose: every Phase 1.5 Insights prompt is identified by the tuple `(actionType, [practiceArea], versionNumber, [tenantId])` resolved at invocation to exactly one `sprk_analysisaction` row.

The remainder of this document walks each decision with options + reasoning, gives a CTRNS Layer 1 worked example end-to-end, and covers rollback + A/B test stories.

---

## 2. Decision 1 — Variant pattern (per-practice-area variation)

### 2.1 Options considered

#### Option A — Parametric injection (single action row per logical prompt)

A single `sprk_analysisaction` row per logical analysis step (e.g., `INS-L1-CLASSIFY`). Per-practice-area variation flows through JPS `parameters` populated at invocation:

```json
{
  "input": {
    "parameters": {
      "categories":            { "placeholder": "{{categories}}" },
      "practiceAreaContext":   { "placeholder": "{{practiceAreaContext}}" },
      "practiceAreaCode":      { "placeholder": "{{practiceAreaCode}}" }
    }
  }
}
```

Universal-ingest playbook reads `sprk_matter.sprk_practicearea` → looks up the practice area's category catalog + context blurb → binds parameters → invokes the single row.

**Pros**:
- Minimal row count: 1 row for Layer 1 Classify regardless of practice-area count
- Single source of truth for the instruction (role, constraints, output format)
- Practice-area additions = pure data change in `sprk_practicearea_ref` (or a new ref table); no new prompt row, no Wave A4-style design re-litigation
- Easy A/B test scope (the prompt itself doesn't change per area, so A/B testing the prompt is per-version not per-area)

**Cons**:
- If two practice areas need structurally different instruction wording (e.g., CTRNS wants "Identify if this is a closing-statement"; IPPAT wants "Identify if this is a patent application AND identify the application phase"), parametric injection can't express it cleanly without overloading the prompt with conditionals
- SMEs editing one area's prompt content can accidentally affect another area (couples areas to a shared row)
- JPS `parameters` placeholders limited to types the placeholder substitution engine supports (string, array, boolean today — see "Classify Document" example)

#### Option B — Variant action rows per practice area (suffixed action codes)

One `sprk_analysisaction` row per (logical step × practice area). Action codes: `INS-L1-CLASS.CTRNS`, `INS-L1-CLASS.IPPAT`, `INS-L1-CLASS.BNKF`. Playbook nodes resolve the action code at invocation by appending the matter's practice-area code.

**Pros**:
- Total freedom per area: each row can have structurally different `instruction.role`, `instruction.task`, `instruction.constraints[]`, output fields
- SME-friendly: editing the CTRNS prompt has zero effect on IPPAT
- Clear audit trail per area (per-row change history in Dataverse)
- Maps to "Classify Document" precedent (each variant is a discrete row)

**Cons**:
- Row count grows N × P (N logical steps × P practice areas). At Wave D2 N=3 areas × ~5 logical steps = 15 rows; at full Spaarke rollout (~20 areas × ~10 steps = 200 rows). Manageable but not free.
- Shared improvements (e.g., a confidence-score tightening that applies to every area's Layer 1) require N edits, not 1
- Risk of drift between areas (the shared improvements problem inverted: areas drift apart over time)
- Resolution complexity: playbook needs to know the area-code → action-code mapping rule

#### Option C — Single row + variant blocks in JPS

Single row with a JPS extension: `instruction.practiceAreaVariants: { "CTRNS": {...}, "IPPAT": {...} }`. Engine selects the block at invocation.

**Pros**:
- One row, full freedom per area

**Cons**:
- **Requires `PlaybookExecutionEngine` patches** to understand `practiceAreaVariants` — schema fork from the existing JPS shape
- Breaks `/jps-action-create` skill (skill authors the standard JPS shape; would need to learn the variant-block extension)
- Single-row size grows linearly with area count (JSON column ≤ 1MB Dataverse limit; not a practical limit but readability suffers)
- No per-area audit trail (all area edits commit to the same row)

### 2.2 Decision: Hybrid (Option A by default; Option B as documented escape hatch)

**Default**: parametric injection (Option A). Single row per logical step. Per-area variation = data (category catalog + context blurb + area code).

**Escape hatch**: if a practice area needs structurally different *instruction* (not just data), promote it to a variant action row (Option B) with action code `<base>.<areaCode>`. Document the promotion in the row's `metadata.derivedFrom` field.

**Why hybrid (rejecting pure Option B)**:

1. Wave D2 ships 3 practice areas. The empirically observed need is data variation (different category lists, different "what counts as outcome-bearing" semantics expressed as data), NOT instruction variation. Defaulting to one row per step lets Wave D2 ship faster.
2. Phase 1.5 acceptance bar requires SMEs to iterate prompts (FR-06). If 80% of edits hit one row, SME friction is lower than if 80% of edits require knowing which of N variant rows to edit.
3. When the escape hatch *is* needed (the rare case), Option B's row-per-area shape is already proven by the "Classify Document" precedent — no engine work required to promote.

**Why hybrid (rejecting pure Option A)**:

1. Forcing every variation through parametric injection risks the prompt becoming a giant conditional template ("if practice area is X then …") that becomes unreadable and untestable. The escape hatch keeps option A clean.
2. Promotion is one-way and reversible (delete the variant row, redirect the playbook back to the parametric base row). No engine logic involved.

**Why Option C rejected**: requires engine patches; couples this design to engine-internal shape; can't be authored by `/jps-action-create` without skill changes. Phase 1.5 already needs `PlaybookExecutionEngine` patches for the universal-ingest refactor (Wave A5); piling on another engine change for variant blocks adds risk for no clear win over Option A + B hybrid.

### 2.3 Resolution rule (binding for Waves C/D)

At playbook-node invocation:

```
target_action_code = base_action_code  // e.g., "INS-L1-CLASS"

IF a row exists with sprk_actioncode = "<base_action_code>.<practiceAreaCode>"
   AND statecode = Active:
   target_action_code = "<base_action_code>.<practiceAreaCode>"

ELSE:
   // Use parametric base row; bind categories + context from sprk_practicearea_ref
   target_action_code = base_action_code
   parameters.categories          = lookup(practiceAreaCode).categories
   parameters.practiceAreaContext = lookup(practiceAreaCode).contextBlurb
   parameters.practiceAreaCode    = practiceAreaCode
```

This rule is documented in the universal-ingest playbook JSON (Wave C1) and enforced by `Deploy-Playbook.ps1` lint (Wave B3 already in place).

---

## 3. Decision 2 — Versioning model

### 3.1 Options considered

#### Option A — New row per version (immutable rows)

Action codes carry a version suffix: `INS-L1-CLASS@v1`, `INS-L1-CLASS@v2`. Each row is immutable once a playbook references it. New version = new row. Playbook nodes pin to a specific version.

**Pros**:
- **Matches the inspected "Classify Document" pattern** (ACT-021 is a frozen row; its `metadata.author = "migration"` and `metadata.createdAt` are immutable; future improvements would land as ACT-021@v2 or similar)
- Rollback = trivial playbook rebind (point the node back to `@v1`)
- A/B test = two playbook copies pinned to `@v1` vs `@v2`
- Full audit trail (each row's CreatedOn / ModifiedBy is the canonical history)
- Zero coupling between version content and version metadata (the version IS the row)
- No schema change required

**Cons**:
- Row count grows with version churn. Realistic Wave D2 estimate: 5 logical steps × 3 areas × 2-3 versions over Phase 1.5 = 30-45 rows. Manageable.
- "Where do I find the latest?" requires either a convention (highest `@vN` is latest) or a "current" pointer row. Phase 1.5 picks convention.
- SME editing requires clone-then-edit workflow (vs. in-place edit). Friction trade-off accepted because immutability is the whole point.

#### Option B — Version field on row (in-place edit + version counter)

Single row per logical action code. JPS `$version` integer increments on edit. Old content overwritten in `sprk_systemprompt`.

**Pros**:
- Lower row count
- SME-friendly in-place edit

**Cons**:
- **No rollback path** without a parallel version-history table. Phase 1.5 won't build one (PR-1 forbids new entities; also out of scope).
- A/B testing requires forking the row (which collapses back to Option A)
- The version counter is informational only; the engine has no way to "execute v1 of action X" once the row has been edited
- Conflicts with the "Classify Document" precedent (which uses immutable rows)
- Dataverse audit logs help reconstruct prior content but are operational, not designed for rollback workflows

#### Option C — Soft versioning (version field + status flags)

Row carries `version`, `status` (`draft` / `active` / `deprecated`). Multiple rows per logical action can exist simultaneously, but only one is `active`.

**Pros**:
- Promotes draft → active without losing history
- Compatible with rollback (flip `active` from new back to old)

**Cons**:
- **Doesn't exist on current schema**: `sprk_analysisaction` has `statecode` + `statuscode` (Active / Inactive) but no semantic "draft / active / deprecated" trio. Adding requires schema change (forbidden per PR-1 spirit + Wave A4 scope).
- Resolution rule becomes "find the active row" which is a query, not a deterministic action-code lookup → introduces invocation-time uncertainty (which deprecated row was active 6 weeks ago?)

### 3.2 Decision: Option A (new row per version) with `@vN` suffix on action code

**Format**:
- Logical action code: `INS-L1-CLASS` (no version suffix at the "logical" level)
- Physical action codes (the actual `sprk_analysisaction.sprk_actioncode` values): `INS-L1-CLASS@v1`, `INS-L1-CLASS@v2`, …
- Variant (per Decision 1 escape hatch): `INS-L1-CLASS.CTRNS@v1`, `INS-L1-CLASS.CTRNS@v2`, …

The base row created in Wave B2 (e.g., `INS-FACT`, `INS-IDXR`, etc., which DO NOT have `@v1`) gets renamed to `<code>@v1` in Wave C2 as part of the prompt-content migration. **This is a load-bearing change to Wave C2**: it's not just "fill the prompt JSON"; it's "fill the prompt JSON + rename the action code to `@v1`". Playbook JSONs created in Wave C1 reference `@v1` from the start.

**Why Option A**:

1. **Immutability matches the "Classify Document" precedent** (ACT-021's `metadata.author = "migration"` + frozen createdAt are evidence of the implicit immutability assumption already in the codebase). r1's design did NOT version-suffix action codes; Phase 1.5 corrects that.
2. **Rollback is a 1-line playbook JSON edit** (`actionCode: "INS-L1-CLASS@v1"` → `actionCode: "INS-L1-CLASS@v2"`). No row-state flip; no risk of "did the rollback actually take effect?"
3. **A/B test is a 2-playbook diff** (`predict-matter-cost@v1.A` pins `INS-L1-CLASS@v1`; `predict-matter-cost@v1.B` pins `@v2`; route 50/50 by tenant or by query parameter).
4. **No schema change**, no engine change. `/jps-action-create` skill authors the new `@vN` row; existing playbook engine resolves by action code as it does today.
5. **Migration cost is bounded**: Wave C2 already needs to fill all Insights rows' prompt JSON; renaming the action code at the same time is trivial.

**Why NOT Option B**: irreversible content loss; no A/B story; conflicts with existing precedent.
**Why NOT Option C**: requires schema additions (forbidden); query-based resolution adds non-determinism.

### 3.3 Versioning rules (binding)

1. **Rows pinned by a playbook are immutable.** Once `predict-matter-cost@v1` references `INS-EVID@v1`, the `INS-EVID@v1` row's `sprk_systemprompt` MUST NOT be edited in-place. Edit = create `INS-EVID@v2`.
2. **Version increment policy**: any change to `instruction` (role, task, constraints, context), `input`, `output`, or `scopes` requires a new version. Cosmetic changes (`metadata.description`, `metadata.tags` typo fix) MAY edit in place — they don't affect runtime behavior.
3. **The "@vN" suffix is on the physical `sprk_actioncode`**, not in JPS `$version`. JPS `$version` continues to reflect the schema version (`1`); the prompt-content version lives in the action code suffix. **Rationale**: the JPS `$version` field is for the schema/contract, not the row's content history.
4. **No automatic deprecation**. Old versions stay `statecode = Active` indefinitely unless explicitly retired. Retirement = a separate operational decision documented in `metadata.retiredAt` + setting `statecode = Inactive`.
5. **Convention: highest `@vN` is the current "blessed" version** for new playbook authoring. Wave A4 does NOT introduce a "current pointer" row; convention is sufficient for Phase 1.5 scale.

---

## 4. Decision 3 — Per-tenant override mechanism

### 4.1 Options considered

#### Option A — Tenant-scoped variant rows with fallthrough resolution

Action code carries a tenant suffix when overridden: `INS-L1-CLASS@v1.tenant-<tenantId>`. At invocation, the resolver first looks up the tenant-scoped row; if absent, falls back to the global `INS-L1-CLASS@v1`.

**Pros**:
- Same primitive as variant rows + versioning (no new mechanism to learn)
- Tenant overrides are discrete rows = full audit trail per tenant
- Resolver is one extra Dataverse query (acceptable for Phase 1.5 scale)
- Removing an override = delete the tenant row; deterministic fallback to global

**Cons**:
- Row count grows with override count (manageable since most tenants won't override)
- Resolution is 2 queries instead of 1 (acceptable; cacheable per-invocation)
- Tenant ID embedded in action code — looks ugly (mitigated by hashing or shortening if needed; not a blocker)

#### Option B — Override mapping table

A separate `sprk_actionoverride` table maps `(tenantId, baseActionCode) → overrideActionId`. Resolver queries the table.

**Pros**:
- Action codes stay clean (no tenant suffix)
- Override is decoupled from the action row (override row's action can have any code)

**Cons**:
- **Requires a new entity** — borderline PR-1 violation. PR-1 forbids new prompt entities; an override mapping table is metadata, not a prompt, but the line is blurry enough to invite re-litigation
- Two-table resolution = two Dataverse queries per invocation, same cost as Option A
- Override authoring is 2-row: create the override action row AND the mapping row. Higher friction.

#### Option C — Tenant blocks within the JPS schema

JPS extension: `instruction.tenantOverrides: { "tenant-<id>": { "role": "...", "constraints": [...] } }`. Engine applies the block at invocation.

**Pros**:
- Single row regardless of tenant override count

**Cons**:
- **Requires engine patches** — same problem as Decision 1 Option C
- Couples the prompt content to tenant override content (one SME editing the global prompt sees + can break tenant overrides)
- No per-tenant audit trail (all overrides commit to the same row)
- Single row size grows with tenant count

### 4.2 Decision: Option A (tenant-scoped variant rows with fallthrough)

**Action code format** (combining all three decisions):

```
<base>[.<practiceAreaCode>]@v<n>[.tenant-<tenantId>]

Examples:
INS-L1-CLASS@v1                            # global, parametric variant, version 1
INS-L1-CLASS.CTRNS@v1                      # global, CTRNS-specific variant (escape hatch), version 1
INS-L1-CLASS@v1.tenant-<id>                # tenant override of the global parametric variant
INS-L1-CLASS.CTRNS@v1.tenant-<id>          # tenant override of the CTRNS variant
```

**Resolution order at invocation** (binding):

1. Tenant-specific variant for the area: `<base>.<areaCode>@v<n>.tenant-<tenantId>`
2. Tenant-specific parametric base: `<base>@v<n>.tenant-<tenantId>`
3. Global variant for the area: `<base>.<areaCode>@v<n>`
4. Global parametric base: `<base>@v<n>`

First hit wins. If (4) doesn't exist, that's a deploy-time bug — `Deploy-Playbook.ps1` lint (Wave B3) catches missing global base rows.

**Why Option A**:

1. **Same primitive as the other two decisions**. SMEs learn one mechanism (the suffixed action-code convention). Resolvers learn one query pattern (suffix-stripping fallthrough).
2. **PR-1 compliant**: no new entities. Tenant overrides are just more `sprk_analysisaction` rows.
3. **Audit trail per tenant**: when a tenant override is created, modified, or deleted, the row's history reflects it. Multi-tenant compliance audits can show "what prompt content was active for tenant X on date Y" by querying row history.
4. **Removal is deterministic**: delete the tenant row → next invocation falls through to the global row. No flag-flip race.
5. **Resolver cost acceptable**: at Phase 1.5 scale (10s of tenants × low override frequency), the up-to-4-query fallthrough is fast (single-table Dataverse query, indexed on `sprk_actioncode`); in-memory cached per-invocation. Phase 2+ can add cross-invocation caching if needed.

**Why NOT Option B**: borderline PR-1 violation; same query cost; higher authoring friction.
**Why NOT Option C**: requires engine patches; couples global + tenant content; no per-tenant audit trail.

### 4.3 Tenant override rules (binding)

1. **Tenant ID format**: use the Spaarke tenant identifier (Dataverse tenant `sprk_tenant.sprk_tenantcode` if present, else Azure AD tenant ID GUID). Convention decision deferred to Wave D2 task authoring; for Wave A4 this design accepts either format with `tenant-<id>` as the action-code suffix.
2. **Tenant overrides also version**. A tenant can pin a tenant-specific `@v2` while the global is `@v1`. Resolution order respects version pinning: the playbook node specifies the version; the resolver finds the tenant override at that version.
3. **Tenant override edits are immutable too** (per Decision 2 rules). Edit = new version of the tenant override row.
4. **No partial overrides** in Phase 1.5. A tenant either overrides the entire prompt (full new row) or uses the global. This avoids the "merge global + tenant deltas" complexity of Option C.
5. **Tenant overrides MUST carry `metadata.tenantId` + `metadata.derivedFrom`** for traceability (e.g., `derivedFrom: "INS-L1-CLASS@v1"`).

---

## 5. Worked example — CTRNS Layer 1 Classify prompt under the chosen options

**Scenario**: SME authors the initial Layer 1 Classify prompt for Commercial Transactions (CTRNS) practice area, in the global catalog. Two months later, tenant Acme wants a tightened version of CTRNS classification with different secondary-classification thresholds. Three months later, an SME finds a constraint typo in the global prompt and ships v2. The Acme override stays on v1 until Acme onboards v2 separately.

### 5.1 Initial state (end of Wave B2; before Wave C2)

8 Insights rows exist with empty/placeholder `sprk_systemprompt`:

| `sprk_actioncode` | `sprk_name` | `sprk_systemprompt` |
|---|---|---|
| `INS-FACT` | Insights — Live Fact Resolver | (placeholder) |
| `INS-IDXR` | Insights — Index Retrieve | (placeholder) |
| `INS-EVID` | Insights — Evidence Sufficiency | (placeholder) |
| `INS-GRND` | Insights — Grounding Verify | (placeholder) |
| `INS-DECL` | Insights — Decline to Find | (placeholder) |
| `INS-RART` | Insights — Return Insight Artifact | (placeholder) |
| `INS-OBS` | Insights Observation Mirror | (placeholder) |
| `INS-AGNT` | Insights — Agent Service Synthesis | (placeholder) |

These are existing rows. A Layer 1 Classify row does NOT yet exist (it's added in Wave C2 / D2 — Layer 1 Classify is a NEW logical step not in r1's 8-row set).

### 5.2 Wave C2 — Migrate `classification.v1.txt` content into the parametric base row

**Step C2.1**: Create the new base row via `/jps-action-create`:

| Field | Value |
|---|---|
| `sprk_actioncode` | `INS-L1-CLASS@v1` |
| `sprk_name` | `Insights — Layer 1 Classify (v1)` |
| `sprk_description` | Practice-area-aware Layer 1 document classification. Parametric over area-specific categories + context. |
| `sprk_tags` | `insights, layer1, classification, parametric` |
| `sprk_sortorder` | 10 |
| `statecode` | Active |

`sprk_systemprompt`:

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "You are a legal-document classification expert. You classify documents into practice-area-specific categories with high accuracy, citing the structural and content features that drove the classification.",
    "task": "Analyze the provided document and classify it into the most appropriate category from the supplied {{practiceAreaCode}} category catalog. Emit a primary classification, confidence, a one-sentence reasoning citing the features observed, and any plausible secondary classifications.",
    "constraints": [
      "The primary category MUST be exactly one of the values in {{categories}}",
      "Confidence MUST be in [0.0, 1.0]",
      "Reasoning MUST be one sentence (12-30 words) citing specific structural or content features",
      "Secondary classifications included only if confidence > 0.3",
      "Return ONLY valid JSON — no markdown fences, no preamble",
      "Practice-area context: {{practiceAreaContext}}"
    ],
    "context": "This is an automated document classification system used in a legal document management platform. Classification drives document triage, search indexing, and workflow routing. The category catalog is practice-area-specific; use the supplied catalog as the authoritative enum."
  },
  "input": {
    "document": {
      "required": true,
      "maxLength": 12000,
      "placeholder": "{{document.extractedText}}"
    },
    "parameters": {
      "categories": {
        "type": "array",
        "description": "Practice-area-specific category catalog",
        "placeholder": "{{categories}}"
      },
      "practiceAreaCode": {
        "type": "string",
        "description": "Practice-area code (e.g., CTRNS, IPPAT)",
        "placeholder": "{{practiceAreaCode}}"
      },
      "practiceAreaContext": {
        "type": "string",
        "description": "Brief practice-area context (1-2 sentences) clarifying what counts as 'outcome-bearing' in this area",
        "placeholder": "{{practiceAreaContext}}"
      }
    }
  },
  "output": {
    "fields": [
      { "name": "category",      "type": "string", "description": "Primary category (must be one of categories[])" },
      { "name": "confidence",    "type": "number", "description": "Confidence [0.0, 1.0]" },
      { "name": "reasoning",     "type": "string", "description": "One-sentence rationale citing observed features" },
      { "name": "secondaryClassifications", "type": "array", "items": { "type": "object", "properties": { "category": { "type": "string" }, "confidence": { "type": "number" } } } }
    ],
    "structuredOutput": true
  },
  "scopes": {},
  "examples": [
    {
      "input": "ASSET PURCHASE AGREEMENT … (parties, purchase price $12M, closing date 2026-04-15, representations & warranties, indemnification cap 20% of purchase price …)",
      "parameters": {
        "practiceAreaCode": "CTRNS",
        "categories": ["closing_statement", "asset_purchase_agreement", "financing_agreement", "term_sheet", "deal_memo", "other"],
        "practiceAreaContext": "Commercial Transactions: 'outcome-bearing' = the document reflects a closed or definitively-structured deal (signed APA, closing statement, executed financing agreement)."
      },
      "output": {
        "category": "asset_purchase_agreement",
        "confidence": 0.94,
        "reasoning": "Document contains parties, purchase price, closing date, and an explicit indemnification cap structure characteristic of an executed APA.",
        "secondaryClassifications": [ { "category": "closing_statement", "confidence": 0.32 } ]
      }
    }
  ],
  "metadata": {
    "author": "wave-c2-migration",
    "authorLevel": 0,
    "createdAt": "2026-06-08T00:00:00Z",
    "description": "Wave C2 migration from Services/Ai/Insights/Prompts/classification.v1.txt → parametric per-area Layer 1 classifier. Per Wave A4 design.",
    "tags": ["insights", "layer1", "classification", "parametric", "wave-c2"],
    "practiceAreaScope": "ALL",
    "supersedes": "classification.v1.txt"
  }
}
```

**Step C2.2**: Wave C2 also deletes `classification.v1.txt` (per spec.md NFR-02: zero `.txt` prompt files after Wave C2).

### 5.3 Wave D2 — Bind CTRNS category catalog (no new row needed)

CTRNS Layer 1 invocation uses `INS-L1-CLASS@v1` with these parameter bindings (from `sprk_practicearea_ref.CTRNS` row or its associated catalog):

| Parameter | Bound value |
|---|---|
| `practiceAreaCode` | `CTRNS` |
| `categories` | `["closing_statement", "asset_purchase_agreement", "financing_agreement", "term_sheet", "deal_memo", "loan_agreement", "security_agreement", "other"]` |
| `practiceAreaContext` | `Commercial Transactions: outcome-bearing = definitively-structured deal docs (signed APA, closing statement, executed financing).` |

No new `sprk_analysisaction` row created for CTRNS Layer 1. The parametric base row handles it.

### 5.4 Tenant Acme override (later)

Acme wants a tighter prompt: secondary-classification threshold raised from 0.3 → 0.5; an additional constraint demanding the reasoning cite a specific dollar amount when present.

**Step**: Create `INS-L1-CLASS@v1.tenant-acme` as a clone of `INS-L1-CLASS@v1` with the Acme-specific changes. `metadata.derivedFrom = "INS-L1-CLASS@v1"`. `metadata.tenantId = "acme"`.

Acme's invocations resolve as:
1. Lookup `INS-L1-CLASS.CTRNS@v1.tenant-acme` → not found
2. Lookup `INS-L1-CLASS@v1.tenant-acme` → **found** → use this row
3. (Resolution stops; global row not consulted)

Non-Acme tenants continue to resolve to `INS-L1-CLASS@v1`.

### 5.5 Global v2 ships (later still)

SME finds a constraint typo in the global v1 and ships v2 (e.g., adds "Avoid hallucinating secondary classifications when the document is short" constraint).

**Step**: Create `INS-L1-CLASS@v2` row. Update playbook JSON for non-Acme tenants to reference `@v2`. Acme remains pinned to `@v1` until Acme separately reviews + onboards v2.

| Row | Used by |
|---|---|
| `INS-L1-CLASS@v1` | Acme (via `@v1.tenant-acme`) — stays referenced via the resolver fallthrough |
| `INS-L1-CLASS@v1.tenant-acme` | Acme tenant override |
| `INS-L1-CLASS@v2` | All non-Acme tenants |

`@v1` is NOT deleted (immutable; still referenced by Acme's override chain).

---

## 6. Rollback story

**Trigger**: Production observes degraded classification accuracy after deploying `@v2`. SME wants to revert.

**Steps**:

1. Edit the playbook JSON (`predict-matter-cost@v1` or whichever is affected). Change the Layer 1 Classify node's `actionCode` from `INS-L1-CLASS@v2` back to `INS-L1-CLASS@v1`.
2. Re-deploy the playbook via `scripts/Deploy-Playbook.ps1` (no node deletion needed; Wave B3 lint accepts the change).
3. Next invocation uses `@v1`. No row state change. No race condition.

**Time-to-rollback**: minutes (deploy script run + cache invalidation, if cached).

**Audit trail**: the playbook JSON change is in git; the `@v2` row remains in Dataverse as the historical record of what was tried.

**Tenant override rollback**: if Acme's `tenant-acme` override is degrading, delete the `tenant-acme` row. Next Acme invocation falls through to the global. Time-to-rollback: minutes (Dataverse row delete).

**Why this rollback story works**: immutability + name-based binding. The playbook says "use action code X@vN"; the resolver finds the row matching that exact name. Changing the playbook = changing the binding = effective rollback.

---

## 7. A/B test story

**Trigger**: SME wants to compare `@v1` vs `@v2` on a 10% slice of traffic before full rollout.

**Two implementation options**:

### 7.1 A/B by playbook fork

1. Create `predict-matter-cost@v1.B` playbook (clone of `@v1` with the Layer 1 Classify node pointing at `INS-L1-CLASS@v2`).
2. Configure the assistant/dispatcher to route 10% of tenants (or 10% of queries) to `@v1.B`; 90% to `@v1`.
3. Compare metrics across the two playbook executions (`sprk_playbookrun` rows tagged with `playbookCode = predict-matter-cost@v1` vs `@v1.B`).
4. Once `@v1.B` proven better, retire `@v1` (set the original playbook's Layer 1 Classify node to `@v2`); keep `@v1.B` as historical archive.

**Pros**: clean separation; comparison is at the playbook-run level (existing telemetry).
**Cons**: requires playbook fork (more rows in `sprk_playbook`).

### 7.2 A/B by tenant override

1. Create `INS-L1-CLASS@v2.tenant-pilot` row for a pilot tenant.
2. The pilot tenant's invocations use `@v2`; everyone else uses `@v1`.
3. Compare metrics filtered by `tenantId = pilot` vs others.

**Pros**: no playbook fork; only the override row added.
**Cons**: tenant-scoped A/B is coarser (a tenant is either in or out; can't split within a tenant).

**Recommended default**: option 7.1 (playbook fork) for general A/B; option 7.2 for tenant-pilot programs.

**Why this A/B story works**: versioning is row-level + immutable, so any two versions can be exercised concurrently by routing to different action codes. The routing happens at the playbook level (option 7.1) or at the resolver level (option 7.2); both are deterministic.

---

## 8. What this design does NOT include (deferred to later waves or Phase 2+)

| Concern | Deferred to | Why |
|---|---|---|
| SME authoring UI for variants | Phase 2+ | FR-06 SMEs edit via Dataverse action row UI; tooling not required for acceptance |
| Cross-invocation caching of resolved action codes | Phase 2+ | Phase 1.5 scale doesn't justify the cache invalidation complexity |
| Schema additions (e.g., explicit `practiceArea` lookup on `sprk_analysisaction`) | Phase 2+ | `metadata.practiceAreaScope` in JPS suffices; schema additions are out of A4 scope |
| Automated retirement of old versions | Phase 2+ | Operational decision; no automation needed for Phase 1.5 |
| "Current version" pointer mechanism | Phase 2+ | Convention (highest `@vN`) suffices for Phase 1.5 |
| Override merge semantics (tenant override inherits parts of global) | Out (Phase 2+ if needed) | Full-row override only per Decision 3 rules; merge adds complexity for no clear Phase 1.5 win |
| Partial-row deltas between versions | Out (Phase 2+ if needed) | Whole-row versioning matches "Classify Document" precedent and engine resolution model |

---

## 9. Validation against acceptance criteria + invariants

| Criterion | Status |
|---|---|
| Variant pattern picked with reasoning | ✅ §2 (Hybrid: parametric default + variant-row escape hatch) |
| Versioning model picked with reasoning | ✅ §3 (New row per version, `@vN` suffix on action code) |
| Tenant-override mechanism picked with reasoning | ✅ §4 (Tenant-scoped variant rows + fallthrough resolution) |
| Worked example: CTRNS Layer 1 under chosen options | ✅ §5 (end-to-end including parameter binding + tenant override + version uplift) |
| Rollback story | ✅ §6 |
| A/B test story | ✅ §7 |
| **PR-1 invariant (no new `sprk_prompt` entity)** | ✅ All three decisions use `sprk_analysisaction` rows only |
| Existing `sprk_analysisaction` schema unchanged | ✅ All three decisions work on current schema (action code + system prompt + statecode); no new columns required |
| JPS shape preserved | ✅ Additions are `metadata.*` extensions only (`practiceAreaCode`, `practiceAreaScope`, `tenantId`, `derivedFrom`, `supersedes`); no engine change required |

---

## 10. Downstream impact (what Waves C2 / D2 / D3 / D4 must do)

| Wave | Action |
|---|---|
| **C2 (021)** | Migrate `.txt` prompts → `sprk_analysisaction.sprk_systemprompt` AND rename action codes to `<base>@v1`. The 8 existing Wave B2 rows (`INS-FACT` … `INS-AGNT`) get renamed to `INS-FACT@v1` … `INS-AGNT@v1` as part of this wave. Create new `INS-L1-CLASS@v1` row for the migrated `classification.v1.txt` content. |
| **C1 (020)** | Universal-ingest playbook JSON references action codes WITH the `@vN` suffix. `Deploy-Playbook.ps1` lint (Wave B3) updated to accept the suffix syntax. |
| **D2 (031)** | Initial 3 practice areas use the parametric base row by default. If any of the 3 areas demands the escape-hatch variant row (Decision 1), the area-specific row is created at `<base>.<areaCode>@v1` and the playbook routes to it via Decision 1's resolution rule. |
| **D3 (032)** | Layer 2 schemas follow the same pattern: `INS-L2-EXTRACT@v1` parametric base; `INS-L2-EXTRACT.<area>.<docType>@v1` only when structural variation is needed. |
| **D4 (033)** | Universal-ingest routing logic implements Decision 1's resolution rule + Decision 3's tenant-fallthrough resolution. |

---

## 11. Open questions for later waves (not blocking Wave A4)

| Question | Owner wave | Why deferred |
|---|---|---|
| Tenant ID format convention (`sprk_tenant.sprk_tenantcode` vs Azure AD tenant GUID) | Wave D2 / D5 | Depends on whether D5 multi-entity work standardises a tenant identifier |
| Should `sprk_playbook` JSON pin versions explicitly or use convention "latest"? | Wave C1 | Wave C1 is the first to author a playbook against the `@vN` convention; can adopt one approach |
| Cache strategy for resolver lookups | Phase 2+ | No Phase 1.5 acceptance criterion demands it |
| Automated lint for "is this action code referenced anywhere?" before retirement | Phase 2+ | Operational, not architectural |

---

*End of design-a4-prompt-variants.md.*
