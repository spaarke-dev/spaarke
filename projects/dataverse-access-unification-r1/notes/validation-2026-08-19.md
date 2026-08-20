# Validation & Re-Assessment — 2026-08-19

> **Trigger**: incorporation of [`auth-v4-coordination-memo.md`](auth-v4-coordination-memo.md) into `design.md`,
> followed by an operator-requested design validation.
> **Method**: three independent Fable-model review passes (scope/justification · feasibility-against-code ·
> factual audit), each read-only, plus direct verification in the main session of every load-bearing finding.
> **Outcome**: project **PAUSED** (open, not archived). Three residual items extracted. Resume triggers named.

---

## 1. Why this document exists

`design.md` was revised on 2026-08-19 to absorb the auth-v4 coordination memo. The validation pass that followed
found that the design — and the RED-4 assessment behind it — was drawn from a **map that the interim hardening
had already invalidated**, and that one scope item was outright wrong. The corrected picture changes the
project's cost/benefit enough to change the decision. This is the record of that investigation.

## 2. The critical finding: `DataverseWebApiClient` cannot be deleted here

All three review passes independently flagged it; verified directly in the main session.

- **45 references across 16 consumer files.** Registered singleton in `SpeAdminModule.cs:56` (**not**
  `GraphModule`), injected across all 4 `Api/SpeAdmin/*Endpoints.cs`, all 6 `Api/ExternalAccess/*Endpoint.cs`,
  `SpeAdminGraphService.cs` (the 4,911-LOC RED-1 frozen god class), `SpeAuditService`, `SpeDashboardSyncService`,
  `RegistrationDataverseService`, `ScopeManagementService`.
- It is **not** part of the `IDataverseService` family, and `DataverseWebApiService` never consumes it. It is an
  independent REST stack with surface the REST service never had (`QueryAsync<T>`, `AssociateAsync`,
  `DisassociateAsync`, `DeleteAsync` — `DataverseWebApiClient.cs:118-231`).

**Consequences**: Phase 3 as originally written would have broken the build; the coordination memo's
"T3's scope halves" claim is **withdrawn** (T3 keeps both sites); and the error's origin — nobody grepped the
deletion target's consumers before scoping — means the project's cost estimate was unreliable in the **upward**
direction.

## 3. The stack inventory (the "two-stack" framing is wrong)

RED-4 is titled *dataverse-**two**-stack*. There are at least **five** Dataverse access stacks at head:

| # | Stack | Status under this project |
|---|---|---|
| 1 | `DataverseServiceClientImpl` — SDK | survivor |
| 2 | `DataverseWebApiService` — REST | **the only one retired** |
| 3 | `DataverseWebApiClient` — REST (SpeAdmin + ExternalAccess) | untouched (non-goal, §2) |
| 4 | `RegistrationDataverseService` — hand-rolled REST + own token refresh, *"same pattern as `DataverseWebApiClient`"* (`RegistrationDataverseService.cs:93`) — a **clone, not a consumer** | untouched |
| 5 | `DataverseAccessDataSource` — own HTTP + MSAL | untouched (auth-v4's) |

(Plus the `Services/Ai` raw-HTTP camp RED-4 noted as separately migrated to MI in AUTHV2-042 Phase C.)

At full success the project delivers **one `IDataverseService` implementation**, not "one Dataverse access
layer" — and the stack it retires is the one the hardening already fenced.

## 4. Re-audit of the surviving justifications

`design.md` claimed three concrete failures persist if we do nothing. Re-checked against code:

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| a | `UpdateRecordFieldsAsync` has **two live impls** selected by which alias the consumer injects | **INTACT — the only fully surviving justification** | Both live: `DataverseServiceClientImpl.cs:1918` + `DataverseWebApiService.cs:1109`. Composite→SDK: `FinanceRollupService.cs:157`. `IFieldMappingDataverseService`→WebApi: `InvoiceReviewService.cs:296`, `ScorecardCalculatorService.cs:224`, `SignalEvaluationService.cs:226`, `DataverseUpdateHandler.cs:42/98`, `UpdateRecordActionCore.cs:135`. **Note**: hardening item B3 was supposed to collapse this ("ONE live impl") and did not |
| b | Narrow interfaces are hand-routed in DI; a mis-route fails at **runtime**, not compile time | **REAL BUT HEAVILY DEFUSED** | Hardening converted 7 silent-empty stubs → `NotImplementedException` (`DataverseServiceClientImpl.cs:1802-1916`). The catastrophic mode (silent wrong/empty data) became a loud, minutes-to-diagnose crash |
| c | The NFR-06 impersonation surface is reached by **concrete-class injection bypassing the interface layer** | **ESSENTIALLY FALSE** | Every consumer injects `IImpersonatedCommunicationQuery` (`CommunicationThreadReadService.cs:114/121`, `CommunicationQueueFeedService.cs:103/111`, `CommunicationAttachmentTextService.cs:49/71`); tests mock the interface (`CommunicationPrivilegePrivacySeamTests.cs:64` + 4 sibling suites). The concrete type appears **once**, in a stateless 5-line pass-through adapter (`IImpersonatedCommunicationQuery.cs:40-56`), registered at `CommunicationModule.cs:272`. That is the prescribed **ADR-010 "interface as testing seam only"** pattern — the code says so at `CommunicationModule.cs:265`. Same shape for `IDataverseAccessGrantService` |

**Three justifications: one intact, one defused, one false.** RED-4's own strongest pro-C argument — that the
impersonation surface "lives in a **majority-dead** class" — was additionally invalidated when hardening deleted
the dead code (−1,414 LOC).

## 5. The risk side (unchanged, and unusually sharp)

| Risk | Why it matters |
|---|---|
| **Fail-OPEN impersonated read** | If the ported `RetrieveMultipleImpersonatedAsync` relies on `Clone()`+`CallerId` and `ServiceClient` does not stamp `MSCRMCallerID` on the `ExecuteWebRequest` HTTP path, the query silently runs app-only and returns org-wide rows, no error, invisible to green-path tests. Highest-severity NFR-06 class |
| **Near-zero characterization baseline** | One live-gated method covered (`DataverseWebApiFieldMappingRegressionTests.cs`); everything else mocks the seams *above* the concrete; the events surface has **zero** behavioral tests on either impl. The safety net must be built before the risky work starts |
| `Clone()` under the MI token provider is empirically unproven | #3b went live 2026-08-17; the `Clone()` sites may not have run under `tokenProviderFunction` auth yet, and `ServiceClient.Clone()` has version-specific edge cases with externally-managed tokens |
| Sync-over-async on hot read paths | `ExecuteWebRequest` is synchronous (`Task.Run`, `DataverseServiceClientImpl.cs:1957`); moving events + Communication feed reads onto it converts async `HttpClient` reads into threadpool-blocking calls |
| Seam contract shape | The impersonated seam passes raw OData strings and returns `Dictionary<string, JsonElement>` rows incl. `@OData…FormattedValue` annotations; a QueryExpression rewrite forces a rewrite of the Communication read stack |
| Verification surface | Dev is the **sole live environment**; demo/prod are decommissioned |
| Contention | The most contended shared lib in the repo; ratchet already red at head |

## 6. Assessment

**High risk, medium-and-shrinking reward, delivering one-fifth of the stated unification.**

RED-4 classified this project (its option **C**) as **OPTIONAL** — *"only if the owner wants the single-impl
end-state; not required to remove the traps (B does that)."* Option B shipped, the traps are fenced, and the
fencing is holding. The hardening bought most of the available value at ~5% of the full-unification cost.

**Verdict: not necessary; as scoped, more risk than reward. PAUSED.**

This reverses the direction the 2026-08-19 memo-integration edit was leaning. The memo integration made the
project look better-grounded than the code supports — the memo is accurate about *auth*, but it inherited the
stale scope from `design.md` rather than re-deriving it.

## 7. Residual work extracted (do these on the hardening track, not as this project)

Roughly 10–15% of the project cost; none of it touches the impersonated read path.

1. ✅ **DONE 2026-08-20 — collapsed `UpdateRecordFieldsAsync` to one live impl** (finishes hardening item B3).
   `FinanceRollupService` — the sole composite-routed caller — now injects `IFieldMappingDataverseService`
   (it keeps `IDataverseService` for the FetchXML `UnwrapServiceClient` cast); the SDK impl's copy is a
   fail-loud stub matching its 7 siblings. WebApi is the single live impl.
   **It was not mechanical — it surfaced a live production defect.** See §7a.
2. **Write the impersonation characterization + negative-canary suite** — valuable independent of unification.
   `spaarke-auth-v4-dataverse-MI` is about to change the credential underneath `DataverseAccessDataSource`'s OBO
   path; this suite protects that work too, and it does not exist today. Canary: an impersonated low-privilege
   read MUST return strictly fewer rows than the app-only read of the same query.
3. **Resolve the god-class ratchet red** — `DataverseServiceClientImpl` 2,975 vs waiver 2,864 (+100 grace),
   over by 11, grown by #3b. (`ComposeEndpoints.cs` is over by 4.) **Owned by `code-quality-and-assurance-r3`**
   (operator direction, 2026-08-20) — not this project's to fix.
   *Note*: item 1's stub replaced ~60 LOC with ~30, so `DataverseServiceClientImpl` is now **2,945** — back
   under waiver+grace (2,964). That is incidental, not the fix; r3 still owns the re-baseline decision, and
   `ComposeEndpoints.cs` remains over.

## 7a. The defect the B3 fix surfaced (2026-08-20)

Re-routing `FinanceRollupService`'s write required reading its payload — which turned out to be **broken in
production, silently, for five months**.

- **What**: the payload wrapped its 5 currency fields in SDK Entity-model `Money` objects
  (`[Field_TotalSpendToDate] = new Money(totalSpend)`, etc.).
- **Why it fails**: `UpdateRecordFieldsAsync` PATCHes the dictionary to the Dataverse **Web API**, which takes
  currency as a bare number. Verified empirically — `JsonSerializer.Serialize(new Money(1234.56m))` produces
  `{"Value":1234.56,"ExtensionData":null}`, an **object**. Dataverse answers HTTP 400.
- **Since when**: `b7b0d4011` (2026-03-03) converted the impl from the SDK Entity model (where `Money` is
  correct) to OData PATCH (where it is not) and did not update this caller. **Every
  `RecalculateMatterAsync` / `RecalculateProjectAsync` has failed at the write since.**
- **Why nobody noticed**: `FinanceRollupService` had **zero tests** — it is effectively untestable as written
  because it needs a real `ServiceClient` for its FetchXML queries (`UnwrapServiceClient`). The endpoints'
  only test (`FinanceRollupEndpointsContractTests`) asserts the 401 auth boundary and never reaches the service.
- **Fix**: payload switched to primitives and extracted to `FinanceRollupService.BuildRollupFields` — a pure
  function testable with no Dataverse connection. Pinned by
  `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FinanceRollupPayloadTests.cs` (4 tests, including a negative
  control proving the guard is not vacuous).
- **Impersonation unaffected**: the live impersonated write (email-intelligence task 031 / FR-10) always ran
  `UpdateRecordActionCore` → `IFieldMappingDataverseService` → WebApi (`MSCRMCallerID` on the PATCH). The SDK
  impl's `Clone()`+`CallerId` branch was parity code with no caller; recoverable from git (`4aca6d65a`).

**This is evidence for the paused project's remaining thesis**, and worth weighing at the next re-evaluation:
the dual-impl arrangement did not merely risk drift — it **produced a silent five-month production failure**,
because a payload-contract change under one impl left the other impl's caller behind with nothing to catch it.
It cuts both ways, though: the defect was in a *caller*, the routing collapse that exposed it cost one
afternoon, and the two remaining stacks are now consistent. It strengthens the case for **finishing the cheap
consistency work** more than it strengthens the case for the full port.

**Still open**: `FinanceRollupService` remains untestable end-to-end (the FetchXML `UnwrapServiceClient`
dependency). Only the payload contract is now guarded. A live-dev smoke of one matter recalculate is warranted
before trusting the fix in production.

## 8. Resume triggers (re-evaluate when any fires)

The "piecemeal cleanup never graduates to the real fix" concern is legitimate, so the triggers are named rather
than left to resolve:

- **A capability genuinely needs porting across the SDK/REST boundary** — a forcing function, not a cleanup.
- **The SpeAdmin / ExternalAccess REST retirement gets scoped** — then unify all five stacks once, from a
  correct map, instead of one-fifth now.
- **A second mis-route reaches production despite the fail-loud stubs** — evidence the fencing is not holding.
- **The routing table grows past ~12 narrow interfaces** (9 today).
- **`spaarke-auth-v4-dataverse-MI` completes** — per the operator's 2026-08-19 direction, re-read this
  assessment then and check whether anything auth-v4 surfaced changes it (e.g. if its OBO migration ends up
  wanting the same characterization suite, or if `DataverseAccessDataSource` turns out to be entangled with the
  `IDataverseService` family after all).

## 9. Corrections owed to other documents

- **`notes/auth-v4-coordination-memo.md`** — §4 table and §6 assume `DataverseWebApiClient` is deleted by our
  Phase 3, and conclude T3's scope halves. Both withdrawn. Auth-v4 should be notified; everything else in the
  memo verified clean (all 12 spot-checked file:line citations exact).
- **`design.md` (fixed 2026-08-19)** — the impersonation citation `DataverseServiceClientImpl.cs:1875-1884`
  pointed at the RED-4 field-mapping **throw-stubs**, i.e. the opposite of the claim. Correct evidence:
  `:1944-1949` inside the impersonated-PATCH block `:1940-1963`.
- **`README.md` (fixed 2026-08-19)** — "~5,686 LOC combined" (actual 4,443), "both god-class waivers" (one),
  "majority-dead class" (dead code deleted), prerequisites phrased as pending (both satisfied).
- **`docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md`** — its auth column still says "ClientSecret —
  ADR-028 violation, #3b" for both impls; #3b landed 2026-08-17 and both are now MI-first. Also attributes the
  impersonation/POA registrations to `GraphModule.cs:44-82`; they are in `CommunicationModule.cs`
  (~`:272`, ~`:648`). Worth a small doc-drift PR **independent of this project's status**.
