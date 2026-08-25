# Audit Log — root cause

> **Task 005** (spec FR-A05) · **Recorded 2026-08-21 BEFORE any fix**, per the task's binding constraint.
> **Method**: live Dataverse schema + row count via Dataverse MCP against the Spaarke Dev org, cross-checked
> against every lookup filter in the repo.

---

## Verdict

**Classification (b) — the code queries columns that do not exist.** Not a privilege problem, not a disabled
audit setting, not a malformed date filter.

And it is worse than the screen: **the write path is broken the same way and swallows its own failures**, so
there is nothing to read even after the read is fixed.

### Empirical evidence

**1 — the column the screen selects does not exist.** Queried live against Spaarke Dev:

```
SELECT TOP 1 sprk_targetresource FROM sprk_speauditlog
```
```
'sprk_SPEAuditLog' entity doesn't contain attribute with Name = 'sprk_targetresource'
and NameMapping = 'Logical' (look up attribute by name is case-sensitive).
orgIndex: 80, logicalName: sprk_speauditlog
(correlation ID: a7cc55d8-a9e3-4f0f-ae56-707b533e5075)
```

That is the error behind *"Failed to retrieve audit log entries from Dataverse."*

**2 — the corrected column set is valid.** The same query with the fixed names returns cleanly (empty, but
no error), confirming every name in the new `$select` resolves:

```
SELECT TOP 1 sprk_speauditlogid, sprk_operation, sprk_category, sprk_targetresourceid,
             sprk_targetresourcename, sprk_responsestatus, sprk_performedby,
             sprk_performedon, sprk_name
FROM sprk_speauditlog                                              →   []   (no error)
```

**3 — the table is empty.**

```
SELECT COUNT(sprk_speauditlogid) AS total FROM sprk_speauditlog    →   0
```

The table exists with a sensible schema and is **completely empty** despite the app having been exercised.
That is the signature of a write path failing silently, not of a screen that has never been used.

---

## The real schema (live, via MCP)

```
sprk_speauditlog  (collection: sprk_speauditlogs)
  sprk_speauditlogid       GUID
  sprk_name                NVARCHAR(850)  NOT NULL        ← required
  sprk_operation           NVARCHAR(100)
  sprk_category            CHOICE  (ContainerType 100000000, Container 100000001,
                                    Permission 100000002, File 100000003,
                                    Search 100000004, Security 100000005)
  sprk_targetresourceid    NVARCHAR(100)
  sprk_targetresourcename  NVARCHAR(100)
  sprk_responsestatus      INT
  sprk_responsesummary     NVARCHAR(1000)
  sprk_performedby         NVARCHAR(100)
  sprk_performedon         DATETIME
  sprk_containertypeconfig LOOKUP → sprk_specontainertypeconfig
  sprk_environment         LOOKUP → sprk_speenvironment
  sprk_businessunit        LOOKUP → businessunit
```

---

## READ defects — `Api/SpeAdmin/AuditLogEndpoints.cs`

| # | Code | Real schema | Effect |
|---|---|---|---|
| R1 | `$select` includes **`sprk_targetresource`** (`:102`) | no such column — it is `sprk_targetresourceid` / `sprk_targetresourcename` | Dataverse 400 |
| R2 | filter on **`_sprk_containertypeconfigid_value`** (`:171`) | lookup is `sprk_containertypeconfig` → `_sprk_containertypeconfig_value` | Dataverse 400 |
| R3 | GUID **single-quoted**: `eq '{configId}'` (`:171`) | `_x_value` is `Edm.Guid`; a quoted literal is `Edm.String` | 400: *incompatible operand types `Edm.Guid` / `Edm.String`* |
| R4 | `sprk_category` filtered as a **string** | column is a **CHOICE** (integer option set) | 400, and the documented example value `"Configuration"` is not even a valid option |

Any one of R1–R3 alone produces `HttpRequestException` → the screen's
*"Failed to retrieve audit log entries from Dataverse."*

### R3 is the outlier the whole codebase disagrees with

Every lookup filter in `src/` was classified. **29 of 31 use a bare GUID literal.** The only two quoted are
both in `AuditLogEndpoints.cs` — the code at `:171` and the doc comment at `:159` that asserts the wrong rule:

> `// Dataverse OData: _xxx_value lookup fields require the GUID wrapped in single quotes`

That comment is false. Its own sibling in the same folder, `ConfigEndpoints.cs:119`, does it correctly:
`_sprk_businessunit_value eq {businessUnitId.Value}`.

---

## WRITE defects — `Services/SpeAdmin/SpeAuditService.cs`

This is why the table is empty. **Five independent defects, each fatal to the create:**

| # | Code writes | Real schema | Effect |
|---|---|---|---|
| W1 | *(nothing)* | **`sprk_name` is NOT NULL** | create rejected on a missing required field |
| W2 | `sprk_targetresource` | no such column | create rejected |
| W3 | `sprk_ContainerTypeConfigId@odata.bind` | nav property is `sprk_containertypeconfig` | create rejected |
| W4 | `sprk_EnvironmentId@odata.bind` | nav property is `sprk_environment` | create rejected |
| W5 | `sprk_category` as a **string** | CHOICE (integer). Callers pass `"Configuration"`, `"ContainerTypeRegistration"`, `"ContainerCreated"`, `"RecycleBin"`, `"FileUploaded"`, … — **none** are valid options except `"Permission"` | create rejected |

…and then:

```csharp
catch (Exception ex)
{
    // Audit failures must never propagate to the caller.
    _logger.LogError(ex, "Failed to write audit log entry. …");
}
```

The swallow is defensible as a policy (an audit failure should not fail the user's operation). What is not
defensible is that it is the **only** signal: nothing surfaces to an operator, so a fully non-functional
audit trail looked identical to a quiet one for the life of the app.

> **This is the same defect shape as tasks 002 and 003** — a lower layer collapsing a failure into an
> absent/empty result that an upper layer reads as success. Third instance in this project.

---

## Escalation triggers — evaluated, neither fires

| Trigger | Fires? | Why |
|---|---|---|
| "missing Dataverse privilege or disabled audit setting" | **No** | The failure is malformed queries against a real table, not authorization. No privilege grant or org setting is required. |
| "the audit data the screen wants does not exist in Dataverse at all … the screen was built against an imagined table" | **No — but close** | The **table** is real and well-shaped. The code was built against **imagined column names** on it. That is a variant of spec §3.2, and it is fixable in code, so removing the screen is not warranted. |

Because neither fires, this proceeds as classification (b) → *"fix the query here"* (step 4), extended to the
write path because `SpeAuditService.cs` is `role="modify"` in the task and the goal — *"the Audit Log screen
renders entries"* — is unreachable while every write is rejected.

---

## Not doing (out of scope, per the task's read-only constraint)

- No schema change. Every column this fix needs already exists; the code simply used wrong names.
- No privilege grant, no org audit setting.
- `sprk_responsesummary` (NVARCHAR 1000) exists and is unused. Populating it is a genuine improvement but
  new surface, not a defect fix — left alone.

---

## The fix (applied after the above was recorded)

### Read — `Api/SpeAdmin/AuditLogEndpoints.cs`

- `$select`: `sprk_targetresource` → `sprk_targetresourceid` + `sprk_targetresourcename`
- filter: `_sprk_containertypeconfigid_value` → `_sprk_containertypeconfig_value`
- filter: GUID unquoted, `{configId:D}` (bare-lowercase, ADR-044)
- filter: category mapped to the option-set integer instead of a quoted string
- response DTO: `Category` is now `int?`, plus a derived `categoryLabel` so the client still has something
  readable without a second round-trip for option metadata
- the false comment at `:159` is replaced with the actual rule and why

### Write — `Services/SpeAdmin/SpeAuditService.cs`

- writes the required `sprk_name` (`"{operation} — {target}"`, truncated to 850)
- `sprk_targetresource` → `sprk_targetresourceid`
- `sprk_ContainerTypeConfigId@odata.bind` → `sprk_containertypeconfig@odata.bind`
- `sprk_EnvironmentId@odata.bind` → `sprk_environment@odata.bind`
- `businessunitid@odata.bind` → `sprk_businessunit@odata.bind`
- `sprk_category` written as an option-set int via a new `MapCategory`, prefix-matched because the caller
  vocabulary is finer-grained than the option set; unmapped input falls back to `Security` rather than
  throwing, since losing the row is worse than a coarse category
- option-set values pinned as constants against the live schema

The swallow-on-failure is **kept** — an audit failure genuinely should not fail the user's operation. The
19 new tests are what make a regression visible instead of silent.

## Verification

| Gate | Result |
|---|---|
| Root cause captured before the fix | ✅ verbatim Dataverse error above |
| Corrected column set resolves against live Dataverse | ✅ query returns cleanly |
| `dotnet build` | ✅ 0 errors |
| Unit tests | ✅ **10,592 passed**, 0 failed (+19 new) |
| ArchTests | ✅ 36/36 |
| Publish size | ✅ 43.68 MB compressed (unchanged; ceiling 60) |
| ADR-044 GUID canonicalization | ✅ every key predicate / filter uses bare-lowercase `:D` |
| Empty-period distinguishable from failure | ✅ 200 + `Count: 0` vs 500 ProblemDetails |
| Dataverse writes from this task | ✅ none — read-only, no schema change, no privilege grant |

## ⚠️ Not verified

**End-to-end "the screen renders entries" is NOT verified**, for two reasons:

1. No deployed SPE Admin app and no live BFF to drive it (the standing UI-verification gap).
2. **The table has 0 rows.** Even a perfect read renders empty until the fixed write path has run in a
   real environment and produced entries.

What IS verified is stronger than a code trace: the exact column set the query now uses was executed
against the real Spaarke Dev org and resolved, and the old one produced the precise error the screen showed.

**Suggested confirmation once deployed**: perform any audited SPE Admin operation (e.g. create a container
type), then re-run `SELECT COUNT(sprk_speauditlogid) FROM sprk_speauditlog` — it should be non-zero for the
first time.
