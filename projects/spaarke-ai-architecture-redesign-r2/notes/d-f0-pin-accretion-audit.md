# D-F0 Pin-Accretion Audit — G-P3 rounds 1–6 vs the Resourcefulness Doctrine

> **Task**: 030 (FR-A1-01, design §7.1 D-F0(a)). **Date**: 2026-07-08. **Owner**: redesign-r2 core.
> **Obligation**: design §7.1 D-F0(a) — "The G-P3 rounds 1–6 pin accretion is audited against
> this block: pins that are instances of the strategy fold in; genuinely scenario-specific
> contracts (per-table write contracts, tool-description guidance) stay catalog data."
> **Acceptance criterion 4** (POML): each pin dispositioned (folded-in vs kept-as-catalog-data).

## Where the pins live

All G-P3 rounds-1–6 directive pins are inside `SprkChatAgentFactory.SideEffectHonestyDirective`
(`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs`, the const originally at
:65). They are one deterministic constant string composed onto the system prompt whenever tools
project (`if (finalTools.Count > 0)`). The related grounded-outcomes directive (`FR-P2-04`,
originally :718–746) and the current-date directive (`R5-A`, `BuildCurrentDateDirective`) are
adjacent but are NOT G-P3 honesty pins in the accretion sense.

## Audit finding (headline)

**Every G-P3 honesty pin is KEPT; none fold.** The pins are honesty-floor / write-determinism /
confirmation-contract instructions that operate **at or above the side-effect line**. They are
*not* read-passivity instances — none of them tells the model to ask permission, hedge, or skip a
read. The passivity D-F0 fixes is **emergent** (the accreted caution + the refusal/grounded-outcome
directives together), not encoded in any single foldable pin. The doctrine therefore ADDS what no
existing pin covers — read-freedom, the degradation ladder, and the affordance rule — as a NEW
block that extends the layer. This is the safe outcome required by Risk row 2: folding an honesty
pin would risk reopening fabrication or weakening a block.

Several pins additionally **back a hard block or a confirmation gate** (R5-E clarify-first backs the
`sprk_document` hard block; R4-3 no-URL backs the fabrication floor; R3-1 once-only + R3-2 lookup
resolution back the confirmation contract Policy v2 owns). Per the task's binding safety rule
("if folding a pin risks changing a gate/block behavior, DON'T fold it"), these are kept regardless.

## Per-pin disposition

| # | Pin (round) | What it does | Line vs side-effect | Disposition | Rationale |
|---|---|---|---|---|---|
| P1 | H6 — never claim a create/save/send/draft without a confirming TOOL RESULT | Honesty floor (`no_fabrication`) | ABOVE | **KEEP** | The 100% honesty floor. The doctrine explicitly reinforces it; folding = reopening "never lie". |
| P2 | When a matching tool exists, INVOKE it — don't describe/simulate/role-play | Act-not-simulate (honesty) | ABOVE | **KEEP** | Reads as an "act" instance but is really an anti-roleplay honesty pin. The doctrine's step 4 ("then ACT") reinforces it without replacing the anti-simulation contract. |
| P3 | User "yes" doesn't create anything — still must invoke the tool | Confirmation→invoke bridge (R3-1) | AT LINE | **KEEP** | Confirmation contract owned by Policy v2 (032). Doctrine must not touch it. |
| P4 | Ask for confirmation in chat AT MOST ONCE; confirmed→invoke immediately | Once-only ceiling (R3-1) | AT LINE | **KEEP** | Same confirmation contract; backs the R3-1 confirm-loop fix. |
| P5 | Resolve lookup references to GUID FIRST, before proposing a write | Write-construction correctness (R3-2) | ABOVE (shapes a write) | **KEEP** | A per-write verify step, but scoped to *write construction*, not read willingness. Design §7.1 explicitly keeps "per-table write contracts" as catalog data — this is that class. |
| P6 | If a tool was SUSPENDED, say exactly that — not "done" | Honesty about gate state | AT LINE | **KEEP** | Honesty floor about the gate. Folding would blur the gate's own status reporting. |
| P7 | `capability_*` tools only GENERATE drafts — success ≠ record created | Generation/execution split (R2-B) | ABOVE | **KEEP** | Scenario-specific contract about the `capability_` prefix vocabulary. Not a passivity pin. |
| P8 | Never claim a tab/view/editor/dialog was opened without a confirming result | UI-action honesty (R2-D) | ABOVE | **KEEP** | Honesty floor; also D-F3 (task 038) territory. Fabrication guard. |
| P9 | Never compose/guess/reconstruct URLs or deep links; relay only verbatim links | No-fabrication of links (R4-3) | ABOVE | **KEEP** | Directly backs the fabrication floor. The doctrine's affordance rule is written to be CONSISTENT with this (relay platform links; never compose). Folding/loosening would reopen R4-3. |
| P10 | Entity-ambiguous "create a record" → clarify the table, don't guess | Clarify-don't-guess (R5-E) | ABOVE (backs a hard block) | **KEEP** | Backs the R5-E `sprk_document` HARD BLOCK. Closest to a "verify/clarify-before-act" strategy instance, but the binding safety rule forbids folding a pin that backs a block. Kept verbatim. |
| P11 | If no tool can perform the action, say so honestly | Honest-refusal muscle | ABOVE | **KEEP + EXTEND** | Kept intact. The doctrine's ladder sits ABOVE this line: exhaust full → partial → structured assistance FIRST, so refusal is LAST and carries an affordance. The refusal muscle is unchanged; the doctrine adds the rungs before it. |

## Genuinely scenario-specific contracts that stay catalog DATA (not in the prompt block)

Per design §7.1, these are correctly NOT part of the strategy block and stay as catalog/handler data:

- **Per-table write contracts** (assignee/regarding mapping, `_value` lookup shapes — R5-B/R5-C) —
  live in Action input-schemas + handler validation + the Business slice (task 003), not the doctrine.
- **Tool-description guidance** (live-schema entity maps — G-P2) — lives in `sprk_analysistool` /
  Binding `sprk_tooldescription` catalog rows (Model-1 GitOps source), audited by `jps-playbook-audit`.
- **The concrete Document Upload deep-link affordance** (D-F0(d) first case) — is authored into the
  R5-E block **message** by task 040 (handler/catalog data), and RELAYED by the model per the
  doctrine's affordance rule. The doctrine says "relay the platform's link"; the link itself is data,
  never model-composed (keeps P9 intact).

## Net effect

- **Pins folded into the doctrine: 0.**
- **Pins kept (honesty floor / write contract / confirmation / block-backing): 11.**
- **New coverage added by the doctrine: read-freedom (b), the degradation ladder (c), the affordance
  rule (d), and the decompose→inventory→verify→act→partial-value strategy (a)** — none of which any
  existing pin provided.
- **Gates/blocks preserved:** R5-E hard block, the SideEffectGate confirmation gate, and the
  honest-refusal outcome are all untouched; the doctrine operates strictly below the side-effect line.
