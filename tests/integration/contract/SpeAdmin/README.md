# SpeAdmin ↔ Graph contract tests

`GraphWireMockFixture` stands up a fake Microsoft Graph endpoint on a loopback port and hands out a
real `GraphServiceClient` pointed at it. Production `SpeAdminGraphService` methods run against it
unchanged — no tenant, no credentials, no network.

> Built by task 040 of `sdap-SPE-admin-app-r2` (spec FR-D01). Every Phase 3 task that changes a
> `$select`, a PATCH body, or a property name adds its coverage here.

---

## Why it exists

The 359 tests across the 14 SpeAdmin test files make **no HTTP call and stand up no host**. The Graph
interaction is the substance of the app, and it had zero automated coverage — which is how R1 closed
75 tasks reporting "4176 passing, 0 failing" for an app that had never worked.

The defect class this defends against (spec §3.2) is **a property name that does not exist on the
real API**: `majorVersionLimit` for `itemMajorVersionLimit`, `storageUsedInBytes` for
`maxStoragePerContainerInBytes`. Those cost nothing to write and fail silently at runtime.

That defect lives in the **request**, so a test that only checks response mapping cannot see it.
Both directions are first-class here.

---

## Writing a test

```csharp
[Trait("Category", "SpeAdminGraphContract")]
public class MyContractTests
{
    [Fact]
    public async Task ListContainers_RequestsTheDocumentedSelectFieldSet()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet("/storage/fileStorage/containers", """{"value":[{"id":"c1"}]}""");

        await CreateSut().ListContainersAsync(graph.CreateGraphClient(), containerTypeId);

        graph.SelectFieldsFor("/storage/fileStorage/containers")
             .Should().BeEquivalentTo("id", "displayName", "containerTypeId");
    }
}
```

### API

| Member | Purpose |
|---|---|
| `CreateGraphClient()` | Real `GraphServiceClient` pointed at the fake endpoint (anonymous auth — cannot reach Entra ID) |
| `StubGet(path, json, status)` / `StubPatch(...)` | Canned response. Path is a prefix; query options are matched separately so you can *assert* them instead of reproducing them |
| `SelectFieldsFor(path)` | The `$select` field set as an unordered set — **the assertion that catches §3.2** |
| `RequestsFor(path)` / `AllRequests` | Observed requests: method, path, raw query, body |
| `RecordedGraphRequest.BodyAsJson()` | Parsed body — use to assert PATCH property names |

---

## Two rules that carry the value

**1 — Author canned responses from Microsoft's documented schema, never from our code.**
A fixture written by copying our own property names agrees with our own bugs. The test only has
teeth when the two sides come from independent sources.

**2 — When you pin a known defect, say so at the top of the test.**
Some tests here assert behavior that is *wrong* (see `..._PinningTheKnownDefect` and
`..._MapsIdAndDisplayNameButNeverTheDeletionTimestamp`). That is deliberate: the defect is owned by
a later task, and an executable record of it is better than a comment. Each names the owning task and
says **the test must fail and be updated when the fix lands**. Deleting one instead of updating it
would restore exactly the silence this project exists to end.

---

## Verified defects pinned here

| Defect | Site | Owner |
|---|---|---|
| `StorageUsedInBytes` hardcoded `null` — Storage tile is always blank | `SpeAdminGraphService.cs:645` | task 024 |
| `deletedDateTime` guarded by `is string`, but Kiota stores a `DateTime` — **every recycle-bin row is undated** | `SpeAdminGraphService.cs:4368` | task 022 |

The second was found by this fixture on its first run. See `notes/task-040-completion.md`.

---

## The seam — and what was deliberately not changed

No production change was needed. **47 methods on `SpeAdminGraphService` already take
`GraphServiceClient graphClient` as a parameter**; a test passes one in directly.

The private `CreateGraphClient*` helpers still hardcode `https://graph.microsoft.com/beta`
(`SpeAdminGraphService.cs:4195`, `:4212`). Making that base address configurable is **task 021's**
decision and was left alone on purpose — the task's escalation trigger names it explicitly.

---

## Gotcha: a blanket 500 from WireMock

WireMock.Net 1.5.45 loads **`MimeKitLite`** at runtime to map incoming requests. The test csproj
carries a `MimeKitLite` reference with `<ExcludeAssets>compile</ExcludeAssets>` — excluding *compile*
avoids the type collision with `MimeKit` while keeping the runtime asset present.

If that is ever changed back to `all`, **every** WireMock request returns 500 with
`FileNotFoundException` inside `GlobalExceptionMiddleware`. That is what disabled the six tests in
`tests/unit/Sprk.Bff.Api.Tests/Integration/GraphApiWireMockTests.cs`, misfiled for years as a
"path matching" problem. Check this first.
