// SPIKE-LOCAL STUB — DO NOT COMPILE INTO Sprk.Bff.Api.
// This file documents the additive change that Phase 5 (task 020) will make to
// the production src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs.
//
// Status: design-locked by spaarkeai-compose-r1 spike-4 (2026-06-29).
// Production location: src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs
// Effective task: 020-bff-add-consumertype-composesummarize.poml (Phase 2, Wave 1b).

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// Diff to apply against the existing ConsumerTypes class:
//
//   public static class ConsumerTypes
//   {
//       // ... existing constants (MatterPreFill, ProjectPreFill, AiSummary,
//       //     SummarizeFile, ChatSummarize, EmailAnalysis, DailyBriefingNarrate) ...
//
//       /// <summary>
//       /// <c>ComposeEndpoints</c> (Compose workspace, R1 smoke test) — whole-document
//       /// summarize action invoked from Compose toolbar. Dispatches via consumer routing
//       /// to the existing Document Summary playbook
//       /// (sprk_analysisplaybookid: 47686eb1-9916-f111-8343-7c1e520aa4df) per design.md §14 row 6.
//       /// </summary>
//       public const string ComposeSummarize = "compose-summarize";
//
//       public static readonly IReadOnlyList<string> All = new[]
//       {
//           MatterPreFill,
//           ProjectPreFill,
//           AiSummary,
//           SummarizeFile,
//           ChatSummarize,
//           EmailAnalysis,
//           DailyBriefingNarrate,
//           ComposeSummarize, // <-- ADDITIVE; appended to keep diff minimal
//       };
//   }
//
// Additive-only properties:
//   - No existing constant renamed or repurposed
//   - All[] order preserved for existing entries (ComposeSummarize appended)
//   - lower-kebab-case naming matches convention
//   - URL-safe (no path separators) for cache-key + telemetry dimension safety
//   - Stable: once shipped, the literal "compose-summarize" is the Dataverse contract
//
// ADR-013 facade boundary: the constant lives in PublicContracts/ — exactly where
// CRUD-side callers (the future ComposeService, task 021) consume it. No production
// code references AI internals (IOpenAiClient, IPlaybookService) via this path.
