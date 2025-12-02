# Task 0 — Add GitHub CI Workflow

This task file is **self‑contained**. It embeds prompt, constraints, guardrails, tests/CI expectations, and deliverables so an autonomous AI agent can complete it without additional instructions.

---

## Objective
Create a GitHub Actions workflow that enforces Spaarke SDAP guardrails on every PR and on pushes to `main`. The workflow must run build (warnings as errors), tests, formatting verification, and ADR policy checks.

## Files to create/edit (expected)
- `.github/workflows/sdap-ci.yml` (new)
- Optional: update solution/test projects if needed so at least one unit test runs

## References (absolute paths)
- ADR‑001 Minimal API & Workers: `C:\code_files\spaarke\docs\adr\ADR-001-minimal-api-and-workers.md`
- ADR‑007 SPE Storage Seam Minimalism: `C:\code_files\spaarke\docs\adr\ADR-007-spe-storage-seam-minimalism.md`
- ADR‑008 Authorization via Endpoint Filters: `C:\code_files\spaarke\docs\adr\ADR-008-authorization-endpoint-filters.md`
- ADR‑009 Caching (Redis‑first): `C:\code_files\spaarke\docs\adr\ADR-009-caching-redis-first.md`
- ADR‑010 DI Minimalism: `C:\code_files\spaarke\docs\adr\ADR-010-di-minimalism.md`
- Simplification Guide: `C:\code_files\spaarke\docs\guides\SDAP_Architecture_Simplification_Guide.md`
- ADR policy script to be executed by CI: `scripts/adr_policy_check.ps1`
- Global instructions/guardrails: `docs/dev/SDAP_Instructions.md`

## Agent Run Loop (execute verbatim)
1. Open `docs/dev/SDAP_Instructions.md` and this task file.
2. Open the ADRs listed in **References**.
3. Create `.github/workflows/sdap-ci.yml` exactly as specified in **Workflow spec** below.
4. Ensure there is at least one runnable unit test. If none exists, add a placeholder test in the existing test project or create a minimal test project.
5. Run `dotnet restore`, `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`, and `pwsh scripts/adr_policy_check.ps1 -RepoRoot .` locally.
6. Output **only**: the unified git diff for new/changed files and a single commit message.
7. Print `NEXT: Task A — Establish DI and pipeline order in Program.cs` and stop.

## Hard Guardrails (must pass)
- CI must run on `windows-latest`.
- Fail on any build warning (treat as error), failing tests, formatting drift, or ADR policy violation.
- Keep YAML minimal (<120 lines), deterministic, and easy to read.
- Use `shell: pwsh` for the PowerShell step.
- Cache NuGet to speed up runs.

## Workflow spec (Claude must implement)
- Name: **SDAP CI**
- Triggers:
  - `pull_request` on any branch
  - `push` on `main`
- Permissions: `contents: read`
- Concurrency: cancel in-progress on same ref (`group: sdap-ci-${{ github.ref }}`)
- Runner: `windows-latest`
- Steps (exact order):
  1. Checkout (actions/checkout@v4)
  2. Setup .NET 8 (actions/setup-dotnet@v4)
  3. Cache NuGet (`~/.nuget/packages`) with key using `**/*.csproj` hash
  4. `dotnet restore`
  5. `dotnet build -c Release -warnaserror`
  6. `dotnet test -c Release --logger trx --results-directory ./TestResults`
  7. `dotnet format --verify-no-changes`
  8. `pwsh ./scripts/adr_policy_check.ps1 -RepoRoot .`
  9. Upload test results artifact (always)

## Implementation Steps (minimal path)
- Create the YAML file under `.github/workflows/` with the spec above.
- If needed, add a trivial passing test (e.g., `Assert.True(true);`) in an existing test project to ensure “Test” step runs green initially.
- Do not add extra jobs or tools.

## Testing (write/run first locally)
- Run the full sequence locally to confirm the workflow will pass once pushed:
  - `dotnet build -warnaserror`
  - `dotnet test`
  - `dotnet format --verify-no-changes`
  - `pwsh scripts/adr_policy_check.ps1 -RepoRoot .`

## CI gates to keep green (after push/PR)
- Build (warnings as errors)
- Test (unit + integration as they get added)
- Formatting verification
- ADR policy checks

## Deliverables (output format)
- Unified git diff for: `.github/workflows/sdap-ci.yml` and any minimal test scaffolding added.
- Single commit message: `ci: add SDAP CI workflow (ADR-001, ADR-007, ADR-008, ADR-009, ADR-010)`.

## Conclusion / Next Task
When CI passes locally and the file is committed, print: `NEXT: Task A — Establish DI and pipeline order in Program.cs`.