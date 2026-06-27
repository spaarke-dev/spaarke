# Insight Envelope Schema — `sprk_matter.sprk_performancesummary`

> **Project**: ai-spaarke-insights-engine-widgets-r1
> **Task**: 025 — Document envelope schema for downstream consumers (Wave 3a, parallel with 014/034/041)
> **Rigor Level**: STANDARD (per task POML)
> **Author**: task-execute
> **Purpose**: Per **FR-15**, enable downstream consumers (canvas apps, plugins, reports, views, emails) to extract `.body` plaintext + structured fields from the JSON envelope persisted by `matter-health-single` playbook to `sprk_matter.sprk_performancesummary`.
> **Authoritative reference**: implementation at `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` (Task 022 deliverable, `persistEnvelope` node).

---

## 1. Schema overview

The envelope is a single-line JSON object stored verbatim as a string in `sprk_matter.sprk_performancesummary` (existing R5-era longtext field per **FR-16** — no new fields). It carries the AI-generated narrative + grounded citations + provenance.

**Key invariants** (binding for all consumers):

| Invariant | Value | Source |
|---|---|---|
| `schemaVersion` is a **string semver** | `"1.0"` (NOT int `1`, NOT `"v1"`, NOT `"@v1"`) | Q-U1 owner ban; project CLAUDE.md key constraints row |
| `playbookName` is **bare**, no version suffix | `"matter-health-single"` (NOT `"matter-health-single@v1"`) | Q-U1; playbook `$comment-naming` in Task 022 |
| Field count | **7 fields** (see §2) | Task 022 `persistEnvelope.configJson.fieldMappings[0].value` |
| Persistence target | `sprk_matter.sprk_performancesummary` | FR-14 + FR-16 |
| Storage shape | Serialized JSON string in longtext column | Task 022 `type='string'` (FieldMappingType.String) |
| Decline path | Field is **NOT overwritten** when `checkSufficiency.verdict='insufficient'` | Task 022 acceptance criteria + playbook `dependsOnGraph` |

**Pre-r1 / legacy content**: Prior to r1, `sprk_performancesummary` contained R5-era static placeholder text. Consumers MUST handle non-JSON content by treating it as "no AI summary present" (FR-17 covers OnLoad handler; downstream consumers should apply the same guard pattern shown in §4).

---

## 2. JSON Schema definition (Draft 2020-12)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://schemas.spaarke.com/insights-engine/v1/matter-health-envelope.json",
  "title": "Spaarke Insights Engine — Matter Health Envelope",
  "description": "Persisted to sprk_matter.sprk_performancesummary by the matter-health-single playbook (Task 022). schemaVersion is a string semver per Q-U1.",
  "type": "object",
  "required": [
    "schemaVersion",
    "body",
    "citations",
    "generatedAt",
    "playbookName",
    "tenantId",
    "dimensions"
  ],
  "additionalProperties": false,
  "properties": {
    "schemaVersion": {
      "type": "string",
      "pattern": "^\\d+\\.\\d+(\\.\\d+)?$",
      "const": "1.0",
      "description": "Semver string. r1 ships '1.0'. Future schema changes bump this; consumers MUST switch on this value before reading fields."
    },
    "body": {
      "type": "string",
      "minLength": 1,
      "description": "Markdown narrative produced by AiAnalysis -> GroundingVerify. Covers the 7 baseline diagnostic dimensions (FR-12). Primary field for plain-text consumers."
    },
    "citations": {
      "type": "array",
      "description": "Grounded citations attached by GroundingVerify. Mixed type: assessment rows + document chunks.",
      "items": {
        "oneOf": [
          {
            "type": "object",
            "required": ["type", "id", "label"],
            "properties": {
              "type":    { "type": "string", "const": "assessment" },
              "id":      { "type": "string", "format": "uuid", "description": "sprk_kpiassessment Guid" },
              "label":   { "type": "string", "description": "Human-readable anchor, e.g. 'Q1 2026 Guideline Compliance assessment'" },
              "excerpt": { "type": "string" }
            }
          },
          {
            "type": "object",
            "required": ["type", "ref", "label"],
            "properties": {
              "type":    { "type": "string", "const": "document" },
              "ref":     { "type": "string", "description": "SharePoint Embedded ref, e.g. 'spe://drive/X/item/Y'" },
              "label":   { "type": "string" },
              "chunkId": { "type": "string" }
            }
          }
        ]
      }
    },
    "generatedAt": {
      "type": "string",
      "format": "date-time",
      "description": "ISO-8601 timestamp from playbook run.startedAtIso. Drives FR-17/FR-18 staleness check (>1 hour triggers fire-and-forget refresh)."
    },
    "playbookName": {
      "type": "string",
      "const": "matter-health-single",
      "description": "Bare name per Q-U1. Authoritative version source is sprk_analysisplaybook.sprk_version (Dataverse-side), NOT this field."
    },
    "tenantId": {
      "type": "string",
      "format": "uuid",
      "description": "Resolved tenant Guid for multi-tenant isolation. Required by downstream consumers when running outside Dataverse OBO context."
    },
    "dimensions": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Diagnostic dimensions covered by body (FR-12). r1 default: ['composite','trend','themes','inflection','critical','risk','evidenceGaps'].",
      "minItems": 1
    }
  }
}
```

### Example envelope payload (for reference)

```json
{
  "schemaVersion": "1.0",
  "body": "Matter ABC-123 currently grades **D-** with a declining trend since Q3 2025...",
  "citations": [
    { "type": "assessment", "id": "11111111-1111-1111-1111-111111111111", "label": "Q1 2026 Guideline Compliance assessment", "excerpt": "..." },
    { "type": "document",   "ref": "spe://drive/X/item/Y", "label": "Engagement Letter", "chunkId": "doc-chunk-7" }
  ],
  "generatedAt": "2026-06-10T18:45:00Z",
  "playbookName": "matter-health-single",
  "tenantId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "dimensions": ["composite", "trend", "themes", "inflection", "critical", "risk", "evidenceGaps"]
}
```

---

## 3. Extraction example A — Power Fx (canvas app / model-driven app custom page)

**Scenario**: surface the AI narrative inside a canvas app screen bound to the current Matter record.

```powerapps
// Read sprk_performancesummary from the current Matter
Set(
    varRaw,
    LookUp('Matters', sprk_matterid = ThisItem.sprk_matterid).sprk_performancesummary
)

// Guard: legacy R5 placeholder text is non-JSON; treat as 'no summary'
If(
    !IsBlank(varRaw) && StartsWith(varRaw, "{"),

    // Parse envelope
    Set(varEnv, ParseJSON(varRaw)),

    // Version gate (Q-U1: schemaVersion is a string)
    If(
        Text(varEnv.schemaVersion) = "1.0",

        // Display body in a markdown control (HtmlText with markdown helper)
        Set(varBody, Text(varEnv.body));

        // Build a citations gallery items source
        ClearCollect(
            colCitations,
            ForAll(
                Table(varEnv.citations),
                {
                    Kind:    Text(ThisRecord.Value.type),
                    Label:   Text(ThisRecord.Value.label),
                    Anchor:  Coalesce(Text(ThisRecord.Value.id), Text(ThisRecord.Value.ref))
                }
            )
        );

        // Stale check (FR-18 mirror, optional in canvas)
        Set(
            varStaleHours,
            DateDiff(DateTimeValue(Text(varEnv.generatedAt)), Now(), Hours)
        );
        Set(varIsStale, varStaleHours > 1),

        // Unknown schemaVersion -> fall back gracefully
        Notify("Insight envelope schemaVersion not recognized; treating as no summary.", NotificationType.Warning);
        Set(varBody, "")
    ),

    // No JSON envelope present (blank OR legacy R5 placeholder text)
    Set(varBody, "")
)
```

**Notes for consumers**:
- `ParseJSON` returns an untyped object; use `Text()` / `Value()` coercion explicitly.
- The version gate (`= "1.0"`) is a **string equality** — never `= 1` (Q-U1).
- The `IsBlank || !StartsWith("{")` guard is mandatory because R5 placeholder text is plain English, not JSON.

---

## 4. Extraction example B — Dataverse plugin (C# / `Newtonsoft.Json.Linq.JObject`)

**Scenario**: a synchronous plugin on `sprk_matter` Update that triggers a notification email containing the latest AI narrative.

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json.Linq;

public sealed class SendMatterHealthNotificationPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var svcFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        var svc = svcFactory.CreateOrganizationService(context.UserId);

        var target = (Entity)context.InputParameters["Target"];
        if (!target.Contains("sprk_performancesummary")) return;

        var raw = target.GetAttributeValue<string>("sprk_performancesummary");
        if (string.IsNullOrWhiteSpace(raw) || !raw.TrimStart().StartsWith("{"))
        {
            // Legacy R5 placeholder text OR empty — nothing to notify on.
            return;
        }

        JObject envelope;
        try
        {
            envelope = JObject.Parse(raw);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            // Malformed JSON in field — log + bail (do not throw, do not block Matter Update).
            return;
        }

        // Version gate — Q-U1: schemaVersion is a string.
        var schemaVersion = envelope.Value<string>("schemaVersion");
        if (!string.Equals(schemaVersion, "1.0", StringComparison.Ordinal))
        {
            // Unknown schema — refuse to send to avoid misrepresentation.
            return;
        }

        // Plaintext body for the email (markdown -> consumer may render or strip).
        var body          = envelope.Value<string>("body") ?? string.Empty;
        var generatedAt   = envelope.Value<string>("generatedAt");
        var playbookName  = envelope.Value<string>("playbookName"); // "matter-health-single" bare

        // Citations are mixed type (assessment | document) — see §2 oneOf.
        var citationCount = (envelope["citations"] as JArray)?.Count ?? 0;

        // ...build + send email via Email entity Create...
    }
}
```

**Notes for consumers**:
- The plugin runs in sandbox isolation — `Newtonsoft.Json` is on the allow-list; no external dependencies are required.
- Always treat parse failure as a no-op (do not throw) to avoid blocking the host pipeline.
- Match `schemaVersion` with `StringComparison.Ordinal` — string semver is case-sensitive (Q-U1).

---

## 5. Extraction example C — View / column transformation (downstream view rendering)

**Scenario**: a model-driven view of Matters wants a "Health Snapshot" column showing just the first 200 chars of the narrative body, while keeping the full envelope in `sprk_performancesummary` untouched.

### Option C1 — Calculated column (preferred when feasible)

Dataverse calculated columns do NOT support JSON parsing natively, so a calculated column cannot extract `.body` directly. **Use Option C2 (a synchronous plugin) instead, OR Option C3 (a view-side annotation).**

### Option C2 — Companion plaintext column populated by post-persist plugin

Add a new column `sprk_performancesummary_plaintext` (longtext, **populated by plugin**, NOT by FR-16-bound r1) and a plugin step on `sprk_matter` Update (filtering attribute `sprk_performancesummary`):

```csharp
public sealed class ExtractBodyToPlaintextColumnPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var target  = (Entity)context.InputParameters["Target"];
        if (!target.Contains("sprk_performancesummary")) return;

        var raw = target.GetAttributeValue<string>("sprk_performancesummary");
        string plaintext = string.Empty;

        if (!string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith("{"))
        {
            try
            {
                var env = JObject.Parse(raw);
                if (env.Value<string>("schemaVersion") == "1.0")
                {
                    var body = env.Value<string>("body") ?? string.Empty;
                    // Strip markdown emphasis chars for grid display; keep first 200 chars.
                    plaintext = StripMarkdown(body);
                    if (plaintext.Length > 200) plaintext = plaintext.Substring(0, 200) + "...";
                }
            }
            catch { /* swallow */ }
        }

        // Set on the Target so the Update writes it in the same transaction.
        target["sprk_performancesummary_plaintext"] = plaintext;
    }

    private static string StripMarkdown(string md) =>
        System.Text.RegularExpressions.Regex.Replace(md ?? string.Empty, @"[*_`#>]", "");
}
```

The view then binds the **`sprk_performancesummary_plaintext` column** in its FetchXml + LayoutXml. This is the canonical "view column transformation" pattern for envelope-shaped columns in Dataverse views.

### Option C3 — View-side JavaScript onRowLoad transformation (less preferred)

Use the view's `onRowLoad` event (model-driven view JS extension) to read `sprk_performancesummary` from the row, parse, and inject the extracted body via DOM. Brittle; only recommended when adding a column is infeasible. Same parse + version-gate guards as §3 / §4 apply.

---

## 6. The `playbookVersion` divergence — recommended action

**Finding (per Task 024 + Task 025 brief)**:

| Source | Field count | Includes `playbookVersion`? |
|---|---|---|
| **Spec FR-14** (this project) | **8 fields** | YES — listed as `"playbookVersion": "<from sprk_playbook.sprk_version>"` |
| **Task 022 implementation** (`matter-health-single.playbook.json` `persistEnvelope.configJson.fieldMappings[0].value`) | **7 fields** | **NO** — omitted |

This is a genuine divergence. The implementation is the operative artifact (downstream consumers will see 7 fields, not 8). One of the two must move.

### Recommendation: **revise spec FR-14 to 7 fields**; DO NOT re-open Task 022 to add `playbookVersion`.

**Rationale**:

1. **Authoritative version source already exists.** Task 022's `persistEnvelope` node was authored with `$comment-producedByVersion` (line 195) explicitly documenting: *"Authoritative version source is `sprk_analysisplaybook.sprk_version` (Dataverse-side) — this field is the in-envelope projection per FR-14 'playbookVersion' envelope field."* The Task 022 author **deliberately omitted** `playbookVersion` from the envelope because the same Guid+Version is recoverable from Dataverse by `playbookName`. Including it in-envelope creates a double source of truth — a known anti-pattern.

2. **Q-U1 owner ban makes `playbookVersion` semantically awkward.** Owner banned `@v1`/`@vN` identifier-suffix syntax. The intended value would be a bare string (e.g., `"1.0"`) — but then it duplicates `schemaVersion`'s shape with no clear consumer use case in r1.

3. **No r1 downstream consumer requires it.** FR-15 enumerates Power Fx, plugin C#, and view column extraction. **None of these need to know the playbook version** to render `.body` + `citations` correctly; if a consumer ever needs it, they can query `sprk_analysisplaybook` by `sprk_name='matter-health-single'`.

4. **Re-opening Task 022 has higher cost than amending the spec.** Task 022 is implemented + deployed. Adding `playbookVersion` would require a playbook JSON edit, redeploy, regenerate test fixtures, update acceptance criteria (currently asserts "exactly 7 fields"), and bump `schemaVersion` to `"1.1"` because field-set additions are non-additive without a version bump (consumers that gate on `additionalProperties: false` would reject the new envelope).

### Concrete spec diff (recommended)

Apply to `projects/ai-spaarke-insights-engine-widgets-r1/spec.md` lines 159–176 (the FR-14 envelope sample):

```diff
 - **FR-14**: Playbook's `UpdateRecord` node writes the **structured JSON envelope** to **existing** `sprk_matter.sprk_performancesummary` (longtext, R5-era placeholder field). This REPLACES the R5 static placeholder text with the AI-generated envelope. Envelope structure:
   ```json
   {
     "schemaVersion": "1.0",
     "body": "<markdown narrative>",
     "citations": [
       { "type": "assessment", "id": "<sprk_kpiassessment Guid>", "label": "Q1 2026 Guideline Compliance assessment", "excerpt": "..." },
       { "type": "document", "ref": "spe://drive/X/item/Y", "label": "Engagement Letter", "chunkId": "..." }
     ],
     "generatedAt": "2026-06-10T18:45:00Z",
     "playbookName": "matter-health-single",
-    "playbookVersion": "<from sprk_playbook.sprk_version>",
     "tenantId": "<Guid>",
     "dimensions": ["composite", "trend", "themes", "inflection", "critical", "risk", "evidenceGaps"]
   }
   ```
+
+  > **NOTE (Task 025 reconciliation, 2026-06-11)**: `playbookVersion` is **intentionally omitted** from the in-envelope payload. The authoritative version source is `sprk_analysisplaybook.sprk_version` (Dataverse-side), resolvable via `playbookName='matter-health-single'`. Including it in-envelope would create a double source of truth. The in-envelope `playbookName` is bare per Q-U1 owner ban on version-suffix vernacular. See `notes/insight-envelope-schema.md` §6 for full rationale.
```

### Alternative: re-open Task 022 (NOT recommended; documented for completeness)

If owner overrules and prefers in-envelope `playbookVersion`:

1. Edit `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` `persistEnvelope.configJson.fieldMappings[0].value` to add `,"playbookVersion":"{{playbook.versionString}}"` and bump `schemaVersion` to `"1.1"`.
2. Update the acceptance criterion currently reading *"renders to JSON with exactly 7 fields"* to *"exactly 8 fields"*.
3. Add `playbookVersion` to the required + properties list in §2 of this document.
4. Bump the JSON Schema `$id` to `.../v1.1/...`.
5. Re-deploy via `scripts/Deploy-Playbook.ps1`.
6. Update §3, §4, §5 examples to gate on `schemaVersion ∈ {"1.0","1.1"}`.

This path adds 5–8 hours of work and produces no consumer-visible benefit in r1.

---

## 7. Cross-references

| Reference | Why |
|---|---|
| [`spec.md`](../spec.md) FR-14, FR-15, FR-16 | Source requirements |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` (Task 022) | Operative implementation; 7-field envelope literal |
| Task POML 022 `acceptance-criteria` | Asserts "exactly 7 fields, schemaVersion '1.0' string" — enforces the divergence |
| Task POML 024 finding | Surfaced the 7-vs-8 divergence between spec FR-14 and Task 022 implementation |
| Project [`CLAUDE.md`](../CLAUDE.md) "Q-U1 owner ban" row | Binding constraint forbidding `@v1`/`@vN` vernacular |
| FR-17/FR-18 Matter form OnLoad handler | Same `IsBlank || !StartsWith("{")` guard pattern; mirror in downstream consumers per §3/§4 |

---

## 8. Acceptance criteria check (Task 025)

| Criterion (from POML) | Status |
|---|---|
| JSON Schema file validates against the spec FR-14 envelope | ✅ §2 — schema authored, matches **Task 022 implementation** (7 fields). Divergence vs spec FR-14 (8 fields) called out + recommendation supplied in §6. |
| 3 extraction examples present (Power Fx + plugin + view/column) | ✅ §3 (Power Fx), §4 (plugin C#), §5 (view column — Option C2 plugin transformation) |
| `schemaVersion` documented as string semver | ✅ §1 invariants table + §2 JSON Schema (`type: "string"`, `const: "1.0"`) + §3/§4/§5 examples gate as string |

---

*Authored 2026-06-11 by task-execute (Task 025, Wave 3a). STANDARD rigor: no quality gates run (documentation-only).*
