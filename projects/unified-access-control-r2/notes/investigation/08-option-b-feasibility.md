# 08 — Option B Feasibility: Dataverse's Real Answer for systemuser-backed SPA Callers

> **Investigation date**: 2026-08-20 · **Project**: unified-access-control-r2 · **Status**: research complete
> **Question**: For a caller who IS a Dataverse `systemuser`, can the SPA accessible record set be computed from Dataverse's actual access answer (instead of column pattern-matching), at what cost, and how?

---

## Summary

**Verdict: FEASIBLE-WITH-CONDITIONS.** The recommended mechanism — **app-only impersonated `RetrieveMultiple` via the `MSCRMCallerID` Web API header** — is not hypothetical: it is already built, registered, and live in this repo as the enforcement mechanism for the messaging read path (`Services/Communication/*`), with a written owner decision behind it (`projects/messaging-communication-app-r1/notes/access-model-decision.md:8-16`). Applying it to the SPA plane means swapping the *source* of root-entity ids inside `AccessibleRecordSetService.ComposeForSystemUserAsync` from `MembershipResolverService` pattern-matching to an impersonated id-only query per root entity type (3 per composition). Everything downstream — `Tier2ScopeFilterInjector`, `ExternalModuleRegistry` child derivation, `CallerPrincipal.ProjectAccess` — consumes only `IReadOnlySet<Guid>` and is unchanged.

The central risk is **fail-OPEN**: an impersonation header silently not applied yields the app-only (System Administrator) row set with no error. The repo's read primitive guards the main vector (throws on `Guid.Empty` caller — `DataverseWebApiService.cs:962-965`), but the opt-in optional-parameter design means "app-only" is the silent default for any new call path; a negative canary test (design below) is a hard condition.

OBO to Dataverse **exists and works in this repo** for workforce users (`DataverseUserClient`, live on the AI `dataverse.*` tool path) — but it is barred on the collaboration/module-host plane by ADR-028 A2/A3 **as policy** for workforce systemusers, and is **physically impossible** for contact-only (CIAM) callers. `RetrievePrincipalAccess` is claimed by docs and comments but has **zero call sites** — and is per-(principal, record), the wrong shape for set composition anyway.

Contact-only callers confirm the prompt's reasoning: no `systemuser` row → no Dataverse security identity → nothing for Dataverse to answer *about*. Option B applies **only** to the `WorkforcePrincipalKind.SystemUser` plane; the contact plane keeps grants ∪ standing-grant membership unchanged.

---

## 1. Mechanism inventory

| Mechanism | Exists in repo? | Live? | Cost per request | List or single |
|---|---|---|---|---|
| **OBO** (caller token → Dataverse token, query as user) | **YES.** (a) `DataverseUserClient` — full Dataverse OBO client, MSAL `AcquireTokenOnBehalfOf` for `{env}/.default`, static CCA cache, fail-closed with no app-only fallback (`Services/Ai/Handlers/Dataverse/DataverseUserClient.cs:17-23, 87-106`). (b) `DataverseAccessDataSource.GetDataverseTokenViaOBOAsync` (`Spaarke.Dataverse/DataverseAccessDataSource.cs:105-144`). (c) Graph OBO: `GraphClientFactory.cs:225`, `GraphTokenCache` (55-min Redis TTL, `Services/GraphTokenCache.cs:16`). All five OBO sites enumerated in ADR-028 E-3 (`.claude/adr/ADR-028-spaarke-auth-architecture.md:245`) | **LIVE on the internal AI tool path** (`dataverse.*` namespace). The `DataverseAccessDataSource` OBO branch IS reached from `AiAuthorizationService` (extracts the caller's bearer at `AiAuthorizationService.cs:78`, passes it at `:176-179`) — but only when `API_CLIENT_SECRET` is configured; under MI-only config `_cca = null` and the OBO branch throws → fail-closed `AccessRights.None` (`DataverseAccessDataSource.cs:67-77, 107-112, 229-247`). **NOT live and FORBIDDEN on the SPA/collaboration plane** (ADR-028 A2 `:72`, A3 `:114`) | 1 OBO exchange (MSAL-cached per user assertion) + N queries; requires confidential-client credential (secret today per E-3; MI-FIC per A4 target) | **LIST** (query runs as the user; Dataverse row-filters natively) |
| **`RetrievePrincipalAccess`** (app-only, per principal+record) | **NO — comment/doc claims only, zero call sites.** All `src/server` hits are XML-doc or DI comments: `AiAuthorizationService.cs:13`, `IAiAuthorizationService.cs:50`, `CommunicationModule.cs:256`, `ICommunicationAccessFilter.cs:28`, filter docs. The app-only path that docs claim uses it actually does a direct document GET instead (`DataverseAccessDataSource.cs:313-331`). `docs/architecture/uac-access-control.md:21,24,44` claims it is the app-only mechanism — **that is doc drift, verified false** | **NO** | 1 Dataverse call **per (principal, record)** — no bulk form (researcher memory `.claude/agent-memory/researcher/dataverse-record-access-security-2026-07-16.md:20`) | **SINGLE** — N+1 for set composition; only fit for discrete gates ("can X open thread Y") |
| **Impersonated `RetrieveMultiple`** (app-only + `MSCRMCallerID`) | **YES.** Helper: `Spaarke.Dataverse/DataverseImpersonation.cs:29-53` (header = target **systemuserid**; the oid pairing in the old note is documented as incorrect at `:20-21`). Read primitive: `DataverseWebApiService.RetrieveMultipleImpersonatedAsync` (`:953-989`), rejects `Guid.Empty` (`:962-965`). Seam: `IImpersonatedCommunicationQuery` (`Services/Communication/IImpersonatedCommunicationQuery.cs:21-56`). Impersonated **write** (PATCH) also exists (`DataverseWebApiService.cs:189-194`) | **LIVE** — messaging thread-read + unread + queue-feed + attachment-text endpoints and the proposal-apply write path, all registered unconditionally (`Infrastructure/DI/CommunicationModule.cs:230-301`). Live-environment iteration evidence: the FormattedValue-annotation fix on impersonated rows, messaging-r3 2026-07-22 (`DataverseWebApiService.cs:974-979`). Unit contract tests exist (`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/DataverseImpersonationTests.cs`) | **1 Dataverse round trip per entity type**, zero token exchanges, zero extra credential machinery (uses the existing app token; header is per-request) | **LIST** — "a single impersonated query returns only the rows B can see → the cleanest, most performant way to filter a LIST" (researcher memory `:28`) |
| **ServiceClient `Clone()` + `CallerId`** (SDK impersonation) | Was written for email-intelligence task 031, **deleted without ever being reached** (`Spaarke.Dataverse/DataverseServiceClientImpl.cs:1932-1938` — "That branch was never reached"; recoverable from git `4aca6d65a`) | **NO** — `dataverse-access-unification-r1` (which would make the SDK path primary) is PAUSED | n/a | n/a |

---

## 2. Scalability

**Today's shape**: `WorkforcePrincipalStrategy` composes accessible sets for **3 root entity types per request** — `sprk_project`, `sprk_matter`, `sprk_workassignment` (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:364-366, 422-427`). Each `ComposeAsync` = 1 membership FetchXml (OR-joined pattern-match, `MembershipResolverService.BuildFetchXml` `:625-755`) + contact-grant reads. Membership results are Redis-cached **5 min** per (systemUserId, entityType, optionsHash) (`MembershipResolverService.cs:74-84`). `CallerPrincipalResolver` itself has **no cache** (verified — zero cache references), so the 5-min membership cache is the only amortization. The separate per-(user, record) 60s/2-min cache tier lives in `CachedAccessDataSource.cs:31-37` (a different plane: single-record `AccessSnapshot`).

**Option B shape**: 3 impersonated id-only queries per composition — e.g. `RetrieveMultipleImpersonatedAsync("sprk_matters", "$select=sprk_matterid&$top=N", callerSystemUserId)`. Same round-trip count as today (3), same LIST shape, **no N+1**. Cacheable identically: key by `(systemUserId, entityType)` + schema version, 5-min TTL — the precedent TTL already accepted for access-affecting data (and shorter than nothing: today's pattern-match answer is also 5-min stale). Safe because impersonation results are deterministic per user; the poisoning risk is only a key that omits the user id (see §3).

**Bounds**: FetchXml `in`-operator handles ~500 values per condition; the resolver already truncates ids to `effectiveLimit` (default **500**, max **5000** — `IMembershipResolverService.cs:149,155`; `MembershipResolverService.cs:1022-1040`). These bounds bind the **downstream injection** (`Tier2ScopeFilterInjector`) identically regardless of where the ids come from, so Option B neither helps nor hurts here — **except** that a user with genuinely broad Dataverse access (org-level Read from a role) will now have a *real* accessible set that can vastly exceed 5000, where pattern-matching structurally returned only assigned/owned rows. Truncation semantics for that case need an explicit decision (top-N by modifiedon + honest "more exists" flag, or a per-plane cap).

---

## 3. Fail-OPEN risk (the central risk) + negative canary design

With impersonation, "header not applied" = app-only query = **org-wide rows, HTTP 200, no error**. Analysis:

**(a) Code paths that could exhibit it**
1. `DataverseImpersonation.Apply` **silently no-ops** on `null`/`Guid.Empty` by design (`DataverseImpersonation.cs:46-47`; contract-tested at `DataverseImpersonationTests.cs:31-49`). Guarded at the read primitive (`RetrieveMultipleImpersonatedAsync` throws on empty caller, `DataverseWebApiService.cs:962-965`) — but **NOT** at `CreateAuthenticatedRequestAsync`/`SendPatchAsJsonAsync`, where `impersonateSystemUserId` is an **optional parameter defaulting to null** (`:151-159, 189-194`). Any new access-scoped call routed through a helper without explicitly passing the id compiles fine and runs app-only. This is the primary vector: **app-only is the silent default of the whole client**.
2. oid→systemuserid resolution failure. Precedent is correctly fail-closed: `CommunicationThreadReadService` refuses the read with 403 when the resolver yields null/empty — "no app-only fallback" (`CommunicationThreadReadService.cs:731-745`). Option B code MUST copy this, not silently skip the term.
3. Cache-key omission: caching a composed set under a key missing `systemUserId` serves one user's set to another. Membership's key shape (`MembershipResolverService.cs:66-74`) is the correct template.
4. Missing privilege ≠ fail-open: per the repo's MS-Learn-verified doc, an impersonated call by an app user lacking `prvActOnBehalfOfAnotherUser` **fails at Dataverse rather than silently widening** (`DataverseImpersonation.cs:24-27`) — the correct fail direction. This claim is doc-level; no automated test exercises it (canary covers it).

**(b) `ServiceClient.Clone()` + `CallerId` under the MI token provider**: **not established in code** — the only SDK impersonation branch was deleted unreached (`DataverseServiceClientImpl.cs:1932-1938`), and the paused unification project explicitly listed "parity-test SDK vs REST impersonation" as an open obligation (`projects/dataverse-access-unification-r1/design.md:241`). Option B should therefore stay on the **Web API header path**, which is the proven one.

**(c) Privilege + runbook status**: the BFF Dataverse application user (`1e40baad-…`) holds **System Administrator** on dev (`docs/architecture/auth-azure-resources.md:299-300`), which includes `prvActOnBehalfOfAnotherUser` (`projects/code-quality-and-assurance-r3/notes/task-011-ng1-3b-mi-migration.md:38`; `notes/bff-auth-surface-map.md:103`). The grant is recorded only as a **go-live prerequisite in project notes** (`access-model-decision.md:28`; `DataverseWebApiService.cs:941-942`; deferred re-provisioning obligation `projects/dataverse-access-unification-r1/README.md:84`) — **no operator runbook (`docs/guides/auth-deployment-setup.md`) records it** (grep: zero hits under `docs/guides/`). Also note `access-model-decision.md:28`'s intersection warning: a *narrowly*-scoped app user silently **narrows** impersonated results (a fail-CLOSED-ish but wrong-answer mode) — the app user must stay broadly scoped.

**(d) Negative canary test** (condition for shipping; `tests/integration/auth/**` KEEP path per ADR-038):

```
CanaryUser = a permanently-provisioned low-privilege systemuser in the test environment
             (User-level Read on sprk_matter only; owns exactly K seeded matters; org
             contains >K matters it cannot read).

Test 1 (strict-subset invariant — catches header-not-applied):
  appOnlyIds       = app-only    GET sprk_matters?$select=sprk_matterid
  impersonatedIds  = impersonated GET (same query, MSCRMCallerID = CanaryUser)
  ASSERT impersonatedIds ⊂ appOnlyIds          // subset
  ASSERT impersonatedIds.Count < appOnlyIds.Count  // STRICTLY fewer — equality = impersonation
                                                   // silently inert → FAIL LOUD
Test 2 (exactness): ASSERT impersonatedIds == the K seeded ids.
Test 3 (empty-caller guard): RetrieveMultipleImpersonatedAsync with Guid.Empty → throws
  (already unit-covered; keep an integration assertion so a refactor can't drop it).
Runtime variant: the same strict-fewer check as a startup/scheduled canary emitting a
  metric + alarm — protects production against config drift (privilege revoked,
  header stripped by a proxy), which no CI test can see.
```

---

## 4. Composition with child derivation — what changes downstream

**Nothing structural.** The contract between root-set composition and child derivation is `IReadOnlySet<Guid>`:
- `AccessibleRecordSet.RecordIds` is the set; `Contains` is the gate (`AccessibleRecordSetService.cs:61-76`).
- `WorkforcePrincipalStrategy` copies the ids onto `CallerPrincipal.ProjectAccess` (`CallerPrincipalResolver.cs:417-431`).
- Child modules declare `ScopeDimension`s pointing at typed parent lookups (`sprk_document.sprk_project` / `sprk_matter` / `sprk_workassignment`), reading **only** precomputed root ids (`ExternalModuleRegistry.cs:19-35, 49-58`).
- `Tier2ScopeFilterInjector.Inject` turns those ids into server-side `<condition operator='in'>` filters (`Tier2ScopeFilterInjector.cs:48-99`).

Swap the id **source** inside `ComposeForSystemUserAsync` (`AccessibleRecordSetService.cs:184-241`) and every layer below is byte-unchanged. Four watch items:

1. **Role provenance is lost.** Membership resolution attributes ids `byRole` (assignedAttorney etc.); Dataverse's answer has no role concept. `ComposeForSystemUserAsync` consumes only `.Ids` (`:192-196`), so the SPA plane doesn't care — but `IMembershipResolverService` has other consumers (briefing, todo, ACS reconcile). **Do not replace the resolver; add a parallel source** consumed only by the SPA composition.
2. **Keep the contact-grants union term** (`:198-226`): a systemuser who is also a granted contact must retain grant-derived ids — Dataverse knows nothing about `sprk_externalrecordaccess` unless those grants are also materialized as POA shares (they are a Spaarke table, not POA — so the union term must survive).
3. **ContactOnly plane untouched** (`:244-301`): grants ∪ standing-grant membership. Option B cannot apply — see §5.
4. **Set-size semantics** (§2): a broad-role user's real set can exceed the 5000 cap where pattern-matching couldn't; decide truncation semantics explicitly.

Direction of change vs today's approximation: **both defects fixed for the systemuser plane** — BU-column matching without role consultation (over-grant, `BuildFetchXml` `:723-728`) disappears because Dataverse consults the actual role depth; POA shares (under-grant, ignored entirely today) are honored automatically ("ownership, role depth, BU, teams, sharing, hierarchy" — `DataverseWebApiService.cs:934-936`).

---

## 5. "Broker-only / no OBO": physics vs policy

**Physics (cannot be amended away):**
- A **contact-only CIAM caller has no Dataverse security identity**: no `systemuser` row → Dataverse has no answer to give; OBO is impossible (a `*.ciamlogin.com` token cannot be exchanged for a workforce-tenant Dataverse user token), and impersonation is equally impossible (nothing to put in `MSCRMCallerID`; impersonation requires an enabled systemuser — researcher memory `:48`; `access-model-decision.md:30` records external participants would need SystemUser records). **Confirms the prompt's reasoning; implication: Option B is a systemuser-plane upgrade only, and the composed model (contact plane on grants) must remain.**
- Cross-tenant workforce Teams users (multitenant app, customer tenant ≠ Dataverse tenant) have no systemuser either — same exclusion.
- `DefaultAzureCredential`/MI **cannot perform OBO** — OAuth requires a client assertion (ADR-028 A4 `:138, 181`). Any OBO path drags in confidential-client credential machinery (secret today under E-3, MI-FIC later).

**Policy (a decision, revisable by amendment):**
- For same-tenant workforce systemusers, Dataverse OBO is **technically proven in this repo** (`DataverseUserClient` live; `DataverseAccessDataSource` OBO branch reachable from `AiAuthorizationService`). The prohibitions at A2 (`:72` "MUST NOT be exchanged for a downstream Graph/SPE/Dataverse token") and A3 (`:114`) are architecture choices: one auth model across both planes, no per-user Dataverse seat requirement, no token-exchange surface on the internet-facing plane.
- **Impersonation is not OBO.** No caller token is exchanged; the BFF acts as itself (app-only credential) and asks Dataverse to scope one query. The messaging read path shipped exactly this under the same broker-only regime with explicit code-review sign-off (`CommunicationModule.cs:230-258`). AccessibleRecordSetService's own header comment defines broker-only as "no caller-token exchange (no OBO)" (`AccessibleRecordSetService.cs:22-24`) — impersonation satisfies that definition. Recommend recording this reading as an ADR Tensions note (path C — comply, with clarification) rather than treating it as an A2 exception.

---

## 6. Recommendation

**Ranking for this use case:**
1. **Impersonated `RetrieveMultiple` (`MSCRMCallerID`) — RECOMMENDED.** Proven live in-repo, LIST-shaped, 3 round trips per composition, zero new credential surface, preserves broker-only, fixes both error directions.
2. OBO — technically viable for the workforce plane but ADR-blocked (A2/A3), adds confidential-client machinery mid-A4-migration, and covers strictly fewer callers (needs seat + same tenant). Not worth an amendment when (1) yields the same rows.
3. `RetrievePrincipalAccess` — wrong shape (per-record, N+1). Keep only as the documented discrete-gate mechanism; fix the drift in `docs/architecture/uac-access-control.md:21,24,44` which claims it is live.
4. **Option A (narrow pattern-matching: `sprk_assigned*` + org only, drop BU)** — a one-line-class change (`BuildFetchXml` `:723-728`) that removes the over-grant but does nothing for the POA under-grant. Worth doing as an interim/fallback if Option B's conditions stall, not as the destination.

**Verdict: FEASIBLE-WITH-CONDITIONS.** Conditions:
1. `prvActOnBehalfOfAnotherUser` (Delegate) granted to the BFF app user per environment **and recorded in `docs/guides/auth-deployment-setup.md` §6** (today it lives only in project notes; dev is covered incidentally by System Administrator).
2. The **negative canary** (§3d) ships in the same wave, under `tests/integration/auth/**`, plus the runtime canary for config drift.
3. Option B applies **only** to `WorkforcePrincipalKind.SystemUser`; contact plane and the contact-grants union term unchanged.
4. Per-user Redis cache keyed `(systemUserId, entityType, version)`, ≤5-min TTL (membership precedent).
5. Explicit truncation semantics for broad-role users whose real set exceeds the 500/5000 bounds.
6. ADR Tensions note: impersonation complies with broker-only (no caller-token exchange); cite A2/A3 explicitly.
7. Stay on the **Web API header path**; do not resurrect `ServiceClient.Clone()+CallerId` without the parity tests the paused unification project demanded.

**Effort: ~5 tasks.**
- **T1**: `ImpersonatedRootSetSource` — impersonated id-only query per root type (reuse/generalize `RetrieveMultipleImpersonatedAsync`; possibly a `$select`-only overload). Files: `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` (overload only if needed), new `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ImpersonatedRootSetSource.cs`.
- **T2**: switch `AccessibleRecordSetService.ComposeForSystemUserAsync` (`:184-241`) to the new source behind a config flag (fail-closed default = existing membership path), keeping the contact-grants union; DI in `Infrastructure/DI/` module.
- **T3**: Redis caching + invalidation-version, mirroring `MembershipResolverService.cs:66-84`.
- **T4**: negative canary integration tests + `Guid.Empty` guard integration assertion (`tests/integration/auth/**`).
- **T5**: runbook §6 privilege entry + `uac-access-control.md` drift fix + ADR Tensions note in this project's `design.md`.
