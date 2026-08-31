# ChatEndpoints Split — R1

> **Status**: INITIALIZED (folder + design only; execution not started, operator-gated)
> **Origin**: code-quality-and-assurance-r3 follow-on seed `notes/red-item-analyses/RED-2-chatendpoints-split.md`
> **Epic**: Code Quality (#427) · **Type**: refactor / decomposition · **Surface**: BFF (`Sprk.Bff.Api`)

## One-liner

Split `Api/Ai/ChatEndpoints.cs` — **4,066 LOC mapping 18 routes** in one file, the fastest-growing BFF
god-file — into cohesive route-group files, pushing inline handler logic into the existing
`Services/Ai/Chat/**`. Restores the "thin endpoints, delegate to services" rule at the worst offender.

## Why now

- It grew +479 lines during r3 alone — actively worsening.
- It's a merge-contention magnet: every chat feature touches this one file (the ~8 active AI/Compose
  worktrees all edit adjacent surface).
- #2 `GodClassGuardTests` waiver.

## Quick links

- [design.md](design.md) — problem, approach, scope, risks, acceptance, hot-path declaration
- Seed analysis: `../code-quality-and-assurance-r3/notes/red-item-analyses/RED-2-chatendpoints-split.md`

## Graduation criteria

- [ ] No resulting endpoint file > 2,000 LOC; `ChatEndpoints.cs` waiver removed from `GodClassGuardTests`.
- [ ] Route-dump identical; all chat contract + SSE tests green (streaming/cancellation unchanged).
- [ ] Build 0/0 under the analyzer gate; ArchTests 38/0.
- [ ] Scheduled into a quiet window; `/conflict-check` clean vs the active AI worktrees before each PR.
