# W2 coordination — hot-file sequencing (task 010, 2026-10-19)

Sources:
- `gh pr view` (read-only);
- `git diff origin/master...origin/<branch>` for every active project branch;
- `projects/INDEX.md`;
- cross-session answers from UAC-r2, ebilling-r1 and ontology-r1, all received 2026-10-19.

No PR was commented on, merged or closed.

## Conflict-check result

This branch had no `src/` or `tests/` changes when the check ran. The overlap check covers the files W2/W3/W4 will
touch, compared against open PRs and against the other projects' branches, merge-base diff:

| Source | State | Overlapping W2-W4 files |
|---|---|---|
| UAC-r2 #2110 | MERGED 2026-10-19, already in this branch | ChildRecordEndpoints, FieldMappingEndpoints, CommunicationService, OwnedChildWrite, RefileWriterAfterRefileGuardTests |
| UAC-r2 #2104 | MERGED 2026-10-19, already in this branch | none |
| UAC-r2 #2078 | OPEN draft | RouteAuthorizationGuardTests.ExternalWrites/Ledger only. UAC confirmed the branch diff does not touch the W2 files. **No conflict** |
| ontology-r1 #1501 | OPEN, stale (updated 2026-10-09; its base #1391 merged 2026-10-10) | ChildRecordEndpoints; `Services/Ai/Handlers/Dataverse/OwnedChildWrite.cs` (old path). Asked ontology-r1 whether it is superseded |
| ebilling-r1 #1911 / `work/ebilling-r1` | OPEN draft, CONFLICTING with master | see the next table. **Not on the planning-time list; found here** |
| code-quality-r4 #935 | OPEN | `SharedPackageCensusTests.cs`. Not a W2 file; no conflict |
| UAC-r2, SPA-external-access-r3 and word-add-in-r1 branches | — | none of the W2-W4 files changed |

ebilling-r1 branch changes to W2-W4 files (merge-base 1473b34c6):

| File | Change | Affects |
|---|---|---|
| `Services/Dataverse/OwnedChildWrite.cs` | +254: invoice-family guard in CreateAsync **and** RefileAsync (`CheckInvoiceFamilyAsync`, `InvoiceFamilyStep`) | 011, 012, 013 |
| `Api/FieldMappings/FieldMappingEndpoints.cs` | +132: push invoice-family guard plus an update-only write (`If-Match: *`) | 040 |
| `Spaarke.Dataverse/IFieldMappingDataverseService.cs` | +18: `UpdateExistingRecordFieldsAsync` overload with impersonation | 005, 040 |
| `Services/Dataverse/RecordOwnershipResolver.cs` | +13: new `RecordOwnerUnresolvedException` constructor | 015 |
| `ChildRecordEndpoints.cs`, `DataverseCreateRecordHandler.cs`, `DecisionRouteCores.cs` | small | 017 |
| `SignalWriter.cs` (+418), `DecisionRecordWriter.cs` (+713) | large | 033 |
| `RecordOwnerAssignmentCensusTests.cs`, `AuthenticationSchemeCensusTests.cs` | census entries for invoice-family tables | 017, 061 |

## Per-file sequencing

| File / area | Owner project(s) | Competing PR | State | Agreed order and rule |
|---|---|---|---|---|
| `OwnedChildWrite.cs` (create half plus `CallerWriteAuthorization`) | UAC-r2 (boundary), ebilling-r1 (guard), this project (W2) | #1911 | draft, not imminent | ebilling preferred to merge first; **the owner decided 2026-10-20 that W2 proceeds on master** (no #1911 date). `RefileAsync` stays in OwnedChildWrite (UAC). The invoice-family guard is carried unchanged; its create half goes to the registry, its refile half stays in RefileAsync. One copy only; whoever lands second reconciles |
| `ChildRecordEndpoints.cs` | UAC-r2, ontology-r1, ebilling-r1, this project (017) | #1501 (stale), #1911 | — | Rebase plus `/conflict-check` before 017; UAC reviews the 017 diff before merge |
| `CommunicationService.cs` | UAC-r2 (#2110 merged) | none open | — | No conflict; rebase before 017/034 |
| Census ArchTests (`RecordOwnerAssignmentCensusTests`, `RefileWriterAfterRefileGuardTests`, `AuthenticationSchemeCensusTests`) | UAC-r2, ebilling-r1, ontology-r1 (task 182) | #1911 | — | Must-fire controls are kept and baselines do not grow (UAC condition 1a). Rebase before 011/017/061 |
| Role definitions | UAC-r2 | none in flight | planned: named core-record delete role; Core User loses prvDelete on core tables | UAC pings before touching our files. Our W5 Assign-hiding (051) does not change roles |
| `CoreAncestorResolver` stamp path | UAC-r2 (#2119, not started) | — | — | UAC pings before starting |
| `FieldMappingEndpoints.cs` push | UAC-r2 (#2110 merged), ebilling-r1 | #1911 | — | Before 040: rebase onto whatever has landed; keep the ebilling push update-only wire tests (#2081 round 3) passing unchanged |
| `IFieldMappingDataverseService.cs` | ebilling-r1 | #1911 | — | 005 adds `sprk_syncmode` to the rule read. Additive, but rebase before 005 |
| Chat / decision cores (`DataverseCreateRecordHandler`, `EmailDraftToolHandler`, `BudgetInquiryExecutor`, `DecisionRouteCores`) | ontology-r1 (its tasks 043/044/046/048/070 are done) | none live (#1501 superseded) | no ontology work in flight or planned | No conflict for 017. Send ontology the 017 range if it touches the DecisionRouteCores create path |
| `SignalWriter`, `DecisionRecordWriter`, `GateDecisionRecordSharing` | ebilling-r1 (tasks 049, 074), then ontology-r1 (a 1-day lane after 074; 042c in its next docs→master sync) | #1911 | ebilling changes on its branch, not master | Before 033: the ontology owner reviews (UAC for ownership), and 033 sends ontology a design note before implementation. See "ontology constraints" |
| Invoice create paths (`InvoiceSubmissionService`, `InvoiceSubmissionPersister`, `InvoiceReviewService` confirm) | ebilling-r1 | #1911 | — | See "ebilling constraints" below. ebilling reviews 025/032 before merge |

## Affected tasks

- **011** — held until the owner decides the ebilling sequencing and condition 1c. UAC reviews the diff.
- **012** — the registry carries the invoice-family create-side guard and possibly #2115 item 5 (the missing
  MayRefile gate at `OwnedChildWrite.cs:1535-1541`). If it does, comment on #2115.
- **014** — UAC condition 1c: a mapped **filing** lookup that fails AppendTo refuses; only reference lookups drop with a
  warning. This is an FR-08 amendment pending the owner. Needs a must-fire test on a Secure parent.
- **015** — fail-closed owner and an unchanged post-create order and refusal codes (UAC 1b).
- **017** — UAC reviews the diff; rebase over ebilling/ontology changes.
- **025 / 032** — ebilling's single-creator rule: invoices are created only by the submission operation. Re-scope at
  task start (see below).
- **028** — no conflict found (Analysis/email); rebase before.
- **040** — keep ebilling's push guard plus the update-only write.
- **060** — the ownership matrix lists `InvoiceSubmissionPersister` as a named exception if it stays outside the service.

## ebilling constraints on the invoice paths (ebilling-r1 answer, 2026-10-19)

- **Single creator (binding; ebilling FR-17/FR-20; ADR-002):** invoices are created only by the submission operation.
  The internal "invoice wizard" is an internal submission (channel Internal) through `InvoiceSubmissionService` and
  the processing job; wizards preview only. The `InvoiceReviewService` confirm route was redirected into submission
  (D-71). Re-pointing must not open a second invoice creator.
- **`InvoiceSubmissionPersister`:**
  - deterministic ids derived from the submission id (`InvoiceIdFor`);
  - one `ExecuteTransaction` holding the invoice, lines, approval and submission link (D-101/D-130);
  - owner resolved once via `ForParents` and applied to every row;
  - status set on create via statecode/statuscode (D-41);
  - totals computed server-side after the write.

  ebilling recommends leaving it out of the W3 re-point, or routing it through the service only if the service takes
  a pre-built transactional batch with fixed ids.
- `InvoiceFamilyCoreRecordGuard` runs before the write. Keep its tests and the push update-only wire tests passing
  unchanged, and ping ebilling to review the rule when it lands.

## ontology-r1 answer (2026-10-19)

- **#1501:** not live. Ontology task 136 is deferred, and the root fix #1391 is merged. The stale delta is tracked
  as #1693. Closing #1501 is the ontology owner's call, so treat it as superseded.
- **Conflicting timing:** ontology expects ebilling's SignalWriter changes on master "probably within days", but
  ebilling says #1911 is "not imminent". Ask ebilling before 033 or 040.
- **033 invariants (binding for any re-point):**
  - writes go through the writer's own restricted connection (Spaarke Ontology Service app user, no client
    secret), never the BFF identity, never widened;
  - Signal create keeps:
    - the dedupe-key alternate-key create plus reconcile;
    - `IRecordOwnershipResolver` with team owner and owner read-back;
    - `CheckRequest` / `PreflightRefusal`;
    - `IsDataverseAccessDenied` classification;
    - escalation reasons and skip codes;
  - Signal close, supersede, redact and the D-122 automated close stay row-version conditional updates, not
    creates;
  - Decision Record keeps:
    - append-only by privilege;
    - the idempotency lookup with `review_invalid`;
    - the content hash including ResolvedSignalIds;
    - the GateDecisionRecordSharing and ownership steps;
  - **no field mapping into Signal or Decision Record rows** (§0.3 binds `sprk_sentence` and the fact snapshot);
  - the `OntologyWriterEvents` EventIds and `OntologyWriterFailureReason` values stay unchanged;
  - §11: 033 must state the cost of doing nothing for the two writers specifically. **Leaving them out of the
    service (a named exception in 060) is a valid outcome; the ontology owner decides.**

## ebilling-r1 heads-up (2026-10-20): SignalWriter / DecisionRecordWriter range in flight

ebilling has a range in flight that adds no creates:
- `SignalWriter.CloseActedByAutomatedDecisionAsync` gains a `decidedByUserId` written as `sprk_resolvedby` inside its
  conditional update. It may be renamed `CloseActedByDecisionAsync`.
- `DecisionRecordWriter` gains a read-only `FindBillingDecisionResolvedSetAsync`.
- `DecisionCommitService.CallerMayDecideAsync` may move to a shared helper as a pure move.

**No collision** with 033, which covers creates only; closes are row-version updates and out of scope. W2/017 do not
touch DecisionCommitService. ebilling sends the shas when the range lands; rebase before 033.

## Shared rules (agreed with UAC-r2, ebilling-r1 and ontology-r1)

1. Before each W2/W3/W4 task: rebase onto `origin/master` and run `/conflict-check`. Whoever merges second rebases.
2. Any change to a refusal code, an owner decision or the order of post-create steps pings UAC-r2 **before** merge.
3. UAC-r2 adversarially reviews the 011 and 017 diffs (send the ranges).
4. ebilling-r1 reviews 025/032 and the invoice-family registry rule.
5. #1911 leaving draft: ebilling messages this project.

## 2026-10-21 — UAC-r2 #2169 (D7 T7a) in `FieldMappingEndpoints.ApplyMappingRule`

- UAC-r2 lands a skip on push: on a `sprk_document` target, rules targeting `sprk_accesspermission` or
  `sprk_accessinheritance` are skipped with a per-rule skip reason. Other target tables are unchanged.
- No collision with in-flight work: 013, 004 and 005 do not touch `FieldMappingEndpoints.cs`.
- **Task 040** (which deletes `ApplyMappingRule`'s Copy logic) must carry the skip into the engine's push path, has a
  criterion for it, and goes to UAC-r2 for review.
- Dev has 0 mapping rules touching either access column (25 rules checked), so no owner decision is needed.
- Resolved: UAC-r2 asked that 014 never map `sprk_document.sprk_accessinheritance` (skip with a warning, matching push)
  while `sprk_accesspermission` stays fill-only. Criteria added to 014 and 040.
- #2169 landed: c8c591777f (PR #2169, draft). Pinning test: `DocumentAccessPermissionPushTargetTests` (3 methods, 6
  cases), which 040 keeps green unchanged. Details in the 040 POML notes.

## 2026-10-21 — ebilling-r1 review of 013 (bad3195920..123b632315)

- **Verdict:** no objection to master once the 013 test gap is closed (fix round 2: the rule gets the exact item and
  caller; a throwing rule; cancellation; the ParentUnavailable arm order). No existence leak and no fail-open on a live
  path. merge-tree against work/ebilling-r1 is clean.
- **Reconciliation items:** F1 (the rule is unregistered → a blocking 017 criterion plus a ratchet), F2 (500 for
  read_failed and unknown_entity), F4 (supersedes keeps Code and Reason).
- **014:** reassign the same `item`; MappedLookup gets a RelatedEntitySet; invoice-family core lookups go in
  MappingProtectedColumns.
- **016 and 040:** call `InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync` (app-only) on stored-row writes once
  #1911 is on master.
- **Updates:** do not widen pre-write rules to updates; RefileAsync stays in OwnedChildWrite. Correction (a)
  (EffectiveLookups) is not needed for creates.

## 2026-10-21 — UAC-r2 review of 013

- **Conditions:** (a) PASS, (b) PASS on code, (c) PASS for 013, K2 PASS.
- **Fix before master (in 013 fix round 2):**
  - F-1: an I-16 run/order test;
  - F-2: the (table, id) key test;
  - F-3: rename Office `RecordCreationService.cs` → `OfficeRecordCreationService.cs` and re-key the census rows (the census can then fire);
  - F-4: correct the step-10 premise (five writers stamp before the create).
- **Step 10 (owner):** UAC recommends FR-06 = derive I-1 (and I-16 for To Do/Event/Communication) into the payload before
  the create, refusing on failure, AND re-derive after the create. Documents stay post-create (#2169). This is
  required before any pre-create stamp writer is re-pointed (017/018).
- **Carried:**
  - K-2 → 014 (no mapped filing lookup via ServerSetLookupColumns);
  - K-3 → 018;
  - K-4, SystemPrincipal narrowing, and the document access columns → 017;
  - K-5: a fault after the create leaves an isolated row unshared, the same as the route today (accepted).
- **Owner, before 017:** caller-refused columns on projects and matters (#1608).

## 2026-10-21 — UAC-r2 review of 014 (fad68a797b..13e6919692)

- **Passed:** step 3 and step 5; `server_set_filing_lookup` 500; D7 for mapping; keeping the 403 for a field-secured
  mapped filing lookup (not an oracle).
- **Fix now (014 fix round 3):**
  - F-A: a (table, id) callback-key test; the 013-r2 test's because-text is stale;
  - F-B: decide "filing" with `ParentsOf`'s predicate. `sprk_workassignment.sprk_regardingcommunication` falls through
    today. A mapped match refuses, which closes K-5.
- **#2188:** live on master through the Office quick-create. Office hotfix PR; the service half is in 014 fix round 3.
  Read as the caller and do not build a per-column check.
- **Carried:** K-1 (the K-2 guard runs after step 3's reads); K-2 (document access columns on the system path → 017);
  K-3 → UAC #2190 (`sprk_regardingcommunication` is missing from the root plan's filing columns); K-4 ("from chat" text
  and the technical 500 detail → owner and 017).
