# ADR-007: SPE storage seam minimalism (single focused facade)
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
SharePoint Embedded (SPE) is integrated via Microsoft Graph, which already provides a high-level abstraction. A generic `IResourceStore` layered above Graph adds indirection without value. SDAP does not plan to support a second storage provider.

## Decision
- Use a single, focused **SPE storage facade** named `SpeFileStore` that encapsulates all Graph/SPE calls needed by SDAP.
- Do **not** create a generic `IResourceStore` interface. If a seam is required for tests, introduce the interface later.
- The facade exposes only SDAP DTOs (`UploadSessionDto`, `FileHandleDto`, `VersionInfoDto`) and never returns Graph SDK types.
- Configure the Graph SDK `RetryHandler` and correlation logging inside the facade once.

## Consequences
Positive:
- Eliminates unnecessary abstractions and duplicated retry/telemetry code.
- Isolates Graph changes to a single class; callers remain stable.
Negative:
- The facade is SPE-coupled (by design). If a second provider is ever required, a minimal `IFileStore` may be introduced then.

## Alternatives considered
- `IResourceStore` with multiple thin adapters. Rejected as premature generalization and added ceremony.

## Operationalization
- Replace `IResourceStore`/`SpeResourceStore` and thin `ISpeService`/`IOboSpeService` pass-throughs with `SpeFileStore`.
- Inject `GraphServiceClient` once into `SpeFileStore`; enable SDK retry and add a delegating handler for correlation.
- Ensure controllers/handlers call the facade and never reference Graph types.

## Exceptions
None at present. If a second provider becomes a real requirement, introduce `IFileStore` with methods limited to the operations SDAP actually needs.

## Success metrics
- Reduced class count and DI registrations.
- No Graph types visible above the facade.
- Stable storage behavior under throttling; consistent audit logging of Graph request IDs.
