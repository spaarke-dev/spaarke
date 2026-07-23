# Task 043 — Privilege / Privacy Accuracy (FR-21 / NFR-01)

**Rigor:** FULL · **Effort:** xhigh · **Status:** complete
**Owner surfaces touched:** BFF read DTO + service, shared-lib conversation UI, tests.

---

## What shipped

Surfaced the three FR-21 markers on the single-thread read (and, via the shared read pipeline, on every read
surface) and confirmed the recipient display equals the actual permitted recipients — never over-disclosing.

### BFF
- `CommunicationThreadReadModels.cs` — `ThreadMessageDto` gains `bool IsInternalOnly` + `bool IsPrivate`
  (`Privilege` already existed). Positional record; only construction site is `BuildDto`.
- `CommunicationThreadReadService.cs`
  - New const `IsPrivateField = "sprk_isprivate"`; added to BOTH `$select` lists (single-thread read +
    `QueryVisibleMessagesAsync`). `sprk_isinternalonly` was already selected (used by the filter).
  - `ParseMessageRow` parses `sprk_isprivate`; `ParsedMessage` carries `IsInternalOnly` + `IsPrivate`
    (default `false` when the column is unset on a **visible** row — a display default, not an access decision).
  - `BuildDto` projects both markers.
- **No endpoint code change** — `GET /threads/{id}/messages` already returns `ThreadReadResult`; the enriched
  DTO flows through unchanged.

### Client (`@spaarke/ui-components`)
- `communicationTimelineApi.ts` `IThreadMessageDto` — `isInternalOnly` + `isPrivate` (mirrors BFF).
- `CommunicationTimeline.types.ts` — `TimelineMessage.isInternalOnly?` / `.isPrivate?` + `PRIVILEGE_*` constants.
- `CommunicationTimeline.buildTimeline.ts` — mapping copies both (`?? false`), no client-side inference.
- **New** `CommunicationTimeline/subcomponents/PrivacyMarkers.tsx` — shared, accessible Fluent v9 badge cluster
  (semantic `color` tokens, dark-mode via host `FluentProvider`, `role="group"` label, renders `null` when
  unmarked). Consumed by `MessageBubble`, `EmailInFlowBlock` (the R3 ConversationView surface) AND `MessageRow`
  (timeline) — one marker component, not three (§11).
- Recipient display (`To`) already rendered by `EmailInFlowBlock` (task 021); left as-is (it is the permitted set).

---

## How over-disclosure is prevented (NFR-01 — DTO→UI trace)

The markers + recipients are projected in `BuildDto`, which only ever runs over
`filtered.Decisions.Where(IsVisible)` where `filtered` = `CommunicationAccessFilter` applied to the **impersonated**
query result. Two composed gates sit UPSTREAM of any projection:

1. **Impersonation (record-level, the R1 primary gate).** A row the caller lacks access to is never returned by
   `IImpersonatedCommunicationQuery` (`MSCRMCallerID`). Since recipients + markers ride the same row, an absent row
   contributes none of them.
2. **Internal-only business rule.** `CommunicationAccessFilter` drops an `sprk_isinternalonly` row for a
   non-internal caller before it reaches `BuildDto`.

Therefore a marker/recipient can only be projected for a row the caller is already permitted to read.

- **BCC never leaks by construction:** `sprk_bcc` is not in any `$select` — asserted by unit + seam tests.
- **Recipient set is server-access-bound, not client-inferred:** `To` is the stored `sprk_to` on the permitted
  row — no membership-union, no per-recipient access probe. The escalation trigger ("permitted-recipient set can't
  be computed accurately") does **not** fire: we never attempt an unreliable per-recipient computation; the To
  header on an access-filtered row is accurate by construction (not a best-guess).
- **`isPrivate` never gates:** it is display metadata only. Private-thread visibility is enforced by impersonation
  (base ownership/scoping per `messaging-communication-app-r1/notes/access-model-decision.md`). It was deliberately
  NOT added to `CommunicationAccessFilter`.
- **Blocked #675 respected:** the three `IsInternalUser: true` composition sites were NOT touched.

---

## Step 9.5 gate — adversarial review + adr-check

- **adr-check:** clean. ADR-021 (Fluent v9 semantic tokens + host dark mode), ADR-028 (no `@spaarke/auth` import,
  no raw Bearer), ADR-015 (privilege never gates; no AI at read), ADR-012 (context-agnostic), ADR-038 (seam test on
  a KEEP path, boundary mocks only). No violations.
- **Adversarial over-disclosure trace:** no path found where a caller can see a recipient/marker/privilege they must
  not (see trace above). BCC exclusion + server-computed recipient set verified by tests.
- Critical/Major findings: none.

---

## Placement Justification (CLAUDE.md §10 · bff-extensions.md)

- **Belongs in BFF?** Yes — this is an additive projection on an existing BFF read (`CommunicationThreadReadService`)
  that already owns impersonation + the shared access filter. It is latency-bound to the ~5s poll and reads
  BFF-managed access state in the same request lifecycle → BFF is correct (decision-criteria rows 1–2 = BFF).
- **New endpoint/service/DI/package/background work?** None. No new surface — extends an existing DTO + service.
- **CRUD→AI dependency?** None added.
- **Config-home (§G):** no new config field; `sprk_isprivate`/`sprk_isinternalonly`/`sprk_privilegeclassification`
  are existing `sprk_communication` columns (data-model doc, task 006), read as display/label metadata.
- **Publish size:** compressed **47.09 MB** (baseline ~46 MB; ceiling ≤60 MB) — within budget, ~0 attributable
  delta (no packages added). **No new HIGH CVE** (no package changes).
- **Test-update obligation:** met — new unit tests (`CommunicationThreadReadServiceTests`) + new seam test
  (`CommunicationPrivilegePrivacySeamTests`).

---

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — succeeds (0 errors).
- BFF tests (filtered): **29 passed / 0 failed** (read-service incl. 3 new FR-21 tests; new seam; access-filter).
  - Negative over-disclosure seam test: `ReadThreadAsync_UnauthorizedCaller_SeesNoRecipientsMarkersOrPrivilege_NoOverDisclosure`.
- Client: scoped `tsc --noEmit` clean (0 errors); jest **205 passed** across affected suites
  (PrivacyMarkers/buildTimeline/ConversationView/CommunicationTimeline); `prettier --write` applied.
