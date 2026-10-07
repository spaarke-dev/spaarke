---
paths:
  - "src/server/api/Sprk.Bff.Api/**"
  - "src/server/shared/Spaarke.Core/**"
  - "src/server/shared/Spaarke.Dataverse/**"
---

# BFF hygiene (binding) — root CLAUDE.md §10

The BFF is the single backend for every Spaarke client surface. When a task adds new endpoints, services, DI registrations, packages or background work to these folders:

1. **Load `.claude/constraints/bff-extensions.md`** before designing the addition. It is the binding pre-merge checklist.
2. **State the placement decision** — even when the answer is "in BFF" — in the PR description or design doc, citing the criteria from `bff-extensions.md`.
3. **Use the `Services/Ai/PublicContracts/` facade** for CRUD code that needs AI. Do not inject `IOpenAiClient`, `IPlaybookService` or other AI-internal types into CRUD code (ADR-013).
4. **Measure publish-size impact on every BFF-touching task**, against a **fresh build of master, never a recorded baseline** (baselines age as other projects merge).
   - Build both sides from fresh worktrees at short paths (e.g. `C:\wtNNNm` / `C:\wtNNNb`). Deep paths silently produce partial publishes that zip smaller.
   - Zip both with the same tool (`Compress-Archive`, as `scripts/Deploy-BffApi.ps1` does).
   - File counts must match; if they differ, one publish is incomplete and the delta is meaningless.
   - Report both sizes, the delta and the zip tool.
   - Thresholds: ≥ +5 MB per task needs explicit justification in the PR (a trigger, not a ban); ≥ 55 MB cumulative triggers an architecture review; ≥ 60 MB is a hard stop — roll back or extract, or the owner approves an ADR-029 amendment first.
   - Procedure, measured hazards and current baseline: `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule".
5. **No new HIGH-severity CVE** from `dotnet list package --vulnerable --include-transitive`. Fix it by upgrading (or replacing) the package. If no fixed version exists upstream, do not ship it silently: record in the PR the advisory ID, why the vulnerable code path is not reachable here (or what mitigates it), and the follow-up that removes it, and get the owner's explicit sign-off in the PR. Until that sign-off exists the finding stays open (F1) and the task is not complete — raise 🔔 Human Input Required (root §6). Any audit suppression added carries the advisory link and a dated comment.
6. **Update tests.** Changes under `Sprk.Bff.Api/Services/` add or update tests in `tests/unit/Sprk.Bff.Api.Tests/`. Endpoints that map unconditionally need unconditional service registration. Exceptions need explicit code-review sign-off with the reason (`bff-extensions.md` § F). When a service must stay feature-gated, ADR-030 is the canonical mechanism for satisfying this. Enforcement is the PR template, the code-review checklist and reviewer judgment — **not** a CI script, by design.

**Conditional DI registrations.** When a `*Module.cs` change sits inside an `if (flag) { … }` block, the reviewer applies `bff-extensions.md` § F.1 (static scan + ADR-032 Null-Object kill switch), § F.2 (inspect fixture/config first when a test is skipped as a suspected DI issue) and § F.3 (reproduce empirically before applying a ledger fix).

**Project requirements.** A project adding code to the BFF has a **Placement Justification** section in `design.md`. A project touching the BFF or `src/solutions/SpaarkeAi/**` has a `<hot-path-declaration>` block in `design.md` (`bff-extensions.md` § G; registry `projects/INDEX.md`).
