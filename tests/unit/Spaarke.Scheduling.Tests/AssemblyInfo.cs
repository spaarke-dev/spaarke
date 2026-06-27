// Disable xUnit parallel test execution for this assembly.
//
// Spaarke.Scheduling tests exercise the ScheduledJobHost (a BackgroundService
// driven by PeriodicTimer ticks at 200ms in test mode). When xUnit runs tests
// in parallel, multiple ScheduledJobHost instances + their underlying tick
// loops contend for CPU on shared GitHub Actions runners, causing predicate-
// wait timeouts even at 25s effective (CI-scaled) windows.
//
// Locally tests run fast even in parallel (faster CPU + less contention);
// disabling parallel execution at the assembly level adds < 2 minutes to
// total runtime in exchange for deterministic CI behavior.
//
// Companion to the CI-scaled WaitUntilAsync helpers in ScheduledJobHostTests
// and RetryAndIdempotencyTests (see commit 164e30812).
//
// Fixed 2026-06-23 in R3 PR #415 after observing CI-only flakes that bumping
// WaitUntilAsync timeouts 5x did not resolve.

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
