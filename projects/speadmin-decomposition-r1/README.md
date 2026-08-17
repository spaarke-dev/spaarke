# SpeAdmin GraphService Decomposition — R1

> **Status**: INITIALIZED (folder + design only; execution not started, operator-gated)
> **Origin**: code-quality-and-assurance-r3 follow-on seed `notes/red-item-analyses/RED-1-speadmin-graphservice-decomposition.md`
> **Epic**: Code Quality (#427) · **Type**: refactor / decomposition · **Surface**: BFF (`Sprk.Bff.Api`)

## One-liner

Decompose `Infrastructure/Graph/SpeAdminGraphService.cs` — the codebase's #1 god-class (**4,911 LOC,
~102 methods, 0 internal structure**) — into cohesive per-concern services, behavior-preserving. It is the
top `GodClassGuardTests` waiver and the reason the ratchet floor can't drop.

## Why now

- It anchors the God-class ratchet floor; every other server file is measured against a guard this file
  holds at 4,911.
- A 102-method class is unreviewable and untestable at the seam.
- **Low risk / high leverage**: phase-1 is a byte-neutral partial-class split.

## Quick links

- [design.md](design.md) — problem, phased approach, scope, risks, acceptance, hot-path declaration
- Seed analysis: `../code-quality-and-assurance-r3/notes/red-item-analyses/RED-1-speadmin-graphservice-decomposition.md`
- Gate it clears: `.claude/patterns/testing/god-class-ratchet.md`

## Graduation criteria

- [ ] No resulting file > 2,000 LOC; `SpeAdminGraphService.cs` waiver **removed** from `GodClassGuardTests`.
- [ ] Public contract unchanged (route-dump identical; SpeAdmin integration tests green).
- [ ] Build 0/0 under the analyzer gate; publish size neutral; no new NuGet.
- [ ] `/conflict-check` clean before each PR.
