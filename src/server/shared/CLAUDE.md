<!--
Maintainer notes (stripped before Claude reads this file):
- Loads whenever Claude reads or edits a file under src/server/shared/.
- Size target: about 4 KB — a target, not a cap; exceed it when the content is load-bearing and say why in the PR. The previous version (Dec 2025) showed Guard, Result<T>, QueryExtensions, EntityExtensions and a
  DataverseService class that do not exist, a csproj reference in the wrong direction, and a Mock<IServiceClient>
  test that ADR-038 bans (B2). Only describe what is in the code.
- Previous full version: .claude/archive/2026-10-07/modules/server-shared.CLAUDE.md
-->
# src/server/shared — shared .NET libraries

| Library | What it holds | References |
|---|---|---|
| `Spaarke.Dataverse` | Dataverse access: `IDataverseService` plus per-area interfaces (`IDocumentDataverseService`, `IEventDataverseService`, …), `DataverseServiceClientImpl`, `DataverseWebApiClient`, impersonation and access-rights helpers. See its `README.md` | none (base layer) |
| `Spaarke.Core` | Cross-cutting: `Auth/`, `Cache/`, `Utilities/`, `Entities/`, constants. See its `README.md` | `Spaarke.Dataverse` |
| `Spaarke.Scheduling` | The scheduled-job host — `IScheduledJob`, `ScheduledJobHost`, leases, retry policy (ADR-036) | `Spaarke.Core` |
| `Contracts/` | Shared contract types (`SpeContainerBusinessUnitBinding.cs`) | — |

## Binding rules

- **Dependency direction:** `Spaarke.Dataverse` ← `Spaarke.Core` ← `Spaarke.Scheduling` ← `Sprk.Bff.Api`. No shared library references the BFF, and no cycles. Enforced by `tests/Spaarke.ArchTests/LayerDependencyTests.cs`; if a refactor changes the direction, update this table and that test together.
- Changes to `Spaarke.Core` or `Spaarke.Dataverse` follow BFF hygiene — [`.claude/rules/bff-hygiene.md`](../../../.claude/rules/bff-hygiene.md) loads automatically when you edit them (root §10).
- **ADR-010** — DI minimalism: register concretes; an interface only where it is a real seam. The Dataverse `ServiceClient` is a singleton, never per-request.
- Keep each library focused; no "utility" kitchen-sink types. Document public APIs with XML comments.
- Dataverse write paths and record invariants: root §17 "create/update path" trigger (ADR-002, no plugins).

## Tests

`tests/unit/Spaarke.Core.Tests/`, `tests/unit/Spaarke.Scheduling.Tests/`; follow [`tests/CLAUDE.md`](../../../tests/CLAUDE.md). Do not mock `IServiceClient` (ADR-038 B2).
