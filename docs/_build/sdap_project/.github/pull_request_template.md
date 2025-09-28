
# Summary
<one-liner of what changed>

## ADRs referenced
- [ ] ADR-001
- [ ] ADR-002
- [ ] ADR-003
- [ ] ADR-004
- [ ] ADR-005
- [ ] ADR-006
- [ ] ADR-007
- [ ] ADR-008
- [ ] ADR-009
- [ ] ADR-010

## Checklist
- [ ] Tests added/updated (unit + integration)
- [ ] Protected endpoints have authorization filters
- [ ] No Graph SDK types outside SpeFileStore or Infrastructure/Graph
- [ ] No Azure Functions/Durable Functions
- [ ] Redis-only cross-request caching; no IMemoryCache across requests
- [ ] All I/O methods accept CancellationToken
- [ ] ProblemDetails used for error shaping
- [ ] dotnet format + analyzers clean
- [ ] `scripts/adr_policy_check.ps1` passes
