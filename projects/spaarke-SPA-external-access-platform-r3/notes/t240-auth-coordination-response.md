# External Access SPA — auth coordination response to customer-provisioning-orchestration-r1 (t240)

> **From**: spaarke-SPA-external-access-platform-r3 · **To**: customer-provisioning-orchestration-r1
> **Date**: 2026-10-07 · **Re**: `projects/customer-provisioning-orchestration-r1/notes/t240-plan.md`
> **Status**: Grounded in code (external-spa @ master 854de8800). Nothing changed on dev. Owner decisions flagged 🔔.
> **Scope note**: the Teams tab IS this SPA (`swa-spaarke-external-spa-dev` / `green-dune-0c4f1221e.7.azurestaticapps.net`, package `src/client/external-spa/appPackage`). Confirmed.

---

## 0. The one correction that reframes everything

Your note (and the owner's 2026-10-07 statement) scopes "the Teams tab and the Office add-ins … to Dataverse-licensed users, internal or B2B guest in Spaarke's tenant." That is correct **for the Teams tab**. But the same code artifact (`external-spa`) is **also served standalone in a browser**, and in that mode it serves a **second identity plane you must not design out**: **CIAM external contacts** (outside counsel / partners). So:

| Surface | Identity plane(s) today | Tenant |
|---|---|---|
| **Teams tab** | **workforce only** (NAA primary + Teams-SSO fallback) | customer workforce tenant (multitenant app + per-customer admin consent) |
| **Standalone browser** | **BOTH** — workforce **and** CIAM external contacts (realm chooser "My organization / Partner") | workforce tenant + the shared Spaarke **CIAM external tenant `spaarkeextid`** |

CIAM external contacts are **local accounts in `spaarkeextid`**, *not* B2B guests in any workforce tenant ([msal-config.ts:19-22](../../../src/client/external-spa/src/auth/msal-config.ts#L19)). This plane is the whole reason the external-access platform exists. **Your per-customer-backend design must decide whether it serves this plane** — see Q3; it's the biggest open item.

---

## 1. What the code does today (ground truth, so we agree on the baseline)

**One SPA, origin-agnostic in code.** MSAL `redirectUri` / `postLogoutRedirectUri` = `window.location.origin` ([msal-config.ts:105-106](../../../src/client/external-spa/src/auth/msal-config.ts#L105)). So the SPA itself needs **no code change** to move origins — but the Teams manifest and the app-registration redirect list are **not** origin-agnostic (see Q1).

**Pluggable authority + runtime-swappable BFF target.** The auth module is already built for multiple authorities and a *runtime-selected* BFF scope/acquirer — this is the key enabler for your proposal:
- `setActiveBffTokenAcquirer(closure)` — the whole app calls `acquireActiveBffToken()`; the active strategy is swapped once at bootstrap ([msal-auth.ts:167-179](../../../src/client/external-spa/src/auth/msal-auth.ts#L167)).
- `setActiveLoginScope(scope)` — the standalone sign-in redirect requests the selected plane's scope ([msal-auth.ts:193-203](../../../src/client/external-spa/src/auth/msal-auth.ts#L193)).
- The NAA path requests `cfg.bffScope` **dynamically at runtime** ([msal-auth.ts:321-346](../../../src/client/external-spa/src/auth/msal-auth.ts#L321)), not a build-time constant.

**But the config feeding those seams is single-valued at build time today.** `VITE_BFF_API_URL`, `VITE_MSAL_BFF_SCOPE`, `VITE_TEAMS_MSAL_CLIENT_ID`, `VITE_TEAMS_MSAL_BFF_SCOPE` are one set of values substituted at CI build ([.env.production](../../../src/client/external-spa/.env.production)). One SPA build = one backend. **That is the thing that has to change for one-backend-per-customer** — from build-time constants to runtime per-customer resolution. The *seams* already exist; the *source of the values* must become a directory lookup.

**Teams tab = NAA primary + Teams-SSO fallback.** `acquireTeamsWorkforceBffToken` tries NAA (`createNestablePublicClientApplication`, requests the workforce `bffScope`) and falls back to Teams SSO `authentication.getAuthToken` ([msal-auth.ts:372-399](../../../src/client/external-spa/src/auth/msal-auth.ts#L372)). This explains the dev app `1e40baad` having **both** the `brk-multihub://` SPA redirects (NAA broker) **and** the `webApplicationInfo` resource (SSO fallback audience).
- NAA requests scope **dynamically** → scales to per-customer scopes.
- The SSO fallback is pinned to the manifest's single `webApplicationInfo.resource` (`api://green-dune-.../1e40baad`) → **does not** scale; a single manifest can name exactly one audience.

**Teams manifest hardcodes the origin in 4 places** ([appPackage/manifest.json](../../../src/client/external-spa/appPackage/manifest.json)): `staticTabs[0].contentUrl` + `.websiteUrl` (L29-30), `validDomains` (L35-39, also lists the BFF host), and `webApplicationInfo.id` + `.resource` (L41-44, = the backend app `1e40baad`). Today **the backend app is also the tab's sign-in client** — exactly the coupling you flagged.

---

## 2. Answers to your three questions

### Q1 — Production origin

**Recommendation: ONE stable Spaarke-owned custom domain for the SPA, shared across all customers.** The external-spa is **Spaarke-tenant shared infrastructure**, not a per-customer asset — there is one SWA, one build, one origin. So every customer backend lists that **single** origin in CORS (not N origins). The code is already origin-agnostic (`window.location.origin`); the work is:
1. Map the SWA to the custom domain (ops).
2. Update the **4 manifest references** + the app-registration SPA redirect-URI list to the custom domain.
3. CI token-substitution stays as-is (it already injects URLs at build).

🔔 **Owner decision needed**: the exact hostname. Suggest something like `portal.spaarke.com` or `external.spaarke.com`. Once chosen it is **stable and singular** — that's the value you put in every customer backend's CORS allow-list. I can't invent the domain; please confirm it and I'll update the manifest + redirects.

### Q2 — Sign-in shape in Teams

**We agree with the add-ins' shape — adopt it.** Today the tab signs in with the backend app itself as the audience (`1e40baad`), which structurally cannot scale: `webApplicationInfo` + a single Teams package can name exactly one app. Your proposed shape fits the existing code with **no auth-engine rewrite**, because the NAA path already requests its scope at runtime:

1. **A dedicated Spaarke Teams client app** (one, in Spaarke's tenant) becomes the manifest's `webApplicationInfo.id` + the NAA client — **decoupled from any customer backend**. (Today they're the same app; this is the change.)
2. The tab calls **one Spaarke directory endpoint** → `[{customerId, displayName, apiBaseUrl, authority, scope}]` for the caller.
3. The tab then requests the **chosen backend's scope** — fed into `getTeamsWorkforceEnvConfig()`/`cfg.bffScope` (today build-time; becomes directory-resolved) and `apiBaseUrl` into the BFF client base URL (today `VITE_BFF_API_URL`; becomes directory-resolved).
4. Your **H3** pre-authorizes the Spaarke Teams client id on every customer backend → NAA silently mints that backend's token.

**One alignment item 🔔**: resolve the **NAA-vs-SSO-fallback** question. NAA (`brk-multihub`) is the primary and is the one that scales. The **Teams-SSO fallback** ([msal-auth.ts:353-359](../../../src/client/external-spa/src/auth/msal-auth.ts#L353)) is pinned to the single manifest audience and **cannot** target per-customer backends — so either (a) drop the SSO fallback for the multi-customer build, or (b) point `webApplicationInfo.resource` at the **Spaarke Teams client app's own exposed API** (not a customer backend) and have that app's token be accepted only as a last-resort identity assertion. Recommend (a) unless there's a known NAA-unavailable host we must support. Please confirm which hosts must work without NAA.

### Q3 — Outside Teams: does the External Access SPA call customer backends, and as which identities?

**Yes — and as two identity types**, which is the item most likely to be missed:
- **CIAM external contacts** — local accounts in the shared `spaarkeextid` external tenant, authority `*.ciamlogin.com`, requesting the CIAM BFF scope ([standalone-plane.ts:52-54](../../../src/client/external-spa/src/auth/standalone-plane.ts#L52)). **Broker-only** (token → BFF only, no OBO).
- **Workforce users** — workforce-multitenant `/organizations`, requesting the workforce BFF scope ([standalone-plane.ts:56-59](../../../src/client/external-spa/src/auth/standalone-plane.ts#L56)). Broker-only.

**Implications for each customer backend** if the standalone SPA is to reach it:
1. It must list the **single Spaarke SPA origin** (Q1) in CORS — same origin as the Teams case, so one entry covers both.
2. 🔔 **Owner/architecture decision**: must each customer backend accept the **CIAM external-contact plane**? If yes, every `sprk-{customerId}-prod-api` needs its `Ciam:Audience` + `Ciam:TenantId` configured for the **shared `spaarkeextid` tenant** (plus a per-customer CIAM scope the SPA requests). If external-contact access is instead meant to stay on a **shared** backend (not per-customer), then the standalone CIAM plane does **not** fan out to customer backends and your per-customer app-reg/CORS only needs the workforce plane. **This is the fork that decides how much per-customer CIAM wiring H3 must do.**
3. The directory endpoint from Q2 should return the CIAM `authority`/`scope` too, so the standalone CIAM path resolves the right customer backend the same way the Teams path does.

---

## 3. Summary of what changes in THIS SPA (once decisions land)

| Change | Where | Trigger |
|---|---|---|
| Custom-domain origin in 4 manifest refs + SPA redirect URIs | `appPackage/manifest.json`, app reg | Q1 domain confirmed |
| Decouple Teams client app from backend app (`webApplicationInfo.id` → Spaarke Teams client) | `appPackage/manifest.json` | Q2 agreed |
| Directory-endpoint lookup + customer picker; feed `apiBaseUrl`/`scope`/`authority` into existing runtime seams | `config.ts`, bootstrap (`main.tsx`), `TeamsHostAdapter`, `standalone-plane.ts` | Q2/Q3 agreed |
| Build-time single-value env → runtime per-customer resolution | `.env.*`, `config.ts` | Q2/Q3 agreed |
| (If Q3 = per-customer CIAM) CIAM scope/authority per customer in the directory payload | directory endpoint + `standalone-plane.ts` | Q3 decision |

**No engine rewrite** — the pluggable-authority + runtime-scope seams already exist; this is directory wiring + manifest/app-reg decoupling + env→runtime config.

## 4. Open decisions blocking us (please route to owner)

1. 🔔 **Production origin hostname** (Q1) — the single Spaarke custom domain.
2. 🔔 **Teams SSO fallback** (Q2) — drop it for multi-customer, or re-point at the Spaarke Teams client's own API? Any NAA-unavailable hosts to support?
3. 🔔 **CIAM external-contact plane vs per-customer backends** (Q3) — do customer backends serve external contacts (→ per-customer CIAM config + CORS), or does external-contact access stay on a shared backend?

---

## 5. RESOLUTION — owner decided all three (2026-10-07); R3 confirms alignment

Provisioning relayed the owner's decisions (t240 second message). R3 **agrees with all three** and confirms:

1. **Origin `https://external.spaarke.com`** — agreed. R3 owns: a prod Teams appPackage + prod registration, updating the manifest origin refs + SPA redirects, and deploying to `swa-spaarke-external-spa-prod`. Each is a live action R3 will confirm with the owner first. Keep the dev origin in CORS during transition.
2. **Drop the Teams-SSO fallback; dedicated Spaarke Teams client app = `webApplicationInfo.id` + NAA client** — agreed. R3 will confirm the supported Teams-host matrix all speak NAA before removing the fallback, and send the Spaarke Teams client id to provisioning once the owner creates the app.
3. **External contacts served by the customer's own backend** — agreed (D-13 forces it). **Contacts are browser-only** (CIAM can't sign into Teams) — confirmed by both sides.

**New item R3 absorbs: Model 1 workforce authority = Spaarke's tenant.** R3's `/organizations` multitenant shape is the Model 2 (deferred) path. For Model 1 (customer staff = B2B guests in Spaarke's tenant), workforce sign-in (Teams + browser) authenticates against **Spaarke's tenant**, like the add-in. This is the existing optional `authority` override — **config, not code**.

**R3's positions on the three 240d co-design points:**
- **CIAM audience → one per customer** (per-stamp audience in `spaarkeextid`), not shared — prevents cross-customer token replay.
- **Routing → invitation / deep-link scoped**, not the workforce directory (CIAM contacts aren't in the `sprk-*-users` groups it resolves). This **converges with R3's C3 deep-link** — the "you've been granted access to {record}" notification already targets one record on one stamp, so it carries `{customerId, apiBaseUrl}`. Cold-landing needs a contact→customers map in the shared registry (written at invite time) or a pick-organization prompt.
- **Provisioner → keyless federated credential** from the stamp MI — agreed; flag cross-tenant workload-identity federation (stamp MI → `spaarkeextid` app) as the item to validate for support.

All captured authoritatively in [`../design.md` §4.6](../design.md). R3's external-contact capabilities (C1 messages/detail, C2 external submission, C3 in-portal) are **gated on 240d**.
