# `tests/integration/regression/**` — regression KEEP category

> **Category authority**: [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md)
> **Authoring rule**: "Every bug = regression test" (per `tests/CLAUDE.md` and `.claude/constraints/testing.md` MUST rules)

## What lives here

**One file per past production bug**, named `Issue{N}_{Description}Tests.cs` (e.g., `Issue417_DailyBriefingCascadeTests.cs`). The test reproduces the bug scenario as an integration test (via `WebApplicationFactory<Program>`) and verifies the fix.

## Deletion-safety rule

KEEP-protected. Deletion requires same-PR replacement. See ADR-038 §2.

## Inventory status (2026-06-26)

5 KEEP-regression files identified. The 2026-06-25 Daily Briefing 9-bug cascade is a backlog candidate for this category (per ADR-038 evidence section S-6). Bulk move pending.
