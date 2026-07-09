# ComposeDisposition v1 — contract doc (seam 010)

> **Status**: published (walking skeleton — contract + reference producer/consumer + contract test).
> **Owner**: `spaarke-ai-architecture-redesign-r2` (core). **Consumer**: `spaarkeai-compose-r2`.
> **Unblocks**: Compose FR-04 (draft-into-editor), FR-16 (pending redline), FR-17 (undo/replace).
> **Spec**: FR-A0-06 / FR-A0-08. **ADRs**: 040 (disposition = only rendering contract), 039 (no second dispatch), 013 (consume via PublicContracts).

## What the core owns (envelope only)

1. **`compose` disposition member** on the ADR-040 rendering contract — a string vocabulary member
   on `SessionOutput.Disposition` (`Models/Ai/Chat/SessionLedgerEntries.cs`). NOT a second
   rendering path: it rides the existing SSE/ledger surface.
2. **SSE frame** — `ComposeDispositionFrame` (`Services/Ai/PublicContracts/ComposeDisposition.cs`).
   Versioned, tolerant-reader, carries `ledger_ref` + `disposition` + provenance + supersession
   keying. **Never the payload** (storage-precedes-rendering).
3. **Supersession** — undo/replace is a NEW `compose` `SessionOutput` superseding the prior one,
   addressable by `{bindingId}@t{n}` (`SessionLedger.BuildOutputKey`). Consumer re-materializes
   from CURRENT ledger state (highest turn) — there is NO client-only DOM undo.

## What Compose owns (opaque payload)

The structured-edit schema (`target_text` / `new_text` / `match_mode` / `rationale` / `sources`)
lives inside the opaque `SessionOutput.Payload`. The core never parses it; the frame carries a
ledger reference, not the edit body. Zero editor semantics are baked into the platform contract.

## Frame shape (v1 wire)

| Member | Wire name | Type | Notes |
|---|---|---|---|
| Version | `version` | string | always `"1.0"`; tolerant-reader gate; v1 additive-only |
| Disposition | `disposition` | string | always `"compose"` |
| LedgerRef | `ledger_ref` | string | `{bindingId}@t{n}` — load-bearing; client re-materializes from it |
| BindingId | `binding_id` | string | provenance |
| Turn | `turn` | int | 1-based session turn |
| SupersedesRef | `supersedes_ref` | string? | prior `{bindingId}@t{n}` on undo/replace; null on first |
| Status | `status` | string | `ready` \| `partial` \| `failed` (failure/partial states) |
| CreatedAt | `created_at` | ISO-8601 | ledger write time |
| (unknown) | — | — | captured into `UnknownMembers`, ignored by v1 reader |

## Versioning + tolerant-reader rules

- `version` present on every frame; v1 is additive-only.
- Unknown members are ignored (surfaced via `[JsonExtensionData] UnknownMembers` for forward-compat).
- Consistent with sibling A0 contracts (OutcomeCard v1, JobAwareCompletionState v1).

## Example payload (wire)

```json
{
  "version": "1.0",
  "disposition": "compose",
  "ledger_ref": "4b2c30d1-0010-f111-ab0e-70a8a590c51c@t2",
  "binding_id": "4b2c30d1-0010-f111-ab0e-70a8a590c51c",
  "turn": 2,
  "supersedes_ref": "4b2c30d1-0010-f111-ab0e-70a8a590c51c@t1",
  "status": "ready",
  "created_at": "2026-07-08T12:00:00Z"
}
```
The compose edit body is NOT here — it is in the referenced `SessionOutput.Payload` (Compose-owned).

## Failure / partial states

- `partial` — streaming/incomplete compose output; Compose may render progressively.
- `failed` — the compose leg failed AFTER a ledger marker was stored (store-before-render preserved);
  failure detail rides the opaque payload.
- A frame can only be produced from an already-stored compose entry (`BuildFrame` throws otherwise);
  a frame whose `ledger_ref` was never stored throws on `Materialize` (store-before-render enforced).

## Reference producer / consumer

- Producer: `ComposeDisposition.BuildFrame(storedEntry, supersedesRef?, status?)` — render-follows-store.
- Consumer: `ComposeDisposition.Materialize(frame, ledgerLookup)` — throws when the referenced entry
  is absent; `ComposeDisposition.ResolveCurrent(ledger, bindingId)` — current = highest-turn compose entry.
- Both pure over an in-memory ledger; Compose can adopt directly. Locked by
  `tests/integration/contract/Api/Ai/ComposeDispositionContractTests.cs`.

## Production wiring NOT in this walking skeleton (described, not applied — parallel-agent safety)

Promoting `compose` to a maker-selectable routing disposition additionally needs (all in shared
routing files owned by other agents this wave):

1. `BindingDisposition.Compose` member in `Services/Ai/PublicContracts/Binding.cs` (option-set value).
2. `BindingDispositionLedgerExtensions.ToLedgerValue` case → `"compose"` in `Services/Ai/OutputRouter.cs`.
3. An `OutputRouter.RouteAsync` case for `BindingDisposition.Compose` (store-then-emit the frame).

The string member + frame published here are the stable seam; Compose builds on those now, and the
routing promotion lands as a follow-on once concurrent contract agents have merged.
