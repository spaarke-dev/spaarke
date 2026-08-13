# Task 021 — sprk_navitem deploy result (spaarkedev1)

> **2026-08-13** · env `https://spaarkedev1.crm.dynamics.com` · solution `SpaarkeCore` (unmanaged)

## Deploy: ✅ SUCCESS (verified)
Ran `scripts/Deploy-SprkNavItemEntity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com`.

- **Entity**: `sprk_navitem` created. `OwnershipType = UserOwned` ✅, `PrimaryNameAttribute = sprk_displayname` ✅.
- **Global option sets** (MetadataIds): `sprk_type` `8166c12e-2297-f111-b8dc-70a8a590c51c`, `sprk_source` `3739b12d-2297-f111-b8dc-7ced8ddc4cc6`, `sprk_pagetype` `4fd58e2e-2297-f111-b8dc-7ced8ddc4a05`.
- **Fields** (verify step): sprk_displayname [String], sprk_lastvisited [DateTime], sprk_navitemid [PK], sprk_pagetype [Picklist], sprk_source [Picklist], sprk_targetid [String], sprk_targetlogicalname [String], sprk_type [Picklist], sprk_url [String], sprk_visitcount [Integer] (+ virtual *name fields). Published.
- Acceptance criteria **1 (exists+UserOwned) and 2 (all fields) — MET + verified**.

## Known-issue note (idempotency proved its worth)
First run created the three global option sets then threw on retrieving them by Name — **Dataverse metadata propagation lag** (read immediately after metadata write hit a replica without them). Confirmed transient: a direct GET returned HTTP 200 + MetadataId seconds later. The idempotent re-run `[SKIP]`ped the existing option sets, resolved their IDs, and completed. No code change needed. (Optional hardening for future: add a short retry/delay in `Get-GlobalOptionSet`.)

## Follow-up (operator-gated) — does NOT block task 030
- **Security roles (POML step 2 / criterion 3)**: the signed-in user (System Administrator) already has org-level access, so capture (030) can be built + dev-tested now. Owner-scoped **User-level** CRUD for *end-user* roles is an org role-model decision — **which security role(s) should receive owner-scoped sprk_navitem CRUD?** Pending operator choice. NFR-03 per-user isolation is structurally guaranteed by UserOwned + User-level (not Org-level) privileges once wired.
- **Two-user isolation empirical test (criterion 3)**: needs a second (non-admin) test user; deferred until role wiring + a test account are available. Enforcement mechanism is in place (UserOwned).

## Status
Deploy + verify complete. Security-role assignment to end-user roles + the empirical 2-user isolation check are the remaining sub-items, tracked here; they do not block Wave C (030 capture / 040 NavigatorPane), which is buildable + dev-testable under the current System Administrator context.
