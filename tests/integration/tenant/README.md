# `tests/integration/tenant/**` — tenant-isolation KEEP category

> **Category authority**: [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md)

## What lives here

Integration tests covering **tenant boundary enforcement**. Critical invariant: cross-tenant reads MUST return 404 (not 403), so attackers can't distinguish "exists but forbidden" from "does not exist".

## Deletion-safety rule

KEEP-protected. Deletion requires same-PR replacement.

## Inventory status (2026-06-26)

**1** KEEP-tenant-isolation file identified — flagged as **CRITICAL BACKFILL** in `notes/test-inventory-summary.md`. Spec.md §73 explicitly includes this category but the codebase has only 1 such test today. Backfill is part of the ≥6-month cultural change window (per design.md §257).
