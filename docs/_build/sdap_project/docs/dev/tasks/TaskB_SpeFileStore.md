# Task B — Create SpeFileStore facade and retire split services

    This task file is **self‑contained**. It embeds prompts, constraints, guardrails, and tests guidance so an autonomous AI agent can complete it without external instructions.

    ---

    ## Objective
    Collapse `ISpeService` and `IOboSpeService` into a single `SpeFileStore` facade.
Hide Graph SDK types inside the facade; expose only SDAP DTOs. Configure Polly retries/correlation **once**.
Keep SPE storage flat; associations/hierarchy remain in Dataverse.

    ## Files to create/edit (expected)
    - `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`
- Remove: `ISpeService`, `IOboSpeService` and migrate call sites
- DTOs in `src/api/Spe.Bff.Api/Models/*`
- Unit tests mocking Graph client; ensure no Graph types leak

    ## References (absolute paths)
    - ADR‑007: C:\code_files\spaarke\docs\adr\ADR-007-spe-storage-seam-minimalism.md
- ADR‑005: C:\code_files\spaarke\docs\adr\ADR-005-flat-storage-spe.md
- Code Review: C:\code_files\spaarke\docs\api\Code Review Spe.Bff.Api.md

    ## Agent Run Loop (execute verbatim)
    1. Open `docs/dev/SDAP_Instructions.md` and this task file.
    2. Open all ADRs listed in **References**.
    3. **Write/adjust tests first** to pin desired behavior.
    4. Implement the **smallest change** to make tests pass.
    5. Run `dotnet format` and analyzers; fix issues.
    6. Run `scripts/adr_policy_check.ps1`; fix violations.
    7. Output **only**: unified git diffs and one commit message.
    8. Print `NEXT: Task C — Authorization core and endpoint filters` and stop.

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
    - One commit message: `"task b: Create SpeFileStore facade and retire split services (ADR-refs)"`.

    ## Conclusion / Next Task
    When tests and CI are green, print: `NEXT: Task C — Authorization core and endpoint filters`.
