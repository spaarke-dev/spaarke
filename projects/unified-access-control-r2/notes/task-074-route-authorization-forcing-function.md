# Task 074 — Route authorization forcing function

> **Status**: implemented · **Date**: 2026-08-25 · **Rigor**: FULL (tests/** → Step 9.5 gates unconditional)
> **Deliverable**: `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`
> **Write scope**: `tests/Spaarke.ArchTests/` + this file. No BFF or client file was modified.

---

## 1. The mechanism decision, and why

The POML offered three mechanisms and asked for (a) if it could be made reliable:

- **(a) reflect the built `EndpointDataSource`** and inspect each endpoint's metadata for an authorization filter
- **(b) static analysis** of the registration files
- **(c) census** assertion in the style of `CredentialCensusTests`

### (a) is structurally incapable of answering the question. This is a platform fact, not an implementation difficulty.

`IEndpointFilter` registrations **do not appear in endpoint metadata**. `AddEndpointFilter` appends to an
internal filter-factory list that is applied when the endpoint is built; the filters are baked into the
compiled `RequestDelegate`. `EndpointBuilder.Metadata` is a separate collection, and there is no
`IEndpointFilterMetadata`. Verified against the actual attachment site — every Spaarke authorization filter
is attached through the **lambda** overload:

```csharp
// Api/Filters/DocumentAuthorizationFilter.cs:20-30
public static TBuilder AddDocumentAuthorizationFilter<TBuilder>(this TBuilder builder, string operation)
    where TBuilder : IEndpointConventionBuilder
    => builder.AddEndpointFilter(async (context, next) => { ... });   // no WithMetadata anywhere
```

So reflecting the route table yields only `IAuthorizeData` (from `RequireAuthorization`) and
`IAllowAnonymous`. That distinguishes "authenticated" from "anonymous" — **which is exactly the distinction
that produced every one of the four findings.** `POST /api/documents/{documentId}/share-link` sits on a
group carrying `.RequireAuthorization()`; it is `IAuthorizeData`-decorated and was still a hole.

There is a second, independent disqualifier. `/api/ai/search` is registered **inside a compound config
gate**:

```csharp
// Infrastructure/DI/EndpointMappingExtensions.cs:239-244
if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
    app.Configuration.GetValue<bool>("Analysis:Enabled", true))
{
    app.MapSemanticSearchEndpoints();
    app.MapRecordSearchEndpoints();
}
```

An app constructed in a test with default configuration would not register it at all, so the route of
finding #1 would be **absent from the census** and the rule would silently pass. A rule whose coverage
depends on test configuration is the failure mode this task exists to remove.

### Chosen: **(b) as the detector, with fail-closed parsing, + (c) as the anti-drift ratchet.**

The POML's objection to (b) alone is correct and is answered structurally rather than by promise:

> "Do not ship (b) alone — a rule that misses helper-wrapped registrations reproduces the original failure."

Two mechanisms convert (b)'s blind spots from silent misses into build failures:

1. **Fail-closed parsing.** The scanner must account for **100% of the `Map{Verb}` call sites** it finds in
   a governed file. A call site it cannot parse is reported as `UNPARSEABLE` and **fails the build** — it is
   never skipped. A helper-wrapped or oddly-formatted registration therefore becomes loud, not invisible.
2. **A file census over the whole routing surface.** The set of files under `Api/**` containing route
   registrations is pinned. A new endpoint file fails the build until it is classified GOVERNED or
   NOT-GOVERNED. Scope cannot drift silently, which is the only way a path-prefix scope boundary can rot.

This is the same shape as `CredentialGuardTests` (ban + reasoned allowlist + negative/positive controls)
plus `CredentialCensusTests` (pinned count), which `tests/CLAUDE.md` records as the sanctioned pattern for
this KEEP path.

---

## 2. The scope boundary (written down, per POML step 2)

**Governed**: every `Map{Get,Post,Put,Patch,Delete}` call site in the files listed as `Governed` in
`RouteAuthorizationGuardTests.EndpointFileCensus` — the surface that serves **document metadata, file
bytes, or container/drive-keyed content**:

| File | Why governed |
|---|---|
| `Api/FileAccessEndpoints.cs` | `/api/documents/{documentId}/*` — bytes + URL minting |
| `Api/DataverseDocumentsEndpoints.cs` | `/api/v1/documents/*` — document rows + download |
| `Api/DocumentOperationsEndpoints.cs` | checkout/checkin/delete/analyze on a document |
| `Api/DocumentsBulkEndpoints.cs` | bulk download |
| `Api/DocumentVersionEndpoints.cs` | drive-keyed version history + prior-version bytes |
| `Api/DocumentsEndpoints.cs` | drive-keyed upload + item delete |
| `Api/UploadEndpoints.cs` | container-keyed file writes |
| `Api/OBOEndpoints.cs` | drive/container-keyed read, PATCH, DELETE, enumerate |
| `Api/Ai/SemanticSearchEndpoints.cs` | document metadata + AI summaries, tenant-wide |
| `Api/Ai/RecordSearchEndpoints.cs` | Dataverse record content |
| `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` | contact-plane document reads (reference impl) |
| `Api/ExternalAccess/ExternalModuleDataEndpoints.cs` | contact-plane module content |
| `Api/ComposeEndpoints.cs` | document bytes into the drafting workspace |

**Not governed**: everything else under `Api/**` — enumerated in the same census with a one-line reason
each. Not governed is *not* the same as *waived*: a not-governed file is out of this rule's subject matter
(it serves no document/Dataverse content), whereas a waiver is an in-scope route deliberately exempted.

**Why a file list rather than a path-prefix list.** Route paths are assembled from `MapGroup` prefixes plus
relative literals, and reconstructing them is the brittle part of static analysis. The file is the unit the
scanner can identify with certainty, and the census makes the file set tamper-evident. A path-prefix scope
would have to be reconstructed correctly to be trusted; a file census only has to be *complete*, and
completeness is mechanically checkable.

---

## 3. Three authorization states (all three visible, none assumed safe)

The scan classifies every governed route into exactly one state:

| State | Meaning |
|---|---|
| `FILTER` | carries a per-resource endpoint filter (`.Add*AuthorizationFilter(...)`, `.AddEndpointFilter<*AuthorizationFilter>()`, `EntityAccessFilter`) |
| `POLICY` | carries `RequireAuthorization("<policy>")` where the policy is backed by `ResourceAccessRequirement` → `ResourceAccessHandler` → `AuthorizationService` |
| `NONE` | only `RequireAuthorization()` / `AllowAnonymous()` / rate limiting — **fails the build unless waived** |

`POLICY` is deliberately **not** folded into `FILTER`. It is a genuine resource-authorization mechanism
(`ResourceAccessHandler` calls `AuthorizationService.AuthorizeAsync` and fails closed on every error path),
but its correctness depends on the route's resource key matching the mechanism's resource domain — and that
is precisely where finding #4 fails. The `POLICY` set is therefore **pinned** so a new route cannot quietly
join it, and is listed in the failure output so a reviewer sees it.

---

## 4. Retroactive validation — what the rule catches, and what it does not

The POML requires proof that the rule fires on all four historical misses. **It fires on three. It cannot
fire on the fourth, and the reason is a real distinction rather than an implementation gap.** Establishing
this required checking the state of each route at HEAD instead of trusting the finding descriptions — which
is build-plan rule 3 ("verify against live metadata, not docs").

| # | Route | State at HEAD | Caught by | Evidence |
|---|---|---|---|---|
| 2 | `Api/OBOEndpoints.cs` drive-keyed routes (7) | **absent** — `AddDocumentAuthorizationFilter` appears **zero** times; only `RequireAuthorization()` + `RequireRateLimiting` | **Rule A** (`NONE`) | `Api/OBOEndpoints.cs:16-341` |
| 3 | `POST /api/documents/{documentId}/share-link` | **absent** — the one route in `FileAccessEndpoints.cs` with no `.AddDocumentAuthorizationFilter` | **Rule A** (`NONE`) | `Api/FileAccessEndpoints.cs:116-125` vs. its 8 gated siblings |
| 1 | `POST /api/ai/search` | **present but decorative** — `.AddSemanticSearchAuthorizationFilter()` was attached from the start (`fbe0fcdb9`); every branch of `ValidateScopeAuthorization` returns `new AuthorizationResult(true, null)`, and the filter's only dependency is `ILogger` | **Rule B** (decorative filter) | HEAD `Api/Filters/SemanticSearchAuthorizationFilter.cs` |
| 4 | `PUT /api/containers/{containerId}/files/{*path}` | **present, real, wrong domain** — `RequireAuthorization("canwritefiles")` → `ResourceAccessRequirement("driveitem.content.upload")` → `ResourceAccessHandler` → `AuthorizationService.AuthorizeAsync`, fail-closed | **NOT CAUGHT** — see §5 | `Api/UploadEndpoints.cs:108`, `Infrastructure/DI/AuthorizationModule.cs:296`, `Infrastructure/Authorization/ResourceAccessHandler.cs` |

**The load-bearing correction to the finding descriptions**: the build plan describes all four as missing
authorization. Two were. **Finding #1 had a filter that decided nothing, and finding #4 has a mechanism that
decides the wrong question.** Those are different defect classes and only one of them is a structural
absence. A rule built on the plan's framing alone would have claimed to cover four and actually covered two.

### Live file-level proof for finding #1 (strongest single piece of evidence here)

Rule B was run against the **same file in two states**, with no seeding:

| State of `Api/Filters/SemanticSearchAuthorizationFilter.cs` | Decision-service references in CODE | Rule B |
|---|---|---|
| **HEAD** (`git show HEAD:…`) | **none.** `AuthorizationService` appears exactly once in the whole file, on line 141, inside `// Future: Add entity-level authorization via AuthorizationService` | **FAILS** |
| **working tree** (task 070's fix, in flight in the main session) | `AuthorizationService` + `AccessRights` in real code | **PASSES** |

This is the retroactive proof for finding #1 on the actual production file rather than on a fixture, and it
independently validates that the `Decomment` step is load-bearing: without it, HEAD's `// Future:` comment
would have been accepted as evidence of a decision, and the guard would have passed on the exact filter that
let a non-admin read all 442 documents. That failure mode is pinned by the `prosePassingAsEvidence`
assertion in `RuleB_NegativeControl_FiresOnADecorativeFilter`.

It also means Rule B **agrees with task 070's fix** without having been written against it.

### Rule B's first run triaged 9 flagged filters — 2 were my false positives, 1 is a new finding

Driving false positives to zero was done by *widening the definition of a decision* where the filter really
decided, and by *waiving with reasons* where it legitimately decides from claims — never by narrowing scope:

- **2 genuine decision seams I had under-specified** → added to `DecisionServices`:
  `IDataversePrivilegeChecker` (`DataverseAuthorizationFilter` — `HasReadPrivilegeAsync` /
  `GetReadableEntitiesAsync`) and `WorkspaceLayoutService` (`WorkspaceLayoutAuthorizationFilter` —
  `GetLayoutByIdAsync(layoutId, userId)`, denies on ownership mismatch). Both were real false positives.
- **6 legitimately claim-only** → `ClaimOnlyFilters`, each with a written reason: `AgentAuthorizationFilter`,
  `CommunicationAuthorizationFilter`, `WorkspaceAuthorizationFilter`, `RegistrationAuthorizationFilter`,
  `SpeAdminAuthorizationFilter`, `ReportingAuthorizationFilter`.
- **1 NEW FINDING** → `KnownDecorativeFilters` (PENDING, unowned): see §7b.

### 7b. A SEVENTH miss — `RecordSearchAuthorizationFilter` is finding #1's twin

`Api/Filters/RecordSearchAuthorizationFilter.cs` gates `POST /api/ai/search/records` — the **same
`/api/ai/search` group** as finding #1. It reads the `tid` claim, extracts the request, writes one
`LogInformation`, and returns `await next(context)`. Its only dependency is `ILogger`. **There is no
authorization decision anywhere in it.** It is structurally the same defect as the filter that produced the
incident this project was opened for, and it is in **no** finding list, no spec, and no Wave 1 task.

Because a filter *is* attached, Rule A classifies `POST /api/ai/search/records` as GATED — which is exactly
why Rule B has to exist alongside Rule A rather than instead of it.

Recorded as PENDING/unowned. Needs the same treatment task 070 is giving its sibling (constrain to the
caller's accessible record set). Suggest assigning it to 070 while that context is live.

### Rule B exists because of finding #1

`SemanticSearchAuthorizationFilter` at HEAD takes one constructor argument (`ILogger?`), references no
authorization or access service, and returns allow from every branch. It produces an **audit log, not a
decision**. That is structurally detectable and generalizes: *a type named `*AuthorizationFilter` that
consults no authorization decision service is decorative.* Rule B asserts it, with a reasoned waiver list
for the filters that legitimately decide from claims or signatures alone.

---

## 5. ESCALATION — finding #4's defect class is not structurally detectable

Per the POML `<escalation><trigger>`: *"If no mechanism achieves both zero false positives and coverage of
the four historical misses, STOP and report the tradeoff rather than shipping a rule that looks like
coverage but is not."*

**Firing it, for finding #4 only.**

`PUT /api/containers/{containerId}/files/{*path}` carries a real, fail-closed resource-authorization
mechanism. The defect is that `ResourceAccessHandler.ExtractResourceId` accepts `containerId`, `driveId`,
`documentId`, `resourceId` and `id` **interchangeably** (lines 144-148) and feeds all of them into a
document-rights lookup. A container GUID is not an `sprk_document` row, so the check does not answer "may
this caller write to this container?".

A rule *could* be written to fire on this — "no route keyed by `{containerId}` or `{driveId}` may rely
solely on a `ResourceAccessRequirement` policy". I did **not** ship that, because it hard-codes one known
mismatch and would read as general coverage while only encoding this single instance. That is the same
"looks like coverage but is not" failure the trigger names, arrived at from the opposite direction.

**What actually closes it**: a behavioral test — an impersonated write against a container the caller has
no access to, asserting denial. That is build-plan rule 4 ("success where you expect denial is the signal")
and it belongs to **task 073**. Rule A pins the route in the `POLICY` census meanwhile, so its mechanism
cannot be removed or changed silently.

**Decision requested**: accept 3-of-4 structural coverage + task 073 owning #4 behaviorally (recommended),
or direct me to add the bespoke container/drive-vs-document-domain assertion.

---

## 6. Waiver list

Split PERMANENT vs PENDING. A PENDING waiver names its owning task, so the list reads as a work list that
shrinks to zero and a reviewer can tell temporary from permanent at a glance. Canonical list lives in the
test file (`Waivers`); this is the narrative copy.

### PENDING — expected to be deleted when the named task lands

| Route | Owning task | Note |
|---|---|---|
| `POST /api/documents/{documentId}/share-link` | **072** | Gate + bounded expiry + drop `scope=anonymous`. Not being executed yet. |
| `PUT /api/containers/{containerId}/files/{*path}` | **073** | `POLICY`-only; authorizes a container id against document rights (§5). |
| `POST /api/containers/{containerId}/upload` | **073** | Same shape, same container key, same file. |
| `PUT /api/upload-session/chunk` | **073** | Chunk writes carry no resource key at all; session must be bound to an authorized container. |
| `PUT /api/drives/{driveId}/upload` | **073** | Drive-keyed write, `POLICY` only. |
| `DELETE /api/drives/{driveId}/items/{itemId}` | **073** | Drive-keyed destroy, `POLICY` only. |
| `GET /api/obo/containers/{id}/children` | **071** | Delete-or-gate; `NONE`. |
| `PUT /api/obo/containers/{id}/files/{*path}` | **071** | `NONE`. |
| `POST /api/obo/drives/{driveId}/upload-session` | **071** | `NONE`. |
| `PUT /api/obo/upload-session/chunk` | **071** | `NONE`. |
| `PATCH /api/obo/drives/{driveId}/items/{itemId}` | **071** | `NONE`. |
| `GET /api/obo/drives/{driveId}/items/{itemId}/content` | **071** | `NONE` — byte read. |
| `DELETE /api/obo/drives/{driveId}/items/{itemId}` | **071** | `NONE` — destroy. |
| `GET /api/obo/drives/{driveId}/items/{itemId}/versions` | **071** | `NONE` (`DocumentVersionEndpoints.cs`). |
| `GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content` | **071** | `NONE` — prior-version bytes. |
| `GET /api/v1/containers/{containerId}/documents` | **NEW — unowned** | See §7. Container-keyed document listing, `NONE`. |

### PERMANENT — genuinely not a per-resource decision

| Route | Reason |
|---|---|
| `POST /api/v1/documents/` (create) | Creation has no pre-existing resource to authorize; the parent-record check belongs to the handler's field validation, not a per-document gate. |
| `GET /api/v1/documents/` (list) | Collection read. Correctness is **result trimming**, not a per-record gate — a per-resource filter has no single resource to check. Trimming is Wave 3's subject. |

`/ping`, `/healthz`, `/healthz/*`, `/status` need no waiver: they live in
`Infrastructure/DI/EndpointMappingExtensions.cs`, which the census classifies NOT-GOVERNED (health/liveness
probes, no document or Dataverse content), and they carry explicit `AllowAnonymous()`.

---

## 7. A fifth ungated route, found by the scan and not by anyone's re-reading

`GET /api/v1/containers/{containerId}/documents` (`Api/DataverseDocumentsEndpoints.cs:532-590`) carries
`RequireAuthorization()` only — no per-document filter and no resource policy. It lists the documents of an
arbitrary container id.

It appears in **no** Wave 1 task. It is the fifth miss, found on the first run of the mechanism, which is
the whole argument of the task's `<origin>`: ~15 estimated → 22 found → +2 → +5 in `OBOEndpoints` → **+1
here**. Filed as PENDING/unowned; needs an owner (suggest folding into 073, which already owns the
container-keyed surface).

---

## 8. CI — verified, and the premise is only half true

`sdap-ci.yml`'s `code-quality` job does run the whole ArchTests project, so **the new test executes in CI
with zero workflow edits**. But it **cannot fail the build there**, for two independent reasons:

```yaml
# .github/workflows/sdap-ci.yml
  code-quality:
    continue-on-error: true   # line 456 — job level, "CI informational-only until test-architecture-reset-r1 lands"
    ...
      - name: ADR architecture tests (NetArchTest)
        run: dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj ...
        continue-on-error: true   # line 507 — step level, "Don't block PR"
```

Full picture across the four workflows that touch ArchTests:

| Workflow | Runs new test? | Blocking? |
|---|---|---|
| `sdap-ci.yml` → `code-quality` | ✅ full suite | ❌ `continue-on-error` at **both** job and step level |
| `ci-tier2-advisory.yml` → NetArchTest full suite | ✅ full suite | ❌ `continue-on-error: true` (advisory by design) |
| `adr-audit.yml` (weekly) | ✅ full suite | ❌ `continue-on-error: true`; opens a rolling issue |
| `ci-tier1-blocking.yml` → `arch-tests` | ❌ **no** | ✅ **blocking** — but selects **7 named facts** by `--filter FullyQualifiedName=...` |

So the POML's acceptance criterion *"the rule runs in CI, and its failure is demonstrated there"* is met for
"runs" and **not** met for "fails". Making it blocking requires adding the fact names to the tier-1
`--filter` allow-list at `ci-tier1-blocking.yml:283` — a **one-line workflow edit**.

**I did not make that edit.** `.github/workflows/**` is owned by the active project
`ci-cd-unit-test-remediation-r1` (per `projects/INDEX.md`), and editing it would be a hot-path conflict.
Handing it over rather than taking it.

**Requested follow-up (one line, workflow owner):** append to the tier-1 filter at
`ci-tier1-blocking.yml:283`:

```
|FullyQualifiedName=Spaarke.ArchTests.RouteAuthorizationGuardTests.EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver
|FullyQualifiedName=Spaarke.ArchTests.RouteAuthorizationGuardTests.NoAuthorizationFilterIsDecorative
|FullyQualifiedName=Spaarke.ArchTests.RouteAuthorizationGuardTests.TheEndpointFileCensusIsPinned
```

These qualify on the tier-1 job's own stated criteria (comment at `ci-tier1-blocking.yml:229-233`):
each detects a real production regression the architecture cannot tolerate, and each is deterministic —
pure source inspection, no network, no ceiling counter that drifts.

---

## 8b. End-to-end proof that the rule turns the build RED (not just that the detector classifies)

The seeded controls prove the detector classifies correctly. To prove the whole pipeline — real file scan →
classification → failed build with an actionable message — the `share-link` waiver key was temporarily
neutralised (a change confined to the test file, no BFF source touched) and the rule re-run:

```
Failed Task 074 Rule A: every governed route carries per-resource authorization or a named waiver
  Ungated routes:
      POST /api/documents/{documentId}/share-link
        at Api/FileAccessEndpoints.cs:116
```

The failure names the route, the file, the line, and the three-option remedy, and explicitly forbids the
tempting fix ("Do NOT make this pass by removing the file from GovernedFiles"). The waiver was restored and
the suite returned to green.

## 8c. The guard enforced its own quality bar on its author

On the first run, `EveryWaiverCarriesAReasonAndPendingWaiversNameTheirOwningTask` **failed on five of my own
waivers** whose reasons were under the 60-character substantive-reason floor (e.g. "Finding #2 — chunk
write, no resource key."). They were rewritten with real reasons rather than the floor being lowered. Worth
recording: the anti-rubber-stamp mechanism fired on its first opportunity, against the person adding the
waivers.

## 9. Build / test state at stopping point

The suite is **GREEN at HEAD with the waiver list above**, and that is only true *because* of the 16 PENDING
waivers. Stated plainly: **Wave 1 is partially landed, so the rule cannot be green without them.** Tasks 072
and 073 are not being executed; 070 and 071 were in flight in other sessions while this ran. As each lands,
its waivers are deleted and the list shrinks toward the two PERMANENT entries. If a PENDING waiver's route
becomes gated, `NoWaiverIsStale` fails and tells the author to delete the waiver — the list cannot silently
outlive its cause.

Nothing was committed or pushed; the main session owns the commit.
