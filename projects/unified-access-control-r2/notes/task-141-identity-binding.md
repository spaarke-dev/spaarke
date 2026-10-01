# Task 141 — workforce identity binding by Entra oid, and the systemuser↔contact link

> **Status**: implemented on branch `task/uac-r2-141` (2026-10-01). Live steps are **pending manual gates** —
> this run was read-only against Dataverse/Entra by instruction (§8 lists every gate with its exact command).
> **Owner answers in force**: round 2 item 4, Q3; round 3 I1 = (b), I2 = (1), T2; trigger (a) for licensed
> collision rows. Source: `session27-owner-decisions-and-research.md`.
> **Peer contract**: [`141-link-contract.md`](141-link-contract.md) (word-add-in-r1 task 083 waits on it).
> **Provisioning handoff**: [`handoffs/INCOMING-141-workforce-tenant-list.md`](handoffs/INCOMING-141-workforce-tenant-list.md).

---

## 0. Baseline (step 0) — re-verified READ-ONLY 2026-09-30/10-01 against spaarkedev1

Every anchor in the POML background was re-read in source. Line numbers drifted after 131/134/151 merged
(e.g. `ResolveContactByEmailAsync` is now `ExternalParticipationService.cs:410-414`, not `:404-408`); the
substance of all ten background items held.

| Live fact | Result |
|---|---|
| `contact.azureactivedirectoryobjectid` exists? | **No** (metadata query returns 0 attributes) — background item 1 confirmed |
| `contact.sprk_externalobjectid` | Text(100), `IsSecured = false`, display name "External Object ID (CIAM oid)" |
| `systemuser.sprk_primarycontact` | Lookup, `IsSecured = false`; navigation property `sprk_PrimaryContact` |
| Alternate keys on `contact` | **None** — so adding one relaxes nothing (escalation trigger 5 does not fire) |
| Contacts carrying `sprk_externalobjectid` | **6**, all well-formed lowercase "D" GUIDs — **no normalisation needed** (step 2a) |
| `fieldpermission` rows for the two fields | none (fields are not secured) |

**Active interactive systemusers** (`isdisabled = false`, `applicationid = null`):

| systemuser | accessmode | link | email-matched active contact | class |
|---|---|---|---|---|
| ralph.schroeder@spaarke.com (oid c74ac1af) | 0 | → 8e9918a9 | 8e9918a9 bound to **6a9fa229** (CIAM) | **collision, existing link kept** |
| eyal.iffergan@spaarke.com (33b58f6c) | 0 | — | 8cb95c16 bound to **6b917a49** | **collision** |
| ralph.schroeder@hotmail.com (ad268fcd, `#EXT#` **guest**) | 0 | — | 2e419a4f bound to **06646385** | **collision** (guest + email match) |
| testuser1@spaarke.com (bcde7809) | 0 | — | ac6d7b68 **unbound** | **bind candidate** |
| final.test / demo / jake.schroeder / e2e.test2 @demo.spaarke.com | 0 | — | none | **create** |
| chelsea.friez@demo.spaarke.com, lori.witkin@demo.spaarke.com | 0 | — | none | **create** (new since the POML was written) |
| ralph.schroeder@spaarke.onmicrosoft.com (66693a9e) | **1** Administrative | — | none | **create** (see note) |

- POML baseline (accessmode 0, 8 users): 1 linked / 3 collisions / 1 bind / 4 create. **Today (accessmode 0, 10
  users): 1 linked (and colliding) / 3 collisions / 1 bind / 6 create.** With the job's interactive filter
  (accessmode 0, 1, 2 — see §4) it is 11 users and 7 creates.
- `test.user@demo.spaarke.com` (the T2 live identity): no systemuser, no contact — a correct Type-2 test.
- Two pseudo users (Support User = 3, Delegated Admin = 5) exist and are excluded by the filter.

## 1. The `acct` claim — read-only check (escalation trigger 3)

`az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c` (dev BFF registration "SDAP-BFF-SPE-API"):

- `signInAudience = AzureADMultipleOrgs`, `requestedAccessTokenVersion = null` (v1 access tokens)
- access-token optional claims today: `email`, `preferred_username`, `upn` — **`acct` is NOT configured**
- the Teams app package (`src/client/external-spa/appPackage/manifest.json`) declares
  `webApplicationInfo.id = 1e40baad…`, i.e. **Teams SSO tokens are access tokens issued FOR this registration**

Microsoft documentation:

- `acct` is an optional claim for JWT (v1.0 and v2.0) — *"If the user is a member of the tenant, the value is
  `0`. If they're a guest, the value is `1`."* ([optional claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference))
- *"When you add claims to the access token, the claims apply to access tokens requested for the application
  (a web API) … No matter how the client accesses your API, the right data is present in the access token"*
  (same page) — so it covers Teams SSO, which obtains an access token for the app's own registration
  ([Teams tab SSO](https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/authentication/tab-sso-overview)).
- Optional claims *"hang off of the application registration object … consistent across every tenant"*, and
  Microsoft's own guidance for blocking guests is to opt in to `acct` and refuse `acct == 1`
  ([Customize Microsoft Entra tokens](https://learn.microsoft.com/en-us/security/zero-trust/develop/zero-trust-token-customization)).

**Verdict: the trigger does not fire.** `acct` can be issued on this registration's tokens, including Teams
SSO. It is simply not configured yet, and adding it is an app-registration change this run was told not to
make — so it is a **pending manual gate (G-2)**. Until it lands, the member test fails closed: a Type-2 caller
gets `sdap.access.deny.workforce_acct_claim_missing` (no email bind, no creation), which is the documented
safe state, not a regression (today every Type-2 caller is denied `principal_not_resolved`). Domain/UPN
inference was **not** added (forbidden).

## 2. The customer-workforce-tenant source (escalation trigger 2) — owner I1 = (b)

A NEW setting, `WorkforceIdentity:CustomerTenantIds` (string array of tenant GUIDs). **Empty or absent = DENY**
every email bind and every creation. It never falls back to `AzureAd:TenantId` and is not
`TenantRouting:Tenants[]`. Malformed entries (non-GUID, all-zero) and the CIAM tenant id are refused at
startup by `WorkforceIdentityOptionsValidator` (`ValidateOnStart`, ADR-010). Provisioning writes it — handoff
in `handoffs/INCOMING-141-workforce-tenant-list.md`.

## 3. The decision table (written before coding — step 1)

One pure decision, `ContactBindingDecision` (public, so no `InternalsVisibleTo`), shared by every plane. Each
plane passes its own facts. Inputs: the plane; the caller oid (parsed Guid); the eligibility (§3.1); the oid
lookup (three-state: rows, or could-not-read, or column missing); the email lookup (ACTIVE rows, top 2); the
references (systemusers whose `sprk_primarycontact` points at the candidate).

### 3.1 Eligibility

| Plane | Eligible for email bind | Eligible for create |
|---|---|---|
| Workforce token | only a **member**: `CallerIdentity.FromPrincipal` = UserDelegated AND `tid` ∈ `CustomerTenantIds` AND `acct` = 0 | same member only |
| CIAM token (`tid` = `Ciam:TenantId`) | yes — the invite-repair path | **never** |
| Systemuser (no token) | non-guest (`domainname` has no `#EXT#`) | yes; a guest only when no active contact carries its email |

Member-test denials each carry their own code: `workforce_app_only_token` (Application / Indeterminate, incl.
a token with no `idtyp`), `workforce_tenant_list_empty`, `workforce_tenant_not_customer`,
`workforce_acct_claim_missing`, `workforce_guest`, `workforce_acct_unrecognized`. A non-member still resolves
through an EXISTING oid binding — the member test gates only the email bind and creation.

### 3.2 Binding state of a row (pure `ReadBinding`)

| `sprk_externalobjectid` | `sprk_identityplane` | State |
|---|---|---|
| null | null | **Unbound** |
| a non-zero GUID | External / Workforce | **Bound** (oid, plane) |
| a non-zero GUID | null | **Bound**, plane = External (every pre-141 writer was CIAM) |
| null | set | **Unreadable** — a masked secured column (FLS) or an orphaned marker; never "unbound" |
| malformed / all-zero, or an unknown plane value | any | **Unreadable** |

The middle-but-one row is the masking defence: once the oid column is field-secured, a reader without FLS Read
gets it back as null — and Dataverse substitutes null in FILTERS too
([column-level security with code](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/column-level-security)).
The plane marker is not secured, so "marker present, oid absent" is detectable and denies.

### 3.3 Unlinked flow (Workforce token, CIAM token, unlinked systemuser)

| # | Condition | Outcome | Code / flag |
|---|---|---|---|
| D0 | caller oid unusable | Deny | `unidentifiable_caller` |
| D1 | oid lookup could not be read | Deny — **never** falls through to email or create | `contact_lookup_failed` / `binding_column_missing` |
| D2 | oid on ≥ 2 contacts | Collision, flag every row | `contact_oid_ambiguous` · OidOnMultipleContacts |
| D3 | oid on 1 INACTIVE contact | Deny — no email bind, no create | `contact_inactive` |
| D4 | oid on 1 active contact | **ResolveByOid** — email never consulted | — |
| E0 | no oid row, caller not eligible | Deny | the member-test code / `contact_not_found` (CIAM) |
| E1 | no email | eligible creator → **CreateByOid**; CIAM → Deny | `contact_not_found` |
| E2 | email lookup could not be read | Deny | `contact_lookup_failed` / `binding_column_missing` |
| E3 | ≥ 2 active contacts carry the email | Collision, flag every row | `contact_email_ambiguous` · EmailAmbiguous |
| E4 | 0 active contacts carry the email | creator → **CreateByOid**; CIAM → Deny | `contact_not_found` |
| E5 | 1 row, binding unreadable | Collision | `contact_binding_unreadable` · BindingUnreadable |
| E6 | 1 row bound to THIS oid | ResolveByOid (a concurrent bind) | — |
| E7 | 1 row bound to another oid | Collision | `contact_bound_to_different_oid` · BoundToDifferentOid |
| E8 | 1 unbound row, systemuser guest | Collision | `contact_guest_email_match` · GuestEmailMatch |
| E9 | 1 unbound row, references unreadable | Deny | `contact_lookup_failed` |
| E10 | 1 unbound row, linked by a systemuser (CIAM: any; others: one whose oid ≠ caller) | Collision | `contact_linked_to_other_user` · LinkedToOtherUser |
| E11 | 1 unbound row, eligible | **BindByEmail** (oid "D" + plane) | — |

### 3.4 Existing-link flow (systemuser with `sprk_primarycontact` = A) — never re-points, never clears

| # | Condition | Outcome | Code / flag |
|---|---|---|---|
| L1 | oid lookup could not be read | Deny | `contact_lookup_failed` |
| L2 | oid on ≥ 2 contacts | Collision | OidOnMultipleContacts |
| L3 | oid on A, active | **LinkVerified** | — |
| L4 | oid on A, inactive | Collision | `linked_contact_inactive` · LinkedContactInactive |
| L5 | oid on C ≠ A | Collision (flag A) | `linked_contact_mismatch` · LinkedContactMismatch |
| L6 | no oid row; A missing | Deny (nothing to flag) | `linked_contact_missing` |
| L7 | A unreadable | Collision | BindingUnreadable |
| L8 | A bound to another oid (**Ralph today**) | Collision, link kept | `linked_contact_bound_to_different_oid` |
| L9 | A unbound, inactive | Collision | LinkedContactInactive |
| L10 | A unbound, user is a guest | Collision | `guest_link_unverified` · GuestLinkUnverified |
| L11 | A unbound, linked by another user | Collision | LinkedToOtherUser |
| L12 | A unbound, otherwise | **BindLinkedContact** (oid onto A; link unchanged) | — |

### 3.5 Writes

| Write | Mechanism | Race / failure |
|---|---|---|
| Bind | `PATCH contacts(id)` with `If-Match: <etag read with the row>` | 412 → re-decide once; otherwise Deny `contact_bind_failed` (**not** "resolved anyway") |
| Create | `PATCH contacts(sprk_externalobjectid='<oid>')` + `If-None-Match: *` — create-only via the alternate key | 412 → re-read by oid and resolve; key not defined → Deny `contact_create_unavailable` |
| Link | `PATCH systemusers(id)` `sprk_PrimaryContact@odata.bind`, `If-Match: <etag>` | 412 → not re-pointed |
| Flag | `PATCH contacts(id)` the four flag columns | written only when the row carries no open flag (idempotent) |
| Before any bind/create/link | masking probe: rows with a plane marker; all oid-null ⇒ masked | Deny `binding_column_masked` |

## 4. Implementation map

### 4.1 New

| File | What it is |
|---|---|
| `Infrastructure/ExternalAccess/ContactBindingDecision.cs` | The ONE pure decision (§3), public: `Decide` (unlinked flow D0–E11), `DecideExistingLink` (L1–L12), `DecideInvite`, `ReadBinding`, `CollisionStillHolds`, `ShouldWriteFlag`, the deny/invite codes, and the shared read `ActiveContactsBoundToQuery` (used by `IdentityNormalizationService` and `CallerContactResolver`). No I/O. |
| `Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs` | `WorkforceIdentity:CustomerTenantIds` + `WorkforceIdentityOptionsValidator` (non-GUID / all-zero / CIAM tenant fail startup; empty is valid and DENIES) + `WorkforceCallerClaims` + `WorkforceMembershipTest` (member = `CallerIdentity.FromPrincipal` UserDelegated ∧ `tid` listed ∧ `acct` = 0; six distinct deny codes). |
| `Infrastructure/ExternalAccess/ContactIdentityStore.cs` | `IContactIdentityStore` (testing seam — HTTP doubles are banned by ADR-038 B1) + `DataverseContactIdentityStore` (Web API, app-only): three-state lookups (Read / Failed / ColumnMissing), ACTIVE rows `$top=2`, `If-Match` bind/link, create-only `PATCH contacts(sprk_externalobjectid='…')` + `If-None-Match: *`, flag write/clear, the FLS masking probe, paged scans. Pure request builders and parsers are unit-tested. |
| `Infrastructure/ExternalAccess/ContactIdentityBinder.cs` | The ONE binding writer. `ResolveWorkforceCallerAsync`, `ResolveCiamCallerAsync`, `EnsureSystemUserLinkAsync` (row / id), `ResolveInviteContactAsync`, `BindInvitedContactAsync`. Runs the decision, performs the conditional writes, re-decides once on 412, writes idempotent flags, probes masking before any write. `ContactIdentityBinderFactory` builds one over another environment (registration). |
| `Services/ExternalAccess/IdentityLinkReconciliationJob.cs` | ADR-036 `IScheduledJob` `identity-link-reconciliation`, `*/5 * * * *`. Masking probe → pass 1 (users) → pass 2 (clear resolved flags, only after a complete pass 1). Report-only unless `IdentityLink:Reconciliation:WritesEnabled` parses `true`. |
| `scripts/Set-ContactIdentityBindingSchema.ps1` | Dry-run / `-Apply` / `-Verify`: (a) normalise oids, (b) alternate key, (c) plane + flag columns, backfill External, collision view, (d) FLS reader/writer profiles, (e) solution components, (f) publish. |

### 4.2 Changed

| File | Change |
|---|---|
| `WorkforcePrincipalResolver.cs` | Contact-only branch → `ContactIdentityBinder.ResolveWorkforceCallerAsync`; the decision's own deny code reaches the response. Systemuser with no contact → inline `EnsureSystemUserLinkAsync` + `IIdentityNormalizationService.InvalidateAsync` (same-request effect); a failed link is not retried for 10 min (`identity-link-attempt` cache marker). `ExtractVerifiedEmail` → `ExtractTokenEmail` (it verified nothing). |
| `IdentityNormalizationService.cs` / `IIdentityNormalizationService.cs` | Read-only. `TryResolveContactByWorkforceIdentityAsync`, the email path and `GuardInertNoBindingColumn` deleted; the systemuser fallback reads the oid binding via `ActiveContactsBoundToQuery` (ambiguous / unreadable → null). `InvalidateAsync` added. A-18 residual comment removed. |
| `CallerContactResolver.cs` | Reads the oid binding (`sprk_externalobjectid`), two rows, ambiguity → `ambiguous-binding`. |
| `ExternalParticipationService.cs` | CIAM contact resolution, `ResolveContactByOidAsync`, `ResolveContactByEmailAsync`, `BindOidToContactAsync` deleted — moved into the binder/decision (§6 D-1). Keeps grant data. |
| `CallerPrincipalResolver.cs` | `CiamContactPrincipalStrategy` → `ResolveCiamCallerAsync`; returns the decision's distinct deny code + message. |
| `AccessibleRecordSetService.cs` | Licensed-user email fallback removed; the contact comes only from `principal.ContactId` (the link). |
| `InviteExternalUserEndpoint.cs` / `InviteAndGrantExternalUserEndpoint.cs` | `ProvisionAsync` resolves through `DecideInvite`: workforce-bound / systemuser-linked / ambiguous → HTTP 409 ProblemDetails with `reasonCode` + flag; no CIAM account, no grant. CIAM-bound stays idempotent. Lookup failure → 500 ProblemDetails (not bare). New CIAM oid bound through the binder (plane External). |
| `RegistrationDataverseService.cs` | `CreateSystemUserAsync` → `LinkContactForNewSystemUserAsync` on `ContactLinkEnvironment(targetDataverseUrl)`. `DemoProvisioningService.cs` unchanged — it already passes `targetDataverseUrl`. |
| `ExternalAccessModule.cs` | Options + validator, named HttpClient, store, binder, factory, `AddScheduledJob<IdentityLinkReconciliationJob>`. |
| `appsettings.template.json` | `WorkforceIdentity:CustomerTenantIds: []`, `IdentityLink:Reconciliation:WritesEnabled: false`, with comments. |
| `scripts/Register-EntraAppRegistrations.ps1` | Step 1 adds the `acct` access-token optional claim (merging, idempotent); `-AcctClaimOnly -AcctClaimAppId` for an existing registration. |
| Doc comments only | `PersonIdentity.cs`, `MembershipResolverService.cs`, `AnalysisServicesModule.cs`, `CallerSystemUserResolver.cs`, `ExternalCallerContext.cs`, `NoAccessListReader.cs`. |
| `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` | §6.5.2 tenant setting + job switch + schema prerequisite, §6.5.3 operator collision procedure, §7.3 `acct`, change-log row. |

### 4.3 Escalation triggers checked

| Trigger | Result |
|---|---|
| 1 Licensed collision rows | Answered: owner (a) / I2 = (1). Flag; never clear or re-point. |
| 2 Tenant-list source | Answered: I1 = (b). New setting; provisioning handoff written. |
| 3 `acct` reliability | **Does not fire** (§1). Configuration is a pending gate (G-2). |
| 4 FLS breaks a client writer | **Does not fire.** Re-grepped `src/client` + `src/solutions`: only reads (`useInlineTodoCreate.ts`, `TrackingFieldTrio/index.ts`, `DailyBriefingApp.tsx` comments). No form/PCF writes either field. |
| 5 Relax an alternate key | **Does not fire.** `contact` has no alternate key; one is added. |
| 6 Member test account | **Does not fire.** Owner T2: `test.user@demo.spaarke.com` (Member, enabled, no licence, no systemuser, no contact). |

## 5. Placement and justification (CLAUDE.md §10 / §11)

**Placement (bff-extensions.md §A)**: every component is in the BFF. The binding runs inside the authorization
path of each Teams/SPA/CIAM request (latency-coupled, B4), over BFF-owned identity data, under the BFF's own
app-only identity. The job is in-process on `Spaarke.Scheduling` per ADR-052 (BFF domain code, BFF-owned tables, low
volume; the one Functions-leaning signal — one dispatch per schedule — is met by the host lease). **No new NuGet
package.** No AI-internal type is injected (ADR-013 facade not relevant). No OBO, no Graph with the caller token, no
new `.WithClientSecret(...)` (ADR-028 A2/A4): the store uses the registered `TokenCredential` (BFF managed
identity); registration reuses its existing per-environment token path.

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `ContactBindingDecision` | `DecideWorkforceEmailMatch` / `ReadOidBinding` (task 013), CIAM copies in `ExternalParticipationService` | **Extended**: it IS those two, generalised to all planes; the copies were deleted, not forked | Each plane keeps its own rule; the CIAM path keeps `$top=1` and the failed-read fall-through (background item 6) |
| `WorkforceIdentityOptions` (+ validator, member test) | `TenantRouting:Tenants[]` (routing, unconfigured), `AzureAd:TenantId` | No — owner I1 = (b) chose a new setting; both existing values are wrong in Model 1 | Member test refuses every Model-1 employee or binds Spaarke staff into a customer stamp |
| `IContactIdentityStore` / `DataverseContactIdentityStore` | `IDataverseService` (SDK; no `If-Match`/`If-None-Match`, no alternate-key upsert-create-only), `ExternalParticipationService`'s private Web API calls | No — the conditional writes are the race guarantees (one contact per oid; never bind over a concurrent bind). The interface is the ADR-010 test seam (HTTP doubles banned) | Two racing first sign-ins create two contacts; a bind overwrites a concurrent bind |
| `ContactIdentityBinder` (+ factory) | `IdentityNormalizationService` (read-only, cached), `ExternalParticipationService` (grant data) | No (POML justification (1)): giving the cached read-only resolver Dataverse writes adds a second reason to change (§11.5). One writer shared by 5 callers | Five call sites each write bindings/links/flags their own way |
| `IdentityLinkReconciliationJob` | `ExternalAccessReconciliationJob` (grant/membership lifecycle), `MembershipReconciliationJob` | No — different scan (systemusers + flagged contacts), different reason to change (§11.5). Copies the safety convention | 7 of 8 dev users never get a link unless they sign in to Teams/SPA; Assigned-To (C9), No Access List (Q4), briefing (Q8) skip them |
| Schema (plane, flag columns, key, FLS, view) | `sprk_externalobjectid` (text, no plane); no collision column; no key; 0 `fieldpermission` rows | Extended the existing column; added the smallest set the refusals need | No durable flag (log lines are not flags); invite answers "AlreadyProvisioned" for an employee; anyone with Write on contact can put their own oid on a granted contact |

**Component complexity (§11.5)**: `ContactBindingDecision.cs` (898 lines) is one cohesive pure decision — a large
share is the closed enum/deny-code vocabulary and XML docs; splitting the table across files would scatter one rule.
`ContactIdentityBinder.cs` (843) has one reason to change (performing the decision's writes) and five thin entry
points over shared write helpers. `ContactIdentityStore.cs` (682) is the Web API mapping. Noted for the PR.

## 6. Deviations and ADR tensions

| # | Item | Path |
|---|---|---|
| D-1 | **CIAM resolution moved OUT of `ExternalParticipationService`** into the binder; the POML's extension note said "resolution stays in" it. Reason: the decision plus its writes (bind with etag, flag, masking probe) is one cohesive unit shared with the workforce plane; leaving the read half in `ExternalParticipationService` would split one rule across two classes and keep two Web API write paths. `ExternalParticipationService` keeps its single reason to change (grant data). The POML allowed this ("If execution shows the writer fits cohesively … say so in the PR"). | Recorded deviation (directional step) |
| D-2 | ADR-038 B8 `InternalsVisibleTo` (task 013's pending deviation): **retired**, not copied. The decision is `public`, so tests assert it directly. | §6.5 **path C** (pivot to comply) |
| D-3 | **Collision rows keep the existing `sprk_primarycontact`** and `IdentityNormalizationService` honours it as-is (owner I2 = (1)); `CallerContactResolver` ("assign it to me") follows only the oid binding, so a flagged kept link does not resolve "me". Deliberate: membership must not lose access; "me" must be proven. Stated in the link contract §5. | Owner decision |
| D-4 | Contact created at first MEMBER sign-in supersedes SPA-r2 FR-11 "no contact merely by access" (owner round 2). Recorded in SPA-r2 notes. | Owner decision |
| D-5 | The job's interactive filter is `accessmode` 0/1/2 (Read-Write, Administrative, Read) — the constraint excludes non-interactive and application users; Administrative/Read users are humans. Today that adds one user (accessmode 1) to the POML's accessmode-0 baseline. | Recorded |
| D-6 | Live steps (schema, `acct`, dev setting, deploy, job runs, live gate) and the publish-size measurement were **not executed** — this run was instructed READ-ONLY for live systems and to skip publish-size. They are pending gates (§8), so the task outcome is `partial`. | Instruction |

No ADR violation is open. ADR-002 (no plugins), ADR-003 (fail closed; three-state lookups; ambiguity denies),
ADR-009 (cache data not decisions — the link-attempt marker never grants), ADR-010 (concrete registrations; the two
interfaces are test seams), ADR-028 (broker-only, app-only, no new secret), ADR-036 (job contract, no throw),
ADR-052 (placement) were checked; see §7.1 and the Step 9.5 record in the POML `<execution><outcome>`.

## 7. Tests and proof that they bite

New tests sit at KEEP paths: `tests/integration/auth/UnifiedAccessControl/IdentityBinding/**` (decision table,
member test, CIAM, reconciliation), `WorkforceEmailNoHijackTests.cs` (rewritten end-to-end over the in-memory
store), `tests/unit/.../ContactAadObjectIdColumnGuardTests.cs`, `DataverseContactIdentityStoreTests.cs` (pure
request builders/parsers — no HTTP double), `RegistrationContactLinkTests.cs`, plus updates to the contract, seam
and resolver suites. No `Mock<HttpMessageHandler>`, no DI-registration test, no ctor null-check test.

**Seeded violations** (each: patch source → build → run the named tests → restore; all sources restored and
re-grepped clean). Every one turned the suite RED:

| # | Seeded violation | Failing test (first) |
|---|---|---|
| G | `QueryExpression("contact")` naming `azureactivedirectoryobjectid` | `ContactAadObjectIdColumnGuardTests` |
| P1 | missing `acct` treated as member | `WorkforceMembershipTests` (3 failed) |
| P2 | tenant list ignored (Model 1) | `WorkforceMembershipTests` (3) |
| P3 | empty tenant list not special | `WorkforceMembershipTests.AnEmpty…` (2) |
| P4 | app-only token accepted | `WorkforceMembershipTests.AnAppOnly…` (3) |
| P5 | could-not-read oid falls through | `ContactBindingDecisionTests` (5) |
| P6 | inactive oid contact falls through | `ContactBindingDecisionTests` (3) |
| P7 | ambiguous email picks the first row | `ContactBindingDecisionTests` (3) |
| P8 | email match bound to another oid binds (hijack) | `ContactBindingDecisionTests` (8) |
| P9 | flag written on every collision | `IdentityLinkReconciliationTests` (3) |
| P10 | CIAM may create | `ContactBindingDecisionTests.E1_NoEmail…` (2) |
| P11 | masking probe ignored | `WorkforceEmailNoHijackTests.AMaskedBindingColumn_WritesNothing` (1) |
| P12 | licensed-user email fallback restored | `AccessibleRecordSetServiceTests` (1) |
| P13 | no cache invalidation after inline link | `WorkforceEmailNoHijackTests.ASystemUserWithNoLink…` (1) |
| P14 | job writes on by default | `IdentityLinkReconciliationTests` (4) |
| P15 | mismatched link re-pointed | `ContactBindingDecisionTests` (1) |
| P16 | invite ignores the workforce plane | `ContactBindingDecisionTests.Invite_AWorkforceBoundContact_IsRefused` (3) |
| P17 | `/invite-and-grant` grants after a refusal | `ExternalAccessContractTests` (2) |
| P18 | registration links in the default environment | `RegistrationContactLinkTests` (1) |
| P19 | "assign it to me" picks the first of two | `CallerContactResolverSeamTests` (1) |
| P20 | CIAM collapses to one deny code | `CallerPrincipalResolverTests` (4) |
| P21 | failed scan not a failed run | `IdentityLinkReconciliationTests.AFailedScan_IsAFailedRun…` (1) |
| P22 | flags cleared on a partial view | `IdentityLinkReconciliationTests.AFailedScan…` (1) |
| P23 | malformed tenant id accepted | `WorkforceMembershipTests` validator cases (3) |

| P24 | `*` accepted as a row version | `DataverseContactIdentityStoreTests.ABindOrLink_WithoutARowVersion…` (2) |
| P25 | invite bind overwrites a binding made meanwhile | `CiamContactBindingTests.TheInviteBind_NeverOverwritesABindingMadeMeanwhile` (1) |
| P26 | invite bind skips the masking probe | `CiamContactBindingTests.TheInviteBind_RunsTheMaskingProbe…` (1) |
| P27 | link re-points a link made meanwhile | `RegistrationContactLinkTests.ALinkMadeMeanwhile_IsNeverRePointed` (1) |
| P28 | an undecided user's flag is cleared | `IdentityLinkReconciliationTests.AFlagWhoseUserCouldNotBeDecidedThisRun…` (1) |
| P29 | a malformed response body escapes as an exception | `DataverseContactIdentityStoreTests.AMalformedResponseBody…` (1) |

(P11, P16, P21 and P22 were first written as `if (false)`, which the compiler rejects as unreachable code under
warnings-as-errors; they were re-seeded as a non-constant false and went RED. P24–P29 cover the Step 9.5 review
fixes in §7.1.)

### 7.1 Step 9.5 review fixes (code-review + adr-check, 2026-10-01)

| # | Finding | Fix |
|---|---|---|
| R-1 | A bind or link with no row version went out as `If-Match: *` — "the row exists", not "the row is as read". The invite's bind of a contact it had just created always did this, and so did the registration link of a just-created systemuser: an unconditional write could overwrite a binding a concurrent sign-in made, or re-point a link. | `DataverseContactIdentityStore.RowVersionPrecondition`: no version (or `*`) = no write. `BindInvitedContactAsync` re-reads a version-less contact and binds only an active, unbound row; `LinkAsync` re-reads a version-less systemuser and never re-points a link made meanwhile. |
| R-2 | The invite's bind skipped the FLS masking probe every other bind runs. | `BindInvitedContactAsync` probes first. |
| R-3 | A response body that is not JSON threw out of the store, breaking its three-state contract. | `TryParseJson` → could-not-read (denies through the decision). |
| R-4 | Pass 2 cleared a flag whose systemuser WAS scanned but whose decision did not complete (a transient read/write failure): "no decision" was read as "the party is gone". The flag flickered off and was re-written next run. | `ScannedOids`: scanned-but-undecided keeps the flag. |
| R-5 | §6.5.3 told operators to resolve a duplicate OID by deactivating the duplicate; the oid lookup reads every state, so the oid stays ambiguous. | Guide corrected: clear both binding columns on the wrong contact. |
| R-6 | WP-1: the new invariant had no row in the write-path invariant registry. | `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5 row I-10. |

Reviewed and accepted (not changed): the reconciliation job logs `internalemailaddress` per decision (operators
compare the report-only run against §0 by person; it is the tenant's own directory data in the tenant's own App
Insights); repeated denied sign-ins cost ~3 reads each (no write — the flag is idempotent); a narrow race can leave a
stale "no contact" identity cached for ≤10 min after an inline link (less access, never more); the invite flag for
"linked to an internal user" reuses reason `InviteMatchesWorkforceContact` (the reason code in the 409 is distinct);
the job's shipping-state test reads `ScheduledJobRegistration` (same precedent as `ExternalAccessReconciliationTests.S2`);
the per-consumer named HttpClient follows `ExternalAccessModule`'s existing one-client-per-service pattern; +7 DI
registrations inside the feature module (ADR-010's ≤15 line is the documented, accepted project-wide elevation).

**Suite results (2026-10-01, after the review fixes)**: full BFF suite (`tests/unit/Sprk.Bff.Api.Tests`, which
also compiles the `tests/integration/**` KEEP paths) **13,347 passed / 0 failed / 54 skipped (13,401)**; NetArchTest
(`tests/Spaarke.ArchTests`) **337 / 0 / 0**; `dotnet list package --vulnerable --include-transitive`: none; no
package changes.

## 8. Pending manual gates (READ-ONLY run — nothing below was executed)

Order is binding: **G-1 before G-4** (the new BFF selects the new columns; without them CIAM and Type-2 sign-ins
fail closed with `binding_column_missing`).

| Gate | Action | Exact command |
|---|---|---|
| **G-1** Schema (dev) | Apply, then verify (exit 0). Dry run 2026-10-01: 6 oids OK (lowercase D), key + 2 choices + 5 columns + view + 2 FLS profiles + 2 writer members (`# mi-bff-api-dev`, `SDAP-BFF-SPE-API`) + 6 BU default teams + 2 secured fields + 1 solution component WOULD be written; backfill 6 as External | `.\scripts\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply` then the same with `-Verify` |
| **G-2** `acct` claim (dev BFF registration) | Add the access-token optional claim | `.\scripts\Register-EntraAppRegistrations.ps1 -TenantId a221a95e-6abc-4434-aecc-e48338a1b2f2 -AcctClaimOnly -AcctClaimAppId 1e40baad-e065-4aea-a8d4-4b7ab273458c`; verify `az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query optionalClaims.accessToken` lists `acct` |
| **G-3** Tenant setting (dev) | Dev's workforce tenant is the registration's tenant (Model-2 shape) | `az webapp config appsettings set -g <dev-rg> -n <dev-bff-app> --settings WorkforceIdentity__CustomerTenantIds__0=a221a95e-6abc-4434-aecc-e48338a1b2f2` (both slots) |
| **G-4** Deploy BFF (dev) | After merge to the project branch | `bff-deploy` skill / `scripts/Deploy-BffApi.ps1` |
| **G-5** Job report-only run | Leave `IdentityLink__Reconciliation__WritesEnabled` unset; trigger a run (admin jobs endpoint, `ManualAdmin`) or wait 5 min; review `[ID-LINK-RECON] before-state` lines + ResultJson against §0 (expect: 1 bind, 7 creates, Ralph's link Verified-or-flagged-kept, 3 collisions flagged, nothing written) | App Insights: `traces | where message startswith "[ID-LINK-RECON]"` |
| **G-6** Enable writes, run again, run a third time | Then confirm the third run changes nothing | `az webapp config appsettings set … --settings IdentityLink__Reconciliation__WritesEnabled=true` |
| **G-7** FLS authorization | As a non-admin dev user: edit both fields in MDA → refused; Daily Briefing inline to-do still defaults Assigned To; TrackingFieldTrio still shows an internal user as internal | manual |
| **G-8** Live gate items (POML criterion "MANUAL LIVE GATE") | (1) `test.user@demo.spaarke.com` signs in to Teams/SPA → resolves; one contact created, plane Workforce; second sign-in resolves by oid. (2) a guest / no-`acct` token is not bound. (3) the 3 collision emails refused + visible in "Contacts with Identity Collisions". (4) G-5/G-6 counts recorded. (5) a linked user named in `sprk_assignedattorney1` no longer logs `member_skipped`. (6) = G-7. (7) first link visible on the same request. (8) invite to a workforce-bound contact's email → 409, no CIAM account | manual; record results here |
| **G-9** Publish size | Skipped by instruction this run. Fresh short-path worktrees, both sides, `Compress-Archive`, equal file counts | CLAUDE.md §10 procedure |
