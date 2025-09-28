# ADR-004: Async job contract and uniform processing
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
Ad‑hoc background processing patterns cause inconsistent retries, missing idempotency, and hard‑to‑diagnose failures. We need a single, disciplined approach across all async work.

## Decision
- Define one Job Contract with JobId, JobType, SubjectId, CorrelationId, IdempotencyKey, Attempt, MaxAttempts.
- Implement BackgroundService workers using ServiceBusProcessor per JobType.
- Enforce idempotent handlers with centralized Polly retry policies and poison‑queue on exhaustion.
- Persist outcomes and emit JobOutcome events for observability.

## Consequences
Positive:
- Predictable error handling and recovery; simpler reasoning about back‑pressure and scaling.
Negative:
- Slightly more explicit plumbing compared to Functions bindings, but consistent and testable.

## Alternatives considered
- Durable Functions orchestration. Rejected due to host fragmentation and additional complexity.

## Operationalization
- Topic/subscription naming convention by JobType; per‑handler idempotency keys.
- Centralized correlation propagation; structured logging and metrics for queue depth and age.
- Health checks for worker liveness.

## Exceptions
None anticipated; if third‑party triggers are required, introduce a dedicated adapter that still pushes to the same Job Contract and processing pipeline.

## Success metrics
- Low rate of duplicate processing; bounded retries; clear poison‑queue handling.
- Observable queue health and processing latency.
