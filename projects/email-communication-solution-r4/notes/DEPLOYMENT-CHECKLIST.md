# email-communication-solution-r4 — Deployment Checklist

> **Authored**: 2026-07-16. **Scope**: everything r4 built that must reach an environment (dev → prod).
> **Golden rule (owner 2026-07-16)**: **anything that deploys BFF.api or a SpaarkeAi code page requires the worktree to be updated to master first, then `/merge-to-master`, THEN deploy.** Never deploy BFF/code-page artifacts straight off a feature worktree.

---

## 0. What r4 produced that needs deploying

| Artifact | Where | Deploy vehicle |
|---|---|---|
| **BFF.api** — rungs 4/5, `ICommunicationClassificationAi`, suggestion endpoint, per-rung telemetry, index-config tokenization, archive endpoint | `src/server/api/Sprk.Bff.Api/` | App Service deploy (after merge-to-master) |
| **appsettings** — `Communication:SemanticMatch`, `Communication:AiClassification`, tokenized `AiSearch` index names | App Service config / Key Vault | Env config (not code) |
| **Connections PCF** (`CommunicationConnectionsSolution_v1.0.2.zip`) | `src/client/pcf/CommunicationConnections/` | `pac solution import` |
| **Actions PCF** (`CommunicationActionsSolution_v1.0.1.zip`) | `src/client/pcf/CommunicationActions/` | `pac solution import` (re-import v1.0.1) |
| **Dataverse schema** (`sprk_regardingservicerequest`, `Suggested`/`Ambiguous` values, `sprk_domain`, etc.) | spaarkedev1 | ✅ already created by owner |
| **Ribbon Send retirement** (`sprk_communication_send.js` ×2 + send button) | deployed solution | remove at PCF re-import (043 remainder) |
| **OOB form config** — place both PCFs, wire auth env-vars, pack Awaiting-Association view | spaarkedev1 form | maker UI (043 remainder) |

---

## A. BFF.api deployment (REQUIRES merge-to-master first)

**A1. Update this worktree to current master** (pick up anything merged since the branch diverged)
```bash
cd c:/code_files/spaarke-wt-email-communication-solution-r4
git fetch origin master
git merge origin/master        # or rebase; resolve any conflicts
```

**A2. Full build + test green on the merged state**
```bash
dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release   # 0 errors
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter Communication          # all green (352+)
```

**A3. §10 gates** (binding before any BFF PR/merge)
- **Publish-size** (compressed, ceiling ≤60 MB; r4 baseline ~45.30 MB incl-PDBs):
  ```powershell
  dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
  Compress-Archive deploy/api-publish/* deploy/check.zip -Force
  (Get-Item deploy/check.zip).Length/1MB   # expect ~45 MB
  ```
- **CVE**: `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive` → confirm no NEW HIGH (the `Microsoft.Kiota.Abstractions 1.21.2` High is pre-existing/transitive).
- **`/conflict-check`** against other active worktrees on `Services/Communication` + `Services/Ai`.

**A4. Merge to master**
```bash
/merge-to-master          # merges work/email-communication-solution-r4 → master
```
(Or `/push-to-github` → PR → merge, if a reviewed PR is required. Pushing to origin ≠ merged.)

**A5. Deploy the BFF App Service** (from master)
- Use the repo's BFF deploy path: `scripts/Deploy-BffApi.ps1` OR the `azure-deploy` skill OR the CI/CD pipeline (`.github/workflows/sdap-ci.yml`). Deploy to the **staging slot** first (`spaarke-bff-dev` / slot `staging` per `config/environments.json`), verify `/healthz`, then swap.
- App Service target (dev): `spaarke-bff-dev`, RG `rg-spaarke-dev`.

**A6. Configure new App Service settings** (these are config, not code — set per environment)
| Setting | Value (dev) | Notes |
|---|---|---|
| `Communication__SemanticMatch__Enabled` | `true` | rung-4 kill-switch (no redeploy to flip) — ✅ set on `spaarke-bff-dev` |
| `Communication__AiClassification__Enabled` | `true` | rung-5 kill-switch — ✅ set on `spaarke-bff-dev` |
| `Communication__AutoFile__Enabled` / `__Threshold` | `true` / `0.85` | ✅ **set explicitly 2026-07-18** (previously relied on `AutoFileOptions` code defaults `true`/`0.85`; now explicit so config is self-documenting + the E-1 kill-switch UAT is a clean toggle) |
| `AiSearch` index tokens | per-env values | 075 tokenized these — dev resolves to real names (`spaarke-records-index` @ AllowedIndexes__2, `spaarke-invoices-index` @ __6, all 8 populated) |
| `Communication__WebhookSigningKey` / `__WebhookClientState` | KV refs | ✅ **moved to Key Vault 2026-07-18** (mirror prod) — `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Communication-WebhookSigningKey \| Communication-WebhookClientState)`. Same vault + MI as 6 existing resolving refs. Definitive resolution check = first inbound webhook (UAT D-1). |
| Graph/Dataverse MI, OpenAI, Search keys | existing | unchanged by r4 |

**A7. Verify**
- `GET /healthz` 200.
- `POST /api/communications/{id}/suggest-associations` returns candidates (read-only; never writes).
- Inbound email → 6-rung association writes `sprk_associationprovenance` + status; check per-rung telemetry logs (`EventId 4501/4502`).

---

## B. PCF deployment (043 owner remainder — no BFF merge required for PCF-only)

PCFs are unmanaged solution imports; they do NOT require the BFF merge (but keep versions in lockstep with the API they call).

**B1. Import both solutions to spaarkedev1**
```bash
pac solution import --path src/client/pcf/CommunicationConnections/Solution/bin/CommunicationConnectionsSolution_v1.0.2.zip --publish-changes
pac solution import --path src/client/pcf/CommunicationActions/Solution/bin/CommunicationActionsSolution_v1.0.1.zip --publish-changes
```
(If CPM blocks import: temporarily rename `Directory.Packages.props` → `.disabled`, import, restore.)

**B2. OOB `sprk_communication` form config** (maker UI)
- Place **Connections PCF** on the accessories column, bound to `sprk_associationprovenance` + `sprk_associationstatus`.
- Place **Actions PCF**, bound to `sprk_communicationtype`. Auth resolves from Dataverse env vars (`sprk_MsalClientId`=170c98e1, `sprk_BffApiAppId`=1e40baad, `sprk_BffApiBaseUrl`) — **zero form config** needed (v1.0.1 env-var fallback).
- Confirm the attachment subgrid + "Add Existing" doc picker.

**B3. Retire the deployed Send ribbon**
- Remove the deployed `sprk_communication_send.js` web resource + its send button. **KEEP the Create-To-Do button** (separate live feature).

**B4. Pack the Awaiting-Association view**
- Publish `CommunicationConnections/views/Communications-Awaiting-Association.md` as a system view on `sprk_communication`.

**B5. Verify**
- Hard refresh (`Ctrl+Shift+R`); both PCFs render; Actions footer reads **v1.0.1**; sign-in works; a test reply sends.

---

## C. Auth / environment facts (spaarkedev1)
- **clientAppId = `170c98e1-d486-4355-bcbe-170454e0207c`** (SDAP-PCF-CLIENT). The old `5175798e-…` was retired → `AADSTS700016`.
- **bffAppId = `1e40baad-…`** · **tenant = `a221a95e-…`**.
- App-registration reactivation NOT needed (SP enabled; SPA/PKCE — no client secret).

---

## D. Order of operations (recommended)
1. **A1–A4** merge BFF to master (so the suggestion/archive endpoints + rungs exist server-side).
2. **A5–A7** deploy + configure + verify the BFF.
3. **B1–B5** import PCFs + form config + ribbon retirement + verify.
4. Smoke-test end-to-end: inbound email → association + provenance → Connections PCF renders → suggestion endpoint returns candidates.

**Not in this deployment** (re-homed / deferred): Responsive Intelligence auto-actions (W5 → RI project); Outlook add-in suggestion UI (deferred, broader add-in strategy).
