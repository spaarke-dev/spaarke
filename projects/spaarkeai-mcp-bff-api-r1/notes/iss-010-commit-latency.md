# ISS-010 (#3184): the decision commit takes ~27 s (2026-11-25)

> Live call: dev `a1b7db1def`, 2026-11-25 21:09:00.8–21:09:27.7Z, `spaarke_complete_decision`, four actions (Create budget, Assign Task, Add To Do, Assign Work), outcome ok, 26,924 ms. App Insights op `23ff642a15526e99ab9df9b869eef423`; the retry `f265871405f3bf6b5b6a74db7942a4ed` was the replay (697 ms, 13 reads, 0 writes). Detail of the issue: `defer-issues.md#iss-010`.
> Owner (2026-11-25): "if you are able to fix it more expeditiously than ontology-r1 then fix it and coordinate". ontology-r1 agreed: we fix it, #3184 stays ours, PR from `origin/master` (at or after `feef9a9c72`), auto-merge off, their independent Opus review, uac-r2 also reviews (second auth pass) if caller-access changes.

## Dependencies of the one call

| Block | Calls | Time |
|---|---|---|
| SDK `Organization.svc` (ServiceClient). App Insights records no operation name, only the URL. Spread evenly over the whole 27 s, 3–13 per 2 s bucket, including after the writes. | 113 | 13.3 s |
| Caller access `systemusers(…)/Microsoft.Dynamics.CRM.*` (RetrievePrincipalAccess, RetrieveUserSetOfPrivilegesByNames), as the caller | 51 | 5.1 s |
| `EntityDefinitions` reads, the same tables repeated | ~40 | ~3.5 s |
| `sprk_recordtype_refs(<id>)`: the same id, 8 times | 8 | 0.8 s |
| The writes (budget, event, to-do, work assignment) | 4 | 1.4 s |
| Assign Task email: Key Vault footer secret, token, `sendMail`, sent-items read, SPE `.eml` archive PUT | | ~3 s |
| `sprk_analysisactions` ×2, `gpt-4o-mini` ×2 (likely the queued `.eml` analysis job sharing the operation id; inferred) | | ~1.3 s |

## Code map (code-mapper agent, Sonnet, read-only; per-site counts inferred)

- **Each step's access checks run twice.** The route preflight (`DecisionCommitService.cs:1367`) runs the step's checks, then `DecisionExecutorBase.ExecuteAsync` runs them again (`DecisionActionExecutors.cs:362-364`; preflight body `:1260-1277`).
- **The create pipeline is most of the SDK calls (inferred, not traced).**
  - Call chain: `DecisionRouteCores.CreateChildAsync` (`:74`) → `ChildRecordEndpoints.CreateAsync` (`:276`) → `RecordCreationService.CreateAsync` (`:313`).
  - The pipeline: owner resolution, `_rootFiling.PlanCreateAsync` (`:843`), stamp and inherited-access answers (`:875`, `:954`, `:1193`), `AfterCreateAsync` (`:268`), `_restamper.AfterWriteAsync` (`:2451`), `AssignedAccessMaterializer` (`:2504`, `:2524`), number column (`:2640`).
  - This runs 4×. Next step: profile it.
- **Caller access (51).**
  - `RetrievePrincipalAccess`: `CallerRecordAccessProbe.cs:1084` (via `DecisionExec.CallerHoldsAsync`, `DecisionActionExecutors.cs:258`) and `CallerWriteAuthorization.cs:782` (`RightsOnAsync` ← `CallerMayAppendToAsync` `:466`).
  - `RetrieveUserSetOfPrivilegesByNames`: `CallerRecordAccessProbe.cs:596`; `CallerWriteAuthorization.cs:116`, `:522`, `:575`, `:643`.
  - All four creates ask AppendTo on the same matter.
  - Only WhoAmI is memoised (`CallerWhoAmIMemo`). `CallerMetadataReadMemo.cs:15-17`: "no authorization answer is cached" (ADR-009).
- **EntityDefinitions (~40).**
  - Reads: `CallerWriteAuthorization.cs:98`, `:146`, `:332`, `:492`, `:630`; `DataverseWriteItemMapper.cs:181`, `:220`, `:313`; `OwnedChildWrite.cs:912`; `SecureRootInheritance.cs:1649`; `DecisionParameterSources.cs:171`.
  - The existing `CallerMetadataReadMemo` (`Infrastructure/Dataverse/CallerMetadataReadMemo.cs:37`) is per-create local and is cleared on any write. So it does not help across the 4 creates or across the two passes.
  - The other caches (`DataverseServiceClientImpl._entitySetNameCache`, 15 min; `DataverseWebApiService` caches) are not on this path.
- **`sprk_recordtype_refs` ×8.** `CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync` (`:516`, `:530`): the same Matter type-ref id on every create, 4 creates × 2 passes.
- **Email.**
  - Inline and awaited: `DecisionCommitService.cs:1532` → `DecisionCommitFollowUps.NotifyAssigneesAsync` (`:69`) → `AssignTaskNotifier.NotifyAsync` (`:273`) → `CommunicationService.SendAsUserAsync`. The send has a 10 s bound, and the archive is awaited.
  - **The `assignee_email_not_sent` warning comes from the send step**, not the recipient gate (`AssignTaskNotifier.cs:193`, `:302`, `:462`, `:477`, `:497`, `:532`). The gate's own refusal is a separate warning (`AssignTaskRecipient.Warning`, `:283-287`).
  - So queuing the send drops "not sent" outcomes from the response: **owner decision.**
  - The system-mailbox fallback is already a job (`AssignTaskEmailJobHandler`).
- **The AI calls** most likely come from the queued `AppOnlyDocumentAnalysisJobHandler` on the archived `.eml`, not from the request (inferred).

## ontology-r1's constraints (2026-11-25)

- **Email.** Keep the recipient gate inline; queue only the send, archive and AI. The job must be idempotent on replay. A send failure is logged with the Decision Record id. Check what task 043 promised. A visible contract change is an owner decision.
- **Metadata cache.** Reuse an existing cache before adding one. A new cache needs the §11 answers, and an in-process cache needs an ADR-009 tension. Metadata only, never rows or access answers, with a bounded TTL.
- **Access memo.**
  - Per commit request only, keyed (caller, table, record, right).
  - Re-ask after any write that can change access.
  - Never cache a fault.
  - The code lives in uac-r2's area, so they review it.
- **Must not change:** D-17 (one Decision Record, written last), replay, execution order, validation, the Signals baselines.
- **Checks:**
  - run `~Signals` + full ArchTests;
  - measure the publish-size delta;
  - the PR names the SDK operations, shows a before/after on the same four-action decision with simulated latency, and is re-timed on dev after merge.

## Candidate fixes (not started)

1. A commit-scoped metadata memo: widen `CallerMetadataReadMemo` from one create to one commit request, for `EntityDefinitions` only (it is never cleared by row writes, which don't change metadata). Saves ~30 reads, ~3 s. It is request-scoped, so it is not a cross-request cache.
2. Memoise the pre-write access answers within the preflight pass only (all four creates ask about the same matter). Every check after a write re-asks. uac-r2 reviews.
3. Profile `RecordCreationService` (the 113 SDK calls) with a call-counting fake, and remove repeated reads. This is the biggest block.
4. The email send off the request: owner decision (see above).

## Implementer stop (Opus, 2026-11-25 ~23:00Z); branch `perf/decision-commit-latency` (from master, no changes yet)

- **No existing harness counts the real SDK calls.**
  - `DecisionCommitServiceTests` uses fake executors.
  - `SecureChildOwnershipAiToolTests.DecisionCommit.cs` runs the real path but fakes every service that makes SDK calls.
  - A profile needs a new world: the real ownership resolver, `CoreAncestorRestamper`, `SecureChildShareSynchronizer`, `SecureRootFilingGate` and `AssignedAccessMaterializer`, all over one scripted, counting `IDataverseService`, plus `IDataverseRecordShareService` and `IAccessibleRecordSetService`. Calls are counted per interface method and table.
- **Fix 2 (access memo) design gap.**
  - The creates are written app-only (`IFieldMappingDataverseService`), not through the caller's client. A memo on the caller's client would not see those writes, so it could answer from before a write.
  - A safe memo needs a commit-scoped write signal raised by the app writer, the restamper, sharing and the materializer. That is cross-cutting work in uac-r2's area.
  - The rule "re-ask after a write" also leaves only these repeats: duplicate questions across the four preflights, step 1's execute-pass recheck, and repeats within one create's two passes. The 8 `sprk_recordtype_refs` reads would drop to about 4, not 1; those reads are an access answer, not metadata.
- **Fix 1 (metadata memo) carrier.** Today's memo instances are locals in `RecordCreationService.DecideAsync`. For one commit, `HttpContext.Items` would carry it (as `CallerWhoAmIMemo` does). Off-request callers (chat tool, jobs) need another carrier or no memo. Dropping "cleared on any write" for metadata changes that class's documented contract.
- **Split, one PR at the end:**
  - A: profile harness, equivalence tests and the notes table. This gates the rest.
  - B: request-scoped metadata memo.
  - C: access memo, designed with uac-r2 first; two auth review passes.
  - D: repeats inside create, scoped from A's numbers.
  - E: verify and deliver.

## uac-r2's design for C (2026-11-25): build it here; uac-r2 reviews the design note, then the diff as an access gate; plus one Opus adversarial review here

- **No cross-cutting "a write happened" signal.** A writer that forgets to raise it leaves a stale allow, which is a security bug.
- **(a) Privilege-set asks** (`RetrieveUserSetOfPrivilegesByNames`, `CallerHoldsPrivilegeAsync`): memo per request. A commit never edits roles or team membership.
- **(b) `sprk_recordtype_refs` and other config or metadata reads:** memo per request. These are not access.
- **(c) Record access** (`RetrievePrincipalAccess` and the probe): a memo OWNED BY THE COMMIT ORCHESTRATOR, keyed (caller, table, record, right). The orchestrator clears it after every step that performs any write, whoever the writer is. Preflight answers carry into execute up to the first writing step. This covers the execute recheck and the four AppendTo asks before the first create.
- **(d) Never cache a fault.** A denial is reused only inside the same no-write window.
- **(e) Tests:**
  - must-fire: an earlier step's write removes access (restamp to Secure or Restricted), and the later step re-asks and is refused;
  - must-not-fire: two steps with no write between them make one call;
  - fault path: a fault is not cached.
- **Sequence:** part 2 PR after part 1 (A+B). The design note goes to uac-r2 first.

## Part D scoping: where the ~64 unmodelled SDK calls come from (code-mapper, Sonnet, read-only, 2026-11-25; counts inferred from call sites)

**Main finding: the Assign Task email's post-send work runs inside the commit request, and it is most of the unmodelled block: an estimated 55–80 SDK calls, which brackets the 64-call gap.** It is far more than the "~3 s email" first estimated. `CommunicationService` runs it after a successful send. Estimated calls per stage:

| Stage | Where | Est. SDK calls |
|---|---|---|
| Archive the `.eml`: owner resolve, container, `sprk_document` create, permission refresh | `CommunicationService.ArchiveToSpeAsync` :3804 (called :3045) | 13–16 |
| Thread resolve, create and assign | `ThreadResolver.ResolveAndAssignCoreAsync` :144 (via :844) | 8–11 |
| **AI enrichment (triage and propose; the 2 `gpt-4o-mini` calls are likely here, not a queued job)** | `CommunicationEnrichmentService.EnrichAsync` :205 (called :3139) | 6–10 |
| Participant index | `CommunicationParticipantIndexer` (called :3160) | 7–9 |
| "Communication arrived" notification | `CommunicationArrivedProducer` (called :3168) | 5–7 |
| Filing stamp and owner resolve before the send | `BuildDataverseRecordForUserAsync` :2939/:3260 | 5–6 |
| Notifier and send tail: contact read, sender check, record create, message-id stamp, bell | `AssignTaskNotifier`, `CommunicationService` :1468/:1573/:4979 | 5 |
| Secure families read before the first write | `DecisionCommitFollowUps.SecureFamiliesAsync` (`DecisionCommitService.cs:1573`) | 3–5 |
| Budget currency (secure BU, BU, currency) | `DecisionParameterSources.ResolveBudgetCurrencyAsync` :239 | 3–4 |

**Harness mislabel:** `DecisionCommitCallProfile.Sdk()` counts `IFieldMappingDataverseService` as SDK, but it is Web API live (`GraphModule.cs:152`), so the true gap may be ~68–70.

**Fix candidates, biggest first:**
1. **E: the email's post-send work off the request.**
   - Archive, thread, AI enrichment, participant index and the arrived notification move to a queued, idempotent job.
   - The send stays inline, so a failed send still returns `assignee_email_not_sent` (owner rule 2026-11-25).
   - It removes most of the ~55–80 calls plus the AI, and is likely the largest single saving.
   - Touches `CommunicationService` (communication owners) and needs their review.
2. **D1:** a request-scoped memo of business-unit and team config reads (owner resolver, share sync, secure BU, currency). ~30–35 repeats; safe, since the commit never writes those rows.
3. **D2:** pass the in-memory `sprk_communication` row forward instead of re-reading it. ~8–10 reads. Unsafe as a read cache, because the row is written several times.
4. **D3:** share the `sprk_recordtype_refs` and matter stamp reads between the creates and the communication, per request.

### Part E constraints (ontology-r1, 2026-11-25)

**Owners**
- field-mapping-r1 first: most recent changes, including #3133.
- ontology reviews too: task 177 / D-217 archive of filed outgoing email (#2666), and task 216 ArchiveOutgoingOptIn (#3117).
- The private-message path (056b `privateContext`) is another project's; E must leave it alone.

**Stays inline**
- The send.
- The `sprk_communication` create with its `sprk_correlationid`, filing lookups and internal-only flag. That row is the idempotency key the system-mailbox job's `AlreadySentAsync` reads (AssignTaskNotifier ~:655). Moving it would allow a second email on replay.

**May go async:** thread, enrichment (AI), participant index, arrived producer. Nothing in the commit or the Decision Record references them. Check that the arrived notification drives no user-visible message within the request. Enrichment's AI must stay out of anything the decision depends on.

**Archive (OWNER DECISION).** D-216: the budget inquiry's "sent, but .eml not archived" warning is returned in the commit response (`executed[].Warning`, DecisionCommitService ~:1522). If the archive goes async, that warning cannot be returned. Either:
- keep the archive inline, or
- the job raises a bell to the decider on archive failure.

D-217: every filed outgoing email becomes an `.eml` Document in Messages. Async means a short lag. The job is idempotent (one Document per communication) and retried. Say so in the PR.
