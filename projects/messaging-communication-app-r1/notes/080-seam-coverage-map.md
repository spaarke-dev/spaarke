# Task 080 — Vertical-Slice Seam Coverage Map (feeds task 090 `/test-diet`)

> Written by task 080. Maps each AC/slice from `tasks/080-vertical-slice-seam-tests.poml` to the test file(s)
> that satisfy it — existing MAINTAIN-class coverage (referenced, not cloned) plus the new additive tests
> this task adds. Per ADR-038, duplicating existing assertions is a bannable anti-pattern (B6/B7/B9); this doc
> is the honesty record for what was and wasn't duplicated.

## Summary

| AC / Slice | Satisfied by (existing, MAINTAIN-class, KEEP) | New in task 080 | Notes |
|---|---|---|---|
| AC1 — send/archive + echo-dedup | `CommunicationServiceMessageSendTests.cs` (all cases: dispatch+persist, echo-dedup mark, echo no-op, duplicate echo, best-effort×2, no-token-leak, explicit ThreadId, InReplyTo) | — (none added) | **Archiver composition NOT added — see Finding 1 below.** The "archiver writes a transcript to SPE" half of AC1 is not composable against real product code; see finding. |
| AC2 — ingest idempotency + DLQ | `IncomingMessagingJobHandlerTests.cs` (single event, duplicate, own-echo no-op, lock race, poison at max attempts, transient retry, enrichment-non-fatal, no-message-id no-op) | — (none added) | Fully covered; no gap found. |
| AC3 — `IThreadResolver` (both directions, fresh + join) | `ThreadResolverSeamTests.cs` (email reply joins parent; fresh outbound email creates record-anchored thread; chat joins ACS thread via channel-ref; fresh chat creates thread+channel-ref; NFR-02 non-fatal; ingestor→resolver wiring) | `MessagingSpineSeamTests.cs` (2 tests — see below) | Existing coverage is the resolver's *own* contract. New tests close a *composition* gap one layer up (raw ACS event → normalizer → ingest → resolve), not a resolver-logic gap. |
| AC4 — privacy: internal-only + private + point-forward | `CommunicationAccessFilterTests.cs` (internal-only + privilege, exhaustive); `CommunicationThreadReadServiceTests.cs` (private-thread total exclusion via empty impersonated set, unread-count exclusion) | `CommunicationThreadReadServiceTests.cs` — **1 new test**: `ReadThreadAsync_PrivateThreadOpenedPointForward_ReturnsOnlyPostBoundaryMessages` | Point-forward-open (NFR-06) had no existing test — genuine gap, closed. |
| AC5 — email inbound characterization | `InboundPipelineTests.cs`, `IncomingAssociationResolverTests.cs` | — (none added; referenced only) | These already pin the field-mapping + dup-skip-on-Graph-message-id behavior the 040 resolver extension must not regress. Confirmed green (see build/test results below) — serves as the characterization guard. |
| AC6 — no banned patterns + placement | N/A (process criterion) | Self-grep run on both new/modified files (see report) | Clean — no `Mock<HttpMessageHandler>`, no DI-registration tests, no ctor null-check tests. |

## New files / changes

1. **`tests/integration/seam/Communication/MessagingSpineSeamTests.cs`** (new) — 2 tests.
2. **`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationThreadReadServiceTests.cs`** (extended) — 1 new test (`ReadThreadAsync_PrivateThreadOpenedPointForward_ReturnsOnlyPostBoundaryMessages`) + `using System.Linq;` added.
3. **This file.**

No product code was changed.

## New test 1: `ProcessAsync_FreshChatMessage_NormalizesIngestsResolvesAndPersistsThreadStampedRecord`

Composes a raw ACS `ChatMessageReceivedInThread` Event Grid job → the REAL `AcsEventNormalizer` → the REAL
`CommunicationChannelDispatcher` → the REAL `MessagingIngestor` → the REAL `ThreadResolver` (with a real
`MessagingThreadKeyStrategy`) → persist, for the **fresh-thread** branch (no existing channel-ref).

**Why additive, not a clone:**
- `IncomingMessagingJobHandlerTests` already composes job → normalizer → dispatcher → ingestor, but its
  `Harness` constructs `MessagingIngestor` **without** a thread resolver (the constructor's
  `threadResolver` parameter is omitted, defaulting to `null`), so `MessagingIngestor.IngestAsync`'s
  `if (_threadResolver is not null)` branch never runs — that suite proves idempotency/DLQ, not thread
  assignment.
- `ThreadResolverSeamTests.MessagingIngestor_InboundChatMessage_PersistsThenStampsResolvedThread` composes
  ingestor → resolver, but hand-builds a `NormalizedMessage` directly as the `ChannelIngestRequest.Message`,
  skipping `AcsEventNormalizer` entirely (no raw ACS JSON event is parsed).
- Neither test exercises the full chain — raw event → normalizer → ingest → resolve → persisted +
  thread-stamped record — which is exactly how `IncomingMessagingJobHandler` is wired in production DI
  (`CommunicationModule` line ~102/187 registers `MessagingIngestor` with the real `IThreadResolver` injected).
  This test closes that composition gap.

## New test 2: `ProcessAsync_ChatMessageOnKnownAcsThread_JoinsExistingThreadWithoutCreatingDuplicate`

Same full chain as test 1, but for the **join** branch (a channel-ref already maps the ACS thread id to an
existing `sprk_communicationthread`). Asserts no new thread/channel-ref is created and the existing thread id
is stamped directly — the direction-symmetric counterpart to test 1, closing the same composition gap for the
other resolver branch.

## New test 3: `ReadThreadAsync_PrivateThreadOpenedPointForward_ReturnsOnlyPostBoundaryMessages`

Added to the existing `CommunicationThreadReadServiceTests.cs` (reuses its harness — `SetupMessages` /
`MessageRow` / `Sut()`). Models point-forward-open per the task's guidance: the Dataverse grant boundary is
enforced upstream of the service (impersonation only returns rows the caller may read), so the impersonated
mock is set up to return **only** post-boundary rows; the test asserts the read service returns exactly those
and explicitly asserts two pre-boundary message ids are absent. This is the one genuine behavioral gap in the
existing privacy-slice coverage — `CommunicationThreadReadServiceTests` had a **total**-exclusion case
(empty impersonated set → empty result) but no **partial**-visibility case modeling "flip to open exposes only
messages from that point forward." No existing test asserted this mixed scenario; nothing was duplicated.

No `EffectiveFrom`-style filter path exists in `CommunicationAccessFilter` (confirmed by reading
`Access/CommunicationAccessFilter.cs` — its two checks are internal-only + privilege-passthrough only), so the
test does not invent one; it models point-forward as a which-rows-come-back contract at the query seam, per the
task's explicit instruction.

## Finding 1 (honesty-over-volume — no test added for this half of AC1)

**The messaging send/archive slice's "archiver writes a transcript to SPE" behavior does not exist in the
shipped product code**, so no seam test was written asserting it as a composed behavior.

Evidence (read directly from source, not inferred):
- `CommunicationService.SendAsync` branches `CommunicationType.Message` sends into a dedicated
  `SendMessageAsync` method (line ~914) that is **entirely separate** from the general send path.
- `SendMessageAsync` (lines 476–613) calls `_channelDispatcher.ResolveSender(CommunicationType.Message)` and
  persists via `CreateMessageDataverseRecordAsync` — it **never calls `_channelDispatcher.ResolveArchiver(...)`
  or `ArchiveToSpeAsync`**.
- `ArchiveToSpeAsync` (the only call site of `ResolveArchiver`, confirmed via repo-wide grep — one match in
  `CommunicationChannelDispatcher.cs` and one in `CommunicationService.cs`) is invoked only from the
  general/mailbox send path (`request.ArchiveToSpe && ...` branches around line 1075–1140) and from
  `IncomingCommunicationProcessor`'s inbound-email archival — never from `SendMessageAsync`.
- `MessagingChannelSender.cs` (the messaging sender) contains no archive/SPE call either.

This is a genuine gap between spec `FR-01`'s stated acceptance ("...the archiver writes a message-transcript
artifact to SPE") / task 080's AC1 wording, and what task 051 (already shipped, `status: completed`) actually
built. `MessagingArchiverTests.cs` already tests `MessagingArchiver.GenerateEml` correctly in isolation (the
artifact-format contract), and the archiver + its `CommunicationType.Message` registration are real and
correctly wired into `CommunicationChannelDispatcher` — but nothing in the send path ever calls it.

Per the task's explicit instruction ("if ... a slice turns out to be genuinely already composed by an existing
test ... DO NOT manufacture a duplicate ... reduce this file to the slices that ARE additive. Honesty over
volume") and per this task's own hard boundary ("NO product code changes"), the correct action was to **not**
fabricate a test that manually stitches `ResolveSender` + `ResolveArchiver.GenerateEml` together outside the
real `SendMessageAsync` call graph — doing so would assert product behavior that does not exist, which is a
worse outcome than a documented gap. This finding is reported here and in the task's final report for the
orchestrator/human to decide next steps (e.g., a follow-up task to wire `ArchiveToSpeAsync` into
`SendMessageAsync`, or an ADR/spec correction if archival-per-message was intentionally descoped for chat).
No product code was changed to work around this — that would be out of scope for a tests-only task.

## Build / test verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — succeeded, 0 errors (pre-existing warnings only, none introduced
  by these test files since they compile into the test assembly, not the API assembly).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Communication"` — **487 passed, 8
  skipped (pre-existing, unrelated), 0 failed, 495 total**. `tests/integration/seam/**/*.cs` (including the new
  `MessagingSpineSeamTests.cs`) compiles into this same assembly per its `.csproj`'s `Compile Include`, so this
  run includes the seam tests — there is no separate `tests/integration/seam/` project to target independently.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~MessagingSpineSeamTests|FullyQualifiedName~PointForward"` —
  **3 passed, 0 failed** (the 3 new tests in isolation).
- Publish-size / CVE: **unchanged** — this is a test-only change (no product code, no new package references).
