# Lessons Learned — Email Communication Solution R2

> **Project**: email-communication-solution-r2
> **Completed**: 2026-03-09

## What Went Well

1. **R1 foundation saved significant time** — Most services (CommunicationService, GraphSubscriptionManager, IncomingCommunicationProcessor) were 60-90% complete from R1. Assessment tasks (001, 010, 020) correctly identified existing state and avoided rework.

2. **Parallel task execution** — Tasks within dependency groups (030/031/032, 033/034, 040/041) executed concurrently with no merge conflicts, cutting elapsed time significantly.

3. **Best-effort pattern** — Wrapping archival and AI analysis in try/catch blocks kept the critical path (communication record creation, email send) clean. Failures in secondary operations don't cascade.

4. **GraphMessageToEmlConverter as pure transformation** — No I/O, no Dataverse calls. Easy to test (13 unit tests) and reason about. Clean replacement for the synthetic request/response pattern in EmlGenerationService.

5. **GraphAttachmentAdapter preserving existing interfaces** — Mapping Graph FileAttachment to existing EmailAttachmentInfo allowed reuse of AttachmentFilterService and EmailAttachmentProcessor without modification.

## Challenges

1. **Moq + Graph SDK v5 incompatibility** — 10 pre-existing test failures due to Moq's inability to mock non-overridable members in Microsoft.Graph v5. These tests (InboundPipelineTests) need rewriting with a custom test double or wrapper interface.

2. **Constructor parameter growth** — IncomingCommunicationProcessor and CommunicationService accumulated many dependencies through the project. Consider introducing a mediator or options pattern if this continues.

3. **EmailProcessingOptions still needed** — Task 035 couldn't consolidate EmailProcessingOptions into CommunicationOptions because it's still actively used by EmailToEmlConverter (Office workers), EmailAttachmentProcessor, and AttachmentFilterService. These are retained R1 services.

## Key Decisions

| Decision | Rationale |
|----------|-----------|
| Keep EmailToEmlConverter alongside GraphMessageToEmlConverter | Office workers (UploadFinalizationWorker) still use the Dataverse email → EML path |
| IncomingAssociationResolver as concrete (no interface) | ADR-010: ≤15 DI registrations, no seam needed |
| 4-level association priority cascade | Thread > Sender > Subject > Mailbox context provides good auto-linking |
| Daily send count reset via BackgroundService | Simpler than Dataverse workflow; runs at midnight UTC |
| HTTP 429 for send limit exceeded | Standard rate limiting response code |

## Reuse Opportunities

- **GraphMessageToEmlConverter** — Could be extracted to shared library if other services need Graph → EML conversion
- **IncomingAssociationResolver** — Association cascade pattern could apply to other entity matching scenarios
- **DailySendCountResetService** — Timer-based daily reset pattern reusable for other daily maintenance tasks
- **MailboxVerificationService** — Pattern for testing Graph API access reusable for other Graph-dependent features

## Metrics

| Metric | Value |
|--------|-------|
| Total tasks | 22 |
| Code tasks (FULL rigor) | 12 |
| Documentation tasks | 7 |
| Test/E2E tasks | 3 |
| New source files created | ~15 |
| Legacy files deleted | 8 |
| New unit tests added | ~80 |
| Lines deleted (legacy cleanup) | ~4,100 |
