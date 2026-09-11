# Task 012 — Document-identity resolver: decisions

> FR-01 server. `POST /api/documents/resolve-identity`. Written 2026-09-10, revised the same day after the Step 9.5
> review (§9), and revised again after the live run against dev (§8). The live run reversed one review decision:
> Graph 403 now means "not resolvable", not 503. See §4.

## 1. Scope: 012 is required whatever the answer to Spike-1 §7

Spike-1 §7 recommended the FR-02 custom-XML stamp as the PRIMARY identity and suggested dropping the URL path from
r1. **The second half does not hold.** The BFF has 16 SPE upload sites, and task 014 stamps only one of them: the
Office save path. A document uploaded any other way arrives **unstamped**: through `DocumentsEndpoints`
(`PUT /api/drives/{driveId}/upload`, the Spaarke UI's upload), email capture, AI working documents and the rest.
When such a document is opened in Word, the URL path is the only way the pane can identify it. Precedence is already
stamp-first (014 step 5), so accepting §7 changes nothing here except, possibly, the order of 012 and 014.

## 2. Contract

```
POST /api/documents/resolve-identity
{ "documentUrl": "<Office.context.document.url, as returned>" }

200 { resolved: true,  documentId, documentName, fileName, relatedRecord: {entityType, id, name} | null, reason: null }
200 { resolved: false, ..., reason: "not_cloud_document" | "not_resolvable" | "not_spaarke_document" }  → pane: new document
200 { resolved: false, ..., reason: "identity_conflict" }                                             → pane: NOT new; no save-as-new
400 ProblemDetails  empty / over 4096 chars / not an absolute URI
401                 the group's RequireAuthorization()
403 ProblemDetails  resolved, but the caller may not read the sprk_document (no id, name or record)
503 ProblemDetails  identity_resolution_unavailable  Graph or Dataverse could not answer (incl. an unhealthy alternate key)
```

**Rules for task 013 (the client):**

1. **Treat 503 as "could not determine"**: retry, or let the user choose explicitly. **Never** treat it as a new
   document. Doing so is exactly how duplicate `sprk_document` rows get minted.
2. **Treat a 403 whose ProblemDetails `reasonCode` is `sdap.access.error.system_failure` as indeterminate too.**
   `AuthorizationService` denies with that code when Dataverse is down during the authorization check
   (`AuthorizationService.cs:137-154`). This is shared behaviour across every document route.
3. **Do not call the route when `document.url` is empty or not absolute** (an unsaved document). Those answer 400.
   Treat them locally as a new document.
4. **`identity_conflict` is not "new".** A row already holds this file's item id under a different drive, so a
   save-as-new would collide with `sprk_graphitemid_uk`.

**Why POST with a body, not GET with a query string:** request URLs are what request telemetry keeps. The file
path does still travel in one place. The outbound Graph call is `/shares/u!{base64url(URL)}`, and the dependency
telemetry for that HttpClient call records it. That is the same class of data the sibling routes already log in
plain text (open-links logs `WebUrl`/`WebDavUrl`, including the file name). This is an accepted exposure, not a new
one.

**Related record** comes from the DIRECT slots in polymorphic-resolver priority: `sprk_matter` > `sprk_project` >
`sprk_invoice` > `sprk_workassignment` (names verified against `DataverseServiceClientImpl.cs:906-914`). The
`sprk_related*` family belongs to task 026. Deactivated rows resolve, as they do in Compose.

## 3. Reuse or copy (step 1)

**Copied semantics, not reused code.** The alternate-key retrieve on the RAW `sprk_graphitemid` is copied from
`ComposeRecordResolution.TryFindDocumentByGraphItemIdAsync`. The match is exact-string, because ADR-044 does not
apply to the SPE item id. That member is internal to a collaborator built inside `ComposeService`, and the Office
path takes no code dependency on Compose (`OfficeDocumentPersistence`'s header states the same rule).

**Deliberate divergence 1: absent vs. indeterminate.** Compose treats every non-key `InvalidOperationException` as
not-found. That is safe on its save path and unsafe here, because
`DataverseServiceClientImpl.RetrieveByAlternateKeyAsync` wraps every failure (absent row, broken key, outage) in one
`InvalidOperationException`. So the inner chain is read instead:

- The ObjectDoesNotExist fault code means absent. This reuses `RecordContainerResolver.IsRecordNotFound`, changed
  from private to internal; it matches the code and not localized message text.
- The client's own "not found with provided alternate key values" literal also means absent.
- Everything else is 503. A cancellation is rethrown as a cancellation, not as a 503.

**Deliberate divergence 2: no #781 self-heal.** Step 1 of the POML says to copy Compose's column-query fallback. Its
constraints say "do not add a competing lookup path that tolerates duplicates", and its escalation trigger says "Do
not add a tolerant secondary lookup" (NFR-07). The fallback picks one row from a duplicated set, which is exactly
that. The POML's steps are directional and its constraints and triggers bind, so the fallback was removed. **This
is CLAUDE.md §6.5 path C: pivot to comply.**

- A duplicated or not-Active key answers 503 "cannot determine", never a guessed row.
- Compose's save path still heals such rows when it touches them, and `scripts/Verify-ComposeIdentityKey.ps1`
  reports the key's health. The 503's log line names that script.
- Removing the fallback also removed two duplicated rules the review flagged as drift risks: the key-fault predicate
  and the canonical-row rule.

**Considered and not reused: `DataverseServiceClientImpl.IsAlternateKeyDuplicate`.** It classifies the CREATE-side
duplicate-key fault (`0x80060892`), and its message fallback matches any text containing "Entity Key". It is the
wrong condition for a retrieve.

**Reused:** `SpeFileStore` (a new OBO facade method), `IGenericEntityService`, `DocumentAuthorizationFilter`
(unchanged), and `AuthorizationService`. **No DI registration** was added (ADR-010).

**Public surface (ADR-038 B8).** B8 bans testing through `InternalsVisibleTo`, and says the compliant fix is a
public surface. So the tested units are public: `SharingUrlToken`, `DriveItemOperations.ResolveAcrossFormsAsync`,
`DocumentUrlIdentityResolution`, and `DocumentUrlIdentityFilter.ResolutionItemKey`. Each has a stated contract. The
BFF is an application assembly with no external consumers, so public adds no API commitment.

## 4. Authorization design (step 6, ADR-008)

Two filters run in registration order. The review confirmed that the first-registered filter is the outermost.

1. **`DocumentUrlIdentityFilter`** resolves the URL to a drive item, then to a `sprk_document`.
   - With no identity it returns `200 resolved:false` itself.
   - Otherwise it writes the resolved id into `RouteValues["documentId"]`, the key
     `DocumentAuthorizationFilter.ExtractResourceId` reads.
   - It **refuses** to run if `id` or `documentId` is already bound. The authorization filter reads `id` first, so
     a future route template that bound one would otherwise get the wrong document authorized.
2. **`DocumentAuthorizationFilter("read")`**: the same filter and operation as `open-links`, unchanged.

The handler runs only after (2) allows, and it is the only place document metadata enters a response. If the two
filters were reversed, (2) would find no id and answer 400 on every call: it fails closed.

**What an unauthorized caller can learn (escalation trigger 2, assessed; not fired).**

- Graph `/shares` runs **as the caller (OBO)**, so it only ever resolves files the caller can already reach.
- Container access is coarser than per-document rights (`FileAccessEndpoints.cs` header), so a caller can reach the
  file yet lack Read on its record. That caller gets 403, where a file with no record answers 200
  `not_spaarke_document`. **The residual is that they learn a Spaarke record tracks a file they can already open.**
  No id, name or related record leaks, and no reason code adds anything.
- The acceptance criterion itself requires the 403, so this residual cannot be closed without changing the
  criterion. It is recorded here rather than argued away.

**How Graph answers are mapped — revised by the live evidence.**

- **401 → 503.** A 401 describes the token, not the item.
- **403 → `not_resolvable`, with a Warning log.** The review had proposed 503, on the theory that a 403 for a file
  the caller has open means a broken environment. **The live run disproved the theory that went with it.** Graph
  answers **403 accessDenied**, not 404:
  - for a path that does not exist inside an SPE container the caller *can* reach, and
  - for a double-encoded spelling of a real file (§8).

  SharePoint does not distinguish "no such item" from "not yours". So a 403 is an answer, and mapping it to 503
  would leave every missing file, and every file in another app's SPE container (e.g. Microsoft Loop), permanently
  "unavailable".
- **What remains:** a broken container-type registration (where every Spaarke file would 403) cannot be told apart
  per request. So each 403 logs a Warning that names the registration as the thing to check. A burst of those
  Warnings alongside Spaarke documents that users can open is the operational signal.

## 5. Encoding (SPIKE-1 link 2) — answered live

`SharingUrlToken.BuildCandidates` tries three spellings, in order, without duplicates:

1. **`encoded`**: each raw path segment of the URL as sent, percent-encoded. It is right for what Office sends
   (a raw path), including a `#` or a literal `%` in a file name.
2. **`normalized`**: `Uri.AbsoluteUri`, right when the caller had already percent-encoded the URL.
3. **`raw`**: the literal spelling the host returned.

**Live answer (2026-09-10, dev, unsampled App Insights):**

- For the Office raw-space URL, the first spelling (`%20`) resolved: `Attempts: encoded=200`. Graph `/shares`
  accepts the percent-encoded form over an SPE `contentstorage` path.
- For the `%20` URL, the first spelling (double-encoded `%2520`) got 403 and the second (`normalized`, `%20`) got
  200.
- Whether Graph would accept a token built over RAW spaces was never reached, because it did not need to be.

**How each answer is classified** (`DriveItemOperations.ResolveAcrossFormsAsync`, unit-tested through a fake fetch):

| Answer | Classification |
|---|---|
| 400 / 404 / 403 | Try the next spelling. If nothing resolves, 400/404 → NotFound and 403 → AccessDenied; the resolver maps both to `not_resolvable` |
| A 200 missing ids | Try the next spelling; if none resolves, Unavailable |
| 401, 429, 5xx, an unparseable error body (Kiota `ApiException`), Polly timeout or open circuit, transport failure, HttpClient timeout | Unavailable, at once |
| Any other exception | Propagates as a 500. It is a defect, not an outage |

## 6. Drive corroboration (SPIKE-1 link 3) — answered live

`sprk_graphitemid_uk` keys on the item id alone. The resolved `parentReference.driveId` is compared, ordinal, with
the row's `sprk_graphdriveid`:

- **A different recorded drive** answers `identity_conflict`.
- **An empty recorded drive** resolves on the item id, with a warning.

**Live:** the probed document resolved, and neither the mismatch Warning nor the empty-drive Warning was logged.
Every row in the query was unsampled (`itemCount = 1`), so none could have been sampled away. The row therefore
carries a recorded drive, and it equals the drive Graph resolved.

## 7. Placement Justification (bff-extensions.md)

| Criterion | Answer |
|---|---|
| Latency budget against BFF state | YES. It runs on the pane's cold path at every document open. |
| Writes BFF-managed state in the request | NO. It is read-only. |
| Retroactive annotation of a stream | NO |
| Event-driven, no synchronous wait | NO. The user is waiting, so Functions is the wrong home. |

- **In the BFF**, in the `/api/documents` group rather than `/api/office`. It extends the existing Graph and
  Dataverse plumbing, it is latency-coupled to the sibling document routes, and it reuses their authorization
  filter verbatim.
- **Not `/api/office`**: nothing in it is Office-specific.
- **No new package**, no new DI registration, and no CRUD→AI dependency.
- **No rate-limit policy**, matching the nine sibling routes. Each call costs up to 3 Graph calls, 1 Dataverse
  lookup and 1 authorization check.

## 8. Live run — dev, 2026-09-10 (deployed `8fec97b2d`, as ralph.schroeder@spaarke.com via az CLI OBO)

Deploy: `Deploy-BffApi.ps1`. The package was 45.37 MB, 4/4 critical files were SHA-256 verified, and `/healthz`
passed.

| # | Input | HTTP | Answer | Graph attempts (App Insights) |
|---|---|---|---|---|
| 1 | Spaarke doc, **raw** (Word-on-web capture) | 200 | resolved → `8c135b45-5da8-f111-aaab-7ced8ddc4a05`, `sprk_matter` **PAT-191111** | `encoded=200` |
| 2 | Same, **`%20`** (BFF open-links form) | 200 | same document | `encoded=403; normalized=200` |
| 3 | Missing file in the same container | 503 → **fixed** | `identity_resolution_access_denied`, which led to the §4 revision; must answer 200 `not_resolvable` after the redeploy | `encoded=403; raw=403` |
| 4 | `file:///C:/…/brief.docx` | 200 | `not_cloud_document` | none (Graph not called) |
| 5, 6 | Empty url / no body | 400 | `document_url_required` ProblemDetails | — |
| 7 | No token | 401 | the route is registered | — |
| — | open-links for the resolved id | 200 | `desktopUrl` = the same file (`…/Document%20Library/Examiner%20report%20draft.docx`) | — |

**Still open:**

- **Word-desktop `document.url` capture** (operator). This is the last SPIKE-1 criterion. Then flip Spike-1 AMBER →
  GREEN.
- **A file that exists but has no `sprk_document`.** This is the only live check of the Dataverse not-found fault
  shape. The Azure CLI cannot list OneDrive (AADSTS65002), and the BFF has no route that lists container children,
  so it needs a URL from the operator: any OneDrive or SharePoint file. A wrong guess about that shape fails safe
  (503), never as a duplicate.

## 9. Step 9.5 review: what changed (2026-09-10)

| Finding | Resolution |
|---|---|
| B8: tests used internals | Tested units made public (§3). No deviation needed. |
| Self-heal = "tolerant secondary lookup" | Removed; path C (§3) |
| Graph 401/403 read as "new" | 401 → 503. 403 → `not_resolvable` + Warning, **after the live evidence** that Graph uses 403 for missing items (§4) |
| Kiota `ApiException`, Polly timeout and circuit escaped as 500 | Classified (§5); `SharedItemResolutionTests` |
| 403 system_failure from authz during an outage | Documented as indeterminate for 013 (§2) |
| "Should not arise" claim | Corrected (§4) |
| `/shares` path in telemetry | Documented as an accepted exposure (§2) |
| Drift-prone predicate and rule copies | Gone with the self-heal (§3) |
| Graph-classification test gap | `SharedItemResolutionTests` |
| Not-found shape unverified live | Needs an operator URL (§8) |
| Drive mismatch read as "new" | `identity_conflict` (§6) |
| `#` and `%` in file names | `encoded` spelling first (§5) |
| Cancellation reported as 503 | Rethrown (§3) |
| Pre-bound `id`/`documentId` | The filter refuses (§4) |
| A 200 missing ids read as not-found | Unavailable (§5) |

**Noted and not changed:**

- Every not-found is logged at Error by the shared `DataverseServiceClientImpl`.
- Malformed JSON gets a bare 400. That is BFF-wide, because there is no `AddProblemDetails`.
- A Graph 429 surfaces as 503, where ADR-019 would say 429.
- The dependency on `RecordContainerResolver` is for the fault predicate only.

All four are shared-code or BFF-wide behaviour outside this task.
