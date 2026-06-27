# `tests/unit/domain/**` — domain-logic KEEP category

> **Category authority**: [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md)
> **Note**: This path is **NEW** as of 2026-06-26 (created from scratch per spec UQ #3).

## What lives here

Pure domain logic unit tests — **calculations, mappings, parsing, serialization, handler-internal orchestration**. No mocks, no DI, no I/O. If a test needs to mock the class-under-test's collaborators, use the integration-first template instead.

## Deletion-safety rule

KEEP-protected. Deletion requires same-PR replacement.

## Inventory status (2026-06-26)

288 KEEP-domain-logic files identified — the LARGEST KEEP category. Bulk move pending.
