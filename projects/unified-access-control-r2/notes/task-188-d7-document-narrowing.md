# Task 188 — D7 T7: one predicate narrows every external document surface

> #2126 / ISS-015. Design: `notes/2119-manage-access-all-forms-design.md` §6.
> Branch `uac/188-d7-document-narrowing` (worktree `C:/wt188`), based on `origin/master` 4f5f22694c.
> Status: code, tests and guard are done; the draft PR is open. Not deployed. The R3 deploy, the live gate and the
> field-security `-Apply` are main-session or owner steps (§10).

## 1. The rule (as built)

A document is visible to a caller who is not an internal systemuser only when ALL of these hold:

1. **Root rule.** `ExternalMultiRootRule` allows it. This is unchanged.
2. **Own value.** Its own stored `sprk_accesspermission` is not Restricted (100000002).
   - A value that is not a known level hides it (fail closed).
   - Null or absent counts as Standard.
3. **Ancestor value** (main-session scope addition, 2026-10-20). On the same bounded `sprk_parentdocument` walk, no
   visited ancestor's own value is Restricted. An unreadable ancestor value also hides it.
   - Rule text: **"An ancestor document's own Access Permission = Restricted hides the descendant (an attachment under a
     Restricted .eml is hidden). The ancestor's value is read on the same walk as rule 4, with no extra round trip. An
     unreadable ancestor value fails closed."**
4. **Communication.** The communication it comes from passes the task 065 rule:
   - flag JSON `false`;
   - a thread set;
   - that thread explicitly Open now.

   Which communication counts:
   - the document's own `_sprk_relatedcommunication_value`;
   - when that is empty, its parent's (`sprk_parentdocument`), then the grandparent's, at most **3 parent hops**;
   - the **first link found decides**.

   The 065 rule is reused, not copied. It moved into `CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync`, and
   the assistant's tools and the external plane share that one copy.

**Fail closed.** Each of these hides the document:
- a link that is present but not a GUID;
- a parent that does not come back;
- a cycle;
- a chain that would need a fourth hop;
- a communication that does not come back.

A failed read hides the whole page, and the response says `Truncated`. The assistant's tools fail the call with no data.

**Own value wider than the root.** It changes nothing, because visibility is "root AND own".

**Internal systemusers** on the external plane are not narrowed. This is the 065 rule's own exemption
(`CommunicationThreadReadService`).
- A caller is internal only when `CallerPrincipal.SystemUserId` is set AND `ISystemUserIdentityResolver.IsExternalAsync`
  is false.
- A contact, a systemuser flagged external, and a failed lookup are all narrowed.
- The resolver reaches `ExternalDataService` as an optional constructor parameter, which the typed-client factory
  supplies (`ExternalAccessModule.cs:95`; the resolver is a singleton). There is no new DI registration.

**The I-14 amendment** is in `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. I-16 is corrected there: "no
access decision reads a child's copy, EXCEPT the external document narrowing — it only narrows; it never widens".

### Interpretation question for the main session (ancestor leg)

The walk stops at the first link (interpretation (b)), so an ancestor ABOVE a document that carries its own link is not
checked. Example: a document linked to a visible communication, whose parent .eml is Restricted, stays visible.
- This is pinned by `ADocumentWithItsOwnVisibleLink_IsDecidedByThatLink_ItsAncestorsAreNotVisited`.
- No writer sets both links on one document today (§7), so the case does not arise from the product's own writes.
- **Alternative (a):** keep walking past the first link to check Restricted ancestors. That costs up to 3 more batched
  document reads per page for linked rows.
- Say which interpretation you want. Either is a one-line change in `ExternalDocumentNarrowing.Step`, plus flipping that
  one test.

## 2. Surface table

| Surface | Wiring (file:line) | Behaviour when hidden |
|---|---|---|
| External list: projects, matters, work assignments, service requests and invoices (one helper, `ExternalProjectDataEndpoints.cs:625`) | `ExternalDataService.cs:490-491` (decide), `:672` (list `$select` carries `SelectColumns`) | Row dropped; `Truncated` when the read faulted |
| Download content (6 routes → `DownloadDocumentForParent`, `ExternalProjectDataEndpoints.cs:645`) | `:684` → `IsDocumentReadableUnderAsync` `:1490`, narrowing `:1505` → `ExternalDataService.IsDocumentVisibleAsync` `:1854`; single read `$select` `ExternalDataService.cs:938` | The existing "not part of this record" refusal, before any SPE pointer resolve or file read |
| Versions (6 routes → `ListDocumentVersionsForParent`, `:2070`) | `:2099` → same as download | Same refusal; no SPE read |
| Preview | None on the external plane: no preview or thumbnail route exists (`MapGet` scan of `Api/ExternalAccess`) | — |
| Documents module fetch | `ExternalModuleDataEndpoints.cs:213` (injects `NarrowDocumentsAsync`), `:342-351` (projects `NarrowingAttributes`), `:377-392` (decide), `:411` (Truncated), `ExternalModuleRegistry.cs:216/225` (`NarrowsDocuments` derived from `RecordEntity`, never configured) | Row dropped; columns it projected stripped; `Truncated` on fault |
| Module single record | Safe: `IsRecordAccessible` never matches a child document id | — |
| Delete own document | A write; out of scope | — |
| Messages | The message routes refuse attachments | — |
| Notifications feed | No documents | — |
| `dataverse.read_query` | `DataverseReadQueryHandler.cs:159` (document guard), `:233` (columns injected), `:295` (decide), `:318` (row filter); injected columns stripped `:359` | Row dropped; reason codes logged |
| `dataverse.search_data` | `DataverseSearchDataHandler.cs:298` (document hits), `:303` (`ReadAndDecideAsync`), `:311` (drop) | Hit dropped silently |
| `dataverse.describe` | `DataverseDescribeHandler.cs:356/360` (guard), `:425-426` (decide) | Same code and message as not-found |

Task 078's assistant rule (related communication only) is removed. `ExternalCallerCommunicationGuard` keeps only the
`IsDocumentTable` gate and the `QueryOver` reader. 078's existing tests pass against the predicate unchanged:
- `ExecuteChatAsync_ExternalCallerOnDocument_HidesDocumentsWhoseRelatedCommunicationFailsRule`;
- `..._RelatedCommunicationsSpanTwoChunks_...`;
- `..._UnreadableRelatedCommunicationLink_Hidden`;
- the search and describe equivalents.

## 3. Component justification (§11)

**`ExternalDocumentNarrowing`** (new static class).
1. *Existing:*
   - `ExternalMultiRootRule` decides by roots;
   - `CommunicationOpenThreadGate` decides a communication (065);
   - `ExternalCallerCommunicationGuard` (078) applied one leg in the assistant only.
2. *Extension:* the root rule's input is root links plus grants, read through the secure-flag and live-parent seams. This
   predicate's input is the document's own columns and its parent chain, read through a reader shared with the
   assistant's user client. So it sits beside the rule. 078's leg is removed in its favour, and the 065 rule is moved
   into `CommunicationOpenThreadGate` rather than copied.
3. *Cost of doing nothing:* a partner with a grant on a matter downloads the .eml of an internal-only email and its
   attachments, and sees a document a user marked Restricted (ISS-015).

**`ExternalDataService.NarrowDocumentsAsync` / `IsDocumentVisibleAsync`** (new methods in an existing file).
- This is the one external-plane seam: it carries the internal-systemuser exemption and the fail-closed wrapper.
- Without it, each of the three external surfaces would carry its own exemption and fault handling.

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

## 4. Query cost (worst case, measured by test)

Reads per request, for a caller who is narrowed:

| Rows | Reads |
|---|---|
| A row that decides on its own columns (own Restricted or unreadable; or no link and no parent) | 0 |
| Per hop level (≤ 3) | 1 batched `sprk_documents` read per 50 DISTINCT parents still undecided. A parent already on the page (or read at an earlier hop) is not read again |
| Communications | ⌈distinct communications / 50⌉ batched reads, then ⌈distinct threads / 50⌉ batched thread-privacy reads (the 065 rule) |
| Internal systemuser | 0 |

**Worst case per page of N rows:**

    3·⌈P/50⌉ + ⌈C/50⌉ + ⌈T/50⌉

- P = distinct parents, C = distinct communications, T = distinct threads.
- Measured: `APageIsReadInBatches_NeverOneReadPerRow` (120 rows under 120 distinct parents) makes exactly 3 document
  reads + 1 communication read + 1 thread read.
- For a 5,000-row list where every row is under its own communication: ~100 + ~100 sequential reads.
  - This is the same order as task 187's live-parent walk on such a page.
  - The reads are sequential chunks, and no cost log was added. See finding F-5.
- A single-document route: ≤ 3 document reads + 1 + 1.

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
  partners.
- `sprk_accesspermission` is user-editable by design. A user's own Restricted can only narrow, and lowering it back
  restores the root's answer, never more.

**The fix.** Field security on both links, using task 133's profiles. The engine was extended with target
`DocumentNarrowingLinks`, and `scripts/Set-DocumentNarrowingFieldSecurity.ps1` is the named entry point.

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

**Trade-off (read before `-Apply`).**
- A secured column reads EMPTY for a user outside the reader profile.
- The assistant's `dataverse.*` tools read as the user, so for such a user the predicate would see "no link" and SHOW
  the document (fail open).
- (p2) guards this: every BU default team must be on the reader profile. A BU created later needs the reader-team step
  re-run.
- The external plane reads app-only (BFF writer profile), so it is unaffected.
- `sprk_parentdocument` is included because the walk reads it. The main session may drop it from the target if it
  prefers to lock only the communication link.

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

**Pipeline attachments** link only to the communication (`sprk_relatedcommunication`), not to the .eml as parent. So a
Restricted .eml does not hide them through the walk (finding F-3).

## 8. Mutation table (negative controls)

`.uac188/t188_mutate.py` applied each mutation, rebuilt, ran the filtered suite (262 cases), restored the source, and
checked SHA-1. All 7 sources matched the pre-mutation hashes afterwards.

| # | Mutation | File | Failed cases | Killing tests (examples) |
|---|---|---|---|---|
| M1 | Own Restricted leg removed | ExternalDocumentNarrowing | 17 | `OwnRestricted_IsHidden_*`, `List_HidesTheDocument(own-restricted)`, `SingleDocumentRoute_Refuses…(own-restricted)`, module, AI |
| M2 | Unknown own value read as Standard | ″ | 3 | `OwnValueThatIsNotAKnownLevel_IsHidden_FailClosed`, `AnAncestorWhoseValueIsUnreadable_*` |
| M3 | Communication (065) leg always visible | ″ | 37 | `LinkedToAnInternalOnlyCommunication_IsHidden`, `…PrivateThread…`, `…NoThread…`, `…FlagIsUnreadable…`, route and module cases |
| M4 | Ancestor Restricted leg removed | ″ | 14 | `AttachmentWhoseParentEmlIsRestricted_WithNoLinkAnywhere_IsHidden`, `ARestrictedGrandparent_HidesTheDocument`, `ARestrictedParentLinkedToAVisibleCommunication_*`, route `parent-eml-restricted` / `grandparent-restricted`, AI read_query, search and describe |
| M5 | Parent walk removed | ″ | 44 | `AttachmentWhoseParentEmlIsLinkedToAnInternalOnly…`, `ACycle_IsHidden`, `AFourHopChain_*`, batching, route cases |
| M6 | Hop bound raised to 4 | ″ | 4 | `AFourHopChain_IsHidden_*`, route `four-hop-chain` (list and single) |
| M7 | Cycle check removed | ″ | 2 | `ACycle_IsHidden`, `ADocumentThatIsItsOwnParent_IsHidden` |
| M8 | Unreadable link check removed | ″ | 1 | `ACommunicationLinkThatIsNotAGuid_IsHidden_WithoutARead` |
| M9 | Parent not found → visible | ″ | 1 | `AParentThatDoesNotComeBack_IsHidden` |
| M10 | Unreadable parent link → visible | ″ | 1 | `AParentLinkThatIsNotAGuid_IsHidden_WithoutARead` |
| M11 | Read fault fails open | ″ | 7 | `AFailedRead_HidesEveryRow_AndSaysSo_*`, `List_WhenTheNarrowingReadFails_*`, `SingleDocumentRoute_WhenTheNarrowingReadFails_*`, module fault |
| S1 | List surface unwired | ExternalDataService | 10 | `List_HidesTheDocument(*)`, `List_WhenTheNarrowingReadFails_*` |
| S2 | List `$select` drops the predicate columns | ″ | 2 | `List_ReadsTheAccessPermission_AndTheParentLinks_InTheListQuery`, `List_HidesTheDocument` |
| S3 | Single-document `$select` drops the predicate columns | ″ | 2 | `SingleDocumentRoute_RefusesTheDocument_*` |
| S4 | Download and versions unwired | ExternalProjectDataEndpoints | 20 | `SingleDocumentRoute_RefusesTheDocument_*` (content and versions), fault case |
| S5 | Module fetch unwired | ExternalModuleDataEndpoints | 2 | `DocumentsModuleFetch_*` |
| S6 | Module projection drops `NarrowingAttributes` | ″ | 1 | `DocumentsModuleFetch_HidesOwnAndAncestorRestricted_*` |
| S7 | read_query unwired | DataverseReadQueryHandler | 6 | 078's three tests, plus `…Task188_OwnOrAncestorRestrictedOrInternalOnlyParent_Hidden` |
| S8 | search_data unwired | DataverseSearchDataHandler | 3 | 078's two tests, plus `…Task188_…_Dropped` |
| S9 | describe unwired | DataverseDescribeHandler | 8 | 078's theory, plus `…Task188_HiddenByThePredicate_AnswersLikeNotFound` |
| E1 | Internal exemption removed | ExternalDataService | 1 | `AnInternalSystemUser_IsNotNarrowed_AndNothingIsRead` |
| E2 | Every systemuser treated as internal | ″ | 1 | `ASystemUserFlaggedExternal_IsNarrowed` |

There were no survivors.

**Guard proofs** (§9). The guard was run against two real-code mutations, and each was restored afterwards:
- the list unwired (`NarrowDocumentsAsync` replaced): the guard fails;
- the single-document narrowing replaced with `Task.FromResult(true)`: the guard fails.

## 9. ArchTest guard

`tests/Spaarke.ArchTests/ExternalDocumentNarrowingGuardTests.cs` uses IL, via `IlCallScan`.

**What counts as a surface.** A BFF source method that calls a surface primitive:
- `BuildDocumentListUrl`;
- `GetDocumentParentAndNameAsync`;
- `DocumentScopeRead.IsReadableUnder`;
- `ExternalModuleDescriptor.ScopeRows`;
- `ExternalCallerCommunicationGuard.IsDocumentTable`.

**What counts as reaching the narrowing.** The method must reach, transitively over BFF methods, one of:
- `ExternalDocumentNarrowing.DecideAsync`, `DecideOrFailClosedAsync` or `ReadAndDecideAsync`;
- `ExternalDataService.NarrowDocumentsAsync` or `IsDocumentVisibleAsync`;
- `DocumentNarrower.Invoke`.

Lambdas, local functions and state machines count for the method they were written in. A comment, a string or a `nameof`
does not count.

**Ratchet baseline.** Two listed non-document callers, each with its reason:
- `ExternalMessageEndpoints.EvaluateRecordGateAsync`: it scopes the message's regarding record;
- `ExternalNotificationFeedEndpoints.GetFeedCoreAsync`: service requests.

An entry goes stale, and fails the guard, when it stops using a primitive or starts narrowing.

**Non-vacuity.** Seven wired surfaces are pinned as both "seen reading" and "seen reaching".

**Controls** (`Detector_MustFireAndMustNotFire`):
- must fire: a mention-only read;
- must not fire: a narrowing inside a lambda; a narrowing through a helper; a type with no document read.

**Limits (K).**
- Reach is per method by name. It says the method can get to the narrowing, not that every path does. The surface tests
  pin the paths.
- A new hand-written `sprk_documents` query that uses no primitive is not seen. The primitive list is the contract a
  reviewer extends.
- A non-document module whose FetchXML link-entities `sprk_document` columns is not narrowed (finding F-6).

## 10. Gates

| Gate | Result |
|---|---|
| BFF build (via the ArchTests and unit builds) | 0 errors, 0 warnings |
| Filtered unit suites (`Services.Ai.Handlers \| ExternalAccess \| UnifiedAccessControl \| Communication`) | 6,580 passed, 2 skipped, 0 failed |
| New tests | `ExternalDocumentNarrowingTests` 50, `ExternalDocumentNarrowingRouteTests` 50, AI handler tests 7 cases, guard 2 |
| Full ArchTests | 1,210 passed, 0 failed |
| Publish size (§10 / NFR-01) | See the block below |
| Vulnerable packages | `dotnet list package --vulnerable --include-transitive`: none. No package changes |
| /conflict-check | Open PRs overlap only on `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`: #2161 (#2159 relocation) and #1911 (ebilling). **Task 189's branch `uac/189-own-narrowing` edits the same I-16 row**, so a line conflict is expected and the second to merge resolves it. The texts are compatible: 189 says a parented Document holds max(own narrowing, inherited); 188 says the stored value may hide a document from partners |
| code-review / adr-check | §11 |

**Publish size**, measured with `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` from
fresh short-path worktrees: master 4f5f22694c in `C:/wt188m`, the branch at dcca594f60 in `C:/wt188b`.
- Mode: framework-dependent linux-x64 (csproj).
- Compression: `Compress-Archive -CompressionLevel Optimal`, PDBs included.
- Master: 39,314,226 B (37.49 MB).
- Branch: 39,324,452 B (37.50 MB).
- **Delta: +10,226 B compressed (+27,864 B raw).** The ceiling is 60 MB.

## 11. Review findings (code-review + adr-check, self-run)

**ADR compliance.**
- ADR-002 WP-6 (fail closed): yes.
- ADR-009: no cache.
- ADR-010: no new interface or registration.
- ADR-013: the AI handlers consume an Infrastructure static. No CRUD→AI dependency.
- ADR-015: reason codes and counts only. The document id is logged as the existing download and versions lines already
  do.
- ADR-028: no auth mechanism changes.
- ADR-052: no background work.
- ADR-038:
  - The new tests sit beside their siblings in `tests/unit/...` (`ExternalMultiRootRuleTests`,
    `ExternalLiveParentRuleTests`, `CommunicationOpenThreadGateTests`). That is outside the KEEP paths.
  - **Recommend /test-diet classify them KEEP**, or move them to `tests/integration/auth/ExternalAccess/`, because they
    are I-14's "Enforced by".
  - No `Mock<HttpMessageHandler>`. The fakes are the existing handler fakes, plus `Mock<IDataverseUserClient>` at the
    module boundary.

**Findings for the main session** (fixed in scope: none outstanding. Each is a decision or a filing):
- **F-1 (interpretation, §1).** Should Restricted ancestors above a linked document also hide it? Currently no
  (interpretation (b)).
- **F-2 (task 189's file, found in passing).** The remark at `ChildAccessPermissionReconciler.cs:95-96` reads "display
  only (no access decision reads it), so nothing here can widen or narrow anyone's access". After 188 that is false for
  documents: a value the reconciler writes as Restricted hides that document from partners. Not edited here: it is
  189's file.
- **F-3 (T8 / 189).** Communication-pipeline attachments link to the communication, not to the .eml as parent. So "a
  Restricted .eml hides its attachments" holds only for documents whose `sprk_parentdocument` is the .eml (the email
  attachment path, `DataverseServiceClientImpl.cs:1023`).
- **F-4 (consistency).** A document filed under a Restricted root stores Restricted (I-16 inheritance). It is therefore
  hidden from partners who reach it through ANOTHER, readable root. This matches "follows its parent unless more
  restricted".
  - Contacts never reach Restricted roots (the `AccessibleRecordSetService` veto), so the contact plane loses nothing it
    had.
  - Confirm that this is intended for external systemusers.
- **F-5 (K3, performance).** The worst case on a 5,000-row list is ~200 sequential reads (§4), and no cost log was
  added. Suggest a follow-up issue if R3 sees latency: a cost log like 187's, or reuse the communication rows the 187
  walk already reads.
- **F-6 (K).** A non-document module with a FetchXML link-entity to `sprk_document` columns is not narrowed. No such
  module exists today: modules are rows, and the documents module is `RecordEntity = sprk_document`.
- **F-7 (FLS trade-off, §6).** The assistant reads as the user. A link masked by field security for a user outside the
  reader profile fails open. (p2) guards it.
- **F-8.** Internal systemusers are exempt on the external plane (the 065 rule's posture). Confirm.

## 12. ADR-034 / auth constraint text (for the main session to apply to `.claude/`)

> A child's Access Permission may narrow partner visibility (Restricted hides it); it never widens it. Enforced by: task
> 188's tests (`ExternalDocumentNarrowingTests`, `ExternalDocumentNarrowingRouteTests`, the `dataverse.*` handler
> tests) and `ExternalDocumentNarrowingGuardTests` (ArchTest).

## 13. Not done here (by design)

- **R3 deploy and the server-side live gate.** The external list must answer 200 with no new 5xx, and a document linked
  to an internal-only communication must be absent. Main session.
- **Field security `-Apply` / `-Verify`.** Owner-approved, after the deploy.
- **`.claude/` edits** (the ADR-034 / auth constraint text, §12). Main session.
- The task exceeded the ~100-step size guideline. Shown by the mutation run, the guard and two scope additions.
