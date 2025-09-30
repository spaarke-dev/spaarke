# Task A — Establish DI and pipeline order in Program.cs

    This task file is **self‑contained**. It embeds prompts, constraints, guardrails, and tests guidance so an autonomous AI agent can complete it without external instructions.

    ---

    ## Objective
    Refactor `Program.cs` to enforce a single, minimal pipeline and module wiring:
- Cross-cutting (ProblemDetails → CORS → RateLimiting → OpenTelemetry)
- Options with `.ValidateOnStart()`
- `AddSpaarkeCore()` (AuthorizationService, rules, RequestCache)
- `AddDocumentsModule()` (endpoints + filters)
- `AddWorkersModule()` (Service Bus + BackgroundService)
- Redis `IDistributedCache`
- Singleton `GraphServiceClient` factory
- Health checks endpoints
Remove any global resource-authorization middlewares; authorization must happen in endpoint filters.

    ## Files to create/edit (expected)
    - `src/api/Spe.Bff.Api/Program.cs`
- Optional DI extension files under `src/api/Spe.Bff.Api/Infrastructure/DI/*`
- Tests under `tests/*` for pipeline/health checks

    ## References (absolute paths)
    - ADR‑001: C:\code_files\spaarke\docs\adr\ADR-001-minimal-api-and-workers.md
- ADR‑008: C:\code_files\spaarke\docs\adr\ADR-008-authorization-endpoint-filters.md
- ADR‑009: C:\code_files\spaarke\docs\adr\ADR-009-caching-redis-first.md
- ADR‑010: C:\code_files\spaarke\docs\adr\ADR-010-di-minimalism.md
- Simplification: C:\code_files\spaarke\docs\guides\SDAP_Architecture_Simplification_Guide.md

    ## Agent Run Loop (execute verbatim)
    1. Open `docs/dev/SDAP_Instructions.md` and this task file.
    2. Open all ADRs listed in **References**.
    3. **Write/adjust tests first** to pin desired behavior.
    4. Implement the **smallest change** to make tests pass.
    5. Run `dotnet format` and analyzers; fix issues.
    6. Run `scripts/adr_policy_check.ps1`; fix violations.
    7. Output **only**: unified git diffs and one commit message.
    8. Print `NEXT: Task B — SpeFileStore facade` and stop.

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
    - One commit message: `"task a: Establish DI and pipeline order in Program.cs (ADR-refs)"`.

    ## Conclusion / Next Task
    When tests and CI are green, print: `NEXT: Task B — SpeFileStore facade`.
