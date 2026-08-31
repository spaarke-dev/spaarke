# Task 040 — WireMock Graph fixture infrastructure

> **Spec FR-D01** · completed 2026-08-21 · rigor FULL (TEST-MODIFYING override)
> **Pulled forward** from Workstream D into W2 so Phase 3's property fixes land protected.

---

## Outcome

A reusable Graph fixture at [`tests/integration/contract/SpeAdmin/`](../../../tests/integration/contract/SpeAdmin/)
that runs **real `SpeAdminGraphService` methods** against a fake Graph endpoint on a loopback port —
no tenant, no credentials, no network — and lets a test assert **both** the outgoing request shape
(`$select`, `$filter`, PATCH body) and the response mapping.

**10 new tests, all green. 10,602 passed / 0 failed** (baseline 10,592).

It found a previously-unknown production defect on its first run (§4).

---

## 1 — The blocker nobody had diagnosed

The task could not start: `Integration/GraphApiWireMockTests.cs` carried six tests skipped as

> *"WireMock.Net path matching returns 500 for all requests in this environment — requires WireMock
> configuration investigation"*

If true, the task's entire mechanism was dead. A probe reproduced the 500 immediately and gave the
real cause, which is not path matching and not configuration:

```
System.IO.FileNotFoundException: Could not load file or assembly
  'MimeKitLite, Version=4.1.0.0, ...'
   at WireMock.RequestMessage..ctor(...)
   at WireMock.Owin.Mappers.OwinRequestMapper.MapAsync(...)
   at WireMock.Owin.GlobalExceptionMiddleware.InvokeInternalAsync(...)
```

WireMock.Net 1.5.45 loads **`MimeKitLite`** at runtime to map an incoming request. The test csproj had:

```xml
<!-- Exclude MimeKitLite to avoid conflict with MimeKit -->
<PackageReference Include="MimeKitLite" Version="4.17.0">
  <ExcludeAssets>all</ExcludeAssets>
</PackageReference>
```

The collision it was added to fix is a **compile-time** one — MimeKitLite re-declares MimeKit's types
in the same namespaces. But `all` also strips the **runtime** asset, so the DLL never reached the test
output and every single WireMock request died in its own exception middleware and came back 500.

**Fix**: `<ExcludeAssets>compile</ExcludeAssets>`. One word. Our code still compiles against MimeKit
alone; WireMock resolves MimeKitLite at runtime.

> The wrong diagnosis is the story here. "Requires WireMock configuration investigation" reads like a
> known-unknown parked deliberately, so nobody re-opened it — and WireMock, the one tool that could
> have caught the §3.2 defect class, sat unusable for the whole of R1. Same shape as
> `AuditLogEndpoints.cs:159`, where a confidently-worded wrong comment kept a bug alive for months.

**Verified no regression**: full suite 10,602 passed / 0 failed; `dotnet list package --vulnerable
--include-transitive` → no vulnerable packages; test-only assembly, no BFF publish impact.

---

## 2 — Escalation trigger: evaluated, does NOT fire

> *"If the Graph SDK's client construction cannot be pointed at a WireMock base address without
> production-code changes, STOP and escalate — the seam change affects task 021."*

**It can, and no production change was made.** `SpeAdminGraphService` has **47 methods that already
take `GraphServiceClient graphClient` as a parameter**:

```csharp
public async Task<IReadOnlyList<SpeContainerSummary>> ListContainersAsync(
    GraphServiceClient graphClient, string containerTypeId, CancellationToken ct = default)
```

The hardcoded `https://graph.microsoft.com/beta` lives in the **private** `CreateGraphClient` (`:4195`)
and `CreateGraphClientFromBearerToken` (`:4212`), which only serve the `GetClientForConfigAsync` path.
Tests bypass that path entirely by constructing the client and passing it in.

**Task 021's decision is untouched.** Whether the production base address should become configurable
remains entirely open — this task deliberately did not pre-empt it.

---

## 3 — Two deviations from the POML

### (a) The `<justification><existing>` premise was wrong

> *"No WireMock fixture exists for SpeAdmin — grep across tests/ shows WireMock.Net referenced in the
> csproj but no Graph fixture."*

Two Graph fakes already existed: `Integration/GraphApiWireMockTests.cs` and
`Mocks/FakeGraphClientFactory.cs`. Per CLAUDE.md §11 both were assessed for extension before anything
new was written:

| Existing | Extend? | Why |
|---|---|---|
| `GraphApiWireMockTests.cs` | **No** | Points a bare `HttpClient` at WireMock and asserts WireMock returned what WireMock was told to return. No production code on the path — it cannot fail for any reason that matters (ADR-038 B7/B10 scaffolding). All six tests skipped. |
| `Mocks/FakeGraphClientFactory.cs` | **No** | A hand-written `HttpMessageHandler` returning fixed stubs for any URL. Deliberately "intentionally simple: it unblocks auth-gated integration tests" — it cannot assert an outgoing request at all, which is the half that catches §3.2. Its own doc comment says richer cases "should use WireMock or a purpose-built fixture". |

Left both in place; corrected the false Skip reason on the first and pointed it at the new fixture.
**Retiring it is task 042's scope, not this task's.**

*Fourth POML premise in this project that did not survive contact with the code.*

### (b) KEEP-path relocation (binding ADR-038 rule)

The POML placed the new files at `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/`. That is **not one of
ADR-038's seven KEEP paths**, and `.claude/constraints/testing.md` is explicit:

> ❌ MUST NOT introduce a new test under any path OTHER than the 7 KEEP categories.

Nor is `tests/unit/domain/**` available — that category is *"pure domain logic … no mocks, no DI,
no I/O"*, and these tests do real HTTP over a loopback socket.

**Placed at `tests/integration/contract/SpeAdmin/`** — the endpoint-contract category (route + status
+ payload shape), here applied to the upstream Graph contract we consume. The csproj already compiles
`..\..\integration\contract\**\*.cs` into this assembly precisely so KEEP-path tests can reach
WireMock and the shared fixtures, so no csproj glob change was needed.

---

## 4 — 🔴 New defect found by the fixture on its first run

**Every container in the recycle bin reports a null deletion timestamp.**

`SpeAdminGraphService.cs:4366-4372` reads `deletedDateTime` out of `AdditionalData`:

```csharp
if (container.AdditionalData != null &&
    container.AdditionalData.TryGetValue("deletedDateTime", out var rawDeletedAt) &&
    rawDeletedAt is string deletedAtStr &&                        // ← never true
    DateTimeOffset.TryParse(deletedAtStr, out var parsed))
```

Kiota does not put a `string` there. Probed against the real SDK:

```
'deletedDateTime' => runtime type: System.DateTime   value: 8/1/2026 5:45:00 PM
TryGetValue('deletedDateTime') = True
  raw is string  => False
  raw type       => System.DateTime
```

Graph sends the value, Kiota parses it, it is sitting in the dictionary — and production **drops it on
a type check**. `DeletedDateTime` is always `null`, so recycle-bin rows cannot be sorted by deletion
date or aged out, and the screen cannot tell that apart from "deleted at an unknown time".

> **Fifth instance of this project's signature defect shape** — a lower layer collapsing a real value
> into an absent one that the upper layer reads as benign. 003 (config load), 005 (audit write),
> 002 (ODataError), 024/§3.2 (storage null), and now this.

**Not fixed here, deliberately.** Task 040 is `parallel-safe=true` *because* it touches no production
code, and the wave rules permit only one `SpeAdminGraphService`-modifying task per wave (W2's is 004).
**Task 022 owns the recycle-bin path** and now inherits a proven root cause plus a ready-made test.

Pinned as a characterization test that names the defect, names the owning task, and states that it
**must fail and be updated** when the fix lands:
`ListDeletedContainers_MapsIdAndDisplayNameButNeverTheDeletionTimestamp`, with the root cause pinned
separately in `DeletedContainerPayload_StoresTheTimestampAsDateTimeNotString`.

---

## 5 — What was built

| File | Purpose |
|---|---|
| `tests/integration/contract/SpeAdmin/GraphWireMockFixture.cs` | The fixture: `CreateGraphClient()`, `StubGet`/`StubPatch`, `SelectFieldsFor()`, `RequestsFor()`, `RecordedGraphRequest.BodyAsJson()` |
| `tests/integration/contract/SpeAdmin/SpeAdminGraphMappingContractTests.cs` | 10 tests over two real mapping paths + the demonstrator |
| `tests/integration/contract/SpeAdmin/README.md` | Usage, the two rules that carry the value, pinned-defect register, the 500 gotcha |
| `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` | `MimeKitLite` `ExcludeAssets` `all` → `compile` |
| `tests/unit/Sprk.Bff.Api.Tests/Integration/GraphApiWireMockTests.cs` | Corrected the false Skip reason; documented the real cause + successor |

### The demonstrator (spec success criterion 18)

A mechanism that cannot be shown to fail is not a mechanism. `WrongPropertyNameInSelect_FailsTheSuite`
runs the same assertion the real tests use against the field set a developer *would* have written
under the §3.2 defect (`majorVersionLimit`), and asserts that it throws. That keeps the proof inside
the green suite instead of leaving a red test as "documentation".

`RequestToAnUnexercisedPath_FailsLoudlyRatherThanSilentlyPassing` guards the fixture itself: asserting
over "requests we saw" would otherwise pass vacuously over an empty list — the same absent-reads-as-
success shape, this time in the test harness.

---

## Verification

| Gate | Result |
|---|---|
| Reusable fixture serving canned Graph responses to a `GraphServiceClient` | ✅ |
| Outgoing REQUEST shape assertable (path, query, `$select` set) | ✅ `SelectFieldsFor()` |
| Wrong property name in a `$select` FAILS the suite | ✅ demonstrator asserts the throw |
| Runs offline — no network, no credentials, no tenant | ✅ loopback WireMock + `AnonymousAuthenticationProvider`; the ctor's Key Vault + HTTP-factory deps are wired to throwing fakes so any outward reach fails loudly |
| No `Mock<HttpMessageHandler>` (ADR-038 B1) | ✅ grep clean |
| No DI-registration / ctor null-check tests (ADR-038 B3/B4) | ✅ grep clean |
| No new NuGet (NFR-02) | ✅ `ExcludeAssets` change on an existing reference |
| `[Trait("Category", "SpeAdminGraphContract")]`, not `[Category(...)]` | ✅ |
| KEEP path (ADR-038 §2) | ✅ `tests/integration/contract/**` |
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ 0 errors, 0 warnings |
| Unit tests | ✅ **10,602 passed**, 0 failed, 97 skipped (+10) |
| ArchTests | ✅ 36/36 |
| Vulnerable packages | ✅ none |
| BFF publish size | ✅ unchanged — **no `src/` file was modified** (43.68 MB, ceiling 60) |

## ⚠️ Not verified

The fixture proves what SpeAdmin **sends** and how it **maps what comes back**. It cannot prove the
canned responses match what Microsoft Graph actually returns today — a fixture is only as truthful as
the schema it was authored from. That is task **041**'s job (`LiveIntegration` against a real tenant),
and the two are complementary: 041 catches schema drift, 040 catches it cheaply and offline thereafter.

**Consequence to hold onto**: a green run here means our code is self-consistent with the documented
schema, not that the platform agrees.
