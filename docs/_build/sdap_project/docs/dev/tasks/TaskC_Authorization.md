# Task C — Introduce AuthorizationService, rules, and endpoint filters

    This task file is **self‑contained**. It embeds prompts, constraints, guardrails, and tests guidance so an autonomous AI agent can complete it without external instructions.

    ---

    ## Objective
    Add `AuthorizationService` and a set of small `IAuthorizationRule` units.
Create Minimal API endpoint filters that load UAC inputs from `IAccessDataSource`, evaluate rules, and block with `ProblemDetails` on deny.

    ## Files to create/edit (expected)
    - `src/shared/Spaarke.Core/Auth/AuthorizationService.cs`
- `src/shared/Spaarke.Core/Auth/Rules/*.cs`
- `src/api/Spe.Bff.Api/Api/Filters/*AuthorizationFilter.cs`
- `src/shared/Spaarke.Dataverse/IAccessDataSource.cs`
- Unit tests for rules and filters; integration tests on endpoints

    ## References (absolute paths)
    - ADR‑003: C:\code_files\spaarke\docs\adr\ADR-003-lean-authorization-seams.md
- ADR‑008: C:\code_files\spaarke\docs\adr\ADR-008-authorization-endpoint-filters.md
- ADR‑009 (inputs caching): C:\code_files\spaarke\docs\adr\ADR-009-caching-redis-first.md

    ## Agent Run Loop (execute verbatim)
    1. Open `docs/dev/SDAP_Instructions.md` and this task file.
    2. Open all ADRs listed in **References**.
    3. **Write/adjust tests first** to pin desired behavior.
    4. Implement the **smallest change** to make tests pass.
    5. Run `dotnet format` and analyzers; fix issues.
    6. Run `scripts/adr_policy_check.ps1`; fix violations.
    7. Output **only**: unified git diffs and one commit message.
    8. Print `NEXT: Task D — Caching (request + Redis)` and stop.

    ## Hard Guardrails (must pass)
    - No Azure Functions/Durable Functions.
    - SPE/Graph calls only inside `SpeFileStore`; no Graph SDK types outside.
    - Authorization only via endpoint filters + `AuthorizationService` + small rules.
    - Redis for cross‑request inputs; never cache authorization decisions.
    - Every I/O method accepts `CancellationToken`.
    - Errors shaped as `ProblemDetails` with correlation ID.

    ## Implementation Steps (minimal path)
    - Follow the **Objective**; keep classes small and explicit.
    - Prefer concrete classes; only introduce an interface for a real seam.
    - Use guard clauses and early returns; avoid nesting.

    ## Testing (write first)
    - Unit tests covering new rule/behavior or seam contract.
    - Integration tests for endpoint paths touched.
    - Deterministic; no sleeps; assert `ProblemDetails` for error paths.

    ## CI gates to keep green
    - `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`
    - `scripts/adr_policy_check.ps1`

    ## Deliverables (output format)
    - Unified git diff for changed files only.
    - One commit message: `"task c: Introduce AuthorizationService, rules, and endpoint filters (ADR-refs)"`.

    ## Conclusion / Next Task
    When tests and CI are green, print: `NEXT: Task D — Caching (request + Redis)`.
