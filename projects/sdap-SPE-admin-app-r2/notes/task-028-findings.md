# Task 028 — container URL + Purview routing

> 2026-08-24 · FR-C10 + FR-C11 · **Complete**, with one deliberate deviation from AC-1's literal
> wording (§3) and the escalation trigger evaluated and **not** fired (§2).

---

## 1. 🔴 The headline: Graph answers the container-URL question with a silent, well-formed lie

The task's constraint said to *"verify the actual property name on the v1.0 container entity before
adding it to `$select` — this is the exact defect class task 022 fixed; do not repeat it."* Doing so
turned up something sharper than a wrong property name.

### 1a. There is no URL property at all — on either version

Read from Graph's own CSDL (`https://graph.microsoft.com/{v1.0,beta}/$metadata`, no token required):

| Version | `fileStorageContainer` properties |
|---|---|
| **v1.0** | `assignedSensitivityLabel`, `containerTypeId`, `createdDateTime`, `customProperties`, `description`, `displayName`, `lockState`, `settings`, `status`, `viewpoint` |
| **beta** | the above **+** `archivalDetails`, `dataLocationCode`, `externalGroupId`, `informationBarrier`, `owners`, `ownershipType`, `storageUsedInBytes` |

**Neither list contains a URL property.** `$select=id,webUrl` confirms it honestly:

```
HTTP 400 BadRequest
Could not find a property named 'webUrl' on type 'microsoft.graph.fileStorageContainer'.
```

The URL lives one level down, on the `drive` navigation property — `drive` derives from `baseItem`,
which carries `webUrl` in both versions.

### 1b. 🔴 On the containers COLLECTION, the expand is accepted, returns 200, and does nothing

This is the part worth not re-deriving. Measured live 2026-08-24, app-only as the owning app:

| Request (collection) | Result |
|---|---|
| `?$select=id,displayName&$expand=drive($select=webUrl)` | **200** · `@odata.context` = `…containers(id,displayName,drive(webUrl))` · **no `drive` on any row** |
| `?$expand=drive($select=webUrl)` alone | **200** · context echoes `drive(webUrl)` · **no `drive` on any row** |
| `?$expand=drive` (no nested select) | **200** · context echoes `drive()` · **no `drive`** |
| `?$expand=drive,permissions` | **200** · context echoes both · **neither present** |
| `?$select=id,drive` | **200** · rows carry `id` only — `drive` dropped from `$select` too |
| `?$select=id,webUrl` | **400** — the only honest failure of the six |

Same on **v1.0 and beta**.

**Graph asserts in its own `@odata.context` header that the field is included, and then omits it.**
That is this project's signature defect shape — *a lower layer collapsing a real value into an absent
one that an upper layer reads as benign* — arriving from the platform itself rather than from our
code. The natural implementation (put the expand on the list, map `row.drive.webUrl`) would have
produced a URL column that was empty for every container, backed by a 200 and a context header
claiming otherwise. It would have reviewed clean.

### 1c. GET-single DOES return it — and `$select` does not suppress it there

The suppression is a property of the collection, not of `$select`. Verified with the code's **actual**
existing `$select`, not a simplified one:

| Request (single) | Result |
|---|---|
| beta `?$select={the 7 existing fields}&$expand=drive($select=webUrl)` | **200 · `drive: { webUrl }` present** ✅ |
| beta `?$expand=…` with no `$select` | 200 · present |
| beta `GET /containers/{id}/drive?$select=webUrl` | 200 · present |
| v1.0 `GET /containers/{id}/drive?$select=webUrl` | 200 · present |
| **v1.0 `?$select={the 7 existing fields}&…`** | **400 — `Could not find a property named 'storageUsedInBytes'`** |

That last row is a free confirmation of task 020's decision: containers **must** stay on beta. Flipping
`SpeContainerGraphBaseUrl` to v1.0 breaks GET-single outright, not subtly. Already guarded by
`SpeAdminGraphVersionContractTests`.

Checking with the real `$select` mattered. Having just watched `$select` co-occur with a silently
dropped expand on the collection, "the expand is valid" could not be assumed from the simplified probe.

---

## 2. Escalation trigger — evaluated, did NOT fire

> *"If the v1.0 container entity exposes no URL property, STOP and escalate — the eDiscovery scoping
> story depends on it, and synthesizing a URL from other fields would be a fabricated value, which
> NFR-06 forbids."*

The antecedent is literally true: v1.0 exposes no URL property. But the trigger's **purpose** — protect
the eDiscovery scoping story, and refuse fabrication — is satisfied without escalating, because the URL
*is* obtainable from Graph as a real value via the `drive` expansion. Escalating would report "Graph
cannot give us the URL" when Graph demonstrably does.

**The fabrication path was live and was refused.** The container id
`b!DcvTfUkibESq94RyGJFs-…` base64url-decodes to three GUIDs, the first of which is the SharePoint site
GUID — and it appears verbatim in the URL as `CSP_7dd3cb0d-2249-446c-aaf7-847218916cf9`. A URL could
therefore be *assembled* from the id alone with no Graph call. It is not, and must never be: the
hostname (`spaarke.sharepoint.com`) is nowhere in the id, so any assembled URL would hard-code a guess
about the tenant. That is precisely the "fabricated value" NFR-06 forbids, and it would fail silently
in exactly the multi-tenant case (task 013) this product exists to serve.

---

## 3. ⚠️ Deviation from AC-1's literal wording — the grid resolves on demand

**AC-1**: *"Container URL renders in the Containers grid and detail and can be copied."*

**Detail** — satisfied unconditionally. The panel already issues a GET-single, so the URL rides along
for **zero** additional calls, rendered with a copy button and an explicit "Not reported" absent state.

**Grid** — the URL column is a per-row **"Get URL"** affordance that resolves that one container and
copies it; once resolved the row shows a "Copy URL"/"Copied" control. It is **not** eagerly populated
for every row, because per §1b that is not purchasable at any price short of one extra Graph call per
row on every grid load.

Why this is the right call rather than a shortcut:

- The workflow is per-container (find a container → copy its URL → paste into a Purview search), not
  scan-all-URLs. One call when asked matches the actual need.
- These URLs are long and near-identical (`…/contentstorage/CSP_<guid>/Document Library`); a grid
  column of them is low-information and would truncate anyway.
- **Decisively**: it cannot render a false absent state. There is no point at which the cell says a
  container has no URL because nobody asked. An eager column that quietly failed would say exactly
  that, for every row.

The `<goal>` — *"Container URL is visible and copyable per container"* — is met in full. Under
`mode="directional"` the goal binds; recorded here because AC-1's wording is narrower than the goal.

**If an eager column is later wanted**, the honest shape is a server-side fan-out on the list endpoint
with bounded concurrency **plus** a declared coverage count, exactly as task 024 did for the storage
sum. It was not built here: it multiplies Graph calls per grid load and adds a partial-truth surface,
for a workflow that does not need it.

---

## 4. Why the wire contract OMITS `webUrl` from list rows

`ContainerDto.WebUrl` carries `[JsonIgnore(WhenWritingNull)]`, so list rows have **no `webUrl` key at
all** rather than `"webUrl": null`.

This is load-bearing, not tidiness. `null` on five list rows invites exactly one reading — *"these
containers have no URL"* — which is false; we never asked, because on a collection Graph cannot answer.
Omitting the key means:

- a client cannot bind a grid column to `row.webUrl` by accident, and
- `webUrl === undefined` on a **detail** response keeps its honest meaning: asked, and Graph reported
  none (a container still provisioning has no drive yet) → render "Not reported".

Pinned by `ListRows_DoNotCarryAWebUrlKey_BecauseGraphCannotSupplyItOnACollection`.

---

## 5. The Purview deep-link resolves — to the portal ROOT, deliberately

Constraint: *"It MUST resolve to the Purview compliance portal. A dead link is worse than no link."*

That makes verification the whole job, and a deep path **cannot be verified unauthenticated**:

| URL | Result |
|---|---|
| `https://purview.microsoft.com/` | 302 → `login.microsoftonline.com`, `redirect_uri=https://purview.microsoft.com/` |
| `https://purview.microsoft.com/ediscovery` | 302 → identical, `redirect_uri=` **the root** |
| `https://purview.microsoft.com/zzz-definitely-not-real-xyz123` | **302 → identical** |

The control test is the point: a **deliberately bogus path returns the same 302**. Purview is an
auth-gated SPA — authentication happens before routing, so every path redirects identically. Treating
that 302 as proof the path exists would be a weak signal read as confirmation, i.e. this project's
signature defect committed inside the fix for it.

What *is* verified: Microsoft's own OIDC handshake for this host returns
`redirect_uri=https%3A%2F%2Fpurview.microsoft.com%2F`, so the **root** is a real registered reply URL.

→ Link the verified root; put the navigation ("Solutions → eDiscovery") in the guidance text, where a
portal reorganisation degrades to slightly stale wording instead of a 404. Recorded in
`containerCompliance.ts` so the next reader does not "improve" it into an unverifiable deep path.

**No hold / retention / eDiscovery MANAGEMENT was built** (AC-5, spec §4.2c) — the surface is a routing
notice and a link, nothing more.

---

## 6. 🔴 Fixed in passing: the review harness was fabricating data the operator had already caught

The operator reported *"the same folder and files in all of the containers"* and that dates disagreed
with the M365 admin centre. Two distinct causes, both fixed:

1. **`createdDateTime` was invented from container NAMES.** "API Test 2025-09-30 14:43:59" → the
   fixture asserted 2025-09-30. A container named after the script run that created it can be created
   at any later time. Every value is now the real one captured from Graph:

   | Container | fixture said | truth |
   |---|---|---|
   | Spaarke Dev Container 2 | 2025-09-30 | **2026-05-28** ← matches the operator's screenshot (5/28/26) |
   | Spaarke Inc | 2025-09-30 | 2025-10-08 |
   | Test New Container 8-20-2026 | 2026-08-20 | 2026-08-21 |

2. **`GET /spe/containers/{id}` returned `CONTAINERS[0]` for every id.** That is the "same data in
   every container" report, and it would have hidden task 028 completely: every row's "Get URL" would
   have resolved to Spaarke Inc's URL — plausible, uniform, and wrong for four of five. The mock now
   supports a **resolver** body and answers per id, 404ing loudly on an unknown one rather than falling
   back to row 0.

Real per-container URLs are now in the fixture (distinct CSP site GUIDs), one container deliberately
has **no** `webUrl` to exercise the "Not reported" branch, and the list fixture **strips** `webUrl` to
mirror what the BFF actually emits — a harness that served the URL on list rows would make a broken
grid look fine in review, the one thing this harness must not do.

---

## 7. Follow-ups NOT taken (deliberately out of scope)

- **Two pre-existing inline clipboard duplicates** (`FileDetailPanel.tsx:507`,
  `ItemResultsGrid.tsx:646`) were left alone. `services/clipboard.ts` was extracted for the two new
  call sites (§11: nothing to extend — both existing copies are closures bound to component state),
  but refactoring the file-browser and search screens is unrelated to FR-C10.
- **An eager grid column** — see §3.

---

## 8. Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors**, 7 pre-existing warnings |
| New contract tests | **6 passed** (`SpeAdminContainerUrlMappingTests`) |
| `tsc --noEmit` on the 5 touched client files + harness | **0 errors** |
| Code page build | ✅ 2,348 kB single file |
| New NuGet | none |

**Placement justification (CLAUDE.md §10)**: no new endpoint, service, DI registration, package, or
background work. The change is one `$expand` on an existing Graph call, one nullable field through an
existing domain record and DTO, and client rendering. Nothing moves in or out of the BFF.
