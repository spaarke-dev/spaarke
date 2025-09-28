# ADR-010: Dependency Injection minimalism and feature modules
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
The BFF accumulated 20+ service registrations driven by interface sprawl, duplicate clients, and helper services. This hurts readability and invites drift.

## Decision
- Register **concretes** unless a genuine seam exists. Keep only two seams in core: `IAccessDataSource` and the collection `IEnumerable<IAuthorizationRule>`.
- Use **feature-module** extension methods (e.g., `AddSpaarkeCore`, `AddDocumentsModule`, `AddWorkersModule`) to group registrations.
- One typed `HttpClient` per upstream (Dataverse). `GraphServiceClient` is built once with retry/correlation and injected as a singleton.
- Use Options (`AddOptions<T>().Bind().ValidateOnStart()`) for configuration.
- Use `IDistributedCache` (Redis) and a small `RequestCache`; remove hybrid cache services.

## Consequences
Positive:
- DI section drops to ~15 lines; wiring becomes obvious.
- Easier for AI agents and reviewers to see the whole composition at a glance.
Negative:
- Some unit tests may need refactoring to depend on the remaining seams.

## Alternatives considered
- Registering interfaces for every service. Rejected as ceremony without payoff.

## Operationalization
- Replace excess registrations with a minimal block in `Program.cs`.
- Introduce `ServiceCollection` extensions for core, documents (rules), and workers.
- Remove `IUacService`, `IDataverseSecurityService`, `IResourceStore`, cache wrapper services, and duplicate HttpClients.

## Exceptions
Add interfaces only for process boundaries, multiple runtime implementations, or external extension points — and record an ADR when doing so.

## Success metrics
- DI registrations ≤ ~15 non-framework lines.
- Build, tests, and workers start; no controllers inject Graph directly.
