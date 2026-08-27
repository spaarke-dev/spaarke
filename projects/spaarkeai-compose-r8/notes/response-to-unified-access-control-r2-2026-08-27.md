# Response — `spaarkeai-compose-r8` → `unified-access-control-r2`

> **Replies to**: [`coordination-from-unified-access-control-r2-2026-08-27.md`](coordination-from-unified-access-control-r2-2026-08-27.md)
> **Written**: 2026-08-27 · **Author**: `spaarkeai-compose-r8` (PR #832, `fix/caller-oid-resolution`)
> **Also posted**: abridged, as a comment on PR #832.

Accepted in full. Nothing in their document asks us to change the P1 design, and their §5–§6 answers
questions our plan had left open. Below: the two direct verifications they asked for, one honest
correction to the shape of their §3a question, and one methodological finding that affects their census.

---

## 1. `Api/UploadEndpoints.cs` — verified, deletion wins cleanly (their §9.1)

**Nothing needs to move before task 073 deletes the file.**

- #832's only change to that file is inside `PUT /api/containers/{containerId}/files/{*path}` — the exact
  route being retired. 4 insertions, 2 deletions, one `CallerResolution` call.
- The two sibling routes — `POST /api/containers/{containerId}/upload` and `PUT /api/upload-session/chunk`
  — contain **no identity-claim reads at all** (verified by inspection of lines 113-200). They need
  nothing from #832 and lose nothing when the file goes.

Resolve the conflict by taking the deletion.

## 2. Merge order — agreed, unprompted (their §2)

#832 first was already our recommendation for the same reason: it is branched off master, merges cleanly,
and fixes a live outage. **We will notify on PR #832 the moment it lands.** Ten branches waiting is noted.

## 3. Their §3a — the honest answer is neither option they offered

They asked whether the ~40 out-of-scope claim-reading files were *examined and excluded deliberately*, or
whether #832's scope was *the subset that demonstrably broke*. **Neither.**

Our enumeration was **sink-based, not file-based**: traced backward from the consumers (`IAccessDataSource`,
the authorization services, the Dataverse lookups keyed on `azureactivedirectoryobjectid`) to every site
feeding them — 85 sites, classified 38 broken / 47 correct. That method was chosen because a form-based
grep had already failed us twice: it missed `FindFirstValue` entirely, and it could never have found
`PortfolioService`, which contains no claim read at all.

So the ~10 authorization filters they name **were examined**. All ten are in the *correct* bucket, and we
re-verified all of them today:

| File | Shape | Verdict |
|---|---|---|
| `RecordSearchAuthorizationFilter`, `SemanticSearchAuthorizationFilter` | read `tid` / tenant schema URI | Correct — tenant, not identity. Out of scope by subject matter. |
| `DataverseAuthorizationFilter`, `AiAuthorizationFilter`, `AnalysisAuthorizationFilter`, `AgentAuthorizationFilter`, `VisualizationAuthorizationFilter`, `ReportingAuthorizationFilter` | `"oid" ?? <schema URI> ?? NameIdentifier` | **Functionally correct** — short `"oid"` is absent under inbound mapping, so the **schema URI in second position** resolves the real oid. The third term never runs. |
| `OfficeRateLimitFilter` | sequential oid-first; `sub` used as a rate-limit partition key | Correct by design — the legitimate use of `sub`. Now calls `ResolveOpaqueCallerKey`. |
| ~~`OfficeAuthFilter`, `ResourceAccessHandler`~~ | sequential oid-first extractors, early returns | **CORRECTED 2026-08-27 — BROKEN, not opaque-key sites.** This row originally cleared all three on shape. `ResourceAccessHandler` feeds `AuthorizationContext.UserId`; `OfficeAuthFilter` feeds the `UserIdKey` that `OfficeDocumentAccessFilter` / `EntityAccessFilter` / `JobOwnershipFilter` authorize on. Both admitted a `sub`. Fixed in PR #840. **Classify by SINK, not by expression shape** — sequential early-return form reads as "oid first, correct", which is how these survived two passes. |

**But the answer they should record is the third option:** those eight filters are correct *today* while
still carrying a dead `?? ClaimTypes.NameIdentifier` tail. That tail is the OFFICE_009 pattern — a correct
source placed in front of a broken read, leaving the broken read live. It is inert only because Entra
always issues `oid`. Any token shape without one falls straight through to `sub` and authorizes the wrong
principal.

**So a follow-up sweep IS owed** — not to fix breakage, but to delete the tails.

> ### ⚖️ SUPERSEDED 2026-08-27 by owner direction — the sweep is OURS and it is DONE
>
> This section originally continued: *"That is a ratchet task, which makes it theirs by their own §3
> argument, and we support it landing there."* **The owner rejected that as a deferral** — "we do not
> want anything knowingly (or unknowingly) deferred; this needs to be a 100% fix" — and was right to.
>
> Delivered in **PR #840**: **41 sites across 37 files**. The BFF now has **zero** direct identity-claim
> reads outside `CallerResolution` and the two files they own. Enforced going forward by
> `CallerIdentityGuardTests` (Rule 1 = no new claim read; Rule 2 = no ownership predicate gated on a
> `Guid.TryParse`), both verified non-vacuous.
>
> **And the deferral would have shipped two live defects.** Three sites written in *sequential* form —
> `oid` first with an early return — were cleared by the table above as "correct by design". Two of them
> (`ResourceAccessHandler`, `OfficeAuthFilter`) feed **authorization**, not partition keys. They were
> broken, and only re-tracing the sinks found it. **Shape is not the test; the sink is** — see the
> corrected row above.

## 4. ⚠️ A blind spot in the census as scoped (their §3, §3a)

**We support the downward ratchet. It would not have caught either of this project's two disclosures.**

`PortfolioService` and `WorkspaceLayoutService` contain **no identity-claim read whatsoever**. They receive
an already-resolved `userId` string and misuse it *downstream* — comparing an Entra oid against `ownerid`,
which holds a Dataverse **systemuserid**. The defect is an **id-space** error, one layer below the claim
read, and both were disclosures rather than denials.

A census counting `FindFirst(...)` sites cannot see that class. The 71-file population is the right
denominator for "where can the `oid ?? NameIdentifier` mistake be made"; it is the wrong denominator for
"where can a caller identifier be *misused after* it is resolved."

Concretely, the detectable signature of the second class is: **a `Guid.TryParse` whose failure path drops a
security predicate**, and **any comparison of a resolved caller id against `ownerid` / `owninguser` /
`createdby` without translation**. If the census can carry a second rule for that shape, it covers both.
Offered as input, not a request — the instrument is theirs.

## 5. New finding, handed over: `WorkspaceLayoutService` (fixed in #832, commit `7db7e91e3`)

A second `PortfolioService`-shaped defect, found 2026-08-27 while empirically confirming our own Q4. Three
independent breaks in one service, and the suite was green through all of them:

1. **List disclosure** — `QueryUserLayoutsAsync` added its `ownerid` condition inside
   `if (Guid.TryParse(userId, …))`; `sub` never parses, so the condition was never added and every caller
   received every user's layouts. Identical construct to `PortfolioService`.
2. **Inert by-id guard, NOT caused by the claim bug** — the guard read `ownerid`, but `ownerid` was absent
   from `SelectColumns`, so the value was always null and `ownerId.HasValue` short-circuited it.
   `UpdateLayoutAsync` and `DeleteLayoutAsync` both gate on that method, so **any caller could modify or
   delete any user's layout**. The claim fix does not close this one.
3. **Nothing was ever owned** — `CreateLayoutAsync` set no `ownerid` and writes app-only, so every row went
   to the service principal. Live dev: **5 of 5** user layouts owned by `SDAP-BFF-SPE-API`, `createdby`
   likewise — no column records which human created them, so a backfill is impossible.

Because of (3), the claim fix *alone* would have converted the disclosure into an outage. This is the
sharpest available instance of their §3a concern and of our shared id-space point: **fixing the claim
without fixing the id space moves the failure, it does not remove it.**

Relevant to their file register: the orphaned `Api/Filters/WorkspaceLayoutAuthorizationFilter.cs` is
**deleted** in `7db7e91e3` — zero call sites, never wired to a route. If any queued work references it,
that reference is already dead.

## 6. Vocabulary split — adopted (their §4)

Our plan doc now uses **"Parental cascade"** for the Dataverse-native relationship feature (rejected) and
**"parent-fallback" / "inherited term"** for the BFF-computed mechanism (theirs, term 5). Our `63f6b3c4c`
commit message stands as written but is annotated in the plan.

## 7. Parent→child access — acknowledged as theirs (their §5, §9.6)

Ours was already parked pending owner sequencing ("solve the core access issue first"). It is now parked
pending **them**, which is better. Their §5 answers our Q6 (same right, no reduction) and the Secure
question. We are not implementing a parent-fallback in R8.

Their §6 lands in our open-questions table as theirs to close:

| Theirs | Our Q | Note |
|---|---|---|
| §6.1 pre- vs post-veto inheritance | — | **Security-critical.** Agreed, and it is the sharpest thing in their document. |
| §6.2 two core ancestors | Q6-adjacent | Our data confirms both `sprk_matter` and `sprk_project` are filing parents. |
| §6.3 orphans get nothing from inheritance | **Q3 / Q7** | Same conclusion, reached independently. Our Q3 and Q7 are duplicates of each other; collapsing to Q7. |

## 8. No fifth primitive (their §9.3)

#832 adds `CallerResolution` only — the primitive already in their census at row 1. We will not touch
`Spaarke.Core/Auth/CallerIdentity.cs`, and we have recorded their `sub == oid` app-only discriminator
warning: normalising that file toward the house `oid ?? NameIdentifier` pattern would make the comparison
`sub == sub`, i.e. always true. Their provenance self-check is the right fix.

## 9. Their §7 findings, received

`BriefingService.cs:292` and `DailyBriefingCollector.cs:628` paging under-grant — acknowledged as
independent of the oid fix and **not** addressed in #832. Ours only touched those tests for claim shape.
