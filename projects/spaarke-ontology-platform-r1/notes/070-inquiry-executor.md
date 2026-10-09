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
3. **Base is origin/master**, which has none of the project-branch code (`Services/Signals/*`, 036 catalog). Nothing here depends on it. The new `Services/Signals/Actions/` folder will sit beside the project's `Actions/DecisionActionCatalog.cs` after merge.
4. **ADR-038 schema pairing**: verified against live spaarkedev1 via Dataverse describe on 2026-10-09: `sprk_servicerequest` has `sprk_direction` (Inbound 100000000, Outbound 100000001, required), `sprk_regardingmatter` lookup, `sprk_regardingrecordid`, `sprk_regardingrecordtypelogicalname`, `sprk_responseduedate` (date only), `sprk_disposition`. No automated live-schema test was added.
5. No live (spaarkedev1) execution of the send: sending real mail and creating rows was not allowed by the POML for this task. Live proof belongs to 043/055.

## Secure matters
`OwnedChildWrite.CreateAsync` refuses a service request under a secure matter (`SecureFilingRefused`: a non-child table cannot be created under a secure record). The executor returns Refused with that text. Task 043 should surface it. Flagged for the owner.
