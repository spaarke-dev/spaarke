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
| D9 | The skill checks `PRQ-C-10` (the group's name AND that it is the group set on the environment — Power Platform admin API, `linkedEnvironmentMetadata.securityGroupId`), `PRQ-C-12` and `PRQ-C-11` (subscription id matched in the `pac licensing` output; batch stops when absent, interactive asks) as the operator in Step 1e-bis, each a hard stop, every `az`/`pac` exit code checked. | Deviation from the POML's "Step 0.5": once-per-customer prerequisites are not run at Step 0.5 (deferred by design, EXEC-10); 1e-bis is where the group id and environment URL are known. H11 re-checks the group's NAME and guest access server-side; the environment's group binding and its billing are visible only to a Power Platform admin — L2 is not one (D4). |
| D11 | Verifier round 2 (2026-10-07): PRQ-C-11 needs `Enabled` too and interactive always confirms; the environment is read by its id (no paged list); the subscription id is normalised before matching; the repeated-email rule applies to guests only (and the skill checks it locally); a consent-verifier timeout is a named failure; a role-resolution failure that is not a missing role uses `userprov-dataverse-user-failed`; Graph error-code extraction never throws; a Worker boot test pins the options validation. | NEW-F1…F8. |
| D10 | Code-review round 1 (2026-10-07): roles resolved ONCE before anyone is invited (they used to be resolved per guest after the invitations); the role list default applied after binding (the binder appends to an initialised list); options validated at start; GUIDs read from Dataverse canonicalized (ADR-044); HTTP timeouts are named failures; Graph error text reduced to its code (D15); a repeated email refused at intake; group id only in the canonical `D` form (schema parity). | F1–F7, F9–F12, F15; ADR-check V1 + W2/W3/W5/W6/W8. |

## Known limits

- K1 — PAYG meter is **per active user per app per month** (Power Apps per app). Each Spaarke app a guest opens
  counts separately — the owner should know how many apps a typical guest opens.
- K2 — PAYG includes only 1 GB database + 1 GB file capacity per environment; tenant pooled capacity does not apply.
- K3 — `pac licensing` commands are preview; `get-environment-billing-policy`'s output shape is unverified (T186). The
  skill looks for the stamp subscription id AND `Enabled` in the text: batch stops when either is absent; interactive
  always asks the operator to confirm.
- K4 — Live unknowns for T186: on-demand add of an unlicensed guest under PAYG via the alternate key (strongly implied by
  Microsoft, not stated); the HTTP status when the guest is not (yet) a group member; group-membership propagation delay.
- K5 — H11 never removes a guest, membership or role (no decommission — design D17).
- K6 — The group-binding check (skill) reads the environment id from Dataverse (`RetrieveCurrentOrganization` →
  `Detail.EnvironmentId`), then ONE environment from the BAP admin API (`linkedEnvironmentMetadata.securityGroupId`);
  those response shapes are not verified live (T186) — a wrong shape is a false hard stop, never an admission. H11 itself can check only the group's name — Entra display names are not
  unique, so the skill's binding check is the real gate.
- K7 — A guest found by mail who never redeemed (or was created by another route) keeps the `b2b-consent` gate
  pending; H11 never re-invites (that would re-send mail on every re-check). Operator path: resend the invitation from
  the Entra admin center (Users → the guest → Resend invitation), then resume H11.
- K8 — A systemuser that already exists in a child business unit cannot take a root-business-unit role → the run stays
  Resumable `userprov-dataverse-user-failed` naming the entry; the operator moves the user to the root unit.
- K9 — Each seam call builds a `DefaultAzureCredential` (the existing L2 idiom), so tokens are not cached across calls —
  about 3 token requests per guest. Acceptable at ≤ 500 users per run; revisit if runs grow.
- K10 — The tests sit under `src/server/services/Sprk.Provisioning.ControlPlane.Tests/` (the L2 suite's existing home),
  not an ADR-038 KEEP path — the project-close `/test-diet` will see them there.

Research record: `.claude/agent-memory/researcher/payg-b2b-guest-dataverse-user-provisioning-2026-10-07.md`.
