# OutcomeCard v1 — contract doc (task 011, FR-A0-02, design D-F2)

> **Status**: walking skeleton shipped. Contract + guard factory + thin render helper +
> self-contained contract test (10 tests green).
> **Home**: `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs`
> **Test**: `tests/integration/contract/Api/Ai/OutcomeCardContractTests.cs`
> **Unblocks**: Compose r2 **FR-05** (create-on-save card) + **FR-28** (push/save completion).

## What it formalizes

One disposition-level shape over two already-shipped R1 primitives:

| R1 primitive | Source | OutcomeCard field |
|---|---|---|
| Audience-split summary (user-facing vs model/internal) | `ToolResult` `UserSummary` vs `Summary` (ToolResult.cs:326) | `Summary` → `OutcomeSummary(UserFacing, Internal)` |
| Server-composed record/deep link (never model-invented) | `Api/Agent/HandoffUrlBuilder.cs` + entityrecord deep-link composer | `Link` → `OutcomeCardLink` (server-composed only) |

It adds the completion-wave slots: next-step chips, a trace reference (→ TraceEvent v1, task 013),
failure/partial `Status`, and a hosted completion state (single-shot OR job-aware).

**No shipped behavior changed** — this wraps `UserSummary`/`HandoffUrlBuilder`, it does not alter
them (task-011 escalation boundary respected).

## Shape (v1 wire — camelCase, `OutcomeCard.JsonOptions`)

```json
{
  "version": 1,
  "ledgerOutputKey": "binding-abc@t3",
  "status": "succeeded",                       // succeeded | partial | failed
  "summary": {
    "userFacing": "Created the matter \"Smith v. Jones\".",
    "internal": "sprk_matter row upserted; offer analysis."   // omitted when null; never shown to user
  },
  "link": { "url": "https://…/main.aspx?pagetype=entityrecord&…", "label": "Open the matter", "kind": "record" },
  "nextSteps": [
    { "label": "Analyze the document", "actionKind": "invoke_capability" },
    { "label": "Open the workspace", "actionKind": "navigate", "targetUrl": "https://…" }
  ],
  "completion": { "mode": "singleShot", "steps": [] },        // or mode "jobAware" + ordered steps
  "traceRef": "trace-7f3a"
}
```

Job-aware completion carries consumer-declared ordered steps (Compose declares
`container → record → profile-analysis → indexing`); each step is `{ key, label, status }`.
The step set aligns additively to `JobAwareCompletionState v1` (task 014).

## Versioning + tolerant reader

- `version` present on every payload; `SchemaVersion = 1`.
- **Additive-only** within v1: new optional fields may appear later; existing fields never change
  meaning/type.
- **Tolerant reader**: consumers ignore unknown fields. `OutcomeCard.JsonOptions` uses default
  System.Text.Json behavior (unknown members ignored — no throw), so a newer additive producer never
  breaks an older consumer. (Asserted by `Deserialize_PayloadWithUnknownFutureField_…`.)

## Two hard invariants (enforced by construction — `OutcomeCard.ForStoredOutcome`)

1. **Store-before-render (ADR-040)** — `ledgerOutputKey` (the stored `SessionOutput.Key`,
   `{bindingId}@t{n}`) MUST be non-empty. A card referencing no stored outcome throws
   `ArgumentException`. (Negative test: `ForStoredOutcome_WithNoStoredLedgerKey_Throws`.)
2. **Server-composed link** — `Link` must come from `OutcomeCardLink.ServerComposed(...)` (a
   HandoffUrlBuilder / deep-link output). A `ModelClaimed` link throws `InvalidOperationException`.
   Provenance (`IsServerComposed`) is a construction-time guard and is NOT serialized; a
   deserialized link is untrusted (false) by design. (Negative test:
   `ForStoredOutcome_WithModelClaimedLink_Throws`.)

## Audience split at render

`OutcomeCard.Render()` returns `OutcomeCardView`, which carries the **user-facing** summary, link
label/url, chip labels, and completion mode — and has **no internal field at all**. The internal
detail cannot leak into the user projection. (Asserted by `Render_…_NeverTheInternalDetail`.)

## ADR-013 placement / Placement Justification

- **Existing overlap?** None. `ToolResult`/`HandoffUrlBuilder` are the internal primitives; there is
  no disposition-level contract wrapping both. Confirmed by grep — no prior `OutcomeCard` type.
- **Extend instead?** The DTO is NEW surface (a cross-project contract) but deliberately extends the
  existing primitives rather than duplicating them — it holds their outputs, not reimplementations.
- **Cost of doing nothing** — without it, the Completion Engine (FR-A1-06/07) and Compose r2 each
  render side-effect outcomes as ad-hoc markdown with local link/summary variants; the audience
  split and server-composed-link guarantees are re-derived per consumer and drift.
- **Facade discipline (ADR-013)**: the DTO has **zero** dependency on AI-internal types — CRUD/Compose
  reach it only through `Services/Ai/PublicContracts/`. The reference producer that maps a real
  `SessionOutput` + `HandoffUrlBuilder` into the guard factory is server-side AI code OUTSIDE the
  facade (see "Production wiring" below).
- **ADR-039**: no second dispatch/render mechanism — OutcomeCard is a shape over existing side-effect
  results.

## Production wiring (NOT in this walking skeleton — for FR-A1-06/07, tasks 035/036)

The self-contained test plays the producer→consumer round-trip. Real wiring, when the Completion
Engine lands, is a thin server-side producer (outside PublicContracts):

```
var link = OutcomeCardLink.ServerComposed(handoffUrlBuilder.Build…(…), label, "record");
var card = OutcomeCard.ForStoredOutcome(storedOutput.Key, status, summary, link, nextSteps, completion, traceRef);
```

No DI registration is required for v1 (pure DTO + static factory).

## BFF hygiene

- **Publish-size delta**: negligible. One pure-DTO source file, **zero new packages** (uses
  framework `System.Text.Json` already referenced). Well under the +5 MB per-task escalation
  threshold; no full publish run (shared worktree, 6 concurrent contract agents building).
- **CVE**: no NEW HIGH/CRITICAL CVE introduced (adds no packages). The pre-existing transitive
  `Microsoft.Kiota.Abstractions` HIGH advisory is unrelated to this change.
```
