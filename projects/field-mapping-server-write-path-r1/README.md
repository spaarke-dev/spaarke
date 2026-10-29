# Field Mapping → Server-Side Write Path (R1)

> **Portfolio**: [Project #1164](https://github.com/spaarke-dev/spaarke/issues/1164) · [Board #2](https://github.com/users/spaarke-dev/projects/2) · Epic [#535](https://github.com/spaarke-dev/spaarke/issues/535) · label `field-mapping-server-write-path-r1` — _registered 2026-10-04; board fields set 2026-10-19 (43 tasks planned)_

**Status**: Initialized 2026-10-19 (`/project-pipeline`). No implementation yet.
**Branch**: `work/field-mapping-server-write-path-r1` · **Worktree**: `c:/code_files/spaarke-wt-field-mapping-server-write-path-r1`

## Quick Links

- [plan.md](plan.md) · [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) · [spec.md](spec.md) · [design.md](design.md)
- Evidence base: [notes/design-review-2026-10-19.md](notes/design-review-2026-10-19.md)
- Project operating manual: [CLAUDE.md](CLAUDE.md) · State: [current-task.md](current-task.md)

## Overview

This project makes the BFF the single owner of what a new record inherits and who owns it. One `RecordCreationService`
applies field mapping (fill-only), the search-index default, the record-first owner and the core-ancestor stamp to
every record the product creates. A scheduled repair pass covers records created outside the product.

## Problem Statement

Field mapping (parent → child value inheritance, configured in `sprk_fieldmappingprofile` / `sprk_fieldmappingrule`)
runs in three engines:

- The enforcing engine runs **in the browser**, in 8 wizards only, and it overwrites values the user typed.
- Office quick-create runs a second engine (C#) for Matter and Project only.
- `/push` runs a third (Copy-only) engine.

Two BFF create cores (`OwnedChildWrite`, `RecordCreationService`) and eight bespoke create paths apply the invariants
differently, so the same Project gets a different owner depending on where it was created. Chat creates skip the I-1
stamp. Matter and Project wizards still create user-owned rows through `Xrm.WebApi`.

## Proposed Solution

- **W1** One canonical engine (`CreateTimeFieldMapping`, promoted), fill-only, with a mapping-source resolver, and a
  cleaned-up configuration model.
- **W2** One `RecordCreationService`, built on `OwnedChildWrite`'s caller-authorization model. It has Create and
  Repair modes, caller and system principals, a per-table rules registry, and typed refusals.
- **W3** Every product creator moves onto it: 10 wizards, Office, typed routes, invoice extraction and system
  creators. The client engine is deleted.
- **W4** `/push` runs on the canonical Copy logic, selected by `sprk_syncmode`.
- **W5** The native UI is redirected (ribbon "New", quick create off, OOB Assign hidden), and a forward-only scheduled
  repair pass covers records created outside the product.
- **W6** A per-table ownership standard, guards, docs and dead-code removal.

## Scope

**In scope**: W1–W6 above (spec.md FR-01…FR-28, NFR-01…NFR-08).

**Out of scope**:
- external-portal creates (external-access-r3 adopts the mapping step itself);
- any historic backfill (forward-only);
- `/push` performance (#2098);
- removing `prvAssign` from roles (follows a UAC-r2 audit);
- a Dataverse change-signal channel;
- I-2 secure-isolation logic itself;
- field-mapping table schema changes;
- wizard UI redesign;
- a runtime kill switch.

## Graduation Criteria

- [ ] One engine: no `ApplyMappingRule` Copy logic, no `applyFieldMappings`, no `FieldMappingHandler` (grep + client
      arch guard).
- [ ] One create service: every in-scope create path uses `RecordCreationService` or is a named exception (census
      guard with a negative control).
- [ ] Mapped on first load from every surface family: wizard, Office, chat, decision, system (integration tests + live
      Matter→Event check).
- [ ] Fill-only: caller values survive (Report Card regression test).
- [ ] Same owner everywhere; no user-owned Matter/Project after go-live (cross-surface owner test + live check).
- [ ] Mapped lookups authorized: an unauthorized mapped lookup is dropped with a warning; a typed one refuses
      (#1614/#1617 tests).
- [ ] Push parity and fixed error text (parity test + #1437 test).
- [ ] Outside-product rows repaired within ≤ 10 min, fill-only, idempotent, forward-only (data-mutation test + live
      import of 5 rows).
- [ ] Ownership standard published with every exception assessed; registry I-3/I-5 done (doc review).
- [ ] Dead code removed; OOB BU→Matter map removed; the native-surface `-Verify` passes on dev (grep + script).
