# T232 — Model 1 guests usable: decisions (2026-10-07)

Plan §7 T232 (D2, G10). Scope: what H11 must do so that every Model 1 customer user can open the customer's Dataverse
environment, and who pays.

## Found (read-only mapping, 2026-10-07)

H11 invited guests and waited for redemption, then completed. Nothing else: no licence (assignLicense ran only for
NativeAccount; a missing SKU option was skipped silently and reported success), no usageLocation, no group membership,
no Dataverse systemuser, no role — `InterStepState.ProvisionedUsers` had no reader. DagAdvancer's comment "its users get
the solution's roles, imported by H6" was not implemented. Every H11 re-run re-POSTed every invitation, which re-sends
the invitation email. Model 1 could pick NativeAccount (the schema's Model 1 example did).

## Decisions

| # | Decision | Why |
|---|---|---|
| D1 | **Licensing = Power Platform pay-as-you-go** (owner, 2026-10-07): the operator links each customer environment to a billing policy on that customer's stamp subscription (`PRQ-C-11`). No per-user licences, no seat pool. | Owner choice (recommended): cost lands on the customer's own subscription (attribution for free), no seat management. Microsoft's PAYG FAQ: guests can use apps in a PAYG environment without licences. |
| D2 | A Model 1 run takes only `B2BGuest` (intake rule, `userprov-model1-requires-b2b-guest`). | Owner D2 — not a choice. |
| D3 | Every B2BGuest run names the environment's **security group** (`environmentSecurityGroupId`, `sprk-{customerId}-users`, `PRQ-C-10`). H11 refuses a group with another name or not security-enabled **before inviting anyone**, then adds each guest. | Isolation, decided by necessity: every Model 1 environment is in Spaarke's tenant; without a group any user of that tenant (another customer's guests included) is admitted on first sign-in. Required for B2BGuest (not "for Model 1") because H11's guest path needs it — Model 2 B2BGuest runs need it too. |
| D4 | The guest's Dataverse user is made through the **`azureactivedirectoryobjectid` alternate key** (`GET systemusers(azureactivedirectoryobjectid=…)` adds a group member on demand) + `systemuserroles_association/$ref` with the root-BU role — as the L2 Worker identity (System Administrator application user, PRQ-C-09). | Microsoft's documented app-callable path. A plain `POST systemusers` needs the system-required `domainname` and has no documented guest behaviour. The BAP "force sync" API needs a tenant-level Power Platform admin / management-app registration — not granted, not needed. |
| D5 | **Guest access** must be on (`organization.restrictguestuseraccess = false`, `PRQ-C-12`); H11 reads it and refuses before inviting. | New environments default to restricted, which blocks guests from Dataverse entirely. The Worker identity can read it app-only. |
| D6 | Roles: `H11UserProvisioningOptions:GuestSecurityRoleNames`, default `Spaarke Basic User` (root BU); every role resolved before any write; a missing role → `userprov-security-role-not-found` naming it. | The package that ships the roles is T218's; the name is configurable until then. |
| D7 | An existing guest (lookup by `mail`) is **reused without a second invitation email**; an address of a tenant **Member** is refused; a failed lookup fails closed. | Re-runs and gate re-checks no longer mail users again. |
| D8 | NativeAccount with no licence SKU configured → refused before any user is created (`userprov-license-sku-not-configured`). | R7 "validated but not wired": it created unlicensed users and reported success. |
| D9 | The skill checks `PRQ-C-10` and `PRQ-C-12` as the operator in Step 1e-bis (hard stop) and shows `PRQ-C-11` for confirmation. | Deviation from the POML's "Step 0.5": once-per-customer prerequisites are not run at Step 0.5 (deferred by design, EXEC-10); 1e-bis is where the group id and environment URL are known. H11 enforces C-10/C-12 again server-side; C-11 is not visible to L2 at all. |

## Known limits

- K1 — PAYG meter is **per active user per app per month** (Power Apps per app). Each Spaarke app a guest opens
  counts separately — the owner should know how many apps a typical guest opens.
- K2 — PAYG includes only 1 GB database + 1 GB file capacity per environment; tenant pooled capacity does not apply.
- K3 — `pac licensing` commands are preview; `get-environment-billing-policy`'s output shape (subscription id) is
  unverified — the skill shows it for the operator to confirm instead of parsing it.
- K4 — Live unknowns for T186: on-demand add of an unlicensed guest under PAYG via the alternate key (strongly implied by
  Microsoft, not stated); the HTTP status when the guest is not (yet) a group member; group-membership propagation delay.
- K5 — H11 never removes a guest, membership or role (no decommission — design D17).

Research record: `.claude/agent-memory/researcher/payg-b2b-guest-dataverse-user-provisioning-2026-10-07.md`.
