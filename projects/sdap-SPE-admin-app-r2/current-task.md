# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-24 (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. History lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none in progress** — W1–W10 done + **011 completed**; 023 🔄 · 025 🔄 · 026 🔄 · 029 🔄 (all partial for one shared reason: the PATCH 400) |
| **Step** | Between tasks. Working tree **clean**, branch **level with origin**, 15 commits this session. |
| **Status** | All committed + pushed to `work/sdap-SPE-admin-app-r2` (draft PR **#811**). Latest: `9af552d9b`. |
| **Operator goal (stated 2026-08-24)** | **Get the app showing LIVE data and DEPLOY it.** Sequence work against that, not against task order. |
| **Next Action** | **Task 028** — container URL + Purview deep-link. Chosen because the operator's own M365 admin-center screenshots showed container URL as the most visible missing field. Then the two defects below, then 027. |

### 🔴 Task 011 completed — but be precise about what now works

The partial left **GET, CREATE and settings-SAVE on the app-only path**, which Graph rejects outright
(403 on v1.0 *and* beta). Three of four container-type ops could never work in production. All are now
on the delegated path that LIST already uses.

| Op | Before | After | Live-verified? |
|---|---|---|---|
| GET | 403 | expected to work | ❌ **no** |
| CREATE | 403 | expected to work (delegated create needs no admin role, design §4.2b) | ❌ **no** |
| **SAVE** | 403 | 🔴 **STILL BROKEN — 400** | ❌ no |

**SAVE was not fixed.** The failure moved from 403 to 400: the write now reaches Graph's own validation
for the first time instead of being rejected before it. That makes the PATCH-400 escalation the *actual*
blocker rather than a symptom hiding behind a 403. Do not report SAVE as working.

**Found by walking the UI, not by reading code** — five prior tasks touched this file and missed it.

### 🔴 Two REAL defects found in the UI review — neither owned by any task

1. **"Manage Permissions" (Search results) lands on the Dashboard.**
   [`ItemResultsGrid.tsx:552`](../../src/solutions/SpeAdminApp/src/components/search/ItemResultsGrid.tsx#L552)
   opens a tab at `?page=containers&containerId=…`, but `App.tsx` never reads those params — it only
   parses `configId`/`buId` from the Dataverse `data` param, and `activePage` always starts at
   `"dashboard"`. A tab opens, so it *looks* like it worked. Signature §2.4 shape.
2. **An expired trial container type is invisible on the grid.** The live tenant's trial expired
   **2025-10-10 (11 months ago)**. `expiryDateTime` renders in exactly one place —
   `ContainerTypeDetail.tsx:863` — as a plain date in the detail panel, with no indication it is past.
   Task 030 added the 30-day warning to the **create** dialog only. design §4.2b: *"a trial type simply
   stops working on day 31."* Also a now-stale comment at `ContainerTypesPage.tsx:108` claims the BFF
   never sends `expiryDateTime` — task 030 made that untrue.

### ✅ The nine-screen UI review happened (2026-08-24) — this was the missing gate

design §9's first acceptance criterion — *"all nine screens work against Spaarke Dev"* — had **never**
been exercised. Every prior fix was verified at the API/mapping/schema layer, which cannot prove a
screen renders. Uncomfortably close to R1's *"4176 passing"* for an app that never ran.

**How to re-run it:**
```
cd src/solutions/SpeAdminApp
npx vite --config vite.review.config.ts       # → http://127.0.0.1:5178
```
It runs the **REAL app** (real routing, AppShell, all nine screens) and swaps ONLY
`src/services/authInit.ts` — the single data choke point — for `dev-review/authInit.mock.ts`. Never used
by `npm run build`. Fixtures carry per-route provenance (live / partial / synthetic).

⚠️ **Bind `host: true`** — Vite's default came up IPv6-only (`[::1]`), which `curl` reached and the
browser did not. That cost three debugging rounds.

**Confirmed working against real captured data**: Dashboard **860.8 MB / 5 containers** (was `0 B`) ·
Containers per-row storage (was `—`) · Container Types owning-app populated + billing status "Unknown"
(correct) · Recycle Bin muted "Unknown" timestamp · Security's honest 403 · Settings has no Graph
Endpoint field (021 deleted it).

**Harness lessons (my bugs, not the app's) — don't re-derive:**
- BFF list shapes are **NOT uniform**: `businessunits`/`configs`/`environments`/`security/alerts`/
  `search/*`/`audit` are **bare arrays**; `containers`/`containertypes`/`recyclebin` are `{items,count}`;
  `dashboard/metrics`/`security/score` are bare objects. Wrong shape ⇒ `d.filter is not a function`,
  white screen. **TypeScript cannot catch this** — `get<T>()` CASTS, it does not parse. Same root cause
  as task 030's row-selection defect.
- `ContainerSearchResult` is `{ container: Container, score? }` — **nested**, not flat.
- Route matching must be **path-segment + method aware**. `includes()` made
  `/spe/containers/{id}/items` match `/spe/containers`, silently serving a wrong-but-plausible payload
  instead of a loud NO FIXTURE.

### ⚠️ Fixtures are partly synthetic — the operator noticed

File-browser items are invented and identical for every container; `createdDateTime` values were
invented from container *names* (M365 shows Spaarke Dev Container 2 created **5/28/26**; the fixture
says Sep 30 2025). **App-only Graph works for containers/files/storage**, so real captures are available
without an interactive sign-in — worth doing before any further UI review.

**M365 admin centre comparison (operator screenshots, 2026-08-24):** billing status is **Active** on all
four types (so `billingStatus` HAS a real value — our "Unknown" is a fixture artefact, not a code
defect) · **container URL** is shown there and missing here → **task 028** · storage 0.03 GB ≈ our
31.6 MB ✅ · container "Designer" belongs to a different owning app, correctly absent from our list.

**Two measurement gaps, still unowned:**
- **SpeAdminApp has no ESLint dependency, config, or install** — its `lint` script has never run.
  `tsc --noEmit` against a stashed baseline was substituted.
- **Publish-size baseline unreproducible.** 44.99 MB measured vs 43.67 recorded; stash-and-remeasure
  gave 44.99 both ways, so the true delta is **0.00 MB** and the *method* drifted. Don't chase it.

### 📋 Remaining work, ordered against the operator's goal (live data + deploy)

| Order | Item | Why here |
|---|---|---|
| 1 | **028** container URL + Purview deep-link | Most visible gap vs the M365 admin centre; straightforward |
| 2 | **Manage Permissions defect** (above) | Real, small, currently unowned |
| 3 | **Expired-trial gap** (above) | Real, small, in scope for FR-C13 |
| 4 | **027** container-type owner management | Needs the delegated path 011 just finished |
| 5 | **PATCH 400 escalation** | 🔔 **operator decision required** — see below |
| 6 | 050 archival · 051 quota ceiling · 052 item recycle bin | New capabilities (Workstream E) |
| 7 | 041 / 042 test suite · 060 / 061 / 062 hygiene | |
| 8 | **090 wrap-up** | `/test-diet` is a BINDING gate |

**Deployment** is not yet attempted. The code page builds clean (2.34 MB single file). No deploy task
exists in the WBS — decide whether it rides task 090 or gets its own.

### 🔑 Live verification is UNBLOCKED — use it

`az` works. App-only tokens work (owning app `170c98e1`, secret restored — see
[`notes/live-verification-credential.md`](notes/live-verification-credential.md)).
**Delegated** tokens work via device-code through **`SPAARKE-SPE-Admin-CLI`**
(`68cf5a14-1efb-4254-80bf-2761ffc89373`) — public-client flows on, both SPE scopes admin-consented.
Delegated needs ~20s of operator time at <https://microsoft.com/devicelogin>.

**Graph's OData `$metadata` needs no token at all** and settled task 025 outright. Reach for it first:
`curl -s https://graph.microsoft.com/{v1.0,beta}/$metadata`.

⏰ The restored credential **expires 2028-08-24**. When it lapses, every app-only SPE path fails silently.

### 🔴 Open escalation — container-type writes are impossible

**Every PATCH to a container type returns `400 invalidRequest`** — nested, top-level, full blob, a
no-op writing the current value back, v1.0, beta, `PUT`, `@odata.type`, `If-Match`, and even a bare
`{"name":…}`. App-only is 403 on GET *and* PATCH.

Leading hypothesis, **not proven**: only the **owning application** may modify its container type. If
so the write needs delegated-as-owning-app — the exchange task 010 proved unworkable — and the ADR-028
§6.5 gate must be re-run. **Blocks AC-2 for BOTH 023 and 025** (one shared escalation, not two).

Two decisive tests, both with side effects, awaiting an operator decision — see
[`notes/live-verification-2026-08-24.md`](notes/live-verification-2026-08-24.md) §2.

### 🔴 Findings that change the tasks ahead

| For | Finding |
|---|---|
| **026** | `consumingTenantOverridables` is a **comma-delimited flag string**, and the SDK's generated enum is **narrower than the live tenant** — 2 of the 3 live flags aren't members. Read it as a raw string; do NOT route through the typed enum. |
| **025 (reopen)** | The settings **form** is still unrebound — it reads the Dataverse config record, not the new Graph settings DTO. |
| **051** | Real ceilings exist: **25 TiB** on standard types, **200 MiB** on the trial. |
| **041** | Recycle bin is **empty**, so task 022's timestamp mapping is still WireMock-only. A throwaway container would confirm it in a minute. |
| **042** | `UpdateContainerTypeSettingsTests.cs` is flagged B16 scaffolding **with this session as evidence** — renaming four DTO properties broke every test in it without one having caught the defect. |

### Session summary — 10 commits

| Commit | Substance |
|---|---|
| `0a7220849` `e2c1b0cf6` | **030** — lifecycle constraints stated before submit; quota → **option A** (operator-confirmed: state limits, block trial on proof, **never publish a "remaining" figure**). Found: row selection had **never worked** (`id` vs `containerTypeId`, response cast not parsed). |
| `c5790afa3` | 🔴 **`billingClassification` null since the Graph 6 upgrade (2026-08-13)** — a comment naming SDK 5.101.0 outlived its truth. Found by writing the first test over that mapping. |
| `f87a2baa9` | **021** — deleted the Graph Endpoint setting. It was **fully persisted and validated**, read by nothing; `IsValidHttpsUrl` accepted **any** host, so wiring it would have been token exfiltration. |
| `6d489b6a1` | **022** — all three POML claims false. Real defect: `is string` could never match Kiota's `DateTime`. |
| `cb5840969` | **023** — writes were no-ops at **three** layers; 4th defect (`ValidSharingCapabilities`) was **defended by 10 tests**. |
| `f65c7dfcf` | **024** — storage implemented. Dashboard showed **`0 B`** for **861 MB**. |
| `16828fc9e` `2e3708761` | 🔴 **`spe-owning-app-secret` did not exist in Key Vault** — so *every* `…ForConfigAsync` path (containers, recycle bin, search, security, audit) could not build a Graph client. Restored; live-verified 022/024/030. |
| `7b37063c9` | **025** — `agent.chatEmbedAllowedHosts` **does not exist**; `sharingCapability` was omitted. Settings had **never** reached the client. |

### Uncommitted, deliberately

**Different repo**: `c:/code_files/spaarke-prototype` holds harness changes (`spe-admin-r2-uat`
scenarios seeded with the **real measured** 861 MB payload + Spaarke PAYGO 1 config id) **plus
unrelated modifications from other projects**. Left uncommitted — pushing another repo, and one with
other people's work in the tree, isn't this session's call.

Harness: `SPAARKE_REPO_ROOT="c:/code_files/spaarke-wt-sdap-SPE-admin-app-r2" npx vite` from that
directory. Was serving on **:5177**.

### 🔑 Task 024 — IMPLEMENT. Spike deliberately not re-run.

Task 020 had already measured it live, and the finding is **sharper than the POML's three options**:
availability is partitioned **by operation**, not by container — beta **LIST yes**, beta **GET no**,
v1.0 **not in the schema at all** (a `$select` returns 400). So the same container legitimately shows a
figure in the grid and none in a detail fetch.

The code had been **asking Graph for the field in its `$select` and discarding it** at four sites →
every Containers row read "—", and the Dashboard summed nothing into a confident **"0 B"** across a
tenant full of real documents.

Three things worth not undoing:
- **`ReadStorageUsedInBytes` accepts every numeric shape Kiota can produce.** Task 022's lesson applied
  preventively — the property is beta-only so the SDK does not type it, and **5 GB does not fit in an
  `int`**. A narrow match is exactly how the deleted-container timestamp was lost.
- **Null ≠ 0, in both directions.** Tests guard each way: absent → "Not reported"; a genuine 0 stays 0.
- **The Dashboard states its own coverage** (`storageReportingContainerCount`), so a partial sum is
  never presented as a total — that would be the same defect in miniature.

✅ **`SpeAdminGraphService.cs` now has ZERO `?? DateTimeOffset.UtcNow` fabrications on any read path**
(the last four cleared here, handed over by 023).
### 🔑 Task 023 — first POML whose claim held, and it still wasn't the bug

`itemMajorVersionLimit` / `maxStoragePerContainerInBytes` were **correct** — confirmed by reflecting
over `Microsoft.Graph` 6.5.0 (stronger than docs, and no live call needed; the SDK types **all nine**
settings). **But fixing the names alone would have changed nothing.** Three independent breaks, any one
sufficient:

1. Settings written **top-level** when they are a **nested `settings` object** → Graph ignores unknown
   top-level members on merge-PATCH → 200, nothing changed.
2. Server-side names wrong (`majorVersionLimit`, `storageUsedInBytes`, `isVersioningEnabled`).
3. 🔴 **The client already sent the CORRECT names** `isItemVersioningEnabled` / `itemMajorVersionLimit`
   — the **server DTO** spelled them differently, so JSON binding dropped both to null before the
   service ever ran. Nobody could see this from either side alone.

🔴 **Fourth defect, not in the POML, defended by its own tests.** `ValidSharingCapabilities` was
`{disabled, view, edit, full}` — three are not Graph values, and this set **is the endpoint's
allow-list**, so every value the client can send except `disabled` got a **400 from our own validator**.
**10 tests asserted the wrong values were correct**, so correcting it would have "broken tests". Now
derived from the SDK enum; retired names kept as explicit negatives.

**Fix uses the SDK's typed settings model** → property names are compiler-enforced. A misspelled
setting is now a build error, not a 200 that does nothing.

⚠️ **AC-2 not live-verified** — write→read-back needs an interactive Azure login this session can't do.

### 🔑 Task 022 — the POML described a bug that did not exist

**All three of its claims were wrong.** There was no OData parsing error; the `$select` did not request
an undeclared property; and the comment 11 lines below did **not** contradict the code — it correctly
said the value arrives via `AdditionalData`, and the code read `AdditionalData`.

The real defect (proven by task 040 against the real SDK): `rawDeletedAt is string` **can never match
Kiota's `System.DateTime`**. Graph sent the value, Kiota parsed it, it sat in the dictionary — and
production dropped it on a type check. `DeletedDateTime` was null for **every** row.

🔑 **The DTO and UI needed no change.** `RecycleBinPage.tsx` already sorted nulls last and already
rendered a muted **"Unknown"** rather than a blank or a fabricated date, and the empty state was
already distinguishable from a failure — so AC-3 and AC-4 were **already met**. The presentation layer
was honest the whole time and simply starved by the layer beneath it. That is what proves the fix
belongs at exactly one layer.

`$select` **removed** rather than corrected, matching the task-030 precedent. Task 040's two
characterization tests were inverted, as 040 instructed.

⚠️ **AC-1 not live-verified** — the `az` session expired and this session cannot run an interactive
login. Behaviour is pinned by WireMock against the real Kiota deserializer, which is where the defect
lived.

### 🔑 Task 021 — DELETED the Graph Endpoint field, and why not to restore it

**The POML aimed at the wrong entity.** It named `ContainerTypeConfig`; the field is actually on
**`sprk_speenvironment`**, surfaced by `EnvironmentConfig.tsx`. And it understated the defect: the
field was not merely inert — it was **HTTPS-validated on create AND update, written to Dataverse,
re-`$select`ed, and mapped into both response DTOs.** A complete round-trip for a value that **zero**
code paths consume. An admin could change it, save, reload, see it persisted — and change nothing.
**No test ever referenced it.**

🔒 **The decisive argument**: `IsValidHttpsUrl` accepts *any* HTTPS host. Wiring this field would let
anyone who can write an environment record point the BFF's **app-only Graph tokens** at an arbitrary
server. The field was safe only because it was dead — one well-meaning "finish wiring this up" commit
from being a credential leak.

Escalation trigger evaluated and did **NOT** fire: no sovereign-cloud reference exists anywhere in
`src/`, `docs/`, or `.claude/` (only `node_modules`).

⚠️ **AC-4 is partial and needs an operator.** The `sprk_graphendpoint` column and its stored rows
survive — deleting a Dataverse column is a schema change, not a code change. Steps are in
[`notes/graph-endpoint-decision.md`](notes/graph-endpoint-decision.md) §5.

**Deliberate deviation**: no WireMock test was added. For a DELETE outcome the only possible test is a
reflection assertion that the DTO lacks a property — a DTO-shape test, exactly the scaffolding ADR-038
bans and task 042 exists to delete. Compilation + grep prove absence more strongly.

### 🔑 Task 030 — what it changed, and the one thing NOT to undo

4 client files + 3 server files + 1 new test file. Gates: BFF build 0 errors · **10,652 tests** (+6) ·
ArchTests 36/36 · publish **43.67 MB, 0 MB delta** · code page builds · 0 type errors in touched files.

**Do NOT add a "N of 25 remaining" quota figure.** `describeProductionQuota()` returns
`atLimit: false` unconditionally *on purpose*: container-type LIST runs delegated, and task 012 proved
the BFF cannot see whether the caller holds the Entra role that widens visibility tenant-wide. The list
is a **lower bound, not a census**, so a remaining figure would be a guess presented as a fact. The
trial limit *is* enforced, because seeing a trial type proves one exists — the asymmetry is deliberate.

🔴 **Also fixed: row selection had never worked.** The DTO sends `id`, the client type declares
`containerTypeId`, and `speApiClient` **casts** the response instead of parsing it — so `getRowId` was
`undefined` for every row and the Register wizard always opened with no type. Normalised in-screen.

✅ **Field gap FIXED here** (operator decision, not deferred to 023/025). `owningAppId` and
`expiryDateTime` now flow Graph → summary → DTO → client. Root cause was the **`$select`**, not the
mapping: a hand-maintained projection never asked for them. **The `$select` was removed, not
extended** — naming properties explicitly re-arms the 400-on-a-wrong-name failure this workstream
exists to remove, and a tenant is capped at 25 container types so there is no size argument.

### 🔴 The real find — `billingClassification` has been null since 2026-08-13

Writing the **first test ever** over this mapping made 3 of 5 fail, one of them on pre-existing
behaviour. `MapContainerType` read the value only from `AdditionalData`, justified by a comment saying
*"Graph SDK 5.101.0 does not include the typed enum"*. True at 5.101.0 — but `dotnet-10-upgrade-r1`
task 033 moved the repo to **Graph 6.5.0, which types it**, so Kiota bound it to the typed property
and `AdditionalData` stopped carrying it. **Null for every container type since.**

Every lifecycle rule keys off that field. Both of 030's client blocks (trial-Register, trial-quota)
would have silently no-opped. Fixed typed-first / `AdditionalData`-second, plus
`NormalizeBillingClassification` (the SDK enum stringifies `Trial`; Graph and every client comparison
use `trial`).

**→ Task 023 must audit the other 6 `AdditionalData` fallbacks in that file** — all written under the
same expired 5.x assumption. A comment naming a dependency version is a claim with an expiry date.

Full record: [`notes/task-030-findings.md`](notes/task-030-findings.md).

### Files Modified This Session

All committed and pushed to `work/sdap-SPE-admin-app-r2` (draft PR **#811**):

| Commit | Contents |
|---|---|
| `5b3ef6194` | **Task 001** — 60 SpeAdmin error sites routed; `Redact`/`Explain`/`ExtractRequestId`/`ClientStatusFor`; client `describeApiError`; 28 tests |
| `753c9ebc1` | **Build fix** — 4 undeclared deps + 2 vite aliases; SpeAdminApp code page builds again |
| `f3747646b` | **Task 002** — 70-site `catch (ODataError)` inventory; ADR-007 fix in `BulkOperationService` |
| `aa69ce941` | **Task 003** — `SyncHealth`/`ConcernOutcome`; Dataverse-outage-looks-like-OK fixed; 9 tests |
| `356001ee7` | docs refresh |
| `44a239aab` | **Task 005** — Audit Log read **and** write paths repaired; 19 tests |
| `b6ffe09e5` | checkpoint |
| `8e3b954da` | **Task 040** — WireMock Graph fixture; **unblocked WireMock repo-wide**; 10 tests. No `src/` change |
| `b4922d9c1` | **Task 004** — Search repaired: wrong entity type + missing `region` + invalid `contentSources`; 16 tests. **Verified live** |
| `958ceef8b` | **Task 010** — OBO spike ⛔ **UNWORKABLE**; `BLOCKED.md` written; 011/012 blocked |

⚠️ **Separate repo, NOT pushed**: `c:/code_files/spaarke-prototype` has **1 unpushed commit** `a53832a`
(the `spe-admin-r2-uat` harness + shared `_infra` mock fixes) on `feature/uat-harness-framework`. Left
unpushed deliberately — pushing another repo needs the operator's say-so.

### 🔑 Task 020 — beta is DELIBERATE for containers; do not "clean it up"

**`storageUsedInBytes` is not in the v1.0 schema.** `$select` on it → **400 "Could not find a property
named 'storageUsedInBytes'"**; the identical call on beta → 200 with the value. `ownershipType` is
beta-only too. Measured live 2026-08-23, same tenant/token/moment. **Operator chose option A**:
containers stay on beta as a documented second exception.

Guarded by `SpeContainerGraphBaseUrl` (constant carrying the verbatim 400) +
`SpeAdminGraphVersionContractTests` — flip it to v1.0 and tests fail pointing at the evidence.

**Paging no longer hardcodes a host** — `ResolveGraphBaseUrl(graphClient)` derives it from the client
about to issue the request, so a nextLink can never point at a different version than page 1 (which
fails as "no more results", not as an error).

✅ **Task 024's spike is answered in advance: YES.** FR-C06 resolves to **implement**, not remove — Graph
exposes consumption on **beta**, **LIST-only** (even beta's GET-single omits it). Don't re-run the spike.

🔴 **Still broken, and it's 011's scope, not 020's**: container-type **GET and CREATE** route through
`…ForConfigAsync` → an **app-only** client, but container types are **delegated-only** (403 app-only on
both versions). 011 wired only LIST. One resource, two API versions *and* two auth models.
`CreateGraphClientFromBearerToken` (`:4278`) is effectively-dead — its only caller is the multi-app OBO
branch that can never succeed; removing it means removing `SpeAdminTokenProvider` too.

### 🔑 Task 013 — the multi-tenant fact, and a retraction

**A Spaarke environment can manage container types in CUSTOMERS' OWN Entra tenants** (operator-confirmed
2026-08-23). `sprk_speenvironment.sprk_tenantid` is why. This makes `GetClientForConfigAsync` **correct**:
the config selection chooses *whose tenant* Graph is called against.

❌ **Retracted**: I argued the Security path was a modeling error and the grant belonged on the BFF. That
assumed one tenant per environment. `IGraphClientFactory.ForApp()` authenticates in the BFF's **home**
tenant and could never read a customer's — so that option was unworkable, not just worse. The POML was
right. Struck in `notes/app-registration-topology.md`; don't re-invent it.

✅ Granted `SecurityEvents.Read.All` on `170c98e1` (exactly one permission; `ReadWrite` NOT granted).
**Secure Score returns 200 live.** → **Per-customer onboarding step**, now in `auth-deployment-setup.md` §5e.

🔔 **Alerts still 403 — different cause, escalated.** `Security.Alerts_v2` needs a **Defender workload**
provisioned; Spaarke Dev has none. Proof it isn't permissions: legacy `/security/alerts` returns **200,
empty array** on the same token/tenant/moment. **No broader grant can fix it.**

Also: **ADR-028 E-1 is partly rehabilitated** — task 010's "no per-customer owning app" is true of
Spaarke Dev only (Spaarke's own tenant, where owning app and browser client collapse onto `170c98e1`).
**010's OBO verdict stands** — the assertion always carries `aud = BFF`, so `Create(OwningAppId)` fails
regardless. Path A remains correct.

### 🔑 Task 012 — do not re-derive

**Entra directory roles are INVISIBLE to the BFF.** `SDAP-BFF-SPE-API` leaves `groupMembershipClaims`
unset, so no `wids` claim is ever emitted. Proven with a **positive control**: a real token for
`aud = api://1e40baad-…`, issued to a **confirmed member** of the tenant's SharePoint Embedded
Administrator role (`1a7d78b6-…`), carried **no `wids` at all** — while `roles` was present.

→ **Claim-absence does not mean role-absence.** Any filter check would tell genuine role holders they
lack the role. **Do not "complete" `SpeAdminAuthorizationFilter` by adding a `wids` check** — the code
says so inline, with the measurement.

The real defect was one layer down and unnamed by the POML: all four container-type ops passed a
hardcoded **500**, so a Graph **403 reached the admin as "Internal Server Error"**. Now: layer 1
(Spaarke app role, visible → filter) and layer 2 (Entra role, only Graph knows → 403-filtered catch),
each speaking only about what it can observe. Layer 2 names the role and what it enables but never
asserts the caller lacks it — 403 also covers unregistered types, consent gaps, wrong-tenant configs.

🔔 **Open operator decision (nothing depends on it)**: set `groupMembershipClaims: DirectoryRole` on
the BFF registration for *proactive* detection? Not taken unilaterally — that registration backs every
Spaarke client surface. See `notes/task-012-completion.md` §5.

`tests/integration/auth/**` was a **dead ADR-038 KEEP path** — README only, compiled by no project.
Now wired; 14 tests live there.

### Critical Context

✅ **Auth resolved — operator chose path A (BFF identity).** Container types run on
`IGraphClientFactory.ForUserAsync`, the BFF's **existing** OBO exchange. **No new `.WithClientSecret`
site** — the BFF already had four; SpeAdmin reuses one. `SpeAdminTokenProvider` is now **dead code on
this path** (it exchanges as `SDAP-PCF-CLIENT`, which exposes no `api://` URI → `AADSTS500011`).

✅ **Tenant isolation shipped** (`325511d5b`). `configId` was a bearer capability — 15 endpoints took
it with zero ownership check. Now `SpeAdminTenantScope` derives the caller's BU from the `oid` claim
(self + descendants) and `SpeAdminTenantScopeFilter` enforces it once on the `/api/spe` group, 404 not
403. ⚠️ **A config with no business unit is treated as accessible** (upgrade compatibility) — so
**every config MUST carry a BU before a shared multi-customer environment counts as isolated.**

🔴 **Outstanding docs debt**: ADR-028 **E-1** still describes a per-customer owning app that does not
exist for SpeAdmin. Amend it or the next project rebuilds on the same false premise.

Every real defect found has the **same shape**, and **none was where its POML said to look**: a lower
layer collapses a failure (or a real value) into an absent/empty result that an upper layer reads as
benign. **Verify a task's premise before implementing to it — seven for seven have now been wrong,
incomplete, or aimed at the wrong layer**, including the spec's own auth hypothesis and the §6.5 gate's.

---

## Full State

### Health at checkpoint

| Gate | Value |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors (7 pre-existing warnings) |
| Unit tests | **10,618 passed**, 0 failed, 97 skipped (+82 added this session) |
| ArchTests | 36/36 |
| Publish (compressed, framework-dependent linux-x64) | **43.66 MB** — under the ~44.96 MB baseline, ceiling 60 |
| New NuGet | none |
| CI | **deliberately not tracked** — operator said to disregard at this stage |

### 🔑 The recurring defect shape — three-for-three

| Task | Where the truth was lost |
|---|---|
| **003** | `LoadContainerTypeConfigsAsync` returned `Array.Empty<>()` on a Dataverse exception → indistinguishable from "none registered" → `SyncSucceeded = true` → green dashboard over a broken app |
| **005** | `SpeAuditService` swallowed every write failure → audit table silently **0 rows** for the life of the app |
| **002** | `BulkOperationService` caught raw `ODataError` (ADR-007 leak; fixed) |

**Look for this shape first in 004** — not for error swallowing in the Graph service.

### 🔑 Do not re-derive: the 70 `catch (ODataError)` sites are already correct

Two-layer design — inner `XAsync` catches **only 404** (`when`-filtered) → null/false; outer
`XForConfigAsync` translates everything else to `SpaarkeStorageException`. A 403/429/5xx is never swallowed.

An earlier task-001 note claimed *"28 of 70 swallow — those screens stay silent until 002 lands."* **Wrong**
(it never checked wrapper pairing). Corrected in [`notes/task-001-completion.md`](notes/task-001-completion.md)
and [`notes/odata-catch-inventory.md`](notes/odata-catch-inventory.md).

### Reusable mechanism — do not reinvent

| Helper | Use |
|---|---|
| `GraphErrorTranslator.ToProblemDetails(summary, errorCode, statusCode, traceId, title)` | Graph failures — code, upstream status, request id, traceId |
| `GraphErrorTranslator.ClientStatusFor(ex)` | Upstream→client status; Graph **401 → 502** so the client retry loop cannot swallow it |
| `ProblemDetailsHelper.Explain(summary, ex)` | Non-Graph failures — appends real type + message, redacted |
| `ProblemDetailsHelper.Redact(message)` | **Always** apply before putting upstream text in a payload |
| `GraphCallScope.Run(...)` / `.RunForConfig(...)` | Keeps `ODataError` inside `Infrastructure.Graph` (ADR-007 §1) |
| `SpeDashboardSyncService.DeriveHealth(concerns)` | "A failed concern can never report Healthy" |
| `SpeAuditService.MapCategory(text)` | Free text → `sprk_category` option-set int |
| `describeApiError(err, fallback)` (`speApiClient.ts`) | Client render sites — appends Graph code + request id |

> ⚠️ A summary passed to these **must not name a cause the caught exception did not establish.**

### 🔑 Task 040 done — the harness now exists, and it already earns its keep

`tests/integration/contract/SpeAdmin/GraphWireMockFixture.cs` + `README.md`. Use it for any
Graph-touching change. **Do not build a second one** — two Graph fakes already existed and were
correctly rejected as non-extendable (reasons in `notes/task-040-completion.md` §3a).

```csharp
using var graph = new GraphWireMockFixture();
graph.StubGet("/storage/fileStorage/containers", """{"value":[…]}""");
await sut.ListContainersAsync(graph.CreateGraphClient(), containerTypeId);   // real production method
graph.SelectFieldsFor("/storage/fileStorage/containers").Should().BeEquivalentTo("id", "displayName");
```

**Three facts worth not re-deriving:**

1. **WireMock was dead repo-wide, mislabeled.** Every request 500'd — WireMock.Net 1.5.45 loads
   `MimeKitLite` at runtime and the test csproj had `ExcludeAssets=all` (stripping the *runtime* asset,
   not just the compile-time collision it was added for). Now `compile`. If WireMock ever blanket-500s
   again, **check that first**. The 6 tests in `Integration/GraphApiWireMockTests.cs` sat skipped as
   *"path matching … requires configuration investigation"* — wrong, and it kept the one tool able to
   catch the §3.2 defect class dark for all of R1.
2. **The seam already existed.** 47 `SpeAdminGraphService` methods take `GraphServiceClient` as a
   parameter. The hardcoded `…/beta` is confined to the private `CreateGraphClient*` helpers.
   Escalation trigger evaluated → **did not fire**; **task 021's base-address decision is untouched**.
3. **KEEP path matters.** The POML's `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/` is not one of
   ADR-038's seven. New Graph tests go in `tests/integration/contract/SpeAdmin/`.

### 🔴 Defect handed to task 022 — do not re-derive

`SpeAdminGraphService.cs:4368` guards `deletedDateTime` with `rawDeletedAt is string`, but Kiota stores
a **`System.DateTime`** (probed against the real SDK). The guard can never be true, so **every
recycle-bin row reports a null deletion timestamp**. Found by the fixture on its first run.

Pinned as characterization tests that **must fail and be updated when 022 fixes it** — deleting one
instead would restore the silence. Same for the `StorageUsedInBytes: null` pin (task 024).

### Standing gap — UI verification

`<ui-tests>` from tasks 001 and 003 are still **NOT DONE**. The code page now *builds*, and a local harness
exists, but neither substitutes for a **deployed** app + `--chrome`.

- **Harness** (`spaarke-prototype/projects/spe-admin-r2-uat`, `npm run dev`, port varies — was **5176**)
  render-verifies task 003's four sync-health scenarios against the *real* `DashboardPage`.
- It **cannot** verify task 001's `authenticatedFetch → ApiError → describeApiError` path: the harness
  aliases `@spaarke/auth` to a mock that always returns 200, so that would test the mock, not the product.

This debt compounds through Workstream C, which is heavily UI. Worth a decision before then.

### Carry-forward

1. **🔔 Task 010 can reopen the auth decision.** §6.5 gate resolved as **path C** (comply under ADR-028 E-1),
   but two verified defects mean the owning-app OBO path cannot currently succeed as written
   (`SpeAdminTokenProvider.cs:142` audience; `:306` OBO actor). If 010 shows the shape is unworkable,
   **STOP and re-run the gate** — do not fall back to BFF-identity OBO silently. It is Opus tier / `xhigh`,
   and an `UNWORKABLE` verdict blocks 011 and everything from 020 onward.
2. **God-file serializes waves.** At most ONE task per wave may modify `SpeAdminGraphService.cs`.
3. **Task 004 is uncapped.** Search root cause not isolated; effort provisional.
4. **Live-tenant safety**: destructive tests need a dedicated throwaway container — existing containers hold
   real documents (signed NDAs, Compose drafts, matter files).
5. **A POML's premise can be wrong.** 001's `<relevant-files>` named 5 of 18 endpoint files (real scope 60
   sites, not 41). 002's premise did not hold at all. 003's held only one layer down. 005's pointed at the
   read path when the write path was equally broken. Under `mode="directional"` the `<goal>` binds.
6. **Residual ADR-007**: `BulkOperationService` still holds two `Microsoft.Graph.GraphServiceClient` locals —
   structural work for `speadmingraphservice-decomposition-r1`; recorded in the odata inventory.
7. **Dataverse MCP works** and is how 005's root cause was proven empirically. Reach for it before declaring
   something unverifiable against a live tenant.

### Session notes — key learnings

- **Two mistakes worth not repeating**: (a) `git stash push -- <path>` with nothing to stash creates no
  entry, so a following `git stash pop` pops *someone else's* stash — it dropped another project's WIP into
  this tree (reset, nothing lost); (b) pushing repeatedly cancels your own in-flight CI runs.
- **A confidently-worded wrong comment kept a bug alive for months.** `AuditLogEndpoints.cs:159` asserted
  lookup GUIDs "require single quotes"; 29 of the other 30 lookup filters in `src/` disagreed.
