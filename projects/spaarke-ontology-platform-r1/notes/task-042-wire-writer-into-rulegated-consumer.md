# Task 042 — wire the Decision Record writer into the rule-gated consumer

> Branch `task/ontology-042-wire-writer` (worktree `C:\wts-042`), cut from `origin/docs/ontology-platform-design` at `bcd5824a8`.
> Spec: FR-23, success criterion 3. Task file: `tasks/042-wire-writer-into-rulegated-consumer.poml`.

## 042 escalation → A+C chosen by the main session; owner may override before merge

The POML's first escalation trigger fired: the shipped `DecisionRecordWriter` contract (tasks 040/142) is review-shaped and could not
record the gate's decision as written. The mismatches were:

| POML says | Writer contract (040/142) |
|---|---|
| a human `ConfirmedByUserId` | the gate has no human; `sprk_confirmedby` is nullable live |
| a `PolicyVersionId` | the gate's rules are `sprk_communicationrule` rows, not policy versions |
| resolved Signals | the gate decides on a communication, not on Signals |
| `sprk_action` from the closed `DecisionActionCatalog` | the gate's RI action is not a catalog action |
| class derived from the catalog (Routine/Judgement) | the gate is Judgement on both paths |
| `sprk_gatesessionid` links an ADR-040 SessionGate entry | the enrichment gate has no SessionGate |

Options put to the main session: (A) add an additive entry point to the writer that reuses its filing/owner/refusal; (B) widen
`WriteAsync`; (C) amend the criteria that cannot hold. **Decision (main session, 2026-10-14): A, plus C's criterion amendment.**

What was built (A):

- `DecisionRecordWriter.WriteGateDecisionAsync(GateDecisionRecordRequest, ct)`, additive. `WriteAsync` is behaviourally unchanged
  (its body moved into a try/catch wrapper that calls the shared private `CreateAsync`; pinned by
  `Gate_TheReviewEntryPointIsUnchanged_…` and the existing 100 writer tests).
- Both entry points reach the SAME `CreateAsync`, which holds the single `_writerClient.CreateAsync` call, `Content`, `FiledAndOwnedAsync`,
  the secure-owner read-back and the result. Nothing was copied: filing (subject = the communication through the shared regarding builder,
  then the core-ancestor stamp), owner resolution (`RecordOwnershipResolver`, D-129/D-130) and the two-wall refusal are the existing code.
- Field semantics (closed set): class Judgement, set explicitly; outcome Authorized (100000000) / Denied (100000001) from
  `decision.Authorize`; `sprk_action` and `sprk_actioncode` null on deny; on authorize the gate's proposed RI action
  (`communication-ri-action`) is free text in the one-step review and is NOT added to `DecisionActionCatalog`; `sprk_confirmedby` null
  (never the BFF identity or an owner, D-129); `sprk_policyversion` null (matched rule id(s) and priority go in `sprk_factsnapshot`);
  resolved Signal ids empty; `sprk_gatesessionid` null.
- Owner: the gate asks the resolver as the CONTENT of the communication (`KeepCreatorUnlessTargetIsTeamOwned`, uac-r2's `ContentOf` shape), so an UNFILED communication (E1: keeps its creator) is refused rather than handed to the business unit's default team; see "Review finding 1" below.
- Matter/project come from `CoreAncestorResolver.ResolveStampsAsync` (D-34), not a direct `sprk_regardingmatter` read. One stamp is the
  core; several are decided by D-37 (the record the communication is directly filed under, via `sprk_regardingrecordid`, then matter over
  project).
- One call site in `RuleGatedAssessedConsumer`, after `_gate.EvaluateAsync` and before `if (decision.Authorize)`. The gate class
  (`CommunicationRuleGate`, lines 72–~220) is untouched; the diff to the file is the consumer region, the two `using`s and a doc block.

### Criteria amendments / interpretations (C)

| # | POML text | Reading used |
|---|---|---|
| 4 | `sprk_gatesessionid` links the SessionGate entry | **Amended:** `sprk_gatesessionid` is null for enrichment-gate records (no SessionGate exists there); the communication-assessed correlation id is in `sprk_factsnapshot` (`correlationId`, with `communicationId`). |
| 5 | gate code unchanged | Holds: no change to `CommunicationRuleGate` or `ConfirmationPolicyEngine`; only the consumer wrapper and the writer changed. |
| 6 | write failure observable in App Insights | A refusal logs `DecisionRecordEvents.GateDecisionNotRecorded` (50312) at Error and the writer meters it under its own reason; an unclassified failure logs the exception and meters `ontology.writer.failures{reason=decision_gate_record_failed}`. Outcome and RI action are unchanged either way. Cancellation is not swallowed. |
| 7 | deny: `sprk_steps` lists the proposed action as not taken with outcome Denied | One step, `Taken=false`, outcome `Denied` (via `Plan.SkippedOutcome`, default `Skipped` so the review route is unchanged). |
| 9 | project-filed communication → `sprk_project` | Proven (`Gate_ACommunicationFiledUnderAProject_RecordsTheProjectAsCore_Criterion9`); `sprk_project` is the stamp the resolver returns. |

POML `outputs` path typo (fixed here only; the POML is not edited): the test output is
`tests/unit/Sprk.Bff.Api.Tests/Services/Communication` (singular), not `…/Communications`.

## uac-r2 read (secure family: the DR writer contract)

Read `origin/work/unified-access-control-r2` at **`f808329b76973781509f4bc620e1027954890292`** against our base `848f63913`.
Only additive / opt-in changes: `SecureChildReconciler` thread reconcile after a communication re-file; `SecureRootInheritance`
`absentIsUnknown` opt-in; `CoreAncestorResolver` service-request party lookups; `DataverseWebApiService` app-only read. Nothing
invalidates the plan. `merge-tree` of this branch with that head shows no conflict in any file this task touches (uac-r2 also edits
`RecordOwnerAssignmentCensusTests.cs`; it merges clean today — the census edit here is two strings).

**uac-r2 must review this PR.** The DR writer contract is secure family: a new entry point creates a `sprk_decisionrecord` row that is
owned through `IRecordOwnershipResolver` over the core, the communication and every derived core (secure-if-any, D-33/D-123). Ask:
the owner parents are core first, then the communication, then the stamps, with NO requester.

## Placement justification (CLAUDE.md §10/§11)

- **Placement:** inside the BFF, in the existing `DecisionRecordWriter` (Signals) and the existing `RuleGatedAssessedConsumer`
  (Communication). ADR-052: it is request-path work (the consumer runs inline on the communication-assessed event); no new background work.
- **Existing / Extension / Cost of doing nothing (§11):** *Existing* — `DecisionRecordWriter.WriteAsync`; *Extension* — extended by an
  additive entry point sharing one create (a copy would fork the filing/owner/refusal rules; widening `WriteAsync` would change the
  commit route's contract); *Cost of doing nothing* — FR-23/criterion 3: a deny leaves no record, so nothing answers why a communication
  produced no action.
- **New surface:** one public method, one public record (`GateDecisionRecordRequest`), one refusal reason, one EventId — no endpoint, no DI
  registration (the consumer's new ctor parameter is optional and `DecisionRecordWriter` is already registered unconditionally in
  `SignalsModule`), no package, no Dataverse column, no AI call.
- **Home (bff-hygiene item 6):** not applicable — no config field is added.
- **No new CRUD→AI dependency.**

## BFF hygiene evidence (NFR-01)

Command `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent
linux-x64, Release, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included**; each from a fresh
worktree at a short path (`C:\pm`, `C:\pbase`, `C:\pbr`), same tool, file counts compared (192 = 192 = 192).

| Tree | Files | Raw MB | Zip MB |
|---|---|---|---|
| fresh `origin/master` (`8a1f3bb11`) | 192 | 122.59 | 36.648 |
| branch base `bcd5824a8` | 192 | 123.16 | 36.854 |
| branch head `8061107c5` | 192 | 123.18 | 36.860 |

Delta vs fresh master **+0.212 MB** (almost all other projects' merges already on the ontology branch); delta attributable to this task
vs its base **+0.006 MB**. Under every threshold (≥ +5 MB / ≥ 55 MB / ≥ 60 MB). No csproj/props change; `dotnet list package --vulnerable
--include-transitive` on the BFF: no vulnerable packages. BFF Hygiene §10 + NFR-01 verified.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings, 0 errors.
- Filtered run (`FullyQualifiedName~DecisionRecord|~DecisionCommit|~RuleGated|~RiActionsViaSeam|~SignalEvaluationTriggerHandler|~CommunicationRuleGate|~CoreAncestor|~SignalWriter`): 604 passed, 0 failed.
  New: 19 writer gate tests (`DecisionRecordWriterTests.GateDecision.cs`), 9 consumer tests, 4 seam tests (criterion 3 included).
- ArchTests (census + immutability): 41 passed. The 142 guards are not weakened: the existing ones stand and a new one pins exactly one
  `_writerClient.CreateAsync`, located in `CreateAsync`, reached by `WriteAsync` and `WriteGateDecisionAsync` (must-fire controls: a second
  create in the gate entry, an entry that never reaches the shared create, a third creating member, a renamed entry point).
- Mutation checks (each reverted after; all caught): deny class derived as Dismissal; deny keeps the action taken; confirmedby always
  written; deny step outcome not `Denied`; D-37 direct rule dropped; record written after the branch; a writer refusal escaping the
  consumer; an unclassified failure not metered; the communication dropped from the owner parents; the gate not asking as the content of the communication (review finding 1).
- Criterion 3: `PublishAsync_ForOneMatter_BothOutcomesAreQueryablePerMatter_Criterion3` runs a FetchXML per matter over both outcomes
  and returns both records.
- Live seam test (`DecisionRecordWriterSeamTests.WriteGateDecisionAsync_BothOutcomes_AsTheWriter…`) compiles and is opt-in; it was NOT run
  (no dev data writes in this task). It should run once on the post-deploy gate (task 146): it writes two rows prefixed `zz-042-seam` and
  sweeps them.

## Review finding 1 (adversarial review, fixed fail-closed; owner decision filed)

The review found that a gate record about an UNFILED communication was owned by the business unit's default team (Basic User = Read on
`sprk_decisionrecord`), so every member of that business unit could read a decision about a communication whose privacy rests on
per-participant shares (E1, owner round 10 item 8). **Fixed** in `FiledAndOwnedAsync`: the gate asks as the content of the communication
(`KeepCreatorUnlessTargetIsTeamOwned = true`; the review route does not, pinned by an assertion in `A142_…`). An unfiled communication now
answers Unchanged, which the writer refuses: nothing is written, the refusal is logged (50312) and metered, and the gate outcome and the RI
action are unchanged. A filed communication that is user-owned is now judged by its own filing (secure-if-any), which is also stricter.
Cost: no Decision Record for an unfiled communication (it has no matter, so criterion 3 is unaffected). **Owner decision DI-84
([#1876](https://github.com/spaarke-dev/spaarke/issues/1876))**: keep this, give such records a writer-only owner, or accept
business-unit visibility. The other review findings: F3 (a test name claimed the owner is not asked) renamed; K2 filed as DI-85
([#1877](https://github.com/spaarke-dev/spaarke/issues/1877)).

## Known limits

1. **An undecidable D-37 tie is fail-soft.** Two matters, or two projects, or a pair that neither the directly-filed-under record nor
   matter-over-project decides: the record is written with no core pair (and a D-37 warning) rather than refused. It is still stamped with
   every core the communication names and owned secure-if-any over all of them, so no row is under-protected and the per-matter query
   still finds it through the stamp. `SignalWriter` refuses in that case; the two picks are duplicated (DI-85).
2. **No record for an unfiled communication** (finding 1, DI-84).
3. **A missing/unreadable core record type** gives a warning and no core pair (missing row), or a refusal `FilingFailed` (faulting read).
4. **`sprk_factsnapshot` carries rule id(s), priority, confidence/threshold and the correlation id; never message content.** The priority
   read is best effort: on failure it is omitted and the record is still written.
5. **The record is written before the gate outcome is acted on** (FR-23), so the RI action starts after about 8-12 sequential Dataverse
   calls on authorize, and a hung call delays it until the client timeout. A writer failure is logged and metered and never blocks the action.
6. **Live schema write path unproven here** (see Verification: the live seam test was not run).

## adr-check

ADR-002 (no plugins, one server-side owner per invariant, security fails closed): met; the invariant stays in the writer, the owner stays
the resolver's answer. ADR-010 (DI minimalism): no new registration; optional ctor parameter filled by the existing singleton. ADR-001/008:
no endpoint. ADR-013/CRUD-to-AI: no AI call. ADR-015: no message content in the snapshot, reason or logs. ADR-038: new behaviour has tests that
fail without it (mutation checks above). No conflict, so §6.5 does not apply.
