# Task 024 — `/by-name/` Deprecation Dashboard (KQL)

> **Generated**: 2026-06-22
> **Task**: 024 — Deprecation telemetry on `/api/ai/playbooks/by-name/{name}` (FR-03)
> **For**: Stabilization-window owner monitoring call-rate decay before deletion
> **Endpoint marker**: `deprecated.endpoint = "playbooks-by-name"`

---

## Purpose

After Wave 1-F (tasks 020 + 021) migrated all production consumers from `/by-name/` → `/by-id/`, the deprecated endpoint should see zero calls from production code. Tasks 024 adds Tier-1 safe deprecation telemetry so the stabilization-window owner can measure:

1. **Total call rate** — is it decaying toward zero?
2. **Per-playbook-name break-out** — which playbooks still have legacy callers?
3. **Per-tenant break-out** — which tenants need migration outreach?
4. **Per-User-Agent break-out** — which client surface (PCF / CLI / legacy script) is the source?

When the call rate has been zero for **N consecutive days** (window owner decides N — typically 14–30 days post-migration), the endpoint may be deleted in a follow-up task. Until then it stays mapped so legacy clients keep working.

---

## KQL — Application Insights `traces` (warning log entries)

The endpoint handler emits one `LogWarning` per call. App Insights surfaces it in the `traces` table with category `PlaybookEndpoints`, `severityLevel = 2` (warning).

### Daily call-rate decay

```kql
traces
| where customDimensions["CategoryName"] == "PlaybookEndpoints"
| where severityLevel == 2
| where message startswith "Deprecated endpoint /api/ai/playbooks/by-name/"
| summarize count() by bin(timestamp, 1d)
| order by timestamp desc
```

### Per-playbook-name break-out (last 14 days)

The structured log message includes `{PlaybookName}` so callers parse the same template field. App Insights surfaces it as `customDimensions["PlaybookName"]`.

```kql
traces
| where timestamp > ago(14d)
| where customDimensions["CategoryName"] == "PlaybookEndpoints"
| where severityLevel == 2
| where message startswith "Deprecated endpoint /api/ai/playbooks/by-name/"
| summarize calls = count() by tostring(customDimensions["PlaybookName"])
| order by calls desc
```

### Per-tenant break-out (last 14 days)

```kql
traces
| where timestamp > ago(14d)
| where customDimensions["CategoryName"] == "PlaybookEndpoints"
| where severityLevel == 2
| where message startswith "Deprecated endpoint /api/ai/playbooks/by-name/"
| summarize calls = count() by tostring(customDimensions["TenantId"])
| order by calls desc
```

### Per-User-Agent break-out (last 14 days) — identifies client surface

```kql
traces
| where timestamp > ago(14d)
| where customDimensions["CategoryName"] == "PlaybookEndpoints"
| where severityLevel == 2
| where message startswith "Deprecated endpoint /api/ai/playbooks/by-name/"
| summarize calls = count() by tostring(customDimensions["UserAgent"])
| order by calls desc
```

---

## KQL — Application Insights `requests` (Activity tags)

The endpoint handler sets two `Activity.Current` tags on the ASP.NET Core request activity. App Insights surfaces them via the `requests` table `customDimensions`. This query is the canonical call-rate metric (one request = one entry, regardless of whether the log entry was sampled).

### Daily call-rate decay (Activity-based)

```kql
requests
| where customDimensions["deprecated.endpoint"] == "playbooks-by-name"
| summarize count() by bin(timestamp, 1d)
| order by timestamp desc
```

### Per-playbook-name break-out (Activity-based)

```kql
requests
| where timestamp > ago(14d)
| where customDimensions["deprecated.endpoint"] == "playbooks-by-name"
| summarize calls = count() by tostring(customDimensions["deprecated.name"])
| order by calls desc
```

---

## ADR-015 tier-1 safety

All dimensions on the captured events are stable identifiers — NOT user content, NOT memory facts, NOT recall results, NOT extracted document text.

| Dimension | Source | Tier-1 safe? | Reason |
|---|---|---|---|
| `deprecated.endpoint` | Constant string `"playbooks-by-name"` | ✅ | Compile-time literal |
| `deprecated.name` / `PlaybookName` | URL path parameter | ✅ | Stable identifier; the playbook name is an admin-controlled string, not user-typed message content |
| `TenantId` | JWT `tid` claim | ✅ | Stable identifier |
| `UserAgent` | `Request.Headers.User-Agent` | ✅ | Client-surface identifier; rotates with deploys but not per-user |

No content, no token, no secret, no PII payload field. Manual audit + automated test
(`PlaybookByNameDeprecationTests.ByName_EmitsExactlyOneWarning_PerCall_WithTier1SafePayload`).

---

## Decision gate

The stabilization-window owner uses the **daily call-rate** query as the primary signal. Suggested gate:

> When `traces`-based daily count == 0 for ≥ 14 consecutive days AND no per-tenant break-out row has count > 0 in the 14-day window, file the deletion task. Until then, the endpoint stays mapped.

Per-playbook-name and per-UA break-outs are diagnostic: if calls persist, they identify WHICH client surface still needs migration outreach before deletion.

---

## Implementation reference

- Handler: `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs::GetPlaybookByName`
- Tests: `tests/integration/Sprk.Bff.Api.IntegrationTests/PlaybookByNameDeprecationTests.cs`
- FR: `spec.md` FR-03
- ADR: `.claude/adr/ADR-015-ai-data-governance.md` (tier-1 logging rules)
