# R3 auth path — definitive recommendation (2026-10-09)

**Status:** recommendation for the owner. It replaces the PROVISIONAL auth items in `design.md` §4.6 once the owner confirms the three decisions in §5.
**Basis:**
- Code on `origin/master` @ `231c5ab2b`.
- The auth system of record, the architecture draft and the research files on branch `work/spaarke-auth-system-of-record-r1`:
  - `projects/spaarke-auth-system-of-record-r1/auth-system-of-record.md`;
  - `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md`;
  - `working/x09a`–`x09d`.
- The owner's live guest test (2026-10-08, add-in Diagnostics view): a B2B guest signing in against Spaarke's tenant gets `tid` = Spaarke, `acct = 1`, `idp` = the home tenant.
- Provisioning T240d (owner-accepted 2026-10-09) and T240c (the directory, designed but not built).

## 1. Who uses R3, and how each signs in

| User | Who they are | Identity | Sign-in in R3 | Server principal today |
|---|---|---|---|---|
| Spaarke staff | U1 | Member of Spaarke's tenant, licensed `systemuser` | Work account (browser) or Teams | `SystemUser` — works |
| Licensed customer staff (Model 1) | U2 | B2B **guest** in Spaarke's tenant, invited by provisioning H11, licensed `systemuser` keyed by the guest `oid` | Work account (browser) or Teams | `SystemUser` — works (same token shape the add-in already uses) |
| Licence-free customer staff ("workforce contacts") | U3 | Must also be a B2B guest in Spaarke's tenant, with no licence | Work account (browser) or Teams | **Refused today** (`ForeignTenant` on stamps) — needs the member-test change (§4) |
| External partners (outside counsel etc.) | U4 | Local account in the CIAM tenant `spaarkeextid` | "Continue as Partner" (browser only, no Teams) | `ContactOnly` — works on dev; T240d makes it per customer |

## 2. Workforce plane (browser "work account" and the Teams tab) — use the Office add-in pattern

1. **Client app.** One dedicated **single-tenant** public client registration in Spaarke's tenant (for example "Spaarke Workspace"), shared by every Model 1 customer, as the add-in client is.
   - It is **not** the BFF app itself (today's dev shape).
   - SPA redirects: `https://external.spaarke.com` and `brk-multihub://external.spaarke.com` (NAA).
2. **Authority.** Pinned: `https://login.microsoftonline.com/{Spaarke tenant}`.
   - This is what makes a guest's token come from Spaarke's tenant with the guest `oid` that the `systemuser` is keyed by.
   - Proven live for the add-in on 2026-10-08.
   - `/organizations` (today's SPA code) would sign a guest into their **home** tenant, with a different `oid` and no `systemuser` match.
3. **Scope.** `api://{customer BFF app}/user_impersonation`.
   - Every provisioned customer BFF app already exposes it (H3).
   - The BFF checks only that a delegated scope is present, never which one (`CallerIdentity.cs:113,248`).
   - So **no new `access_as_user` scope is needed** — config only (`VITE_TEAMS_MSAL_BFF_SCOPE`).
   - H3 must add the new client to its platform pre-authorized list (bicep `PreAuthorizedClientAppIds`), next to the production add-in.
4. **Teams.** NAA first, then the MSAL popup (brokered).
   - **No `webApplicationInfo`** and **no Teams-SSO fallback**: `getAuthToken` needs `webApplicationInfo`, and the SSO token's audience would be wrong for a per-customer server.
   - Open live check (Teams desktop/web, using the existing test guest `ralph@deweycheatham…`): whether NAA against a stamp app also needs Microsoft's broker pre-authorized, as teams-app-r1 needed on dev. If it does, H3 adds it to the platform list.
5. **Which server.** The SPA resolves the customer server at run time and never bakes it.
   - Workforce: through the T240c directory (membership of `sprk-{customerId}-users`).
   - Until 240c ships, dev runs against one configured server (as today).
   - R3 builds the run-time selection seam now. It is the same seam the partner plane needs.

## 3. Partner plane (CIAM) — T240d as accepted

1. Shared CIAM authority.
2. The invite link carries only the customer key.
3. The SPA looks up the server URL and scope through the T240c directory's CIAM lookup.
4. It requests `api://{customer BFF app}/user_impersonation` from `spaarkeextid`.
5. The stamp sets `Ciam:Audience` = its BFF app id.

The production CIAM SPA client (redirect `https://external.spaarke.com`) is created by provisioning after spike S1. R3's review and requests: `notes/coordination/2026-10-09-to-provisioning-t240d-review.md`.

**Result:** both planes use the same scope name and the same run-time server selection. Only the authority differs: Spaarke tenant vs `spaarkeextid`.

## 4. Server-side dependencies (not R3 code unless the owner assigns them)

| # | Change | Why R3 needs it | Owner | Issue |
|---|---|---|---|---|
| D1 | Member test admits a guest of the stamp tenant whose home tenant (`idp`) is on the customer list | Licence-free customer staff (U3) | UAC-r2 / word-add-in task 126 | #1563 |
| D2 | Define module app roles on the BFF registration (H3), assign them to the customer's user group, and seed `sprk_approlemodulemap` | Workforce users otherwise see no gated modules (Front Door wizards, widgets) | provisioning + R3 | #1568 |
| D3 | T240c directory (workforce lookup + CIAM lookup) | Run-time server selection in production | provisioning | — (task 240c) |
| D4 | T240d: per-customer CIAM audience, keyless provisioner, default-scheme CIAM-token guard | Partner plane on stamps | provisioning (+ R3 for the audience forms and the guard, if the owner agrees) | — (task 240d) |
| D5 | Fix the SPA's out-of-plane pages and the production mock identity | Upload and playbook pages fail today | **R3** | #1566 |
| D6 | ADR-028 A5 (impersonated record set) | Direct shares are invisible to system users in the SPA/Teams | UAC-r2 task 036 | #1567 |

## 5. Owner decisions needed

1. **Confirm `user_impersonation`** for the Teams tab and the browser work-account plane (no `access_as_user` on stamps).
2. **Licence-free customer staff are invited as B2B guests** in Spaarke's tenant (no licence). Who invites them: H11 at provisioning time, or an in-product invite? Use the `idp` claim as the trusted home-tenant source.
3. **Module access for workforce users:** define app roles on the BFF registration and assign them to the customer group, versus another rule.

## 6. ADR consequence

ADR-028 A2 says "authenticate Teams users via Teams SSO / NAA against a **multitenant** app". This path uses a **single-tenant client in Spaarke's tenant, NAA plus popup, no SSO fallback**, so it needs a path-B amendment, made together with the record §12b items.

## 7. What R3 can do now

The design can go to `/design-to-spec` with §2–§3 as the auth design; the auth items are no longer blocked on investigation. The build order follows the dependencies in §4:

- R3's own client work (§2.1–2.5, §3, D5) can start immediately against dev.
- Production needs D3 and D4.
- Licence-free staff need D1.
