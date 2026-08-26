# Task 079 — Gate the document version routes

> **Executed**: 2026-08-26 · **Rigor**: FULL · opus @ high · Phase 0c Secure Documents — Wave 1
> **Outcome**: SHIPPED. Both routes **re-keyed onto the document row and gated**; the drive-keyed pair
> DELETED; the live caller migrated and verified.

---

## 1. What was wrong

`Api/DocumentVersionEndpoints.cs` mapped two routes keyed by `(driveId, itemId)` straight off the URL:

| Route | Served |
|---|---|
| `GET /api/obo/drives/{driveId}/items/{itemId}/versions` | version history of an arbitrary SPE item |
| `GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content` | **prior-version BYTES** of an arbitrary SPE item |

Both carried only `RequireAuthorization()` ("are you anyone?") + `RequireRateLimiting`. The file's own
header asserted the control:

> *"Per-document authorization is enforced by SharePoint Embedded itself under the user's delegated
> permission"* (`DocumentVersionEndpoints.cs:22-26`, pre-079)

That claim is the defect, not the mitigation. SPE permission is **container-scoped** and coarser than
per-document Dataverse rights, so a caller holding a container ACL passed the "SPE boundary" for
**every document in that container** — including a secure matter's, and including prior-version bytes,
which are exactly as disclosing as current content and frequently contain material later redacted from
the current version.

Severity framing, consistent with task 071 §0: **latent, not exploitable at HEAD**, because under
broker-only no user is granted a container ACL. It was a bypass *by construction* — for anyone who did
hold one, it routed around every per-document gate task 002 added.

---

## 2. The resolution problem, and why re-keying beat resolving

The POML framed the crux correctly: `DocumentAuthorizationFilter` cannot simply be attached, because
`ExtractResourceId` treats `containerId`/`driveId`/`documentId`/`id` interchangeably, so a drive id
reaches `RetrievePrincipalAccess`, which answers None — **denying 100% of callers, legitimate ones
included**. Fail-closed, but not authorization; a broken route.

The POML offered two paths: (step 1) resolve the owning document from `(driveId, itemId)`, or (step 2)
re-key the route onto the document id. **Step 2 is decisively better here, and the deciding fact came
from the caller, not the route:**

`AllDocuments/src/App.tsx:365-384` renders the modal from a **Dataverse `sprk_document` row** — it keys
the list on `doc.sprk_documentid` (`:366`) and was passing `sprk_graphdriveid`/`sprk_graphitemid`
(`:381-382`) purely because that was the route's shape. **The client already held the document id.**
There was no resolution problem to solve; there was a route keyed on the wrong thing.

Re-keying is strictly stronger than resolving would have been:

1. **The resource domain becomes correct by construction.** The filter authorizes an `sprk_document`
   row and the route now names one. No mismatch to work around.
2. **The SPE pointer becomes server-derived.** The caller no longer names the drive/item at all; the
   handler reads them off the authorized row (`ResolveSpePointerAsync`). A caller cannot address an
   arbitrary SPE item, which is a *stronger* property than authorizing a caller-supplied pair.
3. **It closes a hole that resolution would have left open.** The only unique index available for a
   `(driveId, itemId)` → document lookup is `sprk_graphitemid_uk` (used by
   `ComposeService.TryFindDocumentByGraphItemIdAsync:3421`), which is keyed on the **item alone**. A
   resolution-based gate would therefore have authorized by itemId while leaving the supplied
   `driveId` unvalidated — a caller could pair a readable item with a foreign drive.
4. **It removes the standing invitation.** A drive-keyed route, even gated, keeps inviting *"why not
   just grant the user container access?"* — the question broker-only exists to foreclose. This is
   task 071's own reason for preferring deletion over gating, applied unchanged.

The POML's `<escalation><trigger>` — *"if a version item legitimately has no owning `sprk_document`
row, STOP"* — **did not fire.** Every caller of this surface starts from a document row; there is no
unmodelled-content case here. (Contrast task 071's upload trio, where no row exists at authorization
time — that is why those escalated and these did not.)

### Shape shipped

| Route | Gate |
|---|---|
| `GET /api/documents/{documentId}/versions` | `.AddDocumentAuthorizationFilter("read")` |
| `GET /api/documents/{documentId}/versions/{versionId}/content` | `.AddDocumentAuthorizationFilter("read")` |

Handler mirrors `FileAccessEndpoints.GetContent` exactly: parse id → `GetDocumentAsync` → validate SPE
pointers → call the OBO facade with the **row's** pointers. Fails closed on every path (unparseable id
400, missing row 404, unusable/non-`b!` pointer 409/400) with **no fallback** to a caller-supplied
drive/item or to container permission (ADR-003).

---

## 3. Operation key: `read` — decided, not defaulted

**No new policy key was needed.** `["read"] = AccessRights.Read` already exists.

The parent flagged this as a real judgment call, noting `["download_file"] = Write` and
`["driveitem.content.download"] = Write` ("Download requires Write ... for security compliance"), and
task 072's precedent that publishing a durable handle is a different act from reading. So: do
prior-version BYTES deserve more than `read`?

**No — and the deciding principle is parity with the current-version download on the same surface.**

`FileAccessEndpoints` gates `GET /api/documents/{documentId}/download` and `/{documentId}/content` —
the routes that stream the **current** bytes of a document — at `"read"` (`:86`, `:170`). Prior-version
bytes are *equally*, not *more*, disclosing. Gating history more strictly than the current version
would deny legitimate readers while stopping no attacker, who would simply take the current version
instead. That is security theatre, and it would also break the shipped version-history UI for every
read-only user.

Why the `Write` entries do not govern: `driveitem.content.download` / `download_file` belong to the
legacy **SPE-resource** family, which describes an SPE *item*. `DocumentAuthorizationFilter` authorizes
a Dataverse **row**, and the record-scoped convention (task 003, spelled out on the `["read"]` entry) is
deliberately a bare name — *"reusing a `driveitem.*` key here would misdescribe the resource"*. Task
072 chose `share` for share-link because minting a credential that outlives revocation genuinely is a
different act; streaming bytes to an authenticated, revocable caller is not.

Per CLAUDE.md §11, a new key carrying the same required right would change no decision, so none was
added.

---

## 4. Live caller migrated (`versionHistory.ts`)

| File | Change |
|---|---|
| `src/solutions/AllDocuments/src/versionHistory.ts` | `versionsPath(driveId,itemId)` → `versionsPath(documentId)`; `listVersions(documentId)`; `fetchVersionBytes(documentId, versionId)`; `openPriorVersionReadOnly(documentId, versionId, …)`. Header's "SPE layer enforces per-document authorization" claim corrected. |
| `src/solutions/AllDocuments/src/VersionHistoryModal.tsx` | props `driveId`/`itemId` → `documentId`; both callbacks + dep arrays |
| `src/solutions/AllDocuments/src/App.tsx` | passes `versionHistoryDoc.sprk_documentid`. The SPE-pointer render guard is KEPT as pure UX (don't offer history for a document with no file, which the server answers 409) with a comment stating it is **not** the control. |
| `src/solutions/AllDocuments/src/__tests__/VersionHistoryModal.test.tsx` | new paths; negative test strengthened to assert no URL ever matches `/api/obo/drives/` |

### How the live caller was verified (three independent ways)

1. **Type check** — `npx tsc --noEmit` in `src/solutions/AllDocuments`: **zero errors in changed code**
   (no prop or arity mismatch). Only pre-existing `Cannot find module '@spaarke/*'` workspace-link
   errors remain.
2. **Behavioural** — a focused throwaway jest suite against `versionHistory.ts` (which imports only the
   mocked `@spaarke/auth`, so it bypasses the pre-existing shared-lib resolution failure below): **3/3
   pass**, proving it requests exactly `/api/documents/{id}/versions` and
   `/api/documents/{id}/versions/3.0/content`, never `/api/obo/drives/`, and still opens the blob.
   Deleted before commit — it duplicated the permanent modal test's coverage.
3. **Both-ends contract match** — the server tests hit those same literal paths and return 200 with the
   exact prior-version bytes. Client and server were verified against the *same strings* from opposite
   sides, not against each other's descriptions.

⚠️ **Pre-existing breakage found, NOT caused by this task**: `VersionHistoryModal.test.tsx` cannot
execute at all in this workspace — the suite fails to load with `Cannot find module
'@fluentui/react-icons' from Spaarke.UI.Components/src/icons/SprkIcons.tsx`, because the
`@spaarke/ui-components` `file:` link resolves into the shared lib's **source** without its own
`node_modules`. **Verified pre-existing by stashing all my changes and re-running: identical failure on
unmodified HEAD.** Consequence worth flagging: the task-079 client regression assertion I added to that
file is currently unenforced. Fixing it means touching shared monorepo build wiring — out of scope
here, recommended as a follow-up.

---

## 5. Seam suite migrated — and a test that passed for the wrong reason

`tests/integration/seam/Compose/SpeVersionHistoryOboSeamTests.cs` (ADR-038 KEEP path,
`integration/seam/**`) exercised the deleted routes; 5 of its 6 tests failed after the change. It was
**migrated, not deleted** — it uniquely proves the facade/addressing contract.

Its header also had to be corrected: it asserted *"the SPE layer IS the authorization boundary, not a
post-hoc filter"* — the exact claim this task disproves. Denial is now owned by
`DocumentVersionAuthorizationTests`; this file's caller is deliberately authorized, so its assertions
are about the facade and addressing.

**`ListVersions_ItemNotFound_Returns404` was the 6th test — it did NOT fail, and that was worse.** Its
route had been deleted, so `404` meant *"not routed"* rather than *"the facade returned null"*. It was
passing vacuously. Migration added a `Verify(..., Times.Once)` so the 404 now proves the request
reached the facade. Worth remembering: a route-absence 404 silently satisfies any not-found assertion.

New fixture `DocumentVersionSeamFixture : ComposeFidelitySeamFixture` adds the two doubles the gate
needs (stated-rights `IAccessDataSource` granting **Read only** — not full rights, so a route
accidentally gated on Write would still fail — plus an `IDocumentDataverseService` mapping the seam
document ids to the SPE pointers the existing `SpeMock` setups are keyed on, which is why the Moq
setups needed no rewrite).

This required **unsealing** `ComposeFidelitySeamFixture` (one word, no registration change, zero
behaviour change for existing consumers) rather than forking a second Compose seam host — CLAUDE.md §11,
following the explicit precedent on `DocumentDestroyAuthorizationTestFixture` ("Not sealed — a later
task will want to extend this rather than fork it"). Every other Compose seam test still uses the base
type unchanged, so the two extra registrations have zero blast radius.

Also note `DriveId` had to become **`b!`**-prefixed: the route validates SPE drive-id format, so the old
`"drive-050-version-history"` fixture value would have 400'd every authorized test for an unrelated
reason.

---

## 6. Tests added

`tests/integration/auth/UnifiedAccessControl/DocumentVersionAuthorizationTests.cs` (10 tests). The
byte source substituted is **`ISpeFileOperations`**, not `SpeFileStore` — the two version methods on the
concrete facade are **not `virtual`**, so they cannot be overridden, and the interface is the actual
boundary the handler calls through. Registered **Scoped** (a Singleton factory would receive the root
provider and 500 on the *authorized path only*, leaving a green denial suite beside a broken feature).

| Test | Load-bearing assertion |
|---|---|
| `ListVersions_WhenCallerHasNoRightsOnTheDocument_IsDeniedAndReadsNothing` | `VersionListReads` empty |
| `OpenPriorVersion_WhenCallerHasNoRightsOnTheDocument_IsDeniedAndServesNoBytes` | **`VersionByteReads` empty** + response body contains no byte marker |
| `OpenPriorVersion_WhenTheAccessCheckThrows_DeniesAndServesNoBytes` | ADR-003 fail-closed; no bytes |
| `{ListVersions,OpenPriorVersion}_WithNoToken_…` | nothing reached the facade |
| `ListVersions_WhenCallerHoldsRead_ReturnsTheHistoryNewestFirst` | positive control |
| `OpenPriorVersion_WhenCallerHoldsRead_StreamsTheExactPriorVersionBytes` | exact bytes returned; **pointer recorded is the row's `(b!drive-…, item-…, 3.0)`, proving it was server-derived** |
| `RetiredDriveKeyedVersionRoute_…_Returns404NotRouted` ×2 | deleted pair is not routed |
| `SurvivingVersionRoute_WithoutBearer_Returns401NotFound` | positive control making the two 404s non-vacuous |

Per the parent's bar, **the load-bearing assertion is that the bytes were never fetched, not the status
code** — a 403 rendered after the stream opened is not a denial.

---

## 7. Perturbation verification (three separate perturbations, all restored)

| Perturbation | Result |
|---|---|
| Remove **both** `.AddDocumentAuthorizationFilter("read")`, rebuild, re-run | **Exactly the 3 per-document denial tests went RED** (`ListVersions…NoRights`, `OpenPriorVersion…NoRights`, `…AccessCheckThrows`); the 7 others stayed green. The no-token tests correctly stayed green — they guard the group-level `RequireAuthorization()`, a different mechanism. Gates proven load-bearing. |
| Same, against task 074's ArchTest | Rule A **FAILED**, naming `GET /api/documents/{documentId}/versions` and `GET /api/documents/{documentId}/versions/{versionId}/content`. Proves (a) the scanner parses both new registrations, (b) the old drive-keyed waivers do **not** cover them, so the **gates** are what keeps 074 green — not a waiver. |
| Restore | 10/10 auth, 8/8 seam, 10/10 ArchTest route guard. |

`dotnet build src/server/api/Sprk.Bff.Api/` was run explicitly before **every** test run (the task-072
stale-assembly lesson).

---

## 8. Verification results

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ succeeded, **0 warnings, 0 errors** |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | ✅ succeeded, 0 warnings, 0 errors |
| New auth suite | ✅ **10/10** |
| Migrated seam suite | ✅ **8/8** (was 6 tests, now 8) |
| **Full BFF suite** | ✅ **11,184 passed / 0 failed / 82 skipped** — baseline 11,172 + **12** net new (10 auth + 2 seam) = exact match. 82 skips all pre-existing. |
| ArchTests (whole project) | ⚠️ **9 failed / 105 passed** — the **known master baseline**, unchanged. None is a Task 074 rule. Nothing beyond the 9. |
| ArchTests task 074 route guard | ✅ **10/10** |
| Publish size (compressed, `deploy/api-publish/`, 215 files) | ✅ **45.09 MB incl. PDBs** / 44.16 MB excl. Baseline 45.08 MB incl. → **+0.01 MB**. Ceiling 60 MB. |
| `dotnet list package --vulnerable --include-transitive` | ✅ **no vulnerable packages, any severity**, across all 12 projects. No package-graph change. |
| `GrantMembershipAsync` callers | ✅ **zero** — only its own definition (`SpeContainerMembershipService.cs:59`) + doc mentions |
| `npx tsc --noEmit` (AllDocuments) | ✅ zero errors in changed code |

### One flaky test, diagnosed not hand-waved

`TenantCacheMetricsTests.GetAsync_MissThenHit_IncrementsMissesThenHits` failed on **one** of three full
runs (passed on the other two, same code). It is **structurally order-dependent and unrelated to this
task**: it attaches a process-wide `MeterListener` to the **static** `CacheMetrics.HitsCounter` /
`MissesCounter` and asserts **exact equality** (`misses.Should().Be(1)`), while xUnit runs other test
classes in parallel — any concurrent cache I/O corrupts the count. Passes in isolation.

Honest caveat: this task adds two `WebApplicationFactory` fixtures, so it plausibly *increases the
probability* of the race firing without being its cause. The defect is asserting exact equality against
a global counter. Recommend a follow-up making it delta-based or collection-serialized.

---

## 9. Step 9.5 quality gates

### `code-review`

**Complexity/cohesion (CLAUDE.md §11.5)** — `DocumentVersionEndpoints.cs` 126 → 268 lines, of which
**~95 are the class-doc header** and ~55 the `ResolveSpePointerAsync` helper + its remarks. Executable
code grew by roughly 40 lines: the document read, pointer validation, and gate wiring. Two routes, one
responsibility (read-only version access for a document), one private helper. Cohesive; no
decomposition warranted.

The long header is deliberate and load-bearing: the "do NOT re-add a drive- or container-keyed version
route" instruction has to sit **at the point of temptation**, not only in a notes file — this file was
missed by *every* prior enumeration of the document surface precisely because nothing in it said so.

**AI code-smell scan** — clean: no interface-with-single-impl, no try/catch-log-rethrow (the two
surviving catches return `ProblemDetails` and are unchanged in behaviour), no null-checks on
non-nullable, no code-restating comments (every comment states *why*).

**Two findings, both fixed during review:**
1. The recording SPE double initially implemented a *guessed* `ISpeFileOperations` surface (wrong
   `VersionInfoDto` construction — it is a positional record — and 5 missing members including
   `ResolveDriveIdAsync` and the subscription/delta family). Caught by reading the real interface
   rather than trusting the guess; compile would have failed anyway, but the `VersionInfoDto` shape
   would have been a silent semantic mismatch.
2. The seam fixture's access-source double implemented only `GetUserAccessAsync`, missing
   `GetRecordAccessAsync` (task 070's entity-agnostic path). Both now return the **same** answer so the
   two paths cannot disagree.

**Accepted with rationale (not defects):**
- `ResolveSpePointerAsync` duplicates the *checks* in `FileAccessEndpoints.ValidateSpePointers` rather
  than sharing it. That helper is `private static` in another file; hoisting it into a shared utility
  would edit a hot shared surface mid-wave for no behavioural gain. Divergence risk is bounded by using
  the **same error codes** (`invalid_id`, `document_not_found`, `no_file_attached`,
  `mapping_missing_drive`, `invalid_drive_id`), so the two routes report pointer problems with the same
  contract as their eight siblings on `/api/documents`. Extraction is a reasonable follow-up.
- A second `MapGroup("/api/documents")` exists (one here, one in `FileAccessEndpoints`). ASP.NET permits
  this and the route templates are disjoint (`/{documentId}/versions…` vs `/{documentId}/content` etc.).
  Keeping version routes in their own file preserves the task-050 scope boundary.

### `adr-check`

| ADR | Verdict |
|---|---|
| ADR-001 Minimal API | ✅ preserved; no controller |
| ADR-003 fail-closed | ✅ every resolution failure denies; **no fallback** to caller-supplied pointers or container permission. Proven by the throwing-check test. |
| ADR-007 Graph isolation | ✅ no `Microsoft.Graph` type in scope; facade returns `VersionInfoDto`/`Stream` |
| ADR-008 endpoint-filter auth | ✅ **now compliant** — both routes carry a per-resource filter. This task *removes* an ADR-008 gap rather than adding one (contrast task 071, which needed a §6.5 Path A exception for its surviving upload trio). |
| ADR-009 Redis-first | ✅ no `IMemoryCache` |
| ADR-010 DI minimalism | ✅ **no new interface, no new service, no new DI registration** in production code |
| ADR-013 AI facade | ✅ no AI-internal type |
| ADR-019 ProblemDetails | ✅ `SdapProblemException` rendered by the global handler |
| ADR-028 OBO | ✅ SPE read stays OBO (`*AsUserAsync`), never app-only; no secret construction |
| ADR-038 testing | ✅ new tests at protected KEEP paths (`tests/integration/auth/**`, `tests/integration/seam/**`); negative **and** positive controls; no banned shape (`Mock<HttpMessageHandler>`, DI-registration, ctor-null, `Stopwatch`). Seam file migrated in-place, not deleted. |

**No ADR conflict; CLAUDE.md §6.5 not invoked.** Path C (comply) was available and correct.

---

## 10. BFF hygiene (root CLAUDE.md §10)

**Placement Justification.** No new component. This modifies two existing routes in an existing BFF
endpoint file and adds **zero** services, interfaces, DI registrations, packages, and background work.
Placement in the BFF is not merely correct but forced: the decision being added *is* the BFF
authorization boundary, which on the SPA/Teams surface is the entire security boundary (project
CLAUDE.md fact 1). It cannot live client-side, and Dataverse cannot enforce it because the SPE read is
brokered.

- Publish size: **45.09 MB incl. PDBs**, +0.01 MB vs 45.08 baseline. Ceiling 60. ✅
- New HIGH CVE: none — no package-graph change. ✅
- Test obligation (§F): tests added at KEEP paths in the same change; every test referencing the old
  routes migrated in the same change. ✅
- Registration symmetry (§F.1): routes map **unconditionally**; both backing services
  (`ISpeFileOperations`, `IDocumentDataverseService`) are registered unconditionally. No feature gate,
  so no asymmetric-registration risk. ✅

⚠️ **Side effect**: the publish-size check writes to `deploy/api-publish/`, which
`.claude/constraints/azure-deployment.md` mandates as the only permitted publish location. That path is
shared with the main session; this publish overwrote whatever was there. No deploy performed. (Same
note as task 071 §6c.)

---

## 11. Acceptance criteria

| Criterion | Status |
|---|---|
| A caller without Read on the owning document is denied on BOTH version routes — proven by tests that fail without the change | ✅ 3 denial tests; **perturbation-proven** to fail with the gates removed (§7) |
| Unresolvable `(driveId, itemId)` pairs DENY; no fallback to container permission | ✅ Superseded in the stronger direction — the caller can no longer supply a drive/item **at all**. Unresolvable *documents* deny (400/404/409), and the throwing-check test proves an errored decision fails closed. |
| Version history still works for an authorized caller (`versionHistory.ts` verified) | ✅ three ways (§4) |
| Task 074's two waivers for these routes are DELETED and its suite stays green | ⚠️ **Waivers NOT deleted by me — `tests/Spaarke.ArchTests/**` is parent-owned.** Both are now dead entries; exact text in §12. Suite is green (10/10) and perturbation-proven to be green *because of the gates*, not the waivers. |
| `GrantMembershipAsync` still has zero callers | ✅ verified |

---

## 12. 🔔 SHARED-FILE CHANGES NEEDED FROM THE MAIN SESSION

I did not edit these. **No `OperationAccessPolicy` change is required** — `["read"]` already exists.

### 12.1 `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` — DELETE both 079 waivers (lines 249-258)

Both routes no longer exist, so these are dead entries that make the outstanding-work list overstate
itself. `NoWaiverIsStale` does **not** catch them — it only flags a waiver whose route became *gated*,
and a **deleted** route is never scanned (the same gap task 071 §6a flagged for its four). Delete:

```csharp
// ---------- PENDING — task 079, found by task 071's inventory ----------
new Waiver("GET /api/obo/drives/{driveId}/items/{itemId}/versions", WaiverKind.Pending, "079", …),
new Waiver("GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content",
    WaiverKind.Pending, "079", …),
```

### 12.2 Same file — update the `GovernedFile` reason string (lines 106-107)

Currently reads *"drive-keyed version history and prior-version BYTES. Same shape as the OBO routes."* —
all three claims are now false. Suggested:

```csharp
new GovernedFile("Api/DocumentVersionEndpoints.cs", Scope.RouteLevelGate,
    "document-id-keyed version history and prior-version BYTES, both gated \"read\" by task 079. "
    + "The drive-keyed pair was DELETED; the SPE pointer is now read off the authorized row."),
```

### 12.3 `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs:157` — stale doc comment

Reads *"the projection backing GET /api/obo/drives/{driveId}/items/{itemId}/versions"*. That route is
deleted; it now backs `GET /api/documents/{documentId}/versions`. (I fixed the identical comment on
`ISpeFileOperations.cs:100`, which is not on the shared list.)

### 12.4 Consider extending `NoWaiverIsStale`

Task 071 §6a recommended it and this task is the second instance: also flag a waiver whose route is
**absent from the scan entirely**. Two tasks running have now each left dead waivers that the rule
cannot see.

---

## 13. Residual risk, stated plainly

1. **Coordinated deploy required.** The BFF and the `AllDocuments` Code Page must ship together. If the
   BFF deploys first, a user on a cached older bundle gets **404** on version history until the web
   resource updates; if the page deploys first, likewise until the BFF updates. This is a **transient
   feature outage on one modal, not a disclosure**. I judged it worth accepting over permanently
   keeping a second, drive-keyed shape (see §2.4). Flag it in the release note.
2. **The client regression assertion I added is currently unenforced** — `VersionHistoryModal.test.tsx`
   cannot load in this workspace (pre-existing, §4). Verified independently via the throwaway suite, but
   nothing guards it in CI until the monorepo jest wiring is fixed.
3. **`read` is a deliberate parity choice, not a proof.** If the product later decides byte egress needs
   more than Read, the change must be made on **both** the current-version download and the
   prior-version download together. Splitting them re-creates the incoherence: history harder to obtain
   than the live document.
4. **`ComposeFidelitySeamFixture` is now unsealed.** Behaviour-neutral today, but it means future tasks
   can extend the Compose seam host. That is the intent; it does slightly widen that fixture's contract.
5. **Not audited by this task**: whether other surfaces read prior-version bytes through the facade
   directly (e.g. Compose's own `DownloadFileVersionAsUserAsync` calls in `ComposeService`). Those are
   in-process facade calls, not routes, so they are outside this task's route-level scope — but they are
   byte reads keyed by `(driveId, itemId)` and nobody has enumerated their authorization story. Worth a
   follow-up: **the technique that found these two routes was a CALLER inventory, not a route
   inventory** (task 071's method), and the same technique applied to facade *methods* rather than
   routes is the obvious next sweep.

---

## 14. Note for the record

The POML's closing note is right and worth repeating: these routes *"were missed by every prior
enumeration of the document surface because they live in a version-specific file rather than in
`OBOEndpoints.cs` or `FileAccessEndpoints.cs`."* Task 071 found them by inventorying **callers**, not
routes. This task found two further things the same way — the vacuously-passing 404 test (§5) and the
client's already-available document id (§2) — both by starting from the consumer rather than the
producer. Route inventories find routes; caller inventories find *assumptions*.
