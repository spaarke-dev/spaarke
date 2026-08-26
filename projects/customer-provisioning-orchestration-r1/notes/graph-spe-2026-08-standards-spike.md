# Graph + SPE Standards Spike (Aug 2026)

> **Purpose**: Sanity-check the r1 design.md v3.2 against the latest Microsoft Graph, SharePoint Embedded (SPE), Managed Identity, multi-tenant consent, Exchange, and Power Platform provisioning guidance as of 2026-08-16. Owner explicitly authorized the spike to ensure we are not building against stale patterns.
> **Method**: Prior researcher memory (Aug 2026 SPE + Kiota + net10 memos) + targeted Microsoft Learn re-scrape (SPE `whats-new`, SPE auth doc, Exchange RBAC for Applications doc) + `msgraph-sdk-dotnet` releases + Terraform Power Platform provider registry + Entra workload-identity docs. Web-search results filtered to `learn.microsoft.com` / `github.com/microsoftgraph` / `github.com/microsoft` where available.
> **Timebox**: ~15 min of active work.
> **Scope note**: Only areas where a change would materially alter r1 provisioning are called out; sections that are unchanged since the design.md v3.2 (2026-08-15) baseline are stamped "still current" with a citation.

---

## 1. Graph SDK v6 / Kiota 2 — status

**Verdict: STILL CURRENT — no design change.**

- **Latest**: `Microsoft.Graph` **v6.5.0** (2026-08-06). No v7 shipped. v6 line is on Kiota 2.x since v6.0.0 (2026-05-12). Sources: [msgraph-sdk-dotnet releases](https://github.com/microsoftgraph/msgraph-sdk-dotnet/releases), [NuGet](https://www.nuget.org/packages/Microsoft.Graph/).
- v6.0.0 dropped `net5.0`; targets `netstandard2.0/2.1`, `net8.0`, `net10.0` — matches our .NET 10 cutover baseline exactly.
- **Error type contract unchanged since the cutover**: `ODataError` (not `ServiceException`); `ResponseStatusCode` is `int`; `ResponseHeaders` is `IDictionary`. This is what our design.md §5.5 already codifies (r3 handoff row: "Graph v6 / Kiota 2.0 error type"). No new patterns introduced 2026-06 → 2026-08.
- **Retries / throttling / batch / delta**: no new guidance since the standing 429/503 + `Retry-After` + `$batch` pattern. Nothing worth chasing.
- Related prior memo: [`kiota-cve-2026-44503-tfm-2026-08-11.md`](../../../.claude/agent-memory/researcher/kiota-cve-2026-44503-tfm-2026-08-11.md) — CVE patched by Kiota.Abstractions 1.22.0; separately, the r3/net10 chose Graph 6.5.0 / Kiota 2.0.0 for bundling. **Current per r1.**

---

## 2. SharePoint Embedded — status

**Verdict: Confidential-client + 24h replication are still current; several 2026 items are minor simplifications, one is a bootstrap-privilege loosening worth noting in H8.**

### Still current as of Aug 2026

- **Confidential-client (app-only) for container-type creation** — [SPE auth doc updated 2026-07-13](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/auth) explicitly says: *"Use a confidential client application to keep your app in control of actions taken on behalf of a user. […] certificate-based authentication is the recommended posture for production."* Our T6 fix (delegated → confidential-client with cert from KV) matches the doc. **No change.**
- **Container-type registration in consuming tenant** — Still uses `FileStorageContainerTypeReg.Selected` (delegated OR application). Delegated call requires signed-in user to be SPE-Admin or Global Admin. No bulk-provisioning API has appeared; still one-tenant-at-a-time via admin consent + a `Register-*.ps1` style call. **H8 unchanged.**
- **Replication window** — SPE `whats-new` mentions nothing changing the up-to-24h replication behavior for a new container type. Prereq lead-time in H0 preflight is still correct.
- **Delegated vs app-only matrix** — Application `FileStorageContainer.Selected` requires admin consent in the consuming tenant; delegated does not. App-only token spans all containers of that container type (least-privilege caveat unchanged). This is the model r1 already builds against.
- **Cross-tenant SPE (customer-tenant containers)** — No structural change; the cross-tenant story is still "app is registered in owning tenant, container type is registered in consuming tenant, app-only token from consuming tenant acquired via multi-tenant consent." Confirms our Model 2 design.

### Actual 2026 changes worth noting (all upside, none blocking)

- **June 2026 — `FileStorageContainerType.Manage.All` no longer requires SPE-Admin / Global-Admin** in the owning tenant. Any non-guest user can create a container type and is auto-assigned as owner. **Impact on H8**: the owning-tenant bootstrap is easier — a developer/service account no longer needs a directory role to create the container type. Not a design change; a small runbook simplification. [SPE what's new](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/whats-new).
- **May 2026 — Bulk container permissions upsert via delta PATCH** — the [`filestoragecontainer-patch-permissions`](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-patch-permissions) endpoint. **Impact**: if r1 or a downstream module ends up batching container permission grants, this replaces N individual POSTs. Not in H8/H14 today; note for future.
- **May 2026 — Container type audit logs** (`ContainerTypeCreated / Deleted / Updated / OwnersUpdated`) via Purview audit. **Impact**: H8 verification can post-check audit log presence if we want a stronger idempotency assertion. Optional.
- **March 2026 — `permissions` navigation on container type (owner mgmt)** — beta only; max 3 owners per container type. **Impact**: if we want to add the UAMI SP as a container-type owner (not just the human bootstrapper), we can do it via `POST /containerTypes/{id}/permissions`. Consider for Phase C UAMI migration.
- **March 2026 — SPE agent SDK deprecated** in favor of SharePoint Embedded knowledge source in Microsoft Foundry. **Impact on r1: none** (we do not use the SPE agent SDK).
- **February 2026 — SPE connector for Power Platform GA**. **Impact on r1: none** (we do not use the connector; BFF talks to SPE via Graph SDK).
- **January 2026 — `fileStorageContainer` column APIs GA in v1.0** (list/create/update/delete). Confirms the dedup lever from the [`spe-dedup-content-identity-2026-07`](../../../.claude/agent-memory/researcher/spe-dedup-content-identity-2026-07.md) memo is now GA, not preview. Not a provisioning change; belongs to downstream ingestion.
- **December 2025 — `fileStorageContainerType` + `fileStorageContainerTypeRegistration` APIs GA in v1.0** (were beta). r1 was already assuming v1.0 for H8 — this ratifies the assumption.
- **July 2026 — Copilot Retrieval API GA with `sharePointEmbedded` data source (preview)** — pay-as-you-go on Copilot Studio message meter. Not a provisioning concern for r1; flagged for downstream RAG stack conversation.

### Related prior memos

- [`spe-ciam-crosstenant-apponly-brokering-2026-07-18.md`](../../../.claude/agent-memory/researcher/spe-ciam-crosstenant-apponly-brokering-2026-07-18.md) — app-only ReadContent green for read/download/thumbnail; RED for Word-for-Web / Copilot / Search. Still current.
- [`spe-wopi-coauthoring-lock-423-2026-07-30.md`](../../../.claude/agent-memory/researcher/spe-wopi-coauthoring-lock-423-2026-07-30.md) — WOPI co-authoring lock behavior unchanged.

---

## 3. Managed Identity for Graph app-only — status

**Verdict: UAMI still recommended; **new capability worth Phase-C-plus**: Managed Identity as Federated Identity Credential on an Entra app (GA'd 2026).**

- **UAMI over System-Assigned MI: still the pattern.** No shift in guidance. ADR-028 §24 remains consistent. Our Phase C (`uami.bicep` + `app-service.bicep` refactor) is correctly aligned.
- **Federated Identity Credentials (FIC) with Managed Identity — GA in 2026.** Per [devblogs.microsoft.com "Access cloud resources across tenants without secrets — GA"](https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets-ga/) and [Entra workload-id: configure app to trust a managed identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity), you can add a **UAMI as a federated credential on an Entra app registration** — the app then trusts tokens issued to the UAMI. **This is a strategic upgrade for the Model 2 cross-tenant story**: instead of provisioning a per-customer client secret / cert on the customer's app registration, the customer's app registration federates the Spaarke platform UAMI. Zero secrets in customer tenants.
  - Limit: 20 FICs per app / per UAMI.
  - Case-sensitive issuer/subject/audience matching.
- **Impact on r1**: This is a **Phase-C-plus optimization**, not an r1 scope-changer. r1 provisions per-customer BFF app-regs with certs. If we later want secretless cross-tenant, we would add the platform UAMI as a FIC on each customer's app-reg during H3. Worth capturing as a follow-on backlog item; the r3 handoff obligation (10/14 `GraphAppRoles.cs` GUIDs pending live enumeration) remains the blocking item, not FIC adoption.
- **Known-issue traps beyond T3/T5**: none surfaced in the 2026-06 → 2026-08 window.

---

## 4. Multi-tenant app registration + consent — status

**Verdict: STILL CURRENT — no design change to D18 (consent-callback).**

- **URL-based admin consent** is the current guidance (documented since May 2025 as the SPE guidance; broader guidance unchanged). `/adminconsent?client_id=...&redirect_uri=...&state=...&scope=...` pattern is still correct. Our D18 handler H0.5 aligns.
- **V2 endpoint** is default; no shift. **App Manifest v2 → v1** is not an issue in scope.
- **Consent-capture callback with HMAC signing** — no change in guidance; the pattern we describe (HMAC-verified callback captures `tid` and seeds the run) is neither codified nor contradicted by Microsoft — it is our own hardening on top of the OOB admin-consent redirect. **Keep as designed.**
- **Related memo**: [`ciam-user-provisioning-graph-2026-07-19.md`](../../../.claude/agent-memory/researcher/ciam-user-provisioning-graph-2026-07-19.md) confirms `oid` is the stable link key, cross-tenant needs an app IN the customer tenant, and MI cannot hold Graph perms cross-tenant. Consistent with r1 design.

---

## 5. Exchange ApplicationAccessPolicy — status

**Verdict: LEGACY. RBAC for Applications is the replacement. Not deprecated yet, but "deprecation announced in future." Materially affects H14 and the T4 trap in the medium term.**

Source: [Role Based Access Control for Applications in Exchange Online](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac) (updated 2026-03-16).

### What the doc says (verbatim quoting the key language)

- *"RBAC for Applications in Exchange Online extends the current RBAC model in Exchange Online and it replaces Application Access Policies."*
- *"RBAC for Applications replaces Application Access Policies."*
- Legacy [Application Access Policies](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies) doc is now titled *"(legacy)"*.
- Migration path is documented (5 steps: create management scope → SP pointer → assign role → remove Entra unscoped consent → remove old ApplicationAccessPolicy).
- **No hard cutover date** announced yet — Microsoft blocked non-Microsoft EWS from Oct 1, 2026, but that is EWS-only and does not touch app-only Graph Mail.
- **Cache/propagation**: 30 min–2 hours (comparable to the old ~30-min ApplicationAccessPolicy delay, with a `Test-ServicePrincipalAuthorization` cmdlet that bypasses cache — useful for H14 verification).
- Supports MS Graph + EWS protocols. Full mail/calendar/contacts role set is available; `Application Mail.Send`, `Application Mail.ReadWrite`, `Application MailboxSettings.Read`, etc.

### Impact on r1 (H14 / T4)

- **r1 minimum (do this now)**: keep the `Set-ApplicationAccessPolicy` pattern as designed. It still works today and there is no forced cutover. T4 as currently written (two policies present, both principals verified) is correct and unblocks r1.
- **Recommended upgrade path (Phase D or wrap-up)**: adopt RBAC for Applications for new customer stamps. New H14 semantics:
  - `New-ServicePrincipal -AppId <BFF-app-reg> -ObjectId <BFF-SP-oid> -DisplayName ...`
  - `New-ServicePrincipal -AppId <UAMI-clientId> -ObjectId <UAMI-SP-oid> -DisplayName ...`
  - `New-ManagementRoleAssignment -App <SP> -Role "Application Mail.Send" [-RecipientAdministrativeUnitScope <AU>]` (per principal, per role)
  - Verify via `Test-ServicePrincipalAuthorization -Identity <SP> -Resource <mbx>`
- **Coexistence is safe** — Entra consent + RBAC-for-Apps assignments are additive (union). During migration, both mechanisms can be present; the migration guidance is "remove unscoped Entra consent AFTER RBAC assignment is in place" to avoid an "A and Not A" over-scope condition (see doc "Example Two"). Any migration must be atomic in the runbook.
- **Design change recommendation**: add an RFC-style item to §12 Risk Register + §14 Phasing to migrate H14 (and T4) to RBAC for Applications post-first-customer. Keep r1 execution on ApplicationAccessPolicy; explicitly note the sunset trajectory so the migration is not a surprise.

---

## 6. Power Platform env provisioning — status

**Verdict: STILL CURRENT. Terraform Power Platform provider is production-mature (v4.1.0, Jan 2026), validating D14. `pac admin create-environment` remains a valid interim.**

- **Terraform Power Platform provider (microsoft/power-platform)** — [Terraform Registry](https://registry.terraform.io/providers/microsoft/power-platform/latest/docs), [GitHub](https://github.com/microsoft/terraform-provider-power-platform/releases). v4.1.0 published Jan 26, 2026. Actively maintained; some resources still labelled "preview" but core `powerplatform_environment` and `powerplatform_user` are mature enough for production. **Validates D14 design intent** (adopt TF for Dataverse env lifecycle). Our M-10 decision to defer implementation to first-customer engagement is orthogonal to the provider being ready — the provider IS ready; we just have no customer volume to justify the switch yet.
- **`pac admin create-environment`** — still the right interim per-r1. No 2026 deprecation announcements.
- **SP-created env types**: no change — service principals still can create Sandbox/Production but not Developer (still current per [Microsoft PP docs](https://learn.microsoft.com/en-us/business-applications/playbook/enterprise-solutions/power-platform-terraform-provider)). Our design already notes this.
- **Application User registration**: still the Web-API `systemuser` POST pattern (with `applicationid` field pointing to the app-reg's client ID). Terraform's `powerplatform_user` wraps this same API. No new pattern.

---

## What would change in r1's design if adopted

Ranked by materiality:

1. **[MEDIUM] Exchange RBAC for Applications** — H14 sub-step (a) should have a documented "phase-out ApplicationAccessPolicy → RBAC-for-Apps" migration plan, even if r1 execution stays on ApplicationAccessPolicy. Add to §14 Phasing (post-first-customer) and §12 Risk Register (RC-N: policy path likely sunset in 2027-2028 window).
2. **[LOW] MI-as-FIC for cross-tenant** — Add a Phase-D backlog item to explore replacing per-customer client-secret/cert with a UAMI-federated pattern on customer app-regs. Zero-secret goal aligns with ADR-028. Not for r1.
3. **[LOW] SPE `FileStorageContainerType.Manage.All` privilege loosening** — H8 runbook footnote: owning-tenant bootstrap no longer requires SPE-Admin / Global-Admin role; simplifies the once-per-tenant cert-bootstrap step. Update the H8 runbook, not the handler.
4. **[LOW] SPE bulk permissions upsert / container-type audit log / container-type owner mgmt** — Not r1 scope; note as follow-ups.

---

## Recommendations

- **MUST-adopt (r1)**: none. r1 design.md v3.2 has NO stale patterns that block execution.
- **SHOULD-adopt (r1 wrap-up or Phase D)**: (a) capture the ApplicationAccessPolicy → RBAC-for-Apps migration as a first-class §14 phase entry with a target date bounded by Microsoft's future deprecation announcement; (b) add MI-as-FIC to the follow-on backlog with owner + expected work-size.
- **Can-defer**: SPE bulk permissions upsert, container-type audit log verification, container-type owner mgmt via `permissions` navigation — all are downstream capabilities, not blocking.
- **No-action**: Graph v6/Kiota 2 (still current); confidential-client for SPE (still current); UAMI over SAMI (still current); URL-based admin consent (still current); Terraform PP provider (validates D14, already in design); `pac admin` interim (still current).

---

## Sources

**Microsoft Learn — authoritative**
- [SharePoint Embedded — What's new (updated 2026-08-10)](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/whats-new) — monthly rollup, Sept 2025 → July 2026 material changes
- [SharePoint Embedded — Configure authentication and authorization (updated 2026-07-13)](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization) — confidential-client + certificate posture; `FileStorageContainerTypeReg.Selected` for registration
- [RBAC for Applications in Exchange Online (updated 2026-03-16)](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac) — explicit "replaces Application Access Policies"; migration steps; role catalog
- [Application Access Policies (legacy)](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies) — title now `(legacy)`; retained for reference
- [Overview of federated identity credentials](https://learn.microsoft.com/en-us/graph/api/resources/federatedidentitycredentials-overview) — 20-FIC-per-app cap, case-sensitive matching
- [Workload identity federation — configure app to trust MI](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity)
- [Microsoft Graph .NET SDK overview](https://learn.microsoft.com/en-us/graph/sdks/sdks-overview)

**Official Microsoft GitHub**
- [msgraph-sdk-dotnet releases](https://github.com/microsoftgraph/msgraph-sdk-dotnet/releases) — v6.5.0 latest (2026-08-06); v6.0.0 (2026-05-12) added net10.0, dropped net5.0, moved to microsoft-graph-core 4.x

**Terraform + PP**
- [microsoft/power-platform Terraform Registry](https://registry.terraform.io/providers/microsoft/power-platform/latest/docs) — v4.1.0 (2026-01-26)
- [microsoft/terraform-provider-power-platform GitHub](https://github.com/microsoft/terraform-provider-power-platform)

**Microsoft blogs**
- [MI-as-FIC GA announcement](https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets-ga/) — cross-tenant secretless pattern

**Prior researcher memory (project-scoped, consulted for baseline)**
- [`kiota-cve-2026-44503-tfm-2026-08-11.md`](../../../.claude/agent-memory/researcher/kiota-cve-2026-44503-tfm-2026-08-11.md) — Graph 6.5.0 / Kiota 2.0.0 baseline
- [`spe-ciam-crosstenant-apponly-brokering-2026-07-18.md`](../../../.claude/agent-memory/researcher/spe-ciam-crosstenant-apponly-brokering-2026-07-18.md) — app-only vs delegated matrix
- [`spe-dedup-content-identity-2026-07.md`](../../../.claude/agent-memory/researcher/spe-dedup-content-identity-2026-07.md) — custom columns GA lever
- [`ciam-user-provisioning-graph-2026-07-19.md`](../../../.claude/agent-memory/researcher/ciam-user-provisioning-graph-2026-07-19.md) — cross-tenant Graph identity mechanics
- [`spaarke-customer-stamp-pricing-2026-08-12.md`](../../../.claude/agent-memory/researcher/spaarke-customer-stamp-pricing-2026-08-12.md) — full-stack cost baseline

---

## Caveats

- **RBAC-for-Apps hard-cutover date** is not published. Microsoft's language is *"deprecation announced in the future."* Track quarterly.
- **MI-as-FIC** cross-tenant reference architecture ships in the Entra devblog and workload-id docs but has not yet appeared as a fully worked SPE / Dataverse example; if Spaarke adopts it, prototype cost is non-trivial.
- **Terraform PP provider** — some resources still preview-tagged; check per-resource stability before adopting beyond `powerplatform_environment` + `powerplatform_user`.
- SPE 24h replication window is not documented as an SLO; the "up to 24h" is empirical + community-source, not a Microsoft-published number. If we ever need a hard SLO for a customer contract, this is a talk-to-Microsoft item.

---

## Recommended follow-ups

1. **Add to §12 Risk Register**: RC-N — "ApplicationAccessPolicy → RBAC-for-Apps migration eventually mandatory; H14 will need rework; no hard cutover date but track Exchange team announcements."
2. **Add to §14 Phasing (Phase D or wrap-up)**: "H14 RBAC-for-Apps migration spike + first-customer cut-over."
3. **Add to follow-on backlog**: "MI-as-FIC cross-tenant secretless pattern spike" — owner TBD.
4. **Update H8 runbook**: note that `FileStorageContainerType.Manage.All` no longer requires SPE-Admin / Global-Admin (June 2026 change).
5. **Update §19 References**: add [SPE auth doc (2026-07-13)](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization), [RBAC-for-Apps](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac), [MI-as-FIC](https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets-ga/), [Terraform PP provider](https://registry.terraform.io/providers/microsoft/power-platform/latest/docs).
