# Task E — Async job contract and BackgroundService worker

    This task file is **self‑contained**. It embeds prompts, constraints, guardrails, and tests guidance so an autonomous AI agent can complete it without external instructions.

    ---

    ## Objective
    Implement a single `BackgroundService` that consumes Service Bus messages with the ADR‑004 job envelope (JobId, JobType, SubjectId, CorrelationId, IdempotencyKey, Attempt, MaxAttempts).
Handlers must be idempotent; use Polly retries; send to poison queue on exhaustion; add health checks for queue depth/age where feasible.

    ## Files to create/edit (expected)
    - `src/api/Spe.Bff.Api/Services/BackgroundServices/JobProcessor.cs`
- Service Bus wiring in DI
- Unit tests for idempotency; integration tests simulating retries/poisoning

    ## References (absolute paths)
    - ADR‑004: C:\code_files\spaarke\docs\adr\ADR-004-async-job-contract.md

    ## Agent Run Loop (execute verbatim)
    1. Open `docs/dev/SDAP_Instructions.md` and this task file.
    2. Open all ADRs listed in **References**.
    3. **Write/adjust tests first** to pin desired behavior.
    4. Implement the **smallest change** to make tests pass.
    5. Run `dotnet format` and analyzers; fix issues.
    6. Run `scripts/adr_policy_check.ps1`; fix violations.
    7. Output **only**: unified git diffs and one commit message.
    8. Print `NEXT: Task F — Minimal API endpoint cleanup` and stop.

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
    - One commit message: `"task e: Async job contract and BackgroundService worker (ADR-refs)"`.

    ## Conclusion / Next Task
    When tests and CI are green, print: `NEXT: Task F — Minimal API endpoint cleanup`.
