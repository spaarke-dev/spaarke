
# Summary
<one-liner of what changed>

## ADRs referenced
- [ ] ADR-001: Minimal API + BackgroundService (no Azure Functions)
- [ ] ADR-002: Keep Dataverse plugins thin
- [ ] ADR-003: Lean authorization seams
- [ ] ADR-004: Async job contract
- [ ] ADR-005: Flat storage in SPE
- [ ] ADR-006: Prefer PCF over web resources
- [ ] ADR-007: SPE storage seam minimalism (Graph isolation)
- [ ] ADR-008: Authorization endpoint filters
- [ ] ADR-009: Caching Redis-first
- [ ] ADR-010: DI minimalism
- [ ] ADR-011: Dataset PCF over subgrids
- [ ] ADR-012: Shared component library

## Checklist
- [ ] Tests added/updated (unit + integration)
- [ ] Protected endpoints have authorization filters (ADR-008)
- [ ] No Graph SDK types outside SpeFileStore or Infrastructure/Graph (ADR-007)
- [ ] No Azure Functions/Durable Functions (ADR-001)
- [ ] Redis-only cross-request caching; no IMemoryCache across requests (ADR-009)
- [ ] Expensive resources registered as Singleton (ADR-010)
- [ ] All I/O methods accept CancellationToken
- [ ] ProblemDetails used for error shaping
- [ ] dotnet format + analyzers clean
- [ ] ADR validation passes: `dotnet test tests/Spaarke.ArchTests/`
- [ ] Interactive ADR check: `/adr-check` (Claude Code skill)
