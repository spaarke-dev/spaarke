# Current Task State

> **Last Updated**: 2026-11-25 ~16:10Z (task 040 in progress; checkpoint at 86% context)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | **Task 040** — workforce-contact record tabs scoped by grants (FR-24, #2508). FULL rigor, opus @ high, directional. POML `tasks/040-workforce-contact-tabs.poml`. |
| **Done** | Step 0 (conflict-check: no open PR on these files; master merged in `14802e0a49`, pushed). Steps 1–5 below. SPA vitest `tests/WorkforceContactTabs.test.tsx` 9/9 pass; SPA tsc clean; BFF build clean. |
| **Uncommitted files** | `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs` (rule); `Api/ExternalAccess/ExternalAccessEndpoints.cs` + `MeEntitlementsEndpoint.cs` (doc/description text only); `src/client/external-spa/src/registry/widgetRegistry.ts` (`RECORD_TAB_GATE`, `RECORD_TABS_ENTITLEMENT`); `src/client/external-spa/tests/WorkforceContactTabs.test.tsx` (new). |
| **Next action** | Write the BFF test in KEEP path `tests/integration/auth/ExternalAccess/WorkforceContactRecordTabsTests.cs`: hit `GET /api/v1/external/me/entitlements` through a fixture deriving from `IntakeFixture` (in `tests/integration/contract/Api/ExternalAccess/ExternalIntakeEndpointsTests.cs`, public abstract; sets `Principal`, real `ModuleEntitlementResolver` with an empty map, `WorkforceCeiling` from `ExternalCollaborationTestFixture`). Cases: granted workforce contact (project / matter / WA each) → contains `assigned-work`; ungranted contact → not; systemuser with grants → not; CIAM → `["assigned-work"]` unchanged; home principal granted → yes; ceiling without WA Read → dropped. Plus a unit case in `tests/unit/.../ModuleEntitlementResolverTests.cs`: a role-map row `assigned-work` for a systemuser is stripped. Then run the affected BFF tests, `/code-review`, `/adr-check`, two adversarial passes (authorization tag), UAC review request (SendMessage to uac-r2; quote in notes), publish-size delta vs fresh master (short-path worktrees), CVE check, `/conflict-check`, notes, TASK-INDEX/POML, PR. |

## Task 040 decisions (record in notes/task-040-notes.md)
- **Signal (step 2):** server-side. `ModuleEntitlementResolver.IsGrantedWorkforceContact(principal)` = workforce plane ∧ `IsContactPrincipal` (no systemuser) ∧ (any readable project ∨ matter ∨ WA) on the principal ALREADY composed by `WorkforcePrincipalStrategy.ComposeAsync` (no new read, endpoint or DI). Adds `assigned-work` (const `AssignedWorkModule`). `assigned-work` is stripped from role-map extras on the workforce plane, so a licensed systemuser never gets the tabs whatever the map holds. Covers home and non-home workforce contacts (both are contact principals); recorded for UAC review.
- **SPA:** five widgets use `RECORD_TAB_GATE` = planes ciam+workforce, requiredEntitlement `assigned-work`, default on both planes. CIAM unchanged because the server reports `assigned-work` to every CIAM caller (and me-client's CIAM fallback has it). Billing Guidelines stays CIAM-only.
- **Ceiling:** `MeEntitlementsEndpoint` intersects with the Workforce-role ceiling; `WorkforceRoleCeilingGate.ModuleTables["assigned-work"] = sprk_workassignment`, so a role without WA Read drops all five tabs. Ask UAC whether that single-table mapping is right (each tab's fetch checks its own table).
- **Step 4 (verified, no escalation):** all five tabs read through module fetch descriptors in `Infrastructure/DI/ExternalAccessModule.cs` (`collaboration`, `matters`, `work-assignments`, `documents`, `invoices`), none plane-gated, each Tier-2-scoped from the principal's composed sets.
- **K2 (two unread counts, tid-keyed feed marker / identity cache):** unchanged by this task; stays a known limit (tabs read neither).
- **K3 (licensed user linked to a WorkforceHome contact's (tid, oid) resolves as systemuser):** stays a known limit; with this task that person gets no record tabs (licensed model). Fixing it changes principal precedence = escalation trigger 3 (auth plane), not this task.

## Live on dev (unchanged)
SPA `0267ee64a2` (rollback `3067fb3bdc`); BFF `ef0c566d2c` (uac-r2 deploys; owner go per SHA). Master moved past `0267ee64a2` incl. ebilling ISS-052 #3082 (`2ac250cae4`); list it in the next SPA deploy proposal.

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email? (Told to the owner; not filed. Repo is public.)
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-33 #3010; D-34 #3045 done; D-35 #3046; D-36 #3050; D-37 #3129; D-38 #3149.

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes; on a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`).
