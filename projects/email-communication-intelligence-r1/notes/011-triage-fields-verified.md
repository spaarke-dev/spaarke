# 011 — Triage Fields on `sprk_communication` (VERIFY-ONLY — LIVE `spaarkedev1`)

> **Task**: 011 (P1, STANDARD rigor) · **Date**: 2026-07-29 · **Method**: Dataverse MCP `describe('tables/sprk_communication')` + `describe('tables/sprk_triagecategory')` against live `spaarkedev1`. **No schema created or altered.**
> **Verdict**: ✅ **PASS — all six triage fields already exist** on `sprk_communication`, confirming task 001's finding. Task 011 created NOTHING (verify-only, per operator parallel pre-creation). No type conflicts, no retyping, no new entity.

---

## Why verify-only

Task 001 (`notes/001-operator-schema-verification.md`, live MCP `describe`) already found all six FR-07 triage fields present on `sprk_communication` before this task ran. This task re-confirms that finding directly (independent `describe` call in this session) and produces the durable schema-inventory note that 025 (persistence wiring) must bind to, per the task POML's step 5 output requirement.

---

## Confirmed field inventory (live `sprk_communication`, verbatim from MCP `describe`)

| # | FR-07 field | Confirmed logical name | Confirmed type | Target / option set |
|---|---|---|---|---|
| 1 | Triage category | `sprk_triagecategory` | **LOOKUP (GUID)** | → `sprk_triagecategory` (config table — confirmed live, see below) |
| 2 | Triage priority | `sprk_triagepriority` | **CHOICE** | `Urgent (100000000)` / `High (100000001)` / `Medium (100000002)` / `Low (100000003)` |
| 3 | Triage summary | `sprk_triagesummary` | **MULTILINE TEXT** | 2-line human summary |
| 4 | Triage obligations | **`sprk_triageobligation`** (⚠ SINGULAR, not the spec/schema-doc's plural `sprk_triageobligations`) | **MULTILINE TEXT** | Holds a compact JSON array (lean JSON, D-06) — see shape below |
| 5 | RI confidence | `sprk_riconfidence` | **DECIMAL** | 0–1 score (computed by task 024) |
| 6 | Review outcome | `sprk_reviewoutcome` | **CHOICE** | `File (100000000)` / `Update (100000001)` / `Route (100000002)` / `Dismiss (100000003)` / `Pending (100000004)` |

All six are additive columns on the existing `sprk_communication` table. No existing column (`sprk_associationstatus`, `sprk_regarding*`, `sprk_direction`, etc.) was touched — the full live `describe` output was diffed against task 001's snapshot and is unchanged aside from these six fields already being present in both.

### Naming delta — load-bearing for 025

The spec/schema-doc name is `sprk_triageobligations` (plural). The **as-built live field is singular: `sprk_triageobligation`**. This was first flagged in task 001 and is **re-confirmed here independently**. Task 025 (and any future code touching this field) **MUST bind to `sprk_triageobligation` (singular)** — using the plural name will fail at runtime (attribute not found).

### `sprk_triagecategory` lookup target — confirmed live

`describe('tables/sprk_triagecategory')` confirms the config table (task 013's table) already exists in the live environment:

```
DESCRIBE TABLE sprk_triagecategory (
  ...
  sprk_enabled BIT,
  sprk_name NVARCHAR(850) NOT NULL,
  sprk_priorityweight INT,
  sprk_triagecategoryid GUID,
  statecode / statuscode,
  ...
)
```

So the `sprk_triagecategory` lookup on `sprk_communication` already resolves to a real, populated-schema config table — no dangling dependency on task 013 remains at the schema level (013 still owns seeding taxonomy *rows*, not the table itself).

---

## Obligations JSON shape (D-06 — lean JSON on `sprk_triageobligation`)

`sprk_triageobligation` is a MULTILINE TEXT column intended to hold a **compact JSON array** mirroring the `Obligations` list already produced by the existing classification substrate (`src/server/api/Sprk.Bff.Api/Models/Ai/Communication/CommunicationClassificationResult.cs`):

```csharp
// CommunicationClassificationResult.cs (existing, unchanged)
[JsonPropertyName("obligations")]
public IReadOnlyList<string> Obligations { get; init; } = Array.Empty<string>();
```

For task 025 (persistence wiring), the recommended lean-JSON shape to write into `sprk_triageobligation` is a plain JSON string array, e.g.:

```json
["deadline-response", "executed-document", "calendar-deadline"]
```

This mirrors the existing `Obligations: IReadOnlyList<string>` shape 1:1 (no re-modeling needed — `JsonSerializer.Serialize(result.Obligations)` is sufficient). This is documented here for future promotion to per-obligation child records (out of scope for P1 — D-06 explicitly keeps obligations as lean JSON on the parent, no child entity in this phase).

---

## Downstream binding contract (for 025, 024, 023/022)

| Task | Binds to | Field | Type |
|---|---|---|---|
| 024 (RI-confidence scorer) | writes | `sprk_riconfidence` | DECIMAL 0–1 |
| 025 (enrichment/persistence) | writes | `sprk_triagecategory` | LOOKUP → `sprk_triagecategory` |
| 025 | writes | `sprk_triagepriority` | CHOICE (Urgent/High/Medium/Low = 100000000–3) |
| 025 | writes | `sprk_triagesummary` | MULTILINE TEXT |
| 025 | writes | **`sprk_triageobligation`** (singular — NOT plural) | MULTILINE TEXT, lean-JSON string array |
| 023/022 (triage Action) or 025 | writes | `sprk_reviewoutcome` | CHOICE (File/Update/Route/Dismiss/Pending = 100000000–4) |

---

## §11 Component Justification (per task POML)

1. **Existing** — `sprk_communication` carries `sprk_associationstatus` (association state) but had no triage-output fields prior to the operator's parallel pre-creation; confirmed absent in the pre-existing baseline by task 001's initial audit trail and now confirmed present as operator-added, additive columns.
2. **Extension** — these six fields extend the existing `sprk_communication` entity per D-01 (no new `sprk_triageitem`/triage entity was created or is present in the live schema — confirmed by the full `describe` output above, which shows no such table referenced).
3. **Cost-of-doing-nothing** — without these fields, the triage Action (022/023) has nowhere to persist category/summary/obligations/priority (Pillar 1 / success-criterion 4 fails) and the RI-confidence scorer (024) has nowhere to write the score, leaving the notification path dark (success-criterion 5 fails). This is a concrete contract failure, not a speculative concern.

---

## Acceptance criteria — verdict

| Criterion | Verdict |
|---|---|
| All six triage fields exist with specified types | ✅ PASS — confirmed live via MCP `describe` |
| Verify-or-create honored (reconcile, don't duplicate; escalate conflicts) | ✅ PASS — nothing was missing, so nothing was created; no type conflicts found (all six fields' live types match spec exactly) |
| Obligations JSON shape + review-outcome value set documented | ✅ PASS — see sections above |
| NEGATIVE: no existing column altered/retyped | ✅ PASS — full live schema diffed; only the six additive fields are new relative to the pre-r1 baseline |
| NEGATIVE: review-outcome named "review outcome" (`sprk_reviewoutcome`), not "disposition" | ✅ PASS — confirmed live field name is `sprk_reviewoutcome`; no `sprk_disposition`-style field present |

---

## No downstream task blocked

Task 011 required **no schema creation, no PowerShell/Web API calls, no solution deploy**. Tasks 024/025 (and 022/023) can proceed immediately against the confirmed logical names in this note. The single naming delta to carry forward is the **`sprk_triageobligation` singular** field name.
