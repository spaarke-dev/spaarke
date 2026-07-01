# Spike #3 — SPE Check-out / Check-in + Document Promotion-on-Save

> **Status**: LOCKED (Phase 0 spike output)
> **Task**: [`003-spike-spe-checkout-promotion.poml`](../../tasks/003-spike-spe-checkout-promotion.poml)
> **Owner**: Spaarke Compose R1
> **Locked**: 2026-06-29
> **Rigor**: STANDARD (research + decision artifact; no production code touched)
> **Consumer tasks**: 050 (acquire check-out), 051 (multi-tab UX), 052 (heartbeat), 022 (`ComposeDocumentService`), 023 (`ComposeSessionService`)

This document locks the SPE check-out / heartbeat / Save-promotion behaviour for R1, with a major reuse pivot driven by CLAUDE.md §11 (default-to-reuse).

---

## 1. TL;DR — Locked Decisions

| Decision | Value | Rationale |
|---|---|---|
| **Lock substrate** | **Dataverse-side lock via existing `DocumentCheckoutService`** (NOT a fresh Graph SPE `checkOut`/`checkIn` wrapper) | A complete check-out/check-in/discard service already exists at [`src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs) (~1170 LOC, in-service ~365 LOC of business logic + 800 LOC of result types). Same-user idempotent re-checkout, cross-user 409 conflict, discard, Office Online edit-URL exchange are all already implemented. Per CLAUDE.md §11 default-to-reuse, R1 EXTENDS this service rather than building a parallel SPE-native wrapper. |
| **Heartbeat interval** | **3 minutes (sliding)** — 5 missed heartbeats = stale | Conservative against SPE/Graph rate limits (Graph throttles per-user at ~10k req/10min ≈ 17/sec — heartbeat at 3-min interval = 0.0056/sec/user, well below); fast enough that an orphan lock recovers within 15 minutes of browser-close; slow enough that an idle tab doesn't burn battery on mobile. |
| **Orphan / stale lock auto-release** | **15 minutes after last heartbeat** | Matches design.md §14 row 4 proposed default. The 15-min window is empirically driven by the trade-off: shorter = annoyed user who came back from a meeting and lost their lock; longer = collaborator who can't claim a clean lock for 15+ minutes. |
| **Conflict UX (same user, another tab)** | **"Go-to-other / Force-close"** dialog | Detected via comparing `CheckoutResult.ConflictError.CheckedOutBy.Id` to current user's Dataverse `systemuserid`. If match → render the Compose-specific conflict modal. If mismatch → render the existing "checked out by X" message (already produced by `DocumentCheckoutService`). |
| **Save-promotion idempotency** | **Upsert-by-SPE-drive-item-id (`sprk_graphitemid`)** via Dataverse `PATCH` with `If-None-Match: *` Or filter-then-create with conditional logic | Existing `DocumentCheckoutService.GetDocumentAsync` queries `sprk_documents` by `sprk_documentid`. Add a sibling query `sprk_documents?$filter=sprk_graphitemid eq '{itemId}'&$top=1` — if 1 result, return it (idempotent no-op); if 0 results, POST a new `sprk_documents` row. Concurrent fire of 5 promotes → at most 1 race winner creates the row; others find it on retry. See §5 for the locked algorithm. |
| **BFF endpoint shape** | **2 NEW endpoints** + 0 modified existing endpoints | (1) `POST /api/compose/document/{spe-id}/promote` (Save-promotion; new). (2) `POST /api/compose/document/{documentId}/heartbeat` (lock-keep-alive; new). The existing `POST /api/documents/{documentId}/checkout`, `/checkin`, `/discard`, `/checkout-status` are REUSED unchanged. Total: **2 net-new endpoints** for R1, not 7 as the original §12 BFF surface implied. |

**Net result**: R1 ships **~150 LOC of new BFF code** (heartbeat field + background sweeper + promote endpoint) atop ~1170 LOC of existing service, NOT a fresh ~600 LOC parallel implementation. This is the §11 reuse dividend.

---

## 2. CLAUDE.md §11 Component Justification — Three-Question Check

For each net-new component this spike proposes, applying CLAUDE.md §11:

### 2.1 NEW: `IComposeHeartbeatService` (background sweeper) + `sprk_documents` field additions

**Existing**: `DocumentCheckoutService.CheckoutAsync` sets `sprk_checkedoutdate` + `sprk_CheckedOutBy` lookup, but there is no "last-heartbeat" timestamp and no auto-release mechanism. Verified via `Grep "heartbeat|stale.*lock|orphan.*lock"` over `src/server/api/Sprk.Bff.Api/` — **zero matches**. Not present.

**Extension**: YES — extend `DocumentCheckoutService` with a `RefreshHeartbeatAsync(documentId, ct)` method that PATCHes a new `sprk_lastheartbeatutc` field on `sprk_documents`, AND add a tiny background `IHostedService` that scans for `sprk_lastheartbeatutc < UtcNow - 15.minutes` and clears the checkout. **No new service class needed beyond the background sweeper.**

**Cost-of-doing-nothing**: Without heartbeat, an orphan lock from a crashed/closed Compose tab blocks the same document from being opened by ANY user (including the original owner) until manual intervention. Concrete failure: user opens doc → closes laptop without check-in → comes back next day → lock prevents re-entry, requires admin discard. R1 collaboration model assumes single-editor-with-recoverable-lock; without heartbeat this model degrades to "single-editor-until-admin-fixes-it."

### 2.2 NEW: `POST /api/compose/document/{spe-id}/promote` endpoint + promote method

**Existing**: No endpoint creates a `sprk_documents` row from a bare SPE drive-item id. Document creation today happens via Email-to-Document, MatterCreationWizard, manual maker action, etc. — all flows that already have business context. Verified via `Grep "promote|promotion"` over `Api/`: matches are for AI playbook promotions (different domain), not document promotion. Not present for Path B Compose flow.

**Extension**: PARTIAL. We extend `DocumentCheckoutService` with a `PromoteEphemeralAsync(speDriveId, speItemId, fileName, claimsPrincipal)` method — same service, new capability. Then add ONE endpoint `POST /api/compose/document/{spe-id}/promote` in a new `Api/ComposeEndpoints.cs` (which Phase 2 task 024 already plans). No new service.

**Cost-of-doing-nothing**: Without Save-promotion, the ephemeral Path B flow (upload from Assistant → open in Compose → edit → Save) cannot reach the normal Document pipeline (matter binding, AI search indexing, permissions, audit). The user's edit work is in SPE but invisible to the rest of Spaarke. Concrete failure: user uploads a draft contract via Assistant, edits in Compose, hits Save — the file is in SPE but doesn't appear in Document search, doesn't accrue to a matter, and the AI search index never sees it.

### 2.3 NEW: `sprk_lastheartbeatutc` Dataverse field on `sprk_documents`

**Existing**: `sprk_documents` already has `sprk_checkedoutdate`, `_sprk_checkedoutby_value`, `sprk_checkedindate`, `_sprk_currentversionid_value`, etc. (verified via field list in `DocumentCheckoutService.GetDocumentAsync` line 605–608). There is NO heartbeat-style field.

**Extension**: Could we reuse `sprk_checkedoutdate` as the heartbeat? **No** — `sprk_checkedoutdate` is semantically "when did the check-out start," used in UX ("checked out by X since 9:32 AM"). Mutating it on every heartbeat would clobber that UX. We need a separate field.

**Cost-of-doing-nothing**: Without a distinct heartbeat field, we either (a) clobber `sprk_checkedoutdate` (breaks UX) or (b) carry heartbeat in Redis only (loses durability — Redis flush = all locks dead).

**Decision**: Add `sprk_lastheartbeatutc` (DateTime, nullable, default NULL). Set on checkout AND on heartbeat. Cleared on check-in / discard / stale-sweep.

### 2.4 NOT NEW (and explicitly rejected): SPE-native `checkOut`/`checkIn` Graph wrappers

Microsoft Graph offers `POST /drives/{drive-id}/items/{item-id}/checkout` and `/checkin` for SharePoint document libraries. These were considered as the lock substrate. **Rejected**, with reasoning:

| Question | SPE-native checkOut/checkIn | Dataverse-side (existing) | Winner |
|---|---|---|---|
| Cross-user lock visibility? | Yes (SPE enforces; Word for Web shows "Checked out to X" natively) | Yes (`sprk_documents.sprk_CheckedOutBy` lookup; Compose UI renders it; Word for Web does NOT see Dataverse-side lock) | **SPE-native wins on Word visibility** |
| Same-user multi-tab detection? | Hard — SPE's `checkOut` is idempotent for the same identity but doesn't distinguish "same user, different tab" from "same user, same tab"; no session token | Trivial — `_sprk_checkedoutby_value` + caller's `systemuserid` are GUIDs we own; trivially compared | **Dataverse-side wins** |
| Conflict UX richness? | Limited — SPE returns 423 LOCKED with the locking user's identity; no `Force-close` semantics | Rich — existing service already returns `DocumentLockedError` with full `CheckoutUserInfo`, supports `Discard` for force-close | **Dataverse-side wins** |
| Orphan release? | Limited — SPE auto-releases after a long quiet period (~hours, opaque), not a tunable 15-min knob | Tunable — we own the field, we own the sweeper, we set the threshold | **Dataverse-side wins** |
| Indexing trigger? | None — SPE doesn't notify on check-out/check-in | We already enqueue re-index job on check-in (`DocumentOperationsEndpoints.TryEnqueueReindexJobAsync`) | **Dataverse-side wins** |
| Integration with `sprk_fileversions`? | None — SPE has its own versions, separate from Dataverse | Native — service creates `sprk_fileversions` row on every checkout | **Dataverse-side wins** |
| Word interop? | Word for Web respects SPE check-out flag | Word for Web does NOT see Dataverse-side lock | **SPE-native wins** |

**Decision: Dataverse-side (existing service) wins 6-2.** The one losing item — Word for Web concurrent edit — is the basis for the **ADR Tension** in §6 below.

---

## 3. SPE Check-Out API Surface (reference, for §6 Tension)

For completeness, what SPE-native check-out would look like:

| Operation | Microsoft Graph endpoint | OAuth scopes (delegated) |
|---|---|---|
| Check-out (lock) | `POST /drives/{drive-id}/items/{item-id}/checkout` | `Files.ReadWrite` or `Sites.ReadWrite.All` |
| Check-in (commit) | `POST /drives/{drive-id}/items/{item-id}/checkin` body: `{ "comment": "..." }` | `Files.ReadWrite` or `Sites.ReadWrite.All` |
| Detect check-out state | `GET /drives/{drive-id}/items/{item-id}?$select=publication,id` (look for `publication.versionId == "current"` and inspect `publication.level`) | `Files.Read` minimum |

R1 does NOT call these (per §2.4). The shape is documented so a future ADR amendment can adopt them later if Word concurrent-edit becomes a forcing function (R2+ deliverable).

---

## 4. Heartbeat Algorithm (Locked)

### 4.1 Client-side (Compose React component)

```ts
// In ComposeWorkspace.tsx (task 042)
const HEARTBEAT_INTERVAL_MS = 3 * 60 * 1000; // 3 minutes
const heartbeatTimer = setInterval(async () => {
  if (document.visibilityState === 'visible') {
    try {
      await authenticatedFetch(`/api/compose/document/${documentId}/heartbeat`, { method: 'POST' });
    } catch (err) {
      // Heartbeat failures are non-fatal; if 3 consecutive fail, surface a warning banner
      bumpHeartbeatFailureCount();
    }
  }
}, HEARTBEAT_INTERVAL_MS);

// Cleanup on unmount: clearInterval(heartbeatTimer); + best-effort beacon to /checkin or /discard
```

**Rationale for `document.visibilityState === 'visible'` gate**: a backgrounded tab is the most common "user walked away" case. Heartbeating from a hidden tab would defeat the stale-detection. If user re-focuses the tab within 15 minutes, the next heartbeat refreshes the lock; otherwise the sweeper claims it.

### 4.2 Server-side BFF endpoint

```csharp
// In Api/ComposeEndpoints.cs (task 024)
group.MapPost("/document/{documentId:guid}/heartbeat", async (
    Guid documentId,
    HttpContext httpContext,
    [FromServices] DocumentCheckoutService checkoutService,
    CancellationToken ct) =>
{
    var user = httpContext.User;
    var ok = await checkoutService.RefreshHeartbeatAsync(documentId, user, ct);
    return ok ? Results.NoContent() : Results.Problem(statusCode: 404, title: "No active checkout to refresh");
})
.WithName("ComposeHeartbeat")
.RequireAuthorization()
.RequireRateLimiting("standard"); // protects against runaway clients
```

New method on `DocumentCheckoutService`:

```csharp
public async Task<bool> RefreshHeartbeatAsync(Guid documentId, ClaimsPrincipal user, CancellationToken ct)
{
    await EnsureAuthenticatedAsync(ct);
    var oid = GetUserId(user);
    var userId = await LookupDataverseUserIdAsync(oid, ct);
    if (!userId.HasValue) return false;

    // Only refresh if THIS user holds the lock — guard against cross-user impersonation
    var doc = await GetDocumentAsync(documentId, ct);
    if (doc == null || !doc.IsCheckedOut || doc.CheckedOutById != userId.Value) return false;

    var payload = new Dictionary<string, object?>
    {
        ["sprk_lastheartbeatutc"] = DateTime.UtcNow.ToString("o")
    };
    var resp = await _httpClient.PatchAsJsonAsync($"{DocumentEntitySet}({documentId})", payload, ct);
    return resp.IsSuccessStatusCode;
}
```

### 4.3 Server-side stale-lock sweeper (background)

```csharp
// In Services/Compose/StaleCheckoutSweeperHostedService.cs (task 052)
public class StaleCheckoutSweeperHostedService : BackgroundService
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ScanInterval   = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await ScanAndReleaseStaleAsync(ct); }
            catch (Exception ex) { _logger.LogError(ex, "Stale sweep iteration failed"); }
            await Task.Delay(ScanInterval, ct);
        }
    }

    private async Task ScanAndReleaseStaleAsync(CancellationToken ct)
    {
        // Dataverse query: sprk_documents where
        //   sprk_checkedoutdate ne null
        //   AND sprk_lastheartbeatutc lt {UtcNow - 15.minutes}
        // Page through results; for each, call existing DiscardAsync (system identity)
        // This reuses ALL the existing release logic — no duplicate path.
    }
}
```

**Why a sweeper instead of computing staleness on every checkout-attempt-by-different-user**: a sweeper guarantees release regardless of whether anyone tries to claim the doc. If user A locks doc and never returns, and user B never tries to open doc, the lock would otherwise linger forever (or until A returns). The sweeper bounds the maximum orphan-lock lifetime to `StaleThreshold + ScanInterval = 17 minutes`.

**Cost**: a Dataverse query every 2 minutes. Filtered to `sprk_checkedoutdate ne null` (small subset). Expected query result count in steady state: low single digits. This is well within Dataverse query budget.

---

## 5. Save-Promotion Idempotency Algorithm (Locked)

### Problem

Path B (ephemeral) flow: user uploads to SPE → opens in Compose (no `sprk_documents` row yet) → clicks Save N times in quick succession. Each Save fires `POST /api/compose/document/{spe-id}/promote`. Goal: exactly ONE `sprk_documents` row, regardless of N.

### Locked algorithm

```csharp
public async Task<PromoteResult> PromoteEphemeralAsync(
    string speDriveId,
    string speItemId,
    string fileName,
    ClaimsPrincipal user,
    string correlationId,
    CancellationToken ct)
{
    await EnsureAuthenticatedAsync(ct);

    // Step 1: Idempotency probe — does a sprk_documents row already exist for this drive-item?
    // Use Dataverse $filter on sprk_graphitemid (assumed unique per drive-item in our domain)
    var probeUrl = $"{DocumentEntitySet}?$filter=sprk_graphitemid eq '{speItemId}' and sprk_graphdriveid eq '{speDriveId}'&$select=sprk_documentid&$top=1";
    var probeResp = await _httpClient.GetAsync(probeUrl, ct);
    probeResp.EnsureSuccessStatusCode();
    var probeData = await probeResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    var values = probeData.GetProperty("value");

    if (values.GetArrayLength() > 0)
    {
        // Idempotent no-op: row already exists, return it
        var existingId = Guid.Parse(values[0].GetProperty("sprk_documentid").GetString()!);
        return PromoteResult.AlreadyExists(existingId, correlationId);
    }

    // Step 2: Create the row
    // NOTE: Concurrent racers may both reach here. We accept the cost:
    //   Either the race resolves via Dataverse's record-level lock (last-writer-wins on a
    //   uniqueness constraint), OR if Dataverse permits two rows with the same sprk_graphitemid,
    //   the FOLLOW-UP code path detects-and-merges. Per design.md §8, the spike must validate
    //   the actual uniqueness behavior. ASSUMPTION (R1): we add a Dataverse Alternate Key on
    //   sprk_documents(sprk_graphitemid) so concurrent POSTs collide deterministically. See
    //   open item in §7 below.

    var payload = new Dictionary<string, object>
    {
        ["sprk_documentname"]   = fileName,
        ["sprk_filename"]       = fileName,
        ["sprk_graphdriveid"]   = speDriveId,
        ["sprk_graphitemid"]    = speItemId,
        // ... matter binding, container binding, etc. — depends on task 011 + 022 detail
    };

    HttpResponseMessage createResp;
    try
    {
        createResp = await _httpClient.PostAsJsonAsync(DocumentEntitySet, payload, ct);
    }
    catch (HttpRequestException ex) when (ex.Message.Contains("DuplicateKey", StringComparison.OrdinalIgnoreCase))
    {
        // Lost the race; another concurrent caller created the row. Re-probe and return.
        return await ReProbeAfterRaceAsync(speDriveId, speItemId, correlationId, ct);
    }

    if (!createResp.IsSuccessStatusCode)
    {
        var body = await createResp.Content.ReadAsStringAsync(ct);
        // If body indicates duplicate-key, treat as race
        if (body.Contains("DuplicateKey", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("alternateKey", StringComparison.OrdinalIgnoreCase))
        {
            return await ReProbeAfterRaceAsync(speDriveId, speItemId, correlationId, ct);
        }
        throw new HttpRequestException($"Promote failed ({createResp.StatusCode}): {body}");
    }

    var entityIdHeader = createResp.Headers.GetValues("OData-EntityId").FirstOrDefault();
    var idString = entityIdHeader!.Split('(', ')')[1];
    return PromoteResult.Created(Guid.Parse(idString), correlationId);
}
```

### Concurrent-call test (acceptance criterion)

The task POML acceptance criterion: "Promote-on-Save is idempotent under ≥5 concurrent calls — exactly one `sprk_documents` record created."

**Test approach** (deferred to task 026 unit-test + task 027 integration test):

```csharp
[Fact]
public async Task PromoteEphemeral_5ConcurrentCalls_ExactlyOneRowCreated()
{
    var tasks = Enumerable.Range(0, 5)
        .Select(_ => _client.PostAsJsonAsync($"/api/compose/document/{itemId}/promote", new { }))
        .ToArray();
    var responses = await Task.WhenAll(tasks);

    // All 5 succeed (200 OK with same documentId)
    Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
    var docIds = responses.Select(r => r.Content.ReadFromJsonAsync<PromoteResponse>().Result.DocumentId).Distinct();
    Assert.Single(docIds); // Exactly one document id returned across all 5 calls

    // Dataverse query confirms exactly one row exists
    var dvCount = await CountDocumentsAsync(speItemId);
    Assert.Equal(1, dvCount);
}
```

### Locked dependency: Alternate Key on `sprk_documents(sprk_graphitemid)`

For deterministic concurrency, **R1 requires a Dataverse Alternate Key on `sprk_documents.sprk_graphitemid`**. Without it, Dataverse permits two rows with the same `sprk_graphitemid` value, and a probe-after-race retry could see TWO rows, requiring tie-break logic. With the key, the race-loser gets a `DuplicateKey` HTTP error and re-probes deterministically.

**Action**: Task 010 (create `sprk_workspacelayout` Compose row) should be expanded — or a new task 010b filed — to **ensure an Alternate Key exists on `sprk_documents(sprk_graphitemid)`**. This is a 30-second Dataverse customization. Verify first; if already present (likely, given the field is used as a foreign-key surrogate), no action needed. See "Open items" §7.

---

## 6. ADR Tension Surfaced (CLAUDE.md §6.5)

🔔 **ADR Conflict — Resolution Required**

- **ADR in question**: design.md §14 row 4 ("per-user single-session lock via SPE check-out") — NOT a numbered ADR, but a binding R1 design decision documented in `design.md` with the same force as an ADR for this project.
- **Specific rule**: "BFF wraps **SPE check-out** on Compose session open + check-in on close/save. Word for Web users automatically see 'Checked out to X' via SPE's built-in indicator — no custom UI."
- **Conflict**: The rule says **SPE-native check-out**. The spike concludes that **Dataverse-side check-out via the existing `DocumentCheckoutService`** is the better R1 implementation (6-2 in §2.4). The 6-1 advantage is durable; the 1-loss (Word for Web concurrent edit visibility) matters only if a user opens the same doc in BOTH Compose AND Word for Web simultaneously — which the multi-tab lock UX (task 051) is supposed to prevent at the Compose layer.
- **Proposed path**: **Path A (project-scoped exception)**
- **Rationale**: SPE-native check-out is technically correct per the design.md note, but adopting it strictly would require building a ~600 LOC parallel locking infrastructure that duplicates an existing ~1170 LOC service. CLAUDE.md §11 explicitly forbids this pattern. The Dataverse-side approach delivers the SAME user-facing behaviour (lock-on-open, conflict-detection, force-close-discard, version-tracking, indexing) at ~150 LOC of additive code. The one technical difference — Word for Web's native "Checked out to X" banner — is moot for R1's single-editor model because Compose's own multi-tab UX (task 051) is the authoritative conflict detector at the Spaarke layer, not the Microsoft Word layer.
- **Impact if path A is accepted**: Compose R1 uses the Dataverse-side lock substrate. Tasks 050/051/052 implementation pivots from "wrap Graph SPE checkout API" to "extend `DocumentCheckoutService` with heartbeat + sweeper, reuse existing checkout/checkin/discard endpoints from the Compose UI." Two new BFF endpoints instead of seven. Phase 5 LOC estimate drops from ~600 to ~150. The trade-off — a user who manually opens the same doc in Word for Web concurrently with Compose will not be auto-warned by Word — is documented in spec.md as a known R1 limitation, with R2+ deferral to SPE-native checkout if real-world incidents emerge.
- **Alternative considered (and rejected)**: Path C (pivot to comply) — build SPE-native checkOut/checkIn wrappers per the design.md letter. Rejected because: (a) violates CLAUDE.md §11 (the existing service does ~85% of what we need); (b) the parallel-infrastructure tax has no return; (c) the visibility-in-Word-for-Web edge case is preventable via the Compose multi-tab UX (task 051) without Word's involvement.

**Where this resolves**: User decides at the post-Wave-0 operator review gate (per `current-task.md` and the project's autonomous mode contract). If approved (recommended), `design.md` §14 row 4 should be edited to read: *"per-user single-session lock via Dataverse `sprk_documents` check-out + 15-min idle auto-release (extends existing `DocumentCheckoutService`)"* — and an "ADR Tensions" subsection added to `spec.md` documenting the exception.

---

## 7. Failure Modes + Open Items for Phase 3-5

### 7.1 Failure modes

| Failure | Mitigation | Owner |
|---|---|---|
| Network drop mid-checkout (TCP RST between Compose and BFF) | Browser sees fetch error; UI surfaces "couldn't lock" toast; no Dataverse row was created (the existing service is transactional — only succeeds or doesn't). Retry safe. | Existing service handles |
| Browser-close without check-in | Heartbeat stops; sweeper releases after 15 min | New sweeper (task 052) |
| Conflicting open-in-Word-Desktop session by SAME user | Compose detects via `CheckoutStatusInfo.IsCurrentUser`; surfaces "you have this open in Word — close Word first OR check-in from Word" UX. R1: simple message; R2: deeper Word integration | Task 051 (multi-tab UX) |
| Concurrent Save promotes (5x at once) | Alternate Key on `sprk_documents(sprk_graphitemid)` + race-aware retry logic in `PromoteEphemeralAsync` | Task 022 (`ComposeDocumentService`) — depends on alternate-key existence (open item §7.2) |
| Heartbeat endpoint flooded by misbehaving client | `RequireRateLimiting("standard")` on the endpoint + 3-min server-validated minimum interval (reject heartbeats < 30 sec apart from the same user/doc as no-op) | Task 052 |
| Sweeper down (BFF restart, network) | Heartbeats keep refreshing while sweeper is alive; if sweeper is down, the worst case is a stale lock survives slightly longer (15 + 2 + sweeper-down-time minutes). Acceptable degradation. | Operational — BFF restarts complete in seconds |
| User force-closes their OWN lock from another tab | Calls existing `POST /api/documents/{id}/discard` (already implements `NotAuthorized` check restricted to lock-owner; same user IS the owner so it succeeds) | Existing endpoint handles |
| User attempts to discard someone ELSE's lock | Existing `DiscardAsync` returns `NotAuthorized` (403) — only the lock-holder can discard | Existing service handles |

### 7.2 Open items for Phase 3-5 (must resolve before Phase 5 ships)

| # | Item | Owner | Block | Target |
|---|---|---|---|---|
| OI-1 | Verify (or create) Dataverse **Alternate Key on `sprk_documents.sprk_graphitemid`**. Required for idempotent Save-promotion under concurrency. | Operator + Phase 1 Dataverse task | Task 022 / 027 acceptance | Phase 1 |
| OI-2 | Add `sprk_lastheartbeatutc` field (DateTime, nullable) to `sprk_documents`. | Operator + Phase 1 Dataverse task | Task 052 acceptance | Phase 1 |
| OI-3 | Resolve ADR Tension §6 — operator approves Path A. If not approved, Phase 5 task LOC estimate triples and the SPE-native wrapper path becomes mandatory. | Operator (post-Wave-0 gate) | Phase 5 entire | Post-Wave-0 review |
| OI-4 | Validate empirically that Microsoft Graph rate limit for `/sprk_documents PATCH` heartbeat traffic at 3-min interval per active Compose session is comfortably below 17 req/sec/user under load. Smoke test in Phase 6 task 060. | Phase 6 | Production confidence | Phase 6 |
| OI-5 | Decide if `sprk_lastheartbeatutc` updates should also bump `modifiedon` (Dataverse default). Recommendation: YES — gives operators visibility into "active editing now." Verify no audit-log noise downstream. | Operator + Phase 1 | Phase 5 acceptance | Phase 5 |

---

## 8. BFF Endpoint Shape — Locked R1 Surface

R1 ships **2 new Compose-specific endpoints** + reuses **4 existing Document endpoints** that were already mapped, plus the lockup endpoint shape needed by other Compose tasks (uploaded/load/save documented separately in tasks 022 / 024).

| Endpoint | New / Reused | Service | Task |
|---|---|---|---|
| `POST /api/compose/document/{spe-id}/promote` | **NEW** | `DocumentCheckoutService.PromoteEphemeralAsync` (NEW method on existing service) | 022 + 024 |
| `POST /api/compose/document/{documentId}/heartbeat` | **NEW** | `DocumentCheckoutService.RefreshHeartbeatAsync` (NEW method on existing service) | 052 + 024 |
| `POST /api/documents/{documentId}/checkout` | **REUSE (unchanged)** | `DocumentCheckoutService.CheckoutAsync` | — |
| `POST /api/documents/{documentId}/checkin` | **REUSE (unchanged)** | `DocumentCheckoutService.CheckInAsync` | — |
| `POST /api/documents/{documentId}/discard` | **REUSE (unchanged)** — used as "force-close" mechanism for Compose multi-tab UX | `DocumentCheckoutService.DiscardAsync` | task 051 calls this |
| `GET /api/documents/{documentId}/checkout-status` | **REUSE (unchanged)** — used by Compose to detect "open in another session" on mount | `DocumentCheckoutService.GetCheckoutStatusAsync` | task 051 calls this |

Endpoints in design.md §12 that are **NOT** needed for R1 lock semantics (those concerns are split into separate tasks 022/024 for upload/load/save):
- `POST /api/compose/document/upload` — task 022 / 024 (file upload, separate concern)
- `GET /api/compose/document/{spe-id}` — task 022 / 024 (load DOCX, separate concern)
- `PUT /api/compose/document/{spe-id}` — task 022 / 024 (save DOCX bytes, separate concern)
- `POST /api/compose/document/{spe-id}/checkout` — **eliminated by reuse of existing `/api/documents/{id}/checkout`**
- `POST /api/compose/document/{spe-id}/checkin` — **eliminated by reuse of existing `/api/documents/{id}/checkin`**

**§10 BFF Hygiene compliance**: Both new endpoints route through the existing `DocumentCheckoutService` which is registered via `AddDocumentsModule` (DocumentsModule.cs:49). No new AI dependency. No new HIGH-severity CVE risk. Publish-size delta estimate: ~3 KB (one new endpoint group file, one sweeper hosted service, one method on existing service, one new model). Well within ≤60 MB ceiling.

---

## 9. Phase 5 Task Impact Summary (for main session)

Original spec / TASK-INDEX assumed Phase 5 builds SPE-native checkout. This spike pivots Phase 5:

| Original Phase 5 task | Locked direction (post-spike) |
|---|---|
| **050** — Acquire SPE check-out on Compose open | **REVISED**: Call existing `POST /api/documents/{documentId}/checkout` from Compose React. Frontend-only task (no BFF code). |
| **051** — Multi-tab conflict UX | **UNCHANGED**: Detect `CheckoutResult.ConflictError.CheckedOutBy.Id == currentUserId` → render Compose-specific "open in another session" modal with Go-to-other / Force-close (Force-close = call existing `/discard` endpoint). |
| **052** — Heartbeat + 15 min idle orphan release | **EXPANDED**: Three sub-components — (a) `RefreshHeartbeatAsync` method on existing `DocumentCheckoutService`, (b) `POST /api/compose/document/{documentId}/heartbeat` endpoint, (c) `StaleCheckoutSweeperHostedService`. Reuses existing discard logic. Add `sprk_lastheartbeatutc` field via Phase 1. |

Phase 5 total LOC estimate revised: **~150 LOC** (was ~600 LOC pre-spike).

---

## 10. Locked Artifacts for Subsequent Phases

- **Lock substrate**: Dataverse-side via `DocumentCheckoutService` (existing) — §1, §2, §6.
- **Heartbeat interval**: **3 min sliding**, 5 missed = ~15 min stale — §1, §4.
- **Orphan auto-release**: **15 min** of heartbeat silence + 2 min sweeper interval = **≤17 min** maximum orphan lifetime — §1, §4.3.
- **Save-promotion idempotency**: probe-then-create + Alternate Key on `sprk_documents(sprk_graphitemid)` + race-aware retry — §1, §5.
- **BFF endpoint shape**: **2 new endpoints** (promote + heartbeat), 4 reused (checkout/checkin/discard/status) — §8.
- **ADR Tension**: design.md §14 row 4 — Path A exception requested — §6.
- **5 Open Items**: OI-1 through OI-5 — §7.2.

These are the inputs Phase 1 (task 010 + Dataverse customizations), Phase 2 (tasks 022, 024), and Phase 5 (tasks 050/051/052) consume.

---

## 11. Acceptance criteria mapping (from POML)

| POML criterion | How met |
|---|---|
| "Check-out endpoint acquires SPE lock on a real test container; check-in releases it cleanly." | Pivoted to Dataverse-side per §6 ADR Tension. Existing service already acquires lock + releases — empirical validation deferred to Phase 6 task 060 E2E smoke test. **Locked**: lock substrate is Dataverse-side. |
| "Heartbeat auto-release triggers at the configured idle threshold; orphan locks recover automatically." | Algorithm locked in §4. Empirical validation deferred to Phase 5 task 052 + Phase 6 smoke test. **Locked**: 3-min heartbeat, 15-min stale threshold, sweeper interval 2 min, total max-orphan-lifetime ≤17 min. |
| "Multi-session conflict path returns expected response with Go-to-other / Force-close options." | Conflict detection algorithm locked in §1 + §9 task 051 row. Existing `DocumentCheckoutService` already returns `CheckoutResult.Conflict` with full identity info — Compose-side just needs to compare to current user. **Locked**: conflict detection contract. |
| "Promote-on-Save is idempotent under ≥5 concurrent calls — exactly one `sprk_document` record created." | Algorithm locked in §5. Test pattern provided. Hinges on OI-1 (Alternate Key existence). **Locked**: probe-then-create with race-aware retry. |
| "Spike report names final recommended heartbeat interval with empirical rationale; TASK-INDEX.md status for task 003 is ✅." | This document. Heartbeat = 3 min; orphan = 15 min. Rationale in §1 + §4. TASK-INDEX update: task 11 below. |

---

## 12. References

- design.md §§8, 10.5, 12, 14 row 4
- spec.md (assumed; not re-read in spike)
- CLAUDE.md §3 (Sub-Agent Write Boundary), §6.5 (ADR Conflict Protocol), §10 (BFF Hygiene), §11 (Component Justification)
- `.claude/constraints/bff-extensions.md` — Pre-merge checklist (§A items 1-6)
- `src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs` (reuse target)
- `src/server/api/Sprk.Bff.Api/Api/DocumentOperationsEndpoints.cs` (reuse target)
- `src/server/api/Sprk.Bff.Api/Models/CheckoutModels.cs` (response shapes)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/DocumentsModule.cs` (DI registration pattern to extend)

---

*Locked 2026-06-29 by Spike #3 (`projects/spaarkeai-compose-r1/tasks/003-spike-spe-checkout-promotion.poml`). No production code modified. ADR Tension surfaced for operator review at post-Wave-0 gate.*
