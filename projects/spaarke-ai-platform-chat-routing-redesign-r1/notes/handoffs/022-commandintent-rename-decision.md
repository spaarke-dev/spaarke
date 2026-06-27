# Task 022 — `commandIntent` Wire-Format Rename Decision

**Date**: 2026-06-22
**Task**: 022 (Phase 1: §1.7 Stable Codes Migration)
**Status**: Owner-confirmed; rename executed atomically (FE + BE single commit)

---

## Decision

The chat-request wire-format field formerly named **`commandIntent`** is renamed to **`intentHint`**.

- **Old name**: `commandIntent` (introduced R6 Pillar 8 / task 082 / FR-50)
- **New name**: `intentHint` (Spaarke chat routing redesign R1 / FR-07)
- **Owner confirmation timestamp**: 2026-06-22
- **Confirmed via**: Task POML 022 mission statement; owner-decision recorded by main session

---

## Rationale

`intentHint` is the cleanest semantic match to modern AI / routing UX idioms:

| Term | Used by |
|------|---------|
| **intent hint** | Slack slash-command intent, VSCode user intent, Copilot intent hint |
| `dispatchBias` (rejected) | Internal jargon — leaks implementation detail (routing layer) at the wire boundary |
| `routingHint` (rejected) | Closely tied to a specific subsystem name (`CapabilityRouter`); less stable if routing surface changes |

Considerations:

1. **Self-describing at call sites**: `body.intentHint = 'summarize'` reads as a soft signal to the LLM/agent about what the user is asking for.
2. **Future Phase 5 (task 115)**: the field will carry vector-query bias semantics — `intentHint` remains accurate when the BFF uses it to bias retrieval; `commandIntent` was tied to slash-command genesis.
3. **Idiomatic in industry**: Slack, VSCode, GitHub Copilot, and Azure AI SDKs all use "intent" as the user-facing routing signal vocabulary.
4. **No collision** with existing fields on the wire (audited via grep).

---

## Scope of Change

- **Atomic FE + BE rename in one commit** (no back-compat alias per spec Owner Clarification Q5 / FR-07).
- **No behavior change** — wire field renamed only; Phase 5 task 115 wires the new bias semantics.
- All sites including BE DTO (`ChatSendMessageRequest.CommandIntent` → `IntentHint`), JSON attribute, `CapabilityRouter` parameter chain, `SprkChatAgentFactory` overloads, `NullSprkChatAgentFactory`, `SoftSlashRouter.ts` (`decorateBody` returns `intentHint`), `DecoratedChatBody` interface, `toCommandIntent` function/type names (kept TypeScript-only for now since `CommandIntent` is the closed-vocabulary value type, not the wire field name).

### Important nuance: TypeScript `CommandIntent` type kept; wire field name renamed

The TypeScript `CommandIntent` **type alias** (closed vocabulary union `'summarize' | 'draft' | 'extract-entities' | 'analyze'`) is intentionally **NOT renamed in this task**. It names the *set of values* the field carries, not the field name itself. Renaming the type would expand the diff unnecessarily and produce no semantic improvement (the values are still command intents).

What changed:
- ✅ **Wire field** `commandIntent` → `intentHint` (JSON key, C# DTO property, TS interface field, all call sites)
- ⏸️ **TS `CommandIntent` type alias** stays as the value-shape vocabulary type (no rename)
- ⏸️ **`toCommandIntent` helper function name** stays (the helper produces a `CommandIntent` value; nothing on the wire)
- ⏸️ **`SoftSlashIntentToCapabilityName` BE dictionary name** stays (internal lookup table; values are the wire-vocabulary strings, not the field name)

This preserves an unambiguous distinction: the WIRE FIELD is `intentHint`; the VALUE TYPE it carries remains `CommandIntent` (which is accurate — these *are* command intents like `summarize`).

---

## Verification

Post-rename grep verification:
- `grep -rn '"commandIntent"' src/` → 0 hits
- `grep -rn ' commandIntent\b' src/` (lowercase identifier as a parameter / variable / JSON key) → 0 hits (TypeScript type `CommandIntent` retained per scope decision above)
- `grep -rn ' CommandIntent\b' src/` → 0 hits in C# (DTO + parameter chain renamed); only remaining hits are TypeScript `CommandIntent` *type alias* references which are intentional per the scope nuance above

Integration test `ChatIntentHintRoundTripTests.cs` asserts FE→BE wire round-trip uses `intentHint`.

---

## References

- **Spec**: FR-07, Owner Clarification Q5 (no back-compat needed)
- **Task POML**: `tasks/022-rename-commandintent-wire-format.poml`
- **Phase 5 follow-up**: task 115 wires the bias-routing behavior atop the renamed field
- **Prior introduction**: R6 Pillar 8 / task 082 / FR-50 (`SoftSlashRouter` + `CapabilityRouter` Layer 0.5 pre-pass)
