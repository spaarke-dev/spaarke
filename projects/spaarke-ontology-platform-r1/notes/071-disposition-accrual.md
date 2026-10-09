# Task 071 - disposition accrual (2026-10-09)

## What shipped
- `Services/Signals/Actions/InquiryDispositionService.cs`: `ResolveFromReplyAsync(replyCommunicationId, InquiryDisposition)`, `GetByMatterAsync`, `GetByOutsideFirmAsync(firmId?)`.
- `Services/Signals/Actions/PolicyActionRateService.cs`: `GetAsync()` -> acted / surfaced per (policy code, lane) from `sprk_signal.sprk_resolutiontype`.
- Both registered scoped in `CommunicationModule` beside the 070 executor. No endpoint, no route, no census entry, no schema change.

## Section 11
- Existing: 070's executor already writes `sprk_regardingmatter` and `sprk_regardingorganization`; the reply already carries `sprk_regardingservicerequest` (RegardingFieldMap). Per-firm needed no new column (escalation trigger not fired).
- Extension: nothing existing resolves a service request or reads Signal resolutions; the two classes sit in the 070 folder.
- Cost of doing nothing: criterion 10's second half (reply resolves, dispositions queryable per matter and per firm) and the section 7 metric cannot be shown.

## Decisions
1. **The disposition value is an input.** No document says how a reply maps to Write-off / Budget Revised / Scope Approved / No Action, and ADR-013 keeps AI out of the executor path. `ResolveFromReplyAsync` takes it from the caller (reviewing human or a later classifier). OWNER QUESTION: who chooses it, and is the inbound pipeline to call this automatically?
2. **Resolving = set `sprk_disposition` + deactivate (statecode 1 / statuscode 2).** First reply wins; a later reply gets AlreadyResolved. Conditional on `versionnumber` (If-Match) so racing replies cannot both win; update-only, never creates.
3. **Resolution is an application write** (an inbound email has no caller). The service is internal: the caller must be the inbound pipeline / a route that already authorised the user. It has no auth of its own.
4. **Queries run as the caller** (`IDataverseUserClient`, FetchXML aggregate, no paging). A user's tally covers what they may read. A refused read throws, never an empty tally. Inquiries with no firm group under `FirmId = null`.
5. **Action rate, both lanes, from `sprk_resolutiontype`.** Surfaced = every Signal of the policy (open or closed, any resolution); acted = Acted. Superseded / Policy Retired are counted in the denominator (spec names acted / surfaced; no exclusion is stated). OWNER QUESTION: should administrative closures be excluded?
6. The spec calls the metric a "zero-code Dataverse rollup". A rollup field would be a schema change (no Dataverse writes allowed here); this is the BFF-side aggregate over the same column. A rollup can replace it later without changing the source rule.

## ADR-038 pairing
Describe of sprk_servicerequest, sprk_communication, sprk_signal on spaarkedev1 (2026-10-09), plus the GROUP BY shapes run through read_query on sprk_signal and sprk_servicerequest (valid, zero rows). The FetchXML itself was not run live (read_query is SQL only).

## Mutation proofs (each fails the suite)
second reply overwrites; outbound check dropped; incoming check dropped; state filter added to the fetch; action rate read from sprk_decisionrecord.

## Size
Release publish: 192 files both sides; +28.6 KB vs the current project-branch tip (127,754,340 -> 127,782,956 bytes, whole publish dir incl. symbols).
