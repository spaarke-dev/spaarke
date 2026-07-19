# Test diet report — messaging-communication-app-r1

**Run date**: 2026-07-18
**Branch**: work/messaging-communication-app-r1
**Scope**: tests touched between project start (`784420805`, 2026-07-16) and HEAD
**Classifier**: ADR-038 §7 build-vs-maintain criteria (17-ban list B1–B17)

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 23 files / 136 methods | confirmed — no action |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (wrong KEEP path) | 0 | — |
| **Total R1-authored test files** | **23** | — |

> **Scope note**: `git log 784420805..HEAD` on this branch also lists `tests/**/Ai/*` files
> (`AgentToolProjectionGroundingSeamTests`, `DispositionRoutabilitySeamTests`,
> `AgentTurnLoopContractTests`, `RecordSearchServiceTests`, etc.). Those are **NOT R1's** —
> they entered this branch's ancestry via master merges from `spaarkeai-assistant-enhancements`
> and `email-communication-solution-r4` (002 SurfaceLaunch) during the same window. They are
> out of scope for this diet and belong to their own projects' close-out. R1's authored surface
> is the ACS/messaging/membership Communication tests below.

## R1-authored test files (all MAINTAIN)

Unit — `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/**` (service/domain behavior; KEEP):
`AcsBootSafetyTests`, `AcsEventGridIngressServiceTests`, `AcsEventNormalizerTests`,
`AcsIdentityServiceTests`, `AcsThreadServiceTests`, `CommunicationAccessFilterTests`,
`CommunicationServiceMessageSendTests`, `CommunicationThreadReadServiceTests`,
`DataverseImpersonationTests`, `DirectThreadAccessServiceTests`, `IncomingMessagingJobHandlerTests`,
`MessageAttachmentMaterializerTests`, `MessagingArchiverTests`, `MessagingChannelRoutingTests`,
`MessagingChannelSenderTests`, `MessagingIngestorTests`,
`Membership/{DirectThreadExplicitParticipantReaderTests, MembershipReconcileJobTests, MembershipReconcilerTests, ThreadMembershipDerivationServiceTests}`,
`Registration/AcsBoundaryProvisioningTests`.

Seam — `tests/integration/seam/Communication/**` (vertical-slice-seam KEEP path per ADR-038 §7, added 2026-07-09 by E-40):
`MessagingSpineSeamTests` (task 080, additive), `ThreadResolverSeamTests`.

## Why all MAINTAIN — ban scan evidence

Mechanical B-ban scan across all 23 files came back clean:

| Ban | Pattern scanned | Result |
|---|---|---|
| B1/B7/B15 | `Mock<HttpMessageHandler>` | **0 matches** |
| B4 | `Throws<ArgumentNullException>` on ctor | **0 matches** |
| B13 | name-without-scenario (`Test1`, `_Works`, `_BugNNN`) | **0 matches** |
| B3 | `GetRequiredService` **as assertion** | 2 files use it as **SUT resolution / harness plumbing** (`AcsBootSafetyTests`, `CommunicationServiceMessageSendTests`) + `RungTestSupport.cs` helper — NOT registration assertions. Verified: `AcsBootSafetyTests` resolves the SUT then asserts boot-safety behavior (`.NotThrow` on construct w/o ACS config) + clear config-error messages — this is the regression test for the SIGABRT/exit-134 boot-crash fixed this session (every-bug-is-a-regression → MAINTAIN). |

All method names follow `{Method}_{Scenario}_{ExpectedResult}` (spot-checked; e.g.
`AcsServices_WithNoEndpointConfigured_ConstructWithoutThrowing`,
`ReadThreadAsync_PrivateThreadOpenedPointForward_ReturnsOnlyPostBoundaryMessages`).

Corroborating context: every R1 implementation task (010–070) ran **FULL rigor** with
`code-review` + `adr-check` at Step 9.5, and the tests-only task 080 performed a reuse-first
pass (see `notes/080-seam-coverage-map.md`) that added only 3 genuinely-additive tests and
self-grepped its files clean for banned patterns.

## Delete commands

_None — no scaffolding-class tests found._

## Path-move commands

_None — all files at canonical KEEP paths._

## Ambiguous — reviewer judgment

_None._

## Count delta

- Tests added during project: 136 (R1-authored)
- Classified MAINTAIN: 136
- Classified SCAFFOLDING: 0
- Net post-diet expected count: 136 (unchanged — nothing to remove)

## Open finding carried forward (not a test-diet action)

Task 080 **Finding 1** (messaging-archival gap): the `Message` send path (`CommunicationService.SendMessageAsync`)
never invokes `ResolveArchiver`/`ArchiveToSpeAsync`, so chat messages get no SPE transcript. `MessagingArchiver`
+ its registration are real and unit-tested (`MessagingArchiverTests`), but nothing in the send path calls it.
This is a **product gap**, not a test-scaffolding issue — surfaced here and in the 090 wrap-up for a follow-up
decision (wire archival into `SendMessageAsync`, or spec/ADR correction if per-message chat archival was
intentionally descoped). No test was fabricated to assert non-existent behavior (honesty-over-volume).

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior;
Google test-sizes; DHH less-tests). 17-ban classifier B1–B17.
