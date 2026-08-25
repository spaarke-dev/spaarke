# Deferrals & Issues — spaarke-notification-spine-r1

> Two-write rule: every entry here is mirrored to a GitHub Issue (portfolio board). See `/project-defer-issue-tracking`.

## Issues (ISS)

### ISS-001 — Internal-only messages readable by any caller (read-path hardcodes IsInternalUser:true)

| Field | Value |
|---|---|
| **Status** | Open — handed to messaging-r3 |
| **Urgency** | now (security) |
| **Filed** | 2026-07-21 |
| **Source** | FR-08 fan-out security review; owner confirmed external users can be licensed systemusers |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/674 |

**Description**: `CommunicationThreadReadService.cs` hardcodes `IsInternalUser: true` at 3 sites → the internal-only rule is unenforced on the timeline read path; an external-licensed systemuser reads internal-only messages. Owned/edited by messaging-r3 (PR #664), so the spine did the shared `IsExternalAsync` resolver + its own fan-out fix and **handed off** the 3-site read-path swap. Concrete failure: internal-only content leaks to external-licensed users on read.

**Entry-points**: `notes/HANDOFF-messaging-r3-internal-only-readpath.md` (full step-by-step); `CommunicationThreadReadService.cs` (~L129/185/463); resolver `Services/Identity/SystemUserIdentityResolver.cs` `IsExternalAsync`.

**Blockers**: notification-spine merges first (provides `IsExternalAsync`) + backfill `systemuser.sprk_isexternal`.

---

### ISS-002 — Deploy spaarke-bff-dev to pick up the SignalR endpoint-host diagnostic + suggestion idempotency fix

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-08-19 |
| **Source** | SignalR UAT-10 resolution (2026-08-19) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/802 |

**Description**: Two fixes are on master but NOT deployed to `spaarke-bff-dev` (rg-spaarke-dev). (a) compose-r7's endpoint-host diagnostic log in `SignalRDeliveryService.NegotiateAsync` — until live, a future SignalR key/endpoint drift (like UAT-10) is only diagnosable from a client 401, with NO server-side signal. (b) the `DailyBriefingSuggestionProducer` idempotency fix — the running dev BFF still writes a duplicate `kind=suggestion` outbox row on EVERY Daily-Briefing re-render, so the pending stack keeps ballooning in dev. Concrete failure: dev duplicate-suggestion bloat persists + future SignalR drift stays blind server-side.

**Entry-points**: `deploy-bff-api.yml` (target spaarke-bff-dev); `Services/Notifications/SignalRDeliveryService.cs:224-231` (diagnostic); `Services/Ai/Narrators/DailyBriefingSuggestionProducer.cs` (idempotency dedupe).

**Suggested fix**: run the BFF deploy workflow against spaarke-bff-dev.
**Estimated effort**: < 1 hr. **Blockers**: none. **Related**: ISS-003.

---

### ISS-003 — Move dev `Notifications__SignalR__ConnectionString` to a Key Vault reference (ADR-028)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-08-19 |
| **Source** | SignalR UAT-10 resolution (2026-08-19) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/803 |

**Description**: On `spaarke-bff-dev` the SignalR connection string is a **plaintext App Setting**, not a Key Vault reference. UAT-10 (real-time delivery 401 outage) happened because the Azure SignalR access key was rotated but the plaintext setting was never re-copied — the configured key matched neither current primary nor secondary. A `@Microsoft.KeyVault(...)` reference lets a rotated key re-sync in ONE place (the vault secret), preventing silent recurrence of the exact outage. ADR-028 already requires the KV-reference pattern for production; dev is the gap. Concrete failure: a SignalR key rotation silently breaks real-time delivery again.

**Entry-points**: `az webapp config appsettings list -n spaarke-bff-dev -g rg-spaarke-dev` → `Notifications__SignalR__ConnectionString`; secret source `az signalr key list -n spaarke-signalr-dev -g rg-spaarke-dev --query primaryConnectionString -o tsv`; `SignalRDeliveryOptions.SectionName = "Notifications:SignalR"`; ADR-028.

**Suggested fix**: store the connection string as a KV secret; set the app setting to `@Microsoft.KeyVault(SecretUri=...)`; grant the App Service MI `get`; restart.
**Estimated effort**: 1-2 hrs. **Blockers**: none. **Related**: ISS-002; parent outage `notes/handoffs/signalr-uat10-realtime-delivery-assessment-2026-08-19.md`.

---

## Deferrals (DEF)

### DEF-001 — Consolidate ad-hoc oid↔systemuserid resolution onto shared ISystemUserIdentityResolver

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-21 |
| **Source** | notification-spine-r1 identity-resolver fix (commit 551756b9e); grep during task 020 follow-up surfaced 6+ independent copies |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/673 |

**Description**

The notification spine added a shared, cached, bidirectional `ISystemUserIdentityResolver` (`src/server/api/Sprk.Bff.Api/Services/Identity/SystemUserIdentityResolver.cs`) over `systemuser.azureactivedirectoryobjectid`. At least 6 pre-existing surfaces each implement their own ad-hoc `oid↔systemuserid` lookup, plus an AI-context resolver with different semantics. They should be migrated onto the one shared resolver. `BriefingService.cs:376` already anticipated this ("extract `ICurrentUserResolver` once a third consumer needs the same lookup").

Concrete failure mode (not "for future flexibility"): **identity-resolution drift** — the copies differ in caching/TTL (some uncached), in `isdisabled` filtering, and in how they read the AAD column (Guid vs string). Divergent resolution of the same user across auth-adjacent surfaces is an **authorization hazard** (one surface authorizes/targets a user another surface resolves differently), not merely duplication.

**Entry-points**

- Shared target: `src/server/api/Sprk.Bff.Api/Services/Identity/SystemUserIdentityResolver.cs`
- Ad-hoc copies to migrate: `Services/Workspace/BriefingService.cs` (~L360-420), `Services/DocumentCheckoutService.cs` (~L832-838), `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` (~L260), `Api/Ai/AnalysisEndpoints.cs` (~L819-842), `Services/Dataverse/Privileges/UserPrivilegeChecker.cs` (~L221), `Services/Registration/RegistrationDataverseService.cs` (~L344)
- Decision needed: `Services/Ai/Context/CallerSystemUserResolver.cs` (`ICallerSystemUserResolver`, oid→systemuserid only, uncached, ClaimsPrincipal-coupled) — collapse into the shared resolver or keep as a claims-facade over it?
- Full context: `projects/spaarke-notification-spine-r1/notes/identity-resolver-consolidation-defer.md`

**Suggested fix**: migrate each copy to inject `ISystemUserIdentityResolver`; delete the private lookups; decide the `ICallerSystemUserResolver` collapse-vs-facade question; add a grep guard against new ad-hoc `azureactivedirectoryobjectid` queries.

**Estimated effort**: 1–2 days (touches auth/identity across many BFF surfaces — needs careful per-surface test coverage)
**Blockers**: none (the shared resolver is shipped)
**Related**: ADR-028 (auth v2); commit 551756b9e

---
