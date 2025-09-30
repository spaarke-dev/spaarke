# Task D — Caching: RequestCache (scoped) + Redis for inputs

    This task file is **self‑contained**. It embeds prompts, constraints, guardrails, and tests guidance so an autonomous AI agent can complete it without external instructions.

    ---

    ## Objective
    Add a scoped `RequestCache` to collapse duplicate loads per request.
Use Redis `IDistributedCache` for cross‑request caching of inputs (e.g., UAC snapshots) with short TTLs and versioned keys.
Never cache authorization outcomes.

    ## Files to create/edit (expected)
    - `src/shared/Spaarke.Core/Cache/RequestCache.cs`
- `src/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs`
- Wiring in `Program.cs`
- Unit tests for memoization; integration tests for Redis TTL behavior

    ## References (absolute paths)
    - ADR‑009: C:\code_files\spaarke\docs\adr\ADR-009-caching-redis-first.md
- Simplification: C:\code_files\spaarke\docs\guides\SDAP_Architecture_Simplification_Guide.md

    ## Agent Run Loop (execute verbatim)
    1. Open `docs/dev/SDAP_Instructions.md` and this task file.
    2. Open all ADRs listed in **References**.
    3. **Write/adjust tests first** to pin desired behavior.
    4. Implement the **smallest change** to make tests pass.
    5. Run `dotnet format` and analyzers; fix issues.
    6. Run `scripts/adr_policy_check.ps1`; fix violations.
    7. Output **only**: unified git diffs and one commit message.
    8. Print `NEXT: Task E — Async job contract and worker` and stop.

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
    - One commit message: `"task d: Caching: RequestCache (scoped) + Redis for inputs (ADR-refs)"`.

    ## Conclusion / Next Task
    When tests and CI are green, print: `NEXT: Task E — Async job contract and worker`.
