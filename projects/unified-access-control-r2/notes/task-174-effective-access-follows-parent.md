# Task 174: enforcement uses the parent-derived (effective) access

Owner round 84 (2026-10-08, binding): "Child access should always follow parent; if parent changes, then child changes."
Closes #1442 (cancellation follows the parent) and #1425 (the contact plane honours a secure parent's No Access list).
Carries task 067's verifier amendment (the Manage Access modal marks "No effect" from the effective values). Task 175 owns
the STORED cascade and the UI lock; this task makes ENFORCEMENT right meanwhile and whenever a stored flag lags.

Branch `task/uac-r2-174`, from `origin/master` `65177354f` (#1419 merged). Task 173's `ParentLineage` (PR #1458) was not on
master and does not fit: it walks the four CHILD tables (To Do, Event, Communication, Document) to their roots, while this
task needs the work assignment / project filing chain, which the POML binds to #1419's walk.

## The rule as built

- **Effective flags** of a `sprk_workassignment` or `sprk_project`: Secure if the record or ANY filing ancestor is secure;
  Access Permission the most restrictive (Restricted over Limited over Standard; empty = Standard) of the record and every
  ancestor; the record's own `statecode` kept. `sprk_matter` files under nothing and is unchanged.
- **Most restrictive of own and ancestors**, as the POML states, not "the parent's value replaces the child's": until task
  175 locks the child, a child stricter than its parent stays stricter. Never widens.
- **Fail closed.** An ancestry the walk cannot decide (unreadable row / pair type / ancestor flag, an EMPTY ancestor flag,
  a chain past `MaxFilingDepth`) folds to `RootRecordFlags.Unreadable` (secure AND Restricted, unreadable marker): removed on
  the read path, refused as "could not be read" (503 / `read_failed`) at grant time.

## Mechanism (one walk, no second filing reader)

- `SecureRootInheritance`'s parent read (`ReadParentAsync` / `ReadParentsOfManyAsync`) now also selects
  `sprk_accesspermission`; `DecideNamedParents` records the strictest one met; the climb merges it across levels into
  `SecureParentsAnswer.StrictestPermission` (`FilingPermission`: the value and the record carrying it). Same queries.
- `EffectiveRootFlags` (new static helper, `Infrastructure/ExternalAccess`): `Fold` (own flags + ancestry), `ReadAncestryAsync`
  (the batched walk for many ids, the single-record walk for one, always at `MaxFilingDepth`; never throws a read fault),
  `FoldOneAsync` (single record, skipped when nothing can be stricter), `InheritedFrom` (display).
- `ExternalParticipationService.GetEffectiveRootRecordFlagsAsync` (own flag read + walk + fold) for every access caller
  outside the read path; `GetEffectiveRootAccessAsync` (one record, with `InheritedFrom`) for the 064 read. The service takes
  the app-only `IGenericEntityService` (registered unconditionally, GraphModule; the typed-client factory supplies it). A
  null reader fails closed.
- `AccessibleRecordSetService`: each composition walks ONCE (`EffectiveRootFlags.ReadAncestryAsync`) and uses the answer
  for both the effective flags (cancellation, Restricted, the systemuser-plane Restricted survivor) and the No Access
  veto's parent lists (`BuildSecureAncestryAsync`, the former `ReadSecureAncestryAsync` minus its own walk).

### Component justification (CLAUDE.md §11)

- `EffectiveRootFlags` — (1) overlaps the flag read (own row only) and the #1410 walk (secure parents only); (2) it is the
  fold of those two answers and nothing else, static because the read path folds a walk it already ran and the flag reader
  folds one it runs; no DI registration; (3) without it a filed child of a secure or Restricted parent keeps org-wide,
  standing and (Restricted) direct contact access until its own flag is set (#1442).
- `FilingPermission`, `SecureParentsAnswer.StrictestPermission` — (1) the walk already read the parent row; (2) extends the
  existing answer instead of a second reader; (3) without it Limited/Restricted cannot follow the parent.
- `EffectiveRootAccess`, `RecordNoAccessStatus.AccessPermission` / `.InheritedFrom`, `RecordAccessInheritedFrom`,
  `EffectiveAccessPermission` — (1) 064's `secure` signal; (2) additive fields on the existing response (no new endpoint, per
  goal 5); (3) without them Manage Access marks "No effect" from the child's own stale values (task 067's K4 becomes a defect).
- `GetEffectiveRootRecordFlagsAsync` / `GetEffectiveRootAccessAsync` — (1) `GetRootRecordFlagsAsync`; (2) they compose it
  (the virtual test seam keeps working); (3) every grant-time caller would otherwise need its own walk.

## Call-site inventory (step 1)

Grep: `GetRootRecordFlagsAsync`, `FlagsFrom`, `IsDirectOnly`, `IsRestricted`, `CheckGranteeNoAccessAsync`,
`ResolveDenyVetoAsync`.

| Call site | Purpose | Now |
|---|---|---|
| `AccessibleRecordSetService.ComposeForSystemUserAsync` (flags) | linked-contact grant cancellation, Restricted veto, external-flagged survivor, Q4 secure split | effective (shared walk) |
| `AccessibleRecordSetService.ComposeContactPlaneAsync` (flags) | CIAM + workforce contact cancellation, Restricted | effective (shared walk) |
| `ResolveDenyVetoAsync` (contact plane, read + write) | contact / organization walls | + every secure ancestor's list (#1425) |
| `CheckGranteeNoAccessAsync` (grant time; Grant ~789, materializer ~1026) | grantee wall | + secure ancestors, single-record walk |
| `ResolveSystemUserDenyVetoAsync` | systemuser plane walls | same walk, shared with the flags |
| `ExternalGrantLifecycle.EvaluateGrantPolicyAsync` (`/grant`, `/invite-and-grant`, `/invite`, grant core) | Restricted refusal, org grant on direct-only | effective |
| `ExternalGrantLifecycle.ReadContactHeldGrantsAsync` (contact-side ceiling, reconciliation job) | which rows the read path lets confer | effective |
| `InternalShareEndpoints.ShareAsync` (~343) | Restricted bar for external-flagged users | effective |
| `InternalShareEndpoints.ListAsync` (~1041) | Manage Access "External user — no access" marker | effective |
| `InternalShareEndpoints.LastReaderRefusalAsync` (~1103) | S5: someone can still open it in Dataverse | **own, on purpose**: follows stored ownership (a not-yet-secure child is BU-owned); removing a share never widens |
| `FileAccessEndpoints.ShareLinkProtectionRefusalAsync` | no sharing link on secure / Restricted | effective |
| `RecordNoAccessEndpoint` (064) | `secure` signal, user-wall in-force rule | effective + `accessPermission`, `inheritedFrom` |
| `NoAccessShareEnforcer` (entry's own object record) | NotSecure skip | effective Secure (single walk only when own flag false) |
| `SecureShareNoAccessGuard.CheckCoreAsync` (AsFlagged) / `CheckOwnAndAncestorsAsync` | Q4 own-list scope | effective Secure; a known secure ancestor asks the own list as secure, no second walk |
| `RestrictedExternalShareRemover.RemoveForRecordAsync` | external-flagged shares on Restricted | effective |
| `AssignedAccessMaterializer.ReadFlagsAsync` | Restricted / direct-only / external-flagged assignee rules | effective |
| `AssignedAccessMaterializer.ReadResidualTermsAsync` | mirrors the composition's gates | effective |
| `ExternalCallerContext.RootRecordFlags.IsDirectOnly` | the predicate | unchanged; fed effective flags |
| `FlagsFrom` | own-row mapping | unchanged |

No call site reads the flags for a NON-access purpose (escalation trigger 2 did not fire).

**Readers outside the grep set** that decide Restricted from the stored column directly: `ProvisionProjectEndpoint` (718,
3711: external-flagged creator rule), `SecureChildShareSynchronizer` (1026: child-share mirror), `SpeContainerMembershipSync`
(420), `OfficeEditAccessService` (235), `AssignedAccessStore` (913: the Restricted sweep's query). They follow the STORED
value, so task 175's cascade makes them right; until then a child of a Restricted parent keeps an external-flagged user's
MDA share, container membership and Office edit unless the record is visited by a routed path. Filed as #1478 for task 175.

## Display contract (goal 5)

- 064 (`GET /api/v1/records/{table}/{id}/no-access`, existing, Read-gated) now reports the EFFECTIVE `secure`, plus
  `accessPermission` and `inheritedFrom` (additive). Contract: `notes/phase4-access-report-contract.md`.
- `AccessGrantModal` already reads 064 on every open; it folds the answer in (`effectiveAccessState`): the STRICTER of the
  host's stored values and the server's effective ones, never less strict; `unknown` folds in as Limited; no answer keeps
  the host's values. Gate, banner (now naming the parent: "It follows the matter it is filed under: X.") and "No effect"
  marks all use it. No host (TrackingFieldTrio) change: avoids PR #1458's edits to `TrackingFieldTrio/index.ts`.
- Task 153's banner and indicator read 064's `secure`, so they show Secure for a child of a secure parent (round 84).
- Task 067's K4 ("cancellation follows the record's own flag") is closed.

## Decisions and interpretations

- **Q4 on a filed child's OWN list.** Round 82 reads Q4's "secure" as "the record or any filing ancestor". With effective
  flags a systemuser-subject entry on a not-yet-secure child of a secure parent now binds internal users on the read path,
  in the share guard, in the enforcer and in 064's `inForce` — consistently. #1419 had left that one case on the child's own
  flag; no existing test covered it (the Q4 test uses a non-secure parent and still passes).
- **The Assigned-To materializer** reads the EFFECTIVE flags for suggest-vs-share (A3), the Restricted and direct-only rules
  and the grant-to-share conversion; the **S5 keep follows the record's OWN stored flag** (main-session ruling on verifier
  F1-c), exactly as `/unshare-user` does: a not-yet-flagged child is still owned by its business unit, so the share of an
  ended assignment is revoked there.
- **Cost (goal 6) — main-session ruling: ACCEPTED as bounded.** A matter composition reads nothing extra (pinned). A parentless work assignment or project costs the
  walk's ONE batched row read per 200 rows (pinned), nothing else: whether a record has a parent cannot be known without
  reading its filing, and the POML forbids a second filing reader. The systemuser plane already walked every WA/project
  candidate (#1410), so it pays nothing new; the contact plane and grant time pay that row read (plus parent and
  ancestor-organization reads only when parents exist). Single-record callers skip the walk when the own flags are
  unreadable or already Secure AND Restricted. The POML's "no extra reads" is met in spirit: the one row read per 200 on
  the contact plane (a point read at grant time) is the walk itself, and a second filing reader is forbidden.

## Verifier pass 1 (main session, 2026-10-08) — fixed in this PR

- **F1-a: a non-flagged middle project's No Access list never reached what is filed under it.** WA -> P (not flagged; secure
  through M) -> secure M: a contact or user walled on P kept WA at read and grant time, the guard allowed it while the
  enforcer (treating P as secure) removed it — #1419's F2 loop. Fix: in the walls' climb (`maxDepth > 1`), every visited
  work assignment or project that sits below a secure ancestor counts as a secure parent too, decided over the decisions
  already read (`EffectiveOverDecisions`: a plain reachability walk per question, no memo, so a filing cycle cannot cache
  a partial answer; no extra query). The one-level callers (inheritance, the sharee rule) are unchanged. The read veto, the grantee check, the share guard and 064's coverage all read the same answer. Tests: contact
  plane (secure / non-secure matter), grant time, guard.
- **F1-b: an own-Limited record under a Restricted parent lost Limited.** `IsLimited = own.IsLimited || (!restricted &&
  inherited Limited)`. Test on the fold.
- **F1-c: the materializer kept an ended assignment's share on a not-yet-flagged child (S5 on the effective flag).** S5 now
  reads the record's own stored flag (`Run.OwnIsSecure`); the effective flags still drive suggest-vs-share and convert.
  `ExternalParticipationService.FoldEffectiveAsync` folds flags the caller already read, so the materializer reads its own
  row once. Test: the ended assignment is revoked.
- **F1-d: `inheritedFrom` disclosed a grandparent's id and name to Read-only callers.** It now names only the DIRECT filing
  parent through which the stricter value arrives (`SecureParentsAnswer.DirectParents`, each with the effective Secure and
  Access Permission arriving through it); never a record further up. Contract note updated; the modal banner already reads
  "It follows the {table} it is filed under: {name}", which is now always the direct parent. Test: Read-only caller,
  grandparent governance, the matter's id and name absent from the body.
- **F2: switched behaviours now covered** — the guard's Q4 own-list switch, the guard's own fold, the enforcer's fold for the
  entry's own record, the internal-user composition (an external-flagged user loses a child of a Restricted matter), the 064
  endpoint's effective mapping (endpoint level), the materializer's effective read (suggest, not share). Seeding proofs below.
- **K3/K4 done:** the batch fold treats an id with no walk answer as unreadable (fail closed). `GetEffectiveRootRecordFlagsAsync`
  folds only the ids it walked and keeps the rest as read.
- **Pre-existing defect fixed in scope:** `ExternalGrantLifecycle.ReadContactHeldGrantsAsync` did not count Restricted as
  direct-only, so a firm's organization-wide row capped (or kept alive) a contact-issued row on a Restricted record, where the
  read path lets no organization row confer. Restricted now counts as direct-only (not "nothing": the contact's own rows are
  kept, so the reconciliation job does not end a contact-issued row merely because the record is Restricted for now). Test in
  `ExternalAccessReconciliationTests`.
- **Two 064 tests updated to round 84:** a user wall over an organization only a not-yet-flagged child of a secure matter
  references is now IN FORCE (the child is secure through its parent), and `secure` reads `applies` for such a child.
- **Not in this PR (routed by the main session):** the create / re-file plan climbing only above secure parents (task 175 /
  #1478); the `/unshare-user` Restricted carve-out (new issue); `ExternalDataService` showing the own `sprk_issecure` (#1478).

## Tests

`tests/integration/data-mutation/ExternalAccess/EffectiveAccessFollowsParentTests.cs` (real service, real walk over the
in-memory world, real deny-list matching, real grant policy):

- read path: secure parent admits only named direct grants; Standard parent unchanged; Restricted parent confers nothing;
  through a non-secure project to a secure matter is Secure;
- contact plane: contact walled on the secure parent denied, unrelated wall no effect; organization wall via membership;
- fail closed: unreadable child filing, unreadable ancestor flag, unreadable ancestor organizations, deny-list fault;
  unrelated records stay where the existing posture allows;
- cost pins: matter composition reads no filing; parentless children cost one row read, results identical;
- grant time: walled contact Denied / unrelated Allowed / unreadable chain Unverifiable; contact grant under Restricted
  refused with the Restricted refusal; org grant under secure or Limited refused, Standard allowed; unreadable chain
  Unreadable;
- display: the effective access read names the governing DIRECT parent (never a grandparent, F1-d).

`InternalUserShareTests`: an external-flagged user's share on a child of a Restricted matter is refused 422
`user_not_internal`. Modal (`AccessGrantModal.noAccess.test.tsx`): cancellation inherited from a secure / Restricted parent,
never-less-strict, an older BFF's answer, and the parse/fold helpers. The default 064 body in that suite now says
`secure: doesNotApply` (its matter is not secure; the modal trusts the signal since this task).

## Verifier pass 2 (main session, 2026-10-08) — fixed in this PR

- **F1: the enforcer's organization-object branch was not folded.** `CoveredRecordsAsync` listed only records FLAGGED
  secure that reference the organization (`FindSecureRootsReferencingOrganizationAsync`), so a not-yet-flagged work
  assignment under a secure matter that references the walled organization kept the walled user's share — while 064, the
  read veto and the guard all said the entry was in force; `EnforceForRecordAsync` ("Update Access") had the same gap,
  permanent after a Refused or Failed inheritance. Fix: for `sprk_workassignment` and `sprk_project` the enforcer also lists
  the NOT-flagged records that reference the organization (`FindUnflaggedRootsReferencingOrganizationAsync`, same registry,
  cap and fault contract), folds them with ONE batched `EffectiveRootFlags.ReadAncestryAsync`, and covers those that are
  secure through their filing; an undecidable ancestry is `covered-records-unreadable` on that record (nothing removed on a
  guess). The `MaxCoveredRecords` cap holds. Tests: the probe (share removed) and the fault case.
- **F2a:** the common real case — flagged secure by inheritance, Access Permission still Standard, filed under a secure,
  Restricted matter — at grant time (Restricted refusal) and at `/share-user` (422 for an external-flagged user).
- **F2b:** two not-yet-flagged middle levels (WA -> P1 -> P2 -> secure M, wall on P2): read path, grant time, guard.
- **F4:** the climb's comment and this note said "memoised"; the walk has no memo (corrected).

### Seeding proofs (mutation checks, reverted)

- **Read-path cancellation:** the contact plane composed with its OWN flags (no fold) — 3 of the 20 new tests fail (secure
  parent, Restricted parent, through a non-secure project); the rest pass.
- **Grant-time contact check:** `CheckGranteeNoAccessAsync` passing no ancestry — 2 fail (walled on the secure parent,
  unreadable chain).
- **Verifier pass 1 (one per item; each restored after):**

  | Mutation | Fails |
  |---|---|
  | Guard: own list of a record with a secure ancestor asked on its own flag (`AsFlagged` switch off) | the guard's Q4 own-list test |
  | Guard: no fold in `CheckCoreAsync` | the guard's single-record fold test |
  | Enforcer: no fold for the entry's own record | `Enforce_AnEntryOnANotYetFlaggedChildOfASecureMatter_RemovesTheWalledShare` |
  | Systemuser plane composed on own flags | the external-flagged user / Restricted parent test |
  | 064 on own flags | 3 endpoint tests (grandparent governance, both round-84 in-force tests) |
  | Materializer on own flags | the suggest-not-share test |
  | F1-a: non-flagged middle ancestors not counted | 3 (read path, grant time, guard) |
  | F1-c: S5 on the effective flag | the ended-assignment revoke test |
  | Pass 2 F1 reverted (no not-flagged org-covered records) | both new enforcer tests |
  | M5: skip the walk whenever the record's own flag is Secure | the F2a grant-time and `/share-user` tests |
  | M6: F1-a's climb limited to the direct middle parent | the 3 F2b tests |

### Runs after verifier pass 2 (2026-10-08, final code)

- `tests/unit/Sprk.Bff.Api.Tests`: 18,815 passed, 0 failed, 54 skipped. Focused classes: 1,247 passed.
- `tests/Spaarke.ArchTests`: 811 passed.
- Publish: fresh master `65177354f` 127,317,868 bytes; this branch 127,359,656 bytes; **delta +41,788 bytes**.

### Runs after verifier pass 1 (2026-10-08, final code)

- `tests/unit/Sprk.Bff.Api.Tests`: 18,808 passed, 0 failed, 54 skipped. Focused classes (effective access, Assigned-To,
  enforcer, 064, reconciliation, guard, share, inheritance, accessible set, grantor ceiling, contact grants, contract):
  1,240 passed.
- `tests/Spaarke.ArchTests`: 811 passed. `tests/integration/Sprk.Bff.Api.IntegrationTests`: 87 passed, 4 skipped.
- Jest (`AccessGrantModal`, `TrackingFieldTrio`, `accessStatusBanner`): 287 of 288 on a loaded machine; the one
  (`AccessGrantModal.userShare`) passes 29/29 on rerun. `TrackingFieldTrio.emailMembers` still does not load in a fresh
  worktree (`@spaarke/sdap-client` not built). No client file changed in this round.
- Publish: fresh master `65177354f` 127,317,868 bytes; this branch 127,357,808 bytes; **delta +39,940 bytes**.

### Runs (2026-10-08, first cut)

- `tests/unit/Sprk.Bff.Api.Tests` (includes `tests/integration/{auth,contract,regression,seam,data-mutation}`): 18,770
  passed, 2 failed, 54 skipped, on the build before the new tests. The 2 (`CorsAndAuthTests.Cors_Preflight_AllowsConfiguredOrigin`,
  `DocumentEmailIdentityContractTests.ResolveEmail_WhenNoSavedCopyExists…`) were host timeouts ("client aborted the
  request") and pass on rerun. The new and touched classes: 144 passed.
- `tests/Spaarke.ArchTests`: 811 passed. `tests/integration/Sprk.Bff.Api.IntegrationTests`: 87 passed, 4 skipped.
- `Spaarke.UI.Components` jest, `AccessGrantModal` + `TrackingFieldTrio`: 199 passed; the `TrackingFieldTrio.emailMembers`
  suite does not load in a fresh worktree (`@spaarke/sdap-client` not built; unrelated to this change).
- Publish (first cut): +24,412 bytes. No package added.

## Known limits

- **K2: an older BFF answer in the modal.** 064 without `accessPermission` (a BFF before this deploy) keeps the host's
  values for the permission; the server still enforces. Fails closed only on the server.
- **K2: a 064 read that fails** leaves the modal on the host's stored values (as before this task); the server enforces
  the effective values, and the No Access section already shows its error state.
- **K4: the systemuser plane's ContactUnreadable branch** now also walks (it removes every candidate anyway): a few extra
  reads on a fault path, no behaviour change.
- **Not a limit, by design:** a parentless work assignment or project costs one batched row read on the contact plane and at
  grant time (goal 6 interpretation above).
