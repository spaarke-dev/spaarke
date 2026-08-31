# The PATCH-400 escalation — RESOLVED

> 2026-08-25 · Proven live against Spaarke Dev · **Unblocks 023, 025, 026, 029**
> Supersedes the escalation in [`live-verification-2026-08-24.md`](live-verification-2026-08-24.md) §2.

---

## 1. The answer

**`etag` is a REQUIRED property in the PATCH request BODY.** Every write this product ever attempted
omitted it, so every write was rejected.

Microsoft's own reference for
[Update fileStorageContainerType](https://learn.microsoft.com/en-us/graph/api/filestoragecontainertype-update)
states it twice:

| Property | Type | Description |
|---|---|---|
| `etag` | String | Used for optimistic concurrency control. Must match the value returned from a Create or Get request. **Required.** |

and then, under *"Example 2: Update a fileStorageContainerType **without ETag**"*:

```
HTTP/1.1 400 Bad Request
```

**Our exact symptom, documented as the expected response.**

### Proven live, 2026-08-25

The *identical* no-op PATCH (writing the current value back, so the payload cannot be the variable):

| Request | Result |
|---|---|
| `PATCH …/containerTypes/{id}` — `{"settings":{"itemMajorVersionLimit":500}}` | **400** `invalidRequest` |
| Same body **+ `"etag":"MC4wLjAuMA=="`** | **200 OK** ✅ |
| Same, on **v1.0** | **200 OK** ✅ |

And a full round-trip, which is **task 023's AC-2** — unverifiable since the day it was written:

```
set itemMajorVersionLimit = 499   → 200
read back                          → 499   PERSISTED ✅
restore original value 500         → 200
```

---

## 2. 🔴 Why it hid for two days, and what to learn from it

### It is a BODY property, not the `If-Match` header

The 2026-08-24 session **did** try `If-Match: {etag}` — and correctly recorded that it changed
nothing. That is true and it is a dead end: Graph wants `etag` as a **member of the JSON body**.

The two are one word apart, both are "the etag", and both are legitimate OData concurrency
mechanisms. Trying the header and seeing no change is *weak evidence that the etag is irrelevant* —
and it was read as strong evidence, which sent the whole investigation toward auth.

### Graph's error names nothing

> `400 invalidRequest` — *"One of the provided arguments is not acceptable."*
> `innerError.code: badArgument`

The inner error adds no cause. And the tell was available all along: **an empty `{}` body returns the
same 400** — "one of the provided arguments is not acceptable" when there are **no arguments**. That
is boilerplate, and boilerplate cannot be reasoned from.

**This is this project's signature defect, committed against us by the platform**: a specific,
documented, satisfiable requirement collapsed into a generic message that an upper layer reads as
"something about your arguments".

### The hypothesis it produced was expensive and wrong

The escalation recorded the leading cause as *"only the owning application may modify its container
type"* — which pointed at re-running the ADR-028 §6.5 gate, and at two tests **both with side
effects**: create a throwaway trial container type (tenant limit is one, and one already exists), or
enable public-client flows on the **production SPA registration**.

**Neither was necessary.** No app registration was changed, nothing was created, nothing was deleted.

Three signals pointed away from ownership *before* the doc was found, and are worth recognising next
time:

1. The 400 was **uniform across all four container types**, each owned by a **different** app.
2. **An empty body 400'd identically** — nothing about the caller changed between attempts.
3. `GET …/permissions` returned **200** on all four types, so the token and delegated path were fine.

> **The lesson**: read the vendor's own reference for the exact operation before hypothesising about
> auth. The answer was in a Microsoft doc that our knowledge corpus already linked
> (`learn-containertypes.md:99`), and it took one fetch.

---

## 3. The fix

`UpdateContainerTypeSettingsAsync` is now a **read-modify-write**: it GETs the container type,
takes its `etag`, and sends it in the PATCH body.

The GET is load-bearing, not a convenience:

- It is the read half of optimistic concurrency. Reading the etag immediately before writing keeps
  the window small, and a stale etag makes Graph **reject** a colliding write rather than letting
  this app silently last-writer-wins over an administrator who changed the same type moments ago.
- If Graph returns **no** etag, the code **throws rather than sending a doomed PATCH** — otherwise the
  operator lands right back in front of the 400 that names nothing.

Pinned by three tests in `SpeAdminContainerTypeSettingsPatchTests`:
`SendsTheEtagInTheBody…`, `ReadsTheEtagImmediatelyBefore…`, `WhenGraphReturnsNoEtag_FailsLoudly…`.

`GraphWireMockFixture` gained `PatchRequestsFor()` — a settings write now issues two requests, so an
assertion meaning *"the write we sent"* has to say so. Without it, the negative test
(*"no write was attempted"*) would have passed or failed for the wrong reason.

---

## 4. What this unblocks

| Task | Was | Now |
|---|---|---|
| **023** | 🔄 AC-2 unverifiable — "every PATCH 400s" | ✅ **write → read-back proven live** |
| **025** | 🔄 server complete, writes unverified | write path works; **form rebinding is still open** (separate, known) |
| **026** | 🔄 AC-2 escalated | the *write* half works; AC-2's own finding (overridables is a permission, not a state, and is unreadable from an owning tenant) **stands independently** |
| **029** | 🔄 AC-1 partial | live render now reachable |

⚠️ **025 and 026 are not automatically complete.** Their remaining gaps were never about the 400 —
this removes the blocker, not their scope.

---

## 5. Still open: adding a container-type OWNER returns 400

Task 027's AC-1 is **half verified**:

- `GET …/containerTypes/{id}/permissions` → **200** on all four types ✅ (read path live-verified;
  Graph reports **zero** owners on every type)
- `POST …/permissions` with `{"roles":["owner"],"grantedToV2":{"user":{"userPrincipalName":…}}}` →
  **400** `invalidRequest`, same uninformative message

Given what we just learned, **do not hypothesise — read Microsoft's reference for creating a
container-type permission first.** The body shape, a required property, or the role name are all
candidates, and this exact error message has now demonstrated once that it means "a documented
requirement is unmet", not "you are not allowed".

## 6. Also observed

- **`billingStatus` is `valid` on all four container types**, matching the operator's M365 screenshot
  exactly. The "Unknown" seen in the local review was a **fixture artefact**, not a code defect —
  task 029's mapping is confirmed correct against live data.
- **The expired trial still reports `billingStatus: valid`.** Billing validity and usability are
  independent: a container type can be billing-healthy and dead. Precisely why the trial-expiry work
  keys off `expirationDateTime` rather than billing.
- Listing containers of a container type the calling app does not own → **403**. Unrelated to this
  escalation, but it is why the "does the expired trial hold containers?" question is still open.
