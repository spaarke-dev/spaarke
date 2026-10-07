# Task 041 — per-environment Entra SPA redirects and the deploy-workflow trigger

> **Executed** 2026-09-17 in the main session, on the owner's authorization ("proceed with the correct
> technical fix"). Rigor STANDARD, model opus, steps prescriptive.
>
> **Outcome: no write was necessary.** Both redirect URIs NFR-09 requires were already registered on the
> add-in's app registration for the only environment that exists. This note records the verification, because
> an unverified "it was already fine" is indistinguishable from a skipped task.

---

## 1. Identity used

`az account show` → **`ralph.schroeder@spaarke.com`**, type `user`, tenant
`a221a95e-6abc-4434-aecc-e48338a1b2f2` ("Spaarke Devlopment Environment").

The operator's **own identity**, not a service principal — as NFR-11 and ADR-028 require. No client secret was
created, rotated, read or stored at any point in this task.

## 2. The environments the add-in is deployed to

`az staticwebapp list` returns three Static Web Apps in the subscription, of which exactly **one** hosts the
add-ins:

| SWA | Host | Role |
|---|---|---|
| **`spaarke-office-addins`** (rg `spe-infrastructure-westus2`) | **`icy-desert-0bfdbb61e.6.azurestaticapps.net`** | **the add-in host** |
| `swa-spaarke-website` | `ambitious-bay-0fb5bb10f.1.azurestaticapps.net` | marketing site — not an add-in host |
| `swa-spaarke-external-spa-dev` | `green-dune-0c4f1221e.7.azurestaticapps.net` | external-access SPA — not an add-in host |

So NFR-09's "every environment the add-in will be deployed to" is, today, **dev only**. There is no second
add-in environment to register. When one is provisioned, §6 below is the recipe.

### The host is the one the manifest actually uses

Both required URIs must embed the host the manifest loads from, so the host was taken from the **deployed
artifact**, not from a repo file:

- `GET https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml` → **200**, `<Version>` =
  **1.0.8.0**. The only origins it contains are `icy-desert-0bfdbb61e.6.azurestaticapps.net` (every resource
  URL), `login.microsoftonline.com` and `spaarke.com` (AppDomains / support URL). No `localhost`.
- That matches what the build produces: `webpack.config.js:52-54` resolves
  `ADDIN_BASE_URL = process.env.ADDIN_BASE_URL || (isProduction ? 'https://icy-desert-0bfdbb61e.6.azurestaticapps.net' : 'https://localhost:3000')`
  and `:218` substitutes it into `word/word-manifest.xml` on the way to `dist/word/manifest.xml`.
  `deploy-office-addins.yml` does not set `ADDIN_BASE_URL`, so production builds take the default — the same
  host registered below.

## 3. Pre-change state of the app registration (read BEFORE any write was considered)

**`Spaarke Office Add-in`** — appId **`c1258e2d-1688-49d2-ac99-a7485ebd9995`**. This is the app the pane
authenticates as: `deploy-office-addins.yml:66` injects exactly this id as `ADDIN_CLIENT_ID`.

SPA redirect URIs, as read on 2026-09-17:

| # | URI | Note |
|---|---|---|
| 1 | `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/auth-callback.html` | ✅ **NFR-09 requirement 2** |
| 2 | `brk-multihub://localhost` | local dev NAA broker |
| 3 | `brk-multihub://icy-desert-0bfdbb61e.6.azurestaticapps.net` | ✅ **NFR-09 requirement 1** |
| 4 | `brk-9199bf20-a13f-4107-85dc-02114787ef48://icy-desert-0bfdbb61e.6.azurestaticapps.net` | host-scoped broker scheme |
| 5 | `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/outlook/taskpane.html` | Outlook pane |
| 6 | `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/auth-end.html` | see §5 — target 404s |
| 7 | `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/auth-dialog.html` | see §5 — target 404s |
| 8 | `https://localhost:3000/auth-dialog.html` | local dev |
| 9 | `https://spe-api-dev-67e2xz.azurewebsites.net/office/auth-callback` | pre-migration BFF host |
| 10 | `https://localhost:3000/taskpane.html` | local dev |

Other platforms: `web.redirectUris` is empty; `publicClient.redirectUris` holds only
`https://login.microsoftonline.com/common/oauth2/nativeclient`. Both required URIs are registered under the
**SPA** platform, which is what NFR-09 specifies — registering them as Web or Public client is the documented
cause of AADSTS7000471 and is not the case here.

## 4. Post-change state

**Identical to §3. Nothing was added, changed or removed** — both required URIs (#1 and #3) were already
present for the only deployed host, so the correct action was to write nothing. Every URI present before the
task is present after it, trivially.

This satisfies the task's goal as written: *"Every environment the add-in will be deployed to has both required
SPA redirect URIs registered on its Entra app, verified by reading the registration back."* The verification is
§3; the read-back and the pre-change read are the same read because no write intervened.

## 5. Two observations recorded, deliberately not acted on

1. **Entries #6 and #7 point at pages that do not exist.** `HEAD /auth-end.html` and `HEAD /auth-dialog.html`
   both return **404** on the deployed site (`/auth-callback.html` and `/word/manifest.xml` return 200). A
   registered redirect whose target does not exist is **inert** — it is only reachable if code asks for it, and
   the add-in's MSAL config asks for `/auth-callback.html`. The task's additive-only constraint forbids removing
   them, and removal is not recoverable from this task's information: another surface, or a future dialog-based
   auth flow, may be their reason for existing. **Left in place; flagged for whoever owns add-in auth cleanup.**
2. **Two unused app registrations exist with add-in-sounding names** — `Spaarke Word` (`85c60e7c-…`) and
   `Spaarke Outlook` (`197e4097-…`), both with **zero** SPA redirect URIs. Neither is referenced by the deploy
   workflow or by any add-in config; the shipped add-in authenticates as `c1258e2d-…`. Registering redirects on
   them would have been wrong. **No change made.** Recorded so a future reader does not mistake them for the
   add-in's registration.

## 6. Recipe for the next environment (when one is provisioned)

For a new add-in SWA at `<swa-host>`, register on that environment's add-in app registration, under the **SPA**
platform, exactly:

- `brk-multihub://<swa-host>`
- `https://<swa-host>/auth-callback.html`

Add only; never replace an existing entry. Then read the registration back and confirm both are present
alongside everything that was there before. `scripts/Register-EntraAppRegistrations.ps1` and
`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.3 carry the operator procedure. The add-in derives the
broker host from `window.location`, so **no code change is needed per environment** — only the registration.

## 7. Deploy-workflow trigger — DECISION: `workflow_dispatch`, no file change

`.github/workflows/deploy-office-addins.yml` triggers on `push` to `master` and
`work/SDAP-outlook-office-add-in` (path-filtered), **and already declares `workflow_dispatch`**.

**Decision: rely on `workflow_dispatch`. `work/spaarkeai-word-add-in-r1` is NOT added to the push branches.**

Evidence it works — three successful dispatch runs on this very branch:

| Run | Event | Branch | Result | When |
|---|---|---|---|---|
| `34546485352` | `workflow_dispatch` | `work/spaarkeai-word-add-in-r1` | success | 2026-09-11 |
| `34532444044` | `workflow_dispatch` | `work/spaarkeai-word-add-in-r1` | success | 2026-09-10 |
| `34414699298` | `workflow_dispatch` | `work/spaarkeai-word-add-in-r1` | success | 2026-09-09 |

Rationale, in order of weight:

1. **It is already proven on this ref** — the add-in was deployed from this branch three times this way. A
   branch trigger would add a second mechanism to do what one already does.
2. **A push trigger would auto-deploy a feature branch to the SHARED dev SWA.** There is one add-in
   environment; every push here touching `src/client/office-addins/**` would overwrite what other people are
   testing, with no human in the loop. Deploy timing should stay deliberate.
3. **It requires no change to the deploy workflow at all**, which keeps this task's diff empty and its risk at
   zero. (Root CLAUDE.md §11: the cost-of-doing-nothing test fails for the branch trigger — nothing breaks
   without it.)

**For task 042, the launch command is:**

```powershell
gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1
```

This must also be stated in the PR description so 042 does not go looking for a push trigger.

## 8. Constraint + acceptance verification

| Requirement | Result |
|---|---|
| Both NFR-09 URIs present, **SPA** platform, for every deployed environment | ✅ §3 — present for `icy-desert-…`, the only one |
| Every pre-existing redirect still present | ✅ §4 — no write occurred |
| Host matches the Word manifest as 011 left it | ✅ §2 — deployed manifest 1.0.8.0 uses only that host |
| Workflow triggerable for this branch + approach stated | ✅ §7 — `workflow_dispatch`, evidenced by 3 runs |
| Frozen `ci-router.yml` / `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml` untouched | ✅ this task changed **no** file under `.github/` |
| No client secret created/rotated/stored; no `.WithClientSecret`; KV secrets untouched | ✅ — read-only Graph/az calls, no secret operation |
| Deploy workflow NOT executed by this task | ✅ — 042 owns deployment |
| `deploy-office-addins.yml` still valid YAML, jobs unaltered | ✅ — unmodified |

**Hot-path / conflict check.** Tags include `ci-cd`, so `.github/workflows/**` is on the watchlist. Two open PRs
touch `deploy-office-addins.yml`: **#960** (ours — the task-027 `ORG_URL` addition) and **#909** (dependabot,
`actions/setup-node` 4→7). Because this task edits no workflow file, there is **no overlap**. #909 will still
conflict with whoever edits that workflow next — noted for 042.

## 9. What changed as a result of this task

Only this note, plus the TASK-INDEX/POML status updates. That is the honest output: the configuration NFR-09
demands was already correct, and the trigger question is answered with a documented choice rather than a code
change.
