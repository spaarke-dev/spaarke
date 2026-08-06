# Foundation Spike — Findings & Go/No-Go (Task 001, FR-16)

> **Date**: 2026-08-03
> **Task**: `001-foundation-spike.poml` · **Rigor**: FULL · **Tier**: opus @ xhigh
> **Status**: 🟡 **PARTIAL — code-path verification complete; live Teams-client validation is operator-gated (see §5).**

## 0. TL;DR

| Path | Code-verified verdict | Live-validation verdict | Gate |
|---|---|---|---|
| (a) systemuser → ADR-034 membership | ✅ **GO** — fully wired end-to-end today | ⏳ pending operator (Teams token) | §5 Run A |
| (b) contact-only → contact-anchored membership | 🟡 **CONDITIONAL-GO** — reuse path verified feasible; net-new *entry* layer = tasks 020/021; **no architectural blocker found** | ⏳ pending operator (Teams token) | §5 Run B |
| (c) standalone SPA still works (CIAM) | ✅ **GO** — independent auth path, no regression surface | ⏳ pending operator (visual) | §5 Run C |
| Teams SSO/NAA yields a **BFF-valid workforce token** (desktop **and** web) | ❌ **cannot be verified autonomously** | ⏳ **THIS is the project's true go/no-go unknown** | §5 Run A/B |

**Bottom line: code inspection found NO architectural NO-GO.** The design assumption that the workforce token resolves to a membership-scoped record set with *no new authorization model* holds — the reuse seams the design counts on are real (verified below). The one genuine unknown that gates the whole project is the operator-run validation that **Teams SSO/NAA can deliver a BFF-valid workforce token in the desktop client** (the documented risk in design §4/§5). That is a human-in-the-loop test (§5); an autonomous agent cannot sign into real Teams desktop/web clients or complete interactive workforce OAuth.

---

## 1. What was verified autonomously (code inspection)

This spike's highest-value output is confirming — against the *actual BFF code*, not the design doc — whether both membership planes resolve end-to-end, so the Phase-1/2 build starts on a validated foundation. All file/line refs are on branch `work/teams-app-r1`.

### 1.1 Systemuser plane (criterion 1) — WIRED end-to-end ✅

The full chain exists and is unconditional in DI (per the endpoint's own header comment):

1. **oid → systemuserid**: [`MembershipEndpoints.ResolveSystemUserIdAsync`](../../../../src/server/api/Sprk.Bff.Api/Api/Membership/MembershipEndpoints.cs#L399-L490) queries `systemuser.azureactivedirectoryobjectid == oid` (+ `isdisabled == false`), 10-min tenant-scoped Redis cache. AAD `oid` extracted per ADR-028 at [`ExtractAadObjectId`](../../../../src/server/api/Sprk.Bff.Api/Api/Membership/MembershipEndpoints.cs#L354-L368).
2. **systemuserid → PersonIdentity**: [`IdentityNormalizationService.ResolveAsync(systemUserId)`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/IdentityNormalizationService.cs#L99) resolves 6 identity paths (BU, contact via `sprk_primarycontact` **or** `contact.azureactivedirectoryobjectid` fallback, teams, account, orgs).
3. **PersonIdentity → membership rows**: [`MembershipResolverService.ResolveAsync`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs#L133) → metadata-discovered descriptors → `BuildFetchXml` OR-join → Dataverse rows, grouped by role, cached 5 min.

**Verdict: the systemuser plane needs no new resolution model.** Once a workforce token authenticates a *provisioned* systemuser, `GET /api/users/me/memberships/{entityType}` returns their ADR-034 membership set today.

### 1.2 Contact-only plane (criterion 2) — reuse verified; entry layer is the net-new work 🟡

**The ADR-034 Path-C claim in the task is TRUE at the FetchXml layer.** `BuildFetchXml` already has a `Contact` branch that binds `identity.ContactId` to Contact-typed descriptors: [`MembershipResolverService.cs` case `"Contact"`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs#L466-L500). So contact-anchored membership does **not** need a second membership engine — exactly as design §5 / ADR-034 Path C assumed.

**But the entry + normalization layers are hard-keyed to a `systemuserid`** — this is the real gap, and it is precisely what tasks 020/021 scope:

- The endpoint **rejects a caller with no systemuser row with a hard `401`** ("Authenticated principal is not provisioned as a systemuser in Dataverse"): [`MembershipEndpoints.cs:215-231`](../../../../src/server/api/Sprk.Bff.Api/Api/Membership/MembershipEndpoints.cs#L215-L231). A contact-only workforce user never reaches the resolver today.
- Both `MembershipResolverService.ResolveAsync(Guid systemUserId, …)` and `IdentityNormalizationService.ResolveAsync(Guid systemUserId, …)` **throw `ArgumentException` on `Guid.Empty`** and take only a systemuserid — there is no contact-anchored entry.
- `IdentityNormalizationService` already knows how to map `contact.azureactivedirectoryobjectid == oid` ([`TryResolveContactIdAsync`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/IdentityNormalizationService.cs#L243-L285)) — but only *after* it has a systemuser row's AAD oid.

**What tasks 020/021 must build (confirmed by this spike, no redesign needed):**
- **Task 020 (workforce→principal resolver):** `AAD oid → principal` = systemuser (existing `ResolveSystemUserIdAsync`) **else** contact (`contact.azureactivedirectoryobjectid == oid`, or verified email fallback per the demo-org note). Replaces the unconditional 401 with a principal-typed branch.
- **Task 021 (contact-anchored entry):** a resolver path that accepts a `contactId` (not a systemuserid), builds a `PersonIdentity { ContactId = … }` directly, and feeds the **existing** `BuildFetchXml` Contact branch — filtered to the access-conferring `sprk_assigned*` allowlist (NFR-05). Options: an overload `ResolveByContactAsync(Guid contactId, …)` on `IMembershipResolverService`, or a `PersonIdentity`-in entry that bypasses the systemuser-keyed `IdentityNormalizationService.ResolveAsync`.

**Verdict: CONDITIONAL-GO.** The design's reuse assumption is architecturally sound (FetchXml layer proven). The contact-only plane is net-new *entry* code, not a membership-model change — no blocker.

### 1.3 SPA / CIAM plane (criterion 3) — independent, no regression surface ✅

The SPA auth is a standalone MSAL v5 `PublicClientApplication` on the **CIAM** authority (`*.ciamlogin.com`), `sessionStorage`, silent→redirect: [`msal-config.ts`](../../../../src/client/external-spa/src/auth/msal-config.ts) + [`msal-auth.ts`](../../../../src/client/external-spa/src/auth/msal-auth.ts). It is entirely independent of the Teams-host workforce plane. As long as the Phase-1 shared module keeps **authority pluggable** (the ADR-028 A2 posture — task 010), the SPA path is untouched. **No code-level regression surface** from the Teams work.

### 1.4 Cross-cutting confirmations

- **Broker-only (NFR-02) holds by construction**: the membership read path never exchanges the caller token downstream — it queries Dataverse via the app-only `IDataverseService`. No OBO on this path. ✅
- **`member_skipped: no_systemuser_mapping`** telemetry already exists ([resolver L492-498](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs#L492-L498)) — for a *systemuser* whose `sprk_primarycontact` is unlinked, Contact descriptors are skipped. For the contact-**only** plane there is no systemuser at all, so task 020 must resolve `oid → contact` directly (verified-email fallback if `azureactivedirectoryobjectid` is unpopulated in the dev org).
- **ADR-028 A2 (task 002)** is the correct posture: the BFF *already* runs a workforce default JwtBearer scheme, so A2 sanctions the workforce **client** plane, not a new IdP — consistent with the code (the endpoint validates a standard JWT via `RequireAuthorization()`).

---

## 2. Assumptions tested

| Design assumption | Result |
|---|---|
| Workforce token resolves to a membership-scoped set with **no new authz model** | ✅ Confirmed for systemuser; ✅ confirmed *feasible via reuse* for contact (020/021 wire it) |
| Contact-anchored resolution **reuses** `MembershipResolverService`/`BuildFetchXml` (ADR-034 Path C) | ✅ Confirmed — `Contact` branch already binds `ContactId` |
| No OBO / broker-only on the collaboration read path (NFR-02) | ✅ Confirmed — app-only Dataverse read |
| SPA (CIAM) is unaffected by the Teams host | ✅ Confirmed — independent MSAL instance/authority |
| Teams SSO/NAA can deliver a **BFF-valid** workforce token in **desktop** (popup/CA risk) | ❌ **Untested — operator-gated (§5). This is the project's load-bearing unknown.** |

**No assumption was invalidated by code inspection.** The single unresolved item is the live Teams-token acquisition, which is inherently a human validation.

---

## 3. Why the live paths could not be closed autonomously

Steps 2, 4, 6 of the task require an operator interactively signed into **real Teams desktop and web clients** with **real workforce identities**, against a **running BFF** and the **dev org**, completing **interactive workforce OAuth** (with real Conditional Access). None of that is executable by an autonomous coding agent. Per the task's own `<escalation>` trigger and root CLAUDE.md §6, this is surfaced as a go/no-go for the operator — not coded around. The scaffold in §4 exists to make that operator run a fast click-through.

---

## 4. Spike scaffold (throwaway) — `notes/spikes/teams-tab-spike/`

A minimal, runnable-shaped Teams personal tab the operator deploys to close Runs A/B:

| File | Purpose |
|---|---|
| `manifest.json` | Teams v1.29 personal-tab manifest (static tab, `webApplicationInfo` for workforce SSO). **Fill the `TODO:` placeholders** with the real app id / domain / hosting URL. |
| `index.html` | Zero-build tab page: initializes Teams JS, acquires a workforce token, calls the BFF membership endpoint, renders the result + raw token claims. |
| `teams-sso.js` | `authentication.getAuthToken()` SSO acquisition (primary) with NAA/MSAL note; posts `Authorization: Bearer` to the BFF. |
| `config.sample.js` | Copy to `config.js` and fill: multitenant app id (`1e40baad-…`), BFF base URL, membership entityType, BFF audience/scope. |
| `README.md` | Operator run steps (host, register, sideload, sign in, capture). |

It is **throwaway** (spike-exempt from test/deploy gates per the task constraint) and must **not** be promoted into `src/` without a follow-on task — the production adapter is task 012, the production resolver is tasks 020/021.

---

## 5. Operator runbook + Go/No-Go checklist (fill this in during the live run)

**Prereqs**: running BFF reachable over https from Teams; the multitenant Entra app (`1e40baad-…`) with `access_as_user` exposed + Teams redirect URIs; dev org (7 contacts / 163 systemusers); host the `teams-tab-spike/` files at an https URL; set `config.js`.

### Run A — systemuser plane (Teams desktop, then web)
1. Sideload the spike tab; open it as a **provisioned systemuser** (a workforce user whose `systemuser.azureactivedirectoryobjectid` is set). No second login should appear.
2. Confirm the tab acquired a token and the BFF returned **200 + a non-empty membership set** for the test `entityType`.
3. Repeat in **Teams web**. Record any popup / Conditional-Access difference vs desktop.

- [ ] Desktop: workforce SSO completed with **no second login** → token acquired
- [ ] Desktop: BFF `200` + membership rows returned (systemuser plane)
- [ ] Web: same result
- [ ] Desktop-vs-web difference (record): _______________________
- **Run A verdict**: ⬜ GO ⬜ NO-GO — notes: _______________________

### Run B — contact-only plane (expected to 401 *today*; that is the signal, not a failure)
1. Open the tab as a **workforce user with NO systemuser row** (contact-only). Today the endpoint returns **401** by design (§1.2) — this run **confirms the gap tasks 020/021 close**, it does not block the spike.
2. Record whether the user resolves to a `contact` by `azureactivedirectoryobjectid`; if that field is unpopulated in the dev org, note it (020 uses the verified-email fallback).

- [ ] Contact-only user token acquired (SSO worked)
- [ ] Endpoint returns 401 today (expected) → confirms 020/021 scope
- [ ] Contact resolvable by `azureactivedirectoryobjectid`? ⬜ yes ⬜ no (→ email fallback) — note: __________
- **Run B verdict**: ⬜ CONDITIONAL-GO (gap understood, no blocker) ⬜ NO-GO (unexpected block) — notes: __________

### Run C — SPA regression
1. Open the standalone external SPA in a browser; sign in via **CIAM**; confirm records list.

- [ ] SPA signs in via CIAM and lists records (no regression)
- **Run C verdict**: ⬜ GO ⬜ NO-GO — notes: _______________________

### Overall spike gate
- [ ] **PROJECT GO** — Teams SSO delivers a BFF-valid workforce token (desktop + web); Phase 1 may start.
- [ ] **ESCALATE (NO-GO)** — Teams SSO/NAA cannot yield a BFF-valid token in the desktop client, OR an unexpected block. Per the `<escalation>` trigger, STOP and escalate before Phase 1.

**Recorded by**: ____________  **Date**: __________  **Overall**: ⬜ GO ⬜ NO-GO

---

## 6. Handoff to downstream tasks

- **002 (ADR-028 A2)**: apply as drafted — the code confirms A2 sanctions the workforce *client* plane over the BFF's existing JWT scheme (no new IdP). Safe to apply in Wave 0 (main-session) regardless of the live run.
- **010 (shared MSAL module)**: keep authority pluggable; the SPA CIAM path (§1.3) must remain byte-for-byte behaviorally identical.
- **020 (workforce→principal resolver)**: replace the unconditional 401 at [`MembershipEndpoints.cs:215-231`](../../../../src/server/api/Sprk.Bff.Api/Api/Membership/MembershipEndpoints.cs#L215-L231) with systemuser-else-contact resolution.
- **021 (contact-anchored entry)**: add a `contactId`-keyed resolver entry feeding the existing `BuildFetchXml` `Contact` branch; filter to the `sprk_assigned*` allowlist (NFR-05).
- **022 (enforcement)**: the accessible-record-set gate composes over whichever principal 020 returns.

> **Gate reminder**: per `tasks/TASK-INDEX.md`, **do not start Wave 1 until §5 records an overall GO.** Waves 1–8 remain owner-gated.
