# Node-Executor Authoring (Playbook Node Pipeline)

> **Last Reviewed**: 2026-06-21
> **Reviewed By**: spaarke-platform-foundations-r3 task 066
> **Status**: Verified
> **Source**: R3 spec FR-3H3.5 · ADR-013 · ADR-010

## When
Use when adding a new playbook node executor (new `ActionType` value) — read this BEFORE writing the executor or its tests. Covers the Singleton-with-Scoped-dependency DI pattern + canvas↔server mapping symmetry.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` — contract + `ActionType` enum (add your new value sorted by category)
2. `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs` — canonical Singleton-depends-on-Scoped via `IServiceScopeFactory.CreateScope()` (worked example)
3. `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` — R3 worked example: in-process call to a Scoped resolver service; binds IDs to `OutputVariable`
4. `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs` — closest analog for "fetch IDs → bind to OutputVariable" shape; mirror its `Validate()` + `ExecuteAsync()` signatures
5. `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs` — server-side canvas-type→ActionType mapping (you MUST add an arm here)
6. `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` + `types/playbook.ts` — client-side mapping + ActionType enum (you MUST add matching entries)

## Constraints
- **ADR-010**: executor is `Singleton`; if you depend on `Scoped` services (Dataverse client, OBO token cache), inject `IServiceScopeFactory` and `CreateScope()` per `ExecuteAsync` invocation. NEVER inject a Scoped service directly into a Singleton executor.
- **ADR-013**: extend the existing node-executor framework; do NOT invent new pipeline primitives, new context shapes, or new output-binding mechanisms.
- **`bff-extensions.md` §A**: pre-merge checklist (publish-size delta + CVE scan + test update) applies to every new executor.
- **Canvas↔server symmetry**: every canvas type in `playbookNodeSync.ts` MUST have a matching arm in `NodeService.cs` (and vice versa). The CI drift test (`tests/integration/Sprk.Bff.Api.IntegrationTests/Playbooks/CanvasServerMappingDriftTests.cs`, R3 task 065) catches missing pairs at build time.

## Key Rules
- `SupportedActionTypes = new[] { ActionType.YourNew }` — mirror existing executors exactly
- `Validate(NodeConfig)` returns the framework's standard `NodeValidationResult` — return errors for missing required config keys; do NOT throw
- `ExecuteAsync(NodeExecutionContext, CancellationToken)`: `using var scope = _scopeFactory.CreateScope();` then `scope.ServiceProvider.GetRequiredService<TScoped>()`; bind output via the framework's standard binding API (read QueryDataverseNodeExecutor)
- Caller identity: extract via the SAME convention QueryDataverseNodeExecutor / AgentServiceNodeExecutor use; do NOT invent a new identity-extraction path
- Register in `NodeExecutorRegistry` AND in the DI module (mirror the AnalysisServicesModule sibling registration block exactly)
- **Tests** (mandatory per `bff-extensions.md` §F test-update obligation): `Validate_Missing*_ReturnsError` cases, `ExecuteAsync_HappyPath_BindsOutput`, `ExecuteAsync_UsesScopePerInvocation` (assert `IServiceScopeFactory.CreateScope()` called once per execute)

## Canvas↔Server Drift Checklist
Before opening the PR, verify ALL three sides updated:
- [ ] Client `types/playbook.ts` ActionType enum has your new value
- [ ] Client `services/playbookNodeSync.ts` has the canvas-type case (both canvas→config + config→canvas mappings)
- [ ] Server `Services/Ai/NodeService.cs` has the ActionType + canvas-type arms (MapCanvasTypeToNodeType + MapCanvasTypeToActionType)
- [ ] CI drift test passes locally (`dotnet test --filter "FullyQualifiedName~CanvasServerMappingDriftTests"`)
