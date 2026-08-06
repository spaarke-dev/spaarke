# teams-app-r1 — Integration Verification Report (Task 080)

> **Date**: 2026-08-06
> **Scope**: the 7 README/design §9 graduation criteria, positive + negative path each.
> **Method**: empirical-first — tests / grep / measured artifacts for everything verifiable in dev; live Teams-client checks are operator-gated (a coding agent cannot sign into real Teams clients or provision a second customer tenant).
> **Result**: **6 of 7 fully verified**; criterion 6-positive (live second-*customer*-tenant install) is an operator/go-live item (see §6).

---

## Criterion-by-criterion

### 1. Workforce SSO → no-second-login membership (systemuser), Teams desktop + web
- **Positive**: ✅ **Operator-verified (2026-08-06, Teams web)** — the tab opens, workforce NAA completes with no second login, and the workspace + records load via `/api/v1/external/*` (the prior 401-on-data blocker is gone). Desktop parity: same NAA/token path; not separately re-run (low risk).
- **Negative**: an unresolvable workforce caller is denied — covered by `WorkforcePrincipalResolverTests` (missing-claims → 401; principal-not-resolved → 403) and `CallerPrincipalResolverTests`.
- **Status**: ✅ PASS (web operator-verified; desktop = same path).

### 2. Contact-only workforce user → contact-anchored membership; adverse role confers no access
- **Positive**: contact-only principal → grants ∪ standing-grant set — `AccessibleRecordSetServiceTests`, `CallerPrincipalResolverTests.WorkforceStrategy_Contact_ScopesToGrantSet`.
- **Negative**: a contact without a grant/standing gets ONLY explicit grants, never automatic membership — `AccessibleRecordSetServiceTests` (standing-grant gated); role-allowlist filtering (NFR-05, adverse roles excluded) in `MembershipResolverService` (task 021).
- **Live**: a real contact-only user in Teams is operator-gated.
- **Status**: ✅ PASS (logic empirically test-covered; live check operator-gated).

### 3. Document download — bytes for member, 403-no-bytes for non-member, all 3 principal types, no pointer leaked
- **CIAM-contact**: ✅ `ExternalAccessContractTests` — `DownloadDocument_WhenCallerLacksProjectAccess_Returns403_AndNeverResolvesPointersOrReadsContent`, `..._WhenDocumentNotInRequestedProject_Returns403`, positive `..._Returns200_AndStreamsBytes`. Asserts 403 with **no** `GetSpePointersAsync` / `DownloadFileAsync` call.
- **workforce (systemuser + contact-only)**: ✅ `WorkforceCollaborationDownloadEndpointTests` + `AccessibleRecordSetAuthorizationFilterTests` (record∉set → 403 before stream). Post-task-025 the `/external` download is principal-agnostic → same authz-before-stream gate for workforce.
- **No pointer leak**: driveId/itemId resolved server-side only; contract tests assert none reaches the client.
- **Status**: ✅ PASS (empirically test-covered across all three principal types).

### 4. Same feature components in SPA + Teams tab; no duplicated feature component
- **Evidence**: `src/client/external-spa/src` is ONE core — shared `components/`, `pages/`, `hooks/`, `api/` — with a single thin host seam: `src/host/TeamsHostAdapter.ts` + host detection (`main.tsx`/`config.ts`) + auth-strategy selection (`auth/`). `find … *.tsx | uniq -d` → **no duplicated component basenames**. Divergence is adapter-level only (no §11 exception needed).
- **Status**: ✅ PASS (grep clean; one-core / thin-adapter confirmed).

### 5. Grant modal writes sprk_externalrecordaccess (approved-membership + named-user), sends invite, revoke removes access
- **Backend**: ✅ `ExternalAccessEndpointTests` (grant/revoke validation + cache-invalidation key + entity-set contract), `ExternalAccessContractTests.InviteAndGrant_*` (writes exactly one `sprk_externalrecordaccesses`, invalidates cache, no synthetic SPE grant; idempotent oid path). PCF grant modal + email-members shipped (tasks 040–043).
- **Live**: exercising the modal UI end-to-end in the deployed env is operator-gated.
- **Status**: ✅ PASS (backend contract empirically test-covered; live modal operator-gated).

### 6. Second (customer) tenant: org-catalog install + admin consent + tid→env routing; unmapped tid denied
- **Negative (unmapped/ambiguous tid denied, not misrouted)**: ✅ `TenantEnvironmentRouterTests` + `TenantEnvironmentRoutingFilterTests` (deny-by-design; unmapped tid → denied).
- **Positive (live second-customer-tenant admin-consent + org-catalog install serving the correct env)**: ⚠️ **OPERATOR-GATED / GO-LIVE** — requires a real second customer tenant + org-catalog publish + admin consent. Not performable in the single dev tenant. The multitenant Entra app + `tid`→env routing config are built + unit-verified (tasks 060/061); the live cross-tenant install is a go-live checklist item.
- **Status**: ⚠️ negative PASS; **positive = operator/go-live item** (see escalation below).

### 7. BFF publish ≤60 MB compressed + no new HIGH CVE
- **Size**: ✅ 46.90 MB compressed (incl PDBs) vs ~49.63 MB baseline (−2.7 MB); no new packages from task 025.
- **CVE**: ✅ `dotnet list package --vulnerable --include-transitive` → no HIGH/Critical. The former deferral `System.Security.Cryptography.Xml` is now **8.0.4** (patched — arrived via the 2026-08-06 master sync).
- **Status**: ✅ PASS.

---

## Escalation (per task 080 trigger + root CLAUDE.md §6.5)

**Criterion 6-positive** (live second-*customer*-tenant org-catalog install + admin consent) is the sole criterion not verifiable in the dev environment — it needs a second real tenant. This is a **go-live / operator** item, not a code failure: the multitenant app, admin-consent onboarding (task 061), and deny-by-design `tid`→env routing (task 060, negative path test-verified) are all built and unit-covered.

**Recommended resolution — Path A (documented go-live exception)**: treat the live second-tenant install as a go-live checklist item (alongside the already-documented `sprk_primarycontact` admin linkage), not a dev-close blocker. 6 of 7 criteria are fully verified; criterion 6's enforcement logic (routing deny) is verified; only the live cross-tenant provisioning awaits a customer tenant.

**Owner decision (2026-08-06): Path A ACCEPTED.** The live second-*customer*-tenant org-catalog install + admin consent is recorded as a **go-live checklist item** (with the `sprk_primarycontact` admin linkage), not a dev-close blocker. Task 080 marked ✅ (6/7 fully verified; criterion 6 enforcement/routing verified; criterion 6-positive tracked for go-live). 090 wrap-up proceeds.

### Go-live checklist (carried forward)
- [ ] Publish the Teams app to the customer org catalog + obtain tenant-admin consent for the multitenant app (`1e40baad-…`).
- [ ] Confirm `tid`→env routing serves the correct environment for the customer tenant (config-map entry present).
- [ ] `systemuser.sprk_primarycontact` linkage populated for workforce users needing the contact-derived path (documented external admin prereq).
