# Task 141 — workforce identity binding by Entra oid, and the systemuser↔contact link

> **Status**: implemented on branch `task/uac-r2-141` (2026-10-01). Live steps are **pending manual gates** —
> this run was read-only against Dataverse/Entra by instruction (§8 lists every gate with its exact command).
> ✅ **Third fix round** (`task/uac-r2-141-f3`, 2026-10-02): the §9 owner decision is made — owner round 4 item 4
> = **B2** — and implemented; the registration-link gap of §12.1 is CLOSED (the job reconciles every provisioning
> target); the remaining INFO finding is fixed. See **§13**. G-1..G-8 are owner-approved and pending (the main
> session runs them after deploy).
> **Owner answers in force**: round 2 item 4, Q3; round 3 I1 = (b), I2 = (1), T2; trigger (a) for licensed
> collision rows. Source: `session27-owner-decisions-and-research.md`.
> **Peer contract**: [`141-link-contract.md`](141-link-contract.md) (word-add-in-r1 task 083 waits on it).
> **Verifier fix round** (`task/uac-r2-141-f1`, 2026-10-01): §9 (⛔ OWNER DECISION — alternate key vs field-level
> security on `contact.sprk_externalobjectid`; G-1 blocked), §10 (what changed per finding, with the seeded
> violations), §11 (statements the PR must carry).
> **Second verifier fix round** (`task/uac-r2-141-f2`, 2026-10-01): §12 (findings 4–6 fixed — registration-link
> retry wording and the recorded gap, the invite lookup-failure reason code, one answer for "which contact is bound
> to this oid?"; findings 1–3 and 11–15 still blocked or pending, unchanged).
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
| Bind | `PATCH contacts(id)` with `If-Match: <etag read with the row>`, writing the binding `sprk_externalobjectid` AND the uniqueness mirror `sprk_externalobjectidkey` (same oid, one request — B2, §13) | 412 → re-decide once; otherwise Deny `contact_bind_failed` (**not** "resolved anyway"). Duplicate-key fault (`0x80060892`, another contact holds the oid in its mirror) → **K1**: flag the holder (reason `KeyMirrorConflict`), Deny `contact_key_conflict` |
| Create | `PATCH contacts(sprk_externalobjectidkey='<oid>')` + `If-None-Match: *` — create-only via the alternate key on the MIRROR, the binding in the body (B2) | 412 → re-read by the binding and resolve (a racing first sign-in won); 412 AGAIN with no contact bound → **K2**: flag the holder, Deny `contact_key_conflict`; key not defined → Deny `contact_create_unavailable` |
| Link | `PATCH systemusers(id)` `sprk_PrimaryContact@odata.bind`, `If-Match: <etag>` | 412 → not re-pointed |
| Flag | `PATCH contacts(id)` the four summary columns + `sprk_identitycollisionparties` (every party), `If-Match: <etag>` | a party is recorded once per contact (idempotent per identity+reason); a DIFFERENT identity is appended (fix round, finding 3); 412 → re-read once and append again |
| Flag prune / clear (job pass 2) | `PATCH contacts(id)`, `If-Match: <etag the verdict was made on>`; a clear without a version is refused | 412 → kept; the next run re-evaluates |
| Before any bind/create/link | masking probe: rows with a plane marker; all oid-null ⇒ masked | Deny `binding_column_masked` |

## 4. Implementation map

### 4.1 New

| File | What it is |
|---|---|
| `Infrastructure/ExternalAccess/ContactBindingDecision.cs` | The ONE pure decision (§3), public: `Decide` (unlinked flow D0–E11), `DecideExistingLink` (L1–L12), `DecideInvite`, `ReadBinding`, `CollisionStillHolds`, `ShouldWriteFlag`, the deny/invite codes, and the shared read `ContactsBoundToQuery` + `BoundContactLookup` + `DecideBoundContact` (every statecode, two rows, the binder's own oid step — used by `IdentityNormalizationService` and `CallerContactResolver`; renamed from `ActiveContactsBoundToQuery` in the second fix round, §12). No I/O. |
| `Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs` | `WorkforceIdentity:CustomerTenantIds` + `WorkforceIdentityOptionsValidator` (non-GUID / all-zero / CIAM tenant fail startup; empty is valid and DENIES) + `WorkforceCallerClaims` + `WorkforceMembershipTest` (member = `CallerIdentity.FromPrincipal` UserDelegated ∧ `tid` listed ∧ `acct` = 0; six distinct deny codes). |
| `Infrastructure/ExternalAccess/ContactIdentityStore.cs` | `IContactIdentityStore` (testing seam — HTTP doubles are banned by ADR-038 B1) + `DataverseContactIdentityStore` (Web API, app-only): three-state lookups (Read / Failed / ColumnMissing), ACTIVE rows `$top=2`, `If-Match` bind/link, create-only `PATCH contacts(sprk_externalobjectid='…')` + `If-None-Match: *`, version-conditional flag write/clear with every party as JSON, the FLS masking probe, paged scans. Pure request builders and parsers are unit-tested. |
| `Infrastructure/ExternalAccess/ContactIdentityBinder.cs` | The ONE binding writer. `ResolveWorkforceCallerAsync`, `ResolveCiamCallerAsync`, `EnsureSystemUserLinkAsync` (row / id), `ResolveInviteContactAsync`, `BindInvitedContactAsync`. Runs the decision, performs the conditional writes, re-decides once on 412, writes idempotent flags, probes masking before any write. `ContactIdentityBinderFactory` builds one over another environment (registration). |
| `Services/ExternalAccess/IdentityLinkReconciliationJob.cs` | ADR-036 `IScheduledJob` `identity-link-reconciliation`, `*/5 * * * *`. Masking probe → pass 1 (users) → pass 2 (per party: drop the resolved ones, prune or clear the flag via `ContactBindingDecision.ReconcileFlag`, only after a complete, untruncated pass 1 on a readable probe). Report-only unless `IdentityLink:Reconciliation:WritesEnabled` parses `true` — the same switch gates the inline link (fix round, finding 4). |
| `scripts/Set-ContactIdentityBindingSchema.ps1` | Dry-run / `-Apply` / `-Verify`: (a) normalise oids, (b) alternate key, (c) plane + flag columns, backfill External, collision view, (d) FLS reader/writer profiles, (e) solution components, (f) publish. |

### 4.2 Changed

| File | Change |
|---|---|
| `WorkforcePrincipalResolver.cs` | Contact-only branch → `ContactIdentityBinder.ResolveWorkforceCallerAsync`; the decision's own deny code reaches the response. Systemuser with no contact → inline `EnsureSystemUserLinkAsync` + `IIdentityNormalizationService.InvalidateAsync` (same-request effect), **only when `IdentityLink:Reconciliation:WritesEnabled` is true** (fix round, finding 4); a failed link is not retried for 10 min (`identity-link-attempt` cache marker). `ExtractVerifiedEmail` → `ExtractTokenEmail` (it verified nothing). |
| `IdentityNormalizationService.cs` / `IIdentityNormalizationService.cs` | Read-only. `TryResolveContactByWorkforceIdentityAsync`, the email path and `GuardInertNoBindingColumn` deleted; the systemuser fallback reads the oid binding via `ContactsBoundToQuery` + `DecideBoundContact` (ambiguous in any state / inactive / unreadable → null; §12). `InvalidateAsync` added. A-18 residual comment removed. |
| `CallerContactResolver.cs` | Reads the oid binding (`sprk_externalobjectid`), two rows in any state, through `DecideBoundContact`: ambiguity → `ambiguous-binding`, an inactive bound contact → `inactive-contact` (§12). |
| `ExternalParticipationService.cs` | CIAM contact resolution, `ResolveContactByOidAsync`, `ResolveContactByEmailAsync`, `BindOidToContactAsync` deleted — moved into the binder/decision (§6 D-1). Keeps grant data. |
| `CallerPrincipalResolver.cs` | `CiamContactPrincipalStrategy` → `ResolveCiamCallerAsync`; returns the decision's distinct deny code + message. |
| `AccessibleRecordSetService.cs` | Licensed-user email fallback removed; the contact comes only from `principal.ContactId` (the link). |
| `InviteExternalUserEndpoint.cs` / `InviteAndGrantExternalUserEndpoint.cs` | `ProvisionAsync` resolves through `DecideInvite`: workforce-bound / systemuser-linked / ambiguous → HTTP 409 ProblemDetails with `reasonCode` + flag; no CIAM account, no grant. CIAM-bound stays idempotent. Lookup failure → 503 ProblemDetails with `reasonCode` `sdap.access.invite.contact_lookup_failed` and the decision's message (§12; a generic 500 before). New CIAM oid bound through the binder (plane External). |
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
| **G-1** Schema (dev) | **Unblocked (§9 decided: B2; script amended — §13.1).** Dry run first (read-only), then apply, then verify (exit 0; re-run `-Verify` until the key is Active). Dry run 2026-10-02 (B2 script, read-only): platform rules OK (key on `sprk_externalobjectidkey`, FLS on `sprk_externalobjectid`); WOULD create the mirror column, copy the 6 bindings into it, create key `sprk_ExternalObjectIdUniqueKey` on the mirror, 2 choices (reason choice now 12 options) + 6 columns + view + 2 FLS profiles + 2 writer members (`# mi-bff-api-dev`, `SDAP-BFF-SPE-API`) + 6 BU default teams + 2 secured fields; backfill 6 as External | `.\scripts\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c` (dry run), then with `-Apply`, then with `-Verify` |
| **G-1b** Provisioning target (dev) — NEW, third fix round | The job now reconciles every environment the dev BFF provisions into (§13.2). Read-only 2026-10-02: the dev BFF's `DATAVERSE_URL` = `Dataverse__ServiceUrl` = spaarkedev1; active registry rows in spaarkedev1: **Dev** (spaarkedev1 — the own environment, skipped), **Demo 1** (`https://spaarke-demo.crm.dynamics.com` — a target), `trial-2026-08-18` (no URL — skipped). In spaarke-demo the dev BFF's identities (`5967251e…` MI, `1e40baad…`) are **NOT application users** (only the demo stamp's own `BFF mi-bff-api-demo` / `Spaarke BFF API - Demo`), and the binding columns do not exist there. So every job run will report spaarke-demo as a FAILED environment (`Success=false`; the own environment is still reconciled) — accurately: the dev BFF cannot register into Demo 1 either (its `CreateSystemUserAsync` would get 403). **Owner/main-session choice, NOT approved by round 4 (a different environment):** (a) if dev is meant to provision into Demo 1, add the dev BFF MI as an application user there with the role demo provisioning needs, then run G-1 against spaarke-demo; (b) if not, deactivate the stale "Demo 1" row in spaarkedev1's `sprk_dataverseenvironment` (a data write); (c) accept the reported failure during the G-5/G-6 review. Recommendation: (b) — the row cannot work for this BFF today. | (a) `.\scripts\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://spaarke-demo.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d` (after the app user exists); (b) set `sprk_isactive = false` on the "Demo 1" `sprk_dataverseenvironment` row in spaarkedev1 |
| **G-2** `acct` claim (dev BFF registration) | Add the access-token optional claim | `.\scripts\Register-EntraAppRegistrations.ps1 -TenantId a221a95e-6abc-4434-aecc-e48338a1b2f2 -AcctClaimOnly -AcctClaimAppId 1e40baad-e065-4aea-a8d4-4b7ab273458c`; verify `az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query optionalClaims.accessToken` lists `acct` |
| **G-3** Tenant setting (dev) | Dev's workforce tenant is the registration's tenant (Model-2 shape) | `az webapp config appsettings set -g <dev-rg> -n <dev-bff-app> --settings WorkforceIdentity__CustomerTenantIds__0=a221a95e-6abc-4434-aecc-e48338a1b2f2` (both slots) |
| **G-4** Deploy BFF (dev) | After merge to the project branch | `bff-deploy` skill / `scripts/Deploy-BffApi.ps1` |
| **G-5** Job report-only run | Leave `IdentityLink__Reconciliation__WritesEnabled` unset; trigger a run (admin jobs endpoint, `ManualAdmin`) or wait 5 min; review `[ID-LINK-RECON] before-state` lines + ResultJson against §0 (expect: 1 bind, 7 creates, Ralph's link Verified-or-flagged-kept, 3 collisions flagged, nothing written). These counts no longer drift between G-4 and G-6: the inline link is gated on the same switch (fix round, finding 4). Do not run a demo registration during the review (registration is not gated — §10, finding 4). The ResultJson's root is the own environment; `provisioningTargets.environments[]` carries each target (G-1b) | App Insights: `traces | where message startswith "[ID-LINK-RECON]"` |
| **G-6** Enable writes, run again, run a third time | Then confirm the third run changes nothing | `az webapp config appsettings set … --settings IdentityLink__Reconciliation__WritesEnabled=true` |
| **G-7** FLS authorization | As a non-admin dev user: edit both fields in MDA → refused; Daily Briefing inline to-do still defaults Assigned To; TrackingFieldTrio still shows an internal user as internal | manual |
| **G-8** Live gate items (POML criterion "MANUAL LIVE GATE") | (1) `test.user@demo.spaarke.com` signs in to Teams/SPA → resolves; one contact created, plane Workforce; second sign-in resolves by oid. (2) a guest / no-`acct` token is not bound. (3) the 3 collision emails refused + visible in "Contacts with Identity Collisions". (4) G-5/G-6 counts recorded. (5) a linked user named in `sprk_assignedattorney1` no longer logs `member_skipped`. (6) = G-7. (7) first link visible on the same request. (8) invite to a workforce-bound contact's email → 409, no CIAM account | manual; record results here |
| **G-9** Publish size | Skipped by instruction this run. Fresh short-path worktrees, both sides, `Compress-Archive`, equal file counts | CLAUDE.md §10 procedure |

## 9. ✅ DECIDED (owner round 4 item 4, 2026-10-01: **B2**) — alternate key vs field-level security on `contact.sprk_externalobjectid`

> **Decision** (`session27-owner-decisions-and-research.md` round 4 item 4): FLS stays on `sprk_externalobjectid`;
> the alternate key moves to a new unsecured mirror column `sprk_externalobjectidkey`, written with the same oid in
> the same request as every bind and create; every read keeps using the secured column. **Implemented in the third
> fix round — §13.1.** The text below is the escalation as raised, kept for the record.
>
> Raised by the adversarial verifier (finding 1, CRITICAL), 2026-10-01. Confirmed against Microsoft Learn the same
> day.

🔔 **Human Input Required — conflicting requirements (CLAUDE.md §6 / §6.5 format)**

- **Rules in question**: two of this task's own constraints, against a platform rule. (No ADR is violated by any
  option below except where noted; ADR-003 "fail closed" is the bar each option is held to.)
  - *Constraint "Scope: contact creation"*: "Creation is idempotent by oid: exactly one contact per oid, including
    when two first sign-ins race." Delivered by step 2(b) as an alternate key `sprk_ExternalObjectIdKey` on
    `contact(sprk_externalobjectid)` + create-only `PATCH contacts(sprk_externalobjectid='<oid>')` with
    `If-None-Match: *`.
  - *Constraint "Scope: contact.sprk_externalobjectid and systemuser.sprk_primarycontact"*: "Lock both with
    field-level security … only the BFF app user can write." Delivered by step 2(d).
  - *Platform rule*: "Attributes must not have field-level security applied" ([Work with alternate keys](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity));
    "Columns that have the **Enable column security** property enabled can't be used as an alternate key"
    ([Define alternate keys to reference rows](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-alternate-keys-reference-records)).
- **Conflict**: G-1 can deliver at most one of (b) and (d) on that column. (a) If the key wins, the FLS criterion
  fails for `sprk_externalobjectid`, and any user with contact Write can put their own oid on a contact that holds
  grants and inherit them (a takeover). (b) If FLS wins, `CreateContactForOidAsync` returns `KeyMissing` and every
  Type-2 creation and every systemuser create-link is denied `contact_create_unavailable`; criterion 3 then has no
  enforcing mechanism. The original run's tests were green only because `InMemoryContactIdentityStore` modelled a
  key-plus-FLS combination the platform does not allow (that double's doc now says so). The 2026-10-01 dry run never
  exercised the rule: it printed `WOULD` lines only.
- **What holds under EVERY option** (already in the code, unchanged): two contacts carrying one oid DENY on every
  plane and every reader (`contact_oid_ambiguous`, flagged); a create that cannot be guaranteed unique DENIES
  (`contact_create_unavailable`); a masked binding column refuses every write (`binding_column_masked`). So the
  undecided state is fail-closed, not fail-open.

**Options**

| | Option | Uniqueness | Write lock | Cost | Residual risk |
|---|---|---|---|---|---|
| **B2 (recommended)** | **Keep FLS on `sprk_externalobjectid`; move the KEY to a new unsecured mirror column** `sprk_externalobjectidkey` (Text 100, alternate key), written by the BFF with the same oid in the same request as every bind and create. Every READ keeps using the secured column; the mirror exists only so the platform refuses a second contact for one oid. | Platform-enforced, for creates AND binds | Unchanged (FLS on the binding column) | One column + key; the bind/create payloads write both columns; the create addresses the mirror key; the schema copies the 6 existing bindings into the mirror before creating the key | A user with contact Write can only DENY SERVICE through the mirror: squatting an oid makes that person's create get 412 with no secured binding → deny + error log (detectable); clearing a mirror re-allows a duplicate, which the ambiguity deny catches. Never a takeover — the binding itself stays locked |
| B1 | Keep the key on `sprk_externalobjectid` (unsecured); add an FLS-secured **seal** column the BFF writes with the same oid; a binding is trusted only when both agree | Platform-enforced | Via the seal | One column; every READ path (5 readers) must compare both columns | Same DoS-only profile as B2, but every reader changes, and a reader that forgets the seal is a takeover |
| D | Keep FLS; move uniqueness to a new BFF-owned **lock table** (`sprk_identitybinding`, alternate key on oid; no user role may create or write it) | Platform-enforced | Unchanged | A new table + privileges + a two-step create with partial-failure repair | None beyond B2; heavier |
| A | Keep FLS; **drop the key**; create, then re-query by oid, with a deterministic loser-deletes rule (POML step 2b's own alternative) | NOT guaranteed: read-committed ordering can let two racers each see only their own row; the leftover duplicate is caught by the ambiguity deny and flagged, and the person is denied until an operator removes it | Unchanged | Smallest schema; extra create-path code | Likely to bite: a Teams/SPA client's first page load fires several API calls in parallel, so concurrent first sign-ins of ONE person are the common case, not an edge |
| C | Keep the key; **drop FLS** on `sprk_externalobjectid` | Platform-enforced | None on the binding | Smallest | The takeover the lock exists to prevent (any user with contact Write). Security-sensitive: not recommended |

- **Proposed path**: **B2**. It is the only option that keeps BOTH properties the task requires with a platform
  guarantee, changes no read path (the masking defence in §3.2 and every consumer in the link contract stay as they
  are), and limits what a user with contact Write can do to the mirror to a detectable denial of service.
- **Impact if accepted**: schema step (b) moves to the mirror column (and step (a) copies existing oids into it);
  `DataverseContactIdentityStore.BindPayload` / `CreatePayload` / `BuildCreateByKeyPath` write and address the
  mirror; `InMemoryContactIdentityStore` models the key on the mirror; a test pins that a squatted mirror denies
  rather than binds; the link contract §1.1 and guide §6.5.2 are rewritten. No consumer changes.
- **Alternatives considered and rejected**: A (not a guarantee, and the concurrent-first-sign-in case is the normal
  Teams load pattern), C (re-opens the takeover), B1 (moves the burden onto every reader), D (correct but heavier
  than B2 for the same guarantee).
- **Until decided**: `scripts/Set-ContactIdentityBindingSchema.ps1 -Apply` stops at a platform-rule preflight before
  any network write (proved: with the overlap it throws `BLOCKED … Nothing was written`; with the overlap removed in
  a temporary copy it proceeds to authentication). The dry run and `-Verify` report the overlap as `FAIL`. G-1, and
  every gate after it, wait for this decision.

Acceptance criteria this blocks: criterion 3 (exactly one contact per oid under concurrent first sign-ins — not
met as a deployable design) and the FLS authorization criterion (pending G-1/G-7 AND this decision).

## 10. Verifier fix round (`task/uac-r2-141-f1`, 2026-10-01) — what changed per finding

| # | Finding | Disposition |
|---|---|---|
| 1 | CRITICAL — alternate key and FLS on the same column | **Escalated, not decided** (§9). Schema script: platform-rule preflight that blocks `-Apply` (and reports in dry run / `-Verify`); header text. In-memory store doc states it models a combination the platform refuses. Link contract §1.1, guide §6.5.2, provisioning handoff, SPA-r2 023 closure and its note corrected (they stated the key as fact). |
| 2.1 | Truncated scan guard never shown to bite | Test `ATruncatedScan_SkipsFlagClearing_SoAFlagOfAnUnscannedUserSurvives` (seed F1). |
| 2.2 | Probe-Failed guard never shown to bite | `InMemoryContactIdentityStore.FailProbe`; test `AFailedMaskingProbe_AbortsTheRun_BeforeAnyDecision` (seed F2). |
| 2.3 | Cheap Verified path `IsActive` never shown to bite | Test `ALinkedUserWhoseContactIsInactive_IsFlagged_NeverReportedVerified` (seed F3). |
| 2.4 | Invite-flag re-evaluation never shown to bite | Tests `CollisionStillHolds_AnInviteRefusal_…` (pure) and `AnInviteRefusalFlag_IsKept_WhileTheContactStillBelongsToAnEmployee` (job) (seed F4). |
| 3 | One flag per contact: a second collision vanishes when the first is resolved | **Fixed for both cases.** A flag now records EVERY colliding party (`CollisionParty`; new memo column `sprk_identitycollisionparties`, JSON; the four summary columns stay the first party). Idempotence is per party (identity + reason), so a retry still writes nothing while a DIFFERENT identity is appended. Pass 2 evaluates each party and goes through the pure `ContactBindingDecision.ReconcileFlag`: clear only when no party holds AND no systemuser collided with the contact this run (the verifier's rule), prune otherwise. Appends, prunes and clears are conditional on the row version (a party appended meanwhile survives; a clear without a version is refused). A flag whose parties column cannot be read, or that holds 20 parties, is never overwritten or cleared by the job (an operator clears it after resolving — guide §6.5.3). Seeds F5–F9, F11, F12. |
| 4 | Inline link writes ungated before the G-5 review | **Gated.** `WorkforcePrincipalResolver.TryLinkSystemUserAsync` does nothing (no read, no write) unless `IdentityLink:Reconciliation:WritesEnabled` is true — the job's own switch, read through one helper (`ContactIdentityBinder.LinkWritesEnabled`). Not gated, with reasons (binder XML doc, guide §6.5.2): the Type-2 first sign-in (gating it would deny every Type-2 caller) and the registration link (operator-initiated, in a target environment the job never scans — a gate would leave that user unlinked with no safety net; G-5 note: do not register into spaarkedev1 during the review). Test `BeforeTheOwnerEnablesLinkWrites_ASignInLinksNothing_…` (seed F10). |
| 5 | D-3 mixing must be stated in the PR | Stated in §11 and in the link contract §5. |
| 6, 7, 9 | Verified OK / tests / paper trail | Nothing to fix. 9's remark that SPA-r2 023 is "completed" while 141 is not live: status kept (POML step 10 asked for it) with a `<status-note>` saying the code is delivered but not live, and the closure text corrected. |
| 8, 15 | Publish size | Not measured in this run (instructed; the main session measures after merging). |
| 10, 11 | Criterion 3 and the FLS criterion | Blocked on §9. |
| 12 | Reconciliation criterion | Met in code: the single-flag gap is fixed (finding 3) and the truncated-scan, probe-Failed and invite-flag guards are now shown to bite. |
| 13 | `acct` / tenant setting | Re-checked READ-ONLY 2026-10-01: `az ad app show --id 1e40baad-…` → access-token optional claims `email`, `preferred_username`, `upn`; **`acct` still not configured**; `signInAudience = AzureADMultipleOrgs`, v1 tokens. The Teams package's `webApplicationInfo.id` is this registration, so Teams SSO tokens are access tokens FOR it, and Microsoft documents that access-token optional claims "apply to access tokens requested for the application … no matter how the client accesses your API" ([optional claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference); [Teams tab SSO](https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/authentication/tab-sso-overview): "Teams requests Microsoft Entra endpoint for the access token"). `acct` is listed for JWT in v1.0 and v2.0 ("member … `0` … guest … `1`"). **Escalation trigger 3 does not fire**; the claim is configuration (G-2), the dev setting is G-3 — both pending manual gates, no change to the app registration was made. |
| 14 | Live gates | Pending (G-1 now blocked on §9). The G-5 count drift is removed by finding 4's gate. |

### 10.1 Seeded violations (fix round) — each seeded alone, built, the identity-binding set run, source restored

Harness: `f1_seed.py` (scratchpad) — for each seed: patch ONE source line, `dotnet test` the identity-binding set
(227–228 tests), record the failures, restore, assert the file is byte-identical. All twelve went RED.

| # | Seeded violation (production code) | Failing test(s) |
|---|---|---|
| F1 | job: pass 2 runs after a TRUNCATED scan (`\|\| report.UserScanTruncated` dropped) | `ATruncatedScan_SkipsFlagClearing_SoAFlagOfAnUnscannedUserSurvives` |
| F2 | job: a `Failed` masking probe treated as readable | `AFailedMaskingProbe_AbortsTheRun_BeforeAnyDecision` |
| F3 | binder: the cheap Verified path ignores `IsActive` | `ALinkedUserWhoseContactIsInactive_IsFlagged_NeverReportedVerified` |
| F4 | decision: invite-refusal re-evaluation forced false | `AnInviteRefusalFlag_IsKept_WhileTheContactStillBelongsToAnEmployee`, `CollisionStillHolds_AnInviteRefusal_HoldsWhileTheContactIsAnEmployees_AndClearsOnceItIsNot` |
| F5 | decision: a flag is cleared although a systemuser collided with the contact this run | `AFlagIsKept_WhileAnyDecidedSystemUserCollidesWithTheContact_EvenIfThatPartyIsNotRecorded`, `ReconcileFlag_ClearsOnlyWhenNoPartyHolds_AndNoSystemUserCollidedThisRun` |
| F6 | decision: single-slot flag (a second identity is not recorded) | `ASecondSystemUsersLiveCollision_SurvivesTheFirstOnesResolution`, `ATokenPlaneCallersLiveCollision_SurvivesASystemUsersResolution`, `ASecondIdentityCollidingWithAFlaggedContact_IsRecorded_WithoutOverwritingAConcurrentParty`, `ShouldWriteFlag_IsIdempotentPerParty_AndRecordsASecondIdentity` |
| F7 | store: the reader ignores `sprk_identitycollisionparties` | `ParseContactRow_ReadsEveryRecordedParty`, `ParseContactRow_APartiesColumnItCannotTrust_MarksTheFlagUnreadable_NeverDropsIt` |
| F8 | binder: an append that lost a race (412) is not retried | `ASecondIdentityCollidingWithAFlaggedContact_IsRecorded_WithoutOverwritingAConcurrentParty` |
| F9 | job: the clear re-reads the row and uses the FRESH version instead of the verdict's | `AFlagClear_IsConditionalOnTheVersionItWasDecidedOn_SoAPartyAppendedMeanwhileSurvives` |
| F10 | resolver: the inline link ignores the rollout switch | `BeforeTheOwnerEnablesLinkWrites_ASignInLinksNothing_ExactlyLikeTheReportOnlyJob` |
| F11 | decision: a flag with unreadable parties is reconciled anyway | `ReconcileFlag_KeepsAFlagWithUnrecordedParties`, `ParseContactRow_APartiesColumnItCannotTrust_…` |
| F12 | job: a prune is written for a row read without a version (falls back to an unconditional write) | `AFlagReadWithoutARowVersion_IsNeverPruned` |
| G-1 | schema script: the alternate-key/FLS overlap removed (temp copy) | the run passed the preflight and reached authentication; with the overlap it throws `BLOCKED … Nothing was written` before any request |

**Suite results (fix round, final code)**: identity-binding set 228 passed / 0 failed; full BFF suite
(`tests/unit/Sprk.Bff.Api.Tests`) **13,368 passed / 0 failed / 54 skipped (13,422)**; NetArchTest
(`tests/Spaarke.ArchTests`) **337 / 0 / 0**. No package or csproj change.

F9 was GREEN on its first run: the test staged the concurrent append INSIDE the clear call, which a fresh re-read
just before the clear also misses. The test was corrected to stage the append between the flag scan and the clear
(during pass 2's evidence reads, through a new `InMemoryContactIdentityStore.AfterRead` hook); re-seeded, it went RED.

## 11. Statements the PR must carry

- **D-3 / owner I2 = (1), stated explicitly (verifier finding 5).** `IdentityNormalizationService` still honours a
  licensed user's existing `sprk_primarycontact` even when that contact is flagged as bound to a different oid, and
  `AccessibleRecordSetService`'s contact-grant term then loads that contact's external grants into the user's
  accessible set. In dev that is ralph.schroeder@spaarke.com → contact 8e9918a9, bound to a CIAM oid: his set
  includes that CIAM identity's grants. This is the mixing the POML's rejection of option (b) warned about. It is
  pre-existing (the link predates task 141), the owner chose to keep links rather than clear them (I2 = (1)), and it
  ends when an operator resolves the collision. `CallerContactResolver` ("assign it to me") does NOT follow such a
  link.
- **§6.5 / §6 escalation open**: §9 (alternate key vs FLS). G-1 and every later gate wait for it.
- **Inline link gated** on `IdentityLink:Reconciliation:WritesEnabled` (finding 4); Type-2 binding and the
  registration link are not, with reasons.
- **New schema in this round**: `contact.sprk_identitycollisionparties` (memo, 4000) — justification below.
- **Publish size**: not measured here; the main session measures against fresh master after merging.

**Three-question justification for the new surface of this round (CLAUDE.md §11):**

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `contact.sprk_identitycollisionparties` (memo) + `CollisionParty` / `FlagReconciliation` types | The four `sprk_identitycollision*` columns (one party only) and `CollisionFlag` | **Extended**: `CollisionFlag` keeps its four summary members and gains the other parties; the four columns keep their meaning (the first party). A new column was needed because a text(100) oid column cannot hold a list, and re-purposing it would break the operator view | A second identity colliding with a flagged contact is never recorded; when the first collision is resolved the job clears the flag and a live collision — for a Type-2 or CIAM caller, one nothing else will ever re-raise until they sign in again — vanishes from the operator's view (finding 3) |
| `ContactIdentityBinder.LinkWritesEnabled` (static helper, no DI) | `IdentityLinkReconciliationJob.WritesEnabled` (the job's private parse of the same key) | **Extended**: the job's parse moved to one shared helper; the job now calls it | The inline link writes R1-class links ahead of the report-only review the switch exists for (finding 4) |

Placement (CLAUDE.md §10): unchanged — all in the BFF (bff-extensions.md §A); no new service, DI registration,
endpoint, option, job or package. `WorkforcePrincipalResolver` gains an `IConfiguration` constructor dependency
(already registered).

## 12. Second verifier fix round (`task/uac-r2-141-f2`, 2026-10-01) — what changed per finding

> Branch `task/uac-r2-141-f2` from `task/uac-r2-141-f1`. Owner answers in force unchanged (I1 = (b), I2 = (1), T2).
> READ-ONLY for live systems: the only live call was `az ad app show` (below). Nothing was written to Dataverse,
> Entra or Azure. Publish size not measured (the main session measures after merging).

| # | Finding | Disposition |
|---|---|---|
| 1 | BLOCKER — no deployable mechanism for criterion 3 (alternate key vs FLS on `contact.sprk_externalobjectid`) | **Still blocked on the owner decision in §9** (recommendation B2). Nothing implemented; an unanswered escalation is a first-class stop. Fail-closed meanwhile: `KeyMissing` → `contact_create_unavailable`; `Set-ContactIdentityBindingSchema.ps1 -Apply` throws `BLOCKED` before any write. |
| 2 | BLOCKER — FLS criterion + manual live gate wait on G-1/G-7/G-8 | Unchanged: pending gates (§8), G-1 behind §9. |
| 3 | Not met — publish size (G-9); `acct` (G-2) and the dev tenant setting (G-3) pending | Unchanged: pending gates. `acct` re-checked READ-ONLY today: `az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c` → access-token optional claims `email`, `preferred_username`, `upn`; **`acct` still not configured**; `AzureADMultipleOrgs`; `requestedAccessTokenVersion` null (v1). Escalation trigger 3 still does **not** fire (the claim is issuable for this registration's access tokens, Teams SSO included — sources in §1); applying it is G-2. The app registration was not changed. |
| 4 | LOW — the registration link promised a retry that does not exist outside the BFF's own environment | **Fixed (wording + log) and the gap recorded.** `RegistrationDataverseService.IsReconciledByThisBff(url)` compares the link environment with this BFF's `Dataverse:ServiceUrl` (the only environment `IdentityLinkReconciliationJob` scans, through the DI store). A link that does not land (fault, deny, lost race, or a collision flag) now logs one of two warnings: in the BFF's own environment "…re-decides the user on its next run once `IdentityLink:Reconciliation:WritesEnabled` is true"; anywhere else "…NOT retried by this BFF: its identity-link reconciliation job scans only its own Dataverse:ServiceUrl (…)". The XML doc says the same. The binder's `SystemUserLinkOutcome.Failed` doc, its link-not-written log line and its switch remarks no longer claim a retry either. **Recorded gap (not a retry):** see §12.1. |
| 5 | LOW — the invite lookup failure's reason code was dropped (thrown into the generic 500) | **Fixed.** `InviteContactAction.Fail` (email lookup OR the systemuser-reference lookup unreadable) returns `InviteLookupFailure` → HTTP **503** ProblemDetails, `reasonCode` `sdap.access.invite.contact_lookup_failed`, `detail` = `ContactIdentityBinder.InviteMessage(code)` (no longer dead), on BOTH `/invite` and `/invite-and-grant`; nothing is created, bound, flagged or granted. 503 rather than 500: nothing was decided and a retry is safe; the client (`AccessGrantModal`) reads `reasonCode`/`detail` whatever the status. Both endpoints declare `ProducesProblem(503)`. Any other unexpected decision state still fails closed (throw → generic 500, nothing created). |
| 6 | LOW — two answers to "which contact is bound to this oid?" | **Fixed.** The shared read is now `ContactBindingDecision.ContactsBoundToQuery` (renamed from `ActiveContactsBoundToQuery`): EVERY statecode, two rows, `statecode` selected — the same question as the binder's `FindContactsByOidAsync`. `IdentityNormalizationService`'s fallback and `CallerContactResolver` answer it with `DecideBoundContact` (the binder's own D1–D4 step, made public) over `BoundContactLookup(entities)`. One active + one inactive contact on an oid → no contact (`contact_oid_ambiguous`) in both readers, as in the binder; a sole inactive contact → none (`CallerContactResolver` reports `inactive-contact`, a new identifier-only reason; `ContextBinder` only logs it). Readers still write nothing; the flag is the binder's. |
| 7 | INFO — Ralph's flagged, kept link is still honoured (D-3) | Unchanged by design (owner I2 = (1)); stated for the PR in §11 and link contract §5. Repeated in §12.2. |
| 8 | INFO — job cost on a report-only stamp; a business unit created later needs a re-run | Recorded: guide §6.5.2 now states the cost (≈2–4 reads per unlinked user per 5-minute run while writes stay off; enable writes after the review on a large stamp). The later-BU re-run was already documented (script header, guide §6.5.2). No code change. |
| 9, 10 | VERIFIED | Nothing to do. Finding 9's note on the redundant `>1` branch is moot: that code was replaced by `DecideBoundContact` (finding 6). |
| 11 | Criterion 3 not met | = finding 1 (§9). |
| 12 | FLS criterion not met | = finding 2 (G-1/G-7, behind §9). |
| 13 | Manual live gate not met | = finding 2 (G-8, behind G-1). |
| 14 | `acct` / customer-tenant criterion not met | = finding 3 (G-2, G-3). Script, template and guide parts are in place. |
| 15 | Suites / publish size / CVE / placement | Suites re-run (§12.5). Publish size: G-9, not measured by instruction. No package change, so no new CVE surface. Placement unchanged (all in the BFF; §12.4). |

### 12.1 Recorded gap — a registration link outside the BFF's own environment has no retry in this BFF

> ✅ **CLOSED in the third fix round (§13.2)**: the identity-link job now reconciles every environment this BFF
> provisions users into. The text below is the gap as it stood after the second round.

- **What**: `RegistrationDataverseService.CreateSystemUserAsync` links a just-created systemuser in the TARGET
  environment (`targetDataverseUrl`; for demo provisioning, `DemoEnvironmentConfig.DataverseUrl`). If that link
  does not land, nothing in this BFF re-decides it, and a collision flag written there is never re-evaluated or
  cleared by this BFF's job, because the job scans only `Dataverse:ServiceUrl`.
- **Who covers it**: a BFF deployed against that environment — its own `identity-link-reconciliation` job (with
  `IdentityLink__Reconciliation__WritesEnabled=true`), or the inline link at the user's first sign-in through it.
  In the D-13 per-customer stamp model every customer environment has its own BFF, so the gap is confined to an
  environment that no BFF serves (e.g. a demo environment provisioned from another stamp while its own BFF is
  stopped — `config/environments.json` notes the prod/demo BFF as stopped).
- **Why not a retry here**: an in-process retry only covers a transient blip, and making this BFF's job scan
  every provisioning target is a new multi-environment design (per-environment tokens, which environments, which
  stamp owns them) that the owner has not asked for. Recorded rather than built; the operator signal is the
  `NOT retried by this BFF` warning (guide §6.5.2). If the owner wants a safety net, the cheapest option is a
  bounded retry of the registration link on a fault/lookup-failure; the complete one is a job that scans the
  `sprk_dataverseenvironment` registry — an owner choice, not taken here.

### 12.2 Statements the PR must carry (in addition to §11)

- §11 D-3 (verifier finding 7): Ralph's flagged, kept `sprk_primarycontact` (contact 8e9918a9, bound to CIAM oid
  6a9fa229) is honoured by `IdentityNormalizationService`, so `AccessibleRecordSetService` loads that CIAM
  identity's grants into his internal access set until an operator resolves the collision. Owner I2 = (1).
- §12.1: the registration-link gap outside the BFF's own environment.
- The invite lookup failure is now HTTP 503 (was a generic 500) — a wire change on `/invite` and `/invite-and-grant`.
- `ActiveContactsBoundToQuery` → `ContactsBoundToQuery` (public rename; no caller outside the two readers).
- Report-only cost on a stamp left with writes off (§12, finding 8).

### 12.3 Seeded violations (second fix round) — each alone, built, the affected classes run, source restored

Harness `f2_seed.py` (scratchpad): patch ONE source site, `dotnet build`, `dotnet test` over
RegistrationContactLink / CallerContactResolverSeam / IdentityNormalizationService / ContactBindingDecisionTests /
ExternalAccessContractTests (143 tests), record failures, restore byte-identical. All eight went RED.

| # | Seeded violation | Failing test(s) |
|---|---|---|
| S1 | registration: every environment treated as reconciled by this BFF | `ALinkThatDoesNotLand_InATargetEnvironment_IsReportedAsNotRetried`, `ALinkThatFaults_InATargetEnvironment_IsReportedAsNotRetried` |
| S2 | registration: the fault path logs the old "the reconciliation job retries it" | `ALinkThatFaults_InATargetEnvironment_IsReportedAsNotRetried` |
| S3 | invite: `Fail` thrown into the generic 500 again | `Invite_WhenTheContactLookupCannotBeRead_Returns503_…` ×2 routes, `Invite_WhenTheSystemUserReferenceLookupCannotBeRead_Returns503_…` ×2 routes |
| S4 | `/invite-and-grant` ignores the lookup failure | the same two theories, `/invite-and-grant` rows |
| S5 | the shared read filters `statecode = 0` again | `TheSharedReadOnlyQuery_ReadsTwoRowsInAnyState_ByTheBindingColumn` + 9 seam/normalization tests (10) |
| S6 | normalization counts only active rows | `ResolveAsync_AnActiveAndAnInactiveContactOnTheUsersOid_DerivesNoContact` |
| S7 | "assign it to me" counts only active rows | `ResolveAsync_AnActiveAndAnInactiveContactOnTheOid_IsAmbiguous_NeverTheActiveOne`, `ResolveAsync_TheOnlyContactOnTheOidIsInactive_ReturnsItsOwnUnresolvedReason` |
| S8 | an inactive bound contact reported as `no-matching-contact` | `ResolveAsync_TheOnlyContactOnTheOidIsInactive_ReturnsItsOwnUnresolvedReason` |

New tests (KEEP paths; no `Mock<HttpMessageHandler>`, no DI-registration test, no ctor null-check test, no
`InternalsVisibleTo`): `RegistrationContactLinkTests` (+4, a `CapturingLogger` at the real logging boundary and a
`ContactIdentityBinderFactory` subclass that throws — no HTTP double), `ExternalAccessContractTests` (the old
500 test replaced by two theories, 4 cases), `ContactBindingDecisionTests` (query test rewritten, +3 pure),
`CallerContactResolverSeamTests` (+2; the strict double now rejects a `statecode` filter),
`IdentityNormalizationServiceTests` (+2; same).

### 12.4 Placement and justification (CLAUDE.md §10 / §11)

No new service, DI registration, endpoint, option, job or package. All changes are in the BFF (bff-extensions.md
§A), inside existing components. New public surface, each a modification of an existing component:

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `RegistrationDataverseService.IsReconciledByThisBff` | the job's environment = `DataverseContactIdentityStore.ForDefaultEnvironment` (`Dataverse:ServiceUrl`) | Extends the existing service; reads an already-configured key | Operators are told a failed link is retried when it never is (finding 4) |
| `InviteExternalUserEndpoint.InviteLookupFailure` + `LookupFailureResult` | `InviteRefusal` + `RefusalResult` | Sibling of the existing refusal pair (a failure is not a refusal: different status, no flag) | The distinct reason code never reaches the client; `InviteMessage`'s lookup text stays dead (finding 5) |
| `ContactBindingDecision.ContactsBoundToQuery` / `BoundContactLookup` / `DecideBoundContact` | `ActiveContactsBoundToQuery` (renamed), the private `DecideOnOid` | Extends: the private D1–D4 step is exposed, not copied | Readers and the binder answer the same data differently (finding 6) |
| `CallerContactResolution` reason `inactive-contact` | the identifier set `no-matching-contact` / `ambiguous-binding` / `lookup-failed` | Extends the identifier set | An inactive bound contact is indistinguishable from "no contact" in the trail |

### 12.5 Suite results (second fix round, final code)

- Identity/access subset (IdentityBinding, WorkforceEmailNoHijack, DataverseContactIdentityStore,
  ExternalAccessContractTests, RegistrationContactLink, WorkforcePrincipalResolver, CallerContactResolverSeam,
  IdentityNormalizationService, ContactAadObjectIdColumnGuard, CallerPrincipalResolver, AccessibleRecordSet):
  **354 passed / 0 failed** (the verifier's 340 + 14 new).
- Full BFF suite (`dotnet test tests/unit/Sprk.Bff.Api.Tests`, which also compiles the `tests/integration/**`
  KEEP paths): **13,382 passed / 0 failed / 54 skipped (13,436)** — the f1 round's 13,368 / 13,422 plus 14 new.
- NetArchTest (`dotnet test tests/Spaarke.ArchTests`): **337 / 0 / 0**.
- No package or csproj change (no new CVE surface). Publish size not measured (G-9; the main session measures
  after merging).

## 13. Third fix round (`task/uac-r2-141-f3`, 2026-10-02) — B2 built, the registration-link gap closed

> Branch `task/uac-r2-141-f3` from `task/uac-r2-141-f2`, then a merge of `integ/uac-r2-batch2` (origin/master,
> tasks 130/131/134/151, 109/135/136/144/145). Owner answers in force: round 4 item 4 = **B2**; live steps approved
> (run by the main session after deploy). READ-ONLY for live systems in this round: the only live calls were the
> read-only schema dry run against spaarkedev1, a read-only registry/app-user check in spaarkedev1 and spaarke-demo,
> and an App Service settings read filtered to two non-secret keys. Nothing was written to Dataverse, Entra or Azure.

### 13.0 Merge of `integ/uac-r2-batch2` (five conflicts, both sides kept)

| File | Resolution |
|---|---|
| `CallerPrincipalResolver.cs` | `CiamContactPrincipalStrategy` takes identity from the binder (141: distinct deny codes) AND record scope from the unified evaluator (135/136: read-bearing entries only). `ExternalParticipationService` is no longer a dependency of the strategy (neither half uses it); 141's `RightsFromGrants` left with the grants-only path |
| `ExternalAccessModule.cs` | both scheduled jobs (141 identity-link reconciliation, 144 Secure Record isolation census) |
| `RegistrationDataverseService.cs` | both `using`s |
| `ExternalAccessContractTests.cs` | 136's `DataReads` stub + 141's header-driven identity store + 135/136's veto/plane wiring |
| `CallerPrincipalResolverTests.cs` | 135's evaluator-scope tests over 141's binder (contact named by an in-memory identity store) |

Non-conflicting compile breaks fixed in the same merge commit: `UnifiedEvaluatorSeamTests` and
`OrganizationMembershipReadTests` (135) resolved the CIAM contact through `ExternalParticipationService`, whose contact
resolution 141 removed — they now name the contact in an in-memory identity store through the binder;
`RegistrationSecureRecordPlacementTests` (144) passes the binder factory 141 added. Identity/access subset after the
merge: 519 / 0.

### 13.1 Owner round 4 item 4 = B2 — implemented (the §9 "Impact if accepted" list, item by item)

| §9 impact item | Done |
|---|---|
| Schema step (b) on the mirror | `Set-ContactIdentityBindingSchema.ps1`: `$KeyAttributes = @($MirrorColumn)`; key `sprk_ExternalObjectIdUniqueKey` on `contact(sprk_externalobjectidkey)` (renamed from the never-applied `sprk_ExternalObjectIdKey`, which would have shared its logical name with the new column). FAIL for any key on the secured binding |
| Step (a) copies the 6 existing bindings into the mirror before the key | (a) creates `sprk_externalobjectidkey` (Text 100, never secured — FAIL if someone secures it), normalises each binding to "D" and copies it into the mirror in one PATCH; a mirror its own binding does not carry is reported FAIL and never written; a copy that would collide with another contact's mirror stops the run before the key |
| `BindPayload` / `CreatePayload` / `BuildCreateByKeyPath` write and address the mirror | `BindPayload` writes binding + mirror + plane; `CreatePayload(oid, plane, details)` carries the binding (the key column's value is the URL's); `BuildCreateByKeyPath` = `contacts(sprk_externalobjectidkey='<oid>')`. New: `IsDuplicateKey` (exact `0x80060892`, captured live by email-communication-intelligence-r2 task 020) → `StoreWriteStatus.KeyConflict`, checked before the generic 412 |
| `InMemoryContactIdentityStore` models the key on the mirror, no longer a key+FLS combination the platform refuses | `Contact.KeyMirror`; create is create-only on the mirror; bind fails with `KeyConflict` when another contact holds the oid in its mirror; masking hides only the binding; seeding a binding seeds its mirror (step (a)'s copy); `keyMirror:` seeds a squat. Its doc no longer carries the ⚠️ |
| A test pins that a squatted mirror DENIES (never binds) | `WorkforceEmailNoHijackTests.ASquattedMirror_DeniesTheCreate_…`, `…_DeniesTheEmailBind_TheMatchedContactStaysUnbound`, `…_RetriedSignIn_WritesNoSecondFlag`; `CiamContactBindingTests.TheRepairBind_AgainstASquattedMirror_…`; `CallerPrincipalResolverTests` (key-conflict deny code); `IdentityLinkReconciliationTests.ASquattedMirror_IsFlaggedNotCreatedAround_…` and `AReportOnlyRun_NeverReportsAHeldKeyMirrorFlagAsClearable` |
| The schema script's platform-rule preflight passes | Shown 2026-10-02: the real script with `-Apply` against an unreachable URL passes the preflight and stops at authentication (`No Dataverse token`); a seeded copy with the key back on the binding throws `BLOCKED … Nothing was written` before auth. Read-only dry run against spaarkedev1: `OK no alternate-key column is field-secured`, 6 copies WOULD, key WOULD. Plus a source guard, `ContactIdentitySchemaAgreementTests`: the script keys exactly `ContactBindingDecision.KeyMirrorColumn`, secures `ExternalObjectIdColumn`, never both on one column, and its choices cover every `IdentityCollisionReason` / `IdentityPlaneMarker` value |
| Rewrite link contract §1.1 and guide §6.5.2 | Done (contract §1, §1.1, §3, §4, §5, new §7; guide §6.5.2, §6.5.3, change log). Also SPA-r2 023 closure + note, the provisioning handoff, DATAVERSE-WRITE-PATH-ARCHITECTURE I-10 |

**What a squat does (decision table addition).** K1 — a bind the index refuses (another contact holds the oid in
its mirror): never retried as a race; the holder(s) are read by the mirror (diagnostic read only —
`FindContactsByKeyMirrorAsync`; nothing resolves by it), flagged with reason `KeyMirrorConflict` (`…011`), and the
caller is denied `sdap.access.deny.contact_key_conflict` (error-level log naming the holder). K2 — a create refused
by the key, re-read by the binding with no contact found, refused AGAIN: same outcome. A first 412 on a create is
still a race (a concurrent first sign-in won) and resolves the winner. A key-conflict flag holds while the holder's
mirror carries the oid without a matching binding — **even if the holder is deactivated** (the unique index counts
inactive rows), so it is evaluated before the inactive shortcut, from the holder row alone (also in a report-only
run, which attempts no write and so cannot see the conflict). Operator procedure: guide §6.5.3 "Key mirror held".

**Criterion 3 ("exactly one contact per oid, two concurrent first sign-ins") — MET in code**, platform-enforced
once G-1 creates the key: the create is create-only on the mirror's unique index, the binding lands in the same
request, the race test passes over a store that now models a combination Dataverse accepts, and a squat or an
undefined key DENIES instead of creating a second contact. It is live after G-1.

### 13.2 The registration-link gap (§12.1) — CLOSED, not recorded

The second round recorded that a registration link which did not land in a target environment other than the BFF's
`Dataverse:ServiceUrl` was never retried, and a flag written there never re-evaluated. Now:

- **`IdentityLinkReconciliationJob` reconciles every environment this BFF provisions users into** — its own
  environment, then `DATAVERSE_URL` (registration's default target) and every ACTIVE `sprk_dataverseenvironment` row
  (`ProvisioningTargetUrls`: normalised, de-duplicated, never the own environment). Each target gets the full
  probe → pass 1 → pass 2 through `RegistrationDataverseService.ContactBinderFor(url)` — the registration service's
  existing per-environment token path (the BFF's managed identity; ADR-028: no new secret, no caller token). The same
  report-only switch gates every environment. One environment's failure (unreachable, scan failed, probe failed)
  fails the run (`[env]`-prefixed problems; `provisioningTargets.environments[]` in the ResultJson) but never stops
  the others; an unreadable registry fails the run while `DATAVERSE_URL` is still reconciled.
- **Why that covers every registration-created user**: the approve endpoint provisions only into an ACTIVE registry
  row (`RegistrationEndpoints` refuses an inactive one), and with no target a user is created in `DATAVERSE_URL`. So
  every environment a registration links in is one the job reconciles; the only way out is an operator deactivating
  the row afterwards — which stops this BFF serving that environment, deliberately.
- **`RegistrationDataverseService`**: `IsReconciledByThisBff` and the two-template "NOT retried" warning are gone;
  a link that does not land logs one warning — "re-decides the user on its next run … every environment it
  provisions users into". `ContactBinderFor(url)` is the one place a binder for another environment is built.
- `DataverseEnvironmentService.GetActiveEnvironmentsAsync` became `virtual` — the test seam (ADR-038 B1: no HTTP
  double), the `ExternalParticipationService` convention.

**Live finding (read-only, 2026-10-02) — a G-1b decision for the main session/owner.** In dev the only provisioning
target is "Demo 1" (`https://spaarke-demo.crm.dynamics.com`), and there the dev BFF's identities are NOT application
users and the binding schema does not exist. Every run will therefore report spaarke-demo as a failed environment —
accurately (the dev BFF cannot register into Demo 1 either). Options and recommendation are in §8 G-1b. This is not
one of the round-4 approved live steps (a different environment), so nothing was done.

### 13.3 Other findings of the second-round verdict

| Finding | Disposition |
|---|---|
| INFO — the 503 contract test could not tell the decision's message from the endpoint's hard-coded fallback (identical text) | Fixed: the fallback is now distinct ("The invite could not be completed. …") and the test asserts equality with `ContactIdentityBinder.InviteMessage(InviteContactLookupFailed)`. Seed B18 |
| Registration tests assert on log substrings (brittle, accepted by the verifier) | Unchanged; the operator signal IS the log line (guide §6.5.2) |
| Criterion 3 not met | Met in code (§13.1); live after G-1 |
| Goal clause "every active licensed systemuser is linked or flagged" not guaranteed for registration-created users in other environments | Closed (§13.2) |
| FLS criterion, manual live gate, `acct`/tenant setting, publish size | Live gates G-1..G-9 (owner-approved; main session) — not executed here. Publish size: the main session measures |

### 13.4 Placement and justification (CLAUDE.md §10 / §11)

No new service, DI registration, endpoint, option, scheduled job or package; all in the BFF (bff-extensions.md §A),
inside existing components. The existing job gains a pass; the existing store/binder/decision gain members.

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `contact.sprk_externalobjectidkey` + key `sprk_ExternalObjectIdUniqueKey`; choice option `100000011` | `sprk_externalobjectid` (field-secured binding); the 11 reasons | Owner-decided (B2). The binding column cannot carry the key (platform rule) | Criterion 3 has no enforcing mechanism; every create denies `contact_create_unavailable` |
| `ContactBindingDecision.KeyMirrorColumn` / `MirrorHeldWithoutBinding` / `DenyContactKeyConflict` / `IdentityCollisionReason.KeyMirrorConflict`; `ContactBindingRow.RawKeyMirror` | `ExternalObjectIdColumn`, `ReadBinding`, the deny-code set, the reason enum | Extends each (one constant, one pure predicate, one code, one value, one optional row field) | A squat is indistinguishable from a race or a plain failure: no flag, wrong code, a report-only run promises to clear a live conflict |
| `StoreWriteStatus.KeyConflict`, `IsDuplicateKey`, `FindContactsByKeyMirrorAsync` / `BuildKeyMirrorLookupPath`; `CreatePayload(oid, …)` | `PreconditionFailed`, `IsKeyMissing`, the oid lookup | Extends the store's status set, classifier set and read set | A duplicate-key 412 is retried as a version race, and the holder cannot be named or flagged |
| `RegistrationDataverseService.ContactBinderFor` (replaces `IsReconciledByThisBff`) | `_binderFactory.CreateBinder` + `GetAccessTokenForUrlAsync`, inline in the link method | Extracts the existing expression so the job reuses the same token path | The job would need a second per-environment token path (ADR-028 says reuse) |
| `IdentityLinkReconciliationJob` provisioning-target pass + `ProvisioningTargetUrls` | the job's own-environment passes; `DemoExpirationService`'s use of the same registry | Extends the existing job (same reason to change: identity links), same safety convention | Registration-created users in other environments are never retried or flagged (the verifier's open gap) |
| `DataverseEnvironmentService.GetActiveEnvironmentsAsync` → `virtual` | — | Modification (test seam) | The target pass could only be tested through an HTTP double (banned) |

### 13.5 Seeded violations (third fix round) — each alone, built, the identity subset run, source restored byte-identical and touched

Harness `f3_seed.py` (scratchpad). Every seed went RED:

| # | Seeded violation (production code or script) | Failing test(s) |
|---|---|---|
| B1 | bind payload omits the mirror | `TheBindPayload_WritesTheOidInDFormat_IntoTheBindingAndTheMirror_…` |
| B2 | create addressed by the secured binding again | `TheCreate_IsAddressedByTheMirrorKey_NeverTheFieldSecuredBinding` |
| B3 | create payload omits the binding | `TheCreatePayload_CarriesTheBindingAndThePlane_…` |
| B4 | contact reads stop selecting the mirror | `EveryContactRead_SelectsTheBindingAndTheMirror_…` |
| B5 | duplicate-key fault not recognised | `IsDuplicateKey_RecognisesTheUniqueIndexFault_AndNothingElse` |
| B6 | token-plane email bind treats a key conflict as bound | `ASquattedMirror_DeniesTheEmailBind_…`, `TheRepairBind_AgainstASquattedMirror_…`, `CiamStrategy_EachDeny_…(key-conflict)` |
| B7 | a create the key refuses twice is a plain create failure (no flag) | `ASquattedMirror_DeniesTheCreate_…`, `ASquattedMirror_RetriedSignIn_…` |
| B8 | the holder is never flagged | 5 squat tests (token, CIAM, job) |
| B9 | a systemuser create refused twice is re-decided forever | `ASquattedMirror_IsFlaggedNotCreatedAround_…` |
| B10 | a mirror matching its own binding counts as a squat | `MirrorHeldWithoutBinding_…`, `CollisionStillHolds_AKeyMirrorConflict_…`, `ParseContactRow_ReadsTheBindingTheFlagAndTheETag` |
| B11 | an inactive holder frees the key-conflict flag | `CollisionStillHolds_AKeyMirrorConflict_HoldsWhileTheHolderKeepsTheSlot_EvenWhenInactive` |
| B12 | the job judges a key-conflict party by pass-1 decisions | `AReportOnlyRun_NeverReportsAHeldKeyMirrorFlagAsClearable` |
| B13 | the job skips every provisioning target | 6 target tests |
| B14 | the own environment is not excluded from the targets | `ProvisioningTargetUrls_…`, `TheOwnEnvironmentInTheRegistry_IsReconciledOnce`, 2 more |
| B15 | one unreachable target stops the others | `OneUnreachableTarget_FailsTheRun_ButNeverStopsTheOthers` |
| B16 | an unreadable registry also drops `DATAVERSE_URL` | `TheRegistrationDefaultEnvironment_IsATargetEvenWhenTheRegistryCannotBeRead_…` |
| B17 | registration logs the old "NOT retried" again | `ALinkThatDoesNotLand_InAnyRegistrationEnvironment_…` (×2) |
| B18 | invite lookup failure answers with the endpoint fallback | the two 503 contract theories (×2 routes each) |
| B19 | schema script keys the field-secured binding again | `TheScript_KeysTheMirror_SecuresTheBinding_AndNeverBothOnOneColumn` |
| B20 | schema script lacks the key-conflict reason option | `TheScriptsChoices_CoverEveryValueTheBffWrites` |

Harness notes: the first run's B16 seed did not compile (an inserted `return;` made code unreachable under
warnings-as-errors) and its B19/B20 ran against binaries a preceding `.cs` seed had left stale; both were re-run with
a corrected harness that rebuilds before every test run (B12–B16, B19, B20 re-run; all RED on their own tests). The
schema preflight was also shown to bite directly (§13.1).

### 13.6 Step 9.5 (code-review + adr-check) on this round's diff

ADR-002 (no plugin; every write in the BFF), ADR-003 (a squat, an unreadable registry, an unreachable target all
deny or fail the run — never resolve, never clear a flag), ADR-010 (no new DI registration; the only interface is the
existing `IContactIdentityStore` seam; `GetActiveEnvironmentsAsync` virtual as the test seam), ADR-028 (the target
pass reuses the registration service's per-environment token path — the BFF's managed identity, no new secret, no
caller token, no Graph), ADR-036 A1 (no throw from `ExecuteAsync`; one target's fault fails the run, not the others),
ADR-052 (placement unchanged: in-process scheduler), ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration or
ctor null-check test; the schema guard reads the script as text) — no violation, no tension. Greps over the changed
`src` files for `Microsoft.Graph`, `WithClientSecret`, `IMemoryCache`, `BackgroundService`, `IPlugin`, new
`interface I…`, timers: none.

Review findings — fixed: the per-environment status was missing from the heartbeat and ResultJson (every
environment logged the RUN's status) → `environmentStatus` / `provisioningTargets.environments[].status`; the
probe-blocked message called an unreachable environment "masked" → it now distinguishes masking from "could not be
read (no access, no schema, unreachable)"; the registration test helper still set `Dataverse:ServiceUrl`, which the
service no longer reads → removed. Accepted with reasons: a repeated sign-in by a squatted identity costs two refused
create PATCHes, five reads and one error log each time (the flag write is idempotent; this is the same profile as
the other denies the first round accepted, and the log is the operator signal); `IdentityLinkReconciliationJob.cs`
grew to ~700 lines with the target pass (one reason to change — identity links — and the per-environment code is a
loop over the existing passes, not a second responsibility; CLAUDE.md §11.5); in dev every run reports spaarke-demo
as a failed environment until G-1b is decided (an accurate signal, not noise to suppress).

### 13.7 Suites (third fix round, final code)

- Identity/access subset (IdentityBinding, IdentityLink, CallerPrincipalResolver, UnifiedEvaluatorSeam,
  OrganizationMembershipRead, Registration, ExternalAccessContract, ContactIdentity, WorkforceEmailNoHijack,
  WorkforcePrincipal, AccessibleRecordSet, CallerContactResolver, IdentityNormalization, ContactAadObjectId,
  MembershipPaging, DataverseContactIdentityStore): **637 passed / 0 failed**.
- Full BFF suite (`dotnet test tests/unit/Sprk.Bff.Api.Tests`, which also compiles the `tests/integration/**` KEEP
  paths): **13,791 passed / 0 failed / 54 skipped (13,845)** — on top of the merged batch-2 base.
- NetArchTest (`dotnet test tests/Spaarke.ArchTests`): **341 / 0 / 0**.
- No package or csproj change in this task's diff (no new CVE surface). Publish size: not measured (G-9; the main
  session measures after merging).
