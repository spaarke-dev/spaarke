# TASK-INDEX — sdap-SPE-admin-app-r2

> **Generated**: 2026-08-21 by `/project-pipeline` · **30 tasks** · lint: **30 clean / 0 errors / 0 warnings**
> **Branch**: `work/sdap-SPE-admin-app-r2` · **Draft PR**: [#811](https://github.com/spaarke-dev/spaarke/pull/811)
> Status legend: 🔲 not started · 🔄 in progress / needs retry · ✅ complete · ⛔ blocked

---

## ⚠️ Read before dispatching any wave

**The god-file caps concurrency.** Nearly every task modifies
`Infrastructure/Graph/SpeAdminGraphService.cs` (4,911 LOC). **At most ONE task per wave may modify it.**
Those tasks carry `parallel-safe=false` and run in the main session; `parallel-safe=true` tasks run as
agents alongside. Realistic concurrency is **2–3 agents**, not the 6-agent maximum. See [`../plan.md`](../plan.md) §3.

**Task 010 can reopen the auth ADR gate.** It is the project's highest-risk task. A `UNWORKABLE` verdict
blocks 011 and requires re-running the CLAUDE.md §6.5 block — not a silent fallback.

---

## Task Registry

| # | Task | Phase | FR | Rigor | Model | Effort | Wave | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 001 | [Real error surface via ProblemDetails](001-real-error-surface.poml) | 1 A | A01 | FULL | sonnet | high | W0 | ❌ | — | ✅ |
| 002 | [Audit 70 `catch (ODataError)` sites](002-odata-error-audit.poml) | 1 A | A02 | FULL | sonnet | xhigh | W1 | ❌ | 001 | ✅ |
| 003 | [Sync Status reflects real outcomes](003-sync-status-truth.poml) | 1 A | A03 | FULL | sonnet | high | W1 | ✅ | 001 | ✅ |
| 005 | [Diagnose + fix Audit Log](005-fix-audit-log.poml) | 1 A | A05 | FULL | sonnet | xhigh | W1 | ✅ | 001 | ✅ |
| 004 | [Diagnose + fix Search](004-fix-search.poml) | 1 A | A04 | FULL | sonnet | xhigh | W2 | ❌ | 001 | ✅ |
| 010 | [🔔 SPIKE — owning-app delegated token](010-obo-spike.poml) | 2 B | B01 | FULL | **opus** | xhigh | W2 | ✅ | — | ✅ **UNWORKABLE** |
| 040 | [WireMock Graph fixture infrastructure](040-wiremock-harness.poml) | 2 D | D01 | FULL | sonnet | high | W2 | ✅ | — | ✅ |
| 011 | [Wire hybrid delegated path](011-hybrid-delegated-path.poml) | 2 B | B02 | FULL | **opus** | xhigh | W3 | ❌ | 010 | ✅ **completed 2026-08-24** — 🔴 the partial left **GET, CREATE and settings-UPDATE on app-only**, which Graph rejects outright (403). 3 of 4 container-type ops could never work in production. Now all delegated |
| 012 | [Operator role prerequisite message](012-operator-role-message.poml) | 2 B | B03 | FULL | sonnet | high | W3 | ✅ | 010 | ✅ |
| 013 | [Grant `SecurityEvents.Read.All`](013-security-events-grant.poml) | 2 B | B04 | STANDARD | sonnet | medium | W3 | ✅ | 001 | ✅ **granted; Secure Score live. Alerts blocked on a NON-permission cause — escalated** |
| 020 | [`/beta` → v1.0 migration](020-beta-to-v1-migration.poml) | 3 C | C01 | FULL | sonnet | high | W4 | ❌ | 011, 040 | ✅ **option A — containers stay on beta (documented); paging now derives base addr** |
| 030 | [Lifecycle constraints in UI](030-lifecycle-constraints-ui.poml) | 3 C | C13 | FULL | sonnet | high | W4 | ✅ | 011 | ✅ **quota → option A (operator, 2026-08-23); delete affordance does not exist → new task. 🔴 Found + fixed: `billingClassification` null since the Graph 6 upgrade** |
| 021 | [Graph Endpoint setting — wire or delete](021-graph-endpoint-setting.poml) | 3 C | C02 | FULL | sonnet | high | W5 | ❌ | 020 | ✅ **DELETED** — field was on `sprk_speenvironment`, not the config; fully persisted + validated, read by nothing. ⚠️ Dataverse column removal is an operator action |
| 022 | [Fix recycle-bin `$select`](022-recycle-bin-select-fix.poml) | 3 C | C03 | FULL | sonnet | medium | W6 | ❌ | 020, 040 | ✅ **POML described a different bug.** No OData error existed; the value was dropped on a `is string` type check. 040's 2 characterization tests inverted |
| 023 | [Property names + quota/consumption split](023-property-names-and-quota-split.poml) | 3 C | C04, C05 | FULL | sonnet | high | W7 | ❌ | 020, 040 | 🔄 **shape+names LIVE-CONFIRMED; 🔔 WRITE BLOCKED — every PATCH 400s, escalation open.** POML's names were RIGHT (a first) — but the write path was broken at **3** independent points, plus a 4th defect **defended by 10 tests** |
| 024 | [SPIKE + branch — storage consumption](024-storage-consumption-spike.poml) | 3 C | C06 | FULL | sonnet | high | W8 | ❌ | 023 | ✅ **IMPLEMENT** — spike pre-answered by 020; beta LIST-only. All 4 nulls + all 4 `UtcNow` fabrications gone |
| 025 | [Full 9-property settings surface](025-full-settings-surface.poml) | 3 C | C07 | FULL | sonnet | high | W9 | ❌ | 023, 040 | 🔄 **server complete; form deferred.** 🔴 FR-C07 listed a property that **does not exist** (`agent.chatEmbedAllowedHosts`) and omitted one that does (`sharingCapability`) |
| 026 | [Replication + override state](026-replication-and-override-state.poml) | 3 C | C08 | FULL | sonnet | high | W10 | ✅ | 025 | 🔄 **AC-2 ESCALATED — not achievable from an owning tenant.** 🔴 `consumingTenantOverridables` is a **permission**, not a state |
| 029 | [Billing status surface + warning](029-billing-status-surface.poml) | 3 C | C12 | FULL | sonnet | medium | W10 | ❌ | 020 | 🔄 **AC-1 partial (live render).** 🔴 `billingStatus` appeared **nowhere in the repo** — 0 occurrences. ⚠️ POML's ∥-safe claim was WRONG — it does modify the god-file |
| 027 | [Container-type owner management](027-container-type-owner-management.poml) | 3 C | C09 | FULL | sonnet | high | W11 | ❌ | 011, 020, 040 | 🔲 |
| 028 | [Container URL + Purview deep-link](028-container-url-and-purview.poml) | 3 C | C10, C11 | FULL | sonnet | medium | W12 | ❌ | 020, 040 | 🔲 |
| 041 | [LiveIntegration suite + throwaway fixture](041-live-integration-suite.poml) | 4 D | D02 | FULL | sonnet | high | W13 | ✅ | 011, 040 | 🔲 |
| 042 | [Retire scaffolding tests (ADR-038)](042-retire-scaffolding-tests.poml) | 4 D | D03 | FULL | sonnet | high | W14 | ✅ | 040, 041 | 🔲 |
| 050 | [Container archival](050-container-archival.poml) | 5 E | E01 | FULL | sonnet | high | W15 | ❌ | 020, 040 | 🔲 |
| 051 | [Per-container quota ceiling](051-quota-ceiling.poml) | 5 E | E02 | FULL | sonnet | high | W16 | ❌ | 023, 024, 040 | 🔲 |
| 052 | [Item recycle bin (207 handled)](052-item-recycle-bin.poml) | 5 E | E03 | FULL | sonnet | high | W17 | ❌ | 022, 040, 041 | 🔲 |
| 060 | [Hygiene — dead stub + misfiled file](060-hygiene-stub-and-misfiled.poml) | 6 | F01, F02 | STANDARD | sonnet | low | W18 | ✅ | — | 🔲 |
| 061 | [Refresh SPE knowledge corpus](061-knowledge-corpus-refresh.poml) | 6 | X01 | MINIMAL | sonnet | medium | W18 | ✅ | 025 | 🔲 |
| 062 | [Billing-attach cross-project handoff](062-billing-attach-handoff.poml) | 6 | X02 | MINIMAL | sonnet | low | W18 | ✅ | 029 | 🔲 |
| 090 | [Project wrap-up + `/test-diet` gate](090-project-wrap-up.poml) | 7 | — | STANDARD | sonnet | high | none | ❌ | all | 🔲 |

---

## Wave Execution Plan

Each wave holds **at most one** `parallel-safe=false` GraphService task. Build verification runs between waves.

| Wave | Tasks | Prerequisite | Concurrency | Notes |
|---|---|---|---|---|
| **W0** | 001 ✅ | — | 1 (serial) | **Done 2026-08-21.** 60 error sites routed; endpoint-layer only (no GraphService change needed). ⚠️ UI verification blocked — SpeAdminApp build broken by a pre-existing missing dep; see `notes/task-001-completion.md` |
| **W1** ✅ | **002** ✅, 003 ✅, 005 ✅ | 001 ✅ | 3 | **002 done 2026-08-21** — premise did not hold; the 70 sites were already correct (404-filtered + translating wrappers). One real ADR-007 defect found + fixed in `BulkOperationService`. 003 owns DashboardSync; 005 owns AuditService |
| **W2** | **004** ✅, 010, **040** ✅ | 001 ✅ | 3 | **004 done 2026-08-21** — root cause proven by live Graph calls: `fileStorageContainer` is **not a valid `/search/query` entity type**. 🔔 **The spec's app-only hypothesis (§3.1) is DISPROVEN — neither escalation trigger fired, and task 011 does NOT inherit Search.** Three defects fixed (container entity type; missing `region`; invalid `contentSources`). |
| **W2** cont. | | | | **040 done 2026-08-21** — WireMock was unusable repo-wide (MimeKitLite runtime asset stripped by `ExcludeAssets=all`; the recorded "path matching" reason was wrong). Escalation trigger evaluated, did NOT fire — 47 GraphService methods already take `GraphServiceClient` as a param, so task 021's base-address decision is untouched. Fixture found a 🔴 **new defect for task 022** (see below). **Only 010 remains in W2** — notes-only; 🔔 it may still reopen the ADR gate |
| **W3** | **011** 🔄, **012** ✅, 013 | 010 ✅ | 3 | **012 done 2026-08-22** — Entra directory roles are **invisible to the BFF** (`groupMembershipClaims` unset → no `wids`), proven with a positive control: a token issued to a **confirmed holder** of the SharePoint Embedded Administrator role carries no `wids` at all. So claim-absence ≠ role-absence, and layer 1 must never speak about directory roles. Real defect was one layer down: all four container-type ops hardcoded **500**, so a Graph **403 read as "Internal Server Error"**. Now reported at the layer that is authoritative for each. 011 owns TokenProvider + GraphService; 013 is Azure config |
| **W4** | **020** ✅, 030 🔄 | 011, 040 ✅ | 2 | 020 owns GraphService; 030 is client-only. **030 done 2026-08-23 — partial.** POML facts all verified sound (a first), but **five further constraints were missing from it**, two of them traps: a trial type **expires at 30 days** and **cannot be registered on another tenant** — yet Register was offered for it. 🔴 Also found: **row selection has never worked** (DTO sends `id`, client reads `containerTypeId`, response is cast not parsed) → fixed. 🔔 **Quota escalated**: the visible list is a lower bound, not a census (task 012 — no `wids`), so "N of 25 remaining" would be a guess. Limits stated + trial blocked on proof; no remaining figure published. **Constraint 4 undeliverable**: there is no container-type delete affordance to make conditional. See [`../notes/task-030-findings.md`](../notes/task-030-findings.md) |
| **W5** | **021** ✅ | 020 ✅ | 1 | GraphService + config + client. **Done 2026-08-23 — DELETE.** POML aimed at the wrong entity (`ContainerTypeConfig`; it is actually `sprk_speenvironment`) and understated the defect: the field was not merely inert, it was **HTTPS-validated, written to Dataverse on create AND update, re-`$select`ed, and mapped to both response DTOs** — a complete round-trip for a value **zero** code paths consume, with **no test coverage at all**. 🔒 Decisive argument for DELETE: `IsValidHttpsUrl` accepts *any* HTTPS host, so wiring it would let an admin point app-only Graph tokens at an arbitrary server — the field is safe only because it is dead. Escalation trigger evaluated, did **NOT** fire (no sovereign-cloud need anywhere in the repo). ⚠️ **AC-4 partial**: the `sprk_graphendpoint` column + its rows survive — schema deletion is an operator action, documented. See [`../notes/graph-endpoint-decision.md`](../notes/graph-endpoint-decision.md) |
| **W6** | **022** ✅ | 020, 040 ✅ | 1 | GraphService. **Done 2026-08-24.** 🔴 **All three POML claims were wrong**: the screen did NOT fail with an OData error, the `$select` did NOT request an undeclared property, and the comment did NOT contradict the code (it correctly described `AdditionalData`). The real defect — proven by task 040 against the real SDK — was `rawDeletedAt is string`, which **can never match Kiota's `System.DateTime`**. Graph sent the value, Kiota parsed it, production dropped it on a type check → `DeletedDateTime` null for **every** row. 🔑 **DTO + UI needed no change**: `RecycleBinPage` already sorted nulls last and rendered a muted "Unknown", so AC-3/AC-4 were already met — the presentation layer was honest and starved. `$select` **removed** (not corrected), matching the 030 precedent. See [`../notes/task-022-findings.md`](../notes/task-022-findings.md) |
| **W7** | **023** ✅ | 020, 040 ✅ | 1 | GraphService + DTO. **Done 2026-08-24.** First POML whose specific claim held — `itemMajorVersionLimit` / `maxStoragePerContainerInBytes` confirmed by reflecting over the SDK (stronger than docs, no live call needed). **But fixing the names alone would have changed nothing**: settings were written **top-level** when they are a **nested `settings` object**, AND the client already sent the *correct* names for two fields the server DTO spelled *differently*, so those were dropped at deserialization. 🔴 **4th defect, not in the POML**: `ValidSharingCapabilities` = `{disabled, view, edit, full}` — 3 are not Graph values, and this set is the endpoint's allow-list, so **every client value except `disabled` got a 400 from our own validator**. It survived because **10 tests asserted the wrong values were correct** — correcting it would have "broken tests". Fix moves to the SDK's **typed** settings model, so names are now compiler-enforced. See [`../notes/task-023-findings.md`](../notes/task-023-findings.md) |
| **W8** | **024** ✅ | 023 ✅ | 1 | GraphService (4 null sites) + client. **Done 2026-08-24 — IMPLEMENT branch.** Spike **not re-run**: task 020 already measured it live (v1.0 `$select` → **400**, beta → **200 with value**), and the finding is sharper than the POML's three options — availability is partitioned **by operation**: beta **LIST yes**, **GET no**, v1.0 **not in the schema**. The code had been asking Graph for the field in its `$select` and **discarding it** at 4 sites → every Containers row read "—" and the Dashboard summed nothing into a confident **"0 B"**. `ReadStorageUsedInBytes` accepts every numeric shape Kiota can produce (task 022's lesson applied preventively — 5 GB does not fit an `int`). Dashboard now reports its own **coverage**, so a partial sum is never shown as a total. Also cleared the last 4 `CreatedDateTime ?? UtcNow` fabrications → **zero remain on any read path**. See [`../notes/storage-consumption-spike.md`](../notes/storage-consumption-spike.md) |
| **W9** | **025** 🔄 | 023, 040 ✅ | 1 | GraphService + DTOs + client. **Done 2026-08-24 — server complete, form deferred.** AC-5 satisfied from Graph's own **OData `$metadata`** (stronger than docs, no token needed): v1.0 has exactly **nine** — the count was right, the list was not. 🔴 **`agent.chatEmbedAllowedHosts` does not exist** in either version (absent from both CSDL docs, the SDK model, and all 4 live container types), and **`sharingCapability` was omitted** though it is one of the real nine. Task 023 had already wired 4, so this was **five, not nine**. 🔴 Also: the SDK's `…SettingsOverride` enum is **narrower than the live tenant** — 2 of the 3 live flags are not members — so overridables is read/written as the **raw string** (deliberately the opposite of 023's typed choice, because here the type is provably wrong). **Before this task no settings value reached the client at all.** Form rebinding deferred: it is bound to the Dataverse config record, not the Graph settings DTO. See [`../notes/task-025-schema-verification.md`](../notes/task-025-schema-verification.md) |
| **W10** | **026** 🔄, **029** 🔄 | 025 / 020 ✅ | **1, not 2** | **026 done 2026-08-24 — partial, AC-2 escalated.** Client-only in the end: 025 had already delivered the one field it needed. **Step 1 answered definitively — `replicat*` appears NOWHERE in either CSDL**, so Graph exposes no replication signal and the honesty constraint resolves to "state the expectation" (24h sourced to learn-containertypes.md:101). 🔴 **Premise error at the centre of the task**: `consumingTenantOverridables` is a **PERMISSION** (which settings *may* be overridden), **not a STATE** — it carries no effective value. The POML reads it as state; rendering it that way would assert what the response never said, reintroducing this project's core defect inside the task meant to remove an instance of it. 🔴 The effective value lives on `fileStorageContainerTypeRegistration.settings`, but that collection hangs off **`fileStorage`, not off a container type**, and is **scoped to the calling tenant** — so an owning tenant structurally **cannot** read a remote consuming tenant's overrides. Not a permissions gap. 🔴 **Graph's own CSDL is narrower than Graph's own responses**: all four live types return `sharingCapability` in the overridables string and it is a member of the override enum in **neither** version — sharpening 025's finding (which blamed the SDK). Unrecognised flags are now preserved, never filtered. ⚠️ The constraint's **three**-state requirement is unmeetable — *replicated* vs *pending* are indistinguishable because nothing reports the transition; shipped **two** honest states rather than inventing a third on a timer. The bare green *"Settings saved successfully"* — which asserted a change was in effect when it may take 24h and may **never** reach an overridden tenant — is now an `info` "Saved — replication is pending". 🔔 **AC-2 escalated** (3 paths; recommend re-scope + FR-C08 amendment). See [`../notes/task-026-findings.md`](../notes/task-026-findings.md) · **029 done** below | 🔴 **The "both client + DTO only" premise was false.** **029 done 2026-08-24.** Surfacing a new Graph field needs the mapping layer, so 029 modifies `SpeAdminGraphService.cs` — and 029 and 026 *also* share `ContainerTypeDtos.cs` **and** `components/container-types/`, which both POMLs list as modify targets. Dispatched as parallel agents they would have contended twice over; run serially instead. **Re-check 026's ∥-safe flag before dispatching it.** 🔴 `billingStatus` appeared in **0** files repo-wide (`src/` + `tests/`) — lapsed billing had no route to an admin at all; the purest form yet of this project's signature shape, since the value was never even asked for. 🔴 FR-C12's single generic warning would be **wrong for 2 of the 3 classifications** — only `standard` needs a billing profile in the developer tenant (learn-containertypes.md:79 vs :61/:80) — so the warning branches on classification, and for passthrough it says the docs are silent rather than inventing a remediation. 🔴 Fixed in passing: the settings-save path skipped normalization, returning `"Trial"` where the list returned `"trial"` — same field, casing depending on which endpoint you asked. Making the client type honest about nullability surfaced **3 more sites** rendering an **empty badge** for an absent classification (`capitalize(undefined)` returns `undefined`), the state the grid was in for the 10 days billing classification was null. ⚠️ **AC-1 partial** — CSDL + compiler + WireMock all verified; live render not re-confirmed (delegated-only ⇒ interactive sign-in). Escalation trigger did **NOT** fire — permissions are granted; this is operator time. ⚠️ **Client lint gate does not exist** (no ESLint dep/config/install in SpeAdminApp); substituted `tsc --noEmit` vs a stashed baseline. ⚠️ **Publish-size baseline unreproducible** — measured 44.99 MB vs a recorded 43.67; stash-and-remeasure proved the true delta is **0.00 MB** and the *method* drifted. See [`../notes/task-029-findings.md`](../notes/task-029-findings.md) |
| **W11** | **027** | 011, 020, 040 ✅ | 1 | GraphService + permissions endpoints |
| **W12** | **028** | 020, 040 ✅ | 1 | GraphService ($select) + client |
| **W13** | 041 | 011, 040 ✅ | 1 | Test project only. ⚠️ Provisions a throwaway container |
| **W14** | 042 | 040, 041 ✅ | 1 | Test project only |
| **W15** | **050** | 020, 040 ✅ | 1 | GraphService + endpoints + client |
| **W16** | **051** | 023, 024, 040 ✅ | 1 | GraphService + endpoints + client |
| **W17** | **052** | 022, 040, 041 ✅ | 1 | GraphService + endpoints + client. ⚠️ Irreversible ops — throwaway container only |
| **W18** | 060, 061, 062 | 029 ✅ (062) | 3 | All independent: file moves, docs, cross-project note |
| **close** | 090 | all ✅ | 1 (serial) | 🔔 `/test-diet` is a BINDING gate |

**Bold** = the wave's single GraphService-modifying task.

### Build verification between waves (mandatory)

- Any `.cs` modified → `dotnet build src/server/api/Sprk.Bff.Api/`
- Any `.tsx`/`.ts` modified → build the SpeAdminApp code page (`npm install --legacy-peer-deps`)
- **Build fails → STOP. Do not dispatch the next wave.**

---

## Critical Path

```
001 → 004/010 → 011 → 020 → 023 → 024 → 051 → 090
      (W0)  (W2)   (W3)  (W4)  (W7)  (W8)  (W16)
```

**Longest chain: 8 tasks.** Task 010 is the highest-risk node — an `UNWORKABLE` verdict blocks 011, and
everything from 020 onward depends on 011. The auth spike is not just first; it is load-bearing.

---

## ✅ Workstream B unblocked 2026-08-22 — operator chose **path A** (BFF identity)

Container types now run on `IGraphClientFactory.ForUserAsync`, the BFF's **existing** OBO exchange
(already used by SPE files, Agent, Dataverse user client). **No new `.WithClientSecret` site** — the
A4/E-3 concern was overstated; the BFF already had four OBO sites and SpeAdmin reuses one.

**Task 011 is 🔄 partial**: the containerTypes delegated path is wired and building. What remains of
011's original scope is whatever else assumed `SpeAdminTokenProvider` — that provider is now dead code
on this path and should be assessed for removal.

**🔴 Still outstanding (docs)**: ADR-028 **E-1** describes a per-customer owning app that does not exist
for SpeAdmin. Amend it, or the next project rebuilds on the same false premise.

**✅ Tenant isolation shipped** (`325511d5b`) — `SpeAdminTenantScope` + `SpeAdminTenantScopeFilter` on
the `/api/spe` group. `configId` is no longer a bearer capability. **Every config MUST carry a business
unit before a shared multi-customer environment counts as isolated** — a config with no BU is treated
as accessible for upgrade compatibility. See [`notes/tenant-isolation-gap.md`](../notes/tenant-isolation-gap.md).

---

## Historical — the task-010 blocking record (resolved above)

Task **010 returned UNWORKABLE** (2026-08-21). Escalation triggers 1 and 2 both fired; the CLAUDE.md
§6.5 gate **must be re-run** with new evidence before task 011 starts.

**The gate's premise was false.** It resolved as path C (comply under ADR-028 **E-1**, which exempts
*"per-customer owning apps, which are other applications' identities"*). But `sprk_owningappid` =
`170c98e1-…` = **`SDAP-PCF-CLIENT`** — the SPA client the code page already signs in as. **There is no
per-customer owning app in this environment**, so E-1 does not describe the situation.

| Proven live | |
|---|---|
| `api://{owningAppId}/.default` | `AADSTS500011` — resource principal not found; the app has **no** `identifierUris` and **no** exposed scopes |
| OBO client vs assertion audience | client `170c98e1` ≠ `aud` `1e40baad` (BFF). OBO requires them equal — structurally unfixable here |
| `FileStorageContainerType.Manage.All` | delegated + admin-consented on **both** SPs — **permissions were never the problem** |
| app-only `GET …/containerTypes` | **403** on v1.0 *and* beta — spec §3.1 confirmed |

**Do NOT switch `Create(OwningAppId)` → `Create(BffAppId)`.** It is the one move escalation trigger 1
names: ADR-028 **A4** territory, a new site under **E-3**, and it contradicts
`spaarke-auth-v4-dataverse-MI` `design.md:149`. A human picks path A / B / C in `BLOCKED.md`.

**Not blocked**: A, C (ungated parts), D, E, F, and task 013. **Search is NOT blocked on auth** —
task 004 proved it was a wrong Graph entity type and fixed it. 011 must not inherit Search.

---

## ✅ Task 013 done 2026-08-23 — and a multi-tenant fact that changes other reasoning

`SecurityEvents.Read.All` granted + admin-consented on the **owning app** `170c98e1` in the Spaarke
tenant. Exactly one permission added (before/after diff); `SecurityEvents.ReadWrite.All` NOT granted.
**`GET /security/secureScores` now returns 200 with real data** — it was 403 before.

**🔑 Operator-confirmed: a Spaarke environment can manage container types living in CUSTOMERS' OWN
Entra tenants.** That is why `sprk_speenvironment` carries `sprk_tenantid`, and it makes
`GetClientForConfigAsync` **correct** — the config selection chooses *whose tenant* is read. Two
consequences:

1. **Per-customer onboarding, not one-time setup.** Every customer tenant needs this grant on *its*
   owning app. Now in [`auth-deployment-setup.md` §5e](../../../docs/guides/auth-deployment-setup.md)
   with the full owning-app permission table.
2. **ADR-028 E-1 is partly rehabilitated.** Task 010's *"there is no per-customer owning app"* is true
   of **Spaarke Dev only** — Spaarke's own tenant, where owning app and browser client collapse onto one
   registration. In a customer tenant they are distinct. **Task 010's OBO verdict is untouched**: the
   assertion always carries `aud = BFF`, so `Create(OwningAppId)` fails even with a real separate owning
   app. Path A stands.

❌ **Retracted**: an intermediate analysis argued this was a modeling error and the grant belonged on the
BFF. It assumed one tenant per environment; the BFF's `ForApp()` authenticates in the BFF's home tenant
and could never read a customer's. Struck in
[`notes/app-registration-topology.md`](../notes/app-registration-topology.md) so it is not re-invented.

🔔 **Escalated, not papered over — Alerts still fails, for a DIFFERENT reason.**
`Security.Alerts_v2` ([`SpeAdminGraphService.cs:4593`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs#L4593))
returns `403 "Account is not provisioned"` — it needs a **Microsoft 365 Defender workload** in the
tenant. Proof it is not permissions: legacy `/security/alerts` returns **200 with an empty array** on the
same token, same tenant, same moment. **No broader permission can fix it**, and granting one to silence
it is the exact failure mode this project exists to remove. Options in
[`notes/security-grant-record.md`](../notes/security-grant-record.md) §"cause has changed".

✅ Also done (operator-authorized): two **expired** credentials removed from `170c98e1` — secret
`SharePointEmbeddedVSCode` (exp. 2025-11-22), cert `CN=SharePoint Embedded VS Code Ext` (exp.
2026-03-14). One valid secret + one valid cert retained, both to 2027.

---

## ✅ Task 020 — resolved as **option A** (operator, 2026-08-23). `/beta` → v1.0 is NOT safe wholesale

**Measured live**: `storageUsedInBytes` **does not exist in the v1.0 schema**. `$select` on it returns
**400 "Could not find a property named 'storageUsedInBytes'"**, while the identical call on beta returns
200 with the value. `ownershipType` is likewise beta-only. Both on `/storage/fileStorage/containers`.

**Three consequences:**

1. **Task 024's spike is answered in advance, and the answer is YES.** FR-C06's two-branch requirement
   resolves to **implement** — Graph *does* expose consumption, on **beta**, and **LIST-only** (even beta's
   GET-single omits it). 024 should start from the table in the note, not re-run the spike.
2. **020 and 024 directly conflict.** 020 wants beta gone; 024 needs the one field only beta has. And it
   is structural: `CreateGraphClient` (`:4261`) backs **every** `…ForConfigAsync` method, so flipping that
   one base address flips containers, recycle bin, search, security and audit together.
3. **FR-C01's premise is inverted here.** The rationale is "beta schema drift generates wrong-property
   defects" — but for `containers`, **v1.0 is the version missing properties the app needs.** The rationale
   still holds for container *types* (§4.1's `itemMajorVersionLimit` / `maxStoragePerContainerInBytes`).

**Much of 020 is already done by 011**: container-type LIST runs on `ForUserAsync`, and that factory path
already builds a **v1.0** client (`GraphClientFactory.cs:327`). ⚠️ But 011 left a **version split** —
container-type LIST on v1.0, GET/CREATE still beta via `…ForConfigAsync`. One resource, two versions, no
comment. Close that regardless of the decision.

**A 4th `/beta` site exists that the POML never names**: `GraphClientFactory.cs:164` (`ForApp()`),
BFF-wide. Out of R2 scope — it serves far more than SpeAdmin.

**Site `:4278`** (`CreateGraphClientFromBearerToken`) is **dead code** since path A — delete, don't migrate.

Options + recommendation (A: keep containers on beta as a documented second exception):
[`notes/beta-vs-v1-surface-verification.md`](../notes/beta-vs-v1-surface-verification.md).
**No code changed.**

---

## 🔴 Verified defects handed forward (do not re-derive)

| Found by | Defect | Site | Owner |
|---|---|---|---|
| **040** | `deletedDateTime` is guarded by `rawDeletedAt is string`, but Kiota stores a **`System.DateTime`** in `AdditionalData` (probed against the real SDK). The guard can never be true, so **every recycle-bin row reports a null deletion timestamp** — rows cannot be sorted by deletion date or aged out, and the screen cannot tell that apart from "deleted at an unknown time". | `SpeAdminGraphService.cs:4368` | **022** |
| **012** | The **no-arg** `ToProblemDetails()` (~29 callers in `ContainerItemEndpoints` / `DocumentsEndpoints` / `OBOEndpoints` / `UploadEndpoints`) hardcodes 403 as *"api identity lacks required container-type permission"* — but on any **delegated** path the failing identity is the signed-in user, not the api identity, so it may name the wrong party. **Not fixed** — 29 shared call sites, and which are delegated is unverified. Record, don't guess. | `GraphErrorTranslator.cs:126` | **042** / decomposition-r1 |
| **012** | Container types' general `catch (SpaarkeStorageException)` still maps **every non-403 Graph status to 500**, so a 429 throttle also reads as a server error. 403 was fixed; the rest were left rather than changing error semantics beyond FR-B03. | `ContainerTypeEndpoints.cs` ×3 | **021** / **023** |

Pinned as characterization tests in `tests/integration/contract/SpeAdmin/` that name the defect and
the owning task. **They must FAIL and be updated when the fix lands** — deleting one instead would
restore the silence. Evidence: [`notes/task-040-completion.md`](../notes/task-040-completion.md) §4.

Fifth instance of the project's signature shape: *a lower layer collapsing a real value into an absent
one that an upper layer reads as benign.*

---

## High-Risk Items

| Task | Risk | Guard |
|---|---|---|
| **010** | Owning-app OBO may be unworkable; two verified defects say the current path cannot succeed | Escalation trigger → re-run §6.5 gate. **Never fall back to BFF-identity OBO silently** |
| **011** | Auth change in a BFF hot path; ADR-028 A4 boundary | Opus tier + `xhigh`; §6.5 path-C cited in the PR |
| **004 / 005** | Uncapped — root causes not isolated; effort is provisional | Escalation triggers hand off if the cause is out of scope |
| **041 / 052** | Irreversible ops against a live tenant holding real documents | NFR-07: throwaway container provisioned by the fixture; guard refuses non-fixture container ids |
| **024** | Graph may not expose consumption at all | Two-branch FR — removal is pre-authorized by owner decision OC-04 |
| **042** | Deleting unreplaced coverage would be a regression | ADR-038 deletion-safety; escalation trigger |

---

## Goal-Eligibility (task-create Step 3.85)

| Wave | Eligible | Reason |
|---|---|---|
| W0, W2, W3 | ❌ | Security-sensitive auth work with a live ADR gate; 010 can reopen an architectural decision |
| W1 | ❌ | 004/005 are open-ended root-cause investigations — no machine-verifiable end state |
| W5–W12 | ✅ | Well-specified, low-ambiguity, machine-verifiable (build + tests + read-back). ≥3 tasks across the span |
| W13, W14 | ❌ | Live-tenant and deletion work — excluded per the eligibility rule |
| W15–W17 | ❌ | Irreversible operations against a live tenant |
| W18 | ✅ | Independent, well-specified, verifiable |
| close | ❌ | Terminal task with a binding human gate |

`goal-condition` for W5–W12: *"Every Workstream C task in the wave has its acceptance criteria met, `dotnet build` is 0 errors, the SpeAdmin test suite is green, and each settings change is confirmed by read-back against Spaarke Dev."*

> The Haiku evaluator is a **stopping-condition check, not a quality gate**. Step 9.5 and orchestrator
> authority are unchanged; tasks are never auto-completed on goal achievement.

---

## Traceability — 31 FRs → 30 tasks

| Workstream | FRs | Tasks |
|---|---|---|
| A — Make failures visible | A01–A05 | 001, 002, 003, 004, 005 |
| B — Auth (gated) | B01–B04 | 010, 011, 012, 013 |
| C — API surface | C01–C13 | 020, 021, 022, 023, 024, 025, 026, 027, 028, 029, 030 |
| D — Harness | D01–D03 | 040, 041, 042 |
| E — New capabilities | E01–E03 | 050, 051, 052 |
| F — Hygiene only | F01, F02 | 060 |
| Cross-cutting | X01, X02 | 061, 062 |
| Close | — | 090 |

FR count is 31 vs 30 tasks because tasks 023 and 028 each cover two FRs (C04+C05 and C10+C11), while
task 060 covers F01+F02.
