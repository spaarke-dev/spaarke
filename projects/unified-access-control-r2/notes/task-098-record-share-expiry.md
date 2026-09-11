# Task 098 — one atomic "set the expiry of every share on this record" endpoint

> **Status**: implemented 2026-09-11 (session 8). Code + tests at `23953342a`.
> **Spec**: FR-33 (owner redesign 2026-09-10 — the Manage Access toolbar **Expiration**).
> **Design**: `notes/decisions/external-grant-expiry-mandatory.md` §10–§12.
> **Consumed by**: task 099 (the toolbar date picker).

---

## 1. What was built

`POST /api/v1/external-access/set-record-share-expiry`

```json
{ "recordType": "matter", "recordId": "…", "expiryDate": "2026-12-10" }
→ 200 { "updatedCount": 3, "expiresDate": "2026-12-10" }
```

It writes `sprk_expiresdate` onto **every active (`statecode = 0`) `sprk_externalrecordaccess` row of the
record** — contact shares and organization shares — in **one** `IDataverseService.BulkUpdateAsync`, which
task 096 made a genuine `ExecuteTransactionRequest`. Every share gets the date, or none does.

| File | Change |
|---|---|
| `Api/ExternalAccess/SetRecordShareExpiryEndpoint.cs` | **new** — route, handler, `ResolveRoot`, cache fan-out |
| `Api/ExternalAccess/Dtos/SetRecordShareExpiryRequest.cs` / `…Response.cs` | **new** DTOs |
| `Api/ExternalAccess/DelegationRuleFilter.cs` | **new `case`** for the request type (see §2.1) |
| `Api/ExternalAccess/ExternalAccessEndpoints.cs` | route registered on the existing `/api/v1/external-access` group |
| `Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs` | `EntityLogicalName`, `ActiveRowsForRootFilter`, `QueryActiveRowsForRootAsync`, `ToSdkDateOnly` |

No new service, no DI registration, no package. The handler injects existing registrations only
(`DataverseWebApiClient`, `IDataverseService`, `ITenantCache`, `TimeProvider`).

## 2. The POML's premises, checked against the code (this project's 12th wrong-task-file finding)

### 2.1 🔴 The POML omitted the one change the route cannot work without

`DelegationRuleFilter.ResolveTargetAsync` dispatches on the bound request TYPE, and its default branch
returns `null` → **403 `delegation_target_unresolved`** for every caller. The POML listed the filter only
as a "canonical reference". Registering the route on the group without a new `case` would have produced an
endpoint that denies everyone — fail-closed, but useless, and it reads as a bug rather than a gate. The case
resolves the target through the **same** `SetRecordShareExpiryEndpoint.ResolveRoot` the handler uses.

### 2.2 The cache "fail-open window" premise is false — invalidation kept, rationale corrected

The POML's NFR-01 constraint said a shortened expiry "keeps conferring access until a cache TTL expires — a
fail-open window". Checked:

- The participation cache (`ExternalParticipationService.CachedGrantSet`) stores **which** grants a contact
  holds and their **level** — **no dates**. Expiry is applied by the read `$filter`
  (`sprk_expiresdate ge {today}`, task 007) when an entry is built.
- This endpoint rejects any date before today (097's rule). So after a shortening, the grantee is still
  admitted **today** — cached or not.
- The only window is the ≤60 s tail after an expiry **midnight**, which every grant already has on the read
  path and which invalidating at write time cannot shorten.

So invalidation is **freshness, not a security boundary**. It is kept because it is cheap and it makes a
**renewal** (a lapsed share revived by this call — see §3) visible on the next evaluation rather than up to
60 s later. Non-fatal, like every other invalidator in the group.

### 2.3 "Date Only" — the column's actual behaviour, read live

Live metadata (2026-09-11, read-only Web API query):

| Column | Format | DateTimeBehavior |
|---|---|---|
| `sprk_expiresdate` | DateOnly | **TimeZoneIndependent** |
| `sprk_granteddate` | DateOnly | **TimeZoneIndependent** |

Neither is the `DateOnly` *behavior*; both are TimeZoneIndependent with a date-only *format*. Dataverse stores
a TZI value as given, with no conversion. The SDK value is therefore shaped as **midnight,
`DateTimeKind.Unspecified`** (`ExternalGrantLifecycle.ToSdkDateOnly`) — no offset for anything on the way to
act on. A `Local` value would be serialised with the machine's offset and could land on the neighbouring date
anywhere east of UTC. The Web API path writes the same column as a bare `yyyy-MM-dd`; this is the SDK
equivalent.

### 2.4 The escalation trigger — evaluated, does not fire

> *"If the grant rows cannot be resolved to 'active rows on THIS record' with a single server-side filter …
> STOP."*

They can: `{root value column} eq {recordId} and statecode eq 0` (`ExternalGrantLifecycle.ActiveRowsForRootFilter`),
built from the request's own `recordType` + `recordId`. The client never supplies row ids.

## 3. Owner decision — 2026-09-11

> **Q**: When the record's Expiration changes, what happens to shares that have ALREADY lapsed (still
> `statecode = 0`, date in the past)?
> **A**: **"Renew them too"** (chosen over the recommended "leave lapsed alone").

Every active row gets the new date, lapsed ones included — the literal reading of "applies to all sharing".
Consequence worth knowing: extending the record's Expiration revives any lapsed share still listed on it. The
log line records how many were renewed. Revoked (`statecode = 1`) rows are never touched.

## 4. Design decisions

| Decision | Why |
|---|---|
| Route `set-record-share-expiry`, request names the record ONLY by `recordType` + `recordId` | No legacy `projectId` shorthand: a request carrying both could authorize one record and write another (POML constraint, task 008) |
| Expiry **required** (400 `sdap.access.share_expiry.expiry_required`) | Unlike `/grant` there is nothing to default to — the date is the request. A past date → 400 `sdap.access.grant.expiry_in_past` (097's validator reused, so the rule cannot drift) |
| Write **every** active row, not only the ones whose date differs | `updatedCount` then means "shares now carrying this date", with no ambiguity against "no shares"; re-applying a date is harmless |
| 0 active shares → 200 `updatedCount = 0`, **no write** | The date has nowhere to live until a share exists (099 keeps it as the next add's default). Also mechanically required: `BulkUpdateAsync` throws on an empty list |
| Bound **1,000** shares; query asks for 1,001 | `DataverseWebApiClient.QueryAsync` reads ONE page and drops `@odata.nextLink`. Without the bound, a truncated read would leave unread shares at their OLD (possibly later) date and still report success → 422 `too_many_shares` instead |
| An active row with no id → refuse (500 `enumeration_failed`) | Skipping it would leave that share at its old date while reporting success. `QueryActiveRowsAsync` (the upsert's reader) discards such rows; that is right for an upsert and wrong here, so the new reader keeps them |
| Failed transaction → 500 `write_failed` with an **all-or-nothing** message | Truthful by construction: an outcome-unknown transaction (e.g. timeout after commit) may have applied, so the message claims "never half", not "nothing changed" (cf. #971's `LastException` race — the text is never used to decide behaviour) |
| `catch … when (!ct.IsCancellationRequested)` | An HttpClient timeout surfaces as `TaskCanceledException`; catching only non-cancellation exceptions would turn it into a bare 500. Only a genuinely aborted request propagates |
| Cache fan-out for organization shares via the existing `ExternalOrganizationMembership.QueryActiveMembersAsync` | Reuse, not a new reader (task 020's §11 note asks exactly this). Over-bound / unreadable organizations fall back to the TTL — acceptable per §2.2 |

**Concurrency note.** A share created between this call's read and its write keeps its own expiry. It is still
bounded (097 defaults it to today + 90), and task 099's client sends the toolbar date with every add, so the
two converge.

## 5. Placement Justification (root CLAUDE.md §10 — `.claude/constraints/bff-extensions.md`)

**Decision: in the BFF, on the existing `/api/v1/external-access` group.**

| Criterion | Answer |
|---|---|
| Latency budget against BFF state (<500 ms)? | **Yes** — an interactive toolbar action; the user waits for the result |
| Writes BFF-managed state in the same request? | **Yes** — the participation cache (`ITenantCache`) is invalidated in the same request |
| Retroactive annotation of a stream? | No |
| Event-driven, no synchronous wait? | **No** — synchronous user action → not a Function (ADR-001) |
| Thin facade for external consumers? | No |

Boundary preservation: no CRUD→AI dependency; no new DI registration or module (ADR-010); authorization by the
group's endpoint filter, not middleware (ADR-008); ProblemDetails with reason codes (ADR-019 / ADR-003); cache
holds data, never a decision (ADR-009). ADRs cited: 001, 003, 008, 009, 010, 019, 028 (OBO for the caller's
Write check), 038 (tests). §G (Action/Node/Playbook config) does not apply — no playbook config field.

**§11 — component justification.** *Existing*: `/grant` writes one subject's expiry; `BulkUpdateAsync` updates
many rows atomically; the delegation filter gates the group. *Extension*: `/grant` is per-subject, and N `/grant`
calls from the client is exactly the non-atomic path this exists to avoid; the new route only **composes**
existing pieces. *Cost of doing nothing*: the toolbar Expiration cannot apply to all sharing atomically; a
per-grant shortening can leave some grants at the later date. The one new query helper,
`ActiveRowsForRootFilter`, overlaps `ProjectClosureEndpoint.BuildActiveProjectGrantsFilter` (project-only);
closure was deliberately **not** refactored here (out of scope), but it could delegate to the new helper.

## 6. Contract for task 099

| Case | Response |
|---|---|
| success | 200 `{ updatedCount, expiresDate }` — `updatedCount` = active shares now carrying the date (0 = no shares; keep the date as the next add's default) |
| no Write on the record | 403 `sdap.access.deny.delegation_write_required` |
| missing / unknown `recordType`, missing `recordId` | 403 `sdap.access.deny.delegation_target_unresolved` (authorization runs first) |
| `expiryDate` missing | 400 `sdap.access.share_expiry.expiry_required` |
| `expiryDate` before today (UTC) | 400 `sdap.access.grant.expiry_in_past` (today is valid) |
| more than 1,000 active shares | 422 `sdap.access.share_expiry.too_many_shares` — nothing changed |
| shares unreadable | 500 `sdap.access.share_expiry.enumeration_failed` — nothing changed |
| transaction failed | 500 `sdap.access.share_expiry.write_failed` — all-or-nothing; reload to see which |

Every ProblemDetails carries `traceId` and a human `detail` the picker can show verbatim (owner directive
2026-09-10: a failure gives the user a message, not a bare 500).

## 7. Verification

### 7.1 Publish size (root CLAUDE.md §10 — fresh short-path worktrees, Compress-Archive Optimal, incl. PDBs)

| Side | Commit | Worktree | Files | Zip |
|---|---|---|---|---|
| master | `e0a6f87c4` | `C:\wt098m` | 214 | **45.35 MB** |
| pre-task (session-7 tip) | `4bc66a677` | `C:\wt098p` | 214 | **45.40 MB** |
| task 098 | `23953342a` | `C:\wt098b` | 214 | **45.41 MB** |

**Task 098 delta: +0.01 MB.** Project-cumulative vs master: +0.06 MB. File counts equal on all three sides
(hazard 4 check). Far below the 60 MB ceiling.

### 7.2 CVE

`dotnet list … package --vulnerable --include-transitive` → *"has no vulnerable packages"*. No package added.

### 7.3 Tests (all at KEEP paths)

| File | What it pins |
|---|---|
| `tests/integration/auth/UnifiedAccessControl/RecordShareExpiryTests.cs` (**new**) | which rows change (contact + org + lapsed; not revoked, not another record, not a share also linked to another record), ONE bulk update, the SDK value (via the real transaction builder), cache fan-out, every refusal writes nothing |
| `…/DelegationRuleCharacterizationTests.cs` (+) | 403 `delegation_write_required` for a caller without Write + probed target + **no `BulkUpdateAsync`**; twin passes the gate and gets the handler's 400; legacy `projectId`-only body → 403 `target_unresolved` |
| `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs` (+) | the wire contract: route, 200 `{ updatedCount, expiresDate }`, the two 400 reason codes as literals |

The handler-level fake is **strict**: it applies each `$filter` clause as Dataverse would and throws on a clause
it does not understand, so a filter that GAINS a narrowing clause fails loudly (review finding CR-2).

### 7.4 Perturbations (mandatory — each applied, compiled, observed, restored from git)

| # | Perturbation | Result |
|---|---|---|
| P1 | `BulkUpdateAsync` → per-row Web API `UpdateAsync` loop | **7 fail** incl. the one-transaction test (acceptance ✅) |
| P2 | cache invalidation skipped | **2 fail** (fan-out test + contract 200) (acceptance ✅) |
| P3 | filter drops `statecode eq 0` | **2 fail** (revoked-untouched, zero-shares) |
| P4 | filter drops the root clause | **2 fail** (other-records-untouched, cache set) |
| P5 | delegation filter case returns unresolved | **6 fail** (every route-level test) |
| P6 | SDK value `DateTimeKind.Local` | **1 fail** (stored-date test) |

⚠️ **Lesson 4 applied again.** The first run's P5 hit a transient `CS0016` (build output locked) and P6 printed
**nothing** — neither is a result. Both were re-run individually with the full test tail printed
(`Total 56 / Failed 6` and `Total 56 / Failed 1`) before being counted.

### 7.5 Full suite

| Run | Result |
|---|---|
| `Sprk.Bff.Api.Tests` @ `23953342a` (fresh worktree `C:\wt098c`) | **12,300 passed / 0 failed / 58 skipped** (+20 vs task 097's 12,280) |
| `Spaarke.ArchTests` @ `23953342a` | **196 / 197 — 1 FAIL**: the task-074 endpoint-file census (`ExpectedEndpointFileCount` 117, found 118). Predicted by the ADR check (V1) before the run finished; fixed in the review follow-up (census → 118 + ledger entry) |

_Review follow-up re-verification below._

### 7.6 Quality gates (Step 9.5) — dispositions

Code review (coverage-first, 18 findings, no Critical) and ADR check (13 compliant, 3 violations, 9 warnings)
ran as read-only agents against the fresh worktree.

| Finding | Disposition |
|---|---|
| **ADR V1** census 117 → 118 would fail blocking Tier 1 | **Fixed** — bump + ledger entry (classified: neither metadata nor bytes; inherits `AddDelegationRuleFilter`) |
| CR-1 write-failure TITLE said "Expiry not applied" while the outcome is unknown | **Fixed** — own title "Expiry change not confirmed"; summaries corrected; test asserts the title |
| CR-2 fake ignored unknown filter clauses | **Fixed** — strict fake (throws on an unknown clause) |
| CR-3 id-less and enumeration refusals untested | **Fixed** — two tests |
| CR-4 id-less row reused `enumeration_failed` | **Fixed** — own code `share_unidentifiable` |
| CR-5 a share row with TWO root lookups is selected via one → Write on Y re-dates/revives access on X | **Fixed** — refused, 409 `share_spans_records`, nothing written; test. Directly the POML constraint "never touch another record's grants" |
| CR-6 concurrency ("every share read at the start") | **Fixed** — stated in the class remarks and §4 |
| CR-7 no caller attribution on an app-only write | **Fixed** — caller oid in the success / failure / zero logs |
| CR-8 post-commit invalidation cancellable by the request token | **Fixed** — `CancellationToken.None` after the commit |
| CR-10 "visible on the next evaluation" overstated | **Fixed** — "normally; at worst the 60 s TTL" (fire-and-forget re-population) |
| CR-11 why fan out when siblings do not | **Fixed** — documented (the reader now exists; the POML asks for it) |
| CR-12 "nothing is written" on the 403 was structural only | **Fixed** — asserts `BulkUpdateAsync` Never on the host's `IDataverseService` mock |
| CR-14 / **ADR V2** unresolvable-root 400 had no reason code / traceId | **Fixed** — `record_unresolved` + traceId; test |
| CR-15 `Handle` public | **Fixed** — `internal` |
| ADR W2 private `ExtractTenantId` copy | **Fixed** — `TenantResolution.ResolveTenantId` (the BFF's single tenant resolver) |
| ADR W7 root query has no expiry predicate — dangerous if reused on a read path | **Fixed** — documented WRITE-PATH ONLY |
| ADR W8 "the cache holds no dates" was an unpinned assumption | **Fixed** — stated on `CachedGrantSet`, naming the invalidators that depend on it |
| CR-9 `sprk_granteddate` claim unverified | **No change** — it WAS read live (§2.3 table); this notes file is the evidence |
| **ADR V3** `[Trait("status")]` absent | **Path A, not applied** — the §6.2 values (repaired / real-bug-pending-fix / flaky-quarantined) describe repair-project tests; none fits a new passing test, and no file in `tests/integration/auth/UnifiedAccessControl/` uses the Trait |
| ADR W1 no rate limiting | **No change** — no route in the external-access group has one; a group-level policy is a separate decision |
| ADR W3 upstream 429/503 surface as 500 | **No change** — reason code + truthful message carried; matches siblings |
| ADR W4/W5/W6 `Mock<DataverseWebApiClient>`, `InternalsVisibleTo`, KEEP category | **No change** — repo precedent (the fake interprets the real filter); the POML prescribed the auth path |
| CR-13, CR-16–18 | Info — no action |
