# Task 042 — scaffolding-test retirement inventory

> 2026-08-27 · FR-D03 · ADR-038 §7 (17 bans) + §2 (KEEP paths)
> Classification by 5 parallel read-only agents; **all edits applied by the main session**.

---

## Headline numbers

| Metric | Before | After | Δ |
|---|---|---|---|
| SpeAdmin test **cases** (`--filter FullyQualifiedName~SpeAdmin`) | **722** (721 pass, 1 skip) | **356** (356 pass, 0 skip) | **−366** |
| SpeAdmin `[Fact]`/`[Theory]` **methods** in the non-KEEP location | 378 across 15 files | 147 across 7 files | −231 |
| Files deleted outright | — | **8** | |
| Tests relocated to KEEP paths (not deleted) | — | **4 + 2 new ArchTest rules** | |
| `Spaarke.ArchTests` total | 108 (102 pass / **6 fail**) | 111 (105 pass / **6 fail**) | +3 pass, **±0 fail** |

The 6 ArchTest failures are **pre-existing** — proven by `git stash -u` + re-run, not assumed. They are
FR-27 ×2 (provisioning secret shape), ADR-010, `ServiceBusClientGuardTests`, FR-F1, FR-F2. None are in
files this task touched.

**The skipped test is gone.** `722` included one `[Fact(Skip=…)]` — the manual-test-plan marker.

---

## The structural finding that shaped everything

**`tests/unit/Sprk.Bff.Api.Tests/**` is NOT an ADR-038 KEEP path.** All eight KEEP categories are
compiled *into that same assembly* via `<Compile Include="..\..\integration\…">` globs in the csproj.
So:

- Everything eligible for retirement sat in exactly one non-protected location.
- Relocation to a KEEP path is a **file move with no csproj change** — the globs already cover it.
- The 040/041 work (contract + seam tiers) landed *on* KEEP paths, which is why it can replace this.

29 SpeAdmin test files exist, not the **14** the POML names — 040 and 041 added 10, and the POML's
"359 tests" was also stale (actual 722 cases). *Re-measure; never inherit a count.*

---

## Deleted outright (8 files, 229 methods)

| File | Methods | Why it went whole |
|---|---|---|
| `Integration/SpeAdmin/Phase2IntegrationTests.cs` | 62 | 52 scaffolding; 6 auth-filter tests replaced (below); 4 mapper tests **relocated**; manual plan **relocated** |
| `Integration/SpeAdmin/Phase3IntegrationTests.cs` | 44 | 43 scaffolding/dead; 1 ADR-007 guard **generalised into an ArchTest rule** |
| `Integration/SpeAdmin/MultiAppSupportTests.cs` | 24 | **Every test targets code with zero callers** — see dead-code section |
| `Api/SpeAdmin/MultiTenantTests.cs` | 21 | All DTO round-trips. Despite the name, **no tenant-isolation content** — "consuming tenant" is an app-permission grant, a different concept from ADR-038's tenant KEEP category. Real behaviour covered by the 041 seam test |
| `Api/SpeAdmin/ContainerColumnTests.cs` | 33 | 31 scaffolding; its 2 `FromDomain` keepers duplicate the pair relocated from Phase2 |
| `Api/SpeAdmin/ContainerTypeEndpointsTests.cs` | 23 | 21 scaffolding; 2 ADR-007 guards generalised into ArchTest rules |
| `Api/SpeAdmin/ContainerTypePermissionTests.cs` | 12 | All DTO round-trips; read path covered by `ContainerLifecycleLiveTests.ContainerTypePermissionGrant_ReadPath_…` |
| `Api/SpeAdmin/RecycleBinTests.cs` | 10 | All DTO/reflection. The one thing that mattered — task 022's `deletedDateTime` defect — is covered directly by `SpeAdminGraphMappingContractTests` |

The whole `Integration/SpeAdmin/` directory no longer exists.

---

## Relocated, not deleted (ADR-038 deletion-safety)

| Destination | Content | Source |
|---|---|---|
| `tests/unit/domain/SpeAdmin/SpeAdminDtoMappingTests.cs` (KEEP #7) | 4 tests on `ContainerColumnDto.FromDomain` + `ContainerPermissionDto.FromDomain` — the only tests in Phase2 that called a real mapper | Phase2 |
| `tests/Spaarke.ArchTests/ADR007_NestedDomainRecordTests.cs` (KEEP #8) | 2 new rules + 1 positive/negative control | generalises 5 ad-hoc reflection tests |
| `projects/…/notes/manual-test-plan.md` | Both manual test plans, annotated | Phase2 + Phase3 + MultiApp |

### The ArchTest rules close a real gap (not just a relocation)

`ADR007_GraphIsolationTests` skips any type whose namespace **contains** `Infrastructure.Graph`. Correct
for the facade — but applied by namespace, so it also exempts every **nested domain record declared
inside** `SpeAdminGraphService`. Those records *are* the facade's public output. A Graph SDK type on one
of their properties was invisible to the suite, and five hand-written reflection tests (one per record,
scattered across four files, off the KEEP path) were the only protection.

One rule now covers all of them, unwrapping nullables and generic collections so
`IReadOnlyList<Microsoft.Graph.Models.User>` is caught — a bare namespace check would miss it.

**The return-type rule needed narrowing, and the narrowing is documented, not silent.** As first written
it flagged 9 methods — all returning `GraphServiceClient` from client **factories**
(`IGraphClientFactory.ForUserAsync`, `SpeAdminGraphService.GetClientForConfigAsync`). That is their
contract; ADR-007 governs Graph *models* crossing the facade, not client plumbing. Exactly one exemption
was added, with the reasoning inline. Any `Microsoft.Graph.Models.*` return still fails.

---

## 🔴 An entire file tested code with no callers

`MultiAppSupportTests.cs` (24 tests) and 10 more in Phase3 exercised the owning-app OBO path. Verified by
grep across `src/`, not inferred:

| Symbol | Callers in `src/` |
|---|---|
| `SpeAdminGraphService.GetClientForOwningAppAsync` | **0** (only its own declaration) |
| `SpeAdminTokenProvider.ValidateOwningAppSecretsAsync` | **0** — despite its doc claiming "called during startup validation" |
| `SpeAdminTokenProvider.FetchOwningAppSecretAsync` | **0** |
| `SpeAdminTokenProvider.AcquireOwningAppTokenAsync` | 1 — from the unreachable method above |

This is task 010's `UNWORKABLE` verdict and task 011's path-A pivot, showing up at the test layer: **34
tests were green against a feature that cannot execute.**

The dead production code is **still DI-registered and still shipped in the BFF publish**. That is a
CLAUDE.md §11 removal candidate and **out of 042's scope** (test-scope only) — filed below.

---

## 🔴 Tests that defended a defect

`SearchContainersTests.SkipTokenEncoding_ProducesCorrectNextOffset` (Theory ×3) and
`SkipTokenDecoding_MalformedToken_DefaultsToZeroOffset` — **deleted**.

Both pinned a **numeric from-offset** skip-token scheme and their comments claimed to *"mirror the token
decoding / encoding logic in `SearchContainersAsync`"*. Production forwards Graph's **opaque** OData
`$skiptoken` and its own remarks say *"The previous numeric `from`-offset token has no meaning here."*
There is no `int.TryParse` in the method.

They passed only because they never called production — they re-implemented the dead scheme in the test
body and asserted against their own copy (B6). A reader trusting them would learn a contract that does
not exist. A comment block at the deletion site records this so it is not re-added.

This is the **second** instance in the project of tests defending a defect; the first was task 023's
10 tests pinning `ValidSharingCapabilities = {disabled, view, edit, full}`, of which 3 were not Graph
values at all.

---

## Correction applied to the classification

Agent 1 marked Phase2's 6 auth-filter tests **MAINTAIN**. I checked and **overrode to SCAFFOLDING**:
both suites construct the same `new SpeAdminAuthorizationFilter(logger: null)`, and all six branches
(`oid` identity, no-identity 401, `Admin`, `SystemAdmin`, `IsInRole`, non-admin 403) are covered by
`SpeAdminAuthorizationLayerTests` — which uses the **real** `EndpointFilterInvocationContext.Create`
where Phase2 mocks it (B5). Replaced, and replaced by a *better* test.

Only branch covered by neither: the `NameIdentifier`/`sub` userId fallback. Phase2 did not cover it
either, so deleting loses nothing. Noted as a gap below.

All 5 agents reported honestly, including "I could not independently verify X" — a marked improvement on
task 041, where an agent silently skipped an instruction.

---

## 🔔 ESCALATED — `SecurityEndpointTests.cs` (20 tests, NOT actioned)

All 20 match a ban (18 × B16/B17 DTO round-trips; 2 × B6 re-implementing the handler's `Select` mapping
inside the test). **But no `SpeAdminSecurity*ContractTests` exists anywhere** — deleting them takes
`/api/spe/security/alerts` and `/api/spe/security/score` to zero tests of any kind.

**My reading: the POML's escalation trigger does not fire.** The trigger protects "the ONLY coverage of a
real behaviour." These tests never call the endpoints, so real coverage is already zero. Deleting them
moves the *count* 20 → 0 and the *real* coverage 0 → 0.

It is worth naming what these 20 tests are: **this project's signature defect shape, applied to its own
test suite** — a layer reporting success while not succeeding. Deleting them makes the gap honest.

**Recommendation**: delete, and file the Security contract-test gap. **Left in place pending an operator
decision**, because the alternative reading — "don't zero out a feature's tests before its replacement
exists" — is legitimate and the cost of waiting is one follow-up task.

---

## Not yet retired — 6 files, ~104 scaffolding methods remain

Classification is **complete** for these; the deletions are not applied. `/test-diet` at task 090 is the
designated final classifier, and the POML forbids force-classifying AMBIGUOUS here.

| File | Methods | Scaffolding | Keep | AMBIGUOUS — why held |
|---|---|---|---|---|
| `RegisterContainerTypeTests.cs` | 33 | 23 (+5 ADR-007 now redundant with the new rules) | 5 | SharePoint-REST URL construction is the **only** coverage of the *working* register path |
| `UpdateContainerTypeSettingsTests.cs` | 24 | 17 | 7 | `ValidSharingCapabilities` cluster — the **flagship task-023 regression guard** |
| `SearchItemsTests.cs` | 19 | 12 | 6 | 1 held: asserts `BeOneOf(BadRequest, InternalServerError)` — tolerates a 500, may mask a defect |
| `SearchContainersTests.cs` | 17 | 16 | — | 1 held: empty-query → 400, no contract-test equivalent |
| `BulkOperationTests.cs` | 18 | 15 | — | 3 held: 500-item cap, user/group XOR, role allow-list — **zero** replacement anywhere |
| `CustomPropertyTests.cs` | 16 | 12 | — | 4 held: empty/whitespace property-name rejection — **zero** replacement |

⚠️ One test in `ContainerColumnTests` (now deleted) had a latent defect worth recording: its
`InvalidOrEmptyConfigId_FailsValidation` Theory had a guarding `if` that never fired for the
all-zeros-GUID row, so **that row executed no assertion at all** and "passed" without testing anything.

⚠️ `UpdateContainerTypeSettingsTests.ValidSharingCapabilities_HasExactlyFourEntries` **passes but its
failure message is wrong**: it reads *"exactly 4 … disabled, view, edit, full"* — three of which are the
values task 023 disproved, and which are explicitly negative-controlled three lines away. The count is
coincidentally right (Graph's enum has 4 members today). **Fix the message, keep the test.** Not yet done.

---

## Coverage gaps found (real, pre-existing, none created by this task)

| Gap | Evidence |
|---|---|
| **Security endpoints** — no contract test at all | escalated above |
| **Bulk operations** — validation only ever mirror-tested; the file's docstring claims to cover `BulkOperationService`, which **no test ever constructs** | 3 AMBIGUOUS held |
| **Container columns** — allow-list, `configId`, no-op PATCH, name-required | no `/columns` contract test |
| **Register container type** — `spe.containertypes.register.*` codes never asserted against real output | mirror tests only |
| **CT-006 app-permissions** endpoint | distinct from owner grants (task 027) |
| **Audit logging on container-type creation** | only a reflection check that the method signature exists |
| **`NameIdentifier`/`sub` userId fallback** in the auth filter | covered by neither suite |
| **Dead owning-app code still DI-registered and shipped** | `SpeAdminModule.cs`; CLAUDE.md §11 candidate |

---

## Unrelated pre-existing failure found while measuring

`Sprk.Bff.Api.Tests.Integration.SseStreamingIntegrationTests.Cancellation_NoLingeringBackgroundTask_AfterClientAbort`
fails in the full-project run and **passes in isolation** (167 ms) → order/timing-dependent, **flaky, not
a regression**. Not a SpeAdmin test; recorded, not fixed.

This is the adjudication two earlier CI runs never delivered — both were cancelled by pushes mid-run.
Relevant to master's new `classify-and-retry.ps1` determinism work (PR #830).

---

## ✅ RESOLVED 2026-08-27 — the Security gap is closed, so the escalation no longer blocks

The escalation above left `SecurityEndpointTests.cs` (20 tests) in place on one specific ground:

> *"don't zero out a feature's tests before its replacement exists"*

**The replacement now exists**: `tests/integration/contract/SpeAdmin/SpeAdminSecurityContractTests.cs`
— 11 contract tests on a KEEP path, exercising the real Graph request and response through the HTTP
boundary rather than round-tripping DTOs.

| Coverage | Before | After |
|---|---|---|
| Test *count* on the Security surface | 20 | 20 + 11 |
| **Real** coverage of `/security/alerts` + `/security/score` | **0** | 11 |

### What the replacement actually protects

The two load-bearing cases are about **absence**, because a security screen that cannot distinguish
*"nothing is wrong"* from *"I could not check"* manufactures confidence:

1. **`GetSecurityAlerts_WhenAccessDenied_ThrowsRatherThanReportingNoAlerts`** — a swallowed 403 would
   render "No active alerts" to an administrator whose app registration cannot read alerts at all.
   Not a degraded answer; a confident wrong one, on the screen where that costs most.
2. **`GetSecureScore_WhenGraphReturnsNoSnapshot_ReturnsNullRatherThanAZeroScore`** — a default here is
   a fabricated security posture. `0` reads as catastrophic and triggers work that is not needed; a
   max value reads as perfect and suppresses work that is. Neither number was ever measured. (The
   endpoint turns null into **204**, which is honest.)

Plus: both `$select` field sets (the §3.2 wrong-property-name defect class lives in the REQUEST and is
invisible to a response-only test), `$top`/`$orderby` on alerts, `$top=1` on the score history
collection, full-field mapping, and negative controls for omitted optional fields.

### What this does NOT do

**It does not delete the 20.** That decision belongs to `/test-diet` at task 090, and 090 has not been
started. What changed is that the *reason for holding them* is gone — the choice at 090 is now a
straightforward classification, not a trade-off against leaving a feature uncovered.

### ⚠️ Separately: `SearchItemsTests` is worse than "AMBIGUOUS"

Found while running the suite for this work. `SearchItems_WithToken_ValidConfigIdNotFound_Returns400`
**makes a real outbound Dataverse call from `tests/unit/**`.** It passed twice earlier the same session
and then failed with `TaskCanceledException` after ~100 s — the config lookup went from failing fast to
hanging until the HttpClient timeout.

Proven **pre-existing** (`git stash -u` → identical failure and identical duration), so it is not a
regression from work in flight. But it is **non-deterministic by construction**, and its ~2-minute
timeout holds the whole suite's runtime hostage.

For /test-diet this is no longer only "tighten the assertion": the real choice is an offline Dataverse
double (which would make 400-vs-500 actually decidable) or removal. Tightening the assertion alone
converts an intermittent pass into an intermittent failure without establishing anything. Evidence is
recorded in the test file itself, where whoever trips it will find it.
