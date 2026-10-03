---
name: dataverse-autonumber-existing-primary-name-2026-10-02
description: Dataverse AutoNumberFormat facts — convert existing text/primary-name column, fill-only-when-empty, editable, SEQNUM default 1000 + overflow, seed NOT solution-aware, no uniqueness vs manual values, alt-key NULLs not enforced
metadata:
  type: reference
---

## 2026-10-02: Autonumber on existing sprk_matternumber / sprk_projectnumber (primary name)
**Question**: Can `MAT-{SEQNUM:6}` / `PRJ-{SEQNUM:6}` be added to existing populated primary-name text columns while wizards keep supplying their own numbers and the BFF omits it?

**Findings (documented)**:
- Convert existing text column → autonumber is DOCUMENTED (dev page "You can modify an existing format text column to be an autonumber format"; maker field page "can't change the data type except for converting text columns to autonumber columns"). No doc restriction for primary-name columns (community 2023 blog confirms primary column conversion works; repo already ships autonumber primary names: sprk_backgroundjobrun, sprk_notificationoutbox). Existing rows are NOT back-filled (MVP answer, community).
- Default seed = **1000** ("By default, all autonumber sequential values start with 1000") → `{SEQNUM:6}` first value is `001000` unless `SetAutoNumberSeed(Value=1)`. Seed "not included in a solution"; must call SetAutoNumberSeed in EVERY target env (Spaarke per-customer provisioning step). Changing seed via Solution Explorer adds an unmanaged layer (Rational Developer 2026); use API.
- SEQNUM:N is a MINIMUM length ("The number continues to increment beyond the minimum length") → 999999 → 1000000, no error. Gaps documented ("gaps will be present"). "SQL generates the sequential segment guarantees uniqueness" — of the sequence only; NO check vs manually typed values (Nishant 2021, MVP Poggemann).
- UCI: "controls bound to an autonumber column need to explicitly be set as disabled" → editable by default; "If you don't set the initial column value on the form, the value is set only after you save".
- Supplied value on create KEPT / generated only when empty: NOT on Learn; community-only (Nishant "generates a unique number when there is no value specified"; Jonas Rapp MVP uses "-" to suppress numbering; community "will not overwrite the value that has been provided"). Generated value is already visible to Pre-Create (pre-operation) plugins.
- Alt keys: "When NULL values are used in alternate key columns, uniqueness will not be enforced" (maker alt-key page). Index creation fails (Failed status, ReactivateEntityKey) if existing dupes. Chars `/ < > * % & : \ ? +` (dev) / `#` (maker) break GET/PATCH-by-key.
- Not documented: CreateMultiple/ExecuteMultiple/elastic/app-user behaviour (no exclusion stated; elastic unsupported list does not mention autonumber); Quick Create behaviour; explicit-null vs omitted on create.

**Sources**: learn.microsoft.com/power-apps/developer/data-platform/create-auto-number-attributes (ms.date 2022-06-15, updated 2026-02-12); /maker/data-platform/autonumber-fields (ms.date 2019-02-26, updated 2025-08-11); /maker/data-platform/create-edit-field-portal (2026-01-09); /maker/data-platform/define-alternate-keys-reference-records (ms.date 2023-12-01); /developer/data-platform/define-alternate-keys-entity (2026-03-30); webapi/reference/setautonumberseed (2026-09-03) + getnextautonumbervalue + getautonumberseed; nishantrana.me 2021-11-09 + 2021-11-23; jonasr.app/2020/03/anm-unique-seq; therationaldeveloper.com (2026); experience.dynamics.com idea 2020-08-07 (seed transfer, Needs Votes); community.dynamics.com pavanmanideep 2023-05-15.

**Open questions**: live-probe in dev — (a) explicit `null`/`""` on create vs omitted; (b) CreateMultiple; (c) BFF app-user create; (d) Business-Required form validation on primary name (sprk_matternumber is Recommended today).
