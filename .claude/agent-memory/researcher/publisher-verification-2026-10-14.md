---
name: publisher-verification-2026-10-14
description: Entra publisher verification requirements, setVerifiedPublisher is delegated-only (no app-only), consent impact, per-customer BFF app implications (ISS-026 / #1811)
metadata:
  type: reference
---

## 2026-10-14: Entra publisher verification for multitenant apps (ISS-026, #1811)
**Question**: What does it take to mark Spaarke's multitenant apps publisher-verified (Partner ID 7011236), can provisioning do it app-only, and what happens to unverified apps at consent?
**Findings**: Needs a verified CPP account that is the Partner Global Account (location IDs rejected: MPNGlobalAccountNotFound), tenant associated to the PGA (Partner Center Tenants page, Global admin), app has publisherDomain not *.onmicrosoft.com, and Partner Center primary-contact email domain == publisherDomain or a DNS-verified tenant domain. Human must hold Entra App Admin/Cloud App Admin + Partner Center CPP/MPN Admin or Accounts Admin, sign in with MFA in-session. Graph `POST /applications/{id}/setVerifiedPublisher` (v1.0 and beta) lists Application permissions = NOT SUPPORTED; delegated Application.ReadWrite.All only -> a managed identity cannot call it; per-customer apps need an operator MFA step (batchable in one session). Unverified multitenant apps: consent prompt shows "Unverified"; risk-based step-up (default on, only when user consent enabled) pushes risky user consent to admin; admin consent (adminconsent URL, portal) is not blocked by any doc. publisherDomain cannot be set via API (doc 2023) but defaults to tenant primary verified domain. No documented cap on apps per Partner ID; a tenant can associate with only one Partner Center account; B2C/national clouds unsupported; undocumented risk heuristics (UnableToAddPublisher) can reject.
**Sources**:
- https://learn.microsoft.com/en-us/entra/identity-platform/publisher-verification-overview
- https://learn.microsoft.com/en-us/entra/identity-platform/mark-app-as-publisher-verified
- https://learn.microsoft.com/en-us/entra/identity-platform/troubleshoot-publisher-verification
- https://learn.microsoft.com/en-us/graph/api/application-setverifiedpublisher?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/configure-risk-based-step-up-consent
- https://learn.microsoft.com/en-us/entra/identity-platform/howto-configure-publisher-domain
- https://learn.microsoft.com/en-us/partner-center/account-settings/multi-tenant-account
**Open questions**: Is 7011236 the PartnerGlobal-type ID (not a location ID)? Does the Partner Center primary-contact email domain equal spaarke.com? Does a verified client SPA suffice for customers when the per-customer BFF API app (resource) is unverified (inference: prompt shows the client's publisher; untested)? Rate/risk behaviour of looping many setVerifiedPublisher calls is undocumented.
