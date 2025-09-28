# ADR-001: Standardize on Minimal API + BackgroundService; do not use Azure Functions
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
SDAP requires a single, predictable runtime for synchronous request/response and asynchronous jobs. Mixing Azure Functions with ASP.NET Core introduces duplicated cross‑cutting concerns (auth, retries, correlation, ProblemDetails), inconsistent identity flows, cold‑start variance, and split debugging/deployment pipelines.

## Decision
Run SDAP on a single ASP.NET Core App Service:
- Minimal API for all synchronous endpoints.
- BackgroundService workers subscribed to Azure Service Bus for asynchronous jobs.
- Do not use Azure Functions or Durable Functions.

## Consequences
Positive:
- One middleware pipeline for authentication, authorization, correlation, retry policies, and ProblemDetails.
- Simplified local development and deployment, easier debugging and observability.
- Predictable performance and instance warmup behavior.
Negative:
- Loss of Functions bindings ergonomics; explicit Service Bus SDK usage and schedulers are required.
- Responsibility to implement durable, idempotent job handling rests with us.

## Alternatives considered
- Azure Functions (HTTP/Service Bus triggers): convenient bindings but duplicate cross‑cutting concerns and add operational complexity.
- Durable Functions: powerful orchestration, but increases cognitive load, lock‑in, and host fragmentation.

## Operationalization
- Program.cs registers Minimal API endpoints, ServiceBusClient, HttpClient policies, ProblemDetails, and BackgroundService workers.
- Workers use Azure.Messaging.ServiceBus ServiceBusProcessor with Polly retries and idempotent handlers.
- Health probe exposed at /healthz; App Insights/Serilog for structured logging.
- Design documents replace “Function App / Triggers” with “App Service (Minimal API + Workers)”.

## Exceptions
Permitted only for extreme timer density or multi‑day orchestrations that cannot be reasonably modeled as queued steps. Any exception requires an ADR addendum and an explicit runtime review.

## Success metrics
- Reduced MTTR; single OpenAPI surface.
- Stable p95/p99 latency and cold‑start characteristics.
- No duplicate retry stacks or conflicting identity flows.
