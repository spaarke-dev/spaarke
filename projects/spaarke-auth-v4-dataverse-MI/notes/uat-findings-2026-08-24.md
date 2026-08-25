# UAT findings — 2026-08-24 (Outlook + Word add-in save)

> Both findings are **pre-existing defects surfaced by this project's deploy**, not regressions from the
> MI-FIC migration. Recorded here because this project's deploy is what made them visible.

---

## 1. ✅ FIXED — `/api/office/save` blocked by CORS preflight (UAT blocker)

**Symptom** (Outlook web + Word for Web, both after a successful add-in login):

```
Access to fetch at 'https://spaarke-bff-dev.azurewebsites.net/api/office/save'
from origin 'https://icy-desert-0bfdbb61e.6.azurestaticapps.net' has been blocked
by CORS policy: Response to preflight request doesn't pass access control check:
No 'Access-Control-Allow-Origin' header is present on the requested resource.
```

### Root cause — a code change whose config half was never applied

Commit `66a45cf6a` (2026-08-14, `code-quality-and-assurance-r3` task 030 / FR-17, D3-03) **removed the
blanket credentialed `*.azurestaticapps.net` allowance** from
[`CorsModule.cs`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/CorsModule.cs). That was a correct
security fix — `azurestaticapps.net` is a shared, third-party-registrable domain, and combined with
`AllowCredentials()` it let **any** attacker-owned SWA make credentialed cross-origin calls.

The commit message states the other half of the change explicitly:

> **DEPLOYMENT PREREQ (documented, like 023): live `Cors__AllowedOrigins__N` must list the SPA +
> Office-add-in origins**

That prerequisite was never executed on `spaarke-bff-dev`. The live allow-list held four origins and the
Office add-in origin was not among them. **This project's deploy carried the code to dev for the first
time**, so the config gap became a failure.

That the auth work was innocent is provable without argument: the add-in **logged in and authenticated
fine** — the failure is a browser preflight that never reaches application auth.

**Same class as** the compose-r8 `incident-2026-08-24-servicebus-config-mismatch.md`: code shipped, config
half missed, failure deferred until someone deployed.

### Live allow-list, before

| Index | Value | Assessment |
|---|---|---|
| 0 | `https://spaarkedev1.crm.dynamics.com` | redundant — covered by the `.dynamics.com` suffix rule |
| 1 | `https://spaarkedev1.api.crm.dynamics.com` | redundant — same |
| 2 | `https://agreeable-hill-00bcc911e-preview.westus2.1.azurestaticapps.net` | **STALE** — matches no SWA in the subscription; returns **404** |
| 3 | `https://spaarke.powerappsportals.com` | redundant — covered by the `.powerappsportals.com` suffix rule |

Net: of four configured origins, **three were redundant and one was dead**. Neither live SWA was listed.

### Fix applied

Both origins were verified as ours **before** being allowed — that verification is the entire point of the
rule that was removed, so it is not skippable:

| Origin | Verified as | Evidence |
|---|---|---|
| `https://icy-desert-0bfdbb61e.6.azurestaticapps.net` | SWA `spaarke-office-addins` (`spe-infrastructure-westus2`) | `az staticwebapp list`; also documented in [`office-addins-deploy/SKILL.md`](../../../.claude/skills/office-addins-deploy/SKILL.md) and `docs/guides/office-addins-deployment-checklist.md` — which even contains a CORS preflight curl using this exact Origin |
| `https://green-dune-0c4f1221e.7.azurestaticapps.net` | SWA `swa-spaarke-external-spa-dev` (`rg-spaarke-dev`) | `az staticwebapp list`; live (HTTP 200) |

```bash
az webapp config appsettings set -g rg-spaarke-dev -n spaarke-bff-dev \
  --subscription 484bc857-3802-427f-9ea5-ca47b43db0f0 \
  --settings "Cors__AllowedOrigins__4=https://icy-desert-0bfdbb61e.6.azurestaticapps.net" \
             "Cors__AllowedOrigins__5=https://green-dune-0c4f1221e.7.azurestaticapps.net"
```

The **external SPA origin was added too** — it was broken by the identical cause and would have been the
next UAT surprise.

### Verified after restart

```
/healthz = 200, 200, 200  (3 consecutive — per the health-races-the-drain lesson)

OPTIONS /api/office/save
  Access-Control-Allow-Origin:      https://icy-desert-0bfdbb61e.6.azurestaticapps.net
  Access-Control-Allow-Credentials: true
  Access-Control-Allow-Methods:     POST
```

### Open follow-ups (not this project's to decide)

1. **Remove the stale index 2** (`agreeable-hill-…-preview`). A dead `azurestaticapps.net` hostname sitting
   in a **credentialed** allow-list is precisely the attacker-registrable risk class `66a45cf6a` set out to
   close — if that name is ever re-issued, the allow-list hands it credentialed access. Left in place only
   because deleting indexed settings mid-UAT is gratuitous; it should go.
2. **Config drift has no forcing function.** The origins live only in App Service settings. Nothing fails
   in CI when a deployed environment's allow-list omits a live SWA. `66a45cf6a`'s own follow-up note called
   this out (*"OFFICE_ADDINS_ORIGIN token registry+values (063/r1 config-drift)"*) and it is still open.
3. **`.powerappsportals.com` is the same shared-domain risk class**, flagged but not fixed by `66a45cf6a`
   (its own comment says so). Index 3 makes the blanket rule redundant for our origin anyway.

---

## 2. ⚠️ NOT FIXABLE HERE — `/healthz/catalog` = Unhealthy (503)

**Pre-existing.** `RoutingConsumerTypeHealthCheck` (ADR-039 / FR-P0-04) reconciles the two closed AI
catalogs at boot. Drift report captured from startup logs after the restart above — **17 findings in three
dimensions**:

### Dimension 1 — Binding rows with no `ConsumerTypes` constant (8) → **Unhealthy**

```
agreement-classify, compose-make-concise, compose-rewrite-instruction, create-project,
create-todo, list-tasks, nda-review, nda-standard-summary
```

Independently reconciled against `ConsumerTypes.All` (27 constants) and `sprk_playbookconsumer`
(35 distinct values) — the report matches exactly. **All 27 constants have rows**; these 8 are the reverse.

**These are not admin typos.** Each is a well-formed, deliberate consumer type seeded into shared dev by
another **in-flight** project ahead of its BFF constant landing on master — `agreement-classify`
(agreements-r1), `nda-review` / `nda-standard-summary` (nda-r1), `compose-make-concise` /
`compose-rewrite-instruction` (compose), `create-todo` / `list-tasks` (smart-todo), `create-project`.

**Not fixable here, and should not be**: deleting the rows breaks those projects' active UAT; adding the
constants means shipping code for capabilities this project does not own.

> **Design tension worth an owner decision.** The check is deliberately asymmetric
> ([`RoutingConsumerTypeHealthCheck.cs:519`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/RoutingConsumerTypeHealthCheck.cs#L519)):
> a **constant without a row** is a blessed forward-declaration → *Degraded*; a **row without a constant**
> is the admin-typo class (the 2026-06-24 `matter-pre-fil` incident) → *Unhealthy*. That asymmetry is sound
> for a single-owner environment. In a **shared dev environment with ~17 active worktrees**, a row landing
> ahead of its constant is the normal steady state — so `/healthz/catalog` is effectively **un-greenable in
> dev**, and a permanently-red gate stops being read. Worth revisiting by the AI-catalog owner: the
> mirror-image case arguably deserves the same *Degraded* treatment, distinguishing "unknown value that
> matches no naming convention" (typo) from "well-formed value awaiting its constant" (forward-declared row).

### Dimension 2 — Registered handlers with no `sprk_analysistool` row (2) → **Unhealthy**

```
DailyBriefingOverviewHandler, GridOverviewHandler
```

**This is a live functional defect, not cosmetic.** Since the FR-P2-01 cutover the catalog is the *only*
tool projection — a handler without a row **cannot be invoked**. `grid_overview` is therefore dead in dev
*despite* commit `fb06291c0` having just fixed its grounding for R4 UAT. The fix was deployed; the row was
never seeded.

**Fixable here** — both have authored row JSONs (`infra/dataverse/sprk_analysistool-grid-overview-row.json`,
`…-daily-briefing-overview-row.json`); `scripts/Seed-TypedHandlers.ps1` creates them.

### Dimension 3 — Live `sprk_description` diverges from the compiled mirror (7) → **Unhealthy**

Descriptions are the LLM-facing tool text, so divergence degrades tool selection.

| Handler | Row name | Re-seedable? |
|---|---|---|
| `DataverseReadQueryHandler` | `SYS-Dataverse Read Query` | ✅ authored JSON exists |
| `EmailDraftToolHandler` | `SYS-Email Draft` | ✅ authored JSON exists |
| `SendWorkspaceArtifactHandler` | `SYS-Send Workspace Artifact` | ✅ authored JSON exists |
| `DocumentClassifierHandler` | `Document Classifier` | ❌ no authored JSON |
| `GenericAnalysisHandler` | `General Analysis` | ❌ no authored JSON |
| `SemanticSearchHandler` | `Search Documents` | ❌ no authored JSON |
| `SummaryHandler` | `Summary Generator` | ❌ no authored JSON |

The seed's upsert carries a **safety filter requiring `sprk_name` to start with `SYS-`** — which is exactly
why the bottom four are untouched by it. They are legacy rows owned by the AI-catalog surface.

### Verdict — can it be fixed here?

**Partially, and it will not turn the probe green.** `Seed-TypedHandlers.ps1` (`-WhatIf` verified: ~39
`SYS-` upserts) resolves **5 of 17** findings — dimension 2 entirely, plus 3 of 7 in dimension 3. The other
12 belong to other projects. `/healthz/catalog` stays **Unhealthy** either way.

**Not run.** The blast radius is ~39 upserts across other teams' live catalog rows — and the seed is
*authoritative by design* (*"a live edit is silently reverted"*), so running it mid-UAT could revert
in-flight work belonging to compose / agreements / nda. It does not achieve the stated goal, so it is not
worth that risk during an active UAT window. Recommended **after** UAT:

```powershell
pwsh -File scripts/Seed-TypedHandlers.ps1          # restores grid_overview + daily-briefing-overview
```

Remaining 12 findings should be booked to the AI-catalog owner, not to this project.

---

# UAT round 2 — the add-in save flow itself

With CORS unblocked, `POST /api/office/save` succeeded and created job
`82d09a70-…`. Three further defects then surfaced. **All three are pre-existing, and none is an auth-v4
regression** — every one is inbound claim reading or a route string; no OBO or credential path participates.

Fixed together in `77f61574b` and deployed, because all three needed the same redeploy.

## 3. ✅ FIXED (BLOCKER) — 403 `OFFICE_009` on every job poll, for every user

```
GET /api/office/jobs/82d09a70-… → 403
{ "errorCode": "OFFICE_009", "detail": "You do not have access to this job" }
```

**Inverted claim precedence on the same identity.**

| Path | Resolves userId as | Role |
|---|---|---|
| `OfficeEndpoints.SaveAsync` (was line 214) | `NameIdentifier` (**`sub`**) → `oid` | **stamps** the job's `CreatedBy` |
| `OfficeAuthFilter.ExtractUserId` | **`oid`** → `NameIdentifier` → `sub` | what `JobOwnershipFilter` **compares** |

`sub` and `oid` are different values in every Entra user token (`sub` is pairwise per-application; `oid` is
the tenant-wide object id). The save stamped one and the filter checked the other, so
`CreatedBy != userId` **always** → permanent 403 for every user.

**Established by elimination, not by pattern-matching** — worth recording, because a 403 is
[shape #1 in the lessons-learned](lessons-learned.md) (*fail-closed: a broken lookup and a legitimate
denial look identical*):

1. A 403 requires `CreatedBy` non-empty **and** `!= userId` (`JobOwnershipFilter`).
2. The Dataverse fallback in `GetJobStatusAsync` **never sets `CreatedBy`** → it can only yield 200 or 404,
   never 403. So the job was served from the in-memory `_jobStore`.
3. The plan is **capacity 1** with **1 live instance**, so POST and GET hit the same process — a
   cross-instance in-memory miss is excluded.
4. `CreatedBy` is stamped in exactly one place (`OfficeService.cs:216`), fed from `SaveAsync`.

⇒ The precedence divergence is the only reachable cause.

**Fix**: `SaveAsync` now reads `Items[OfficeAuthFilter.UserIdKey]` first — which is what the sibling
`GetJobStatusAsync` and `StreamJobAsync` handlers **already did**. The correct pattern existed 300 lines
away in the same file.

> **Deliberately NOT fixed**: five other `OfficeEndpoints` handlers still use the raw
> `NameIdentifier`-first pattern. **None of them stamps `CreatedBy`**, so none is reachable by this bug.
> Changing identity-resolution semantics blind across five untested endpoints on a fail-closed surface is a
> worse trade than leaving a latent inconsistency that is now documented. Flagged for the owning project.

**Verification**: requires a real user token, so this one is **yours to confirm in UAT** — it cannot be
proven from the server side without impersonating a user.

## 4. ✅ FIXED — SSE preflight rejected `Cache-Control`

`SseClient.ts:136` sends `Cache-Control: no-cache` on every stream open, and `Last-Event-ID` on reconnect.
Neither is a CORS-safelisted request header, and neither was in `CorsModule`'s `WithHeaders(...)`.

Both added. `Last-Event-ID` matters independently: sent only on **reconnect**, so omitting it fails later
and far more confusingly than `Cache-Control` does.

```
OPTIONS /api/office/jobs/{id}/stream  →  204
Access-Control-Allow-Headers: …,Cache-Control,Last-Event-ID
Access-Control-Allow-Origin:  https://icy-desert-0bfdbb61e.6.azurestaticapps.net
```

## 5. ✅ FIXED — server emitted `StatusUrl`/`StreamUrl` without the `/api` prefix

`OfficeService.cs` (6 sites) returned `/office/jobs/{id}` and `/office/jobs/{id}/stream`. The client builds
the SSE URL as `` `${apiBaseUrl}${streamUrl}` `` → a path that does not exist. Polling was unaffected only
because the client **hardcodes** `/api/office/jobs/${jobId}` — which is why the symptom looked
SSE-specific.

Proven after deploy:

```
/api/office/jobs/{id}/stream → 401   (route exists, auth required)
/office/jobs/{id}/stream     → 404   (route never existed)
```

### The forcing-function failure worth keeping

`OfficeEndpointsContractTests.cs:77` **already asserted the correct contract**:

```csharp
result.StatusUrl.Should().Contain("/api/office/jobs/");
```

…but it carries `[Fact(Skip = "Requires fully mocked Office services - ContainerId not configured in test")]`.
The contract was written correctly, then disabled, and the implementation drifted away from it unopposed.
This is the same lesson as the OBO harness in [lessons-learned.md §6](lessons-learned.md): **a skipped check
reads exactly like a passing one.** The skip was not un-skipped here — its stated blocker (unmocked Office
services) is real and unrelated to the assertion — but a test whose only *live* purpose is to hold a
contract, while skipped, is holding nothing.

---

## Deploy record (`77f61574b`)

| | |
|---|---|
| Package | **45.04 MB** vs 44.96 MB baseline (**+0.08 MB**) — NFR-01 ceiling 60 MB ✅ |
| Hash-verify | 4/4 critical files SHA-256 matched |
| Health | 3 consecutive `/healthz` 200s |
| Tests | Office/Cors/Duplicate/Idempotency **300 passed, 0 failed**, 13 skipped · `Spaarke.ArchTests` **56/56** |
| Secret-free posture | unchanged — no credential, DI, or config surface touched |

**Still open**: retest Outlook + Word save. Finding 3 is the one that needs a human with a real token.
