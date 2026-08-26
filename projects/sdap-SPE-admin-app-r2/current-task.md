# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-26 16:55 UTC (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. §1–§6 are this session; §7 is preserved history.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | Post-deployment **UAT remediation** — not POML task execution |
| **Status** | in-progress — the app is **FUNCTIONAL** and deployed to dev |
| **Branch** | `work/sdap-SPE-admin-app-r2` · clean · pushed · 1 behind `origin/master` |
| **Head** | `8e0983b33` |
| **Next Action** | Move the container-type **configs UI** out of [`settings/SettingsPage.tsx`](../../src/solutions/SpeAdminApp/src/components/settings/SettingsPage.tsx) into [`container-types/ContainerTypesPage.tsx`](../../src/solutions/SpeAdminApp/src/components/container-types/ContainerTypesPage.tsx) behind an **Edit** toolbar button. The tab is **already relabelled "Environments"** in `AppShell.tsx`, so navigation promises this move — until the UI physically moves, **config editing is unreachable**. This is the only half-done item. |

### Deployed right now (dev)

| Surface | State |
|---|---|
| BFF `spaarke-bff-dev` | hash-verified live · `/healthz` 200 · publish 45.03 MB (ceiling 60) |
| Code page `sprk_speadmin` on **spaarkedev1** | 2,307 KB · published |

**Open it**: `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_speadmin`

### 🚨 Two hazards that WILL bite

1. **The dev BFF is contended.** On 2026-08-25 another session redeployed `spaarke-bff-dev` **52 minutes after** our fix landed, silently reverting it. UAT then failed with the *identical* original error and looked like our diagnosis had been wrong. **Before re-diagnosing any BFF bug, hash-compare the deployed DLL against the local build** (§6). Six deployments hit that app service in one day; only one was ours.
2. **Dataverse solution-import contention.** The code-page deploy failed once with `0x80071151 — another [Import] running`. Content uploads but does **not** publish. Re-run the same command, then verify `modifiedon`.

---

## 1. What this session did

Five UAT rounds against the deployed app. **Two root causes explained thirteen reported symptoms.**

| # | Fix | Commit |
|---|---|---|
| 1 | Key Vault credential pinned to the UAMI — recovered 5 dead screens | `d80efbbba` |
| 2 | Audit-log contract (envelope + every field name + `$top`) + `PageErrorBoundary` | `d80efbbba` |
| 3 | Four more envelope mismatches (search ×2, security alerts, container permissions) | `962ba102e` |
| 4 | Header/nav — title, spelled-out labels, right-align, tab order | `3983252a6` |
| 5 | Container-type `id` → `containerTypeId` — fixed the Register dropdown | `0ec6952de` |
| 6 | Four layout gaps + stringified permission scopes | `fbd88d5ce` |
| 7 | File Browser folded into Containers; Settings → Environments | `8e0983b33` |

**Merged to master as PR #824** (`3b87b07bc`) — that covers #1–#3 only.
⚠️ **Commits #4–#7 are branch-only and NOT on master.**

---

## 2. The recurring defect shape — read before debugging anything here

> **A lower layer collapses a real value — or a real failure — into an absent/empty/garbage result that an upper layer reads as benign.**

Confirmed **thirteen** times in this project. Three from this session:

- `.ToString()` on a Graph **collection** returned the collection's *type name*, so every permission scope rendered as `System.Collections.Generic.List\`1[System.String]`. Nothing errored.
- The wire sends container-type `id`; the client read `containerTypeId` → `undefined` on every type. Display survived because the *other* fields happened to match, so the list looked correct while everything keyed on the identifier failed silently.
- Five endpoints return `{items, count}` envelopes while the client declared bare arrays. TypeScript believed the annotation, because a declared return type is an assertion about JSON that **nothing verifies**.

**The method that worked:** when N screens fail identically, find the one that *works* and ask what it does differently. Container Types worked (delegated path, never touches Key Vault) while five app-only screens failed — that split named the bug without reading a single log line.

---

## 3. Remaining UAT items (operator-approved, not yet built)

1. **Configs UI move** — see Next Action. Half-done; navigation already promises it.
2. **Container Types master-detail** — details are in a cramped side pane; move below the list. Apply the *same* pattern to Containers so it is learned once.
3. **Fold Search into Containers** — Search is homeless because it *is*: it searches within the selected container type, which is exactly what Containers scopes. Takes nav 7 → 6.
4. **Dashboard** — "Config ID" column → container type **Name** (`containerTypeName` is already returned; display-only).
5. **Container Types → shared DataGrid component** — its own task; land it *after* the master-detail rework, not beside it.
6. Container list should show the **container id**; Environments page title description onto a second line.

### ⚠️ Flagged, awaiting an operator decision

**Single-select on the Containers grid.** The operator asked for it; I deliberately did **not** do it. Their stated reason ("since can't browse multiples") is fully satisfied by gating **Browse** on exactly one selection — whereas single-select would *also* disable Activate/Lock/Unlock/Delete across multiple containers and strand `BulkOperationService` behind an unreachable UI. Flip it only on explicit confirmation.

---

## 4. Not code defects — do not chase

- **Security alerts 403.** Graph says *"Account is not provisioned"* — the tenant lacks the Defender/security workload. That is **not** the missing `SecurityEvents.Read.All` grant our error message guesses at. Our wording is misleading and could be sharpened; the failure itself is not fixable in code.
- **File Browser folders** (`Communications`, `Emails`, `Exports`). **Nothing in this repo creates them** — searched every `.cs`/`.ts`/`.tsx`; the only hits are API route names. Their origin is a question about the tenant, not a bug. **Resolve before task 052 (item recycle bin) touches anything destructive.**
- **Folders "don't open"** — the grid wires folder navigation to `onDoubleClick`; single click only selects. Unconfirmed by the operator.

---

## 5. Structural finding — why these bugs shipped

**`vite build` does not typecheck.** `SpeAdminApp` ships with **38 pre-existing type errors** and has **no test runner** (`lint` invokes an uninstalled ESLint). A total client/server shape mismatch reached UAT because nothing between developer and operator checks shapes.

Correcting an earlier claim of mine in this file's history: I once wrote that client logic was "verified by `tsc` alone". **`tsc` is not in the build path at all.**

**Restructuring on this foundation will keep producing UAT rounds like these.** Recommend a task adding `tsc --noEmit` to the build plus a vitest runner **before** the remaining UX work.

---

## 6. Verification recipes

```bash
# Is the deployed BFF actually my build?  RUN THIS BEFORE RE-DIAGNOSING ANY BFF BUG.
TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)
L=$(sha256sum deploy/api-publish/Sprk.Bff.Api.dll | cut -d' ' -f1)
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://spaarke-bff-dev.scm.azurewebsites.net/api/vfs/site/wwwroot/Sprk.Bff.Api.dll" -o /tmp/r.dll
[ "$L" = "$(sha256sum /tmp/r.dll | cut -d' ' -f1)" ] && echo MATCH || echo "STALE — redeploy"

# What is ACTUALLY published to Dataverse (never trust the deploy message):
#   webresourceset(5f86c079-cd1f-f111-88b3-7ced8d1dc988)?$select=content  -> base64 decode -> grep
```

**Deploys — always `pwsh`, never `powershell`** (5.x lacks `Get-FileHash` in this harness):

- BFF: `rm -rf src/server/api/Sprk.Bff.Api/publish; pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1`
- Code page: clear `dist/* node_modules/.vite/ .vite/` first, then
  `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-SpeAdminApp.ps1 -Environment dev -DataverseUrl "https://spaarkedev1.crm.dynamics.com"`

> **We have NEVER deployed with slots.** `config/environments.json` declares `appServiceSlot: staging`; **zero slots exist**. Do not claim slot-swap rollback as a safety property — I did once, and it was wrong.

### Domain facts learned live

- **"Config"** = `sprk_specontainertypeconfig` — binds container type + business unit + environment + owning app registration + Key Vault secret name. Relabelled "Container Type" in the UI, but it is **not** the container type; that is why the picker cannot simply become the Container Types list.
- **ADR-028 E-1** covers per-customer **owning-app** credentials — **not** the BFF's own identity reaching Key Vault to fetch them. auth-v4 applied the exclusion one layer too wide; that is how the last bare `DefaultAzureCredential` in `src/` survived its sweep.
- `spaarke-bff-dev` has a **user-assigned** identity (`mi-bff-api-dev`, clientId `5967251e-171c-46fe-a6c2-ef843c90309d`) and **no system-assigned one**, so every credential MUST pin the client id. `ManagedIdentityCredentialFactory` exists for exactly this.
- Fluent v9 **`Divider` defaults to `flex-grow: 1`** — in a column layout it grows *vertically* and renders space-line-space. `flexShrink: 0` does not help.
- Master **branch protection is disabled** (the `merge-to-master` skill still claims it is on).

---

## 7. Preserved history — earlier sessions

### 🔑 The PATCH-400 was a missing `etag` (2026-08-25)

Two days of "container-type writes are impossible" was a missing **required body property**. Graph's Update API lists `etag` as REQUIRED **in the body**, and Microsoft's own *"Example 2: Update without ETag"* documents the 400.

| Identical no-op PATCH | |
|---|---|
| without `etag` | **400** |
| with `etag` in the body | **200** (beta AND v1.0) |
| round-trip: wrote 499 → read back 499 → restored 500 | ✅ task 023 AC-2 |

⚠️ It is a **BODY property, NOT the `If-Match` header.** An earlier session tried the header, correctly recorded that it changed nothing, and read that as "the etag is irrelevant" — which aimed the whole investigation at auth. Full record: [`notes/patch-400-resolution.md`](notes/patch-400-resolution.md).

> **THE LESSON, and it happened twice in one day:** both 400s were documented requirements returned as `invalidRequest` naming no cause, and both were one fetch of Microsoft's reference page away. **The corpus and the CSDL being silent is not the platform being silent.**

### Other prior findings

- **Nine POML premise errors, nine for nine** — task 027's core premise (supersession) was simply false. Verify a task's premise against code/CSDL before implementing it.
- **Master was red before our merge** — 6 tests asserted endpoints auth-v4 deliberately deleted; two were passing **vacuously** (asserting a no-access user gets 403 while *every* user got 403).
- **A remembered failure mode is a hypothesis, not a diagnosis** — 66 test failures were blamed on a remembered WireMock/MimeKit issue; reading the actual exception took one command and showed a ctor change.

---

## 8. Remaining POML backlog (untouched this session)

**041** (LiveIntegration suite + throwaway container fixture) → **042** → **051 / 050 / 052** → **061 / 062** → **090** (wrap-up; `/test-diet` is a **BINDING** gate).

> ⚠️ Project [`CLAUDE.md`](CLAUDE.md): **destructive tests MUST use a dedicated throwaway container.** The existing containers hold real working documents — signed NDAs, Compose drafts, matter files.
