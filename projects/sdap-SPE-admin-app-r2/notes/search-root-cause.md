# Search — root cause

> **Task 004** (spec FR-A04) · **Recorded 2026-08-21 BEFORE any fix**, per the task's binding constraint.
> **Method**: live app-only calls to Microsoft Graph as the owning app `170c98e1-…` against the Spaarke
> Dev tenant (`a221a95e-…`), container type `8a6ce34c-…`. Read-only; no mutation, no container created.

---

## Verdict

**Classification (b) — wrong entity type.** `fileStorageContainer` is **not a supported `entityTypes`
value on `/search/query`**, on beta or v1.0.

**The spec's leading hypothesis (a) — the app-only permission limitation — is DISPROVEN.** App-only
search works; it returns real results the moment the request is valid.

And there are **three** distinct defects, not one. The Items screen is broken too, for different
reasons, and would still have been broken after "fixing" Search.

---

## The reproduction

The exact body our code emits — captured first from production code via the task-040 WireMock
fixture, so this is what we really send, not what we think we send:

```json
{"requests":[{"fields":["id","displayName","description","containerTypeId"],
              "from":0,"query":{"queryString":"contoso"},"size":25,
              "entityTypes":["fileStorageContainer"]}]}
```

POSTed live to `https://graph.microsoft.com/beta/search/query`:

```
HTTP 400
{"error":{"code":"BadRequest","message":"The call failed, please try again.","target":""},
 "Instrumentation":{"TraceId":"723c0963-e408-33ee-40cf-4ecf0a24a735"}}
```

> ⚠️ **"The call failed, please try again." is Graph's own message, not our wrapper.** The screen text
> everyone read as a Spaarke placeholder is the upstream payload verbatim. Task 001's error surface
> reports it faithfully — and it is *still* useless. No amount of error-surfacing work would have made
> this diagnosable; only a differential experiment did.

---

## The isolation matrix

All calls app-only, same token (roles: `FileStorageContainer.Selected`,
`FileStorageContainerTypeReg.Selected`, `Files.ReadWrite.All`, `Files.SelectedOperations.Selected`,
`Files.ReadWrite.AppFolder`).

| # | Request | Result |
|---|---|---|
| A | `fileStorageContainer`, beta, our exact body | 400 generic |
| B | `fileStorageContainer`, beta, no `fields` | 400 generic |
| C | `fileStorageContainer`, **v1.0** | 400 generic |
| D | **`driveItem`**, beta, no region | 400 — **`"Region is required when request with application permission."`** |
| E | **bogus** `notARealType`, beta | 400 generic |
| F | `GET /storage/fileStorage/containers?$filter=containerTypeId eq …` | **200** — real containers |
| G | `driveItem` + `"region":"NAM"` | **200 — real hits** |
| H | `fileStorageContainer` + `"region":"NAM"`, beta | 400 generic |
| I | `fileStorageContainer` + region, **v1.0** | 400 generic |
| J | **bogus** type + region | 400 generic |

**How the matrix decides it:**

- **G proves app-only search works.** Hypothesis (a) is dead — the permission model is not the barrier.
- **D vs A** — a *supported* entity type gets a **specific, actionable** error. Ours gets the generic one.
- **H/I vs J** — `fileStorageContainer` and a **deliberately nonexistent type** produce the *identical*
  response, with and without region, on both API versions. That is the signature of an unrecognized
  `entityTypes` value.
- **F** — the app-only token reads containers perfectly through the storage API. Not a permission gap.

---

## Three defects

### D1 — Container search targets an entity type Graph does not expose to `/search/query`

`SpeAdminGraphService.cs:3086` — `AdditionalData["entityTypes"] = new string[] { "fileStorageContainer" }`.

The serialization comment at `:3084` is **correct** (verified: the wire body carries a proper JSON
array). The bug is not the encoding; it is the value. Container search must not go through
`/search/query` at all.

### D2 — Item search omits `region`, which is mandatory for app-only

`SpeAdminGraphService.cs:4829-4855` builds the `driveItem` request with no `Region`. Probe **D** is the
exact failure; probe **G** is the exact fix. This screen has never worked app-only either.

### D3 — Item search sends `contentSources`, which is invalid for `driveItem`

`SpeAdminGraphService.cs:4845-4847` sets `ContentSources = ["/drives/{driveId}"]` to scope a search to
one container. Live result:

```
HTTP 400  SearchRequest Invalid (EntityRequest Invalid (Content Source is required only for ExternalItem))
```

`contentSources` is accepted **only** for `externalItem`. So even with `region` added, any
container-scoped item search still 400s. Two independent defects on the same call.

---

## The replacement for container search — verified working

`GET /beta/storage/fileStorage/containers` supports OData `$filter` with `contains()`:

| # | Request | Result |
|---|---|---|
| K | `… and startswith(displayName,'API')` | **200**, 1 correct match |
| L | `… and contains(displayName,'Test')` | **200**, correct matches |
| N | `… and (contains(displayName,'Test') or contains(description,'Test'))` | **200**, correct matches |
| O | `…&$top=2` | **200** + a real `@odata.nextLink` carrying `$skiptoken` |
| P | `… and contains(displayName,'zzzznomatchzzz')` | **200 `{"value":[]}`** |

**P matters for acceptance criterion 3**: a genuine no-match is a 200 with an empty array, cleanly
distinguishable from a failure — no ambiguity to resolve in the mapping layer.

**O matters for pagination**: the existing contract's `NextSkipToken` was a numeric `from` offset,
which has no meaning here. The containers endpoint pages with an opaque OData `$skiptoken`.

### Two consequences to accept honestly

1. **Scope narrows from full-text to substring.** `/search/query` would have matched indexed content
   including custom properties. `contains()` matches substrings of `displayName`/`description` only.
   That is a real reduction — but the alternative on offer is a screen that returns nothing at all.
2. **`contains()` is case-sensitive-ish and unanchored**; no relevance ranking, no `total` estimate.
   The response contract's `TotalCount` becomes honestly `null` rather than fabricated.

---

## Escalation triggers — evaluated, neither fires

| Trigger | Fires? | Why |
|---|---|---|
| "root cause is the app-only permission limitation (spec §3.1) → hand to Workstream B" | **No** | Probe **G** returns 200 app-only. The permission model is not the barrier, so this is not task 011's problem and must not be deferred to it. |
| "something architectural, e.g. search requires an index that is not provisioned" | **No** | Probe **G** proves the index is live and returning hits for this tenant. The container index is simply not addressable via `entityTypes`. |

Both hypotheses the task pre-registered are excluded **by evidence**, so the fix belongs here (step 4).

---

## The fix (applied after the above was recorded)

### D1 — `SpeAdminGraphService.SearchContainersAsync`

Replaced `/search/query` with an OData `$filter` against `/storage/fileStorage/containers`. Now takes
`containerTypeId` (supplied by `SearchContainersForConfigAsync` from `config.ContainerTypeId`).

- filter: `containerTypeId eq {guid}` (bare Edm.Guid, ADR-044) `and (contains(displayName,'{term}') or contains(description,'{term}'))`
- `$top` for page size; the opaque OData `$skiptoken` for pagination, extracted from `@odata.nextLink`
- **only the `$skiptoken` value** is returned to the client, not the whole nextLink — a nextLink would
  hand the browser a fully-formed Graph URL including host and filter
- `TotalCount` is now honestly `null`; the endpoint reports no total, and returning `items.Count` would
  make the last page indistinguishable from a complete result set
- new `EscapeODataStringLiteral` doubles apostrophes. The term is interpolated into a single-quoted
  literal, so without it a container named `O'Brien Matter` 400s the whole screen and a crafted term
  could append clauses to a filter we build by concatenation
- reuses the **existing** private `ExtractSkipToken` nextLink parser rather than adding a second one

### D2 / D3 — `SpeAdminGraphService.SearchItemsAsync`

- added `Region` (new `SpeAdmin:SearchRegion` setting, default `NAM`). Configuration rather than a
  constant because region is a property of where the tenant is provisioned — a tenant outside North
  America needs `EUR`/`APC`, and hardcoding would pass here and fail there
- **removed `ContentSources`** entirely
- container scoping now filters hits by `parentReference.driveId` after the call, because Graph offers
  no server-side way to scope a `driveItem` search. Paging is consequently approximate — Graph counts
  a page before our filter runs — so `MoreResultsAvailable`, not the post-filter count, decides whether
  a next-page token is issued

## Verification — replayed live against Spaarke Dev

The request the **fixed code** emits was captured from production code via the task-040 WireMock
fixture, then replayed verbatim against real Graph:

| Replay | Result |
|---|---|
| Container search — exact URL emitted by the fixed code | **HTTP 200**, real containers returned |
| Item search — exact body emitted by the fixed code (`region`, no `contentSources`) | **HTTP 200**, real hits returned |
| No-match term | **HTTP 200 `{"value":[]}`** — empty is distinguishable from failure |
| Escaped apostrophe `contains(displayName,'O''Brien')` | **HTTP 200** — no 400 |

| Gate | Result |
|---|---|
| Root cause captured before the fix | ✅ verbatim Graph error + 10-probe matrix above |
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ 0 errors (7 pre-existing warnings) |
| Unit tests | ✅ **10,618 passed**, 0 failed (+16 new) |
| ArchTests | ✅ 36/36 |
| Publish size | ✅ **43.66 MB** compressed incl. PDBs (was 43.68; ceiling 60) |
| New NuGet | ✅ none |
| ADR-007 | ✅ Graph SDK types stay inside the service; endpoints see domain records only |
| ADR-019 | ✅ error path unchanged — still `ToProblemDetails` with the real Graph code + request id |
| Live-tenant safety | ✅ read-only throughout; no container created, modified, or deleted |

## ⚠️ Not verified

**The Search screen itself was not driven end-to-end**, because there is still no deployed BFF or app
(the standing UI-verification gap). What is verified is stronger than a code trace: the exact request
the fixed code emits was executed against the real tenant and returned real data, and the exact request
the old code emitted was executed against the real tenant and reproduced the reported error verbatim.

The remaining untested link is the client rendering of a successful response.

## Follow-ups this surfaced (not fixed here)

1. **Item search paging is approximate when container-scoped** — a page can come back partly empty
   while more matches exist. Acceptable for an admin screen; worth revisiting if it becomes visible.
2. **Container search lost full-text reach** — `contains()` covers `displayName`/`description` only,
   not custom properties or indexed content. Recovering that needs a different mechanism, not a
   different `entityTypes` value.
3. **`tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/SearchContainersTests.cs`** contains tests that
   *mirror* the old numeric-offset token logic in the test body rather than calling production
   (ADR-038 B6). They still pass because they never touched production code — which is precisely why
   they did not catch any of this. Flagged for **task 042**.

## Secret handling

The owning-app secret was read from Key Vault `spaarke-spekvcert` (`spe-owning-app-secret`) into a
shell variable for token acquisition and **never printed, logged, or written to any file**. Only the
secret's *length* was echoed, to confirm the fetch. No secret value appears in this repo.
