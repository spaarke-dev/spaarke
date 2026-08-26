# Task 075 — Record-aware container resolver

> **Status**: shipped · **Date**: 2026-08-26 · **Rigor**: FULL (opus @ xhigh)
> Wave 2 of Phase 0c Secure Documents. Task 076 routes the call sites onto this seam.

---

## 1. Step 0 — what was verified before designing anything

| Claim in the POML | Verified how | Result |
|---|---|---|
| No server path reads `sprk_project.sprk_containerid` outside provisioning | `grep -rn "sprk_containerid" src/server/ --include=*.cs` | **CONFIRMED.** Every hit is either provisioning (`ProvisionProjectEndpoint`), a doc-comment, a `sprk_document`/`sprk_container` column list, or `WorkingDocumentService`/`AnalysisChatContextResolver` reading it off a **matter** for Compose/analysis output — none of which is the upload decision. Nothing consumed the stamp as a storage decision. |
| `sprk_issecure` exists on exactly 3 entities | Not hard-coded — see §3.3. The registry derives it from live metadata at runtime, so the count is whatever Dataverse says. | Design does not depend on the number |
| Strategy 2 (`ArchiveContainerId`) is the easy one to forget | `grep -rn "ArchiveContainerId"` | **Wider than the POML said** — see §6 finding F-1. Not 2 sites (`IncomingCommunicationProcessor:868,991`) but **9** across 3 files. |
| Strategy 3 (document's own pointers) needs no change | Read `Infrastructure/Dataverse/DocumentStorageResolver.cs` | **CONFIRMED** — it answers `documentId → (DriveId, ItemId)`, a different question. Untouched. |

---

## 2. The seam, in one paragraph

`IRecordContainerResolver` answers **both** directions of one mapping:

```
ResolveForRecordAsync(entityLogicalName, recordId, nonSecureFallbackContainerId)  →  ContainerResolution
ResolveOwningRecordAsync(containerId)                                            →  OwningSecureRecord?
```

The forward direction **throws** rather than returning a sentinel when a secure record has no
container of its own. The reverse direction is what tasks 073 and 078 authorize against.

### Why the forward direction throws

A discriminated result (`Resolved | FailClosed`) relies on every caller checking the discriminant.
The POML's `<role>` asks for a seam "impossible to bypass **by accident**", and the entire failure
mode being removed is a *silent* substitution. A caller that ignores a return value proceeds with a
shared container; a caller that ignores an exception does not proceed at all. So:

- **secure + own container present** → returns it (`Source = SecureRecordOwnContainer`)
- **secure + own container absent/blank** → **throws** `SdapProblemException("secure_record_container_missing", 409)`
- **not secure** → returns the caller's fallback (`Source = NonSecureFallback`)
- **not secure + no fallback** → returns `ContainerId = null`, `Source = Unresolved` — preserves the
  existing "log a warning and skip" behaviour of the archive path, which is a config-absence case,
  not a security case.

**The load-bearing invariant**: `Source == Unresolved` ⟹ the record is **not** secure. There is no
input for which a secure record yields `Unresolved`, because that branch throws first. Pinned by a
test (`Unresolved_IsUnreachable_ForASecureRecord`).

### Fail-closed also covers "I could not find out"

If securability cannot be *determined* — the metadata probe throws, the record read throws, the
record is missing — the resolver **throws**. It never treats "unknown" as "not secure". This is
build-plan rule 1 ("any error, null, or missing config denies") applied to the question *is this
record secure?* rather than only to the question *where is its container?* An unknown-securability
answer silently read as "not secure" is the same isolation failure with an extra step.

---

## 3. Components

### 3.1 `SecureContainerDecision` — the pure decision

`Infrastructure/Dataverse/SecureContainerDecision.cs`. One `static` method, no I/O, no dependencies:

```csharp
Decide(bool isSecure, string? ownContainerId, string? fallbackContainerId) → Outcome
```

Everything else in this task is data-fetching that funnels into this call. Both the resolver and the
ingest path reach the decision through it, so there is exactly one place in C# where the rule lives.

### 3.2 `RecordContainerResolver` — the data-fetching half

`Infrastructure/Dataverse/RecordContainerResolver.cs`. Registered **Scoped**, unconditionally, in
`Program.cs` next to `IDocumentStorageResolver` (§5). Deps: `ISecurableEntityRegistry`,
`IGenericEntityService`, `ILogger`. No Graph types — ADR-007 respected; this component never touches
SPE, it only decides which container id to hand to `SpeFileStore`.

### 3.3 `SecurableEntityRegistry` — the metadata-derived list

`Infrastructure/Dataverse/SecurableEntityRegistry.cs`. The POML forbids a hard-coded list ("a fourth
securable entity must not silently bypass the resolver"). Derivation is a single
`RetrieveMetadataChangesRequest` filtered to the attribute `sprk_issecure`, projecting only logical
names; any entity that comes back carrying the attribute is securable.

- Cached in the shared `IDistributedCache` for 6h under `sdap:dv:securable-entities`, matching the
  `MetadataService` precedent (ADR-029, one Redis per BFF).
- **Cache failure is graceful; metadata failure is not.** An unreachable Redis falls through to a
  live query (same as `MetadataService`). An unreachable *Dataverse metadata service* throws — see
  "fail-closed also covers I could not find out" above.
- A **negative** result is cached too, so the common non-securable case costs one lookup per 6h
  rather than one metadata round-trip per upload.

### 3.4 The reverse direction, and the ambiguity case that matters

`ResolveOwningRecordAsync(containerId)` queries each securable entity for rows whose
`sprk_containerid` equals the given container, then:

| Claimants found | Answer | Why |
|---|---|---|
| none | `null` — "not a record-owned container" | A BU/archive container. 073/078 decide what to do with that; the resolver does not guess. |
| exactly one, and it is secure | that record | The intended case |
| one secure claimant **plus** any non-secure claimant | **throws** | A secure record's container is also some non-secure record's container. That is co-mingling — the exact condition this wave exists to prevent — so it must be loud, not resolved. |
| more than one secure claimant | **throws** | Two secure records sharing one container is an isolation violation |
| claimants exist but none is secure | `null` | Shared container, as above |

This matters **today**, not hypothetically: three live projects carry the ROOT BU's container id
(POML `<origin>`). Under the old code that is invisible. Under this mapping, the moment one of those
projects becomes secure, the reverse lookup refuses instead of answering.

---

## 4. INV-7 and the client/server split — the honest account

**This is the part the POML told me to surface rather than absorb quietly, so it is stated in full.**

The decision now exists **twice**: once in C# (`SecureContainerDecision.Decide`) and once in
TypeScript (`decideContainer` in `RecordContainerResolver.ts`). A single implementation *is*
technically possible — put the decision on the server and have the client ask over HTTP. I built the
two-half version instead, for three specific reasons:

1. **A recordId → containerId HTTP endpoint is a new disclosure primitive, and this project has a
   live finding of exactly that shape.** Task 070 deliberately *stopped* emitting `driveId` /
   `speFileId` to clients. Task 081 is open on master because
   `Endpoints/Diagnostics/TenantContainerResolverEndpoint.cs` — a route whose purpose is "resolve the
   SPE container id" — takes `tenantId` from the query string and leaks another tenant's container.
   Adding a second container-id-resolving route while the first one is an open cross-tenant finding
   is the wrong direction.
2. **It would need a record-scoped authorization filter that does not exist.** `AddDocumentAuthorizationFilter`
   resolves rights for `sprk_documents` GUIDs. Pointing it at a project/matter id is precisely
   finding #4's wrong-resource-domain defect (`ResourceAccessHandler.ExtractResourceId` accepts
   `containerId`/`driveId`/`documentId`/`id` interchangeably), which task 074 pinned in
   `PolicyOnlyRoutes` rather than accept.
3. **The BFF is not reachable from every client surface.** Task 076's own analysis notes that an
   absent `authFetch`/`bffBaseUrl` skips provisioning silently. A resolver that *requires* the BFF
   would have to fail closed on every upload — including non-secure ones — whenever the BFF is
   unreachable. The client can answer both halves of the question from host-context `Xrm.WebApi`
   under the user's own Dataverse security, with no BFF dependency
   (`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`).

There is also a census cost: `RouteAuthorizationGuardTests.ExpectedEndpointFileCount` is pinned at
111, so a new endpoint file fails the build until classified. That is the ratchet working correctly,
and it is a reason to be sure a new route is warranted — not by itself a reason to avoid one.

### How drift is prevented — a fixture, not a promise

`tests/fixtures/secure-container-decision-table.json` is a machine-readable decision table with 14
cases. **Both** halves' test suites load **that same file** and drive their own pure decision
function against it:

- C# — `tests/integration/auth/UnifiedAccessControl/SecureContainerDecisionTableTests.cs`
- TS — `src/client/shared/Spaarke.UI.Components/src/services/__tests__/RecordContainerResolver.test.ts`

Consequences, all mechanical:

- Change one half's behaviour → that half's test fails against the fixture.
- Change the fixture to match one half → the **other** half's test fails.
- Add a case to the fixture → both halves must implement it.
- Each suite asserts the fixture's **case count** and that every case name was exercised, so a suite
  cannot silently stop reading the file and pass vacuously (the same "vacuous pass" guard task 074
  used in `ScannerAccountsForEveryRegistrationInTheGovernedFiles`).

The residual is honest and bounded: the fixture pins **behaviour**, not source. Two halves can still
diverge in what they *fetch* (which columns, which entity) — only their decisions are pinned. Closing
that would take the record-keyed upload contract in §7.

---

## 5. Placement Justification (root CLAUDE.md §10)

**In the BFF.** Criteria from `.claude/constraints/bff-extensions.md`:

- **Is it a client concern?** No. The reverse mapping (container → owning record) is an
  authorization input for tasks 073/078 and can only be trusted server-side. The forward mapping is
  needed in-proc by server ingest, which has no client at all.
- **Does it belong in Provisioning ControlPlane?** No — this is a per-request read on the document
  path, not environment setup. Provisioning *writes* the stamp; this *reads* it.
- **New package?** None. Uses `MetadataService`'s existing SDK surface and `IGenericEntityService`.
- **New DI registration?** Three, all Scoped, all unconditional (no feature gate → no ADR-032
  Null-Object question arises).
- **Publish size**: see §8.

**Component justification (root CLAUDE.md §11), three questions:**

1. **Existing** — `IDocumentStorageResolver` (documentId → drive/item pointers, i.e. strategy 3);
   `MetadataService` (entity metadata projection); the client BU cascade
   (`getSpeContainerIdFromBusinessUnit`). Verified by grep, not assumed.
2. **Extension** — cannot extend any of them. `IDocumentStorageResolver` answers a different
   question about a different entity (an existing `sprk_document`, not the owning record) and has no
   notion of record security. `MetadataService` is a metadata projector, not a storage decision — but
   it *is* reused as this component's metadata source rather than issuing its own
   `RetrieveEntityRequest`. The BU cascade is client-side by design (INV-7) and has no notion of
   record security. This component is the join.
3. **Cost-of-doing-nothing** — a concrete, current behaviour: `IncomingCommunicationProcessor:868`
   PUTs an inbound email attachment into `Communication:ArchiveContainerId` regardless of whether the
   communication's regarding is a secure matter. Because SPE permissions are additive-only, that byte
   is then readable by every member of the shared archive container and **no later permission change
   can retract it**.

---

## 6. Findings the POML did not anticipate

### F-1 · Strategy 2 is 9 call sites in 3 files, not 2 in 1

The POML and design §5.1c both name `IncomingCommunicationProcessor:868, 991`. Actual inventory:

| File | Lines | What |
|---|---|---|
| `Services/Communication/IncomingCommunicationProcessor.cs` | 868, 991 | inbound attachments; inbound `.eml` |
| `Services/Communication/CommunicationService.cs` | 460, 1259, 1574, 2054, 2146 | outbound archive + 4 more |
| `Services/Communication/MessageAttachmentMaterializer.cs` | 114 | message attachments |

Task 075 routes the two the POML names (its `<outputs>` scope: "the ingest/archive path"). **The other
seven are task 076's**, and 076's POML lists only `IncomingCommunicationProcessor` — so 076 will
under-count unless told. Flagged to the orchestrator; see the final report.

### F-2 · `CommunicationService.cs:2368` already says the legacy path "is no longer used here"

A comment claims `_options.ArchiveContainerId` is no longer used at that site while five other sites
in the same file still read it. Whoever routes that file must not trust the comment.

### F-3 · The reverse mapping is ambiguous on live data *right now*

Three projects share the root BU's container id. Handled (§3.4) by refusing rather than guessing, but
it means the reverse direction is **not** a total function on the current dev data, and task 073 must
treat `null` ("no record owns this container") as its own decision rather than an error.

### F-4 · A securable record is not the same thing as an *upload context*

The resolver answers about the record it is given. A document uploaded against a
`sprk_communication` whose regarding is a secure matter will resolve to the archive container unless
the caller asks about the **parent**. That is why the ingest wiring resolves the communication's
primary regarding first (`RegardingFieldMap.All` priority order) and asks about *that*. Any other
child-entity upload path has the same trap, and it is the shape of 076's escalation trigger.

---

## 7. What this task deliberately does NOT do

- **It does not change the upload contract.** The architecturally cleaner end state is a
  **record-keyed** upload (`PUT …/records/{entity}/{id}/files/…`): the server resolves the container
  from the record it is already authorizing, the client never decides, and the two-implementation
  question in §4 disappears entirely. That is a contract change spanning 073's authorization and
  076's routing, so it is recorded here as the recommended direction rather than smuggled in.
- **It does not migrate anything.** Zero secure projects exist (build plan §2). The escalation
  trigger for pre-existing shared-container content did not fire.
- **It does not touch `sprk_issecure` in the authorization/read path** — Wave 3.
- **It does not grant SPE container permissions.** `GrantMembershipAsync` still has zero callers.

---

## 8. Verification

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | ✅ **11,196 passed / 0 failed / 82 skipped** — baseline was 11,172/0/82, delta **+24** = exactly the new C# tests |
| New C# tests | 24 passed (`SecureContainerDecisionTableTests` 4, `RecordContainerResolverTests` 20) |
| New TS tests | 35 passed (`RecordContainerResolver.test.ts`) |
| `dotnet test tests/Spaarke.ArchTests/` | ✅ 9 failed / 105 passed — **exact known master baseline** (FR-27 ×2, FR-28, FR-29, FR-32, FR-F1, FR-F2, ADR-010, ServiceBusClientGuard). **Zero delta.** All four task-074 route-authorization facts pass, including the pinned 111-file census — this task adds no endpoint file and registers no route |
| Publish size | **45.10 MB compressed incl. PDBs** vs 45.08 MB baseline → **+0.02 MB**. Ceiling 60 MB; escalation threshold +5 MB single-task. Uncompressed 137.61 MB / 135.33 MB excl. PDBs (stated because the baseline convention is *compressed*, which is a trap: the raw directory is ~3× the quoted figure) |
| `dotnet list package --vulnerable --include-transitive` | ✅ no vulnerable packages, any severity |
| `npx tsc --noEmit` | 3 pre-existing errors, none in this task's files (`@spaarke/auth` / `@spaarke/sdap-client` sibling workspace packages are not built in this worktree) |
| `npx eslint` on the new TS | ✅ clean |

## 9. Perturbation results

A perturbation that does not bite is not evidence until the perturbed source is confirmed present in
the built artifact, so **every** run below did an explicit `dotnet build` of BOTH the API and the test
project first — `dotnet test` will otherwise happily reuse a stale assembly and report a false PASS
(this produced a false pass on task 072).

| # | What was broken | Expected | Actual |
|---|---|---|---|
| 1 | `SecureContainerDecision.Decide` — secure + no own container returns `ResolvedFallback(fallback)` instead of `FailClosed`. The exact historical defect. | fail-closed tests go red | ✅ **9 red / 15 pass**: `a secure record with NO container FAILS CLOSED even though a fallback is available`, all 4 blank-form theory cases, `'unresolved' is unreachable for a secure record` (×2, table + code), `the C# decision matches every case in the shared decision table`, `a resolved outcome always carries a container id` |
| 2 | `RecordContainerResolver.ResolveOwningRecordAsync` — the co-mingling refusal condition changed to `secureClaimants.Count > 99` (never true) | reverse ambiguity tests go red | ✅ **2 red / 18 pass**: `two secure records claiming one container refuses`, `a secure record SHARING its container with a non-secure record refuses` |
| 3 | `decideContainer` in TypeScript — same defect as #1, injected into the client half only | the shared-fixture cases go red on the TS side | ✅ **11 red / 24 pass**: the 3 `*-FAILS-CLOSED` fixture cases, the vacuous-pass guard, the secure-record invariant, and all 6 client-resolver fail-closed assertions |

Perturbation #3 is the one that matters most for §4: it proves the shared fixture genuinely pins
**both** halves rather than each half marking its own homework. Perturbation #2's first attempt used
`if (false)`, which **failed to compile** (`CS0162 Unreachable code detected` — warnings are errors
here). Worth recording: that is the failure mode the explicit-build rule exists to catch. Had the
build error been ignored, `dotnet test` would have run the previous assembly and reported green,
which reads exactly like "the perturbation did not bite".

All three perturbations were reverted and the suites re-run green before commit.

---

## 10. Step 9.5 quality gates — 3 CRITICALs found and fixed

`code-review` + `adr-check` ran against commit `6153049`. **0 hard ADR violations**, but **3 CRITICAL
fail-open defects**, all in the FETCH/QUERY layer rather than the decision layer — which is exactly
the residual §4 called "honest and bounded". It was honest; it was not bounded. Recorded here because
the lesson generalises: **the fixture pins the decision, and every remaining defect was somewhere the
fixture cannot see.**

| # | Defect | Why it was fail-OPEN | Fix |
|---|---|---|---|
| **C-1** | The reverse lookup trimmed on the wrong side. Forward normalizes with `Trim()`, so a record stamped `"  b!x  "` stores content in `b!x`; the reverse filtered Dataverse with `Equal` on the *trimmed* input, and Dataverse does not trim stored values → **no match → `null` → "this is a shared container"**. Tasks 073/078 would authorize a secure container as unowned. | The fixture has a dedicated padded-stamp case, so the design already treats padding as a real shape — the reverse half was the only place it broke | Select `sprk_containerid` (the old `ColumnSet` never fetched it, so the query could not self-check) and match **in code**, trim-tolerant and exact. `Like '%…%'` was rejected: SPE drive ids routinely contain `_`, a LIKE single-char wildcard, so it would over-match |
| **C-2** | The reverse lookup silently truncated at 25 rows. `TopCount` does **not** populate `MoreRecords`, so truncation was undetectable by construction. >25 non-secure claimants could push the one secure claimant out of the page → `null` → shared container | The code's own comment conceded the premise — three live projects already share the root BU container, so "many claimants" is the normal case | Split into **two bounded queries**: secure claimants filtered on `sprk_issecure == true` (cannot be crowded out by noise), and a `TopCount 1` co-mingling probe. Hitting the bound now **refuses** (`container_ownership_indeterminate`) instead of answering |
| **C-3** | The TS half failed **open** where the C# half failed closed. `record?.[flag] === true` maps a `null`/`undefined` read to `isSecure = false` → BU fallback container. `IWebApiLike.retrieveRecord` is typed non-nullable and satisfied **structurally**, so TypeScript warns at no call site — and the shipped adapters are not the only implementations | A fail-open client next to a fail-closed server is the worst of the two | Throw before computing `isSecure`. An empty *object* stays non-secure (a real read that returned no columns), which is the pre-existing honest answer |

### Warnings also fixed in the same pass

- **W-1 (ADR-010 headroom).** The two new 1:1 interfaces took the in-assembly count to exactly the
  `knownOneToOneCeiling = 153`. `<= 153` still passes, so "zero ArchTest delta" was true *and*
  concealed that headroom was now zero — the next 1:1 interface anywhere in the BFF would fail the
  build blaming an unrelated project. **`IRecordContainerResolver` was deleted** and the concrete
  class registered instead (the file became `OwningSecureRecord.cs`). No seam was lost: the tests
  substitute the resolver's *dependencies* and exercise the real decision logic, which is
  higher-fidelity than mocking the decision. `ISecurableEntityRegistry` stays — it is a genuine seam
  the tests use. Net: 152, headroom restored.
- **W-2 (ADR-009).** Cache key added to the `SystemCacheKeys` allow-list as
  `DataverseSecurableEntities` with its SYSTEM-LEVEL EXCEPTION (NFR-08) justification, plus the
  inline comment. The code had also mis-cited **ADR-029** (publish hygiene) for "one Redis per BFF";
  the Redis ADR is **ADR-009**. Corrected.
- **W-3.** `CommunicationContainerResolver` converted "couldn't find out" into "not secure" **twice**
  — a null communication read and an empty securable-entity set both returned an empty regarding
  list, which falls through to the shared archive container. Its own `<remarks>` claimed failures
  propagate. Both now throw (`communication_regarding_unknown`, `securable_entities_unknown`).
- **W-4.** An empty securable-entity set was cached for 6h. An empty set is indistinguishable from a
  failed metadata query or an under-privileged identity, so caching it would extend a transient fault
  into a 6-hour window where every record reads as non-secure. **Empty is now never cached**, and
  logged at Error rather than Warning.
- **W-5.** `sprk_issecure` **absent** is not the same as `false`: Dataverse omits null-valued
  properties, and field-level security returns the row with the attribute *masked* rather than
  erroring — both map to non-secure via `GetAttributeValue<bool>`. A blanket throw would be wrong (a
  securable entity legitimately has NULL rows), so absence is now logged distinguishably; the live
  assertion that `sprk_issecure` is neither field-secured nor NULL belongs with task 047.
- **W-6.** `IGenericEntityService.RetrieveAsync` returns non-nullable and the production impl
  **throws** on not-found, so the documented `container_record_not_found` 404 was unreachable and a
  deleted record surfaced as a raw fault — which the ingest helper's catch did not classify as
  permanent, producing an unwinnable retry loop. Not-found is now normalized to the documented 404
  (matched narrowly, so a timeout is never mis-classified as permanent).

### Perturbation round 2 — the three new guards

| # | What was broken | Actual |
|---|---|---|
| 4 | C-1's trim-tolerant code match reverted to an untrimmed comparison | ✅ **1 red** — `reverse (C-1): a PADDED stored container still resolves to its owner`. The substring test correctly stayed green: that perturbation does not introduce over-matching |
| 5 | C-2's truncation refusal disabled (`>= int.MaxValue`) | ✅ **1 red** — `reverse (C-2): hitting the probe bound REFUSES instead of reporting 'unowned'` |
| 6 | C-3's empty-read guard removed from the TS half | ✅ **2 red** — both `FAILS CLOSED when the record read resolves to null / undefined` |

### Re-verification after the fixes

| Gate | Result |
|---|---|
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | ✅ **11,199 passed / 0 failed / 82 skipped** (+27 vs the 11,172 baseline) |
| TS | ✅ 38 passed |
| ArchTests | ✅ 9 failed / 105 passed — **still the exact known baseline, zero delta**, now with ADR-010 headroom restored |
| Publish | ✅ **45.10 MB** compressed incl. PDBs (unchanged; +0.02 vs 45.08 baseline) |
| CVE | ✅ clean |
| eslint / tsc | ✅ clean (the 3 `tsc` errors remain pre-existing, none in these files) |

### What the review found that the design genuinely missed

1. **The fixture pins the decision, not the fetch — and all three CRITICALs lived in the fetch.** §4's
   residual was understated. The perturbation evidence proves the *decision* is pinned; it says
   nothing about the query layer, which is where the risk actually was.
2. **Two-hop children are still a live gap.** F-4 covers `communication → secure matter`. It does
   **not** cover `communication → sprk_invoice → secure matter`: `sprk_invoice` is in
   `RegardingFieldMap.All` but is not securable, so it is skipped and the attachment lands in the
   shared archive. The project's model says children inherit one hop via a denormalized core
   ancestor; the container decision does not follow that stamp. **Not fixed here** — it needs the
   Phase 3 ancestor stamp (tasks 050–055) and is filed as a finding, not faked.
3. **`RegardingFieldMap.All` is hard-coded (12 entries) while the securable list is metadata-derived.**
   On the communication path a fourth securable entity bypasses the resolver unless someone also
   edits that map. Benign today and self-limiting (a new regarding lookup needs a map entry anyway),
   but the stated invariant is stronger than what ships.
4. **"ArchTests: zero delta" is a weaker signal than it reads as** — a ceiling-based ratchet consumed
   to zero headroom reports as unchanged. Failure-count parity ≠ ratchet-headroom parity (W-1).

---

## 11. Second review round — the C-1/C-2 fix introduced 4 more defects

A re-verification pass against `7db13de` found that the C-1/C-2 restructure had itself introduced
three new defects plus one warning. **Recorded prominently because the pattern is the real lesson: two
consecutive rounds of fixes on this component each introduced a fresh fail-open or fail-closed defect
in the query layer.** The decision layer has not moved since the first commit; everything since has
been the fetch.

| # | Defect | Class | Fix |
|---|---|---|---|
| **N-1** | The secure probe lost its container-**value** filter (`sprk_issecure == true AND sprk_containerid NOT NULL`, `TopCount 25`). It therefore returned *any* 25 secure records rather than claimants of **this** container. Once the org merely *holds* 25 secure records — the intended steady state, each with its own container — the page fills on every call and the truncation guard throws `container_ownership_indeterminate` **for every container, including the correct owner's**. Tasks 073 and 078 permanently dead at a trivially reachable number | **fail-CLOSED outage**, hard cliff | Restored a selective, trim-tolerant filter: `sprk_containerid Like '%escaped%'` with T-SQL bracket escaping (`_`→`[_]`, `%`→`[%]`, `[`→`[[]`). That answers the original `_`-is-a-wildcard objection properly — bracket escaping is exactly what it is for — while keeping the code-side exact-after-trim compare as the authority |
| **N-2** | The co-mingling probe still carried **the original C-1 bug, mirrored**: `Equal` on the trimmed input with `ColumnSet(false)`, so it could not self-check. A non-secure record stamped `"  b!x  "` sharing a secure record's `b!x` was invisible → `nonSecureClaimantCount` stayed 0 → the ambiguity refusal never fired → the secure record was named **sole owner of a co-mingled container** | **fail-OPEN**, on the exact condition this wave exists to detect | Same shape as the secure probe: select the column, `Like` filter, trim-compare in code |
| **N-3** | `sprk_issecure NotEqual true` is SQL `<> 1`, and `NULL <> 1` is **UNKNOWN**, so NULL-flagged rows were **excluded** from the co-mingling probe — while W-5's own fix documents exactly why those rows exist and are expected (Dataverse does not back-fill Two Options columns; FLS masks the value). A second, independent blind spot in the same detector | **fail-OPEN** | Nested `LogicalOperator.Or`: `(NotEqual true) OR (Null)` |
| **N-4** | `IsRecordNotFound` matched localized message substrings. Locale: Dataverse fault messages are localized, so on a non-English org classification silently stops and W-6's raw fault returns. Over-breadth: *"Attribute sprk_issecure was not found"* — a real schema/FLS error — was reported to the operator as "the record does not exist", misdiagnosing the very case W-5 exists to surface | WARNING | Typed: `FaultException<OrganizationServiceFault>` with `ErrorCode == -2147220969` (`0x80040217 ObjectDoesNotExist`) |

**Structural fix beyond the four**: pass 2 now runs **only when a secure claimant exists**. Probing a
shared BU container — which legitimately has hundreds of non-secure claimants — would otherwise fill
the page and turn the ordinary shared-container case into a refusal, breaking task 078 for every
normal container.

### The test double was lying twice, and that is the most important finding in this round

Perturbation revealed that **two of the four new regression tests passed vacuously** on their first
run. The double had been written to model *intent* rather than to evaluate the query:

- **N-3 passed vacuously** because the double routed rows to pass 1 / pass 2 by `Flag == true`, so
  reverting the nested `Or` to `NotEqual`-only changed the query but not the double. Fixed by
  evaluating the query's real flag conditions under **SQL three-valued logic** (`NULL <> 1` →
  UNKNOWN → excluded).
- **N-2 passed vacuously** because the double looked only for a `Like` condition and **fell back to
  match-everything** when it found none, so reverting to `Equal` removed the Like and the fallback
  matched every row. Fixed by evaluating whichever operator the query actually used — `Like` as an
  unescaped substring match, `Equal` as exact and untrimmed.
- Both helpers now **throw** on an unmodelled operator, and on a probe carrying no flag/container
  condition at all, rather than defaulting to permissive. A double that defaults to permissive cannot
  test a filter.

The generalisable rule, and the reason this is recorded rather than quietly fixed: **a test double
that encodes what a query is *for* cannot detect a change in what it *does*.** Both vacuous passes
looked identical to a real pass — green, fast, correctly named — and were caught only because every
guard was perturbed individually.

### Perturbation round 3

| # | Broken | Result |
|---|---|---|
| 7 | N-1's container-value filter removed from the secure probe | ✅ 1 red (`N-1: 25 secure records that DON'T claim this container…`) |
| 8 | N-3's `Null` leg removed from the nested Or | ⚠️ **first attempt: PASSED** (double was lying) → after fixing the double: ✅ 1 red |
| 9 | N-2's `Like` reverted to `Equal` on the trimmed input | ⚠️ **first attempt: PASSED** (double was lying) → after fixing the double: ✅ 1 red |
| 10 | N-4's typed fault check reverted to substring matching | ✅ 2 red (both the localized-message case and the schema/FLS misclassification case) |

### Re-verification after round 2

| Gate | Result |
|---|---|
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | ✅ **11,204 passed / 0 failed / 82 skipped** (+32 vs the 11,172 baseline) |
| New C# tests | 32 (was 27; +5 N-regressions) |
| TS | ✅ 38 passed |
| ArchTests | ✅ 9 failed / 105 passed — exact known baseline, zero delta |
| Publish | ✅ **45.10 MB** compressed incl. PDBs (unchanged) |
| CVE | ✅ clean |

---

## 12. Third review round — D-1, D-2, and the verification-debt ceiling

Pass 3 verified N-1..N-4 correct against source (including that `EscapeForLike` applies `[`→`[[]`
**first** — traced a literal `[_]` through escape and the double's inverse; it round-trips) and
confirmed the structural fix has no third door. Two findings.

### D-1 — the truncation refusal was still vacuous: the double ignored `TopCount`

`ContainerConditionMatches` / `FlagConditionsMatch` evaluated real conditions, but the double returned
**every** matching row and never honoured `query.TopCount`. Both
`container_ownership_indeterminate` tests therefore passed only because the fixture happened to
supply exactly `ClaimantProbeLimit` rows — the count check and the cap read the same constant, so the
test could not tell *"the page filled"* from *"there are 25 rows"*.

**This is the third instance of my own rule, inside the fix for the defect the rule came from.**

Fixed: the double now applies `query.TopCount ?? int.MaxValue`, plus a **30-row** test so the refusal
is driven by the real cap rather than by fixture size, and a just-under-the-cap test so the bound is
pinned as a *threshold* rather than as "any largish number refuses".

**One correction to the reported perturbation.** The suggested check — *delete `TopCount` from either
query and both tests stay green* — **does not bite, and would not have bitten even after the fix.**
Removing the cap only *increases* the returned row count, and the guard is `Count >= ClaimantProbeLimit`,
so the refusal fires more readily rather than less. Verified empirically (P11: 0 red).

The dangerous divergence is the **opposite** direction — cap **below** the check threshold — because
then truncation goes undetected and a claimant beyond the page is silently missed: fail-**open**.
That direction is now caught (P12, cap 5 vs threshold 25: **2 red**, including the new 30-row test).
And critically, **P12 only bites because of the D-1 fix**: with the double ignoring `TopCount` it
returned all 30 rows regardless of the cap, so `30 >= 25` fired and the test passed. So the fix is
load-bearing — just for a different perturbation than the one reported. The reviewer's diagnosis was
right; the proposed proof was not the one that works.

### D-2 — two definitions of container equality, the looser one on the byte path

`RecordContainerResolver.IsSameContainer` uses `Ordinal`; `CommunicationContainerResolver` still built
`secureContainers` with `StringComparer.OrdinalIgnoreCase`. Two secure records whose containers differ
only in case would collapse to one entry, `Count > 1` never fires, and `Single()` writes the bytes
into whichever was inserted first — one of two different secure records' containers, chosen by
iteration order. Reachability is negligible (SPE ids are base64url and case-significant); fixed anyway
because it is a security-identity comparison and this task was defining "same container" two ways
across its own two components. Now `Ordinal`.

### Cleanups in the same pass

- Deleted the dead `isSecureProbe` / `MatchesSecureProbe` routing pair and its stale "stable key"
  comment. Routing is now purely condition-evaluation, so there is no discriminator to mis-key — the
  comment was describing a mechanism that no longer exists.
- `ContainerConditionMatches` no longer flattens `Criteria.Filters` into the top-level list and ANDs
  everything. Harmless today (no container condition sits inside the `Or`), but it would silently
  mis-model the day one does — ANDing the members of an `Or` is simply wrong. It now **throws** if a
  container condition appears nested.
- Removed the dead `NotNull`/`Null` arms for the container column and `NotNull` for the flag. Neither
  probe emits them any more, so they were model code asserting nothing; an unmodelled operator now
  throws, which is the loud outcome.

### 🔻 OPEN VERIFICATION DEBT — the component's ceiling (recorded verbatim, per the coordinator)

Another review round has **low expected yield**, because both verification mechanisms are blind in the
same place. The fixture pins the decision and cannot see a query; the double now pins the query —
*against a model of Dataverse written by the same agent that wrote the query*. The perturbations prove
the code matches the double; **they cannot prove either matches Dataverse.**

Five claims in this component are currently **unfalsifiable by any test in this repo**:

1. That `Like` honours T-SQL bracket escaping (`[_]`, `[%]`, `[[]`).
2. That `NotEqual` excludes NULL under three-valued logic.
3. That `TopCount` does not populate `MoreRecords`.
4. That `Null` works on a two-option attribute.
5. **That Dataverse string collation is case-INSENSITIVE while both `IsSameContainer` and the double
   use `Ordinal`** — so the double is strictly *stricter* than the platform and can never surface case
   behaviour. This is also why **D-2 went unnoticed for three rounds.**

**Recommendation**: **task 047 gains an explicit Dataverse operator-semantics assertion list** covering
those five, plus the `sprk_issecure` field-security / NULL check already booked onto it. Until that
runs, the five claims above are assumptions carrying a fail-open or fail-closed consequence each.

**Sequencing** (corrected): **task 078 should not ship before 047**, as the first real consumer of the
reverse direction. Task **073 is NOT gated** on this — it retired its three routes rather than
consuming this seam, so as shipped it consumes nothing from 075. An earlier draft of this
recommendation said "073 and 078"; that is stale for 073.
