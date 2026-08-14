# Graph 6.x / Kiota 2.0 Break Assessment — decision-grade sizing

> **Created**: 2026-08-11 · **Mode**: READ-ONLY assessment (no source modified).
> **Question answered**: Is migrating the BFF from `Microsoft.Graph 5.101.0 → 6.5.0` (+ `Microsoft.Kiota.* 1.22.0 → 2.0.0`) small/mechanical enough to **fold into the in-flight .NET 8→10 upgrade**, or deep enough to **keep as a separate future project**?
> **Companion memo**: [`kiota-cve-finding.md`](kiota-cve-finding.md) (the CVE is already closed on Graph 5.x/Kiota 1.22 — this doc is strictly about the *optional* Graph-6/Kiota-2 modernization, which stays deferred per design §6.4 unless this sizing says otherwise).

---

## Bottom line (read this first)

**BATCH-FRIENDLY — the break is genuinely mechanical, not deep.** Recommendation: this is *safe to fold into the net10 project* as an isolated, clearly-scoped sub-task **IF the owner wants to retire the 7 Kiota pins now**; otherwise it is equally safe to defer with no accruing risk. It is **not** a "defer-warranting" deep break.

**Why it's small**: the genuinely-breaking Graph transition — the **v4→v5 Kiota rewrite** (`ServiceException`→`ODataError`, `Microsoft.Graph.Models` namespace, Kiota request-builders, generated request-body types) — **has already been fully absorbed by this codebase.** Every Graph call site here is already written in the v5/Kiota idiom. The remaining **v5→v6 step is a comparatively minor major**: `Microsoft.Graph.Core 3.x→4.x`, drop net5 / add net10, and a **transitive** Kiota 1→2 bump. Critically, **this codebase does not use a single one of the Kiota 2.0 broken APIs directly** (see §2).

**The only real swing factor** is whether `Microsoft.Graph.ServiceException` is retained in Graph.Core 4.x (§4). Evidence says **yes, retained** → **~1 file of forced change** (the csproj) + full re-smoke. Worst case (if removed) → **~6 files of mechanical dead-code deletion** (72 catch blocks that are already dead code today). Either way: no logic rewrite, no serialization rewrite, no auth rewrite.

| Scenario | Forced source changes | Churn class |
|---|---|---|
| ServiceException **retained** in v6 (expected) | **1** csproj + 1 module doc | trivial / mechanical |
| ServiceException **removed** in v6 (contingency) | + ~6 files, ~72 dead catch blocks deleted | mechanical, higher volume |

---

## 1. Graph SDK call-site inventory (v5→v6 impact)

The v5→v6 delta touches **models, request-builders, and paging = NONE / no-change** (v6 does not reorganize the generated surface the way v4→v5 did — the 6.0.0 changelog lists only Graph.Core 4.x + net5-drop as breaking). Exception handling is the only surface with any exposure, and only via the `ServiceException` contingency.

| File | Graph API / pattern used | v5→v6 impact | Why |
|---|---|---|---|
| `Infrastructure/Graph/GraphClientFactory.cs` | Constructs `GraphServiceClient(httpClient, authProvider, baseUrl)`; OBO/MI/ClientSecret credentials; `AzureIdentityAuthenticationProvider` | **none** | ctor overload + auth-provider type unchanged in v6 |
| `Infrastructure/Graph/CiamGraphClientFactory.cs` | Same ctor; cert-based app-only; `AzureIdentityAuthenticationProvider` | **none** | same |
| `Infrastructure/Graph/DriveItemOperations.cs` | `Drives[].Items[]` (Get/Patch/Delete/Children/Content/Versions), `Storage.FileStorage.Containers[].Drive`, `.Preview`/`.CreateLink` request bodies, `$select`/`$filter`/`$orderby` via `requestConfiguration` | **mechanical (contingency only)** | **40 `catch (ServiceException)` sites** — retained→no change; removed→delete (already dead code, §4) |
| `Infrastructure/Graph/UploadSessionManager.cs` | `Drives[].Root.ItemWithPath().Content.PutAsync`, `.CreateUploadSession.PostAsync` (`CreateUploadSessionPostRequestBody`, `DriveItemUploadableProperties`), raw-HTTP chunk PUT | **none (primary) / mechanical (contingency)** | ODataError catches are primary + correct; 17 `ServiceException` catches are explicitly labelled belt-and-suspenders dead code |
| `Infrastructure/Graph/ContainerOperations.cs` | `Storage.FileStorage.Containers` Post/Get, `[id].Drive.GetAsync` | **mechanical (contingency)** | 9 `catch (ServiceException)` sites |
| `Infrastructure/Graph/SpeFileStore.cs` | `Subscriptions` CRUD; `Drives[].Items["root"].Delta.GetAsDeltaGetResponseAsync` + `.WithUrl(deltaLink)` delta paging | **none** | delta/`WithUrl` idiom unchanged in v6 |
| `Infrastructure/Graph/SpeAdminGraphService.cs` (4.9k LOC — heaviest) | `Storage.FileStorage.Containers/DeletedContainers/Columns/Permissions`, `Search.Query.PostAsQueryPostResponseAsync`, `Drives[]` items/versions/thumbnails, manual `OdataNextLink`/`$skiptoken` paging in `ExecuteWithRetryAsync` | **none** (70 `ODataError` catches) **+ 1 mechanical** | 1 stray `ServiceException` catch; direct Kiota surface here is §2 |
| `Services/SpeAdmin/BulkOperationService.cs` | delegates to SpeAdminGraphService; `catch (ODataError)` ×2 | **none** | ODataError shape stable |
| `Services/Registration/GraphUserService.cs` | `Users` Post/Get/Patch, `Users[].AssignLicense.PostAsync`, `Groups[].Members.Ref` (`ReferenceCreate`, `AssignLicensePostRequestBody`); `catch (ODataError)` ×2 | **none** | v6 keeps these builders |
| `Services/Communication/GraphSubscriptionManager.cs` | `Subscriptions` Post/Get/Patch/Delete (`Subscription` model); `catch (ODataError)` ×3 | **none** | |
| `Services/Communication/MailboxVerificationService.cs` | `Users[].SendMail.PostAsync` (`SendMailPostRequestBody`), `Users[].Messages.GetAsync`; `catch (ODataError)` ×2 | **none** | |
| `Services/Communication/IncomingCommunicationProcessor.cs` | `Subscriptions[]`, `Users[].Messages[]` Get (`$expand=attachments`)/Patch, `.Attachments.GetAsync` (`Message`/`Attachment`/`FileAttachment`); `catch (ODataError)` | **none** | |
| `Services/Communication/Channels/EmailChannelSender.cs` | `Users[].SendMail` + `Me.SendMail` PostAsync, `Me/Users[].MailFolders["sentitems"].Messages`; `catch (ODataError)` | **none** | |
| `Services/Ai/Nodes/SendEmailNodeExecutor.cs` | `Me.SendMail.PostAsync` (`SendMailPostRequestBody`) | **mechanical (contingency)** | **1 `catch (ServiceException)`** |
| `Services/Ai/Security/PrivilegeGroupResolver.cs` | **`PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>.CreatePageIterator(...).IterateAsync(ct)`** | **none** | PageIterator API (Graph.Core) is stable v5→v6 |
| `Services/Jobs/Handlers/IncomingCommunicationJobHandler.cs` | **string/type-name sniff**: `ex.GetType().Name.Contains("ODataError")` | **none (fragile)** | keeps working; flagged as brittle regardless of migration |
| `Infrastructure/ExternalAccess/SpeContainerMembershipService.cs` | (SPE membership; 4 `catch (ServiceException)`) | **mechanical (contingency)** | 4 dead `ServiceException` catches |
| `Infrastructure/Errors/ProblemDetailsHelper.cs` | `FromGraphException(ODataError)`; reads `ResponseStatusCode`, `Error.Code/Message`, **iterates `ResponseHeaders`** | **none (verify)** | `ODataError.ResponseHeaders` shape (`IDict<string,IEnumerable<string>>`) — verify at build |
| `Api/Office/Filters/OfficeExceptionFilter.cs` | `catch (ODataError)`, iterates `ResponseHeaders` for request-id | **none (verify)** | same |
| `Api/Office/Errors/OfficeProblemDetailsExtensions.cs` | `FromGraphException(ODataError)`, iterates `ResponseHeaders` | **none (verify)** | same |
| `Api/FileAccessEndpoints.cs` | 1 `catch (ODataError)` | **none** | |

**Paging**: no framework churn — the code uses `PageIterator` in exactly one place (`PrivilegeGroupResolver`, stable API) and otherwise hand-rolls `OdataNextLink`/`OdataDeltaLink` + `.WithUrl(...)` loops (idiom unchanged in v6).
**Batch**: **zero** `BatchRequestContent` / `BatchRequestContentCollection` usage anywhere in the repo — the `BatchRequestContentCollection` v6 change is **not applicable**.
**Upload sessions**: custom (raw-HTTP chunk PUT + Kiota `CreateUploadSession.PostAsync`), no `LargeFileUploadTask` — no exposure to any v6 upload-task change.

---

## 2. Direct Kiota usage inventory (v1→v2 impact) — the "we use Kiota often" surface

This is the part the owner flagged. The finding: the direct Kiota usage is **real but entirely on the stable side of the Kiota 2.0 break.** Kiota 2.0's breaking changes are: `IAsyncParseNodeFactory` removed; synchronous `KiotaSerializer` deserialize removed; `MultipartBody.GetPartValue(string)`/`RemovePart(string)` 1-arg overloads removed; parse-node/serialization **factory registration** changes; net5/net6 dropped. **This codebase uses none of those five.**

| File | Kiota abstraction used directly | v1→v2 impact | Why |
|---|---|---|---|
| `Infrastructure/Graph/GraphClientFactory.cs` | `Microsoft.Kiota.Abstractions.Authentication` (import); `Microsoft.Kiota.Authentication.Azure.AzureIdentityAuthenticationProvider(credential, scopes)` (×3) | **none** | provider ctor unchanged in Kiota.Authentication.Azure 2.0 |
| `Infrastructure/Graph/CiamGraphClientFactory.cs` | `AzureIdentityAuthenticationProvider(SimpleTokenCredential, scopes)` | **none** | same |
| `Infrastructure/Graph/SpeAdminGraphService.cs` | `AzureIdentityAuthenticationProvider`; **`BaseBearerTokenAuthenticationProvider`**; **custom `IAccessTokenProvider` (`StaticBearerTokenProvider`)** implementing `GetAuthorizationTokenAsync(Uri, Dictionary<string,object>?, CancellationToken)` + `AllowedHostsValidator`; **`UntypedObject`/`UntypedString`/`UntypedBoolean`.GetValue()** | **none (verify custom provider at build)** | `IAccessTokenProvider` signature + `BaseBearerTokenAuthenticationProvider` + `AllowedHostsValidator` are **not** in the 2.0 break list; `UntypedNode` model + `GetValue()` stable since Kiota ~1.7 |
| (7 direct `Microsoft.Kiota.*` package pins in `Sprk.Bff.Api.csproj`) | version-alignment pins, **not** API consumption | **deletable** | they exist only to float the transitive graph to 1.22.0 for the CVE; under Graph 6.5.0 the transitive Kiota is 2.0.0 and all 7 pins can be deleted |

**Custom `IAuthenticationProvider`?** None — the codebase composes the *library* `AzureIdentityAuthenticationProvider` / `BaseBearerTokenAuthenticationProvider`; the only custom type is a trivial `IAccessTokenProvider` (returns a static token). Signature unchanged in 2.0.
**Custom `DelegatingHandler` / Kiota middleware?** `GraphHttpMessageHandler` is a plain `System.Net.Http.DelegatingHandler` attached to the **named `HttpClient`** via `IHttpClientFactory` (`.AddHttpMessageHandler<>()` in `GraphModule.cs`) and that HttpClient is passed to the `GraphServiceClient` ctor. It is **not** registered into a Kiota `IRequestAdapter` middleware pipeline → **framework-agnostic, zero Kiota-2 exposure.**
**Custom `IRequestAdapter`?** None. **`RequestInformation` hand-built?** None. **`IParseNode`/`ISerializationWriter`/factory registration?** None. **Synchronous `KiotaSerializer` / `MultipartBody`?** None (every `Deserialize` hit in the repo is `System.Text.Json`/`Newtonsoft`, not Kiota).

**Conclusion for §2**: "we use Kiota often" is true in the sense of *auth-provider composition + one custom token provider + untyped-node reads + one PageIterator* — but **every one of those is a Kiota-2.0-stable API.** The exhaustive search for the five Kiota-2.0-broken APIs returned **zero hits.**

---

## 3. Totals & bucketed churn

- **Graph call-site files**: ~20 source files use the Graph SDK surface (models/request-builders/exceptions).
- **Direct-Kiota files**: 3 (`GraphClientFactory`, `CiamGraphClientFactory`, `SpeAdminGraphService`) + 1 DI wiring (`GraphModule`, HttpClient only). SpeAdminGraphService is the only file with non-trivial direct Kiota surface.
- **Exception-catch census**: `catch (ODataError)` = **101 sites / 12 files** (all no-change); `catch (ServiceException)` = **72 sites / 6 files** (all dead-code / belt-and-suspenders today).

**Bucketed estimate (files requiring a real change):**

| Bucket | Count | Items |
|---|---|---|
| **Mechanical (forced)** | **1–2 files** | `Sprk.Bff.Api.csproj` (Graph `5.101.0→6.5.0`, delete 7 Kiota pins); `Sprk.Bff.Api/CLAUDE.md` "Package Management" section (doc). `Directory.Build.props` NU1903 delete is already a net10 task (004). |
| **Mechanical (contingency — only if ServiceException removed in v6)** | **+6 files** | delete ~72 dead `ServiceException` catches in `DriveItemOperations`, `UploadSessionManager`, `ContainerOperations`, `SpeContainerMembershipService`, `SpeAdminGraphService`, `SendEmailNodeExecutor` |
| **Moderate** | **0** | — |
| **Deep** | **0** | — |

Everything else (101 ODataError sites, all request-builders, models, delta/`WithUrl` paging, `PageIterator`, upload sessions, auth-provider construction, `UntypedNode` reads, custom `IAccessTokenProvider`) is **no-change / verify-at-build**.

---

## 4. The one swing factor — does `ServiceException` survive into Graph.Core 4.x?

`DriveItemOperations.cs` catches `ServiceException` **exclusively** (40 sites), and 5 other files add ~32 more. If v6 **removed** the type, all 72 sites become compile errors.

**Evidence it is RETAINED (expected):**
- The v6 (`Microsoft.Graph 6.0.0`) changelog lists **only** two breaking changes: dependency bump to `Microsoft.Graph.Core 4.x`, and drop of net5.0. It does **not** mention removing `ServiceException`.
- The `Microsoft.Graph.Core 4.0.0` changelog lists **only** the internal `IAsyncParseNodeFactory` removal (Kiota 2.0). No `ServiceException` removal.
- `ServiceException` (namespace `Microsoft.Graph`, assembly `Microsoft.Graph.Core.dll`) survived the far larger v4→v5 Kiota rewrite as a legacy compat type and is still documented under `graph-core-dotnet`.

**Could NOT determine with 100% certainty**: a definitive v6-era API reference for `ServiceException`'s exact member set (`ResponseStatusCode` int, `ResponseHeaders` with `.RetryAfter`) — the Microsoft Learn page is stale (2020, Graph.Core v1.22 shape showing `StatusCode`/`HttpResponseHeaders`, which is **older** than the `ResponseStatusCode`/`ResponseHeaders` the current code already compiles against on 5.101). **→ Verify at build.** Risk is low and, even in the worst case, mechanical.

**Important latent-bug note (pre-existing, out of scope):** because Kiota throws `ODataError` (not `ServiceException`), the `DriveItemOperations` `ServiceException` catches are **already dead code today on v5** — a Graph 404/403/429 in that file currently falls through to `catch (Exception ex) { throw; }` and surfaces as a raw/opaque error, unlike `UploadSessionManager` which was fixed (DEF-14) to catch `ODataError`. This is a **current** defect independent of the migration; the migration would be a natural moment to fix it (convert the dead `ServiceException` catches to `ODataError`), but it is **not required** by the version bump.

---

## 5. net10-specific coupling

**Low / essentially none — but a mild alignment tailwind.** Per the CVE memo, both stacks build on net8 *and* net10:

| Package | TFMs | Runs on net10? |
|---|---|---|
| `Microsoft.Graph 5.101` → Core 3.x / Kiota 1.22 | netstd2.0/2.1, net6, net8 | Yes (net8 assembly on net10 runtime — supported, not "native") |
| `Microsoft.Graph 6.5.0` → Core 4.0.1 / Kiota 2.0 | netstd2.0/2.1, **net8, net10** | Yes (net10-native assemblies) |

- **No hard net10 gate** in either direction — Graph 5.x/Kiota 1.22 run fine on net10 via net8-compat.
- **Mild synergy** (a *pro-fold* nudge, not a requirement): Graph 6.5 / Kiota 2.0 are the versions that **natively target net10**; folding the bump into the net10 move means your Graph assemblies target the same TFM as the app, rather than running net8-targeted Graph assemblies on the net10 runtime. This is a cleanliness win, not a correctness one.
- **No genuine code-level coupling** between the net10 move and the Graph-6/Kiota-2 API surface was found.

---

## 6. Regression surface (what to re-smoke if Graph/Kiota moves)

Source is (almost) unchanged, but the **entire Graph HTTP path** changes assemblies, so behavior-level re-smoke is warranted (serialization, CAE, backing-store defaults could shift subtly even with identical source):

| Area | Paths / files | Re-smoke |
|---|---|---|
| **Auth — OBO (delegated)** | `GraphClientFactory.CreateOnBehalfOfClientAsync` + Redis token cache + `AzureIdentityAuthenticationProvider` | user opens/downloads a doc; OBO exchange + cached-token path |
| **Auth — Managed Identity (app-only)** | `GraphClientFactory.CreateAppOnlyClient` (`DefaultAzureCredential`) | background container ops, indexing |
| **Auth — CIAM app-only cert** | `CiamGraphClientFactory` (`AcquireTokenForClient` + `AzureIdentityAuthenticationProvider`) | external-user provisioning (`POST /users`) |
| **Auth — SpeAdmin OBO bearer** | `SpeAdminGraphService` `BaseBearerTokenAuthenticationProvider` + custom `IAccessTokenProvider` | multi-app SPE admin ops |
| **Auth — named API-key schemes** | (BuilderAdmin/Rag) | **no Graph involvement → no re-test needed** |
| **SPE file ops** | `DriveItemOperations` (list/download/upload/delete/metadata/versions/preview/sharing-link), `UploadSessionManager` (small + chunked + replace-content + If-Match), `ContainerOperations`, `SpeFileStore` (delta + subscriptions) | full SPE CRUD + large-file chunked upload + delta sync + Compose save (412/423 typed errors) |
| **Mail / communication** | `EmailChannelSender` (SendMail app-only + OBO), `MailboxVerificationService`, `IncomingCommunicationProcessor` (Messages/Attachments), `GraphSubscriptionManager`, `SendEmailNodeExecutor` | send/receive email, webhook subscription lifecycle, attachment materialization |
| **Background jobs** | `IncomingCommunicationJobHandler` (**type-name string sniff on "ODataError"** — verify still matches), `BulkOperationService` | inbound email job, bulk container ops |
| **User provisioning** | `GraphUserService` (Users/Groups/AssignLicense) | create user, assign license, group membership |
| **Error → ProblemDetails mappers** | `ProblemDetailsHelper`, `OfficeExceptionFilter`, `OfficeProblemDetailsExtensions` (iterate `ODataError.ResponseHeaders`) | confirm 403/404/429 still map to correct ProblemDetails + request-id header still extracted |

**Two behavior-level "verify" items** (not code changes): (a) `ODataError.ResponseHeaders` enumeration shape under Kiota 2.0; (b) whether v6 changes CAE (continuous-access-evaluation) or backing-store defaults in a way that alters long-lived-token / property-tracking behavior. Neither is a source edit; both are smoke-test observations.

---

## 7. Recommendation (honest)

**Fold-eligible, low-risk — but optional.** The Graph-6/Kiota-2 bump is a **mechanical, isolatable sub-task**, not a deep break, because the hard part (v4→v5 Kiota rewrite) is already done and **none of the Kiota-2.0-broken APIs are used here.** Concretely:

- **If the owner wants to retire the 7 Kiota pins + NoWarn now**: fold it in as **one dedicated task, executed *after* the net10 retarget is green**, sequenced so the Graph swap is its own commit/PR for clean bisect. Expected change set: **1 csproj + 1 doc**, plus a build to confirm the `ServiceException` question (§4) and the full Graph re-smoke matrix (§6). Budget a **contingency** for deleting ~72 dead `ServiceException` catches across 6 files if the type turns out removed — still mechanical.
- **If the owner prefers minimal blast radius on the net10 project**: **defer it** — there is **no accruing risk and no CVE pressure** (the CVE is already closed on 5.x/1.22 per the companion memo). It remains a clean, self-contained future project.

**What would make it "defer-warranting" (and does NOT apply here)**: heavy `BatchRequestContentCollection` use, custom `IRequestAdapter`/parse-node factories, `MultipartBody` 1-arg overloads, synchronous `KiotaSerializer`, or un-migrated v4-era `ServiceException`-as-primary error handling. **None are present.**

**Net**: batch it if you want the pins gone; defer it freely if you don't. Do **not** treat it as a deep/risky modernization — the evidence doesn't support that framing.

---

## 8. Sources

- Microsoft Graph .NET SDK v6.0.0 changelog (Graph.Core 4.x dependency + net5 drop as the only listed breaks): `https://github.com/microsoftgraph/msgraph-sdk-dotnet/blob/main/CHANGELOG.md`
- Microsoft.Graph.Core 4.0.0 changelog (Kiota 2.0 / IAsyncParseNodeFactory internal change): `https://github.com/microsoftgraph/msgraph-sdk-dotnet-core/blob/main/CHANGELOG.md`
- Kiota .NET 2.0.0 breaking changes (IAsyncParseNodeFactory removed, sync deserialize removed, `MultipartBody` `GetPartValue`/`RemovePart` overload change, net5/net6 dropped + net8/net10 added): `https://github.com/microsoft/kiota-dotnet/blob/main/CHANGELOG.md`
- Graph .NET SDK v5 upgrade guide (the v4→v5 Kiota model this codebase already fully adopted — `ODataError`, `Microsoft.Graph.Models`, request-builders): `https://github.com/microsoftgraph/msgraph-sdk-dotnet/blob/main/docs/upgrade-to-v5.md`
- `ServiceException` retention signal (still documented under `graph-core-dotnet`; note the Learn page itself is stale/2020): `https://learn.microsoft.com/en-us/dotnet/api/microsoft.graph.serviceexception?view=graph-core-dotnet`
- ODataError-vs-ServiceException behavior (Kiota SDK throws `ODataError`, confirming the dead-code status of the `ServiceException` catches): `https://github.com/microsoftgraph/msgraph-sdk-dotnet/issues/2580`, `.../issues/2903`
- Companion CVE memo: [`kiota-cve-finding.md`](kiota-cve-finding.md)

> **Flags / could-not-determine**: (1) exact v6-era `ServiceException` member set — verify at build (§4, low risk). (2) `ODataError.ResponseHeaders` enumeration shape + CAE/backing-store defaults under Kiota 2.0 — behavior-level smoke items, not code (§6). No dedicated `.NET`-specific "upgrade-to-v6" doc exists (v6 was minor for .NET); sizing is triangulated from the three changelogs above + the absorbed v5 upgrade guide + this repo's actual call sites.
</content>
</invoke>
