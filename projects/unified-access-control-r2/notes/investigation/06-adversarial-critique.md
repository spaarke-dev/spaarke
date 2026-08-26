# 06 — Adversarial Critique of the UAC-r2 Design Direction

> **Role**: Adversarial reviewer. Mandate: refute the proposed direction, not endorse it.
> **Method**: Code read of the r2 worktree (`c:/code_files/spaarke-wt-unified-access-control-r2`). Code is ground truth; every claim is cited `path:line`. CONFIRMED = verified in code. SUSPECTED = plausible, not fully verified.
> **Date**: 2026-08-20

---

## Summary

The briefing's central recommendation — "two mechanisms, one spine; extend the compute/derive **model A** (ScopeDimension descriptors) for the external plane because it is cheap and additive" — **does not survive contact with the code for the primary target entity (`sprk_todo`).** The query-boundary attacks I ran against the compute model mostly **failed** — the FetchXML guard + always-AND-injected scope filter + fail-closed `ScopeRows` are genuinely robust. But the design is undercut on three axes the briefing treats as solved:

1. **Generalization is false.** 8 of `sprk_todo`'s 11 regarding parents are **not** roots, and `CallerPrincipal` carries only **3** accessible root-id sets. "Just add a descriptor" cannot express a todo parented to a communication/event/invoice. (CONFIRMED)
2. **The authorization boundary over-includes.** The systemuser branch feeds **raw ADR-034 membership** (all six identity-lookup types, no access-conferring filter) into the SPA boundary, read **app-only** — so the BFF's broad "membership" is the *entire* boundary and can exceed the user's real Dataverse rights. (CONFIRMED mechanism)
3. **The push half cannot revoke.** POA rows carry no provenance; the existing precedent only ever grants. Reconciliation-on-removal is unbuildable without a shadow ledger nobody has specified. (CONFIRMED)

Ranked findings follow.

---

## Findings (ranked by severity)

### F1 — "Just add module descriptors" is refuted for `sprk_todo` (the primary target). **CONFIRMED. Severity: CRITICAL (to the thesis).**

- **Mechanism**: The ScopeDimension model reads *precomputed* accessible-root-id sets off `CallerPrincipal`. Those sets are exactly three: `ProjectAccess`, `AccessibleMatterIds`, `AccessibleWorkAssignmentIds` (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:91`, `:97`, `:102`). A `ScopeDimension` is `(Attribute, Func<CallerPrincipal, IReadOnlySet<Guid>>)` (`ExternalModuleRegistry.cs:49-58`) — it can only point at a set that already exists on the principal.
- **The gap**: `sprk_todo` has **11** regarding lookups — `sprk_regarding{analysis,budget,communication,contact,document,event,invoice,matter,organization,project,workassignment}` (`docs/architecture/spaarke-todo-architecture.md:34`). Only **matter / project / workassignment** have accessible-id sets. The other **8** parents have none. `AccessibleCommunicationIds` / `AccessibleEventIds` / `AccessibleInvoiceIds` **do not exist anywhere in the BFF** (grep: 0 hits).
- **Worse — the 2-hop / child-is-itself-a-child case the assignment flagged**: `sprk_invoice` and `sprk_document` are themselves **child modules** scoped by matter/project (`Infrastructure/DI/ExternalAccessModule.cs:166-190`); `sprk_communication` and `sprk_event` have **no external read path at all**. So a todo → communication → matter chain has no representable dimension: `sprk_regardingcommunication ∈ {accessible communications}` requires a set that is *itself derived*, which the `Func<CallerPrincipal,…>` shape cannot compute (it is a pure synchronous read, no Dataverse round-trip — that is its whole design point, `ExternalModuleRegistry.cs:10-17`).
- **Failure scenario**: UAC-r2 registers a `todo` module with three dimensions (matter/project/WA). A todo regarding *only* a communication (a common shape — "call the client back about thread X") has all three dimensions empty → `ScopeRows`/injector return **0 rows** (fail-closed). The external user silently never sees that todo. The "cheap, additive" extension delivers **wrong answers**, not a security hole — but it refutes the briefing's core claim that extension is a descriptor edit. Making it correct requires composing accessible-communication/event/invoice sets onto `CallerPrincipal` (extra per-request Dataverse round-trips, or recursive composition) — i.e. new mechanism, not new config.

### F2 — Over-inclusion asymmetry: raw ADR-034 membership is the app-only authorization boundary. **CONFIRMED mechanism; SUSPECTED exploitability (data-dependent). Severity: HIGH.**

- **Mechanism**: For a workforce systemuser, `ComposeForSystemUserAsync` calls `_membership.ResolveAsync(systemUserId, entityType, options: null, ct)` (`AccessibleRecordSetService.cs:192`) — the **full** resolver, which discovers **every** Lookup targeting the 6 identity tables (`systemuser, contact, team, businessunit, account, sprk_organization`) and matches the user's normalized identity across **all** of them. The access-conferring allow-list (`FilterToAccessConferringContactRoles`, `MembershipResolverService.cs:511-558`) is applied **only** on the contact path (`ResolveByContactAsync`), **never** on the systemuser path.
- **Why it is the *entire* boundary**: the BFF reads Dataverse **app-only** for external callers (`ExternalModuleDataEndpoints.cs:14-22` "broker-only … all reads execute APP-ONLY"). Dataverse row security is therefore **never consulted** for the SPA. Whatever `ResolveAsync` returns *is* what the user can see.
- **Consequence**: a systemuser's SPA-accessible matter/project set = "every record where I am referenced by any owner/team/BU/account/org/contact lookup," which is deliberately broader than "records I have Dataverse read privilege on" (ADR-034 was built for AI scoping, where over-inclusion is acceptable). A user can be surfaced records they have **no MDA access to**.
- **Failure scenario**: an internal user is named on a matter via a non-access lookup (e.g. resolved through a shared team/BU, or a `sprk_regarding*` contact match on their primary contact). On the SPA that matter — and, once F1's todo/communication modules ship, its **child todos and privileged communications** — becomes visible even though Dataverse would deny them in the MDA. This is the exact confidentiality inversion the SPA's app-only posture makes unrecoverable at the data layer.

### F3 — Uncapped `in`-clause in the scope injector will overflow Dataverse's ~500-value limit. **CONFIRMED. Severity: HIGH (availability).**

- **Mechanism**: `Tier2ScopeFilterInjector.Inject` emits **one `<value>` per accessible id**, for **every** non-empty dimension, with **no cap** (`Tier2ScopeFilterInjector.cs:81-84`). A todo module has up to 3 dimensions → up to 3×N values.
- **The limit is documented *in this same codebase***: `BuildTransitiveFetchXml` notes "The `in` operator handles up to **500 values per condition** in standard Dataverse" and defensively caps at `MaxLimit` (`MembershipResolverService.cs:1027`, `:1038-1040`). The injector has **no** equivalent cap.
- **Failure scenario**: a supervising partner or paralegal with membership on 600+ matters logs into the SPA. The document/todo fetch injects a 600-value `in` condition → Dataverse rejects the query → `ExecuteScopedFetchAsync` returns `DV_FETCH_INTERNAL_ERROR` (`ExternalModuleDataEndpoints.cs:232-239`). The **highest-value users get a broken workspace**. This is deterministic and config-independent — the most *certain* incident to fire, though "only" availability.

### F4 — The push model is additive-only with no provenance; reconciliation-on-removal is unbuildable as specified. **CONFIRMED. Severity: HIGH.**

- **Mechanism**: the only grant seam is `GrantAccessAsync(entitySet, recordId, principalSystemUserId, accessRightsCsv)` (`Services/Communication/Access/IDataverseAccessGrantService.cs:17-22`). The shipping precedent, `DirectThreadAccessService`, **only ever grants** — `GrantMessageAccessAsync` and `GrantReadAccessToPrincipalsAsync` (`DirectThreadAccessService.cs:153-205`) contain **no revoke path**. POA (`principalobjectaccess`) rows record `(principal, record, rights)` and **nothing about why** the share exists.
- **The unrecoverable part**: on member removal, a reconciliation job must revoke the cascade's share **without** touching a share a human created via the MDA "Share" button — but the two are indistinguishable at the POA layer. `RevokeAccess(principal, record)` removes the access mask wholesale. So reconciliation must either **retain** (privilege-retention bug — the removed member keeps child access) or **over-revoke** (data-loss bug — a human's deliberate share is wiped). The cascade doc (§6.2, §6.3) proposes reconciliation as "the only clean way to propagate removals" but specifies **no shadow ledger** of cascade-created shares. Without one, correctness is impossible.
- **Failure scenario**: attorney A is removed from `sprk_assignedattorney1` on a matter. The nightly job runs. Either A still sees the matter's todos tomorrow (retention), or the job revokes a share the matter lead had manually granted A for an unrelated reason (loss). Sharing being additive-only (cascade doc §6.3) is stated but its consequence — that removal has *no correct automated implementation here* — is not.

### F5 — The access-conferring boundary is a naming convention, not a semantic gate. **CONFIRMED mechanism; SUSPECTED exploitability. Severity: MEDIUM-HIGH.**

- **Mechanism**: `FilterToAccessConferringContactRoles` admits a lookup iff (1) it is Contact-typed, (2) its field name **starts with `sprk_assigned`** (default prefix), (3) not on an exclusion list (`MembershipResolverService.cs:540-553`). The docstring advertises that a "newly-added `sprk_assigned*` contact lookup **auto-qualifies with no code change**" (`:507-509`) — this is presented as a feature.
- **The hole**: the gate is lexical. Any Contact-typed lookup a maker names `sprk_assigned*` **silently confers access**, regardless of intent. Conversely, a legitimate access-conferring contact lookup that does *not* follow the convention (e.g. `sprk_leadcontact`) is silently **excluded** — a legitimate member wrongly denied. The comment claims adverse/opposing-counsel fields "fail here," but that is only true if they happen to be named outside the prefix; `sprk_assignedopposingcounsel` would **pass**.
- **Failure scenario**: a maker adds `sprk_assignedmonitor` (a read-only observer contact, not meant to confer access) to `sprk_matter`. With no code change and no review of this security-load-bearing convention, that observer now derives access to the matter and all its children on the SPA. SUSPECTED because it depends on a field being so named; the mechanism is CONFIRMED.

### F6 — Two planes = two definitions of "member" for the *same person*. **CONFIRMED. Severity: MEDIUM.**

- **Mechanism**: the same human resolves through **different** membership logic depending on branch. A contact-backed internal user hitting the SPA can resolve as `SystemUser` (oid→systemuser found) → `ResolveAsync`, **all** identity lookups, no filter (`AccessibleRecordSetService.cs:192`); or, if they have no systemuser row, as `ContactOnly` → `ResolveByContactAsync`, **`sprk_assigned*`-only** (`:276-277`). Resolution order is systemuser-first (`WorkforcePrincipalResolver` per briefing §2), so the *same identity* gets a **broader** access set as a systemuser than as a contact.
- **Why it matters for the two-plane thesis**: the briefing says "reuse `FilterToAccessConferringContactRoles` on both planes." The code does **not** — the systemuser plane has no such filter. So "one spine, two mechanisms" already means **two membership semantics**, and the design's own recommended unification is not what the code does. This is the divergence a single unified evaluator would eliminate — and it is the strongest argument *for* one evaluator (see below).

### F7 — Expiry is not enforced on the compute path. **CONFIRMED. Severity: MEDIUM.**

- **Mechanism**: `QueryGrantSetAsync` filters grants on `statecode eq 0` only (`ExternalParticipationService.cs:406`, `:511`, `:553`). The schema carries `sprk_expiresdate` (briefing §5), but it appears **nowhere** in the read path (grep of `Infrastructure/ExternalAccess`: only an unrelated cache-TTL log line). So a grant with a **past** `sprk_expiresdate` still confers access until someone manually deactivates the row.
- **Failure scenario**: outside counsel is granted access "until case close, 2026-06-30." That date passes; nobody deactivates the row; the partner retains full SPA access to the matter and (post-F1) its children indefinitely. Time-boxed access — a natural UAC requirement — silently does not work.

---

## Attacks that FAILED (the design survived — stated honestly)

1. **FetchXML injection via caller input.** The injector builds XML with `XElement` and `id.ToString("D")` over server-static attribute names from the module descriptor (`Tier2ScopeFilterInjector.cs:75-86`); no caller string is concatenated into a query. The caller's FetchXML is `XDocument.Parse`d, not string-spliced. **No injection surface. SURVIVES.**
2. **`<link-entity>` / join smuggling to ride internal data out.** `FetchXmlEntityExtractor.ExtractEntities` walks `Descendants("link-entity")` at **any depth**, includes m:m `intersect` and outer joins, throws on a missing `name`, and the endpoint rejects if **any** referenced entity ≠ the module entity (`FetchXmlEntityExtractor.cs:94-110`; `ExternalModuleDataEndpoints.cs:160-172`). **SURVIVES.**
3. **OR-ing the scope filter away.** The injected `<filter type="or">` is added as a **top-level sibling** of the caller's filters under `<entity>` (`Tier2ScopeFilterInjector.cs:88-96`); sibling entity-level filters are **AND-combined** by FetchXML semantics, so the caller cannot dilute it. **SURVIVES.**
4. **Empty-set / fail-open.** All-empty dimensions short-circuit to 0 rows **without querying** (`ExternalModuleDataEndpoints.cs:184-191`); `ScopeRows` keeps a row **only** on a positive id match and drops any row it cannot verify (`ExternalModuleRegistry.cs:159-187`, `RowMatchesEntity:195-204`). Two independent fail-closed layers. **SURVIVES.**
5. **Omitting the projected parent-lookup to dodge in-memory scoping.** If the caller doesn't select the lookup, `TryGetAttributeId` returns false → `ScopeRows` drops the row. The server-side filter still ran. Net: **fewer/zero** rows, never more. **SURVIVES (footgun, not a leak).**
6. **Single-record child read (`GET /record/...`).** `IsRecordAccessible` checks `childId ∈ {parent-id set}` → structurally always false for a child → fail-closed deny (`ExternalModuleRegistry.cs:126-144`). **SURVIVES** (over-restrictive by design).

**Minor / SUSPECTED, low-confidence:**
- **Multiple `<entity>` siblings**: the extractor reads only the *first* `document.Root.Element("entity")` for the primary (`FetchXmlEntityExtractor.cs:78-81`); a second sibling `<entity>` would evade the primary check. But Dataverse rejects a multi-root fetch, so **SUSPECTED non-exploitable** — worth a guard nonetheless (reject >1 `<entity>`).
- **Aggregate queries** return empty (no `@logicalName`/lookup on aggregate rows → `ScopeRows` drops them). Broken feature, not a leak.

---

## Single unified evaluator vs. two-plane split — which survives the code?

**Argument for one evaluator (my recommendation):** F6 shows the split has *already* produced two definitions of "member" for one person, and F2 shows one of them (systemuser, unfiltered) is the wrong shape for an authorization boundary. Two mechanisms = two revocation stories (F4 for push; "nothing to revoke" for compute) = two audit stories. A single evaluator — `IAccessibleRecordSetService` already **is** this seam (one `IsRecordAccessibleAsync`, `AccessibleRecordSetService.cs:39-55`) — with **one** access-conferring policy applied uniformly to both principal kinds, would collapse F2 and F6.

**Argument against one evaluator:** the two planes are not merely two code paths — they are two **enforcement substrates**. For a licensed `systemuser` in the **MDA**, Dataverse enforces row security natively regardless of what the BFF computes; the BFF cannot *be* that boundary there. For a `contact` in the **SPA**, there is no Dataverse principal at all, so the BFF's compute is the *only* possible boundary. A single evaluator that tried to unify these would either (a) duplicate Dataverse's row engine (and drift from it) or (b) leave the MDA path unenforced. So "one membership *definition*, two *enforcement* mechanisms" is defensible — **but only if the one definition is the access-conferring-filtered one, applied to systemusers too.** The code today does the opposite (F2/F6).

**Verdict:** the two-plane *enforcement* split survives; the two-plane *membership-definition* split does **not**. The design must state that the ADR-034 access-conferring filter is applied on **both** principal kinds, or accept F2 as a documented, reviewed privilege-broadening.

---

## Attack on using the ADR-034 resolver as an authorization input

Built for AI scoping (over-inclusion is a feature there), the resolver:
- **Computes app-only** (`ExternalModuleDataEndpoints.cs:14-22`), so it returns records irrespective of the caller's Dataverse rights (F2).
- **Applies no access-conferring filter on the systemuser path** (F2, F6) — the asymmetry is in-code, not hypothetical.
- On the **contact path** the filter is lexical, so it can both **over-include** (`sprk_assigned*` non-access lookups, F5) and **under-include** (access-conferring lookups not named `sprk_assigned*`, F5).

Using it as an authorization oracle inherits all three. It is safe as an *AI-scoping* input; it is **not** safe as the *sole* authorization boundary without (a) the filter on both planes and (b) a documented acceptance that app-only membership may exceed MDA rights.

---

## Missing requirements (nobody has asked; each can sink the project)

1. **Audit / attestation — "who can see this record and why."** No per-record effective-access API exists. POA has no provenance (F4); compute-derived sets are ephemeral (recomputed per request, never stored). A compliance auditor cannot answer the question at all.
2. **Effective-permissions UX.** No surface tells a user or admin what an external partner can currently see.
3. **Delegated administration + least-privilege on grant writes.** Who may call `/grant` / `RevokeExternalAccessEndpoint`? Not analyzed here — must be, before any push-model write endpoint ships (root §10 BFF hygiene).
4. **Expiry.** `sprk_expiresdate` exists but is unenforced (F7).
5. **Break-glass / emergency revoke-all.** No "cut off partner X now" primitive across both planes.
6. **Tenant isolation (CIAM).** Plane selection keys on `iss` containing `ciamlogin.com` or `tid == Ciam:TenantId` (`CallerPrincipalResolver.cs:234-254`) — a single misconfigured `Ciam:TenantId` mislabels every workforce caller as CIAM (or vice-versa). No test on a dev-only env exercises the wrong-tenant case.
7. **GDPR / right-to-be-forgotten on derived access.** Deleting a contact leaves orphan POA rows (push) and stale grant rows; compute silently stops resolving (good), but the push half needs an erasure path.
8. **Performance at scale.** Every workforce SPA request composes **three** accessible sets via three `ComposeAsync` Dataverse round-trips (`CallerPrincipalResolver.cs:422-427`) before any data read; F3 compounds this. No load test on the near-zero dev baseline (README links the paused r1 validation flagging exactly this).
9. **Testability / verifiability on a dev-only environment.** The README's own related-work note records "fail-OPEN row-level-security risk on a near-zero test baseline." None of F1–F7 has a failing test today.

---

## The killer question — most likely production incident if shipped as written

**Most severe likely incident (confidentiality):** UAC-r2 ships the briefing's recommendation — model A extended to `sprk_todo` / `sprk_communication` as child modules, scoped by `sprk_regardingmatter ∈ CallerPrincipal.AccessibleMatterIds`. A workforce **systemuser** signs into the SPA. Their `AccessibleMatterIds` is composed by `ComposeForSystemUserAsync` → `ResolveAsync` with **no access-conferring filter** (`AccessibleRecordSetService.cs:192`), read **app-only** (Dataverse row security never consulted). So the set includes every matter the user is *referenced* on by any of the 6 identity-lookup types — including matters where they are a `sprk_regarding*` or team/BU-derived reference with **no MDA read right**. The todo/communication module's OR-filter (`Tier2ScopeFilterInjector.cs`) then returns those matters' **child todos and privileged client communications**. Mechanism, end to end: `WorkforcePrincipalStrategy.ResolveAsync` (`CallerPrincipalResolver.cs:422-427`) → `ComposeForSystemUserAsync` (`:184-241`, no filter) → `AccessibleMatterIds` → todo module `ScopeDimension(sprk_regardingmatter, …)` → `Tier2ScopeFilterInjector.Inject` → the child rows are returned. **Result: disclosure of privileged matter communications/todos to an internal user who lacks Dataverse access to that matter** — and this is the very population (external + contact-backed internal) the SPA exists to serve.

**Most *certain* incident to fire (availability):** F3. A heavily-assigned internal user (600+ accessible roots) opens the SPA on day one; the injected `in` clause overflows Dataverse's ~500-value limit; the widget returns `DV_FETCH_INTERNAL_ERROR`. Deterministic, data-shape-independent, hits the highest-value users first.

---

## Recommended design changes

1. **Do not claim model A generalizes to `sprk_todo` by descriptor edit.** Either (a) restrict cascade to root-parented children only and document the 8 non-root regarding lookups as out of scope, or (b) build first-class accessible-set composition for communication/event/invoice (new mechanism, new per-request cost) — and price it honestly. (F1)
2. **Apply the access-conferring filter to the systemuser plane too**, or escalate F2 as a §6.5 documented, reviewed privilege-broadening. The design MUST state which. (F2, F6)
3. **Cap and batch the injector `in` clause** (mirror `BuildTransitiveFetchXml`'s `MaxLimit` cap, `MembershipResolverService.cs:1038`); page or chunk large accessible sets. (F3)
4. **If any push/grant cascade is built, add a provenance ledger** (a `sprk_*` shadow table keyed by principal+record+source) so reconciliation can revoke cascade shares without clobbering human shares — or drop automated removal entirely and make it a manual, audited operation. (F4)
5. **Replace the lexical access-conferring convention with an explicit per-field opt-in** (a metadata flag / config list of access-conferring fields), so a `sprk_assigned*` name is neither necessary nor sufficient. (F5)
6. **Enforce `sprk_expiresdate`** in `QueryGrantSetAsync` (`ExternalParticipationService.cs:406`). (F7)
7. **Add the missing requirements as explicit spec line items** — audit/attestation, effective-permissions, delegated admin + least-privilege on grant writes, break-glass, tenant-isolation test, GDPR erasure, a load test. (Missing-requirements §)
8. **Add a guard rejecting FetchXML with more than one `<entity>`** in `FetchXmlEntityExtractor` (defense-in-depth, closes the SUSPECTED multi-root gap).
