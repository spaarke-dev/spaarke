# Test diet report — email-communication-intelligence-r2

**Run date**: 2026-08-31
**Branch**: work/email-communication-intelligence-r2
**Scope**: `tests/**/*.cs` added/modified during R2 (ADR-038 §7 classifier is .cs-only per skill Step 1; colocated TS/TSX `src/**/__tests__` are out of scope by design).
**Method**: enumerate R2-touched `tests/**/*.cs`; apply the 17-ban classifier (heuristics 0–12). Grep for banned shapes (`Mock<HttpMessageHandler>`, `GetRequiredService` assertions, ctor-null `Throws`) returned **zero** matches.

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 20 | confirmed — no action |
| SCAFFOLDING (DELETE candidate) | **0** | none |
| AMBIGUOUS (reviewer judgment) | 0 | none |
| PATH-VIOLATION-PROTECTED (`tests/unit/Sprk.Bff.Api.Tests/**`) | 8 | **KEEP** — pre-existing repo-wide location, load-bearing, no same-PR replacement |
| Likely-swept (not R2 — external-access) | 3 | out of scope; no action |
| **Total `.cs` tests touched** | **31** | — |

## Delete commands

**None.** Zero scaffolding-class tests. R2 added no `Mock<HttpMessageHandler>`, DI-registration, or ctor-null tests.

## Path-move commands

**None emitted.** The 8 `tests/unit/Sprk.Bff.Api.Tests/**` files are flagged PATH-VIOLATION by heuristic 1 (that tree is not among the enumerated KEEP paths), but they are classified **PATH-VIOLATION-PROTECTED per the skill's behavior contract**: they test real behavior (footer injection, HMAC token signing, alias/tracking rungs, cross-path link, Graph normalizer, queue-feed, attachment-text, schema), live in the historical repo-wide BFF unit-test tree that predates R2, and have no same-PR replacement. Moving them is a repo-wide test-tree decision, not an R2 close-out action. Recorded for a future test-architecture sweep, not deleted.

## Maintain — confirmed (no action)

| KEEP path | Files | Why maintain |
|---|---|---|
| `tests/integration/seam/Communication/**` | EmailPropose, EmailTriage, EmailRegardingIntent, EmailCreateTask, EmailUploadCapture, EmailAttachmentAction, CommunicationProposalApply, CreateTaskApply, CommsAssessedProducer, TriagePersistence, TestRoutingGate | Vertical-slice seam tests (KEEP path since ADR-038 §2 E-40) — DoD for the dispatch-spine + Pillar A/B/C/D/E behaviors |
| `tests/integration/contract/Api/**` | ChatDocumentEndpointsContractTests, CommunicationsEndpointsContractTests | Endpoint contract tests (KEEP path) — the E1c `from-document` + Office comms endpoints |
| `tests/unit/domain/Communication/**` | CategoryRoutingGateTests | Domain unit test (KEEP path) — FR-E task 057 routing gate |

## PATH-VIOLATION-PROTECTED — keep (historical BFF unit-test tree)

`tests/unit/Sprk.Bff.Api.Tests/**`: CommunicationServiceFooterTests, TrackingTokenSignerTests, TrackingTokenRungTests, RecipientAliasRungTests, GraphMessageNormalizerTests, CrossPathLinkTests, CommunicationAttachmentTextServiceTests, CommunicationQueueFeedServiceTests, DataverseEntitySchemaTests, CommunicationIntegrationTests. All behavioral; keep in place.

## Likely-swept — out of R2 scope

`tests/integration/seam/ExternalAccess/StandingGrantRuntimeUnionSeamTests.cs`, `tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs`, `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalParticipationServiceInvalidationTests.cs` — matched the scope grep via a broad/merge commit but belong to the external-access workstream, not R2. No action.

## Reliability registry

No R2-touched `.cs` test method appears in `tests/.reliability-registry.json`; no stale entries to remove (registry `_exitRule` N/A).

## Count delta

- `.cs` tests touched during R2: 31
- Classified MAINTAIN / PROTECTED-KEEP: 28
- Classified SCAFFOLDING (delete): **0**
- Classified AMBIGUOUS: 0
- Out-of-scope swept: 3
- **Net post-diet expected count: unchanged (no deletions).**

## Verdict

**Clean diet — no reviewer action required.** R2's test additions are uniformly maintain-class behavioral/seam/contract tests. The only classifier flag is the pre-existing `tests/unit/Sprk.Bff.Api.Tests/**` location (repo-wide, not R2-introduced), protected from deletion by the skill's behavior contract.

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck scaffolding; Feathers characterization-vs-behavior; Google test-sizes). 17-ban classifier B1–B17.
