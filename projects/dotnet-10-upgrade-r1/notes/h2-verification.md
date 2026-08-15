# H2 — Adversarial verification of DI-graph fixes (task 021, NFR-07)

> **Verifier**: task 021 (non-author, Opus, FULL rigor, isolated worktree) — 2026-08-12
> **Subject**: task 020 DI fixes (10 roots R1–R10) per `notes/h2-di-validation.md` §4
> **Verdict**: **PASS** — all 10 roots CONFIRMED behavior-preserving; independent clean boot confirmed; no REFUTED items.

## 1. Non-author attestation
This verification was performed by a different agent than task 020's author (main session + `fix-h2` subagent). Source was read as-committed at branch HEAD in an isolated worktree; the DI guard was re-run independently.

## 2. Independent clean-boot / DI-guard result — PASS
Ran `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter DiGraphValidationTests`.
Result: `Failed: 0, Passed: 1, Skipped: 0, Total: 1 (net10.0)`.
`DiGraphValidationTests` re-enables ValidateOnBuild+ValidateScopes over the full BFF graph in the Development environment on net10 and asserts no captive/unresolvable dependency. It passed — confirming a clean boot without trusting the author's claim.

## 3. ValidateOnBuild / ValidateScopes not disabled — CONFIRMED
Grep of `src/server/api/Sprk.Bff.Api/**` for `ValidateOnBuild|ValidateScopes|UseDefaultServiceProvider` finds only doc-comments in the 5 fixed production files. The only disabling (`= false`) is in the test fixture `CustomWebAppFactory` (pre-existing, for other fixtures); the guard's `ValidatingWebAppFactory` subclass re-enables both (last-call-wins). `Program.cs` uses plain `CreateBuilder`/`Build()` — production Development keeps the on-by-default validation. No suppression was used to force a green boot.

## 4. Per-root verdicts

| Root | File | Family | Verdict |
|---|---|---|---|
| R1 | Workers/Office/UploadFinalizationWorker.cs | A | CONFIRMED |
| R2 | Workers/Office/ProfileSummaryWorker.cs | A | CONFIRMED |
| R3 | Services/Communication/CommunicationEnrichmentService.cs | A | CONFIRMED |
| R4 | Services/Communication/IncomingCommunicationProcessor.cs | A | CONFIRMED |
| R5 | Services/Ai/LinearConsumers/LinearConsumersModule.cs + Infrastructure/DI/AnalysisServicesModule.cs | B | CONFIRMED |
| R6 | Services/Ai/Nodes/AiAnalysisNodeExecutor.cs | A | CONFIRMED |
| R7 | Services/Ai/Nodes/LiveFactNode.cs | A | CONFIRMED |
| R8 | Workers/Office/UploadFinalizationWorker.cs (IEmailToEmlConverter/AttachmentFilterService) | A | CONFIRMED |
| R9 | Services/Communication/CommunicationService.cs | A | CONFIRMED |
| R10 | Services/Communication/IncomingCommunicationProcessor.cs (IEmailAttachmentProcessor) | A | CONFIRMED |

## 5. Stream-scope lifetime (R1, R9) — CONFIRMED
Every SPE stream is fully materialized to memory (CopyToAsync → ToArray / in-memory MemoryStream upload) BEFORE its resolving scope disposes; no lazily-SPE-backed Stream outlives its scope.
- R1 `UploadToSpeAsync` uploads an in-memory stream; `ProcessEmailAttachmentsAsync` downloads + `ExtractAttachments` within the outer scope (`using(emlStream)`); per-attachment uploads use their own scopes.
- R9 `ArchiveToSpeAsync` (in-memory upload), `FetchEmlAttachmentsForEmbedAsync` (per-attachment scope spans `CopyToAsync`), `DownloadAndBuildAttachmentsAsync` (batch scope spans every download+copy; returns `byte[]`).
Behavior-preservation rests on `SpeFileStore` being a stateless ADR-007 facade — which holds. Minor stylistic inconsistency noted (batch scope vs per-item scope between the two Fetch/Download methods); not a defect.

## 6. R5 demote + NullActionResolver symmetry — CONFIRMED
`ActionResolver` (AddScoped) is stateless. All `IActionResolver` consumers are per-request endpoint params or Scoped services (grep-verified) — no singleton consumer; the Communication AI facades reach it only via R3's per-op scopes. The guard passing is dispositive (a singleton→scoped capture would have failed ValidateScopes). `NullActionResolver` peer demoted symmetrically to AddScoped (ADR-032); its P3 fail-fast behavior (throws `FeatureDisabledException("ai.linear-consumers.disabled")`) is unchanged by lifetime.

## 7. Guard-test soundness — TRULY ASSERTS
`DiGraphValidationTests.BffDiGraph_InDevelopment_HasNoCaptiveDependenciesOrUnresolvableServices` forces the host build via `factory.Services`, flattens any AggregateException, and calls `Assert.Fail` with the captive-dep list — a reachable failure path. Network-free and no file I/O (reuses CustomWebAppFactory mocks). It is a whole-graph startup-contract guard, distinct from the banned B3 per-service DI-registration test. Recommend KEEP as the permanent H2 regression guard.

## 8. Overall
PASS. No REFUTED items — task 020 stands. No §6.5 ADR conflict surfaced.
