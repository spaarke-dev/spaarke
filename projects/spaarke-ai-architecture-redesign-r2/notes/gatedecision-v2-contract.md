# GateDecision v2 — contract note (task 012 · FR-A0-04 · design D-F1)

> **Status**: Phase-A0 walking skeleton published. Contract + reference producer + reference
> consumer + contract test landed. The full deterministic Policy v2 engine (FR-A1-03 / task 032)
> replaces the producer BODY but MUST emit this same shape.
>
> **Code**: `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/GateDecisionV2.cs`
> **Test**: `tests/integration/contract/Api/Ai/GateDecisionV2ContractTests.cs` (34 cases, KEEP-path)

---

## What it is

`GateDecision v2` is the gate **OUTCOME** shape — the deterministic result of the ONE existing
Confirmation Gate (`SideEffectGateAIFunction` + `PendingPlanManager`), **not a second gate**. It is
what the Policy v2 engine PRODUCES and clients + Compose r2 CONSUME (FR-05, FR-28).

## Carried fields

| Field | Type | Meaning |
|---|---|---|
| `schemaVersion` | int (=2) | tolerant-reader version; readers accept `<=` own + ignore unknown fields |
| `tier` | `GateRiskTier` | `Tier0/1/2a/2b/2c/3/4` — catalog-declared DATA (policy-v2 note §1) |
| `origin` | `GateRequestOrigin` | `Explicit` / `Inferred` (fail-closed default `Inferred`) |
| `completeness` | `GateArgCompleteness` | `Complete` / `Incomplete` |
| `overlays` | `GateOverlayResult` | 3 flags + `decisive` (first overlay to fire) |
| `confirmationState` | `GateConfirmationState` | projection of the ADR-040 Gate-ledger status |
| `outcome` | `GateOutcome` | `Execute / ExecuteWithUndo / ConfirmDialog / Elicit / HonestBlock` |
| `tierProvenance` | `GateRiskProvenance` | MUST be `CatalogDeclared` (ADR-039); `RuntimeModelJudged` is rejected |
| `gateId` | string? | correlates to the stored ledger entry (identifier only, NFR-07) |
| `association` | `GateAssociationAffordance?` | OPTIONAL associate-to picker (matter/project/invoice/work-assignment/none) |

## Overlay precedence (encoded in `GateOverlayResult.Resolve` + `GateDecisionProjector`)

Strict order — first that fires decides, BEFORE origin/tier (policy-v2 note §2):

1. **InjectionSuspect** (dispatchUncertain / content-safety / untrusted-doc) ⇒ dialog + suspicion
2. **SafetyPerimeterDegraded** (PromptShield fail-open) ⇒ gated writes (Tier ≥ 2) confirm; reads fail-open
3. **IncompleteArgs** ⇒ ONE elicitation turn, then re-evaluate

Then origin+tier: Tier 0/1 execute; Tier 2a/2b explicit ⇒ execute+Undo, else ONE dialog; Tier 2c
preview/confirm; Tier 3/4 always dialog.

## Two structural invariants

- **ADR-039 (risk is DATA)**: `GateDecisionProjector.Project` only accepts catalog enums — a runtime
  model risk-judgment is not expressible. `GateDecisionV2.Validate()` throws on `RuntimeModelJudged`;
  `GateDecisionConsumer` fail-closes a non-catalog-grounded decision to `HonestBlock`.
- **ADR-040 (no second ask)**: `confirmationState` is a projection of the stored ledger Gate status.
  `GateDecisionConsumer.RequiresUserPrompt` returns **false** for a `Confirmed` decision **regardless of
  `outcome`** — a second ask is structurally impossible (kills the R3-1 confirm loop). Ledger statuses
  `confirmed` / `confirmed-unexecutable` / `dispatch-failed` all map to `Confirmed`.

## Example payload (explicit + complete Tier 2b, associate-to picker offered)

```json
{
  "schemaVersion": 2,
  "tier": "Tier2b",
  "origin": "Explicit",
  "completeness": "Complete",
  "overlays": { "injectionSuspect": false, "safetyPerimeterDegraded": false, "incompleteArgs": false, "decisive": null },
  "confirmationState": "None",
  "outcome": "ExecuteWithUndo",
  "tierProvenance": "CatalogDeclared",
  "gateId": null,
  "association": {
    "allowedTargets": ["None", "Matter", "Project", "Invoice", "WorkAssignment"],
    "selected": "Matter",
    "selectedRecordId": "matter-0001",
    "required": false
  }
}
```

## Example payload (injection-suspect overlay forces the dialog)

```json
{
  "schemaVersion": 2, "tier": "Tier2b", "origin": "Explicit", "completeness": "Complete",
  "overlays": { "injectionSuspect": true, "safetyPerimeterDegraded": false, "incompleteArgs": false, "decisive": "InjectionSuspect" },
  "confirmationState": "Pending", "outcome": "ConfirmDialog", "tierProvenance": "CatalogDeclared",
  "gateId": "confirmation-abc123", "association": null
}
```

## Downstream binding

- **FR-A1-03 / task 032** — Policy v2 engine replaces `GateDecisionProjector`'s body; emits this shape.
- **FR-A1-04 / task 033** — origin-classification eval family asserts against `origin`.
- **Compose r2 FR-05 / FR-28** — consumes via `Services/Ai/PublicContracts/` (ADR-013), zero local variant;
  renders `association` inside the existing `ActionConfirmationDialog` (no bespoke banner).

## Production wiring still TODO (out of this A0 skeleton's scope)

- Task 032 wires `GateDecisionProjector` (or its engine successor) into `SideEffectGateAIFunction` /
  `PendingPlanManager` so a real suspension emits a `GateDecisionV2` alongside the `action_confirmation`
  SSE event. This A0 task deliberately did NOT edit those shared files.
- The `association` picker's Dataverse record-resolution + write-back lands with Compose r2 Tier-2c.
