# Task 188 — D7 T7: one predicate narrows every external document surface

> #2126 / ISS-015. Design: `notes/2119-manage-access-all-forms-design.md` §6.
> Branch `uac/188-d7-document-narrowing` (worktree `C:/wt188`), based on `origin/master` 4f5f22694c.
> Status: code, tests and guard are done; the draft PR (#2170) is open. Not deployed. The R3 deploy, the live gate and
> the field-security `-Apply` are main-session or owner steps (§10).
> Round 2 (main-session decisions on #2170, 2026-10-20): every ancestor is checked; every read the predicate makes runs
> app-only on every surface; the cost is measured, logged and bounded (§1, §4, §6, §11).
> Round 3 slice A (security pass 1, F1-A, 2026-10-21): the AI retrieval trim applies the narrowing for external callers
> (§14). Slices B and C are separate.

## 1. The rule (as built)

A document is visible to a caller who is not an internal systemuser only when ALL of these hold:

1. **Root rule.** `ExternalMultiRootRule` allows it. This is unchanged.
2. **Own value.** Its own stored `sprk_accesspermission` is not Restricted (100000002).
   - A value that is not a known level hides it (fail closed).
   - Null or absent counts as Standard.
3. **Ancestor value** (main-session scope addition and decision, 2026-10-20). On the bounded `sprk_parentdocument` walk,
   NO ancestor's own value is Restricted. An unreadable ancestor value also hides it.
   - Rule text: **"An ancestor document's own Access Permission = Restricted hides the descendant (an attachment under a
     Restricted .eml is hidden), wherever it sits on the walk, even when the descendant carries its own visible
     communication link. The ancestor's value is read on the same walk as rule 4. An unreadable ancestor value fails
     closed."**
   - So the walk continues past the first communication link, to the top of the chain (at most 3 hops).
4. **Communication.** The communication it comes from passes the task 065 rule:
   - flag JSON `false`;
   - a thread set;
   - that thread explicitly Open now.

   Which communication counts (unchanged):
   - the document's own `_sprk_relatedcommunication_value`;
   - when that is empty, its parent's (`sprk_parentdocument`), then the grandparent's, at most **3 parent hops**;
   - the **first link found decides**. An ancestor's link above it is not consulted.

   The 065 rule is reused, not copied. It moved into `CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync`.

**Fail closed.** Each of these hides the document:
- a link that is present but not a GUID;
- a parent that does not come back;
- a cycle;
- a chain that would need a fourth hop;
- a communication that does not come back.

**Recorded trade-off (accepted by the main session, 2026-10-20).** Because rule 3 must be decided to the top of the
chain, a cycle or a chain longer than 3 hops hides the document **even when the document has its own visible link**.
Pinned by `ADocumentWithItsOwnVisibleLink_AndAChainThatNeedsAFourthHop_IsHidden`, `ADocumentWithItsOwnVisibleLink_InACycle_IsHidden`
and the route case `own-visible-link-four-hop-chain`.

A failed read hides the whole page, and the response says `Truncated`. The assistant's tools fail the call with no data.

**Own value wider than the root.** It changes nothing, because visibility is "root AND own".

**Internal systemusers** on the external plane are not narrowed (confirmed intended, 2026-10-20). This is the 065 rule's
own exemption (`CommunicationThreadReadService`).
- A caller is internal only when `CallerPrincipal.SystemUserId` is set AND `ISystemUserIdentityResolver.IsExternalAsync`
  is false.
- A contact, a systemuser flagged external, and a failed lookup are all narrowed.
- The resolver reaches `ExternalDataService` as an optional constructor parameter, which the typed-client factory
  supplies (`ExternalAccessModule.cs:95`; the resolver is a singleton).

**Whose reads (main-session decision 2, 2026-10-20).** The predicate is a security check, so EVERY read it makes runs under
the BFF's app-only identity, on every surface:
- the documents' own columns (Access Permission and both links);
- the parent chain;
- the communications and the threads.

The external plane already read app-only (`ExternalDataService.QueryRowsOrThrowAsync`). The assistant's tools now reach the
predicate only through the injected `AppOnlyDocumentNarrowing` delegate
(`ExternalDataService.ReadAndNarrowDocumentsAsync`, registered in `ExternalAccessModule`). It re-reads the documents BY ID
app-only and returns only a hide decision. The user's own read still supplies every row the model sees; the narrowing can
only remove rows from it. A link that field security masks for the user (it reads as null) can therefore never read as
"no link". The user-OBO reader (`ExternalCallerCommunicationGuard.QueryOver`) is removed.

**The I-14 amendment** is in `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. I-16 is corrected there: "no access
decision reads a child's copy, EXCEPT the external document narrowing — it only narrows; it never widens".

## 2. Surface table

| Surface | Wiring (file:line) | Behaviour when hidden |
|---|---|---|
| External list: projects, matters, work assignments, service requests and invoices (one helper, `ExternalProjectDataEndpoints.cs:625`) | `ExternalDataService.cs:490-491` (decide), `:672` (list `$select` carries `SelectColumns`) | Row dropped; `Truncated` when the read faulted |
| Download content (6 routes → `DownloadDocumentForParent`, `ExternalProjectDataEndpoints.cs:645`) | `:684` → `IsDocumentReadableUnderAsync` `:1490`, narrowing `:1505` → `ExternalDataService.IsDocumentVisibleAsync`; single read `$select` `ExternalDataService.cs:938` | The existing "not part of this record" refusal, before any SPE pointer resolve or file read |
| Versions (6 routes → `ListDocumentVersionsForParent`, `:2070`) | `:2099` → same as download | Same refusal; no SPE read |
| Preview | None on the external plane: no preview or thumbnail route exists (`MapGet` scan of `Api/ExternalAccess`) | — |
| Documents module fetch | `ExternalModuleDataEndpoints.cs:213` (injects `NarrowDocumentsAsync`), `:342-351` (projects `NarrowingAttributes`), `:377-392` (decide), `:411` (Truncated), `ExternalModuleRegistry.cs:216/225` (`NarrowsDocuments` derived from `RecordEntity`, never configured) | Row dropped; columns it projected stripped; `Truncated` on fault |
| Module single record | Safe: `IsRecordAccessible` never matches a child document id | — |
| Delete own document | A write; out of scope | — |
| Messages | The message routes refuse attachments | — |
| Notifications feed | No documents | — |
| `dataverse.read_query` | `DataverseReadQueryHandler.cs:177/185` (document guard), `:306-316` (ids → `AppOnlyDocumentNarrowing`), `:334-338` (row filter). Nothing is injected into the user's query any more | Row dropped; reason codes logged |
| `dataverse.search_data` | `DataverseSearchDataHandler.cs:309-317` (document hits → `AppOnlyDocumentNarrowing`), `:320-326` (drop) | Hit dropped silently |
| `dataverse.describe` | `DataverseDescribeHandler.cs` guard, then `:439` (`AppOnlyDocumentNarrowing` on the record id) | Same code and message as not-found |

Task 078's assistant rule (related communication only) is removed. `ExternalCallerCommunicationGuard` keeps only the
`IsDocumentTable` gate (and its own sprk_communication-row guard, unchanged). 078's tests now run against the predicate
with the truth seeded in the app-only world (`AppOnlyDocumentWorld`) and the user's read carrying only what the query
selected.

## 3. Component justification (§11)

**`ExternalDocumentNarrowing`** (new static class).
1. *Existing:*
   - `ExternalMultiRootRule` decides by roots;
   - `CommunicationOpenThreadGate` decides a communication (065);
   - `ExternalCallerCommunicationGuard` (078) applied one leg in the assistant only.
2. *Extension:* the root rule's input is root links plus grants, read through the secure-flag and live-parent seams. This
   predicate's input is the document's own columns and its parent chain, read through one app-only OData reader. So it
   sits beside the rule. 078's leg is removed in its favour, and the 065 rule is moved into `CommunicationOpenThreadGate`
   rather than copied.
3. *Cost of doing nothing:* a partner with a grant on a matter downloads the .eml of an internal-only email and its
   attachments, and sees a document a user marked Restricted (ISS-015).

**`ExternalDataService.NarrowDocumentsAsync` / `IsDocumentVisibleAsync`** (new methods in an existing file).
- This is the one external-plane seam: it carries the internal-systemuser exemption and the fail-closed wrapper.
- Without it, each of the three external surfaces would carry its own exemption and fault handling.

**`AppOnlyDocumentNarrowing` delegate + `ExternalDataService.ReadAndNarrowDocumentsAsync` + one scoped registration in
`ExternalAccessModule`** (round 2, decision 2).
1. *Existing:* `ExternalDataService` is the app-only reader the external plane uses; owner rounds 7 and 8 gave the write
   tools narrow-by-construction app-only helpers.
2. *Extension:* the method extends `ExternalDataService`; the delegate is the narrowest dependency a public tool handler
   can take on it (nothing else of the reader is reachable). Injecting `ExternalDataService` itself would hand the tools
   every app-only read.
3. *Cost of doing nothing:* under field security, the tools read a masked link as "no link" and show an internal-only
   email's attachments (the round-1 F-7 fail-open).
- The delegate resolves `ExternalDataService` lazily, so a tool call that never narrows (an internal caller) builds no
  client.

**`CommunicationOpenThreadGate.ReadChunksAsync` / `MaxChunksInFlight`** (round 2, decision 3).
1. *Existing:* every level already read in OR-filter chunks of `ChunkSize`, one after another.
2. *Extension:* one helper in the gate, used by the hop reads, the communication read and the thread read.
   `ReadOpenThreadIdsAsync` is unchanged (other callers).
3. *Cost of doing nothing:* a 5,000-row list makes its 100-chunk levels one request at a time (§4).

**`DocumentNarrowingCost` + `DocumentNarrowingResult.Cost` + `LogNarrowingCost`** (round 2, decision 3). Counts what one
decision read, for the one cost line per request the coordinator asked for. Nothing else carries the counts.

**`DocumentNarrower` delegate.** `ExecuteScopedFetchCoreAsync` is a static pipeline driven by delegates (`executeFetch`,
`readNonSecureRoots`, `readLiveParents`). The delegate is a required parameter, so a caller cannot leave the narrowing
out.

**Optional `ISystemUserIdentityResolver` constructor parameter on `ExternalDataService`.**
- It reuses the existing singleton, so there is no new registration.
- Without it, internal systemusers on the SPA would lose internal-only email that the internal plane shows them.

**`ExternalModuleDescriptor.NarrowsDocuments` / `NarrowingAttributes`.**
- Derived, not configured, so a documents module cannot opt out.
- Without them, the module rows would lack the columns. The SDK omits null attributes, so an unprojected column would
  read as "not set" and open the leg.

**`CommunicationOpenThreadGate.RowQuery` / `ReadVisibleCommunicationIdsAsync` / `UnreadableLink`.** These moved from
078's guard (one copy now); they are not new behaviour.

**Field-security target `DocumentNarrowingLinks` plus `scripts/Set-DocumentNarrowingFieldSecurity.ps1`.**
- This extends task 133's engine (`Set-DocumentPointerFieldSecurity.ps1`); it is not a new mechanism.
- Without it, any user with Write can clear a link through the Web API and un-hide an internal-only email's
  attachments (§6).

**ArchTest `ExternalDocumentNarrowingGuardTests`.** This is new guard surface (§9).

## 4. Query cost

**How "one batched read per hop level per page" is met.** A "batched read" is one OR-filter read by primary key over the
DISTINCT ids of a level: `$filter=sprk_documentid eq a or sprk_documentid eq b ...`. The GET URL bounds one request to
**`ChunkSize` = 50 ids** (`CommunicationOpenThreadGate.ChunkSize`, shared with the 065 rule; about 2.8 KB of filter). So
one level over P distinct ids is ⌈P/50⌉ requests.

- The number of **levels** per request is fixed: at most 3 hop levels + the communications + the threads (+ 1 by-id level
  for the assistant's tools, whose rows come from the user's read). It never grows with the number of rows.
- No read is ever made per row. A parent on the page, or read at an earlier hop, is not read again.
- Round 2: the chunks of ONE level now run **up to `MaxChunksInFlight` = 4 at once**
  (`CommunicationOpenThreadGate.ReadChunksAsync`). Before, they ran one after another. Levels stay sequential: each needs
  the ids the previous one returned.

| Rows | Requests |
|---|---|
| A row that decides on its own columns (own Restricted or unreadable; or no link and no parent) | 0 |
| Each hop level (≤ 3) | ⌈P/50⌉ by-key `sprk_documents` reads over the DISTINCT parents still undecided |
| Communications | ⌈C/50⌉ reads over the distinct communications of the walks that passed, then ⌈T/50⌉ thread-privacy reads over their distinct threads (none when no communication passes the flag test) |
| Assistant tools only | + ⌈N/50⌉ by-id reads of the rows themselves |
| Internal systemuser | 0 |

**Worst case per request of N rows** (requests, and latency rounds with 4 in flight):

    requests = 3·⌈P/50⌉ + ⌈C/50⌉ + ⌈T/50⌉           rounds ≈ 3·⌈P/200⌉ + ⌈C/200⌉ + ⌈T/200⌉

- The external list reads at most 5,000 rows (`MaxCollectionRows` = 25 pages × 200) and narrows them in ONE decision.
- **Round 1's "~200 sequential reads"** was a list of 5,000 rows each under its own communication and thread, with no
  parents: ⌈5000/50⌉ = 100 communication requests + 100 thread requests, one after another. Now: the same 200 requests in
  about 50 rounds.
- **Worst case after decision 1**: 5,000 rows, each with its own link AND its own distinct parent, grandparent and
  great-grandparent, and its own thread: 300 hop requests + 100 + 100 = **500 requests, about 125 rounds** (before
  concurrency: 500 sequential). Typical data is far smaller: an email's attachments share one .eml parent and one
  communication, so P and C are the number of emails, not of attachments.
- A single-document route: ≤ 3 document requests + 1 + 1. Describe: + 1 by-id request.
- Measured by test: `APageIsReadInBatches_NeverOneReadPerRow` (120 rows under 120 distinct parents: 3 document requests,
  1 communication, 1 thread, one hop level); `TheChunksOfOneLevel_AreReadConcurrently_UpToTheCap` (5 chunks: exactly 4 in
  flight); the cost records in `ExternalDocumentNarrowingTests`.

**The cost line.** One line per request, counts only (no ids), from the seam that ran the decision:

    [EXT-NARROWING] cost plane={external|assistant} rows={N} hidden={H} faulted={bool} hopLevels={L}
        documentReads={D} communicationReads={C} threadReads={T}

It is Debug, and Information when the request made more than `NarrowingCostInformationThreshold` = 20 requests. Pinned
by `TheAssistantsNarrowing_LogsOneCostLine_IdsNeverLogged` and `TheExternalPlane_LogsOneCostLinePerRequest`.

**Still large (for the coordinator's follow-up decision).** The 500-request worst case is real if partners hold grants on
records with thousands of deep, unshared document chains. Options, none taken here:
- a larger chunk (the URL allows far more than 50 ids; the chunk is shared with the 065 rule);
- start the communication read for rows whose own link is known while the hop walk runs (fewer rounds, same requests,
  some reads wasted on rows an ancestor later hides);
- reuse the communication rows task 187's live-parent walk already reads on the external list.

## 5. Cache statement

**Uncached.**
- Every request reads the current own value, the ancestors' values, the communication flag and the thread privacy.
- So a Restricted flip, a flag lowered (`CommunicationInternalOnlyFloor.LowerWhenSafeAsync`) or a thread closed applies on
  the next request, with nothing stamped or invalidated.
- No Redis or in-memory entry was added (ADR-009 n/a).

## 6. Model-driven hole result

This was a read-only check of spaarkedev1 on 2026-10-20 (`.uac188/t188_mdhole2.py`, metadata only):

| Column | Secured | Create / Update | On forms | On views | fieldpermissions |
|---|---|---|---|---|---|
| `sprk_relatedcommunication` | no | yes / yes | 0 of 5 | 0 of 11 | 0 |
| `sprk_parentdocument` | no | yes / yes | 0 of 5 | 0 of 11 | 0 |
| `sprk_accesspermission` | no | yes / yes | 1 of 5 (main form) | 0 of 11 | 0 |

**The hole.**
- Neither link is on a form, but both are writable through the Web API by any user with Write on the document.
- Clearing `sprk_relatedcommunication` on an internal-only email's .eml makes it and its attachments visible to
  partners. Clearing `sprk_parentdocument` on an attachment of a Restricted .eml does the same.
- `sprk_accesspermission` is user-editable by design. A user's own Restricted can only narrow, and lowering it back
  restores the root's answer, never more.

**The fix.** Field security on BOTH links, using task 133's profiles. The engine was extended with target
`DocumentNarrowingLinks`, and `scripts/Set-DocumentNarrowingFieldSecurity.ps1` is the named entry point. Both columns stay
in the target (main-session decision 2026-10-20).

**The dry run on dev** (`-WhatIf`-equivalent default mode; nothing written):
- p1: both profiles exist and are in SpaarkeCore — OK.
- p6: `sprk_document` is a root component — OK.
- p2: the reader profile is on all 6 business-unit default teams — OK.
- p3: both BFF app users are explicit writers — OK.
- p5: no field-mapping rule, AI topic-registry row or email update field targets the columns — OK.
- p7: both columns exist — OK.
- p4: none of 5,329 deployed web resources writes either link — OK.
- (a)+(b): WOULD secure each column and grant reader (read) and writer (read/create/update) at once.
- Informational: 16 System Administrator holders, one of them a team. They bypass field security by platform rule.

**`-Apply` is an owner-approved main-session step.**
- Live order: deploy the BFF with task 188 → dry run → owner approval → `-Apply` → `-Verify`.

**Masked reads no longer matter to the predicate (round 2).**
- A secured column reads EMPTY for a user outside the reader profile.
- The predicate never reads as the user any more (§1 "Whose reads"), so a mask cannot open a document. Pinned by the
  must-fire tests in the three AI handler test classes, where the user's read returns the link or the parent as null and
  the app-only world holds an internal-only communication or a Restricted parent.
- (p2) stays as defence in depth: it keeps users and forms reading the links. A business unit created later needs the
  reader-team step re-run, as for every other locked column.

## 7. Writers, re-file and set-regarding (confirmation)

**`sprk_relatedcommunication` writers.** Every writer is the BFF, app-only, with a server-known value:
- `CommunicationService.cs:519/2736/3019`;
- `IncomingCommunicationProcessor.cs:1128/1347`;
- `MessageAttachmentMaterializer.cs:279`.

**`sprk_parentdocument` writer:** `Spaarke.Dataverse/DataverseServiceClientImpl.cs:1023` (the email attachment path).

**What touches neither link:**
- No code path clears either link.
- Re-file, set-regarding and the #2159 relocation change filing lookups only; they touch neither link.
- Compose copies the links app-only, so the copy inherits the narrowing.
- The generic record-update paths have no column guard but are config-driven. (p5) confirms that no field-mapping rule
  targets these columns on dev.

**Pipeline attachments** link to the communication (`sprk_relatedcommunication`), not to the .eml as parent, so the
communication leg covers them (an internal-only or non-Open email hides them). A Restricted .eml does not hide them
through the walk (finding F-3); the coordinator carries that to T8.

## 8. Mutation table (negative controls)

Each mutant was applied to the source in turn, the unit suite was run (filter: the narrowing, route, handler and
freeze tests, 335 cases), and the source was restored. The 9 sources were checked against their pre-mutation SHA-1s after
every run. **35 of 35 killed.** Runner: `.uac188/t188_mutate.py` (untracked).

| Id | Mutation | Failing tests | Killed by (representative) |
|---|---|---|---|
| M1 | own Restricted leg removed | 16 | `ASystemUserFlaggedExternal_IsNarrowed` |
| M2 | unknown own value read as Standard | 2 | `OwnValueThatIsNotAKnownLevel_IsHidden_FailClosed` |
| M3 | communication (065) leg removed | 24 | `DocumentsModuleFetch_HidesOwnAndAncestorRestricted_...` |
| M4 | ancestor Restricted leg removed | 18 | `List_HidesTheDocument(case: ...)` |
| M5 | parent walk removed | 39 | `DocumentsModuleFetch_WhenTheNarrowingReadFails_HidesTheRows_...` |
| M6 | hop bound raised to 4 | 4 | `ADocumentWithItsOwnVisibleLink_AndAChainThatNeedsAFourthHop_IsHidden` |
| M7 | cycle check removed | 3 | `ACycle_IsHidden`, `ADocumentWithItsOwnVisibleLink_InACycle_IsHidden` |
| M8 | unreadable link check removed | 1 | `ACommunicationLinkThatIsNotAGuid_IsHidden_WithoutARead` |
| M9 | parent not found read as visible | 1 | `AParentThatDoesNotComeBack_IsHidden` |
| M10 | unreadable parent link read as visible | 1 | `AParentLinkThatIsNotAGuid_IsHidden_WithoutARead` |
| M11 | read fault fails open | 4 | `List_WhenTheNarrowingReadFails_HidesThePage_AndSaysTruncated` |
| A1 | ancestor walk stops at the first link (round-1 shape) | 10 | `ADocumentWithItsOwnVisibleLink_UnderARestrictedParentEml_IsHidden` |
| A2 | a cycle under an own link left to the communication leg | 1 | `ADocumentWithItsOwnVisibleLink_InACycle_IsHidden` |
| A3 | a chain past the bound under an own link left to the communication leg | 3 | route case `own-visible-link-four-hop-chain` |
| A4 | the LAST link decides the communication (an ancestor overrides) | 4 | `ADocumentWithItsOwnInternalOnlyLink_UnderAParentLinkedToAVisibleCommunication_IsHidden`, route case `own-visible-link-parent-eml-internal-only` |
| A5 | a hidden walk still sends its communication to the 065 read | 9 | `ADocumentWithItsOwnVisibleLink_UnderARestrictedParentEml_IsHidden` (its read counts) |
| O1 | read_query narrows over the user (OBO) reader | 9 | `ExecuteChatAsync_ExternalCallerOnDocument_FieldSecurityMasksTheLinkForTheUser_AppOnlyReadsAnInternalOnlyCommunication_Hidden` (and the ArchTest, below) |
| O2 | search_data narrows over the user (OBO) reader | 4 | `ExecuteChatAsync_ExternalCallerDocumentHit_TheUsersReadWouldShowNoLink_AppOnlyReadsAnInternalOnlyCommunication_Dropped` (and the ArchTest) |
| O3 | describe narrows over the user (OBO) reader | 8 | `ExecuteChatAsync_ExternalCallerDocument_Task188_ParentMaskedForTheUser_AppOnlyReadsARestrictedParent_AnswersLikeNotFound` (and the ArchTest) |
| O4 | the registered seam does not narrow | 1 | `TheAssistantsNarrowing_IsRegistered_OverTheAppOnlyReader` |
| C1 | assistant cost line removed | 1 | `TheAssistantsNarrowing_LogsOneCostLine_IdsNeverLogged` |
| C2 | external cost line removed | 1 | `TheExternalPlane_LogsOneCostLinePerRequest` |
| P1 | chunks of a level read one after another | 1 | `TheChunksOfOneLevel_AreReadConcurrently_UpToTheCap` |
| P2 | no cap on chunks in flight | 1 | `TheChunksOfOneLevel_AreReadConcurrently_UpToTheCap` |
| S1 | list surface unwired | 2 | `List_HidesTheDocument(case: ...)` |
| S2 | list select drops the predicate columns | 2 | `List_ReadsTheAccessPermission_AndTheParentLinks_InTheListQuery` |
| S3 | single-document select drops the predicate columns | 1 | `SingleDocumentRoute_RefusesTheDocument_AsNotUnderTheParent_AndReadsNothingFromSpe` |
| S4 | download/versions unwired | 2 | `SingleDocumentRoute_WhenTheNarrowingReadFails_Refuses_AndReadsNothingFromSpe` |
| S5 | module fetch unwired | 2 | `DocumentsModuleFetch_HidesOwnAndAncestorRestricted_...` |
| S6 | module projection drops the narrowing attributes | 1 | `DocumentsModuleFetch_HidesOwnAndAncestorRestricted_...` |
| S7 | read_query unwired | 6 | `ExecuteChatAsync_ExternalCallerOnDocumentWithUnreadableRelatedCommunicationLink_Hidden` |
| S8 | search_data unwired | 4 | `ExecuteChatAsync_ExternalCallerDocumentHitWithUnreadableRelatedCommunicationLink_Dropped` |
| S9 | describe unwired | 5 | `ExecuteChatAsync_ExternalCallerDocumentHiddenByRelatedCommunication_AnswersLikeNotFound` |
| E1 | internal exemption removed | 1 | `AnInternalSystemUser_IsNotNarrowed_AndNothingIsRead` |
| E2 | every systemuser treated as internal | 1 | `ASystemUserFlaggedExternal_IsNarrowed` |

P2 survived the first run: the concurrency test released its reads as soon as the cap's worth had started, so the
released reads could finish before a 5th started. The test now holds every read until it has seen the cap's worth start
and waited 300 ms more (the idiom of `AccessibleRecordSetServiceTests`' NFR-02 cap test), so a missing cap shows as a 5th
read in flight. P1 and P2 were re-run against the fixed test; both were killed.

**Guard proof (rule 2, ArchTests).** O1, O2 and O3 were each applied and the ArchTests project was rebuilt. Each one
fails `TheCoreDecision_IsCalledOnlyFromTheAppOnlySeams` (1 failed, 3 passed in `ExternalDocumentNarrowingGuardTests`).
Runner: `.uac188/t188_guardproof.py`. The sources were checked against their SHA-1s afterwards.

## 9. ArchTest guard

`tests/Spaarke.ArchTests/ExternalDocumentNarrowingGuardTests.cs` uses IL, via `IlCallScan`.

**Rule 1 — every surface reaches the predicate.**

What counts as a surface. A BFF source method that calls a surface primitive:
- `BuildDocumentListUrl`;
- `GetDocumentParentAndNameAsync`;
- `DocumentScopeRead.IsReadableUnder`;
- `ExternalModuleDescriptor.ScopeRows`;
- `ExternalCallerCommunicationGuard.IsDocumentTable`.

What counts as reaching the narrowing. The method must reach, transitively over BFF methods, one of:
- `ExternalDocumentNarrowing.DecideAsync`, `DecideOrFailClosedAsync` or `ReadAndDecideAsync`;
- `ExternalDataService.NarrowDocumentsAsync`, `IsDocumentVisibleAsync` or `ReadAndNarrowDocumentsAsync`;
- `DocumentNarrower.Invoke` or `AppOnlyDocumentNarrowing.Invoke`.

Lambdas, local functions and state machines count for the method they were written in. A comment, a string or a `nameof`
does not count.

Ratchet baseline. Two listed non-document callers, each with its reason:
- `ExternalMessageEndpoints.EvaluateRecordGateAsync`: it scopes the message's regarding record;
- `ExternalNotificationFeedEndpoints.GetFeedCoreAsync`: service requests.

An entry goes stale, and fails the guard, when it stops using a primitive or starts narrowing. Non-vacuity: seven wired
surfaces are pinned as both "seen reading" and "seen reaching". Controls (`Detector_MustFireAndMustNotFire`): must fire
for a mention-only read; must not fire for a narrowing inside a lambda, a narrowing through a helper, or a type with no
document read.

**Rule 2 — app-only reads (round 2, decision 2).** The core decision (`DecideAsync`, `DecideOrFailClosedAsync`,
`ReadAndDecideAsync`) takes its reader as an argument. So it may be referenced only from `ExternalDocumentNarrowing` and
`ExternalDataService`, whose only reader is app-only (`TheCoreDecision_IsCalledOnlyFromTheAppOnlySeams`).
- Non-vacuity: `ExternalDataService::NarrowDocumentsAsync` and `::ReadAndNarrowDocumentsAsync` are seen calling it.
- Controls (`CoreDecisionDetector_MustFireAndMustNotFire`): a fixture deciding over a reader of its own fires; one using
  the injected `AppOnlyDocumentNarrowing`, and one using `NarrowDocumentsAsync`, do not.

**Limits (K).**
- Reach is per method by name. It says the method can get to the narrowing, not that every path does. The surface tests
  pin the paths.
- A new hand-written `sprk_documents` query that uses no primitive is not seen. The primitive list is the contract a
  reviewer extends.
- Rule 2 trusts that `ExternalDataService` hands the predicate its own app-only reader; a second reader added inside
  `ExternalDataService` would not be seen. It has one reader today.
- A non-document module whose FetchXML link-entities `sprk_document` columns is not narrowed (finding F-6).

## 10. Gates

Round 2, run on the final sources (after the mutants, with the binaries rebuilt from the restored files):

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | succeeded, 0 warnings |
| Unit tests, filtered (`Services.Ai.Handlers` \| `ExternalAccess` \| `UnifiedAccessControl` \| `Communication`) | 6614 passed, 2 skipped, 0 failed |
| `tests/Spaarke.ArchTests` (full project) | 1212 passed, 0 failed |
| Mutants (§8) | 35 of 35 killed; guard proof O1–O3 fails rule 2 |
| Publish size (§10 bullet 3) | branch `62b767a4c3` zip 39,328,681 B vs master `4f5f22694c` (fresh build, the branch's merge base) zip 39,314,285 B: **+14,396 B (+0.01 MB)**; raw +42,848 B; 4 PDBs on both sides. Total 39.3 MB, under the 55 MB review line |
| `dotnet list package --vulnerable` | no vulnerable packages; no package changes |
| `git merge-tree` against `origin/master` `44063b3473` | clean. Master moved by 27 files since the base; one BFF file (`GateDecisionRecordSharing.cs`, +18/−9), which is not on this path |

Not run, by the brief: the full unfiltered unit suite, any deploy, and field security `-Apply`.

**Expected conflict.** Task 189 edits the I-16 row of `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. This round leaves I-16
alone (round 1 corrected it), so the conflict is a line conflict for whichever PR merges second.

## 11. Review findings (code-review + adr-check, self-run)

**ADR compliance.**
- ADR-002 WP-6 (fail closed): yes.
- ADR-009: no cache.
- ADR-010: one new scoped registration (the `AppOnlyDocumentNarrowing` delegate, §3); no new interface.
- ADR-013: the AI handlers consume an Infrastructure delegate. No CRUD→AI dependency.
- ADR-015: reason codes and counts only; the cost line carries no ids (pinned). The document id is logged as the existing
  download and versions lines already do.
- ADR-028: no auth mechanism changes. The tools' app-only read is the existing external-plane identity, used for a
  hide-only decision (see the rule amendment below).
- ADR-052: no background work.
- ADR-038: the new tests sit beside their siblings in `tests/unit/...` (`ExternalMultiRootRuleTests`,
  `ExternalLiveParentRuleTests`, `CommunicationOpenThreadGateTests`). The coordinator confirmed the placement and that
  /test-diet keeps them. No `Mock<HttpMessageHandler>`: the fakes are the existing `FakeDataverse` handler, plus
  `Mock<IDataverseUserClient>` at the module boundary.

**🔔 Rule amendment needing owner sign-off (CLAUDE.md §6.5 path B).** `IDataverseUserClient`'s remark states the spec MUST
rule "User-OBO ONLY ... AI tool handlers MUST NOT reach Dataverse through any other client". Decision 2 makes the three
read tools take ONE app-only dependency, `AppOnlyDocumentNarrowing`. It is built the way owner rounds 7 and 8 amended the
rule for the write tools (narrow by construction: a delegate that returns only a hide decision; no app-only data reaches
the model). The amendment text is in each handler's class remarks, marked "owner sign-off pending on the PR". The PR
cites it; code review must approve it explicitly.

**Findings for the main session.**
- **F-1 (resolved, decision 1).** Restricted ancestors above a linked document hide it. The interpretation test is flipped
  into the must-fire `ADocumentWithItsOwnVisibleLink_UnderARestrictedParentEml_IsHidden`.
- **F-2 (task 189's file, found in passing).** The remark at `ChildAccessPermissionReconciler.cs:95-96` reads "display
  only (no access decision reads it) ...". After 188 that is false for documents. 189 fixes its own remark (decision 6);
  not edited here.
- **F-3 (carried to T8 by the coordinator).** Communication-pipeline attachments link to the communication, not to the
  .eml as parent, so the communication leg covers them; "a Restricted .eml hides its attachments" holds only for
  documents whose `sprk_parentdocument` is the .eml (the email attachment path).
- **F-4 (confirmed intended).** A document under a Restricted root stores Restricted (I-16), so it is hidden from external
  systemusers who reach it through another root. Contacts never reach Restricted roots, so they lose nothing they had.
- **F-5 (addressed, decision 3).** Chunks of a level now run 4 at once; one cost line per request; the worst case is in
  §4 for the coordinator's follow-up decision.
- **F-6 (K).** A non-document module with a FetchXML link-entity to `sprk_document` columns is not narrowed. No such
  module exists today: modules are rows, and the documents module is `RecordEntity = sprk_document`.
- **F-7 (resolved, decision 2).** The predicate reads app-only on every surface; a masked link can no longer fail open.
  (p2) stays as defence in depth.
- **F-8 (confirmed intended).** Internal systemusers are exempt on the external plane (the 065 rule's posture).

## 12. ADR-034 / auth constraint text (for the main session to apply to `.claude/`)

> A child's Access Permission may narrow partner visibility (Restricted hides it); it never widens it. The external
> document narrowing's own reads (links, parent chain, Access Permission, communication flag and thread) always run under
> the BFF's app-only identity, on every surface; the assistant's tools reach it only through `AppOnlyDocumentNarrowing`.
> Enforced by: task 188's tests (`ExternalDocumentNarrowingTests`, `ExternalDocumentNarrowingRouteTests`, the
> `dataverse.*` handler tests with a masked user read) and `ExternalDocumentNarrowingGuardTests` (ArchTest: every surface
> reaches the predicate; its core decision is called only from the app-only seams).

## 13. Not done here (by design)

- **R3 deploy and the server-side live gate.** The external list must answer 200 with no new 5xx, and a document linked
  to an internal-only communication must be absent. Main session.
- **Field security `-Apply` / `-Verify`.** Owner-approved, after the deploy.
- **`.claude/` edits** (the ADR-034 / auth constraint text, §12). Main session.
- **Merge order.** Task 189 edits the same I-16 row; a line conflict is expected and the second to merge resolves it.
- The task exceeded the ~100-step size guideline, through the mutation runs, the guard and two rounds of decisions.

## 14. Round 3 slice A — the AI retrieval trim (F1-A, security pass 1)

Commit `f1263870c0`. Slices B (content routes) and C (`sprk_analysis`, the ArchTest allow-list) are not touched here.

**The defect.** `RetrievalAccessTrim.TrimByRecordAsync` checked only the caller's own Dataverse Read. Every AI retrieval
path depends on it: `DocumentSearchHandler`, `KnowledgeRetrievalHandler`, `SemanticSearchToolHandler`, `/api/ai/search`,
Insights, `IndexRetrieveNode` and the playbook chat context. So a systemuser flagged `sprk_isexternal`, with a share on
matter M, got the chunks of the .eml of an internal-only email filed to M.

**The fix** (main-session decision; `Services/Ai/PublicContracts/RetrievalAccessTrim.cs`):
1. After the Read check, and only when at least one DOCUMENT key is readable, the trim asks whether the caller is a
   resolved internal systemuser (`ExternalCallerCommunicationGuard.IsExternalCallerAsync`, reused: flagged external,
   unresolved, or no principal is external). For any other caller, every document key passes through THE narrowing
   (`AppOnlyDocumentNarrowing`, one call per page, app-only, hide-only).
2. **Document-id keys** are narrowed directly.
3. **Drive-item keys** (`sprk_driveitemid`, `IndexRetrieveNode` evidence refs): the caller's own read now selects
   `sprk_driveitemid,sprk_documentid`. The key passes only when at least one row on the item that the caller's read
   returned is shown. A returned row with no usable `sprk_documentid` cannot be shown (fail closed).
4. **Parent-record keys** (matter, project, invoice, work assignment) are unchanged and trigger no lookup.
5. **Faults.** A failed externality lookup (the guard throws on `lookup-failed` or on an `IsExternalAsync` fault) counts as
   EXTERNAL: the narrowing applies, so an internal user loses only hidden documents. A failed narrowing withholds every
   document key; parent-record keys stay, and the outcome stays `Evaluated`. Logs: counts and reason codes.
6. Internal callers: no narrowing call and no app-only read. Unattended runs with no caller: unchanged (`NoVerifiedCaller`
   before any of this).

| | Before | After |
|---|---|---|
| External caller with Read on an internal-only .eml, an own-Restricted document or one under a Restricted parent | Chunk returned | Withheld (both key kinds) |
| External caller, visible document | Returned | Returned |
| Internal user, any document they can Read | Returned | Returned; no narrowing, no app-only read |
| Externality lookup fails | n/a | Narrowing applies; visible documents still returned |
| Narrowing read fails, external caller | n/a | Document keys withheld; parent records returned |

**Component justification (§11)** for the three new constructor dependencies (`ICallerSystemUserResolver`,
`ISystemUserIdentityResolver`, `AppOnlyDocumentNarrowing`):
1. *Existing:* the externality rule (`ExternalCallerCommunicationGuard.IsExternalCallerAsync`) and the narrowing seam are
   reused as they are; nothing is copied.
2. *Extension:* the trim is the ONE seam every AI retrieval path applies, so narrowing it there covers every consumer.
3. *Cost of doing nothing:* an external systemuser's chat, Insights or playbook retrieval returns an internal-only email's
   text or a Restricted document (F1-A).
- All three are registered unconditionally (`CommunicationModule`, `NotificationsModule`, `AddExternalAccess`);
  `GateCombinationConstructibilityTests` resolves them under every gate combination. Two new internal helpers on
  `RetrievalRecordKey` (`IsDocumentIdKey`, `IsDriveItemKey`) name the two document key kinds; `DocumentIdField` is now
  internal for the drive-item `$select`.

**Rule amendment.** The trim now takes `AppOnlyDocumentNarrowing`, the same one app-only dependency as the round-2
amendment of the `dataverse.*` tools (§11, CLAUDE.md §6.5 path B, owner sign-off pending on the PR). Stated in the class
remarks.

**Tests.**
- `RetrievalAccessTrimTests` (new cases over the real predicate behind the real app-only seam, `AppOnlyDocumentWorld`):
  - must fire: `AnExternalCaller_WithReadOnAHiddenDocument_GetsNoChunkOfIt` (internal-only communication, own Restricted,
    Restricted parent × document-id and drive-item key); `AFailedExternalityLookup_AppliesTheNarrowing_ItDoesNotRefuseEverything`
    (resolver `lookup-failed` and a throwing `IsExternalAsync` × both key kinds); `AFailedNarrowing_WithholdsTheDocumentKeys_ParentRecordsStay`;
    `ADriveItem_WhoseOnlyVisibleRowTheCallerCannotRead_IsWithheld`; `ADriveItemRowWithoutADocumentId_IsWithheld_ForAnExternalCaller`.
  - must not fire: `AnInternalUser_ResultsAreUnchanged_AndNothingIsNarrowed`, `AnExternalCaller_OnAVisibleDocument_KeepsIt`
    (both key kinds); `ADriveItem_WithOneHiddenAndOneVisibleReadableRow_Passes`;
    `AnExternalCaller_ParentRecordKeys_AreNotNarrowed_AndNothingIsLookedUp`.
- `Issue1511_AiRetrievalAccessTrimTests` (handlers over the real trim): `InternalOnlyEmailDocument_TheCallerCanRead_ReachesDocumentSearch_OnlyForAnInternalCaller`
  (external: withheld; internal: unchanged) and `ExternalCaller_ObservationQuotingAnInternalOnlyEmailItem_DoesNotReachIndexRetrieve`
  (drive-item key).
- `ExternalDocumentNarrowingGuardTests`: one line pins that `RetrievalAccessTrim::TrimByRecordAsync` reaches the narrowing.
- Constructor sites updated to an internal caller: `InsightsOrchestratorTests` (strict mocks; nothing readable),
  `SemanticSearchAuthorizationTests` (Spe.Integration.Tests), `ClassifierRecallLiveHarness` (strict; no caller on that
  path). The shared fake `ReadableDocumentsUserClient` accepts a two-column `$select` and models document rows per drive
  item (`AllowDocumentOnDriveItem`).

**Before → after.** The original trim, given only the new constructor signature so the tests compile: 19 failed, 95
passed (of 114 in the trim, #1511 and Insights classes). After: 114 passed.

**Mutants** (runner `.uac188/t188a_mutate.py`, untracked; the source SHA-1 was checked after every run):

| Id | Mutation | Failing tests | Killed by (representative) |
|---|---|---|---|
| M1 | narrowing removed | 18 (+ the ArchTest guard: 1 of 4 in the class fails) | `AnExternalCaller_WithReadOnAHiddenDocument_GetsNoChunkOfIt` |
| M2 | drive-item keys skipped | 10 | `AnExternalCaller_WithReadOnAHiddenDocument_GetsNoChunkOfIt(…DriveItem)`, `ExternalCaller_ObservationQuotingAnInternalOnlyEmailItem_DoesNotReachIndexRetrieve` |
| M3 | a lookup fault counts as internal | 4 | `AFailedExternalityLookup_AppliesTheNarrowing_ItDoesNotRefuseEverything` |
| M4 | a narrowing fault counts as visible | 2 | `AFailedNarrowing_WithholdsTheDocumentKeys_ParentRecordsStay` |

**Gates** (on `f1263870c0`):

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental` | succeeded, 0 warnings |
| Unit tests, filtered (`Services.Ai` \| `Regression.Ai` \| `Seam.Ai` \| `ExternalAccess` \| `UnifiedAccessControl` \| `Communication` \| `Insights` \| `SemanticSearch`, `Category!=Live`) | 11382 passed, 3 skipped (pre-existing), 0 failed |
| `Spe.Integration.Tests` `SemanticSearchAuthorizationTests` | 49 passed |
| `tests/Spaarke.ArchTests` (full project) | 1212 passed, 0 failed |
| Test projects that compile the changed files (unit, ArchTests, Spe.Integration.Tests, Sprk.Bff.Api.IntegrationTests) | build, 0 warnings |
| Publish size (canonical: `dotnet publish -c Release`, framework-dependent linux-x64, `Compress-Archive -CompressionLevel Optimal`, PDBs included, 4 PDBs each side, fresh worktrees) | branch `f1263870c0` 39,331,381 B vs master `4f5f22694c` (the merge base) 39,314,223 B: **+17,158 B (+0.02 MB)**; raw +49,516 B. Slice A alone (vs `c3da0a5d0e`, 39,328,661 B): +2,720 B. Total 39.3 MB |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages; no package changes |
| `git merge-tree` against `origin/master` `44063b3473` | clean |

**Known limits (K).**
- A narrowing fault for an external caller withholds the document keys with outcome `Evaluated`, so the tool result does
  not say "withheld" (the brief's decision: parent-record keys must survive, which `CheckFailed` would not allow). The
  warning log carries it.
- Cost: every trim with a readable document key now makes one externality lookup (both resolvers cache); an external
  caller adds one narrowing call per page (§4: + ⌈N/50⌉ by-id app-only reads, then the hop and communication levels).
- Not run: the full unfiltered unit suite and the `Category=Live` harnesses (by the brief).

## 15. Round 3 slice B — the internal plane's document routes (security pass 1)

Slices A (§14) and C (`sprk_analysis`, read_query, search_data) are not touched here. Coordinator decisions applied: Option A for the test hosts, the `internal-routes` plane, the list-cost limit (K-B1).

**Also reached (not in the brief's 26).** `ComposeDocumentAuthorizationFilter` (6 compose routes:
`ComposeAnnotationEndpoints.cs:37/61`, `ComposeDocumentEndpoints.cs:40`, `ComposeSaveEndpoints.cs:33`,
`ComposeSyncEndpoints.cs:73`, `ComposeTemplateEndpoints.cs:40`) and `OfficeVersionSaveAuthorizationFilter`
(`Office/OfficeEndpoints.cs:194`, the Office version save) both delegate to `DocumentAuthorizationFilter`, so they are
narrowed too. `DocumentsModule.cs:90` registers a scoped `DocumentAuthorizationFilter("read")` (no route found using it).

**The defect.** The internal plane's document routes decided a caller only by the root grants (`AuthorizationService`, the
caller's own Dataverse rights). A systemuser flagged `sprk_isexternal` with a share on matter M could read, download,
preview, version, edit, check out, delete or list the .eml of an internal-only email filed to M, its attachments, or a
document a user marked Restricted, through any document-id route, the bulk download and the two container list routes.

**The fix.** One static helper, `Api/Filters/InternalPlaneDocumentNarrowing.ForCallerAsync`, used by all three surfaces:
1. It runs only AFTER the root grants allowed; each route's own authorization is unchanged (no endpoint loses or changes
   its auth; no new status code, policy or registration).
2. A caller who is not a resolved INTERNAL systemuser (the rule of `ExternalCallerCommunicationGuard.IsExternalCallerAsync`:
   flagged external, unresolved, or no principal) is narrowed by ONE `InternalRouteDocumentNarrowing` call over every
   document id the request names. That delegate is the same app-only read and decision as `AppOnlyDocumentNarrowing`
   (`ExternalDataService.ReadAndNarrowDocumentsAsync`); the D7 rule is not copied.
   - The externality rule is applied over the guard's own two resolvers (`ICallerSystemUserResolver`,
     `ISystemUserIdentityResolver`) rather than by calling the guard: non-AI code may not depend on
     `Services.Ai.Handlers.Dataverse` (ArchTest D-66), and moving the guard would touch slice C's files. Under this
     helper's fault policy the two give the same answer (pinned by mutants M3a, M3b, M6, M7).
3. Internal systemusers: no narrowing call and no app-only read.
4. Faults (the slice A rule): a failed externality lookup counts as EXTERNAL (the narrowing applies; it never refuses
   every request and never treats the caller as internal). A failed narrowing hides every document of the request.
5. Logs: counts and reason codes (`[DOC-NARROWING] {surface}: {hidden} of {count} ...`), never ids. The one cost line
   per request is `ReadAndNarrowDocumentsAsync`'s, with the plane now passed in: `plane=internal-routes` for these routes
   (`ExternalDataService.InternalRoutesPlane`); `plane=assistant` is unchanged for `AppOnlyDocumentNarrowing`.
6. A route id that is not a GUID is not narrowed. It names no document (`sprk_documentid` is a GUID), so the handler's
   own validation answers as before (`/identity`'s 400, pinned by `DocumentIdentityByIdContractTests`).

| Surface | Behaviour when hidden (or the narrowing faults) |
|---|---|
| `DocumentAuthorizationFilter.InvokeAsync` (all 26 routes below) | The filter's existing deny: `ProblemDetailsHelper.Forbidden("sdap.access.deny.insufficient_rights")`, the 403 `OperationAccessRule` gives a caller without the operation's right — indistinguishable from "no right". The handler never runs, so nothing is streamed or written |
| `BulkDownloadAuthorizationFilter.InvokeAsync` | **Dropped**: the id is removed from the authorized set, exactly as an unauthorized id is. The handler reports it with the same manifest reason as an unauthorized or missing document and never reads its bytes. A fault drops every document (all gone → the handler's existing 404) |
| `DataverseDocumentsEndpoints.ListDocumentsAsync` (`GET /api/v1/documents?containerId=`) and `ListContainerDocumentsAsync` (`GET /api/v1/containers/{containerId}/documents`) | Row removed from the WHOLE list before paging, so `items`, `totalCount` and `hasNextPage` see visible rows only (no total that includes them, no next page that skips or promises them). A fault removes every row (empty page, `totalCount` 0) |

The two list handlers were lambdas inside `MapDataverseDocumentsEndpoints`; they are extracted verbatim into the named
methods above so the ArchTest can see each route on its own (one shared lambda key would let one route stop narrowing
unseen). Their binding, filters and auth are unchanged.

**Route table** (every route carrying `AddDocumentAuthorizationFilter`; 26; filter file:line on this head). R3: each one's
behaviour changes for a caller who is not an internal systemuser.

| # | Route | Operation | Filter at |
|---|---|---|---|
| 1 | `GET /api/v1/documents/{id}` | read | `DataverseDocumentsEndpoints.cs:93` |
| 2 | `PUT /api/v1/documents/{id}` | write | `DataverseDocumentsEndpoints.cs:280` |
| 3 | `POST /api/v1/documents/{id}/file` | write | `DataverseDocumentsEndpoints.cs:306` |
| 4 | `DELETE /api/v1/documents/{id}` | delete | `DataverseDocumentsEndpoints.cs:377` |
| 5 | `GET /api/v1/documents/{id}/download` | read | `DataverseDocumentsEndpoints.cs:545` |
| 6 | `POST /api/documents/{documentId}/checkout` | write | `DocumentOperationsEndpoints.cs:43` |
| 7 | `POST /api/documents/{documentId}/checkin` | write | `DocumentOperationsEndpoints.cs:55` |
| 8 | `POST /api/documents/{documentId}/discard` | write | `DocumentOperationsEndpoints.cs:71` |
| 9 | `DELETE /api/documents/{documentId}` | delete | `DocumentOperationsEndpoints.cs:88` |
| 10 | `GET /api/documents/{documentId}/checkout-status` | read | `DocumentOperationsEndpoints.cs:101` |
| 11 | `POST /api/documents/{documentId}/analyze` | write | `DocumentOperationsEndpoints.cs:118` |
| 12 | `GET /api/documents/{documentId}/versions` | read | `DocumentVersionEndpoints.cs:146` |
| 13 | `GET /api/documents/{documentId}/versions/{versionId}/content` | read | `DocumentVersionEndpoints.cs:211` |
| 14 | `GET /api/documents/{documentId}/preview-url` | read | `FileAccessEndpoints.cs:66` |
| 15 | `GET /api/documents/{documentId}/preview` | read | `FileAccessEndpoints.cs:78` |
| 16 | `GET /api/documents/{documentId}/content` | read | `FileAccessEndpoints.cs:92` |
| 17 | `GET /api/documents/{documentId}/office` | read | `FileAccessEndpoints.cs:103` |
| 18 | `GET /api/documents/{documentId}/open-links` | read | `FileAccessEndpoints.cs:112` |
| 19 | `GET /api/documents/{documentId}/view-url` | read | `FileAccessEndpoints.cs:123` |
| 20 | `GET /api/documents/{documentId}/download` | read | `FileAccessEndpoints.cs:148` |
| 21 | `GET /api/documents/{documentId}/eml-render` | read | `FileAccessEndpoints.cs:168` |
| 22 | `POST /api/documents/resolve-identity` (id from `DocumentUrlIdentityFilter`) | read | `FileAccessEndpoints.cs:197` |
| 23 | `GET /api/documents/{documentId}/identity` | read | `FileAccessEndpoints.cs:219` |
| 24 | `POST /api/documents/resolve-email-identity` (id from `DocumentEmailIdentityFilter`) | read | `FileAccessEndpoints.cs:244` |
| 25 | `POST /api/compose/documents/{documentId}/refresh-profile` | write | `ComposeDocumentEndpoints.cs:66` |
| 26 | `POST /api/office/documents/{documentId}/generate-profile` | write | `Office/OfficeEndpoints.cs:1603` |
| — | `POST /api/documents/bulk-download` | read × N | `DocumentsBulkEndpoints.cs:65` (`BulkDownloadAuthorizationFilter`) |
| — | `GET /api/v1/documents?containerId=` | list | `DataverseDocumentsEndpoints.cs:549` → `ListDocumentsAsync` |
| — | `GET /api/v1/containers/{containerId}/documents` | list | `DataverseDocumentsEndpoints.cs:576` → `ListContainerDocumentsAsync` |

The two list routes were at `:550` and `:646` on `7bba318b10` (confirmed); they are now the method-group registrations
above. Routes 22 and 24 resolve the id in an earlier filter and write it into the route values; the narrowing decides on
that id like the rest.

| | Before | After |
|---|---|---|
| External caller (flagged, unresolved or failed lookup) with the right, on a hidden document, any of the 26 routes | Served / written | 403 `insufficient_rights`, handler not run |
| Same, bulk download | Zipped | Dropped (manifest: same reason as unauthorized) |
| Same, container lists | Listed and counted | Removed before paging; not counted |
| External caller, visible document | Served | Served |
| Internal systemuser | Served | Served; no narrowing call, no app-only read |
| Narrowing fault, external caller | n/a | Single route 403; bulk and lists drop the documents |
| No right (root grants) | 403 | 403, before any narrowing |

**Component justification (§11), slice B.**
- `InternalPlaneDocumentNarrowing` (static helper, `Api/Filters`):
  1. *Existing:* the externality rule and the app-only narrowing are reused.
  2. *Extension:* the surfaces are an endpoint filter, a bulk filter and two route handlers with no common service, so one
     helper holds the fault policy once instead of four times.
  3. *Cost of doing nothing:* an external systemuser reads, edits or lists internal-only email and Restricted documents
     through the internal plane.
- `InternalRouteDocumentNarrowing` delegate + one scoped registration in `ExternalAccessModule` (coordinator condition 4):
  1. *Existing:* `AppOnlyDocumentNarrowing` over the same reader.
  2. *Extension:* passing a plane through `AppOnlyDocumentNarrowing` would change its signature and every lambda built for
     it, including slice A's and slice C's surfaces and their tests. A sibling delegate passes `plane=internal-routes`, and
     it lets a test host state these routes' narrowing without touching the assistant's.
     `ReadAndNarrowDocumentsAsync` gains an optional `plane` parameter (default `assistant`).
  3. *Cost of doing nothing:* the cost line for these routes says `plane=assistant`.
- `CallerDocumentVisibility` (the helper's answer type). No new interface and no package.

**Query cost (slice B).** One narrowing call per request, for external callers only (internal systemusers: 0 reads).
Chunks of `ChunkSize` = 50 ids, `MaxChunksInFlight` = 4 (§4).

| Surface | Worst case per request |
|---|---|
| Single-document route | 1 by-id + ≤ 3 hop + 1 communication + 1 thread = **≤ 6 requests** |
| Bulk download (≤ 500 ids) | ⌈500/50⌉ = 10 by-id + ≤ 30 hop + ≤ 10 + ≤ 10 = **≤ 60 requests, about 15 rounds** |
| Container lists (N = every document in the container) | ⌈N/50⌉ by-id (⌈N/200⌉ rounds) + ≤ 3·⌈P/50⌉ + ⌈C/50⌉ + ⌈T/50⌉ |

**K-B1 (known limit, coordinator condition 5).** The list routes narrow the WHOLE container list, because paging must not
count or skip hidden rows. So their by-id reads are ⌈N/50⌉ with N unbounded. The list already reads every page
(`GetDocumentsByContainerAsync` → `RetrieveAllPagesAsync`). Both list routes are also unreachable on real data today (the
`Guid.TryParse` container-id type bug, task 078 §4).

**Test hosts (coordinator decision: Option A).** About 85 existing tests, in hosts whose principal is not a resolved
internal systemuser (a non-GUID `oid`, or no systemuser modelled), hit the narrowing, whose app-only read cannot succeed
offline. Those hosts are about other features. ONE shared double,
`tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/AllVisibleDocumentNarrowing.cs`, replaces `InternalRouteDocumentNarrowing`
only (the assistant's `AppOnlyDocumentNarrowing` is untouched). Each host registers it in one place, with a one-line pointer
to `DocumentRouteExternalNarrowingTests`:
- `CustomWebAppFactory` (and `DocumentIdentityTestWebAppFactory`);
- `WorkspaceTestFixture` (and the document destroy and version fixtures);
- `OfficeTestWebAppFactory` (and its version-save and generate-profile factories);
- `ComposeCreateOnSaveFixture`;
- `ComposeFidelitySeamFixture` (and its seam fixtures);
- `DocumentFileAttachContractTests.AttachHost`;
- the two filter unit tests that build a bare `DefaultHttpContext` (`CallerObjectIdResolutionTests`,
  `ComposeDocumentAuthorizationFilterTests`).

`DocumentRouteNarrowingTestFixture` overrides the double with the real predicate.

**Production registers the real seam** (condition 2). `ExternalDocumentNarrowingRouteTests.TheInternalRoutesNarrowing_IsRegistered_OverTheAppOnlyReader`
builds `AddExternalAccess`, resolves `InternalRouteDocumentNarrowing`, and sees it read app-only, decide (own and ancestor
Restricted) and log `plane=internal-routes`. The double lives in the test assembly, which `src/` cannot reference.

**Tests (slice B).**
- `DocumentRouteExternalNarrowingTests` (44). Real host, real filters, real `AuthorizationService` over token-stated
  rights, real `CallerSystemUserResolver`, and the REAL predicate behind the real seam (`AppOnlyDocumentWorld`).
  - Must fire:
    - own Restricted × internal-only communication × Restricted parent, on read, download, versions, preview, delete and
      checkout (18 cases);
    - a failed lookup (the resolver throws; the external flag is unreadable) narrows;
    - a narrowing fault refuses read, download and delete;
    - the bulk download drops every hidden id in ONE call (external caller and failed lookup); a bulk narrowing fault drops
      all;
    - both list routes remove hidden rows before paging (page 1: 2 of the 3 visible, `totalCount` 3, `hasNextPage` true;
      page 2: the 3rd, `hasNextPage` false), also with a failed lookup, and drop every row on a narrowing fault.
  - Must not fire:
    - an internal user: a Restricted document is served and deleted, the bulk download zips it, the list is unchanged;
      0 narrowing calls and 0 app-only reads;
    - an external caller keeps a visible document (read 200, delete 204, the content routes pass the filter);
    - with no right, the 403 comes first and nothing is narrowed (single and bulk).
- `ExternalDocumentNarrowingRouteTests.TheInternalRoutesNarrowing_IsRegistered_OverTheAppOnlyReader` (above).
- ArchTest `TheInternalPlaneDocumentRoutes_ReachTheNarrowing`. These must reach the narrowing, and a pin that disappears is
  reported:
  - `DocumentAuthorizationFilter::InvokeAsync`;
  - `BulkDownloadAuthorizationFilter::InvokeAsync`;
  - `DataverseDocumentsEndpoints::ListDocumentsAsync` and `::ListContainerDocumentsAsync`;
  - every caller of `IDocumentDataverseService.GetDocumentsByContainerAsync`.

  Control `InternalPlaneDetector_MustFireAndMustNotFire`: it fires for a filter that skips the narrowing and for a list that
  does not narrow; it does not fire for a filter that narrows through a helper, or for a list that narrows.
  `InternalRouteDocumentNarrowing.Invoke` is added to the narrowing primitives.

**Mutation table (slice B).** Each mutant was applied to the source; the unit test project and the ArchTests were rebuilt;
`DocumentRouteExternalNarrowingTests` + `ExternalDocumentNarrowingRouteTests` (110 cases) and
`ExternalDocumentNarrowingGuardTests` (6) were run; then the source was restored and checked against its SHA-1. **8 of 8
killed.** Runner: `.uac188/t188b_mutate.py` (untracked; deleted at the end).

| Id | Mutation | Unit failing | ArchTest | Killed by (representative) |
|---|---|---|---|---|
| M1 | `DocumentAuthorizationFilter`'s narrowing removed | 28 | fails | `SingleDocumentRoute_ExternalCaller_HiddenDocument_IsRefused` (18 route × kind cases), lookup-fault and narrowing-fault cases |
| M2 | the list routes skip the narrowing | 6 | fails | `List_ExternalCaller_RemovesHiddenRows_BeforePaging`, `List_LookupFault_Narrows`, `List_NarrowingFault_DropsEveryRow` (× 2 routes) |
| M3a | a lookup that throws is treated as internal | 1 | passes | `SingleDocumentRoute_LookupFault_Narrows_ItDoesNotRefuseEverything(ExternalFlagFault)` |
| M3b | `lookup-failed` (unresolved) is treated as internal | 4 | passes | `SingleDocumentRoute_LookupFault_…(LookupFault)`, `BulkDownload_ExternalCaller_DropsHiddenDocuments(LookupFault)`, `List_LookupFault_Narrows` (× 2) |
| M4 | the bulk filter skips the narrowing | 3 | fails | `BulkDownload_ExternalCaller_DropsHiddenDocuments` (× 2), `BulkDownload_NarrowingFault_DropsEveryDocument` |
| M5 | a narrowing fault shows everything | 6 | passes | `SingleDocumentRoute_NarrowingFault_IsRefused` (× 3), `BulkDownload_NarrowingFault_…`, `List_NarrowingFault_…` (× 2) |
| M6 | the internal exemption is removed (everyone narrowed) | 4 | passes | `SingleDocumentRoute_InternalUser_IsNotNarrowed`, `BulkDownload_InternalUser_IsNotNarrowed`, `List_InternalUser_IsNotNarrowed` (× 2) |
| M7 | every systemuser is treated as internal | 33 | passes | every external must-fire case |

The ArchTest is the second net for M1, M2 and M4 (`TheInternalPlaneDocumentRoutes_ReachTheNarrowing`: 1 of 6 fails). M3, M5,
M6 and M7 change the decision, not the wiring, so only the behaviour tests can see them.

**`ExternalWorkforceLinkEndpointsTests` (coordinator condition 3): root cause found and fixed; no issue needed.**
- Symptom: one case (`GuestToken_FailingACheck_…(defect: "expired")`) got `guest_token_invalid` instead of
  `guest_token_expired`, in every broad branch run (3 of 3) and in a mid-size one, never in isolation. The identical broad
  filter on `origin/master` `44063b3473` passed it (1 failed of 13,031, the timeout below), so per condition 3 it was ours.
- Cause (a diagnostic printed the inner exception): the validator saw `AggregateException("An error occurred while writing
  to logger(s)")` over `ObjectDisposedException` (`EventLogInternal`), not `SecurityTokenExpiredException`. IdentityModel's
  logger is process-wide (`LogHelper.Logger`), and Identity Web binds it, once, to the logger of the FIRST host whose
  JwtBearer options are built. In a test run that host is a fixture that is disposed when its class finishes; every later
  IdentityModel log line (the expired path logs at Error) then writes into its disposed logger factory, the Windows EventLog
  provider throws, and the token handler returns that as the failure. Which host is first depends on the test mix, so
  slice B's added tests and hosts moved the order and exposed it. A latent test-isolation defect, not a production one
  (a production process has one host).
- Fix: `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/IdentityModelLoggerIsolation.cs`, a `[ModuleInitializer]` that
  pins `LogHelper.Logger` to a no-op, process-lifetime `IIdentityLogger` before any host is built (Identity Web leaves a
  logger that is already set in place). No test asserts IdentityModel's own log lines.
- Test: `IdentityModelLoggerIsolationTests.AnExpiredToken_StillReadsAsExpired_AfterAHostIsDisposed` builds a real
  `LinkFixture` host, materializes its JwtBearer options, disposes it, and validates an expired token. Without the
  initializer it fails (`AggregateException` … `ObjectDisposedException`); with it, it passes.
- §11: (1) existing: `TestOutboundNetworkGuardModuleInitializer` is the assembly's other process-wide test setup, for a
  different concern; (2) extension: it could host the line, but its file is about outbound network, and one concern per
  initializer keeps each one's rationale next to it; (3) cost of doing nothing: any test that validates a token after the
  first host is disposed gets a wrong verdict, depending on test order.

**`ProvisionProjectIdempotencyTests.ProvisionProject_WhenRecordingTheContainerTimesOut_…`** times out (about 1 m 26 s) in
broad runs on `origin/master` too, and its fixture uses `FakeAuthHandler` (no token validation), so it is a separate,
pre-existing defect: filed as **#2185** (`bug`). It passed in the final branch run.

**Gates (slice B)**, on the final sources:

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | succeeded, 0 warnings |
| Unit and integration, filtered (`AccessControl` \| `ContractTests` \| `Documents` \| `Filters` \| `FileAccess` \| `Office` \| `Compose` \| `Regression` \| `ExternalAccess` \| `UnifiedAccessControl` \| `Services.Ai.Handlers` \| `RetrievalAccessTrim` \| `IdentityModelLoggerIsolation`, `Category!=Live`) | final sources: **13,234 passed, 0 failed, 7 skipped** (35 m 34 s). Before the logger fix: 13,231 passed, 2 failed (`ExternalWorkforceLinkEndpointsTests` `expired`, fixed above; #2185). The same filter on `origin/master` `44063b3473`: 13,023 passed, 1 failed (#2185), 7 skipped |
| Same failing areas, smaller filter (`AccessControl` \| `ExternalAccess`) | master 7,366 / 0 failed; branch 7,536 / 0 failed |
| `tests/Spaarke.ArchTests` (full project) | 1,214 passed, 0 failed (D-66 caught the first draft's dependency on `Services.Ai.Handlers.Dataverse`; fixed) |
| Mutants | 8 of 8 killed; sources SHA-checked after every run |
| Publish size (`dotnet publish -c Release`, framework-dependent, `Compress-Archive -CompressionLevel Optimal`, 4 PDBs each side) | branch 39,336,481 B vs a fresh `origin/master` `44063b3473` build 39,314,338 B: **+22,143 B (+0.02 MB)** for all of task 188 (rounds 1 to 3); raw +59,452 B. Total 39.3 MB |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages; no package changes |
| `git merge-tree --write-tree origin/master HEAD` | clean |

Not run, by the brief: the full unfiltered unit suite, any deploy, any Dataverse, Azure or Key Vault access.
