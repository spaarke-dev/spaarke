// Spaarke.Scheduling — in-process background-job infrastructure for Spaarke BFF.
//
// Scaffolded by R3 task 010 (2026-06-21). Contracts (IScheduledJob, JobExecutionContext,
// JobOutcome enum) land in task 011. ScheduledJobHost : BackgroundService lands in task 013.
//
// Per ADR-001: in-process scheduling only — no Azure Functions or external scheduler.
// Per ADR-012: shared component library under src/server/shared/.
// Per ADR-036 (to be authored in task 017): canonical replacement for ad-hoc
// BackgroundService implementations across BFF (~26 candidates to migrate opportunistically).
//
// Depends on: Spaarke.Core (sibling shared lib) + Cronos 0.13.0 (~50KB) +
// Microsoft.Extensions.Hosting.Abstractions / Logging.Abstractions / Options.

using System.Runtime.CompilerServices;

// Expose internals to test assembly (created in P2 Phase D).
[assembly: InternalsVisibleTo("Spaarke.Scheduling.Tests")]
