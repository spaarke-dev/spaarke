# ADR-005: Flat storage model in SharePoint Embedded (SPE)
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
Deep folder hierarchies in document systems produce brittle, path‑based permissions and poor UX. SDAP needs matter‑centric metadata and many‑to‑many associations without nesting complexity.

## Decision
- Use flat storage in SPE; represent hierarchy via metadata and associations in Dataverse.
- Store a single canonical document object with associations to one or more business contexts (e.g., matters).
- Manage permissions via Dataverse UAC and application‑mediated access; SPE remains headless storage.

## Consequences
Positive:
- Simpler synchronization, resilient associations, cleaner access decisions.
- Easier cross‑matter reuse and search.
Negative:
- Requires a robust metadata profile and association management UI.

## Alternatives considered
- Deep folder trees with inherited permissions. Rejected due to permission drift and duplication.
- Multiple physical copies per context. Rejected due to versioning and compliance risks.

## Operationalization
- Document global record + association link records; search index includes association metadata.
- SPE operations executed only via SpeFileStore; no direct user permissions in containers.
- Projection views support fast grids and filters.

## Exceptions
If a specific integration demands fixed folder paths, emit a derived “presentation path” while retaining flat storage and metadata source of truth.

## Success metrics
- Reduced permission drift incidents; faster cross‑context retrieval; simpler audits.
