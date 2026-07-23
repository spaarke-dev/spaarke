# Task 041 — Comms Policy Layer (FR-12): Implementation Notes

> **Status**: ✅ Completed 2026-07-22. Comms policy gate over the owner-created `sprk_communicationrule` table (Path B). FULL rigor (opus/xhigh). Rule-store decision + ADR-039 exception: `notes/041-rule-store-decision.md`. Full BFF suite green.

## What shipped

| Artifact | Change |
|---|---|
| `Configuration/CommsPolicyOptions.cs` (NEW) | `DefaultConfidenceThreshold` (default 0.8) — dedicated dial mirroring `EventRulesOptions.ClassifyConfidenceThreshold`'s SHAPE (not its section/state). Bound from `Communication:Policy`. |
| `Services/Communication/CommunicationRuleGate.cs` (NEW) | `CommunicationRuleDecisionRequest` (CommunicationId, Tenant, MatterId, Confidence) + `CommunicationRuleDecision` (Authorize, MatchedRuleId, Confidence, Threshold, PrivilegeFlagged, Reason) + the gate. Reads enabled `sprk_communicationrule` rows, matches tenant (blank=all) ∧ matter (empty=all), lowest `sprk_priority` wins; authorize ⇔ match ∧ confidence ≥ (rule `sprk_confidencethreshold` ?? default); privilege carried from `sprk_flagprivilege` (flagged, never decided — ADR-015); logs every decision; store-read failure → fail-closed DENY. Concrete class (ADR-010). |
| `Services/Communication/CommunicationRuleGate.cs` (`RuleGatedAssessedConsumer`) | The REAL consumer behind task 040's `ICommunicationAssessedProducer` seam: maps `CommunicationAssessedSignal` → gate request (re-reads `sprk_regardingmatter`, tenant null, confidence from the signal), runs the gate, logs authorize/deny. On authorize it EXECUTES NOTHING (that is task 042) — logs the authorization. No outbox, no `FireAsync`. |
| `Services/Communication/ICommunicationAssessedProducer.cs` | `CommunicationAssessedSignal` gains `double Confidence = 0` (additive; existing 5-arg constructions stay valid). |
| `Infrastructure/DI/CommunicationModule.cs` | `Configure<CommsPolicyOptions>` + `AddSingleton<CommunicationRuleGate>` + swap `ICommunicationAssessedProducer` registration from `LoggingCommunicationAssessedProducer` → `RuleGatedAssessedConsumer` (the interim log-only default from 040 is replaced; emit point unchanged). |
| `tests/integration/seam/Communication/CommunicationRuleGateSeamTests.cs` (NEW, 5 tests) | All four branches + default-threshold fallback + priority tiebreak: (a) match+≥threshold→authorize; (b) no-match→deny; (c) match+<threshold→deny (privilege carried); (d) privilege present on every decision; all-matter/no-threshold fallback; lowest-priority wins. Doubles only the Dataverse boundary. |

## Rule-store decision (§11 escalation → owner call)

The POML front-loaded a §11/ADR-039 judgment: extend Binding vs new table. Grep evidence (`notes/041-rule-store-decision.md`): Binding's `MatchConditionsJson` predicate CAN express tenant∧matter, but the r2-owned `ConsumerRoutingService.ResolveContextValue` only resolves `mimeType`/`documentType` — tenant/matter can't match through it without modifying that shared surface. **Escalated per the trigger; owner chose Path B (dedicated `sprk_communicationrule` table)** for the anticipated future comms-rule family. **ADR-039 exception accepted + documented** (Path A / §6.5): the table is a comms-RI *policy* store, not a second AI *dispatch-routing* surface.

## Confidence-source boundary (deviation, documented)

Task 040's `communication_assessed` signal carries no confidence, and the enrichment pipeline computes no RI-confidence score today. The gate evaluates whatever confidence it is given; the seam wiring passes `signal.Confidence`, which **defaults to 0** (→ DENY under any positive threshold). This is the **safe governance posture** — no ungoverned RI action fires until a real assessment confidence is plumbed into the signal (a downstream concern; the assessment/RI-scoring feature, adjacent to task 042). The gate's authorize branch is fully exercised by the seam tests (explicit confidences). No behavior regression — DENY-by-default means nothing acts, exactly as before this task.

## Acceptance — all 9 criteria met

1. ✅ Rule-store decision documented with grep evidence (file:line) before any table use — `notes/041-rule-store-decision.md`.
2. ✅ Escalation fired (Binding can't express tenant/matter via the shared resolver) → stopped + escalated to owner before building; owner chose Path B.
3. ✅ Match + confidence ≥ threshold → authorize (seam test a).
4. ✅ No matching rule → deny + logged no-action, no side effect (seam test b).
5. ✅ Match + confidence < threshold → deny + logged (seam test c).
6. ✅ Never calls `IEventRulesService.FireAsync` (structural — the gate + consumer have NO EventRules dependency) and no `SessionId`/`UserOid` state.
7. ✅ Every decision (authorize + deny) carries an explicit `PrivilegeFlagged` field, not auto-resolved (ADR-015).
8. ✅ Tests cover all four branches under `tests/integration/seam/Communication/` (KEEP path); placement rationale stated (table read boundary + branchy eval → seam test, per 023/024/040 precedent).
9. ✅ Publish **49.83 MB incl-PDB** ≤60 (on the ~49.63 baseline; no package added → CVE set unchanged, 0 new HIGH); Placement Justification stated.

## Verification
- `dotnet build`: 0 errors. Gate seam tests: 5/5. Full BFF suite: **8860 passed / 0 failed** (baseline 8855; +5) — behavior neutral, real DI container resolves the gate + swapped consumer.
- Step 9.5: code-review CLEAN (0 Critical); adr-check clean except the **documented ADR-039 exception** (Path A, owner-approved, cited in the rule-store note).

## For downstream
- **Task 042** (RI actions via seam) executes on `authorize` — it calls/observes this gate's decision and writes the `kind=communication-assessed` outbox row + `appnotification` mirror. It is also where a REAL assessment confidence should be plumbed into the signal so authorize can fire in production.
- Admins author rules as `sprk_communicationrule` rows (matter lookup / tenant / confidence threshold / privilege flag / enabled / priority).
