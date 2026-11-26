# Defer / Issue Tracking — spaarkeai-mcp-bff-api-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label spaarkeai-mcp-bff-api-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it. "For future flexibility" / "improve testability" / "separation of concerns" = NOT a valid deferral reason — refuse to file.

---

## Open (in priority order)

### DEF-001 — Copilot widget Decide lane: "done today" not supplied (no v1 tool returns closed-today)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-14 |
| **Source** | Task 031 adversarial review (F4); spec FR-13 (spec.md:232) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2832 |

**Description**

FR-13 asks the widget's category cards for count, oldest age and "done today". Task 031 built the lane, but no v1 MCP tool returns the items closed today, so `DecideLane.tsx` renders the line only when a value is given and nothing supplies one (the widget does not fake it). Failure mode: the Copilot widget's cards lack the progress line the Console shows.

**Entry-points**

- `src/client/widgets/spaarke-decisions/src/lane/DecideLane.tsx` (`doneToday`)
- `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Decisions/ShowDecisionsTool.cs` (where a `closedToday` count could go)

**Suggested fix**

Owner decides: (a) `closedToday` per category on `spaarke_show_decisions` (Services/Signals read, ontology review); (b) a separate app-only tool; (c) drop it from v1 and amend FR-13.

**Estimated effort**: (a) about half a day plus ontology review; (c) a spec edit.
**Blockers**: owner decision.
**Related**: tasks 022, 031.

---

### ISS-001 — April 2026 Copilot agent code: an unregistered handler, two services nothing resolves, and a package the csproj calls "USED"

| Field | Value |
|---|---|
| **Status** | Handed off → spaarkeai-mcp-bff-api-r2 (accepted 2026-11-26; their task 001 commit `8a92da8751` removes it all; closes with their merge PR) |
| **Urgency** | someday |
| **Filed** | 2026-11-05 |
| **Source** | design.md §3 and §8 B-9 (BFF component-model review, 2026-11-05); owner approved filing (Q-11) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2575 |

**Description**

The April 2026 Copilot attempt left a chain of BFF code that no request reaches:
- `SpaarkeAgentHandler` (an `ActivityHandler`) is not registered anywhere: no `AddAgent`, no adapter, no `/api/messages` route. It is the only code that uses `Microsoft.Agents.Hosting.AspNetCore 1.0.1` (through `Microsoft.Agents.Builder` / `.Core`).
- `PlaybookInvocationService` and `EmailDraftService` are registered in `AgentModule` (L55, L58), but no endpoint, handler or test resolves them.
- `AdaptiveCardFormatterService` is registered (`AgentModule.cs:43`), but only those two services inject it. The `/api/agent/*` endpoints carry TODOs saying it is "not available" (`AgentEndpoints.cs:275,538`).

Concrete failure: the csproj comment (`Sprk.Bff.Api.csproj:68-70`) says the M365 Agents SDK package is "USED" by `SpaarkeAgentHandler`. A maintainer reading it keeps and upgrades a package that runs on no path. It still counts toward publish size (§10) and CVE scans, and two DI registrations build services nobody calls.

Not covered by existing issues: #1523 covers `AgentTokenService` / `AgentTokenOptions`; #1579 covers the auth-side dead surfaces and the bot template.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs:21`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:43,55,58`
- `src/server/api/Sprk.Bff.Api/Api/Agent/EmailDraftService.cs`, `PlaybookInvocationService.cs`
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj:68-70,81`
- `grep -rn "SpaarkeAgentHandler\|EmailDraftService\|PlaybookInvocationService" --include=*.cs src tests`

**Suggested fix**

Decide with the Copilot surface's owner. `spaarkeai-mcp-bff-api-r1` replaces this route with an MCP server, so remove the handler, the two services, the formatter and the package (measure the publish-size delta), and correct the csproj comment. Keep the `/api/agent/*` endpoints while the "Spaarke AI" agent package still calls them.

**Estimated effort**: half a day, plus the publish-size measurement
**Blockers**: none. If `spaarkeai-mcp-bff-api-r1` adopts the declarative-agent fallback (design §5.2), do this together with retiring the April OpenAPI action.
**Related**: #1523, #1579, #1574 (the "remove before production" token logger, also found by this design and already filed there); ADR-010; CLAUDE.md §10

### ISS-005 — Rate-limit OnRejected log writes the user's name (a claim value) on every 429

| Field | Value |
|---|---|
| **Status** | Done 2026-11-13 — PR #2759 merged to master (`0d676cc66a`); #2756 closed |
| **Urgency** | soon (MCP routes now reach the handler) |
| **Filed** | 2026-11-12 |
| **Source** | Task 020 adversarial review |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2756 |

**Description**

`RateLimitingModule.cs` `OnRejected` (~L321-326) logs `User.Identity.Name` and the client IP at Warning on every 429. ADR-015 / NFR-05 allow ids, sizes, timings and reason codes, not claim values. Concrete failure: a Copilot client over the 60/min `mcp` limit writes the user's UPN to App Insights on each rejected call.

**Suggested fix**

Log the opaque caller key the limiter partitions by (`oid`, else `sub`), or no user at all; add a regression test that the rejected-request log has no UPN or email. Shared code: the BFF platform owner fixes it, not task 020.

Fix in review 2026-11-12: PR #2759 (branch `fix/2756-ratelimit-log-claim`) logs the opaque caller key and `TraceIdentifier`, no name or IP; regression test `Issue2756_RateLimitRejectionLogTests`; ArchTests 2530/2530. Merge waits on the owner's go.

### ISS-006 — CopilotAuth auth-failure log writes token audience and issuer on every route, /mcp included

| Field | Value |
|---|---|
| **Status** | Done 2026-11-13 — PR #2763 merged to master (`2da422bb9c`, uac-r2 auth gate PASS); #2762 closed |
| **Urgency** | soon (before `Mcp:Enabled` goes on in dev, task 041) |
| **Filed** | 2026-11-12 |
| **Source** | Task 021 adversarial review, pass 1 (reproduced with an expired connector-shaped token on `POST /mcp/legal-operations`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2762 |

**Description**

The `CopilotAuth` `OnAuthenticationFailed` handler (`AuthorizationModule.cs:160-184`) decodes every bearer token that fails the default JwtBearer scheme and logs `aud`, `iss`, `appid`/`azp` and the exception message at Warning, on every route. ADR-028 A10 / NFR-05 forbid logging the caller's token claims (allowed: `oid`, `appid`, `obo`, `tenantId`, `correlationId`). Concrete failure: Copilot presents expired tokens on each refresh cycle, so with MCP on, each refresh writes the BFF audience, the issuer and the connector client id to App Insights. Not #1574 (a different logger).

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:160-184`

**Suggested fix**

Log only the failure code and correlation id (or remove the April-era diagnostic); regression test that the failure log has no audience or issuer. Shared auth file: uac-r2 owns the change; outside task 021's scope.

**Estimated effort**: 1–2 hours plus the test
**Blockers**: none
**Related**: #1574, #2756, ADR-028 A10, ADR-015

---

### ISS-007 — MCP route: Host/Origin validation per the MCP transport requirements

| Field | Value |
|---|---|
| **Status** | Open — owner decision |
| **Urgency** | next-round (decide before task 042's live run) |
| **Filed** | 2026-11-14 |
| **Source** | Task 025 conformance run (`--spec-version 2026-07-28 --suite all`) and its adversarial review |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2835 |

**Description**

The MCP transport requirements for 2026-07-28 ask servers to validate the `Host`/`Origin` of incoming requests. `/mcp/legal-operations` does not enforce this (`AllowedHosts` is `*`; no host or origin filter on the route). The conformance suite's DNS-rebinding scenario fails on it, and task 025 baselined that one check so the rest of the suite can gate. Risk is low: every call needs a valid bearer token, there are no ambient credentials, and CORS is an allow-list. Concrete failure: NFR-09's conformance run stays green only with this check excused.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/Mcp/McpEndpoints.cs` (route mapping)
- `tests/integration/Mcp.Conformance.Host/conformance/conformance-baseline.yml` (the baselined check)

**Suggested fix**

Owner decision first (validate on `/mcp/*`, against which allow-list). Copilot and Cowork call server-to-server, so their `Host`/`Origin` behaviour must be observed live (task 042) before a rejection rule ships; then remove the baseline entry.

**Estimated effort**: 2–4 hours plus a live check
**Blockers**: owner decision; task 042 observation
**Related**: NFR-09, task 025

---

### ISS-008 — Decision wizard person picker shows only the name, so same-name contacts cannot be told apart

| Field | Value |
|---|---|
| **Status** | Handed off → spaarke-ontology-platform-r1 (their DI-170, accepted 2026-11-24, commit 5f4453db1d) |
| **Urgency** | next-round |
| **Filed** | 2026-11-24 |
| **Source** | Owner, live Copilot run on dev `0362599b94` (Assign Task step) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/3062 |

**Description**

In the Assign Task step, "Assign to" lists matching contacts by name only. Dev has three active contacts with the same full name (different email domains), and the owner could not tell which one they were picking. The Confirm step's summary ("Assign to: …") also shows the name only. The server already returns the contact's email on every Person candidate (`DecisionLookupCandidate.Email`), so the data is there: the inline field drops it. Concrete failure: the decision assigns the task to the wrong person, and the summary does not let the assigner catch it before or after recording.

**Entry-points**

- `src/client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField.tsx:726` (an option renders `item.name` only)
- `src/client/shared/Spaarke.UI.Components/src/components/DecisionWizard/steps/ParameterFields.tsx:80-94, 221` (`LookupParameter` maps search results to `ILookupItem` { id, name })
- `src/server/api/Sprk.Bff.Api/Services/Signals/IDecisionLookupSearch.cs:59` (`Email` already on the candidate)
- The Confirm summary of a Person parameter (decision draft keeps `{ id, name }`)

**Suggested fix**

Extend the shared `ILookupItem` with an optional secondary line and render it under the name (reuse, no fork). Carry the email from the lookup result into the draft label so the Confirm summary reads "Name (email)". The Console's side-pane picker is unaffected; check it shows the email too.

**Estimated effort**: 2–4 hours plus tests
**Blockers**: none
**Related**: D-182 (side-pane lookup), task 168 (Assign Task)

---

### ISS-010 — Decision commit takes ~27 s, so Copilot stops waiting and shows "may not have been recorded"

| Field | Value |
|---|---|
| **Status** | Open; sent to spaarke-ontology-platform-r1 (owner of the commit path) for a fix or a hand-off |
| **Urgency** | now |
| **Filed** | 2026-11-25 |
| **Source** | Owner, task 042 live run 3 in Cowork as the non-admin test user; App Insights on dev BFF `a1b7db1def` (`notes/live-e2e-results.md` "Run 3") |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/3184 |

**Description**

One decision with four actions (Create budget, Assign Task, Add To Do, Assign Work) took **26,924 ms** on the server for `spaarke_complete_decision` (outcome ok, 2026-11-25 21:09:27Z). The Copilot host stopped waiting before the answer arrived, so the widget showed "The decision may not have been recorded". It had been recorded. Try again took 697 ms, made 13 reads and 0 writes, and returned the server-side replay, so there was one Decision Record and no duplicate writes or email. The safety path works. The cost does not: NFR-01 sets 15 s for complete and 30 s as the hard bound for every call, and this call used 90% of the hard bound. A user who sees "may not have been recorded" after every multi-action decision stops trusting the tool, and a slightly slower call would cross the 30 s bound. The Console's REST commit runs the same service, so it is likely as slow there.

Where the 27 s went, in one call (dependency sums):
- about 110 SDK (`Organization.svc`) calls, 13.3 s;
- 51 caller-access calls on `systemusers(…)/Microsoft.Dynamics.CRM.…`, 5.1 s;
- about 40 `EntityDefinitions` metadata reads (the same tables re-read: `sprk_matter` ×8, `sprk_recordtype_ref` ×8, `contact` ×4), about 3.5 s;
- four writes (budget, event, to-do, work assignment), about 1.4 s;
- inside the same request after the writes: the assignee notification email (Key Vault secret, token, `sendMail`, sent-items read, SPE archive PUT), about 3 s, and two Azure OpenAI `gpt-4o-mini` calls plus analysis-action reads, about 1.3 s.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionCommitService.cs` (commit order and writes)
- `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionCommitFollowUps.cs`, `AssignTaskNotifier.cs` (what runs after the writes)
- App Insights app `6a76b012-…`: `dependencies | where operation_Id == '23ff642a15526e99ab9df9b869eef423' | summarize count(), sum(duration) by type, target, name`; the retry is `f265871405f3bf6b5b6a74db7942a4ed`
- `traces | where message startswith 'MCP tool spaarke_complete_decision'` for the duration of each call

**Suggested fix**

Hypotheses for the owner to validate:
- Move the email, archive and AI work out of the request into the ADR-004 job queue once the record is written (non-fatal already per task 043).
- Cache the `EntityDefinitions` reads per process.
- Batch or memoise the per-record caller-access checks within one commit.
- Find what the ~110 SDK calls are; that is the largest single block.

No change to validation, idempotency or execution order (spec MUST NOT).

**Estimated effort**: 1–2 days, starting with a profile of the SDK calls
**Blockers**: none
**Related**: NFR-01; ontology task 222 (the read-side fixes); task 043 (follow-ups must not fail the commit); ADR-004, ADR-052

---

### ISS-009 — Assign Task email refused as "no access" for an assignee whom Dataverse lets read the task

| Field | Value |
|---|---|
| **Status** | Handed off → spaarke-ontology-platform-r1 (their DI-171, accepted 2026-11-24, commit 5f4453db1d). Owner's fix (ontology task 207): new warning code `assignee_email_not_sent_admin_account` for an Administrative-access assignee. No widget change here: the widget uses the shared `describeWarning`, and `spaarke_complete_decision` passes `warnings[]` through verbatim; the sentence reaches Copilot with the next dev BFF build that includes their PR (https://github.com/spaarke-dev/spaarke/pull/3068, closes #3063). |
| **Urgency** | next-round |
| **Filed** | 2026-11-24 |
| **Source** | Owner, live Copilot run on dev `0362599b94`: the commit at 2026-11-24 14:40:38Z warned `assignee_email_not_sent_no_access` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/3063 |

**Description**

The task was created and assigned; the email was not sent, with "the assignee does not have access to this record". Read-only checks the same day found nothing that refuses: the contact is active and linked (`sprk_primarycontact`) to exactly one enabled systemuser whose Entra oid matches the contact's binding; the user is Administrative access mode, unlicensed, with no security roles of its own, and is a member of its business-unit team (role "Spaarke Basic User"), which owns the task and the matter; the matter is not secure; no No Access entry names the matter or the user; and `RetrievePrincipalAccess` answers Read for that user on both the task and the matter. The gate's reason line (`[ASSIGN-TASK-NOTIFY] … no email, {Reason}`) was dropped by App Insights sampling, so which check refused is unknown. Concrete failure: an assignee who can see the record gets no email, and the assigner is told they lack access, which sends them looking for a security-role problem that may not exist.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Signals/AssignTaskRecipientGate.cs:174-278` (internal path; every `NoAccess` exit logs its reason at Information)
- `src/server/api/Sprk.Bff.Api/Services/ExternalAccess/ContactUserRepresentation.cs:61-155`
- App Insights: `traces | where message startswith '[ASSIGN-TASK-NOTIFY]'` (the 14:40Z line is missing; sampling ~33%)
- Dev task `67913cae-2f3d-4184-a40b-1921f826f6b7`, assignee contact `65e3a2c9-d1be-f111-a05b-3833c5e9614d`

**Suggested fix**

Reproduce it: run the gate for that contact and task (a test over dev data, or a repeat assignment with the reason line captured), name the check that refused, then decide whether it is a defect or intended (for example, if Administrative-mode users should not be emailed, the warning should say so rather than "no access"). Consider excluding the gate's reason line from sampling, since it is the only record of why an email was withheld.

**Estimated effort**: 2–4 hours to reproduce; fix unknown
**Blockers**: none
**Related**: D-199, task 168, uac-r2 task 193 (`ContactUserRepresentation`)

---

### ISS-004 — The decision commit does not require a prior plan read, although the spec says it must

| Field | Value |
|---|---|
| **Status** | Closed 2026-11-07: intended behaviour (ontology ruling). A missing stamp means "review starts now", the fail-safe direction. Spec FR-08, NFR-06 and the MUST rule are amended; the pinning test is kept. |
| **Urgency** | soon (before task 024 ships the MCP complete tool) |
| **Filed** | 2026-11-07 |
| **Source** | Task 014 (B-1 commit capability), acceptance criterion "a commit with no prior plan read returns the existing refusal reason code" |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2628 |

**Description**

NFR-06, FR-08's negative case, the MUST "fail closed on … a missing plan read" and design §11 say a commit needs a prior plan read. `DecisionCommitService` does not refuse a commit that has no plan-read stamp: it takes the review start as now (`DecisionCommitService.cs:672-674`). The only refusal that a missing plan read causes is `ontology.decision.signal_not_open` (409), for a Signal the system closed before the commit.

Concrete failure: a client, or an MCP host after task 024, that sends a confirmed review with the plan's action codes and never reads the plan gets the decision recorded while the Signal is open. The commit is still as the caller, rights-checked, confirmed and idempotent; only the "plan read first" condition is not enforced.

**Suggested fix**

Owner and ontology choose: (1) amend the spec text (the stamp sets the review start and is not a gate), or (2) ontology adds a refusal for "no stamp" in `DecisionCommitService`, which is a REST contract change with a new 011 baseline case. Do not gate only the MCP tool (a second rule outside the single owner; ADR-002 WP-1, ontology condition 2). Task 014 could not do either: the spec forbids changing `DecisionCommitService` validation and B-1 adds no rule.

**Estimated effort**: option 1, one spec edit; option 2, half a day in ontology plus a baseline case
**Blockers**: owner decision; option 2 needs ontology-platform
**Related**: task 014 (`DecisionCommitServiceTests.Capability.cs` pins today's behaviour), task 024, FR-06, FR-08, NFR-06

---

## In Progress

### ISS-003 — PrecedentAdminEndpoints background task may use a disposed request-scoped service (unconfirmed)

| Field | Value |
|---|---|
| **Status** | Closed 2026-11-26, not a defect: every dependency the task reaches is a singleton or has no disposable state (evidence in the #2623 comment). Optional hardening (fresh scope) left to the Precedents/Insights owners. |
| **Urgency** | soon |
| **Filed** | 2026-11-07 |
| **Source** | Sibling scan for ISS-002 (#2620 / PR #2622) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2623 |

**Description**

`Api/Insights/PrecedentAdminEndpoints.cs:294` starts a fire-and-forget `Task.Run` that uses `IPrecedentProjectionSync`. That service is request-scoped (`Infrastructure/DI/InsightsModule.cs:67`). If the task outlives the request, it can use disposed services. This is not caused by this project; the Precedents/Insights owners decide the fix (a fresh scope inside the task, or an ADR-004 job).


### ISS-002 — ComposeProfileDispatcher may null the originating request's HttpContext

| Field | Value |
|---|---|
| **Status** | Fix in review: PR #2622, updated from master 2026-11-26 (`605438d64f`, no conflicts, defect still on master, 5/5 regression tests pass and fail without the fix). No reviewer yet; owner checklist item H. |
| **Urgency** | now |
| **Filed** | 2026-11-07 |
| **Source** | Read-only investigation for task 010 (option C analysis), 2026-11-06 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2620 |

**Description**

`ComposeProfileDispatcher.cs:130-134` sets `IHttpContextAccessor.HttpContext` inside `Task.Run`. In ASP.NET Core 10.0.14 the setter clears the existing AsyncLocal holder's context. That holder flowed into the task from the originating Compose save request, so code later in that request may read a null context intermittently. That code comes after `ComposeService.cs:2323`. A null context would make caller-dependent paths fail closed. No test proves it yet.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeProfileDispatcher.cs:88`, `:117-134`
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:2323`

**Suggested fix**

Reproduce it with a test first. If it is real, never mutate the shared holder inside the task: use an explicit detached context, or `ExecutionContext.SuppressFlow()` plus a fresh holder.

**Estimated effort**: 0.5–1 day
**Blockers**: none
**Related**: task 010 option C (owner, 2026-11-06); Compose owners review the fix PR

---


*None.*

---

## Closed (Done / Won't Fix / Superseded)

*None.*

---

## Notes

- IDs are sequential per kind: DEF-001, DEF-002, ... ISS-001, ISS-002, ...
- ID never gets reused after closure — preserves traceability.
- **Already filed elsewhere, not duplicated here:** the "DEBUG" token logger (`MiddlewarePipelineExtensions.cs:121-158`) is #1574 (labels `unified-access-control-r2`, `spaarke-auth-system-of-record-r1`).
