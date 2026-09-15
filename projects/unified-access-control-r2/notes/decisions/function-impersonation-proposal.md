# Proposal: may an Azure Function impersonate a Dataverse user?

> **Status**: 🔔 **AWAITING OWNER SIGN-OFF** — auth ADR change (CLAUDE.md §6.5 path B; §6 security-sensitive).
> **Asked**: 2026-09-14, session 11. The owner answered the carried task-102 question ("confirm the Function
> no-impersonation MUST NOT") with: *"which is the correct approach that allows the functions to work; the best
> technical approach"*.
> **Evidence**: researcher report 2026-09-14 (Microsoft Learn; memory
> `.claude/agent-memory/researcher/dataverse-impersonation-async-functions-2026-09-14.md`).
> **Nothing canonical has changed.** ADR-052 §6 still carries the blanket MUST NOT until this is signed off.

## 1. The answer in one paragraph

Replace the blanket ban with a **conditional permission**. Microsoft documents Dataverse impersonation
(`CallerObjectId` / `MSCRMCallerID`) as supported for services and background processing, and Dataverse's own
async pipeline carries the requesting user's identity across the async boundary by default. For user-initiated
async work it is the correct mechanism. The alternatives are both worse:
- app-only ignores the user's row-level security, and this project's whole premise is that a BFF-side filter
  becomes the entire security boundary;
- OBO needs stored refresh tokens and a user who can re-sign-in.

Impersonation also **cannot widen** what the Function can already do app-only: the effective rights are the overlap
of the impersonator's and the user's. The real risks are narrower:
- acting as the wrong user, then delivering or attributing results to someone else;
- a forged caller id.

Both are controlled by where the caller id comes from, which is what the conditions below govern.

**But the conditions are not met today** (§3), so in practice nothing changes until the prerequisites land: the
rule just stops being a flat prohibition and becomes a defined path.

## 2. Proposed rule — replaces ADR-052 §6's impersonation clause and reconciles §5's line

1. **Scope.** A Function, or a BFF job handler, MAY impersonate a workforce `systemuser` only for a unit of work
   that user started through an authenticated BFF request. It MUST NOT impersonate for work triggered by a timer, a
   webhook, or the system itself. Work with no requesting user runs app-only, with explicit filtering or no
   user-scoped output. Contacts and CIAM users are never impersonated (they are not security principals).
2. **Where the caller id comes from.** It MUST come from a typed requester field in a job message written only by
   the BFF: the JWT-validated `oid` plus `tid`. That message MUST travel on a channel only the stamp identity can
   write:
   - Service Bus with Entra auth only (`disableLocalAuth: true`);
   - *Azure Service Bus Data Sender* on that queue held only by the stamp's managed identity.

   The caller id MUST NOT come from a client payload, a webhook body, or Dataverse data.
3. **The same principal end to end.** The impersonated user MUST be the user the output is delivered to or
   attributed to. Writes are impersonated only where attributing the record to that user (`createdby`) is intended;
   otherwise write app-only.
4. **One fail-closed helper.** All impersonation goes through a single helper in `src/server/shared`:
   - It throws on an empty id, or on a tenant or environment mismatch, and never falls back to app-only.
   - It prefers `CallerObjectId` with the `oid`.
   - The NFR-04 canary (impersonated results must be a strict subset of app-only) extends to it.
5. **Who holds the privilege.** Only the stamp's application user holds `prvActOnBehalfOfAnotherUser`, assigned
   directly (Microsoft forbids inheriting it through a team). Any other holder needs owner approval.
6. **Unchanged.** OBO, user tokens, confidential clients and calling BFF endpoints stay banned.
   `WorkloadPlacementGuardTests` changes from "no `MSCRMCallerID`/`CallerObjectId` under `src/server/functions/**`"
   to "only through the shared helper". ADR-028 A5's scope grows from "a BFF request" to "a BFF-initiated job".

**ADR-052 §5, line "MUST NOT run work needing the caller's identity outside the BFF request holding it"** — as
written, it forbids even a BFF `IJobHandler` from impersonating. Reword it to its real intent: *the caller's
**token** (OBO, user tokens) never leaves the BFF request that holds it; acting for a user outside that request is
only rule 2's impersonation.*

## 3. Prerequisites (not met today — each needs an issue before rule 2 can be used)

| # | Gap | Where | Blocking? |
|---|---|---|---|
| P1 | The Service Bus namespace still accepts SAS: a namespace-wide Send+Listen rule (`SpaarkeAppAccess`), its connection string output, and a connection-string fallback in `ServiceBusClientFactory` — anyone holding it could enqueue a job naming any user | `infrastructure/bicep/modules/service-bus.bicep`, `Infrastructure/Auth/ServiceBusClientFactory.cs` | 🔴 yes — also a standing risk independent of Functions |
| P2 | `JobContract` has no typed requester field (only a loose `SubjectId`) | `Services/Jobs/JobContract.cs` | yes |
| P3 | The fail-closed refusal lives in `RetrieveMultipleImpersonatedAsync`, not in the shared helper; the helper stamps legacy `MSCRMCallerID`, not `CallerObjectId` | `Spaarke.Dataverse/DataverseImpersonation.cs` | yes |
| P4 | Model 1 (shared app): the message's tenant must bind to the Dataverse environment URL | invariants I2–I5 | before any Model 1 impersonating Function |
| P5 | Behaviour when impersonating a user disabled after enqueue is undocumented | live test | before first use |

## 4. Open risks recorded
- Dead-letter resubmission re-runs work as the user. Resubmit tooling must not allow editing the requester field.
- Throttling attribution under impersonation is undocumented.

## 5. Decision needed

- **Accept:** amend ADR-052 §5/§6 (concise and full) and ADR-028 A5; change the guard pattern; file P1–P3 as
  issues. P1 is worth fixing regardless.
- **Refine:** say what to change.
- **Keep the blanket ban:** record it as a considered decision, with this analysis as the rejected alternative.
