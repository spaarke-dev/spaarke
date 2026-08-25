# Task 022 — document-surface authorization inventory

> In progress, started 2026-08-24. Rigor FULL, `opus` @ `xhigh`, directional steps.
> **The inventory is a deliverable**, not a means: task 002 gated 4 members of this class, and the
> review estimated the class at "~15". The real count is **22 routes across 4 files**.

---

## The mechanism

`AddDocumentAuthorizationFilter("<operation>")` ([`Api/Filters/DocumentAuthorizationFilter.cs`](../../../src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs)) is the extension point. It pulls the caller from `ClaimTypes.NameIdentifier`, resolves the resource from route values (`id` → `documentId` → `containerId` → `driveId` → `itemId` → `resourceId`, first match wins), and calls `AuthorizationService.AuthorizeAsync`.

Two properties worth recording because the gates depend on them:

- **It fails closed.** `OperationAccessPolicy.GetRequiredRights` **throws** on an unknown operation string, and the filter's `catch (Exception)` returns 500. So a typo'd operation denies rather than allows.
- **That is also a trap.** The file's own header records the prior incident: an unmapped operation produced `"unknown_operation"` for *every* caller — an unconditional 403 on the finance surface, the Office save path, and three document read routes. **New operation keys must be added to `OperationAccessPolicy` BEFORE a filter is attached with them.**

### Available bare-string operation keys

Before this task: `read` · `finance.read` · `finance.confirm` · `entity.associate_document`
Added by this task: **`write`** (→ `Write`) · **`delete`** (→ `Delete`)

There was no bare `write` or `delete` key — **even though `AddDocumentAuthorizationFilter`'s own `<param>` doc has always read `'e.g. "read", "write", "delete"'`.** The extension point documented a contract that two thirds of it could not honour, and honouring it would have produced an unconditional 403 rather than a compile error. That is the sharpest form of the trap in the header above.

Both new keys were verified reachable before being added: `DataverseAccessRightsMapper` maps `WriteAccess → Write` and `DeleteAccess → Delete`, and `RetrievePrincipalAccess` returns the full comma-separated rights string, so the snapshot can actually carry them. Skipping that check would have re-created the 403 by a different route — the hazard the `entity.associate_document` comment records for `AppendTo`.

⚠️ **Both depend on RPA being live.** `DataverseAccessDataSource`'s fallback probe caps rights at Read *by construction*, so on an RPA outage every `write`/`delete` gate denies. Correct fail-closed direction and the same trade task 008 accepted for the delegation gate — but it means these routes are unavailable, not degraded, if RPA is misconfigured. Live verification is task 034's (`RPA-FALLBACK` log marker).

---

## The 22 routes

### ✅ Gated (4)

| Route | File | Operation | Gated by |
|---|---|---|---|
| `GET /api/documents/{documentId}/content` | FileAccessEndpoints.cs:58 | `read` | task 002 |
| `GET /api/documents/{documentId}/download` | FileAccessEndpoints.cs:125 | `read` | task 002 |
| `GET /api/documents/{documentId}/eml-render` | FileAccessEndpoints.cs:145 | `read` | email-r5 task 010 |
| `GET /api/v1/documents/{id}/download` | DataverseDocumentsEndpoints.cs:443 | `read` | pre-existing |

### 🛑 Gated in form only — no authorization decision (1)

| Route | File | Finding | Status |
|---|---|---|---|
| `POST /api/documents/bulk-download` | DocumentsBulkEndpoints.cs:43 | **C1** | ✅ fixed — see below |

`BulkDownloadAuthorizationFilter.InvokeAsync` confirmed verbatim: read a `tid`/tenant claim (401 if absent), extracted the `BulkDownloadRequest`, logged `"Bulk download authorization granted: tenant={TenantId}, requestedCount={Count}"`, and returned `await next(context)`. **No per-document decision at any point.** 500 GUIDs per request, streamed app-only, and failures listed in a `_FAILED.txt` manifest while zipping continues — so one call both exfiltrated and enumerated.

**The doc comment is why it survived review.** It asserted — twice — that "per-document access is enforced at Dataverse lookup time via the user's identity (same model as `GET /api/documents/{id}/preview-url`)". Both halves false: the lookup is `IDocumentDataverseService.GetDocumentAsync`, which is app-only and carries no caller identity; and `preview-url` had no per-document authorization either, so the claim derived its authority from a route making the same empty claim. **A comment asserting that enforcement happens elsewhere is a claim to verify, not evidence.**

### 🛑 Ungated — destroy (2, both Critical)

| Route | File | Finding | Note |
|---|---|---|---|
| `DELETE /api/documents/{documentId}` | DocumentOperationsEndpoints.cs:57 | **C2** | `DeleteAsync(Guid documentId, string correlationId, CancellationToken)` — **no user identity parameter at all**. Destroys the Dataverse row and the SPE file app-only. Reachable from a shipped client hook. |
| `DELETE /api/v1/documents/{id}` | DataverseDocumentsEndpoints.cs:230 | **C3** | Second app-only destroy path, while its `/download` sibling *is* gated. |

### 🔴 Ungated — mutate / disclose (8)

| Route | File | Finding |
|---|---|---|
| `GET /api/v1/documents/{id}` | DataverseDocumentsEndpoints.cs:108 | **H2** — returns `GraphDriveId`/`GraphItemId`, the exact pointers C1 and C2 consume |
| `PUT /api/v1/documents/{id}` | DataverseDocumentsEndpoints.cs:163 | **H2** — app-only tamper by GUID |
| `POST /api/documents/{documentId}/checkout` | DocumentOperationsEndpoints.cs:30 | **H3** — returns an **editable** URL |
| `POST /api/documents/{documentId}/checkin` | DocumentOperationsEndpoints.cs:39 | **H3** |
| `POST /api/documents/{documentId}/discard` | DocumentOperationsEndpoints.cs:48 | **H3** |
| `GET /api/documents/{documentId}/checkout-status` | DocumentOperationsEndpoints.cs:66 | **H3** |
| `POST /api/documents/{documentId}/analyze` | DocumentOperationsEndpoints.cs:74 | **H3** |
| `POST /api/documents/{documentId}/share-link` | FileAccessEndpoints.cs:89 | **H4** → owned by task 012 (constraint), not gated here |

### 🟠 Ungated — URL-minting reads (4)

These mint URLs that **outlive the request**, which is why the original investigation flagged them separately (open item 6). The `preview-url` route is also the one C1's justifying comment cites as its model.

| Route | File |
|---|---|
| `GET /api/documents/{documentId}/preview-url` | FileAccessEndpoints.cs:33 |
| `GET /api/documents/{documentId}/preview` | FileAccessEndpoints.cs:44 |
| `GET /api/documents/{documentId}/office` | FileAccessEndpoints.cs:68 |
| `GET /api/documents/{documentId}/view-url` | FileAccessEndpoints.cs:100 |
| `GET /api/documents/{documentId}/open-links` | FileAccessEndpoints.cs:76 |

### ⚪ Different shape — no caller-supplied document id (3)

Not part of the per-document class; listed so the inventory is provably complete rather than silently truncated. These need collection-level scoping, which is Phase 1 evaluator work.

| Route | File | Shape |
|---|---|---|
| `POST /api/v1/documents/` | DataverseDocumentsEndpoints.cs:30 | create — no id yet |
| `GET /api/v1/documents/` | DataverseDocumentsEndpoints.cs:447 | list — needs collection scoping |
| `GET /api/v1/containers/{containerId}/documents` | DataverseDocumentsEndpoints.cs:520 | list by container |

---

## Progress

### ✅ H5 — fixed

`ExternalDataService.GetProjectsAsync` ordered the accessible-projects query by `sprk_name`, which does not exist on `sprk_project` — the display name is `sprk_projectname`, and the `$select` on the line above already had it right. Dataverse answered 400, `GetCollectionAsync` caught it and returned an empty list, so **the external SPA rendered "you have no grants" for every caller who had grants.**

This is the **sixth** instance of the stale-column class in this project. The reason it survived every prior review: the `$select` and the `$orderby` are on adjacent lines and disagree, so eyeballing the select gives a false all-clear. A repo-wide grep confirms no other `sprk_projects` query orders by `sprk_name`.

### ✅ Keys added, then C2 + C3 + H2 + H3 gated

**Keys first, gates second, in one commit** — see the section above.

| Finding | Route | Operation |
|---|---|---|
| **C2** | `DELETE /api/documents/{documentId}` | `delete` |
| **C3** | `DELETE /api/v1/documents/{id}` | `delete` |
| **H2** | `PUT /api/v1/documents/{id}` | `write` |
| **H2** | `GET /api/v1/documents/{id}` | `read` |
| **H3** | `POST /api/documents/{documentId}/checkout` | `write` |
| **H3** | `POST /api/documents/{documentId}/checkin` | `write` |
| **H3** | `POST /api/documents/{documentId}/discard` | `write` |
| **H3** | `GET /api/documents/{documentId}/checkout-status` | `read` |

**Correction to this file's own earlier claim.** It said C2 "is NOT a filter attachment" because `DocumentCheckoutService.DeleteAsync(Guid, string, CancellationToken)` has no user-identity parameter, and called it "a signature change with call-site fallout". **That was wrong.** `DocumentAuthorizationFilter` reads the caller from `ClaimTypes.NameIdentifier` on the `HttpContext` and the resource from route values — it never needs the service to accept an identity. C2 is an ordinary filter attachment.

What the missing parameter *does* mean is worth keeping, just not as a blocker: the delete runs **app-only**, so Dataverse's own row-level security never sees the caller and there is **no defence in depth** — the filter is the entire boundary. Every sibling (`CheckoutAsync`, `CheckInAsync`, `DiscardAsync`, `GetCheckoutStatusAsync`) takes a `ClaimsPrincipal`; the destroy is the one that does not. That asymmetry is now recorded in `DeleteAsync`'s own doc comment so a future call-site added without the filter is visibly an unauthenticated destroy path.

Also corrected: the `/checkout` route's old comment claimed "PCF controls button visibility based on Dataverse security profile / actual permissions enforced by Graph API via OBO". **Both halves were false for that route** — client-side button visibility is not enforcement, and the checkout path is app-only, so nothing downstream evaluated the caller either. This is the sixth doc-comment-lies instance in this area.

`DeleteAsync` was made `virtual` (ADR-038 §4 substitution seam), same justification as task 009's `UpdateTodoAsync`: the gate is only meaningfully verifiable if a test can assert the destroy **did not happen**, and this one destroys the SPE file as well as the row.

**Tests**: 21 in `tests/integration/auth/UnifiedAccessControl/DocumentDestroyAuthorizationTests.cs` + `DocumentDestroyAuthorizationTestFixture.cs`. The fixture substitutes `IAccessDataSource` so rights ride on the bearer token (`Bearer rights=ReadAccess,DeleteAccess`) — the `OfficeSaveTestFixture` convention — because offline the real data source fails closed and every negative assertion would otherwise be vacuous. Both destroy paths and the PUT are recorded, so "denied" means *nothing was mutated*, not merely *the response said no*.

**Perturbations — 10 of 10 bite, baseline 0/21:**

| Perturbation | Failures |
|---|---|
| Remove the C2 filter | 2 of 21 |
| Remove the C3 filter | 2 of 21 |
| `delete` key also requires Write (over-restrictive) | 4 of 21 |
| `delete` key downgraded to Read (over-permissive) | 5 of 21 |
| `delete` key removed entirely | 6 of 21 |
| `write` key removed entirely | 9 of 21 |
| Remove the H2 PUT `write` filter | 1 of 21 |
| Remove the H2 GET `read` filter | 1 of 21 |
| Downgrade `/checkout` from `write` to `read` | 1 of 21 |
| Remove the checkout-status `read` filter | 1 of 21 |

⚠️ **The first sweep produced fake numbers and had to be redone.** The harness restored files with `shutil.copy2`, which preserves the *backup's* mtime — older than the built DLL, so MSBuild skipped recompiling and some runs measured a **stale binary still carrying the previous perturbation**. It reported 3 failures for the `write`-key removal; the true value is 1. Caught because an unexplained count was checked rather than accepted. Fixes: `os.utime(f, None)` on restore, plus a **clean-tree baseline run** (must be 0) before the sweep — without that baseline, every count is measured against unknown noise. Any future perturbation harness in this project needs both.

### ✅ C1 — bulk download gated, and the oracle closed with it

`BulkDownloadAuthorizationFilter` now authorizes the caller for `read` against **every** requested document, through the same `AuthorizationService` and the same `"read"` key the single-document `/download` route uses, and publishes the allowed set on `HttpContext.Items` — mirroring how `CallerPrincipalAuthorizationFilter` hands a resolved principal to its handlers. The decision stays in a filter (ADR-008); only the manifest-building stayed in the handler, which already owned it.

The handler **fails closed on an absent verdict**: a missing key means the filter did not run (route mapped without it, or reordered), which is a wiring fault, not a rights outcome — so it returns 500, not 403. Saying 403 would claim the caller's rights were evaluated and found wanting when nothing evaluated them.

**Both halves, because closing one alone makes things worse.** Per-item authorization introduces a *new* distinguishable outcome ("denied") which, next to the existing "not found in Dataverse", turns `_FAILED.txt` into an **enumeration oracle amplified 500× per request**. There was no such oracle before the fix — every document was simply returned, so there was no denial to distinguish. Both now collapse to one string, `NotAccessibleReason`. Shape errors (null, empty, non-GUID) stay distinguishable: they describe the caller's own input. Failures *after* a successful authorization stay distinguishable too — that caller holds Read, so the record's state is theirs to know.

#### 🚨 NEW finding, unrelated to authorization: the endpoint threw on its happy path

Writing the **first ever test for this route** — there were none; `grep` for `bulk-download` across `tests/` returned only the new files — surfaced a production defect:

`ZipArchive` is a **synchronous** API. In Create mode it writes to the stream it wraps with blocking calls, and the handler wraps `HttpResponse.Body` directly. Kestrel has `AllowSynchronousIO = false` by default since .NET 3.0, and **`AllowSynchronousIO` appears nowhere in this repo**. So the first entry flush threw `InvalidOperationException: Synchronous operations are disallowed`, followed by a cascading `IOException: Entries cannot be created while previously created entries are still open` when `_FAILED.txt` was written over the entry that had failed to close. Response headers (200 + `application/zip`) commit *before* that point, so a caller got a truncated archive on a broken connection rather than a readable error.

Fixed by opting **this one request** into synchronous body IO via `IHttpBodyControlFeature`. Buffering the archive instead would defeat the endpoint's stated reason for existing (bounded memory for 500 documents, per its own Placement Justification), and the BCL has no async `ZipArchive`. Global default unchanged.

**This nuances C1's exploitability and the honest reading is worth recording**: the *download* half was broken, so an attacker got at most a corrupt partial zip. The **enumeration half worked perfectly** — the pre-flight loop ran 500 app-only Dataverse lookups and the 404 total-failure branch returned per-document `failedItems` reasons. So C1 was a working enumeration primitive and a broken exfiltration primitive. Both are closed.

**Why no test caught any of this**: `WorkspaceTestFixture`'s `FakeAuthHandler` issues `oid`, `NameIdentifier`, `name` and `roles` but **no `tid`** — so every route behind a tenant check answers 401 under the shared fixture regardless of the code under test. The route was effectively unreachable from the test suite. Solved with a `tid`-bearing scheme in the task-022 fixture rather than by widening the shared handler, whose claims back a large number of tests.

**Perturbations — 5 of 6 bite, baseline 0/30:**

| Perturbation | Failures |
|---|---|
| Handler ignores the authorized set | 5 of 30 |
| Filter authorizes everything | 5 of 30 |
| Denial reason renamed to "Access denied" (re-opens the oracle) | 2 of 30 |
| Filter never publishes its verdict (the wiring fault) | 3 of 30 |
| `AuthorizationService`'s fail-closed catch inverted to ALLOW | 2 of 30 |
| Filter's own catch inverted to authorize | **0 — expected** |

That last zero is **not** a test gap, and chasing it as one would have been wrong. `AuthorizationService.AuthorizeAsync` already has a catch-all returning `IsAllowed=false`, so a failing access data source is denied one layer down and never reaches the filter's catch — it is unreachable defence-in-depth. The load-bearing guard is `AuthorizationService`'s, and perturbing *that* fails 2 tests, so the fail-closed path IS covered. Both facts are now in the filter's own comment so the next reader does not repeat the investigation. **Lesson: before "fix the test", check whether the perturbed code is reachable at all — a zero can mean redundant code rather than absent coverage.**

### ✅ `analyze` + the five URL-minting reads — gated

**`analyze` → `write`**, decided by the owner 2026-08-24 rather than defaulted. The case for `read` was real (the analysis is written to a DIFFERENT entity than the one the filter authorizes — the same reasoning that made `finance.confirm` deliberately not require Create). `write` won because the route commits real resources on the caller's say-so (AI spend + queued background work) and the profile fields it populates are the document's own.

**The five URL-minting reads** gated with `read`, and the "does minting deserve its own key?" question is answered **no**: a separate key would carry the same required right and change no decision, so CLAUDE.md §11 cannot be satisfied — there is no behaviour that fails without it. The real difference is url LIFETIME, which no operation key can address; revoking access later does not invalidate an already-minted url. Filed with task 012's link-lifecycle work.

#### ⚠️ These five are NOT the same shape as `/content` and `/download` — verified, not assumed

All five reach SPE through `*AsUserAsync` → `IGraphClientFactory.ForUserAsync`, i.e. **genuine OBO**. Graph therefore already enforced the caller's own SPE access. So this inventory's earlier framing — "any authenticated caller could mint a url for any document by GUID" — was **overstated**, and the first draft of the code comment repeated it before the OBO check was run. Corrected before commit. Same class of error as C1's false claim, caught this time only because C1 taught it.

That also answers the open item "do the OBO read routes need a record check too?": **yes**. SPE permission is container-scoped and coarser than per-document Dataverse rights, so the gate deliberately **narrows** behaviour — a caller with container access but no `Read` on the `sprk_document` row previously succeeded and now gets 403. That caller seeing another client's document is exactly the disclosure spec FR-01 exists to close.

`share-link` (H4) stays with task 012, and its OBO claim was checked too: `CreateSharingLinkAsUserAsync` really does call `ForUserAsync`, so unlike bulk-download's identical-sounding claim, **this one is true**. Useful contrast: "OBO means the caller's own access enforces" is a valid mechanism; C1's version was false because the call was app-only, not because the reasoning pattern is wrong.

**Perturbations — 5 of 5 bite, baseline 0/42**: remove the analyze filter (1), downgrade analyze to `read` (1), remove the preview-url / view-url / open-links filters (1 each).

### ✅ Resolved: the filter-catch zero was REDUNDANT CODE, not missing coverage

Confirmed by a two-factor experiment rather than by reading the code (owner asked for confirmation):

| A: `AuthorizeAsync` throws outside its own try | B: filter catch inverted to authorize | Failures |
|---|---|---|
| off | off | 0 of 30 |
| off | on | 0 of 30 ← the original puzzling result |
| **on** | off | **14 of 30** |
| **on** | **on** | **17 of 30** |

The **3-test delta between the last two rows is the catch's coverage**. The guard is live, correct, and already pinned by tests — it simply cannot be reached today, because `AuthorizeAsync` wraps everything except its own `ArgumentNullException.ThrowIfNull` in a catch-all that returns a denial. The zero was a fact about `AuthorizationService`, not about the test suite. Both measurement and conclusion now live in the filter's own comment so nobody repeats the investigation.

**Generalised lesson, carried forward:** a zero-failure perturbation has **two** possible causes — the test is at the wrong level, *or the perturbed code is unreachable*. Distinguish them before rewriting tests, or you add coverage for a path that cannot execute.

### Remaining

1. **The 3 collection-shaped routes** (`POST /api/v1/documents/`, `GET /api/v1/documents/`, `GET /api/v1/containers/{containerId}/documents`) — no caller-supplied document id, so they need collection-level scoping, which is Phase 1 evaluator work (tasks 032/054). Confirmed still out of scope here rather than silently dropped.
2. **`share-link`** (H4) — owned by task 012. Its OBO enforcement is real (verified above), so it is not an open disclosure; what task 012 owns is the anonymous-link *policy* and lifecycle.

**Route status: 19 of 22 gated.** The 3 remaining are the collection-shaped ones above.
