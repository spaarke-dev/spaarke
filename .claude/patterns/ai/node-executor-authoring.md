# Node-Executor Authoring (Playbook Node Pipeline)

> **⛔ Frozen engine.** This pattern is for the node-graph engine, which takes **no new capability** (ADR-039). Use it only to maintain the existing Insights pipelines. **That does not mean playbooks are unused**: a new playbook is Actions + Bindings, with an `ICodedWorkflow` for several steps. See `docs/architecture/playbook-architecture.md` and CAPABILITY-INDEX rows 16 and 34.

> **Last Reviewed**: 2026-11-15
> **Reviewed By**: reuse-governance-tightening-r1 (ISS-001: `ActionType` → `ExecutorType` rename, commit `d2f768e8d5`); previously spaarke-platform-foundations-r3 task 066
> **Status**: Verified
> **Source**: R3 spec FR-3H3.5 · ADR-013 · ADR-010

## When
Use when adding a new playbook node executor (new `ExecutorType` value) — read this BEFORE writing the executor or its tests. Covers the Singleton-with-Scoped-dependency DI pattern + canvas↔server mapping symmetry.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` — contract (`SupportedExecutorTypes`) + `ExecutorType` enum (add your new value sorted by category)
2. `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs` — canonical Singleton-depends-on-Scoped via `IServiceScopeFactory.CreateScope()` (worked example)
3. `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` — R3 worked example: in-process call to a Scoped resolver service; binds IDs to `OutputVariable`
4. `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs` — closest analog for "fetch IDs → bind to OutputVariable" shape; mirror its `Validate()` + `ExecuteAsync()` signatures
5. `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs` — server-side canvas-type→`ExecutorType` mapping in `MapCanvasTypeToActionType` (the method kept its old name; you MUST add an arm here)

## Constraints
- **ADR-010**: executor is `Singleton`; if you depend on `Scoped` services (Dataverse client, OBO token cache), inject `IServiceScopeFactory` and `CreateScope()` per `ExecuteAsync` invocation. NEVER inject a Scoped service directly into a Singleton executor.
- **ADR-013**: extend the existing node-executor framework; do NOT invent new pipeline primitives, new context shapes, or new output-binding mechanisms.
- **`bff-extensions.md` §A**: pre-merge checklist (publish-size delta + CVE scan + test update) applies to every new executor.
- **Canvas→server mapping**: `NodeService.MapCanvasTypeToActionType` is the only mapping now. The client `playbookNodeSync.ts` and its CI drift test were removed when PlaybookBuilder was cut back to the catalog editor (`df874a910`, `485f6a65a`), so nothing checks this arm for you — add it by hand.

## Key Rules
- `public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[] { ExecutorType.YourNew };` — mirror existing executors exactly (e.g. `QueryDataverseNodeExecutor`)
- `Validate(NodeConfig)` returns the framework's standard `NodeValidationResult` — return errors for missing required config keys; do NOT throw
- `ExecuteAsync(NodeExecutionContext, CancellationToken)`: `using var scope = _scopeFactory.CreateScope();` then `scope.ServiceProvider.GetRequiredService<TScoped>()`; bind output via the framework's standard binding API (read QueryDataverseNodeExecutor)
- Caller identity: extract via the SAME convention QueryDataverseNodeExecutor / AgentServiceNodeExecutor use; do NOT invent a new identity-extraction path
- Register in `NodeExecutorRegistry` AND in the DI module (mirror the AnalysisServicesModule sibling registration block exactly)
- **Tests** (mandatory per `bff-extensions.md` §F test-update obligation): `Validate_Missing*_ReturnsError` cases, `ExecuteAsync_HappyPath_BindsOutput`, `ExecuteAsync_UsesScopePerInvocation` (assert `IServiceScopeFactory.CreateScope()` called once per execute)

## Canvas→Server Mapping Checklist
Before opening the PR (no automated drift test exists since `485f6a65a`):
- [ ] Server `Services/Ai/NodeService.cs` has the `ExecutorType` + canvas-type arms (MapCanvasTypeToNodeType + MapCanvasTypeToActionType)
