# Task 021 — Email-in-flow compact block + open→modal (FR-04)

**Status**: completed 2026-07-21 · FULL rigor · sonnet/high · Step 9.5 gate RUN — verdict **SHIP** (2 Minor fixed, 2 accepted) · included an **escalation-resolved backend enrichment** (owner chose "full backend enrichment first")

## Escalation → resolution (the reason this task grew)
The POML escalation trigger fired: the compact block needs subject + from + to, but the read DTO (`ThreadMessageDto`, FR-18) carried `From` only — **no `Subject`, no recipient list**. Displaying recipients is correctness-critical (NFR-01/FR-21). Surfaced to the owner (§6); owner chose **full backend enrichment**. Key correctness finding: recipients are stored as `sprk_to` (a "; "-joined field) ON the `sprk_communication` row, so projecting it rides the SAME already-impersonated, access-filtered row (no new query/gate) — the authoritative To header of a row the caller may already read, NOT a fabricated/derived list, and NEVER BCC (`sprk_bcc` is a separate column, never selected).

## What shipped (3 layers)
**Backend enrichment** (`Services/Communication/`):
- `ThreadMessageDto` gained `string? Subject` + `IReadOnlyList<string> To`.
- `CommunicationThreadReadService`: `SubjectField`/`ToField` consts; added to BOTH message `$select`s (thread-read + by-regarding); `ParsedMessage` + `ParseMessageRow` project them (`SplitRecipients` splits `sprk_to` on ';', trims, drops empties → never null); `BuildDto` passes them through on the VISIBLE row only.
- Tests: `CommunicationThreadReadServiceTests` (Subject/To projection + empty-degrade) + `CommunicationByRegardingReadTests` (guards the second `$select`) — **26 backend tests green**.

**Client plumbing**:
- `IThreadMessageDto` (+`subject`/`to`), `TimelineMessage` (+`subject?`/`to?`), `mapThreadMessageDtoToTimelineMessage` mapping.

**Frontend** (`ConversationView/`):
- New `subcomponents/EmailInFlowBlock.tsx`: compact block (subject/from/to with fallbacks, ONE `<ChannelBadge channelType="email"/>`, open-icon → `onOpen(message)`), `role="group"` region, keyboard/ARIA, semantic tokens.
- `ConversationView.tsx`: renderer branches `channelType==='email'` → block / else → bubble, INSIDE the existing `data-message-id` anchor (filters + `scrollToMessage` + compose bar untouched). New optional `onOpenEmail?(message)` prop (ADR-012 — host mounts the extended `SendEmailDialog`; the now-enriched message lets it build the view/detail sourceRecord). Mirrors task 023's `onPin` seam.
- Test-fixture migration: 011/023 flipped their type-agnostic bubble fixtures to Message-type; 014 filters now asserts email→block (`role="group"`) vs chat→bubble (`role="article"`) — semantically lossless (gate-verified no weakened coverage).

## §10 BFF hygiene
Placement: extends the existing read service — **no new endpoint/service/DI/package/background work** (pure projection). Publish size: **unchanged** (zero new dependencies; 143 MB uncompressed ≈ ~49.6 MB compressed baseline, well under the 60 MB ceiling). 0 new CVE (no package change). Tests updated (§10 obligation).

## Step 9.5 gate — SHIP, recipient-disclosure SAFE
Reviewer traced `ParseMessageRow → CommunicationAccessFilter → BuildDto` in both read paths: `To` rides only visible rows (a hidden row contributes zero recipients); BCC structurally excluded; both selects updated. No Critical/Major.
- **Minor 1 (FIXED)**: filter/display incoherence — the block renders subject/to but the task-014 word filter matched only body+sender+senderName → couldn't filter emails by subject, and matched on unshown body. Added `subject` + `to` to `messageSearchText`, and `subject` to `extractWordOptions` (addresses still excluded from options to avoid "com" noise).
- **Minor 3 (FIXED)**: added a by-regarding Subject/To assertion (guards the second `$select` from silent revert).
- **Minor 2 (accepted)**: `SplitRecipients` over-splits a display name containing ';' — cosmetic, rare, no disclosure risk.
- **Minor 4 (accepted)**: `EmailInFlowBlock` is exported at the top-level barrel — symmetric with its sibling `MessageBubble` (already exported); defensible.
- ADR verdict: NFR-01/FR-21 recipient-safety PASS · ADR-021 PASS · ADR-012 PASS · ADR-038 PASS · §10 PASS · §11 PASS.

## Verification
- Backend: `dotnet build` clean; 26 read-service tests pass.
- Client: `npm test -- src/components/ConversationView` → 48 pass; combined w/ CommunicationTimeline → 134 pass. `tsc` 2 pre-existing unrelated errors only. `eslint` clean.

## Acceptance criteria — all met
Email → compact block (subject/from/to) not a bubble; message → bubble ✅ · exactly one Email indicator ✅ · open-icon launches the extended SendEmailDialog with thread+regarding (host seam; integration test renders the real dialog w/ "Smith v Jones") ✅ · missing-field fallbacks + keyboard/ARIA ✅ · dark mode ✅ · tests pass ✅.
