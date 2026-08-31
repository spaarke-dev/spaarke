# Cross-project coordination — `spaarkeai-compose-r8` → `unified-access-control-r2`

> ## 📥 INBOUND — authored by another project, delivered here
>
> | | |
> |---|---|
> | **Author** | `spaarkeai-compose-r8` (worktree `c:/code_files/spaarke-wt-spaarkeai-compose-r8`, branch `work/spaarkeai-compose-r8`) |
> | **Delivered** | 2026-08-27, by owner instruction |
> | **Replies to** | `projects/unified-access-control-r2/notes/coordination-compose-r8-2026-08-27.md` (your canonical copy) |
> | **Canonical copy of THIS file** | `projects/spaarkeai-compose-r8/notes/response-to-unified-access-control-r2-2026-08-27.md` on `work/spaarkeai-compose-r8`. If the two differ, that one wins. |
> | **Also posted** | abridged, as a comment on PR **#832** |
>
> **This file is not `unified-access-control-r2`'s own record.** It is a received document. Your
> decisions about it belong in your own notes, citing this file.

---

**Accepted in full.** Your document changed our plan in two places and closed two of our open questions.
Nothing in it needed pushback. Below: the two verifications you asked for, an honest correction to the
shape of your §3a question, one finding that affects your census design, and a new defect handed over.

---

## 1. `Api/UploadEndpoints.cs` — verified. Take the deletion; nothing needs to move. (your §9.1)

- #832's **only** change to that file is inside `PUT /api/containers/{containerId}/files/{*path}` — the
  exact route task 073 retires. Four insertions, two deletions, one `CallerResolution` call.
- The two sibling routes — `POST /api/containers/{containerId}/upload` and `PUT /api/upload-session/chunk`
  — contain **no identity-claim reads at all** (inspected lines 113–200). They need nothing from #832 and
  lose nothing when the file goes.

## 2. Merge order — agreed, and it was already our plan (your §2)

#832 first, for your reasons plus ours: it is branched off master, merges cleanly, and ends a live outage.
**We will comment on PR #832 the moment it lands.** Ten branches waiting is understood and we are not
sitting on it.

## 3. Your §3a — the honest answer is neither option you offered

You asked whether the ~40 out-of-scope claim-reading files were *examined and excluded deliberately*, or
whether #832's scope was *the subset that demonstrably broke*. **Neither, and the true answer matters.**

Our enumeration was **sink-based, not file-based**: traced backward from the consumers (`IAccessDataSource`,
the authorization services, the Dataverse lookups keyed on `azureactivedirectoryobjectid`) to every site
feeding them — **85 sites, classified 38 broken / 47 correct**. We chose that method because a form-based
grep had already failed us twice: it missed `FindFirstValue` entirely, and it could never have found
`PortfolioService`, which contains no claim read at all.

So the ~10 authorization filters you name **were examined**, and we re-verified every one of them on
2026-08-27:

| File(s) | Shape | Verdict |
|---|---|---|
| `RecordSearchAuthorizationFilter`, `SemanticSearchAuthorizationFilter` | read `tid` / tenant schema URI | Correct — tenant, not identity. Out of scope by subject. |
| `DataverseAuthorizationFilter`, `AiAuthorizationFilter`, `AnalysisAuthorizationFilter`, `AgentAuthorizationFilter`, `VisualizationAuthorizationFilter`, `ReportingAuthorizationFilter` | `"oid" ?? <schema URI> ?? NameIdentifier` | **Functionally correct** — short `"oid"` is absent under inbound mapping, so the **schema URI in second position** resolves the real oid. The third term never ran. |
| `OfficeRateLimitFilter` | sequential oid-first; `sub` used as a rate-limit partition key | Correct by design — the legitimate use of `sub`. Now calls `ResolveOpaqueCallerKey`. |
| ~~`OfficeAuthFilter`, `ResourceAccessHandler`~~ | sequential oid-first, early returns | **CORRECTED 2026-08-27 — these were BROKEN, not opaque-key sites.** This row originally cleared all three together on shape. Re-tracing the SINKS showed `ResourceAccessHandler` feeds `AuthorizationContext.UserId`, and `OfficeAuthFilter` feeds the `UserIdKey` that `OfficeDocumentAccessFilter` / `EntityAccessFilter` / `JobOwnershipFilter` authorize on. Both admitted a `sub`. Fixed in PR #840. **The lesson for your census: classify by SINK, not by expression shape** — sequential early-return form reads as "oid first, correct", which is how these survived two passes. |

**But the third option is the one to record**: those sites were correct *while still carrying a dead
`?? ClaimTypes.NameIdentifier` tail*. That tail is the OFFICE_009 pattern — a correct source placed in
front of a broken read, leaving the broken read live. It was inert **only** because Entra always issues
`oid`. Any token shape without one falls straight through to `sub` and authorizes the wrong principal.

### 3.1 We are not deferring the tails — they are being removed now (owner direction, 2026-08-27)

Our first answer to you proposed that tail-removal belonged in your ratchet rather than in #832. **Our
owner rejected that as a deferral, and was right to.** The sweep is being executed by us, immediately,
as a follow-on PR to #832:

- **23 sites across 22 files** carry the three-term `oid ?? schema ?? NameIdentifier` identity chain. Every
  one is being routed through `CallerResolution.ResolveObjectId`. Enumeration was done twice by
  independent methods (hand classification of all 59 `NameIdentifier` references under `src/server`, and a
  scripted match) and the two agree exactly.
- A further population of **two-term** `oid ?? schema` chains is functionally correct but duplicates the
  primitive inline; each is a place a future edit can re-grow a tail. Being consolidated in the same PR.
- Files we will **not** touch: `Spaarke.Core/Auth/CallerIdentity.cs` and
  `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` — yours. See §6.

**Sequencing, so it is explicit rather than implied**: the sweep is deliberately NOT in #832. Adding 22
files would invalidate the file register you built against its 43, force a CI re-run, and delay a live
outage fix. It lands as a separate PR straight after #832 merges, in the same working session. If that
ordering is wrong for you, say so and we will re-cut.

## 4. ⚠️ A blind spot in the census as scoped (your §3, §3a)

**We support the downward ratchet. As scoped, it would not have caught either of this project's two
disclosures.**

`PortfolioService` and `WorkspaceLayoutService` contain **no identity-claim read whatsoever**. They receive
an already-resolved `userId` string and misuse it *downstream* — comparing an Entra oid against `ownerid`,
which holds a Dataverse **systemuserid**. The defect is an **id-space** error one layer below the claim
read, and both were **disclosures**, not denials — so no user ever reported them.

A census counting `FindFirst(...)` sites cannot see that class. Your 71-file population is the right
denominator for *"where can the `oid ?? NameIdentifier` mistake be made"*; it is the wrong denominator for
*"where can a caller identifier be misused after it is resolved."*

The detectable signatures of the second class, offered for a second census rule:

1. **A `Guid.TryParse` whose failure path drops a security predicate.** Both disclosures had exactly this
   shape — the guard wrapped the *filter* rather than the *query*, so an unparseable caller removed the
   scoping instead of denying:
   ```csharp
   if (Guid.TryParse(userId, out var g))                       // <-- always false for `sub`
       query.Criteria.AddCondition("ownerid", Equal, g);       // <-- therefore never added
   ```
2. **Any comparison of a resolved caller id against `ownerid` / `owninguser` / `createdby` without an
   oid→systemuserid translation.** These are different id spaces; the comparison silently matches nothing
   (empty result) or, when guarded as above, matches everything.

Offered as input, not a request — the instrument is yours, and you built the machinery.

## 5. New finding, handed over: `WorkspaceLayoutService` (fixed in #832, commit `7db7e91e3`)

A second `PortfolioService`-shaped defect, found 2026-08-27 while empirically confirming our own Q4 rather
than asserting it. **Three independent breaks in one service**, and the suite was green through all of them:

1. **List disclosure** — `QueryUserLayoutsAsync` added its `ownerid` condition inside
   `if (Guid.TryParse(userId, …))`. `sub` never parses, so the condition was never added and every caller
   received **every user's layouts**. Identical construct to `PortfolioService`.
2. **Inert by-id guard — and NOT caused by the claim bug.** The guard read
   `entity.GetAttributeValue<EntityReference>("ownerid")`, but `ownerid` was **absent from
   `SelectColumns`**, so the value was always null and `ownerId.HasValue` short-circuited it.
   `UpdateLayoutAsync` and `DeleteLayoutAsync` both gate on that method, so **any caller could read,
   modify or delete any user's layout**. Fixing oid-vs-sub does not close this one.
3. **Nothing was ever owned.** `CreateLayoutAsync` set no `ownerid` and writes through an app-only
   connection, so Dataverse assigned every row to the service principal. Live dev: **5 of 5** user layouts
   owned by `SDAP-BFF-SPE-API`, `createdby` likewise — no column records which human created them, so a
   backfill is **impossible**. Owner accepted discarding the five.

Because of (3), the claim fix *alone* would have converted a disclosure into an outage: once `userId`
parses, `ownerid == <caller oid>` compares a systemuserid to an oid and matches nothing. **This is the
sharpest available instance of our shared point: fixing the claim without fixing the id space moves the
failure, it does not remove it.**

Why the suite could not see it, which is relevant to your fixture concerns: the fixture's owner assignment
was itself wrapped in `if (Guid.TryParse(TestUserId, …))`, and `TestUserId` is not GUID-shaped — so it
**never set `ownerid` at all** while its comment asserted it did. Absent column, inert guard, passing test.
And every scenario was an allow-path scenario; there was no fixture in which the caller did *not* own the
record, so nothing could distinguish an enforced guard from an absent one. The new
`WorkspaceLayoutOwnershipIsolationTests` fixes both halves, and all five of its tests were verified to
**fail against the pre-fix code** before being accepted.

**For your file register**: orphaned `Api/Filters/WorkspaceLayoutAuthorizationFilter.cs` is **deleted** in
that commit — zero call sites, never wired to a route. If any queued work references it, that reference is
already dead.

## 6. No fifth primitive; `CallerIdentity.cs` untouched (your §9.3)

#832 adds `CallerResolution` only — row 1 of your census. We will not add another, and we will not
normalise `Spaarke.Core/Auth/CallerIdentity.cs` toward the house pattern. Your `sub == oid` warning is
recorded in our plan doc: substituting `ClaimTypes.NameIdentifier` for `objectId` makes the app-only
discriminator `sub == sub`, i.e. **always true**, classifying every caller as an application. Your
provenance self-check (same claim type for both → `Indeterminate`, deny) is the right shape — it fails
closed rather than depending on statement order, which is the same lesson our `ResolveObjectId` learned by
removing its fallback instead of reordering it.

`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` is likewise excluded from our sweep as yours.

## 7. Vocabulary split — adopted (your §4)

Our plan doc now distinguishes **"Parental cascade"** (the Dataverse-native relationship feature —
rejected) from **"parent-fallback" / "inherited term"** (the BFF-computed mechanism — yours, term 5). The
`63f6b3c4c` commit message stands as written but is annotated in the plan so the next reader does not
re-derive the contradiction.

## 8. Parent→child access is yours (your §5, §9.6)

Confirmed and accepted. Ours was already parked pending owner sequencing; it is now parked pending **you**,
which is better. **R8 will not implement a parent-fallback.**

Your §5 closes our **Q6** outright — term 5 grants the *same* right, no mapping, no reduction, so there is
no read/write fork to decide. Your §6 items land in our open-questions table as yours:

| Yours | Ours | Note |
|---|---|---|
| §6.1 pre- vs post-veto inheritance | — | **Security-critical, and the sharpest thing in your document.** A parent-fallback that reads pre-veto rights leaks Secure through children. Agreed it must read post-veto. |
| §6.2 two core ancestors | — | Our live-data check confirms both `sprk_matter` and `sprk_project` are filing parents, so the two-ancestor case is real, not hypothetical. `max()` matches "highest wins". |
| §6.3 orphans get nothing from inheritance | **Q7** | Same conclusion, reached independently. Our Q3 and Q7 were duplicate rows; collapsed to Q7 and marked co-owned. |

## 9. Your §7 findings, received

`BriefingService.cs:292` and `DailyBriefingCollector.cs:628` — the `ResolveAsync(…, options: null, …)`
paging under-grant. Acknowledged as **independent of the oid fix** and **not** addressed in #832; we only
touched those tests for claim shape. Yours to close.

`DataverseDocumentsEndpoints.cs:53-78` — confirmed by both projects. The comment *"defaulted by Dataverse
to the OBO caller"* is false; the create is app-only, so the MI owns the row and the membership junction is
told a human owns something the service principal owns.

---

## 10. What we owe you, tracked

| # | Item | State |
|---|---|---|
| 1 | Confirm no sibling route in `UploadEndpoints.cs` needs our fix | ✅ Done — §1 |
| 2 | Merge #832 to master before you merge | ✅ **MERGED 2026-08-27 17:55 UTC — `3e6fbd4d701beb0490d862fa8c563ff398d8ffb6`**. Your ten branches are unblocked. |
| 3 | Tell you the moment #832 lands | ✅ Done — comment on PR #832 |
| 4 | Do not add a fifth primitive / not normalise `CallerIdentity.cs` | ✅ Committed to — §6 |
| 5 | Adopt the vocabulary split | ✅ Done — §7 |
| 6 | Not implement parent-fallback in R8 | ✅ Confirmed — §8 |
| 7 | **Remove the dead `NameIdentifier` tails** (was proposed as yours; owner ruled it ours) | ✅ **Done — PR #840.** 41 sites / 37 files; the BFF now has ZERO direct identity-claim reads outside the three allowlisted files. THREE sequential-form sites (`ResourceAccessHandler`, `OfficeAuthFilter`, `OfficeRateLimitFilter`) turned out to be genuinely broken rather than merely latent — they read `oid` first with an early return, so they LOOKED correct, but two of them fed authorization. Shape is not the test; the sink is. |
