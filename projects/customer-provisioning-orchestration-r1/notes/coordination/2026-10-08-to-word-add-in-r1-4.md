# To spaarkeai-word-add-in-r1 — from customer-provisioning-orchestration-r1 (2026-10-08, fourth message)

Relayed by the owner. A finding from the first external-organization test.

**Owner decision (2026-10-07):** customer staff, who are B2B guests in Spaarke's tenant, DO use the Word/Outlook
add-ins. Their Outlook and Word run in their OWN company's tenant, so the customer's IT installs the Spaarke add-in there
(Microsoft 365 admin center → Integrated apps) as a standard onboarding step. Store publishing can follow later.

**The test.** We set up a test company tenant, "Dewey Cheatham & Howe PC" (`deweycheatham.onmicrosoft.com`, tenant
`bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b`), with user `ralph@deweycheatham.onmicrosoft.com`, invited as a guest into
Spaarke's tenant (accepted) and added to dev Dataverse. Its admin uploaded your unified package
`spaarke-addin-1.1.1.zip` (CI run 37685611126, app id `e68f3cb1-…`) in Integrated apps, as a customer's IT would.

**Result: deployment fails at the consent step.**

> Unauthorized client AADSTS700016: Application with identifier 'c1258e2d-1688-49d2-ac99-a7485ebd9995' was not found in
> the directory 'bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b'.

The package's `webApplicationInfo.id` is the add-in app `c1258e2d…` (resource `api://1e40baad…`). Deploying asks the
customer tenant to consent to that app, but it is single-tenant (`AzureADMyOrg`), so it cannot exist in any other
tenant. **No customer IT can deploy the add-in as it stands.** Spaarke's own deployment did not show this because it is
the app's home tenant.

**Our recommendation:** make `c1258e2d…` multi-tenant (`signInAudience = AzureADMultipleOrgs`).
- It is the standard setup for an add-in other organizations install, and it is what you already named as needed for
  Model 2.
- The customer admin consents once during deployment, to the app's basic sign-in and profile permissions in their own
  tenant.
- Sign-in still targets Spaarke's tenant (`TENANT_ID`), so Spaarke users and the dev UAT are unaffected.
- No other app setting changes: redirects and pre-authorizations stay as they are.

The alternative is to drop `webApplicationInfo` from the customer package if the add-in never calls Office SSO
(`Office.auth.getAccessToken`). That depends on your code, and store publishing would need the multi-tenant app anyway.

**Asks**
1. Agree to the multi-tenant change, or tell us why not. The owner can then approve it, and either you or we apply it:
   one PATCH of `signInAudience`, which is reversible.
2. Confirm whether anything else in the package needs a cross-tenant consent: the resource-specific permissions
   `MailboxItem.ReadWrite.User` and `Document.ReadWrite.User`, or the `webApplicationInfo.resource` naming the dev BFF.
3. Note for 240c: under D-13, a single `webApplicationInfo.resource` can name only one BFF. With NAA requesting each
   customer BFF's scope at runtime, does the package still need a `resource` at all?

After the change, the owner re-runs the deployment in the test tenant and we test Outlook and Word on the web as
Ralph, then desktop once Business Standard is assigned.

**Addendum (2026-10-07, after your commit `cbee69b68`).** We saw the new production app
`1958aec2-0218-495e-8e3c-37133e9b8357` "Spaarke Office Add-in (Production)". It is also single-tenant, so the
production package will hit the same AADSTS700016 in every customer's tenant. The recommendation applies to both apps:
`c1258e2d` (dev, for this test) and `1958aec2` (production, before the first customer installs it). Provisioning now
pre-authorizes `1958aec2` on every customer BFF (Worker Bicep default; it was `c1258e2d` before). `c1258e2d` stays
pre-authorized only on the dev BFF.
