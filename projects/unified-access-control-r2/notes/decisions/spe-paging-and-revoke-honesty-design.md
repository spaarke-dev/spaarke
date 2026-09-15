# Design — SPE membership paging (M1) + `/revoke` status honesty (M2)

> **Status**: **DESIGN COMPLETE, NOT IMPLEMENTED.** Written 2026-09-09 to make task **024** executable
> without re-deriving the approach. Supersedes the one-line sketch in task 020's hand-off
> ("one paged read + local email match + delete-by-permission-id"), which is a direction, not a design.
> **Read this before starting 024.**

---

## 0. 🔴 The load-bearing assumption NOBODY HAS VERIFIED — establish this first

Finding M1 says a container whose permission list "spans more than one page" reports clean after a
partial clear. Two separate claims sit inside that, and only one is verified:

| Claim | Status |
|---|---|
| The code issues a single `.GetAsync()` and never follows `@odata.nextLink` | ✅ **VERIFIED** — `SpeContainerMembershipService.cs:167-168` (`RevokeMembershipAsync`) and `:235-236` (`ListExternalMembersAsync`) |
| SPE container permissions **actually page** | ❌ **UNVERIFIED.** Nobody has established that `GET /storage/fileStorage/containers/{id}/permissions` ever returns `@odata.nextLink`, or at what size. |

**Step 0 of task 024 is to establish the second claim**, because it sets the severity:

- **If permissions DO page** — M1 is a live false-assurance defect and the priority stands.
- **If they DO NOT page** (single response regardless of size, or a documented server cap) — the code
  fix is still correct as *defensive* handling, but this is a latent robustness issue, not a live hole,
  and it should be re-ranked accordingly.

**How to establish it** (in order of cost): Microsoft Graph docs for the SPE `permissions` collection;
then the `PermissionCollectionResponse` model — does it even expose `OdataNextLink`?; then, decisively,
seed a dev container past any plausible page size (Graph's default collection page is commonly 100 or
200) and observe. **Do not skip this because the finding is confidently worded.** This project has been
wrong four times in one session by trusting a task file over the artefact.

> ⚠️ **I asserted a precedent that does not exist.** In conversation I said `DriveItemOperations.cs:392`
> "handles `OdataNextLink` correctly" and could be copied. **That is wrong.** It only *detects*
> `OdataNextLink` and synthesises a client-facing skip-token URL — it never follows the link server-side.
> It is client-pagination passthrough, not enumeration. **The real in-repo precedent is
> `PrivilegeGroupResolver.cs:202`**, which uses the Graph SDK v6 `PageIterator`. Corrected here so the
> wrong file is not copied.

---

## 1. The design in one paragraph

Replace both single-shot permission reads with **one** paged reader that returns *both* the items **and
whether enumeration completed*. Make every caller treat "incomplete enumeration" as failure rather than
as an empty/negative result. Nothing in the outcome vocabulary needs to change — the existing enum
already *promises* what paging would make true. Then align `/revoke`'s HTTP status with closure's —
while recognising that this change delivers **no user-visible improvement on its own**, because the
client ignores both the status and the body.

---

## 2. Paging: the reader

### 2.1 One reader, three consumers

Today three call sites read container permissions independently. After 024 there is **one**:

```
internal async Task<PermissionReadResult> ReadPermissionsAsync(string containerId, CancellationToken ct)
```

returning:

```
internal sealed record PermissionReadResult(
    IReadOnlyList<Permission> Permissions,
    bool EnumerationComplete);     // false => bound hit, or a page fetch failed
```

| Consumer | Today | After |
|---|---|---|
| `ListExternalMembersAsync` (`:235`) | single `GetAsync` | `ReadPermissionsAsync` |
| `RevokeMembershipAsync` (`:167`) | single `GetAsync` | `ReadPermissionsAsync` |
| Org-member cleanup (task 020, `RevokeExternalAccessEndpoint`) | **N reads for N members** | **ONE** `ReadPermissionsAsync`, then match locally, then delete by permission id |

That third row is task 020's N+1, fixed as a side effect of consolidating rather than as separate work.

⚠️ **Do NOT use `RemoveAllExternalMembersAsync` as the paged primitive** (task 020's warning, preserved):
it removes *every* external member, not just the target organisation's.

### 2.2 Use `PageIterator`, not a hand-rolled loop

Follow `PrivilegeGroupResolver.cs:202` — `PageIterator<Permission, PermissionCollectionResponse>
.CreatePageIterator(graphClient, firstPage, callback)` then `IterateAsync(ct)`. Graph SDK v6.5.0 is
already referenced.

> ⚠️ **Do not copy `PrivilegeGroupResolver`'s shape verbatim — it has a bug.** It adds the first page's
> items in a `foreach` *and then* hands the same response to `CreatePageIterator`, which iterates that
> page again. Page-1 items are collected **twice**. Harmless there only because the result is used as a
> membership test; it would be a real defect here. Let the iterator own **all** collection. Filed
> separately as **ISS-001** — it is not task 024's to fix.

### 2.3 Bounding, and what the bound means

Mirror the existing convention: `ProjectClosureEndpoint.MaxMembersPerSweep = 200` exists to convert
silent truncation into a *detectable* condition, not to cap work. Apply the same idea to pages —
a `MaxPermissionPages` bound, and **hitting it sets `EnumerationComplete = false`**. It is a detector.

**The bound must never be reported as a complete read.** That is the entire point.

---

## 3. 🔑 The crux: what each outcome is ALLOWED to claim

This is the part the one-line sketch omits, and it is where the real work is.

### 3.1 The vocabulary is already correct — the implementation lies about it

`SpeContainerRevokeOutcome.NoPermissionFound` is documented today as:

> *"The container's permissions were read **successfully** and this Contact holds none. **Genuinely
> absent** — the expected result under the broker-only model."*

With a single-page read that sentence is **false**: the read succeeded, but it was partial, so "genuinely
absent" is unprovable. **No new enum member is needed.** Task 024 does not extend the vocabulary — it
makes the code finally honest to a contract that already exists. That is a deliberately small blast
radius, and it is why the DTO does not change.

### 3.2 The mapping rule (the binding part)

| Enumeration | Target found? | Outcome |
|---|---|---|
| Complete | yes, deleted | `PermissionRemoved` |
| Complete | no | `NoPermissionFound` ← *now truthful* |
| **Incomplete** | yes, deleted | `PermissionRemoved` (the deletion is a fact; it succeeded) |
| **Incomplete** | no | 🔴 **`RemovalFailed`** — **never** `NoPermissionFound` |

That last row is the whole fix. `RemovalFailed` already reads *"The Contact may RETAIN file access; the
revoke should be retried"* — exactly right for "we could not finish looking". Fail closed (ADR-003).

### 3.3 The same rule for the closure guard

`container_not_cleared` is a *verification* step: after clearing, re-read and confirm nothing remains.
Its value is entirely that it verifies. Apply the identical rule:

| Enumeration | Members remaining | Result |
|---|---|---|
| Complete | 0 | cleared ✅ |
| Complete | N > 0 | `container_not_cleared` |
| **Incomplete** | any | 🔴 **`container_not_cleared`** — an unverified set is never "clean" |

**A guard that cannot complete its check must report failure, not success.** Reporting clean on an
unenumerated set is worse than having no guard, because a clean report gets acted on.

---

## 4. M2 — status parity, and the finding that changes the plan

### 4.1 The divergence (verified)

| Path | Failed / partial SPE clear |
|---|---|
| `/close-project` | **500** + `sdap.closure.incomplete.container_not_cleared` (`ProjectClosureEndpoint.cs:269`) |
| `/revoke` | **200**, failure in the body (`RevokeExternalAccessEndpoint.cs:220`) |

### 4.2 🔴 The server change alone achieves NOTHING observable

`AccessGrantModal.postJson` (`AccessGrantModal.tsx:327-335`):

```ts
const res = await authenticatedFetch(path, { ... });
return (await res.json()) as T;     // no res.ok check
```

Trace both worlds:

| | Today (200 + body) | After a naive 500 |
|---|---|---|
| Client checks status? | no | **no** |
| Client reads outcome from body? | **no** | no |
| What the user sees | success | ProblemDetails cast to `RevokeAccessResponse`; fields `undefined` → falsy → renders wrong, or throws on a nested read |

So changing the status while the client ignores `res.ok` **trades a silent wrong answer for a different
silent wrong answer**, and possibly a runtime error. This is not a reason to skip M2 — it is a reason to
**sequence** it.

### 4.3 Recommendation — split the task

**Do M1 (paging + honesty semantics) as task 024. Move M2 to land with the client fix.**

M1 is self-contained, has no client coupling, and fixes a real false-assurance hole. M2 is a two-line
server change whose entire value is unlocked by `res.ok` handling in the client (finding **M8**, filed
against task **065**). Shipping them together — server status + client check, one PR or two adjacent
ones — is the only ordering where M2 delivers anything.

This is the same shape as FR-33's notification-first constraint: *the mechanism is worthless until the
thing that surfaces it exists.*

---

## 5. Test design — three layers, and what each actually proves

The POML's constraint is sharp and correct: *"Mocking at a seam proves the CALLER, never the CALLEE.
Test the paging at the SERVICE level."* Task 017 learned it here; task 025 hit the identical shape (the
A-13 matcher was invisible because `SpeRevokeMatcherTests` substitutes the whole service). `PageIterator`
needs a real `GraphServiceClient`, and `Mock<HttpMessageHandler>` is banned (ADR-038 B1) — so no single
layer can carry this. Three layers, each honest about its limits:

| Layer | Proves | Does NOT prove | Runs |
|---|---|---|---|
| **1. Pure decision function** — extract §3.2's mapping as a pure function over `(EnumerationComplete, TargetFound)` → outcome | The honesty rule. Incomplete + not-found ⇒ `RemovalFailed`, never `NoPermissionFound`. Cheap, exhaustive, no Graph. | That the reader ever sets `EnumerationComplete = false` | always |
| **2. Structural guard** (ArchTest, same shape as task 025's `ExternalAccessQueryIntegrityGuardTests`) — assert no `Permissions` + `.GetAsync(` outside `ReadPermissionsAsync`, and that no consumer branches on a bare `.Permissions` list | Nobody reintroduces a single-page read. **This is what defends the fix over time.** | Runtime paging behaviour | always |
| **3. LIVE-gated integration** (`SkippableFact`, the `DataverseRecordShareRoundTripTests` pattern from task 060) — a container seeded past one page is fully enumerated | Real Graph paging | nothing offline | on demand |

**Layer 2 is the one that matters most**, and it is proven technology here: the equivalent guard in task
025 was perturbation-checked and fires. Layer 3 has a real prerequisite — **a dev container seeded past
one page**, which §0 must establish is even possible.

**Perturbations to run** (POML step 5): return only page 1 ⇒ layer 3 fails, layer 2 fails if the reader
was bypassed; re-swallow the revoke failure ⇒ layer 1 fails. Record counts. A perturbation failing zero
means the test is at the wrong level — fix the test, never drop the perturbation.

**Test-double warning, preserved from task 020**: its first double derived member emails from the first 8
GUID characters, which all fixture members shared — so "three members" was **one email three times** and
the fan-out was never exercised. **State identities in a map. Never derive them.** Extend
`SpeRevokeMatcherTests`'s marked regions; do not restructure. Both existing doubles THROW on unmodelled
input — preserve that; do not reintroduce a permissive fallback.

---

## 6. Recommended task decomposition

| # | Scope | Depends on | Notes |
|---|---|---|---|
| **024a** | §0 verification spike: do SPE permissions page? | — | Cheap. Sets 024b's severity. May be answerable from docs + the model in under an hour. |
| **024b** | The paged reader + all three consumers + §3 honesty semantics + layers 1–2 | 024a | The substance. Self-contained; no client coupling. |
| **024c** | M2 status parity | 024b **and** the M8 client fix (task 065) | Two-line server change. Worthless before the client checks `res.ok`. |
| **047** (existing) | Live verification, layer 3 | 024b | Already the home for live-env checks. |

If the owner prefers to keep 024 whole, that is fine — but **024c must not ship before the client fix**,
and that constraint needs to be in the POML rather than in someone's memory.

---

## 7. Residuals — explicitly out of scope

Both belong to task **047** (live verification), not here, and neither is fixable by paging:

1. **Eventual consistency.** SPE permission lists are eventually consistent — a permission deleted moments
   ago may still be observable. So even a complete enumeration can report a false positive. Paging fixes
   incompleteness, not staleness.
2. **Alias / proxy addresses.** The email match is case-insensitive but not alias-aware. A member invited
   under an address other than `contact.emailaddress1` reads as absent no matter how many pages are read.

Both were identified by task 020 and are recorded here so 024 is not blamed for leaving them.

---

## 8. Corrections this design makes to earlier statements

1. **`DriveItemOperations.cs:392` is NOT a paging precedent** — it detects `OdataNextLink` and builds a
   client skip-token URL; it never follows it. I said otherwise in conversation. The precedent is
   `PrivilegeGroupResolver.cs:202`.
2. **"A container with more members than one Graph page" is an assumption, not a verified fact** (§0).
   The *code* defect is verified; the *exploitability* is not.
3. **No enum change is required.** The one-line sketch implied new outcome states; the existing
   vocabulary already promises what paging makes true (§3.1).
4. **M2 alone changes nothing a user can see** (§4.2) — which reorders the work.
