# `tests/integration/data-mutation/**` — data-mutation KEEP category

> **Category authority**: [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md)

## What lives here

Integration tests covering **writes, transactions, rollback semantics**. Includes Dataverse row creation/update/delete, SPE file commits, audit-log writes, and rollback-on-failure scenarios.

## Deletion-safety rule

KEEP-protected. Deletion requires same-PR replacement covering the same write scenario.

## Inventory status (2026-06-26)

45 KEEP-data-mutation files identified. Bulk move pending.
