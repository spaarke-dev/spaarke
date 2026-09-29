# Task 073 — `Api/ComposeEndpoints.cs` decomposition

> **Date**: 2026-08-26 · **Branch**: `work/compose-r8-073` · **Rigor**: FULL, effort xhigh
> **Nature**: behaviour-preserving refactor. Route paths, verbs, filters, auth, rate-limit policies,
> handler bodies and wire DTOs are unchanged — proven mechanically (§3), not by inspection.

---

## 1. The acceptance criteria this task was executed against

The POML asks for "below 2,000 lines" and "DELETE its god-class waiver entry". **Both are obsolete.**
`tests/Spaarke.ArchTests/GodClassGuardTests.cs` no longer exists — the God-class LOC ratchet was
**retired 2026-08-20** (root `CLAUDE.md` §11.5 · `docs/standards/COMPONENT-COMPLEXITY.md` ·
`.claude/patterns/testing/god-class-ratchet.md`, which is already marked RETIRED) precisely because it
gated on **line count, the wrong instrument**. There is no waiver to delete.

Executed to the **current** standard instead (owner reframing, 2026-08-26): decompose where
**responsibilities have diverged**; extract each cluster that has its **own reason to change**; do not
manufacture thin components to hit a number (root §11). Line count is reported as an **observation**.

Everything else in the POML's criteria still binds and was verified — see §3–§6.

---

## 2. The decomposition — one reason-to-change per unit

`Api/ComposeEndpoints.cs` held 18 routes, 25 handlers/helpers and 25 wire DTOs in one file. It was not
one component with one reason to change; it was nine, sharing a route prefix. Each extracted unit is a
`Map{Feature}Endpoints` extension method (`bff-extensions.md` §C) over the **same** `RouteGroupBuilder`,
so the group's prefix, `RequireAuthorization()` and tags still apply to every route inside it.

| Unit (file) | Routes | The one reason it changes |
|---|---|---|
| `ComposeEndpoints.cs` (aggregator) | — (owns the group) | The **group's own contract**: prefix, authorization posture, which clusters are mounted, and the shared RFC-7807 / projection wire shapes (`BadRequest`, `MapProjectionResponse`, `MapWarningResponses`, `ComposeProjectionResponse/Warning`). |
| `ComposeMountEndpoints.cs` | `POST /upload`, `POST /project` | How bytes reach the editor for a draft with **no `sprk_document` yet** — the retained-bytes cache convention shared with the chat upload pipeline, and the stateless bytes→projection contract for the Browse door. |
| `ComposeDocumentEndpoints.cs` | `GET /documents/{id}`, `.../promote`, `.../refresh-profile` | The **identity + resume contract** of a document that already lives in SPE: which session a reopen resumes, which record id a drive-item maps to, what the open path seeds. |
| `ComposeSaveEndpoints.cs` | `.../save`, `/documents/create-on-save` | The **save outcome contract**: the size gate, the terminal `ComposeSaveOutcome`, and the exception→ProblemDetails/telemetry mapping. (This is where R8 tasks 013/015/016 landed.) |
| `ComposeTemplateEndpoints.cs` | `.../apply-template` | How a firm/matter template is **resolved** (org-shared Dataverse asset, app-only, via the ADR-013 `IComposeTemplateSource` facade) and merged as chrome. The only Compose route that mints a Dataverse token. |
| `ComposeCheckoutEndpoints.cs` | `.../checkout`, `.../checkin`, `.../heartbeat` | The **document lock model**. The only cluster backed by `DocumentCheckoutService`, and the only one scheduled to change wholesale when Phase 5 wires real check-out. |
| `ComposeAnnotationEndpoints.cs` | `.../pull-annotations`, `.../reanchor-annotations`, `GET|POST /sessions/{id}/annotations` | The **annotation + anchor model**: what is extracted from `w:comment`/`w:ins`/`w:del`, how a prior anchor re-binds after a Word round-trip, what annotation state a session carries. |
| `ComposeSyncEndpoints.cs` | `POST /api/compose/webhooks/spe-doc-changed`, `.../check-changes` | The **Graph change-notification contract** and its subscription/etag substrate — including the only fail-closed **unauthenticated** surface in Compose (HMAC filter + constant-time clientState), which now sits in one reviewable place. |
| `ComposeActiveDocumentEndpoints.cs` | `POST /active-document` | The **chat↔Compose session-pointer semantics**: what "the document the user is acting on" means across multiple Compose tabs, and when a client-minted document session is materialized. |

**Mechanics that kept it a pure move**

- Handlers, mapping statements and DTOs were relocated **by line range** by a one-shot generator, not
  retyped. Line-level conservation check: the only non-blank source lines that changed are the three
  shared helpers going `private static` → `internal static`, plus a dropped `// Handlers` banner.
- Every feature file carries `using static Sprk.Bff.Api.Api.ComposeEndpoints;`, so handler bodies call
  `BadRequest(...)` / `MapProjectionResponse(...)` **unqualified, exactly as before** — zero body edits.
- Files stay at `Api/` root in namespace `Sprk.Bff.Api.Api` (the `Communication*Endpoints` /
  `Documents*Endpoints` sibling-file precedent), **not** `Api/Compose/`. An `Api/{Feature}/` subfolder
  would carry the sub-namespace convention every other subfolder uses, which would move 25 **public**
  wire DTOs to `Sprk.Bff.Api.Api.Compose` and break their consumers in the contract/seam suites.
- The feature classes are `internal`, so no endpoint can be mapped directly from `Program.cs`.

---

## 3. Route-equivalence proof (two independent oracles, both byte-identical)

**Oracle A — source fluent chains.** A mechanical extractor pulls every `Map{Verb}(...)` statement's
FULL chain (comments stripped, whitespace normalized, sorted) from the endpoint source. It captures the
route path, verb, endpoint name, **endpoint filters** (`.RequireWebhookSignature(...)`, which never
reach `Endpoint.Metadata`), `.AllowAnonymous()`, rate-limit policy, `WithMetadata`, and every
`Produces`. 19 chains (18 routes + the `MapGroup`).

```
before  5ead214a0a20297866e47b687662d748  (1 file)
after   5ead214a0a20297866e47b687662d748  (9 files)
```

**Oracle B — built-host endpoint metadata.** The same `CustomWebAppFactory` host the contract suite
boots, enumerated through `EndpointDataSource`, filtered to `api/compose*`, one row per route with verb,
path, endpoint name, authorization posture, rate-limit policy, tags, declared responses **and the raw
`Endpoint.Metadata` type list**. Captured by physically restoring the pre-refactor single file, running,
then restoring the refactor and running again:

```
before  90665cb85651e5d2047130494fb7f11e  (18 rows)
after   90665cb85651e5d2047130494fb7f11e  (18 rows)
```

This is the oracle that proves the **group conventions still land** — `RequireAuthorization()` and
`WithTags("Compose")` are conventions on the `RouteGroupBuilder`, and only the built endpoint shows
whether they reached a route whose `group.MapPost(...)` now lives in another file.

**Per-endpoint filter/auth verification** (from Oracle B): 17 of 18 routes carry `AuthorizeAttribute`
and `tags=Compose`; the 18th, `api/compose/webhooks/spe-doc-changed`, carries `AllowAnonymousAttribute`
+ `rateLimit=webhook-graph` exactly as before, and its `RequireWebhookSignature(...)` filter chain is
byte-identical under Oracle A. `RequestSizeLimitAttribute` is present on exactly the two save routes.

**The oracle is now a permanent test**: `tests/integration/contract/Api/Compose/ComposeRouteSurfaceContractTests.cs`
(endpoint-contract KEEP path, ADR-038 §2) pins the contract-bearing facets and asserts that every
`/api/compose` route except the Graph webhook is authorization-gated.

**Mutation check (both tests observed FAILING before the fix)**: renaming `/upload` → `/upload-mutated`
and adding `.AllowAnonymous()` to `.../checkout` made both tests fail; reverting made both pass.

```
[xUnit.net] Compose: every /api/compose route except the Graph webhook requires authorization (ADR-008) [FAIL]
[xUnit.net] Compose: the /api/compose/* route surface matches the approved snapshot [FAIL]
Failed!  - Failed: 2, Passed: 0
```

---

## 4. `bff-extensions.md` §F.1 asymmetric-registration scan

Step 1 — services injected into Compose endpoint handlers: `IComposeService`, `SpeSyncOrchestrator`,
`ISpeFileOperations`, `ITenantCache`, `IDistributedCache`, `IComposeTemplateSource`, `TokenCredential`,
`IOptions<DataverseOptions>`, `DocumentCheckoutService`, `ChatSessionManager`, `IConfiguration`.

Step 2 — registration conditionality:

| Service | Registration | Verdict |
|---|---|---|
| `IComposeService` | `ComposeModule.cs:23` | UNCONDITIONAL |
| `SpeSyncOrchestrator` | `ComposeModule.cs:61` | UNCONDITIONAL |
| `ISpeFileOperations` | `DocumentsModule.cs:43` | UNCONDITIONAL |
| `DocumentCheckoutService` | `DocumentsModule.cs:49` | UNCONDITIONAL |
| `ITenantCache` | `CacheModule.cs:195` | UNCONDITIONAL |
| `ChatSessionManager` | `AnalysisServicesModule.cs:397` | UNCONDITIONAL |
| `IDistributedCache` | `CacheModule.cs:212–223` | Conditional lines are a **decorator swap** (`MetricsDistributedCache`) over an existing registration, not a feature gate |
| `IComposeTemplateSource` | real impl in `AddDeliveryServices` (compound-AI-ON); **`NullComposeTemplateSource` peer** in `AddNullObjectsForCompoundOff`, called on **both** compound-OFF branches (`AnalysisServicesModule.cs:261,267`) | ADR-032 P3 null-object **already applied** — symmetric |
| `TokenCredential` | `Program.cs:46` | UNCONDITIONAL |
| `IOptions<DataverseOptions>` | `ConfigurationModule.cs:31` (`AddOptions<DataverseOptions>()`) | UNCONDITIONAL |

`app.MapComposeEndpoints()` (`EndpointMappingExtensions.cs:153`) and `AddComposeModule()`
(`Program.cs:212`) are both unconditional. **Result: no asymmetry found; no new ADR-032 application
needed.** This task adds no service, so it cannot introduce one.

---

## 5. ADR-010 — DI registration count

No `*Module.cs` file and no `Program.cs` line was touched (`git status` over
`Infrastructure/DI/` + `Program.cs` is empty). None of the nine changed/added source files contains a
`services.Add*` call. BFF-wide `services.Add*` / `services.TryAdd*` count: **666 → 666**. Extension
methods are not DI registrations.

---

## 6. Verification results

| Check | Before | After |
|---|---|---|
| BFF suite | 11391 passed / 0 failed / 97 skipped | **11393 passed / 0 failed / 97 skipped** (+2 = the new route-surface tests) |
| ArchTests | 62 / 62 | **62 / 62** |
| Publish (pwsh 7 zip) | 45.05 MB | **45.05 MB** (ceiling 60 MB) |
| Publish (shell-independent) | ~137.5 MiB raw | **144,161,261 bytes = 137.48 MiB · 215 files · 4 `.pdb`** |
| CVE | — | `dotnet list package --vulnerable --include-transitive`: **no vulnerable packages** |
| `dotnet format whitespace` (changed paths only) | — | exit 0 on both the BFF and test projects |
| Line endings | CRLF | CRLF on all 9 source files + the new test (`.gitattributes` `*.cs text eol=crlf`) |

No new NuGet reference.

---

## 7. Line counts — an **observation**, not the target

| File | Lines |
|---|---|
| `ComposeSaveEndpoints.cs` | 677 |
| `ComposeAnnotationEndpoints.cs` | 469 |
| `ComposeMountEndpoints.cs` | 445 |
| `ComposeDocumentEndpoints.cs` | 434 |
| `ComposeActiveDocumentEndpoints.cs` | 342 |
| `ComposeSyncEndpoints.cs` | 305 |
| `ComposeTemplateEndpoints.cs` | 244 |
| `ComposeCheckoutEndpoints.cs` | 132 |
| `ComposeEndpoints.cs` (aggregator) | 130 |
| **total** | **3,178** (was 2,932 in one file; +246 = 9 file headers, usings, class/method scaffolding) |

`Api/ComposeEndpoints.cs` no longer appears in `scripts/report-large-server-files.ps1` (≥2,000 LOC).

**Judgement on the remainder.** `ComposeSaveEndpoints.cs` (677) is the largest and is deliberately left
whole: `Save`, `CreateOnSave` and `ExecuteSaveAsync` are one cohesive unit by design — the two routes
exist only to shape two request bodies into the **same** `SaveComposeDocumentRequest` and funnel into the
one `ExecuteSaveAsync`, whose ~290 lines are a single exhaustive exception→outcome mapping. Splitting the
routes from the shared executor is exactly the divergence the file's own comment warns against ("on the
ONE path both save routes share so the replace and create-on-save routes can never diverge on it"), and
splitting the exception mapping from the telemetry it records would separate two things that must change
together. It is a large *cohesive* file, which root §11.5 explicitly permits.

---

## 8. Found along the way / could not verify

- **The POML's own `<relevant-files>` and `<outputs>` name `tests/Spaarke.ArchTests/GodClassGuardTests.cs`**,
  which does not exist. Sibling Track-D tasks 070/071/072/074 and `090-wrapup.poml`, plus `spec.md`,
  `plan.md`, `design.md` and this project's `CLAUDE.md` ("five Compose files are frozen … delete each
  waiver"), all still describe the retired ratchet. Only the pattern file
  `.claude/patterns/testing/god-class-ratchet.md` has been updated (it is correctly marked RETIRED).
  Those project docs are outside this task's edit scope and are left for the wrap-up task; they are
  recorded here so the next Track-D task does not re-derive the same finding.
- **Conflict check** — the `/conflict-check` skill was not invoked; an equivalent manual check was run
  instead (the main session owns git and PR mechanics). Result: **no overlap**. Of the 22 open PRs, none
  touches `Api/Compose*`. Against the integration branch `origin/work/spaarkeai-compose-r8`, the commits
  since `merge-base 32b2d3eb` change only `projects/spaarkeai-compose-r8/notes/patch-engine-retirement.md`
  and `projects/spaarkeai-compose-r8/tasks/TASK-INDEX.md` — zero commits touch
  `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` or any file this task adds.
- **Task 059's tenant work is intact**: exactly 4 `TenantResolution.ResolveTenantId` call sites survive
  the move (Upload, Load, GetAnnotations, RegisterActiveDocument), and the tripwire shapes
  `[FromQuery … tenantId` and `Headers[…Tenant…]` appear **nowhere** in the nine files.
- `ComposeShadowPatchEngine.cs` (owned concurrently by task 074) was **not touched**.
