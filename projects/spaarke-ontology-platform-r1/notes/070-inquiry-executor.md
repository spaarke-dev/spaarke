# Task 070 - the Inquiry executor (2026-10-09)

## What shipped
`Services/Signals/Actions/BudgetInquiryExecutor.cs` (+ `InquiryEmailSender`), registered scoped in `CommunicationModule`.
Contract for task 043: `ExecuteAsync(BudgetInquiryRequest{MatterId,To,Subject,Body,Confirmed}) -> BudgetInquiryResult{Outcome,ServiceRequestId,CommunicationId,Error}`.
Outcomes: Sent, NotConfirmed, Invalid, Refused, CreateFailed, SendFailed (SendFailed still returns the service request id).
Writes no Decision Record, closes no Signal, no chat session, no AI call, no SLA, no sprk_responseduedate.

## Reuse (CLAUDE.md section 11)
- Existing: `OwnedChildWrite.CreateAsync` (uac-r2 G5: caller's Create + AppendTo checked as the caller, application writes the row owned by the team `IRecordOwnershipResolver` names) and `CommunicationService.SendAsync` (SendMode.User, OBO).
- Extension: nothing new except two classes. No new endpoint, no route, no census entry.
- Cost of doing nothing: criterion 10 cannot pass without an effect.

## Deviations
1. **Action row + Binding NOT authored.** The POML outputs list them, but step 3 (amended D-17) says the executor is not wired from a row, and no acceptance criterion needs a row. A `sprk_playbookconsumer` row with a tool description would make the send text-projectable in chat (violates D-52), and a coded row without a registered workflow class fails dispatch (`dispatch.action-kind-unsupported`). The catalog code `send-budget-inquiry` (task 036, on the project branch) is the contract. Owner to decide whether a descriptive row is wanted after 043.
2. **Created via the G5 pattern, not a run-as-user POST.** POML step 4 says "as the caller"; NFR-10 says check as the caller then write. A run-as-user POST of a row filed under a matter fails `RecordOwnerAssignmentCensusTests.EveryRunAsUserWriteIsClassified` (uac-r2) and leaves the row owned by the caller in an ordinary BU. G5 satisfies NFR-10 exactly.
3. **Base is `docs/ontology-platform-design`** (round 1 first cut it from master, which has none of the project-branch code; PR #1509 is superseded by #1512). The executor sits beside the project's `Actions/DecisionActionCatalog.cs`; it does not depend on it.
4. **ADR-038 schema pairing**: verified against live spaarkedev1 via Dataverse describe on 2026-10-09: `sprk_servicerequest` has `sprk_direction` (Inbound 100000000, Outbound 100000001, required), `sprk_regardingmatter` lookup, `sprk_regardingrecordid`, `sprk_regardingrecordtypelogicalname`, `sprk_responseduedate` (date only), `sprk_disposition`. No automated live-schema test was added.
5. No live (spaarkedev1) execution of the send: sending real mail and creating rows was not allowed by the POML for this task. Live proof belongs to 043/055.

## Secure matters
`OwnedChildWrite.CreateAsync` refuses a service request under a secure matter (`SecureFilingRefused`: a non-child table cannot be created under a secure record). The executor returns Refused with that text. Task 043 should surface it. Flagged for the owner.

## Round 2 (independent review)
- **Reply link:** the service request is the FIRST association, because `CommunicationService.MapAssociationFieldsAsync` maps only `associations[0]` to `sprk_regarding*`; the email's `sprk_regardingservicerequest` is set and the footer names the inquiry. Chosen over a later PATCH of `sprk_servicerequest.sprk_regardingcommunication` (a second write that could fail after the mail is out). Cost: the communication is no longer directly regarding the matter; the matter is reached through `sprk_servicerequest.sprk_regardingmatter`, and the matter stays second (counted in `sprk_associationcount`, not mapped). Live check of the matter timeline belongs to 043/055.
- **Per outside firm:** optional `OutsideFirmId` -> `sprk_regardingorganization` (AppendTo checked).
- **`PreflightAsync`** (null = would proceed): confirmation, shape, WhoAmI, caller Create/AppendTo, owner and secure-matter decision, no writes. The secure test mirrors `OwnedChildWrite.CreateAsync` for this table; a parity test pins it.
- Body is `BodyFormat.PlainText`. The send ignores the caller's token once the row exists, and any send exception (including cancellation) returns SendFailed with the id.
- Mutation proofs (each fails the suite): drop/reorder service-request association, HTML body, shared-mailbox send, empty-To guard, org bind, firm lookup, preflight secure check, preflight rights check, send gets ct, cancellation not caught.
