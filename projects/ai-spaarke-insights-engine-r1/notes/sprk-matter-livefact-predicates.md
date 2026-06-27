# sprk_matter LiveFact predicate mapping (task 071 Step 1)

> **Generated**: 2026-05-29 from `mcp__dataverse__describe_table("sprk_matter")` against Spaarke Dev
> **Purpose**: Document the field mapping the new `DataverseLiveFactResolver` uses for the 4 predicates the D-P14 `predict-matter-cost` synthesis playbook (task 060) reads via `LiveFactNode`.

---

## Background

The predict-matter-cost playbook (`src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json`) starts with a single `LiveFactNode` (`resolveLiveFacts`) configured with:

```json
{
  "subject": "matter:{{matterId}}",
  "predicate": "currentMatterFacts"
}
```

The original task 060 design called for the resolver to return **a compound fact** containing all 4 sub-predicates (`attorney`, `client`, `matterType`, `opposingCounsel`) in a single envelope so the downstream synthesis prompt could template all 4 from one upstream variable.

For task 071, we close the StubLiveFactResolver gap by supporting **both shapes**:

1. **Per-predicate** (`attorney`, `client`, `matterType`, `opposingCounsel`) — each returns a single-value `FactArtifact`. Most flexible; future playbooks can compose any subset.
2. **Composite** (`currentMatterFacts`) — returns a single `FactArtifact` whose `Value.Raw` is a JSON object containing all 4 sub-values. Matches the existing playbook config exactly (no playbook re-deploy needed for task 080).

Both shapes read the same sprk_matter row, so the read cost is identical. The per-predicate shape is the canonical Phase 1.5+ direction (one predicate per LiveFactNode invocation, allowing template variables to address individual sub-claims).

---

## sprk_matter schema (relevant fields)

From `DESCRIBE TABLE sprk_matter`:

| Field | Type | Target | Domain meaning |
|---|---|---|---|
| `sprk_matterid` | GUID | (primary) | Matter row id |
| `sprk_mattername` | NVARCHAR(1000) | — | Display name |
| `sprk_matternumber` | NVARCHAR(100) | — | Business identifier (e.g., "M-2024-0341") |
| `sprk_assignedattorney1` | LOOKUP | contact | Primary attorney |
| `sprk_assignedattorney2` | LOOKUP | contact | Co-counsel (Phase 1: unused) |
| `sprk_assignedparalegal1` | LOOKUP | contact | Primary paralegal (Phase 1: unused) |
| `sprk_assignedlawfirm1` | LOOKUP | sprk_organization | First-assigned law firm (= "our firm" per matter intake convention) |
| `sprk_assignedlawfirm2` | LOOKUP | sprk_organization | Second-assigned law firm — **mapped here to opposingCounsel** for Phase 1 (see Open Question below) |
| `sprk_externalaccount` | LOOKUP | account | External client account |
| `sprk_mattertype` | LOOKUP | sprk_mattertype_ref | Matter type (e.g., "IP licensing", "M&A", "Employment") |
| `sprk_practicearea` | LOOKUP | sprk_practicearea_ref | Practice area (broader than matter type) |
| `sprk_matterdescription` | MULTILINE TEXT | — | Free-form description |
| `sprk_totalspendtodate` | MONEY | — | Cumulative spend |
| `sprk_totalbudget` | MONEY | — | Authorized budget |

---

## Phase 1 predicate-to-field mapping

| Playbook predicate | sprk_matter field | Return type | Notes |
|---|---|---|---|
| `attorney` | `sprk_assignedattorney1` (LOOKUP → contact) | object `{ id, name }` | Phase 1 returns the contact reference; Phase 1.5+ may expand to {id, name, email}. |
| `client` | `sprk_externalaccount` (LOOKUP → account) | object `{ id, name }` | The external client; `sprk_externalaccount.name` is the account display name. |
| `matterType` | `sprk_mattertype` (LOOKUP → sprk_mattertype_ref) | string | The reference table's primary name (e.g., "IP licensing"). |
| `opposingCounsel` | `sprk_assignedlawfirm2` (LOOKUP → sprk_organization) | object `{ id, name }` | See Open Question. |
| `currentMatterFacts` (composite) | (all four above) | object `{ attorney, client, matterType, opposingCounsel }` | Composite shape for the existing playbook config. Resolves all 4 sub-values from a single sprk_matter read. |

All `FactArtifact` returns carry:
- `Confidence = 1.0` (deterministic system-of-record per `design.md §2.1`)
- `ProducedBy = { Kind: "query", Id: "dataverse://sprk_matter", Version: "v1" }`
- `Evidence = [{ refType: "fact-source", ref: "dataverse://sprk_matter/{id}#{predicate}" }]` (per `SPEC §3.4.1`)
- `Subject = "matter:{matterId}"` (echoed back per LiveFactNode contract)

---

## Open question (raised to project owner)

**Q**: `sprk_assignedlawfirm2` is mapped to `opposingCounsel` based on the matter intake convention that LawFirm1 = our firm and LawFirm2 = the additional/opposing firm. There is no explicit "opposing" flag on sprk_matter; the documentation just calls them "Assigned Law Firm 1" and "Assigned Law Firm 2".

**Phase 1 risk**: if production data uses LawFirm2 for "co-counsel on our side" rather than "opposing counsel", the predict-matter-cost synthesis will surface the wrong firm as opposing counsel in the prompt, biasing the prediction.

**Phase 1 mitigation**:
1. `DataverseLiveFactResolver` returns `null` (not exception) when `sprk_assignedlawfirm2` is unset on the matter row. `LiveFactNode` surfaces this as "Subject not found" (graceful degradation; the synthesis prompt handles missing opposingCounsel).
2. The composite `currentMatterFacts` shape always returns the matter row's actual `sprk_assignedlawfirm2` value (whatever it means in production). The synthesis prompt template treats it as opposingCounsel; if production data violates the convention, the prediction degrades but doesn't crash.

**Phase 1.5 path forward**: add an explicit `sprk_opposingcounsel` lookup to sprk_matter, or add a `sprk_lawfirmrole` flag on sprk_workassignment with values { our-firm, opposing-firm, co-counsel }. Until then, the resolver follows the existing convention.

**Owner sign-off**: needed for task 080 deploy verification. If owner says "LawFirm2 is co-counsel, not opposing", we add an interim alias to return `null` for opposingCounsel until Phase 1.5 schema change lands.

---

## Unsupported predicates (Phase 1)

Any predicate not in the table above throws `LiveFactNotSupportedException(subject, predicate)` from `DataverseLiveFactResolver`. The `LiveFactNode` consumer catches this and emits a node-level `NodeErrorCodes.InvalidConfiguration` error so playbook authors see misconfigured predicates immediately. Future predicates (e.g., `totalSpend`, `matterDurationDays`, `budgetUtilization`) are reserved for Phase 1.5+ when the synthesis playbook portfolio grows beyond `predict-matter-cost`.

---

## References

- Schema: `mcp__dataverse__describe_table("sprk_matter")` against Spaarke Dev, 2026-05-29
- Playbook config: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json` (task 060, commit `9bf57e16`)
- LiveFactNode contract: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LiveFactNode.cs` (task 022)
- Zone B Dataverse-read template: `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/DataversePrecedentBoard.cs` (task 012, commit `fe175ca2`)
- SPEC §3.4.1 — Fact artifact wire shape
- design.md §2.1 — Confidence = 1.0 for Facts
