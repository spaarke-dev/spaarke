# SRFR-053 — Fix BFF Push Auth (401 Unauthorized) via MSAL Bearer Token

**Task**: SRFR-053 — Port the MSAL auth pattern from `sprk_communication_send.js` to `sprk_fieldmapping_push.js` so the Push Updates ribbon can authenticate against the BFF.
**Rigor**: FULL (touches production JS webresource used by ribbon UAT)
**Started**: 2026-07-06
**Completed**: 2026-07-06
**Related**: SRFR-061 (original push webresource), SRFR-062 (ribbon wiring), SRFR-082 (initial webresource deploy)

---

## 1. Root cause

Owner UAT on the Matter Push Updates ribbon button hit:

```
POST spaarke-bff-dev.azurewebsites.net/api/v1/field-mappings/push 401 (Unauthorized)
```

`sprk_fieldmapping_push.js` v1.0.0 (shipped by SRFR-061) issued the push request as:

```js
fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", "Accept": "application/json" },
    body: JSON.stringify(body),
    credentials: "include"   // <-- cookie-based auth
});
```

The BFF `POST /api/v1/field-mappings/push` endpoint requires a Bearer JWT (OBO). It does NOT accept cookie-credentials from Dataverse origins — the SRFR-061 header comment's claim ("BFF is configured … to accept host-context credentials") was incorrect. No MSAL token acquisition was ever wired into this webresource. The issue has been present since v1.0.0 shipped in SRFR-082 (2026-07-02); owner UAT is the first end-to-end exercise, and it surfaced immediately.

Wave 4 SRFR-040 built the presave webresource with the same "no MSAL, cookies only" assumption but that path never crosses origins (Xrm.WebApi only) so it never surfaced. Push crosses origin from `*.crm.dynamics.com` → `spaarke-bff-dev.azurewebsites.net`, which is where the assumption breaks.

## 2. Fix — v1.1.0

Ported the MSAL auth pattern from the CANONICAL `sprk_communication_send.js` reference (Communication Send ribbon). Same MSAL CDN version, same env-var names, same silent → SSO → popup fallback chain.

### Added helpers (in `sprk_fieldmapping_push.js`)

| Function | Purpose |
|---|---|
| `resolveMsalConfig()` | Reads `sprk_MsalClientId`, `sprk_BffApiAppId`, `sprk_TenantId` from `environmentvariabledefinition` (default-value fallback), derives `redirectUri` from `Xrm.Utility.getGlobalContext().getClientUrl()`. Cached on module. |
| `loadMsalLibrary()` | Loads `msal-browser@2.38.0` from `https://alcdn.msauth.net/browser/2.38.0/js/msal-browser.min.js` — SAME URL as `sprk_communication_send.js` so browser cache hits when both buttons are used in the same session. |
| `initMsal()` | Creates `msal.PublicClientApplication` with resolved config. Reuses `Sprk.Communication.Send._msalInstance` or `Spaarke.Email._msalInstance` if either is already present on the page — avoids duplicate popups. |
| `getAuthTokenForBff()` | Public API: silent → SSO → popup fallback. Returns access token for scope `api://{bffAppId}/user_impersonation` or `null` on total failure. |
| `ssoAndPopupFallback()` | Shared SSO-then-popup branch. |

### Modified `pushOne()`

```js
return getAuthTokenForBff().then(function (token) {
    if (!token) {
        throw new Error("BFF auth token acquisition failed");
    }
    return fetch(url, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "Accept": "application/json",
            "Authorization": "Bearer " + token  // <-- Bearer JWT, not cookies
        },
        body: JSON.stringify(body)
    });
}).then(function (response) { ... });
```

When token acquisition throws, `pushSequentially`'s per-target `.catch` handler buckets the failure as a network error and increments `agg.networkErrorCount` — the aggregate toast surfaces "N target(s) failed to reach the server" so the user gets clear feedback instead of a mysterious 401.

## 3. ADR-028 Path A exception (project-scoped)

`@spaarke/auth` is TypeScript; vanilla-JS webresources cannot consume it. This webresource uses `msal-browser` from CDN directly — the SAME approach already used by `sprk_communication_send.js`, `sprk_emailactions.js`, and `sprk_DocumentOperations.js`. This is the established pattern for Dataverse ribbon webresources and does not introduce a new auth mechanism; it just aligns Push with Send.

Rationale documented in the module-header `# Auth` section of `sprk_fieldmapping_push.js` (source of truth for future maintainers).

## 4. Ribbon changes — NONE required

**Key finding**: The MSAL library is loaded from CDN (`https://alcdn.msauth.net/browser/2.38.0/js/msal-browser.min.js`) at runtime via a `<script>` tag injected by the webresource itself. It is NOT a separate Dataverse webresource. Ribbon XML files do not reference `msal-browser` in any capacity.

Grep verification:
```
$ rg 'msal-browser|sprk_msal' infrastructure/dataverse/ribbon
# → 0 matches
```

The three existing MSAL-consuming ribbon webresources (`sprk_communication_send.js`, `sprk_emailactions.js`, `sprk_DocumentOperations.js`) all load MSAL from the same CDN URL at runtime.

Therefore `infrastructure/dataverse/ribbon/MatterRibbons/Entities/sprk_Matter/RibbonDiff.xml` and `infrastructure/dataverse/ribbon/ProjectRibbons/Entities/sprk_Project/RibbonDiff.xml` remain **UNCHANGED**. The single `<Library>` entry for `$webresource:sprk_fieldmapping_push` is sufficient — the JS itself bootstraps its own MSAL dependency on demand.

**Note on deployed env**: There is no `sprk_msal_browser.js` webresource on spaarkedev1. Confirmed via `Grep msal.*sprk_` across the repo — no other module references such a resource. No blocker.

## 5. Deploy sequence

Built a fresh minimal solution `FieldMappingPushUpdate v1.1.0` containing ONLY the updated webresource, using the SAME WebResourceId (`14c667f4-5a40-4570-9037-2e611c22de31`) so Dataverse treats it as an UPDATE, not a new resource — the existing ribbon `$webresource:sprk_fieldmapping_push` references continue to resolve without modification.

| # | Step | Result |
|---|---|---|
| 1 | Auth check | `pac auth list` → active connection = SPAARKE DEV 1 |
| 2 | Update JS | `src/client/webresources/js/sprk_fieldmapping_push.js` v1.0.0 → v1.1.0 (+~250 LOC of MSAL helpers + `pushOne` modification) |
| 3 | Syntax check | `node --check sprk_fieldmapping_push.js` → clean |
| 4 | Stage build | `c:/tmp/srfr-053-build/FieldMappingPushUpdate/` — same solution-folder template as SRFR-082, single WebResource RootComponent |
| 5 | Pack | `pac solution pack --zipfile FieldMappingPushUpdate_v1.1.0.zip --folder FieldMappingPushUpdate --packagetype Unmanaged` → 13,694 B zip, `Processing Component: WebResources — sprk_fieldmapping_push` |
| 6 | Import + publish | `pac solution import --path FieldMappingPushUpdate_v1.1.0.zip --publish-changes` → **Solution Imported successfully. Published All Customizations.** |

## 6. Deploy verification

Web API query via Dataverse MCP:

```sql
SELECT webresourceid, name, displayname, description, modifiedon
FROM webresource
WHERE name = 'sprk_fieldmapping_push'
```

| webresourceid | name | description | modifiedon |
|---|---|---|---|
| `14c667f4-5a40-4570-9037-2e611c22de31` | `sprk_fieldmapping_push` | "Ribbon action + EnableRule for the Push Updates to Related Records button on parent-entity forms. v1.1.0 adds MSAL Bearer token auth (SRFR-053)." | 2026-07-06T23:34:38 |

Same GUID as SRFR-082 baseline (14c667f4-…) — the existing `MatterRibbons`/`ProjectRibbons` `$webresource:sprk_fieldmapping_push` references resolve unchanged. `MatterRibbons 1.0.0.0` and `ProjectRibbons 1.0.0.0` were NOT re-imported (not required — MSAL loads from CDN, no ribbon library entry needed).

Solutions present in spaarkedev1 after deploy:

| uniquename | version |
|---|---|
| `MatterRibbons` | 1.0.0.0 (unchanged) |
| `ProjectRibbons` | 1.0.0.0 (unchanged) |
| `SetRegardingWebResources` | 1.0.0 (unchanged; still bundles the old-but-now-superseded webresource metadata; live webresource content is the v1.1.0 payload from `FieldMappingPushUpdate`) |
| `FieldMappingPushUpdate` | 1.1.0 (NEW — this task's delivery) |

## 7. LOC diff

- **`src/client/webresources/js/sprk_fieldmapping_push.js`**: +303 lines / −16 lines (760 → 1050). Bulk of the addition is the MSAL helper block (`resolveMsalConfig`, `loadMsalLibrary`, `initMsal`, `getAuthTokenForBff`, `ssoAndPopupFallback`) + module-header `# Auth` section rewrite + `pushOne` token-acquisition wrapping.
- **`infrastructure/dataverse/ribbon/MatterRibbons/Entities/sprk_Matter/RibbonDiff.xml`**: unchanged (0 lines).
- **`infrastructure/dataverse/ribbon/ProjectRibbons/Entities/sprk_Project/RibbonDiff.xml`**: unchanged (0 lines).

## 8. UAT next steps

Owner should:
1. Refresh the Matter form in spaarkedev1 (hard reload to bypass browser cache of the old push JS).
2. Click "Push Updates" — on first click the browser may pop up an MSAL consent window if this is the user's first BFF Bearer-token acquisition in this tab (subsequent clicks silent).
3. Confirm the request headers include `Authorization: Bearer eyJ…` and the BFF returns 200 or a BFF-side ProblemDetails (200 = success; anything else = downstream issue to investigate).

Verify same on a Project record.

If the token popup fires unexpectedly on subsequent clicks, check that `sessionStorage` MSAL cache is not being wiped by a page reload.

## 9. Files changed

| Path | Change |
|---|---|
| `src/client/webresources/js/sprk_fieldmapping_push.js` | v1.0.0 → v1.1.0: added MSAL Bearer token acquisition; replaced `credentials: "include"` with `Authorization: Bearer <token>` on `POST /field-mappings/push` |
| `projects/set-regarding-and-field-mapping-resolver-r1/notes/task-053-bff-push-auth.md` | New — this log |

Build artifact (not committed): `c:/tmp/srfr-053-build/FieldMappingPushUpdate_v1.1.0.zip` (13,694 B).

## 10. Blockers

None. Deploy succeeded; downstream UAT unblocked.

## 11. Constraint compliance

- **§3 sub-agent write boundary**: this task did not touch `.claude/`.
- **ADR-028 Spaarke Auth v2 (Path A exception)**: documented in-file. Rationale = vanilla-JS webresources cannot consume the `@spaarke/auth` TypeScript package; same rationale already accepted for three sibling ribbon webresources.
- **RegardingResolver PCF (SRFR-052 scope)**: NOT modified. Version 1.3.0 remains untouched.
