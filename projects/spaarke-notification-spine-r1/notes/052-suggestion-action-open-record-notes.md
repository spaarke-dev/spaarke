# Task 052 — Suggestion action opens the regarding record in a modal (FR-17): Implementation Notes

> **Status**: ✅ Completed 2026-07-22. Phase 5 Wave 17 — the "what does acting on a suggestion DO" leg. FULL rigor (opus/high). **REFRAMED from the POML's "dispatch-parity proof" by an explicit owner decision** (see below). BFF build 0-err; publish 46.10 MB (≤60, zero delta); all touched suites green; both Step 9.5 gates CLEAN.

## The reframe (owner decision — §6.5 path-A, escalated + resolved 2026-07-22)

The POML specified task 052 as a **dispatch-parity proof**: prove that acting on a suggestion re-enters the shipped `dispatchConsumer`/`SurfaceLaunch` dispatch path. Investigation surfaced a **blocking gap the POML did not anticipate**: the `SuggestionEnvelope` (task 013) carries `actionHint` + `regardingRecordId` but **no `bindingId`**, the dispatch endpoint 400s on any non-GUID `bindingId`, and no `actionHint → bindingId` resolution exists — so the proactive card could not reach the dispatch path at all (the interim 051 wiring silently degraded to the ADR-019 failure line). Resolving it would have required either a client-side routing map (❌ ADR-039 violation), an envelope contract change carrying a pre-resolved binding, or a new server resolve surface — **and, more fundamentally, a daily-briefing "review this matter" nudge does not map to any capability binding.**

Escalated to the owner (root §6 / §6.5). **Owner decision: "if it's being shown it needs to be actionable — clicking should open the referenced record, as a MODAL (not navigate-away)."** This makes the action **navigation, not a capability dispatch**, which:
- Dissolves the `bindingId`/dispatch-parity problem entirely (there is no dispatch).
- Clears the POML escalation trigger ("no second dispatch pipeline"): opening a record uses the EXISTING `INavigationService` navigation surface, not a second capability-execution path.
- Supersedes the POML's dispatch-parity acceptance criteria. The new contract is the modal-open one below.

## What shipped

| Layer | Artifact | Change |
|---|---|---|
| **BFF (013)** | `Services/Notifications/Envelopes/SuggestionEnvelope.cs` | Added **required `RegardingRecordType`** (`regardingRecordType` wire) — pairs with `RegardingRecordId` so a consumer can OPEN the record. A record type is an identifier, not content/token (NFR-02/03 hold). Envelope is now 9 fields (was 8). |
| **BFF (050)** | `Services/Ai/Narrators/DailyBriefingSuggestionProducer.cs` | Sets `RegardingRecordType = item.EntityType` (the grounding gate already guarantees it non-empty). |
| **BFF tests** | `EnvelopeSerializationTests.cs` (reflection field-list 8→9 + camelCase wire field + factory), `DailyBriefingSuggestionProducerSeamTests.cs` (asserts `envelope.RegardingRecordType`), `PendingPollFallbackSeamTests.cs` (fixture). | The `/pending` endpoint carries the envelope through unchanged — no endpoint change needed; the new field flows to the client automatically. |
| **Shared lib** | `@spaarke/notifications` `types.ts` | `regardingRecordType: string` added to the `SuggestionEnvelope` client mirror. |
| **Shared lib** | `INavigationService` + `xrmNavigationServiceAdapter` + `bffNavigationServiceAdapter` + `mockNavigationService` | Added **optional `openRecordModal(entityName, entityId)`** — the modal-decision standard's **Layout 1** (`Xrm.Navigation.navigateTo({ pageType: "entityrecord" }, { target: 2, position: 1, 85% × 85% })`), distinct from `openRecord` (`openForm`, navigate-away). Optional so no existing implementer/test-double breaks. New `xrmNavigationServiceAdapter.test.ts` locks the Layout-1 shape (target:2, 85%, entityrecord; `openForm` NOT used). |
| **SpaarkeAi** | `useSuggestionCards.tsx` | `SuggestionEnvelopeLite` gains `regardingRecordType`; the `isSuggestionEnvelope` guard now REQUIRES both `regardingRecordId` + `regardingRecordType` — a suggestion that can't be opened does not render (owner rule: *shown ⇒ actionable*). |
| **SpaarkeAi** | `ConversationPane.tsx` | `onSuggestionAction` replaced (was interim `dispatchBinding`) with `previewNavigationService.openRecordModal(envelope.regardingRecordType, envelope.regardingRecordId)`. Hoisted the single `previewNavigationService` above the hook + removed the duplicate memo (one shared nav service, §11). |
| **SpaarkeAi tests** | `SuggestionCard.test.tsx` | Fixtures carry `regardingRecordType`; the click test asserts the fresh envelope handed to the host carries **both type + id** (the modal-open inputs) AFTER the re-fetch; new test: an envelope missing its record type does NOT render. |

## Design decisions

### Record type lives on the envelope (not a new /pending field)
`regardingRecordType` pairs with `regardingRecordId` — splitting the (type, id) open-target across the envelope and a separate pending-item field would be a latent footgun. The producer already has `item.EntityType` at produce time, and the `/pending` endpoint serializes the stored envelope verbatim, so the field reaches the client with no endpoint change. **No cross-project impact**: messaging-r3 mirrors `CommunicationEnvelope`, not `SuggestionEnvelope`.

### `openRecordModal` extends the canonical nav service (§11), optional to stay non-breaking
The shared `INavigationService.openRecord` uses `Xrm.Navigation.openForm` — which **navigates away** (replaces the page). The owner explicitly wants a modal. Rather than a SpaarkeAi-local one-off, the Layout-1 modal open is added to the canonical `INavigationService` (reusable by any Spaarke surface — Layout 1 is the standard for record row-clicks). Made **optional** so the interface change is additive-only: every existing implementer/inline test double keeps compiling; callers use `nav.openRecordModal?.(...)`.

### Re-fetch-before-act is retained
Task 051's re-fetch/re-ground-before-acting (confirm the row is still pending server-side) and pre-mount expiry filter are unchanged — they run before the modal opens, so a stale/revoked suggestion opens nothing and shows the stable local line.

## Acceptance — the modal-open contract (supersedes the POML dispatch-parity criteria)
1. ✅ Clicking a suggestion card opens the regarding record as a **modal** (`navigateTo` entityrecord, `target: 2`, 85% × 85%) — NOT navigate-away (adapter test asserts `openForm` is not used).
2. ✅ The modal-open inputs (`regardingRecordType` + `regardingRecordId`) reach the client: producer → envelope → `/pending` → hook → `onSuggestionAction` (BFF envelope round-trip test + hook call-order test).
3. ✅ Re-fetch/re-ground still runs BEFORE the open; a stale row opens nothing + shows the stable local line (hook tests, unchanged from 051).
4. ✅ A suggestion missing its record type does not render (owner rule: shown ⇒ actionable) — hook test.
5. ✅ No second dispatch pipeline: the action uses the existing `INavigationService` navigation surface (escalation trigger cleared by the owner reframe).
6. ✅ Existing suites unmodified in behavior: 354 SpaarkeAi conversation tests + 148 BFF notification/narrator tests + envelope suite all green.

## Verification
- **BFF**: `dotnet build` 0 errors. Envelope + producer + pending tests: 44 targeted + 148 notification/narrator green. Publish **46.10 MB compressed incl-PDB** (≤60; **zero delta** — no package added), 0 new HIGH CVE.
- **Shared lib**: `xrmNavigationServiceAdapter.test.ts` 2/2 (Layout-1 shape + host-unavailable throw).
- **SpaarkeAi**: `SuggestionCard.test.tsx` 8/8; **354 conversation tests all green** (ConversationPane rewiring behavior-neutral to siblings); typecheck surface-gate **0 surface-owned** (shared-lib count unchanged at 243).

## Step 9.5 gates — both CLEAN
- **code-review**: 0 Critical / 0 Warning / 2 informational — (i) `openRecordModal` optional on the interface (deliberate: additive, non-breaking); (ii) `previewNavigationService` hoisted + de-duplicated (removed a duplicate memo — net simplification). Failure path (stale → stable ADR-019 line) retained; no secrets; no raw error surfaced.
- **adr-check**: 0 violations. Modal standard Layout 1 (MODAL-DECISION-CRITERIA); ADR-039 (navigation, zero client routing/intent detection — the dispatch concern is gone); ADR-024 (regarding pair type+id); ADR-021 (card tokens-only, unchanged); ADR-010 (extended existing nav service, no new abstraction); ADR-038 (envelope domain + 050 seam + adapter tests); NFR-02/03 (record type is an identifier, not content/token). §10 BFF Hygiene: Placement Justification stated (envelope is the natural home; no new endpoint/package; 46.10 MB; 0 CVE; tests updated).

## For downstream / 090 wrap-up
- The POML's dispatch-parity framing was **superseded by the owner decision** — cite this note in the 090 wrap-up PR. `/test-diet` should treat the adapter test + hook tests + envelope round-trip as MAINTAIN-class (regression anchors for the modal-open contract).
- Future: `CommunicationEnvelope` would need the same `regardingRecordType` pairing if "open the communication's regarding record" is ever wired. A future suggestion source that genuinely dispatches a capability (not just opens a record) would revisit the `actionHint → binding` resolution deferred here — but that needs a real target binding to exist first.
