# Task 024 — SPE permission paging (M1) + `/revoke` status parity (M2)

> **Closed 2026-09-09** for the **M1 half**. **M2 is deliberately NOT done** — it is moved to land with
> the client fix (task 065), per the design's §4.3 recommendation. See §5 below.
> Design: [`notes/decisions/spe-paging-and-revoke-honesty-design.md`](decisions/spe-paging-and-revoke-honesty-design.md).

---

## 1. 🔴 Step 0 spike — the finding is real as a DEFECT, unproven as an EXPLOIT

The design's §0 was right to insist on this, and the answer **re-ranks the finding**.

| Claim | Verdict |
|---|---|
| The code issues a single `.GetAsync()` and never follows `@odata.nextLink` | ✅ **VERIFIED** — 3 call sites |
| The Graph SDK models paging here (`PermissionCollectionResponse : BaseCollectionPaginationCountResponse`, which declares `OdataNextLink`); `PageIterator<,>` is available in Microsoft.Graph.Core 4.0.1 | ✅ VERIFIED — but **weak evidence**: *every* Graph collection response inherits that base regardless of whether the service pages |
| The SPE permissions endpoint actually **emits** `@odata.nextLink` | ❌ **NOT ESTABLISHED**, and the documentary evidence leans **against** it |
| Live probe | ❌ **403** — `az account get-access-token --resource https://graph.microsoft.com` yields the Azure CLI's delegated token, which lacks `FileStorageContainer.Selected` and the container-type-level grant. Needs the BFF's own app identity → **operator step, filed to task 047** |

**The documentary evidence.** [List fileStorageContainer permissions](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-list-permissions)
documents support for **`$skip`, `$top`, `$orderBy`, `$filter`** — and **not `$skiptoken`**. The sample
response carries `@odata.context` and **no `@odata.nextLink`**. Server-driven paging in Graph *is*
`@odata.nextLink` carrying a `$skiptoken`; the documented parameter set is the client-driven shape.

### So why fix it anyway

Not "it might page". The stronger and actually correct reason:

> **Ignoring `@odata.nextLink` is an OData protocol violation independent of current server behaviour.**
> A service may begin server-driven paging at any time, and doing so is explicitly **not** a breaking
> change — that is the point of the mechanism. Correctness here is against the protocol, not against one
> observed response.

**Severity re-ranked**: from *"live false-assurance defect"* to *"latent protocol defect + a real honesty
defect"*. The honesty half (§3) is worth having on its own merits and is **not** contingent on paging.

### 🔴 How to CLOSE this question — and it is cheaper than the design assumed

`scripts/Test-SpeContainerPermissionPaging.ps1` (added by this task, read-only) answers it in one
command. **Record the verdict here when it runs.**

The design assumed settling this needed *"a dev container seeded past one page"*, which is what made it
look expensive — and possibly unachievable, since we do not know the page size. It does not.
**`$top` is documented as supported on this endpoint**, and capping the page below the total is exactly
the condition that produces a `nextLink` *if the service does server-driven paging at all*. So any
container with **≥ 2 permissions** settles it.

The only real prerequisite is a Graph token from an app holding `FileStorageContainer.Selected` plus the
container-type grant — **the BFF's own registration already has both**. An `az` CLI token does not and
403s.

Three possible verdicts, each with its consequence already worked out:

| Result | What it means |
|---|---|
| Unmodified GET returns a `nextLink` | M1 was a **live** defect; 047 should assert multi-page enumeration |
| Only `$top=1` returns a `nextLink` | The service **does** page; today's containers are just small. M1 is genuinely latent and goes live as containers grow — the fix is load-bearing |
| Neither returns one | **No evidence of paging.** Consistent with the docs. The fix stays as protocol compliance, is re-ranked defensive, and **047 need not assert live paging at all** |

---

## 2. 🔴 The design named the wrong precedent — there is a better one, in-repo, on this exact endpoint

The design (correcting an earlier claim about `DriveItemOperations.cs:392`) named
**`PrivilegeGroupResolver.cs:202`** as the precedent, warning it double-counts page 1 (ISS-001).

**Both were superseded by what is actually in the repo.** `SpeAdminGraphService.ListContainerPermissionsAsync`
([`SpeAdminGraphService.cs:3097`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs))
**already pages this exact collection correctly** — a `while` loop following `response.OdataNextLink` via
`.WithUrl(...)`. It has no double-count bug.

Two consequences:

1. **The repo was internally inconsistent.** SpeAdmin paged container permissions; ExternalAccess did
   not. Same collection, two behaviours. That inconsistency is itself weak corroboration that someone
   who worked this endpoint thought paging was needed.
2. **Deviation from POML step 1 and design §2.2**, both of which say "use `PageIterator`". I used the
   `while` + `WithUrl` shape instead. Reasons: it is the proven in-repo precedent *for this collection*;
   it has no ISS-001 hazard to avoid replicating; and a **page bound** is far more directly expressible
   in a plain loop than through an iterator callback. `<steps mode="directional">` permits this; recorded
   here as required.

---

## 3. The honesty rule — the part that matters regardless of paging

`SpeContainerRevokeOutcome.NoPermissionFound` was *already* documented as *"read **successfully** … this
Contact holds none. **Genuinely absent**"*. Under a single-page read that sentence was **false**. **No
enum member was added** — the vocabulary was already correct and the implementation was lying about it.

`ClassifyRevokeResult(enumerationComplete, deletedPermissionId, contactEmail)` is the rule as a pure,
exhaustive function:

| Enumeration | Target found | Outcome |
|---|---|---|
| complete | yes | success |
| complete | no | benign absence (`NoPermissionFound`) |
| **INCOMPLETE** | yes | success — **the deletion is a fact**; an unread tail cannot retract it |
| **INCOMPLETE** | no | 🔴 **failure** — never a benign absence |

Same rule for the closure guard, via `SpeBulkRemovalResult.IsComplete => Failed == 0 && EnumerationComplete`.
`Failed == 0` alone was a false clean: **members nobody enumerated cannot fail to be removed.**
`ProjectClosureEndpoint` maps `IsComplete` onto `container_not_cleared` — so an incomplete read surfaces
there with **zero endpoint change**.

**One deliberate asymmetry.** On an incomplete read, `RemoveAllExternalMembersAsync` still removes
everyone it *did* see, then reports not-cleared; `ListExternalMembersAsync` **throws**. The bare-list
signature cannot say "here are SOME of them", and a short list reads to a caller as the whole truth —
that is the task-016 defect one layer up. Bulk removal aborting, meanwhile, would leave strictly *more*
access in place, which is exactly the reasoning tasks 016/017 used for not aborting on a per-member
failure.

---

## 4. Tests — the fake `IRequestAdapter` upgraded design §5's layer 3 from "live-gated" to "always runs"

The design's §5 assumed no offline test could prove the loop (`PageIterator` needs a real
`GraphServiceClient`, and `Mock<HttpMessageHandler>` is banned by ADR-038 B1), so layer 3 was to be a
`SkippableFact` gated on a seeded dev container.

**That assumption was wrong, usefully.** `GraphServiceClient` is constructed from a Kiota
**`IRequestAdapter`** — 9 members — which is the **SDK's own published abstraction boundary**, not HTTP
transport. Substituting it drives the *real* `PermissionsRequestBuilder` chain, the *real* `OdataNextLink`
plumbing and our *real* loop, with no HTTP anywhere. That is a module boundary, and B1 does not reach it.

`SpeContainerPagingTests` (14 tests) therefore proves behaviourally, offline:
- a member on **page 2** is seen (list) and matched (revoke);
- **three** pages are followed — following one link ≠ enumerating a collection;
- a single-page container issues **exactly one** request (paging is not an excuse to re-read);
- the follow-up request uses the **server-supplied** nextLink, not a synthesised skip token;
- the incomplete cases produce failure, never the absence text.

Plus **layer 2**, the structural guard (`ExternalAccessQueryIntegrityGuardTests`, seam 5): the paged
reader must exist, must itself contain the fetch (non-vacuity), and must be the **only** place a
permission collection is read. That is what stops a *future* call site going around it — which no
behavioural test can do.

### Perturbations (POML step 5 — mandatory)

| # | Break | Tests failed |
|---|---|---|
| 1 | Return only page 1 (`\|\| pagesRead >= 1`) | **8** |
| 2 | Re-swallow: incomplete+not-found emits the `NoPermissionFound` text | **2** |
| 3 | Bypass the reader — bare `.Permissions.GetAsync()` in `ReadExternalMembersAsync` | **7** (1 ArchTest + 6 behavioural — both layers caught it independently) |
| 4 | `IsComplete => Failed == 0` (drop the enumeration conjunct) | **2** |

None failed zero. Perturbation 1 was reshaped from `if (true)` — which is unreachable-code (CS0162)
under `--warnaserror` — to a compiling predicate, per the standing rule.

> ⚠️ **Process note, my error.** I ran perturbation 1 against an **uncommitted** file and reverted with
> `git checkout <file>`, which discarded the whole task's work, not the perturbation. Fully recovered
> (edits were exact), and the workflow is now: **commit first, then perturb, then `git checkout` is
> safe.** Worth institutionalising — the perturbation protocol should say "commit before you break it".

---

## 5. ❌ NOT DONE, deliberately: M2 (`/revoke` status parity)

`/revoke` returns **200** with the failure in the body; `/close-project` returns **500** +
`sdap.closure.incomplete.container_not_cleared` for the identical shape. Aligning them is a two-line
change and I did **not** make it.

**Why.** `AccessGrantModal.postJson` (`AccessGrantModal.tsx:327-335`) is
`return (await res.json()) as T` with **no `res.ok` check**. Changing 200→500 while the client ignores
the status trades one silent wrong answer for another — and likely a runtime error, since ProblemDetails
cast to `RevokeAccessResponse` yields `undefined` fields. M2's entire value is unlocked by the **M8**
client fix, which is filed against **task 065**.

This is the design's §4.3 recommendation and the POML's own `<escalation>` trigger ("coordinate — the
client may need to land first"). **Task 065 must ship the `res.ok` check and the status change together.**
Acceptance criterion 3 is therefore **open**, not met — recorded rather than quietly dropped.

---

## 6. Deferred, with reasons

| Item | Why not here |
|---|---|
| ~~**Task 020's N+1**~~ | ✅ **DONE — see §8.** Deferred first, then closed the same day at owner direction. |
| **ISS-001** — `PrivilegeGroupResolver.cs:202` double-counts page 1 | Pre-existing, different subsystem, already filed. Not 024's. |
| **Eventual consistency** — a deleted SPE permission may stay observable briefly | Not fixable by paging. Task 047. |
| **Alias / proxy addresses** — the match is case-insensitive but not alias-aware | Not fixable by paging. Task 047. |
| **Live confirmation that this endpoint pages** | Needs the BFF app identity. Task 047. |

---

## 7. Placement justification (CLAUDE.md §10)

**Modification only — no new component, no new DI registration, no new package.** `ReadPermissionsAsync`
is a private method that *removes* a duplicated read from two call sites; `PermissionReadResult` and
`ClassifyRevokeResult` are `internal` members of the existing service. §11's three questions do not fire
(the rule applies to NEW surface). The one contract change is a third parameter on `SpeBulkRemovalResult`,
defaulted so existing construction keeps its meaning.

---

## 8. ISS-004 (#968) closed — the org-revoke N+1, converged

Deferred in §6 on regression risk, then closed the same day at owner direction (*"we need to fix all of
these"*). Recorded here rather than rewritten above, so the sequence stays visible.

**The problem, restated.** `RevokeExternalAccessEndpoint`'s organization sweep called
`RevokeMembershipAsync` once per member, and each call read the container's ENTIRE permission
collection. N members = N full reads — and **task 024's own paging made that N × pages**. The fix I had
deferred became more justified because of the fix I shipped.

**The convergence.** New `SpeContainerMembershipService.RemoveMembershipsAsync(containerId, emails, ct)`:
one paged read → match every email locally → delete by permission id. It returns one
`SpeContainerMembershipResult` **per distinct email, keyed case-insensitively** — deliberately the same
shape `RevokeMembershipAsync` returns, so the endpoint's existing classification (`Success` /
`NoPermissionFound` prefix / other) works **unchanged**. The blast radius stays inside the loop.

The endpoint now runs in two phases: resolve every member's email (Dataverse), then ONE Graph call.

**What was preserved, and why each matters:**

| Invariant | Source |
|---|---|
| A per-member delete failure never aborts the sweep | tasks 016/017 — stopping early leaves strictly MORE access in place |
| A READ failure marks **every** member failed, and nobody absent | nobody can be confirmed absent off a read that did not happen |
| An incomplete enumeration flows through `ClassifyRevokeResult` | an unseen member is never reported "genuinely absent" (§3) |
| `RevokeMembershipAsync` untouched | the per-CONTACT revoke path still uses it |
| ⚠️ **NOT** `RemoveAllExternalMembersAsync` | it removes EVERY external member, not the target org's — task 020's warning, still binding |

**One behaviour change, stated rather than buried.** Results are keyed by EMAIL, so two contact rows
sharing one address both report the outcome of the single permission that address owns. Previously the
second call found it already deleted and reported `NoPermissionFound` — which reads as *"this person
never had access"*. Keying by identity is the more truthful of the two, but it is a change.

**Perturbation**: re-reading the container inside the per-member loop (restoring the N+1) fails **3**
tests, one asserting the read count directly (`GetRequestCount` counts collection GETs only, so member
deletes cannot mask it).

### Two fixture gaps this exposed — both the same shape

Changing which seam the endpoint uses broke **8** of task 020's org-revoke tests, because they
substitute `RevokeMembershipAsync`. Both doubles (`SpeMemberStub`, `SpeServiceStub`) needed the new
method modelled. `SpeServiceStub`'s omission surfaced as a **`NullReferenceException` from the
endpoint** — an un-stubbed virtual returns a null `Task`, so a fixture gap presents as a product defect.
Worth remembering: that stack trace points at `RemoveOrganizationMembersSpePermissionsAsync`, not at the
test. Per CLAUDE.md §10 F.2 (Fixture-Config-FIRST), the fixture is the first place to look.

`CapturedEmails` still receives one entry per member in order, so **every existing assertion about
WHICH KEY was used — the whole of finding A-13 — is unchanged.** Extended, not restructured, per the
POML's task-020 constraint.

### Process note

I perturbed this change against a **file backup**, not `git checkout` — after the earlier revert in this
same task discarded uncommitted work. The rule stands: commit or copy before you break it.
