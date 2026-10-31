# 036 — Is OBO needed for SharePoint Embedded? (OBO resolution)

> Read-only research, 2026-10-19. Code read at `origin/master` 2b07480ab and at the 036 branch
> `origin/feat/uac-036-fr20-flag-gated-swap` e7a8392bb (PR #2078, open). No code, ADR or other file was changed.
> Answers the owner: *"for 036 you say remove OBO — BUT isn't this what is required for access to SharePoint Embedded?
> … don't change this until this is definitively resolved."*

## Summary (plain language)

1. **OBO** ("on behalf of") means the server swaps the user's sign-in token for a new token that lets it call another
   service *as that user*. The BFF does this in five places: two for SharePoint Embedded (SPE)/Graph, three for Dataverse.
2. **Option C does not touch SharePoint Embedded.** The check 036 would change asks *Dataverse* only ("may this person
   edit this record?"). Its token is for Dataverse (`CallerRecordAccessProbe.cs:570`), never for Graph or SPE.
3. Internal users' document access, file open, preview and Word/Office co-authoring do not use this check, and no option
   changes them. In-app open and preview already read SPE app-only (`FileAccessEndpoints.cs:21-30`). The Office edit runs
   as the user inside Office, with no BFF token swap.
4. **ADR-028 allows OBO for internal users** (Console, model-driven app, Office add-ins, Copilot agent; A4).
   It **forbids OBO on the SPA/Teams plane** (`/api/v1/external/*`; A1, A2, A3, A8 and A9).
5. **On master, nothing breaks the ADR.** Every one of the about 30 places that use `CallerRecordAccessProbe` today is
   on an internal route. No `/api/v1/external` route performs OBO on master.
6. **The conflict is new code on the 036 branch.** To confirm SPA/Teams writes, 036 calls the probe's OBO path for
   systemusers who signed in through Spaarke's tenant (`ExternalProjectDataEndpoints.cs:1038`, `:1056`).
7. That path runs only while the FR-20 flag is on, and the flag ships off. So nothing performs OBO on that plane in any
   deployed environment today. The rule still forbids the code as written.
8. **Correction:** my earlier framing ("the existing OBO code breaks the ADR; C removes OBO from this check") was wrong.
   The existing code is compliant. C only stops 036 from *adding* OBO to the SPA/Teams plane, and the probe's OBO keeps
   serving every internal route.
9. Option C answers the same question app-only, naming the user (the A5 mechanism). It is the mechanism 036 already uses
   for the user's read set, and for home-directory users' writes.
10. **Recommendation: C.** It needs no new rule and no new prerequisite, and it leaves SPE untouched. The owner decides C
    (comply) or B (amend ADR-028 to allow this one Dataverse OBO).

---

## 1. Every OBO exchange in the BFF (origin/master)

There are exactly five `AcquireTokenOnBehalfOf` call sites (`git grep AcquireTokenOnBehalfOf -- src/server`).

| # | Site | Token for | Reached from (examples) | Plane |
|---|---|---|---|---|
| 1 | `Infrastructure/Graph/GraphClientFactory.cs:290-293` (`CreateOnBehalfOfClientAsync`; entry points `ForUserAsync` :335 and `ForUserBetaAsync` :416) | **Graph / SPE**: `https://graph.microsoft.com/.default`, which includes `FileStorageContainer.Selected` (:271) | SpeFileStore `*AsUserAsync` methods. Used by Compose (`ComposeSpeAccess.cs:54-80`, only when the request was not authorized for that exact document), chat staging uploads (`ChatDocumentEndpoints.cs:1326`, `ChatWordExportEndpoints.cs:179`), `AiAuthorizationFilter.cs:787`, `FileIndexingService.cs:92` and `DocumentUrlIdentityResolution.cs:223`. Also the SpeAdmin container-type endpoints (`ForUserBetaAsync`), send-as-user mail (`CommunicationService.cs:1531`) and `OfficeEmailEnricher` | Internal |
| 2 | `shared/Spaarke.Dataverse/DataverseAccessDataSource.cs:233,254` | **Dataverse** (user_impersonation) | `Spaarke.Core/Auth/AuthorizationService.cs:225,282`, the document authorization filters on internal `/api/documents`-style routes | Internal |
| 3 | `Infrastructure/Dataverse/DataverseUserClient.cs:198-200` | **Dataverse** | AI `dataverse.*` tools, Signals executors, Events, Ontology admin, Reporting, Communications (about 45 consumer files, all on internal routes) | Internal |
| 4 | `Api/Agent/AgentTokenService.cs:137` (Graph) and `:208` (Dataverse) | **Graph and Dataverse** | `/api/agent` (`AgentEndpoints.cs:37`), the M365 Copilot agent | Internal (Agent) |
| 5 | `Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs:569-571` | **Dataverse only**: `{Dataverse:ServiceUrl}/.default` (:570), then `WhoAmI` (:622) and `RetrievePrincipalAccess` (:679) or `RetrieveUserSetOfPrivilegesByNames` (:468) | See §2 | Internal on master |

`NoAccessShareEnforcer.EnforceForUserAsync` only matches the name pattern. It is not an OBO call.

**What ADR-028 says about each plane** (`.claude/adr/ADR-028-spaarke-auth-architecture.md` at master):

- **Internal (sites 1–5 as used on master): allowed.** The base rule at :26 says that every confidential client acting
  as the BFF ("OBO / delegated exchanges") **MUST** use a secret-free credential. The A4 table at :188 lists
  *"Confidential client … **OBO / delegated exchanges**"* as a sanctioned class. A4's evidence at :159-160 shows it
  proven for *"OBO → Graph / SPE"* and *"OBO → Dataverse `user_impersonation`"*. ADR-007's cross-reference at :817 says
  *"OBO retained for delegated"*. A2 and A3 at :87 and :137 say *"Internal Xrm surfaces are UNAFFECTED"*.
- **External / collaboration / module host (`/api/v1/external/*`): forbidden.**
  - A1 :52: *"The external user's token … **MUST NOT** be exchanged for a downstream Graph/SPE/Dataverse token (**no OBO
    on the external path**)."*
  - A2 :77: *"… (**no OBO on the collaboration path**)."*
  - A3 :128: *"**MUST NOT** exchange the caller's token for a downstream Graph/SPE/Dataverse token on **either** plane."*
  - A4 :270: *"does not weaken the A1/A2/A3 'no OBO on the external, collaboration, or module-host planes' invariants"*.
  - A5 :339-341: *"No OBO is sanctioned anywhere by A5 … still in force."*
  - A8 :568 and A9 :642: *"no OBO on any collaboration plane."*
  - The A3 plane is the workforce side of `/api/v1/external`. Internal licensed users also reach the SPA and Teams through
    it: the `ExternalCollaboration` policy accepts the Spaarke-tenant scheme (`AuthorizationModule.cs:453-461`).
- **Code agrees on master.** The external group's own comments say *"app-only, no OBO"*
  (`ExternalAccessEndpoints.cs:124`; `AuthorizationModule.cs:448-450`). The A9 link route says
  *"No OBO with either token"* (`ExternalWorkforceLinkEndpoints.cs` header). A grep of `Api/ExternalAccess/External*.cs`,
  `ContactGrant*.cs`, `MeEntitlementsEndpoint.cs` and `Infrastructure/ExternalAccess/` at master finds no OBO entry point.

## 2. Where `CallerRecordAccessProbe` is used, and whether each use conflicts

On master the probe has **only** OBO methods: `GetCallerRightsAsync` :219, `GetCallerRightsForRecordsAsync` :325,
`GetCallerSystemUserIdAsync` :393 and `CallerHoldsPrivilegeAsync` :437. All four go through the exchange at :569. So
every master use below is an OBO use.

| Use (master) | Route group / plane | OBO? | Allowed by ADR-028? |
|---|---|---|---|
| `DelegationRuleFilter` (`ExternalAccessEndpoints.cs:174-177`), which gates `GrantExternalAccessEndpoint` (:52/:94), `InternalShareEndpoints` (:221/:284), `InviteAndGrantExternalUserEndpoint` (:32/:74), `ProvisionProjectEndpoint` (:543/:581), `UnsecureProjectEndpoint` (:163/:197), `RecordAccessGateEndpoint` (`/can-manage-access` :92) and the rest of `/api/v1/external-access/*` | `/api/v1/external-access`. Despite the name, this is the **internal management** group: default workforce scheme only (`RequireAuthorization()`), called from the model-driven app and the Console | Yes | **Yes**: internal plane, A4 |
| `RecordNoAccessEndpoint` (`/api/v1/records`, :67-70) | Internal | Yes | Yes |
| `DataverseDocumentsEndpoints` PUT (`/api/v1/documents`, :24, :104/:111) | Internal | Yes | Yes |
| `FieldMappingEndpoints` (`/api/v1/field-mappings`, :29; :824 rights, :833 WhoAmI) | Internal | Yes | Yes |
| Filters on internal routes: `EntityAccessFilter`, `RecordRouteAccessAuthorizationFilter` (:369, :459, :492), `TodoSourceAccessFilter`, `QuickCreateSourceAccessFilter`, `CommunicationRecordAuthorizationFilter` (:760-776), `FinanceAuthorizationFilter` (:376), `AnalysisAuthorizationFilter` (:482), `PlaybookAuthorizationFilter` (:244), Insights (`InsightsAssistantEndpoint.cs:188`), `PinnedMemoryEndpoints` (:748), `ReportingEndpoints` (:1059) | Internal (Console, MDA, Office, AI) | Yes | Yes |
| Services: `OfficeEditAccessService` (the Office edit-role grant on `/api/documents/{id}/office` and `/open-links`), `OfficeSearchService`, `ReconciledEmailFiling`, `RecordCreationService`, `ComposeService`, `SecureDesignationRemoval.ForRequest`, `DecisionActionExecutors` / `DecisionCommitService` | Internal | Yes | Yes |

**Master: zero conflicts.** No `/api/v1/external` route reaches the probe.

**The 036 branch adds the probe to `/api/v1/external`.** Commit 17a635cbe introduced it ("every external write asks
Dataverse as the caller"). Commit e7a8392bb added the app-only path for home-linked users. Both readers choose their
path from the resolved principal:

- `ExternalProjectDataEndpoints.CallerRightsReader` (:1029-1039) uses the app-only path
  (`GetPrincipalRightsAppOnlyAsync`, :1034) when `ResolvedThroughHomeLink` is set (:1066-1067). Otherwise it uses
  **OBO** (`probe.GetCallerRightsAsync(token, …)`, **:1038**).
- `CallerPrivilegeReader` (:1047-1057) does the same: app-only at :1052, otherwise **OBO** (`CallerHoldsPrivilegeAsync`,
  **:1056**).
- The readers are consulted only when `caller.DataverseConfirmsWrites`, that is, with the FR-20 flag on
  (`WithDataverseRightsAsync` :909-910; `CallerPrincipalResolver` diff). The flag defaults to `false`.

### Exactly which uses conflict with ADR-028

These are the OBO branches of the two 036 readers (`ExternalProjectDataEndpoints.cs:1038` and `:1056` on e7a8392bb).
They run when a **Spaarke-tenant-signed-in systemuser** (default JwtBearer scheme, not home-linked) acts on
`/api/v1/external` with the flag on. They are reached from these routes:

| Route (all `/api/v1/external/…`) | Handler, line of the reader call |
|---|---|
| GET `{projects,matters,workassignments,servicerequests,invoices}/{id}/documents` (per-record `canCreate`) | `ListDocumentsForParent` :571-572 |
| POST `…/{id}/documents` (upload) | `UploadDocumentForParent` :1613-1614 |
| GET `{projects,matters,workassignments}/{id}/todos` (`canCreate`) | `ListTodosForRoot` :714-715 |
| POST `{projects,matters,workassignments}/{id}/todos` | `CreateTodoForRoot` :756-757 |
| GET `{projects,matters,workassignments}/{id}/events` (`canCreate`) | `ListEventsForRoot` :2025-2026 |
| POST `projects/{id}/events` | `CreateEvent` :2082-2083 |
| PATCH `todos/{id}` | `UpdateTodo` :2208 |
| DELETE `documents/{id}`, `todos/{id}`, `events/{id}` | `DeleteOwnDocument` :2431, `DeleteOwnTodo` :2496, `DeleteOwnEvent` :2511 |
| POST `{root}/{id}/messages` | `ExternalMessageEndpoints.PostMessageAsync` :158-159 (consumed at :264) |

The A3 rule at :128 forbids this as written, and A8 :568 and A9 :642 restate it. The 036 notes already flagged the
tension and did not resolve it (`notes/036-adr-edits-owner-a-f.md` "Observation for the main session", branch).
Its claim that the tension *"predates decision (a)"* is true. It does not predate 036, though: master has none of it.

**Not conflicting on the branch:**

- the app-only, named-principal path (:1034, :1052; `DataverseWebApiService.RetrievePrincipalRightsAsync` :1605, with
  `MSCRMCallerID` impersonation at :1636-1639). This is the A5 mechanism, *"Impersonation is not OBO"* (:320).
- every flag-off request, which asks nothing (:909-910).
- every CIAM or workforce-contact caller (`DataverseConfirmsWrites` is false for them).

## 3. What each option changes

**In every option, no SharePoint Embedded or Graph access changes.** Only the two readers above change. Sites 1–4 and
the probe's internal uses stay byte-for-byte the same. To the owner's specific worry:

- **Internal document access and file open:** the URL-minting reads (preview, content, office, open-links, view-url)
  have run app-only since task 171 (`FileAccessEndpoints.cs:21-30`, :49-60).
- **Word/Office co-authoring:** *"The Office EDIT itself still runs as the user in Office"* (:27). Office signs the user
  in to SharePoint directly. The BFF only grants a writer role on secure containers (`OfficeEditAccessService`), and
  that runs on internal `/api/documents/{id}/office` and `/open-links` with its own internal OBO probe. None of the
  options touch it.
- **Compose, chat uploads and SpeAdmin** keep their Graph OBO (site 1).
- **External SPA documents** have streamed app-only all along (A1 :52). Unchanged.

### Option C: comply (app-only, named principal, for the conflicting uses only)

- **Files and calls:**
  - `ExternalProjectDataEndpoints.CallerRightsReader` / `CallerPrivilegeReader` (:1029-1057) always return the app-only
    reader for a `DataverseConfirmsWrites` systemuser. The OBO return lines :1037-1038 and :1055-1056 go away, and
    `AppOnlyConfirmationPrincipal` (:1066-1067) widens from home-linked users to every systemuser.
  - `ExternalMessageEndpoints` needs no change, because it uses the same readers.
  - `CallerRecordAccessProbe`'s OBO methods stay, still used by all of §2's internal rows.
  - Tests: `…Fr20a_ANormalSystemUser_StillUsesObo_AndNeverTheAppOnlyPath` inverts, and the OBO-path write tests move to
    the app-only double.
  - Docs:
    - ADR-034 A2 text on the branch (`.claude/adr/ADR-034…:93`, `docs/adr/ADR-034…` ~:405-423) drops "OBO".
    - The 036 draft edit B1 to ADR-028 A5 (`notes/036-adr-edits-owner-a-f.md`, not yet applied on any branch) widens from
      "WorkforceHome-linked" to "every systemuser confirmed on `/api/v1/external`".
    - This is a scope extension of A5's sanctioned mechanism, like the 2026-09-15 one (:347-361). It adds no new
      exception.
- **SPE/Graph:** none.
- **Behaviour versus OBO:**
  - *Who Dataverse answers about:* the same person, with the same roles, teams, shares and hierarchy. With OBO the
    identity is the credential (`WhoAmI`). With C the identity is data: `CallerPrincipal.SystemUserId`, the id that the
    validated token was resolved to. That is the same id and trust basis that the flag-on read set already uses (A5
    :295-312). Read and write are then answered by one mechanism and cannot disagree.
  - *Latency:* app-only is 1 call (`RetrievePrincipalAccess`), plus 1 privilege read when the answer has AppendTo.
    OBO is a token exchange (MSAL-cached after the first) plus `WhoAmI` plus `RetrievePrincipalAccess`, plus
    `WhoAmI` again and the privilege read for a child create (`036-fr20-implementation.md` :270-273).
  - *Failure modes:* both fail closed (None or false).
    - OBO can additionally fail on Entra: a Conditional Access policy that targets Dataverse (`interaction_required`),
      missing delegated consent, or a disabled Entra account at exchange time. App-only skips those checks at
      confirmation time. The user's sign-in to the BFF was already checked.
    - App-only fails if `prvActOnBehalfOfAnotherUser` is missing, or if Dataverse refuses to impersonate that user.
  - *Disabled or unlicensed users:* both paths ask Dataverse about that user. A Dataverse-disabled user is expected to be
    refused on both. Under the flag, the A5 read already impersonates the same user, so C adds no new class of outcome.
    Unlicensed-user behaviour was **not measured live** for either path; check it on dev in the 036 gate.
  - *Replication lag:* the OBO probe re-asks a 404 (`NotFoundRetryDelay`, :108-113). The app-only path maps 404 to
    None at once (`036-fr20-implementation.md` :577). A write on a record created seconds earlier could be refused once.
    That is already a known limit for home-linked users, and C makes it apply to all systemusers. Fix in scope if the
    gate shows it.
- **Prerequisite:** `prvActOnBehalfOfAnotherUser` on the BFF application user (ADR-028 A5 "Deployment prerequisites
  (register E-2 / E-3)", :363-370). The flag-on read already needs it, and the flag must not be turned on without it.
  C therefore adds **no new** prerequisite.

### Option B: amend ADR-028 to sanction this Dataverse OBO

- **Files:** ADR-028 concise only. Add a narrow carve-out to A3 :128, mirrored in A5 :339, A8 :568 and A9 :642. It would
  say: Dataverse-only OBO, for a Spaarke-tenant systemuser's write confirmation on `/api/v1/external`, flag on. Graph/SPE
  OBO stays forbidden. The code comments `AuthorizationModule.cs:448-450` and `ExternalAccessEndpoints.cs` say "no OBO"
  for the group and would need a qualifier. The 036 code stays as it is.
- **SPE/Graph:** none.
- **Behaviour:** the code as it is on e7a8392bb. Spaarke-tenant users are confirmed by OBO and home-linked users
  app-only, so the plane has two mechanisms for the same question.
- **Prerequisite:** the same `prvActOnBehalfOfAnotherUser` (for the read), plus the existing delegated Dataverse consent
  on the BFF app.
- **Cost:** it weakens the one invariant that A1–A9 each restate as "unchanged", in order to keep a second mechanism
  that C shows is unnecessary.

### Option A: project exception

- **Files:** a row in the project's `design.md` / `spec.md` ADR Tensions, cited in PR #2078. Code-review approves it
  explicitly. Code and ADR text are unchanged.
- **SPE/Graph, behaviour and prerequisite:** the same as B.
- **Cost:** ADR-028 keeps saying "no OBO on any collaboration plane" while shipped code does OBO there. That is the
  undocumented-deviation state that A4 itself was written to end (:171-179).

## 4. Is the conflict real?

**Yes, but it is narrow, and it is 036's own.** Correcting the earlier framing:

- *"The existing OBO code breaks the ADR"*: **false.** On master every OBO site, including every
  `CallerRecordAccessProbe` use, is on an internal route that A4 sanctions.
- *"C removes OBO from this check"*: **only on one plane.** C removes OBO from the two 036 readers on `/api/v1/external`.
  The probe's OBO, and therefore internal write and delegation checks, stays. Graph/SPE OBO is never involved.
- **The conflict is about Dataverse, not SPE.** The disputed token is a Dataverse token (`CallerRecordAccessProbe.cs:570`).
  SPE does not depend on it.
- It is **latent** until the flag is turned on, because it is unreachable with the flag off, which is the shipped
  default. It is still a conflict with the ADR as written, and must be resolved before PR #2078 merges.

## 5. Recommendation

**Choose C.** It keeps ADR-028's plane rule intact and puts the user's read set and write confirmation on one mechanism
on that plane. That mechanism, A5 impersonation, already has its fail-closed guards and negative canary. C needs no new
prerequisite beyond the one the flag already requires, and it touches no SharePoint Embedded, Graph or internal path.
Its two costs are both small and testable: the id is passed as data rather than proven by the credential (the same
trust as the read), and the 404 re-ask is lost.

**The owner decides two things:**

1. C (comply) or B (amend ADR-028 to allow Dataverse-only OBO for this check). A is not advised.
2. If C, approve widening the drafted A5 scope extension (edit B1) from home-linked users to every systemuser
   confirmed on `/api/v1/external`.

Until the owner rules, nothing changes, as directed.

## Independent long-term critique (2026-10-20, adversarial reviewer, Opus)

**Verdict: C is the best long-term choice of A, B and C.** It is also the base for a stronger end state later.

**Reasons:**
1. **On this plane OBO enforces nothing.** The write is always app-only (§2.3 of the implementation note); OBO only changes who answers the question. The OBO token is cached per inbound token (`CallerRecordAccessProbe.cs:690-694`, no ForceRefresh), so Entra session and Conditional Access checks run at most once per inbound token. B would protect only some writes, only for Spaarke-tenant users, and never the A5 reads.
2. **C covers every systemuser** (WorkforceHome users can never use OBO), so reads, write confirmation and attribution all rest on one resolved SystemUserId. B keeps two mechanisms for ever. B's one gain, identity taken from the credential, is moot, because a resolver bug already exposes the read set.
3. **Same Dataverse answer, fewer moving parts.** There is no token exchange and no new consent. Field security is ignored by RetrievePrincipalAccess either way. The blast radius of prvActOnBehalfOfAnotherUser is no larger than the BFF app user's own app-only reach.

**A fourth option: impersonated writes** (the BFF writes with MSCRMCallerID instead of checking and then writing app-only). This is better long-term: Dataverse enforces exactly what the model-driven app does, with native audit. It reuses C's mechanism and is incompatible with B. **Follow-up, not 036.**

**Conditions C must meet** (paths in C:/wt036):
- **F (fix now): replication-lag retry.** `DataverseWebApiService.cs:1613` maps 404 and 403 to None immediately, so under C every systemuser loses the OBO path's 404 re-ask (`CallerRecordAccessProbe.cs:108-113, 816-826`). A create followed by an immediate toggle would be refused. Re-ask on 404 only, on the same schedule.
- **Fault vs denial.** An impersonation fault (e.g. 0x8004A110) is read as "no rights" and logged at Information. Use RetrieveRightsOrUnknown (`:1754`) and log at Warning.
- **Disabled and unlicensed users, proven live.** At the 036 dev gate, app-only RetrievePrincipalAccess for one disabled and one unlicensed systemuser must be refused. If Dataverse answers with rights instead, add an isdisabled re-read. The resolver's identity cache is 2 minutes (`WorkforcePrincipalResolver.cs:101, 250`).
- **Never OBO on /api/v1/external, enforced mechanically.** Widen AppOnlyConfirmationPrincipal (`ExternalProjectDataEndpoints.cs:1066-1067`) with Guid.Empty still failing closed. Add a source-scan ArchTest: no token-taking GetCallerRightsAsync / CallerHoldsPrivilegeAsync calls in `Api/ExternalAccess/External*.cs`.
- **Conditional Access on the right resource.** Record in ADR-028 A5's deployment prerequisites that CA and sign-in-risk policies for SPA/Teams users must target the BFF API app and the SPA client.

**What would change the verdict:**
- a live OBO vs app-only comparison giving different answers (likeliest cause: `restrictGuestUserAccess`);
- impersonated calls counting against the app user's service-protection limits at SPA volume;
- an owner requirement for Dataverse-targeted CA or CAE (which would argue for OBO on reads too, reversing A1–A3, not for B);
- Microsoft retiring MSCRMCallerID.
