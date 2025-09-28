# ADR-002: Keep Dataverse plugins thin; no orchestration in plugins
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
Heavy business logic and remote I/O in Dataverse plugins leads to long transactions, service‑protection throttling, opaque failures, and limited observability. We need platform guardrails, not workflow engines, inside Dataverse.

## Decision
- Plugins perform only synchronous validation, denormalization/projection, and audit stamping.
- No HTTP/Graph calls or long‑running logic inside plugins.
- Orchestration resides in the BFF/API (Minimal API) and BackgroundService workers.

## Consequences
Positive:
- Short, reliable transactions and fewer service‑protection issues.
- Unified retries, telemetry, correlation, and error handling in the BFF/workers.
Negative:
- Slightly more code in the BFF to coordinate multi‑step operations.

## Alternatives considered
- Complex plugins and custom workflow activities. Rejected due to observability, scale, and ISV deployment risks.

## Operationalization
- Maintain two plugin classes: ValidationPlugin and ProjectionPlugin (each under ~200 LoC; execution p95 < 50 ms).
- If external data is required, emit a Service Bus command; a worker completes the operation and writes back via the API.
- Document these limits in coding standards and the SDAP spec.

## Exceptions
Atomic multi‑row writes without external calls may use a thin Dataverse Custom API invoked by the BFF.

## Success metrics
- Zero plugin‑originated remote I/O.
- No plugin‑originated Dataverse service‑protection limit errors.
- Plugin execution p95 under 50 ms.
