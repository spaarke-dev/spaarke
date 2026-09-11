# Task 097 — every external grant carries an expiry

> **Date**: 2026-09-10 (session 7) · **Spec**: FR-33 (a) · **Design**: `notes/decisions/external-grant-expiry-mandatory.md` §9–§12
> **Unblocks**: 098, 099, 100, 101

## Owner decision that reshaped the task — "Server fills +90" (2026-09-10, session 7)

The POML originally made `ExpiryDate` a **required request field** (400 when absent). Before starting, the
client code was checked, and **no client sends an expiry**:

| Surface | Endpoint(s) | Sends an expiry? |
|---|---|---|
| `AccessGrantModal` (Manage Access) | `/grant`, `/invite-and-grant` | no — posts `{recordType, recordId, accessLevel, email}` |
| `TrackingFieldTrio` PCF | `/grant`, `/invite-and-grant` | no |
| external SPA `InviteUserDialog` | `/invite` | no — `expiryDate?` is typed in `bff-client.ts`, never set |

A 400 would have broken every sharing surface, and only the modal is scheduled to gain a picker (099).
Offered three options; the owner chose **a server default**: an absent expiry **keeps the grant's existing
expiry, else becomes today + 90**; a past one is still rejected. Required in the **stored data**, not in the
request. Spec FR-33, the decision record, the POML and TASK-INDEX were amended in their own commit.

## Step 0 findings (POML premises checked against code)

- **`/invite` writes no grant.** It only onboards (resolve-or-create Contact + CIAM account); the grant is
  written by `/grant` or `/invite-and-grant`. The POML's "all three write endpoints" was wrong for `/invite`:
  there is nothing to default there, and its `ExpiryDate` is ignored. (Task-file error #10 for this project.)
- **Escalation trigger 1 does NOT fire.** `DelegationRuleFilter.cs:223` builds a `GrantAccessRequest` with
  `ExpiryDate: null` only to call `ResolveGrantRoot` — target resolution for the authorization check, not a
  write. Left unchanged, and pinned by a test.
- **Escalation trigger 2 does NOT fire.** The read filter's `sprk_expiresdate eq null` clause is unchanged.
  After the backfill no active grant has a null expiry, so flipping null → expired would change nothing today —
  but it stays the owner's call, as the POML requires.
- **Every grant write goes through one core** — `GrantExternalAccessEndpoint.CreateGrantAsync`, shared by
  `/grant` and `/invite-and-grant` — so the default lives there, once.
- **The read filter's "today" is the UTC date** (`ExternalParticipationService.TodayUtc`). The write path uses
  the same calendar, so "expires today" written means "live today" read.

## What changed

| File | Change |
|---|---|
| `Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs` | `DefaultExpiryDays = 90` (a constant, not a setting), `TodayUtc(TimeProvider)`, `DefaultExpiry(today)` |
| `Api/ExternalAccess/GrantExternalAccessEndpoint.cs` | handler takes `TimeProvider`; `ValidateRequestedExpiry` → 400 `sdap.access.grant.expiry_in_past`; `CreateGrantAsync(today)`: match path keeps an existing expiry, defaults only an unbounded one; create path defaults; the task-023 expired-row check uses the same `today` (was `DateTime.UtcNow`) |
| `Api/ExternalAccess/InviteAndGrantExternalUserEndpoint.cs` | validates the expiry **before onboarding** — a refused request leaves no Contact / CIAM account |
| `Dtos/GrantAccessRequest.cs`, `Dtos/InviteExternalUserRequest.cs` | docs; the latter said "No expiry if not specified" — true until now, and the reason FR-33 exists |
| `Infrastructure/DI/ExternalAccessModule.cs` | `TryAddSingleton(TimeProvider.System)` — task 072's convention, so the grant routes don't depend on another module |

Clients: **unchanged**. The 409 "matched an EXPIRED row, no new expiry" behaviour (task 023) is unchanged —
an expired grant keeps its date and is reported, never silently renewed.

## Tests

| KEEP path | File | What it proves |
|---|---|---|
| `tests/integration/auth/` | `GrantLifecycleCharacterizationTests` | core rule on `CreateGrantAsync`: a new grant with no expiry → today + 90; an unbounded grant re-granted with no expiry → today + 90; a longer existing expiry is **not shortened** (twin of the existing test proving a shorter one is **not extended**). The class's 7 wall-clock dates were replaced by a fixed `Today`, since the core now takes `today` as a parameter |
| `tests/integration/contract/` | `ExternalAccessContractTests` | through HTTP, on **both** grant routes: a past expiry → 400 + `sdap.access.grant.expiry_in_past` and **nothing written** (on invite-and-grant: no Contact, no CIAM bind); expiry = today → accepted and stored; no expiry → stored as today + 90. Fixture gained a `FakeTimeProvider` (fixed 2026-09-10) and create-payload capture |
| `tests/integration/auth/` | `DelegationRuleCharacterizationTests` | pins escalation trigger 1's answer: with Write, a past-expiry body passes the delegation gate and gets the **handler's** 400; the read-only twin still gets the **gate's** 403 |

## Perturbation (mandatory — committed first, each reverted with `git checkout`)

| # | Perturbation | Result |
|---|---|---|
| P1 | Remove the default on both paths (an absent expiry is stored as null again) | **4 fail** — exactly the four default tests: `CreateGrant_NewGrantWithNoExpiry_…`, `Upsert_NoExpiryOnAnUnboundedExistingGrant_…`, `PostGrant_WithNoExpiry_…`, `InviteAndGrant_WithNoExpiry_…` |
| P2 | Neutralise the past-date check | **4 fail** — the two contract past-date tests and the two delegation pins |

(A first P1 attempt replaced the create-path expression with a bare `;` and failed to COMPILE — a broken
perturbation, not a result. Redone with a per-path replacement; the numbers above are from the corrected run.)

## Backfill (LIVE DEV DATA — owner-authorised in the POML)

**Before-state recorded here BEFORE any write.** Queried 2026-09-11 01:14 UTC against
`https://spaarkedev1.crm.dynamics.com` (Web API, `az` token — the Dataverse MCP server was down this session):

- active `sprk_externalrecordaccess` rows: **28**
- **active with `sprk_expiresdate` = null: 25** ← the backfill targets (prior value **null** for every one)
- inactive with null expiry: 0 (inactive rows are not touched in any case)

**Backfill date: 2026-09-11 (UTC).** Value written: **today + 90 = 2026-12-10** (Date Only).
🔴 **Task 100 (reminders) must be live by 2026-11-10** — the first reminder fires 30 days before expiry, so
after that date these grants' first reminders are silently missed.

**Revert**: PATCH `sprk_expiresdate` back to `null` on exactly these 25 ids (every prior value was null).

| # | `sprk_externalrecordaccessid` | grantee | root |
|---|---|---|---|
| 1 | `9ec4e19c-5aad-f111-aaab-000d3a9cc3c2` | contact `a9e5dfbf-5f19-f111-8343-7ced8d1dc988` | matter `34625b38-68a5-f111-aaad-7ced8ddc4cc6` |
| 2 | `6f54531a-7a96-f111-b8db-3833c5e5d030` | org `67577f8c-4301-f111-8407-7ced8d1dc988` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 3 | `abf5560d-8296-f111-b8db-3833c5e5d030` | contact `52bb55e7-9d15-f111-8343-7c1e520aa4df` | matter `2444af6d-e1f2-f011-8406-7ced8d1dc988` |
| 4 | `87c541ac-9196-f111-b8db-3833c5e5d030` | contact `394fda9f-ab95-f111-b8dc-7ced8ddc4cc6` | project `7524864d-3660-f111-ab0b-70a8a59455f4` |
| 5 | `6f75adfb-9196-f111-b8db-3833c5e5d030` | contact `394fda9f-ab95-f111-b8dc-7ced8ddc4cc6` | work assignment `6838fe6c-0564-f111-ab0c-7ced8ddc4a05` |
| 6 | `0a9e4a50-c195-f111-b8dc-70a8a590c51c` | contact `394fda9f-ab95-f111-b8dc-7ced8ddc4cc6` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 7 | `5ce82df8-c295-f111-b8dc-70a8a590c51c` | contact `52bb55e7-9d15-f111-8343-7c1e520aa4df` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 8 | `6a5625b5-c395-f111-b8dc-70a8a590c51c` | contact `8cb95c16-e974-f111-ab0e-7ced8ddc4a05` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 9 | `0452ab4b-c895-f111-b8dc-70a8a590c51c` | contact `394fda9f-ab95-f111-b8dc-7ced8ddc4cc6` (firm `67577f8c-…`) | matter `2444af6d-e1f2-f011-8406-7ced8d1dc988` |
| 10 | `07a1957f-c995-f111-b8dc-70a8a590c51c` | contact `8e9918a9-9021-f111-88b5-7c1e520aa4df` | matter `2444af6d-e1f2-f011-8406-7ced8d1dc988` |
| 11 | `24d98312-bc21-f111-88b5-7ced8d1dc988` | contact `2e419a4f-010d-f111-8342-7ced8d1dc988` | project `b12496d1-dff7-f011-8406-7c1e520aa4df` |
| 12 | `4da67e3c-ca43-f111-bec7-7ced8d1dc988` | contact `8e9918a9-9021-f111-88b5-7c1e520aa4df` | project `b12496d1-dff7-f011-8406-7c1e520aa4df` |
| 13 | `b606d962-74ac-f111-aaab-7ced8d21a9d7` | contact `2e419a4f-010d-f111-8342-7ced8d1dc988` | matter `0d8df610-aa57-f111-a824-3833c5d9bcab` |
| 14 | `ec54e576-74ac-f111-aaab-7ced8d21a9d7` | org `67577f8c-4301-f111-8407-7ced8d1dc988` | matter `0d8df610-aa57-f111-a824-3833c5d9bcab` |
| 15 | `3b1bb054-bea0-f111-aaad-7ced8ddc4a05` | contact `52bb55e7-9d15-f111-8343-7c1e520aa4df` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 16 | `5f2e875d-bea0-f111-aaad-7ced8ddc4a05` | contact `52bb55e7-9d15-f111-8343-7c1e520aa4df` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 17 | `76c1b487-bea0-f111-aaad-7ced8ddc4a05` | contact `52bb55e7-9d15-f111-8343-7c1e520aa4df` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 18 | `cbad0998-bea0-f111-aaad-7ced8ddc4a05` | contact `52bb55e7-9d15-f111-8343-7c1e520aa4df` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 19 | `f0557eee-8e96-f111-b8dc-7ced8ddc4a05` | contact `2e419a4f-010d-f111-8342-7ced8d1dc988` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 20 | `1967189b-9296-f111-b8dc-7ced8ddc4a05` | org `67577f8c-4301-f111-8407-7ced8d1dc988` | project `656fe858-0d1c-f111-88b3-7ced8d1dc988` |
| 21 | `5a1e8da2-9796-f111-b8dc-7ced8ddc4a05` | contact `43c4e819-9496-f111-b8db-0022482fb5a7` | matter `b68299c6-bafb-f011-8407-7c1e520aa4df` |
| 22 | `e4cde4b4-9896-f111-b8dc-7ced8ddc4a05` | contact `43c4e819-9496-f111-b8db-0022482fb5a7` | project `7524864d-3660-f111-ab0b-70a8a59455f4` |
| 23 | `b64fbc28-be9c-f111-b8de-7ced8ddc4a05` | contact `2e419a4f-010d-f111-8342-7ced8d1dc988` | matter `86335ce3-4c18-f111-8343-7ced8d1dc988` |
| 24 | `b74fbc28-be9c-f111-b8de-7ced8ddc4a05` | contact `8e9918a9-9021-f111-88b5-7c1e520aa4df` | matter `86335ce3-4c18-f111-8343-7ced8d1dc988` |
| 25 | `9aed8ab9-c29c-f111-b8de-7ced8ddc4a05` | org `67577f8c-4301-f111-8407-7ced8d1dc988` | matter `042f4462-860e-f111-8342-7c1e520aa4df` |

**After-state (re-queried 2026-09-11 01:17 UTC)**: **25 written, 0 skipped, 0 failed.** Active rows with a
null expiry: **0** (was 25). Active total: **28** (unchanged). All 25 targets carry **`2026-12-10`**. Each row
was re-read first and written only if still active with a null expiry, and each PATCH used `If-Match: *`
(update-only — a PATCH to a missing id can never create a row). `sprk_externalrecordaccess` is audited
(session 6), so every change is also in Dataverse audit history.

⚠️ **Observed in passing — pre-existing duplicate grants.** Contact `52bb55e7-…` holds **five** active rows
on matter `b68299c6-…` (#7, #15–#18). These predate task 010's upsert; task 010 collapses them on the next
grant or revoke for that key (both sweep by key), and revoke already removes all of them. Not touched here —
a backfill is not the place to deactivate rows.
