# T263 — Per-customer Spaarke-tenant shared mailbox (H14m)

> **Status**: code + tests built 2026-10-10 (task 263). **Not live-proved.** The Exchange writes need an **owner decision**
> (§5, D32 proposed): `Spaarke Exchange Admin` today cannot run `New-Mailbox` or `Add-DistributionGroupMember`, so until the
> owner approves and an Organization Management admin applies prerequisite **PRQ-E-16**, H14m fails Resumable with a
> diagnostic that names the missing cmdlet, and **nothing is created**.
> Owner decision behind the task: #1562 (2026-10-10) — one Spaarke-tenant shared mailbox per customer, created and verified by
> a provisioning step after H14a.

## 1. Recommendation (one design)

H14 gets a **second in-process sub-step, H14m**, which runs after H14a in the same handler invocation. It needs H14a's
group-scoped grants in place before its authorization test can pass. H14m has three steps.

1. **Ensures the mailbox through the sidecar** (`POST /ensure-customer-mailbox`). It inspects first and creates only
   when nothing exists:
   - `New-Mailbox -Shared` creates the mailbox.
   - `Add-DistributionGroupMember -BypassSecurityGroupManagerCheck` adds it to the customer's scope group.
   - A read-back confirms the result.
   - `Test-ServicePrincipalAuthorization` proves that the stamp identity's three `Application Mail.*` roles reach it.
2. **Writes the stamp's `sprk_communicationaccount` row** in the stamp's Dataverse. The row is shared, send- and
   receive-enabled, the default sender, and `Verified`. H7b uses the same identity for its writes.
3. **H13 re-checks both, read-only** (`POST /read-customer-mailbox` plus a Dataverse read). H13 refuses Ready if the
   mailbox or the row is missing, unverified, out of scope, or foreign.

**L2 does not call the BFF's verify endpoint.** `POST /api/communications/accounts/{id}/verify` is a user endpoint
(`CommunicationAuthorizationFilter` + record filter: the caller must hold Write on the account). The L2 Worker holds no user.
An immediate Graph send/read test would also fail on a fresh stamp, because RBAC-for-Applications grants and membership
changes take **30 min – 2 h** to take effect ([application-rbac][rbac]: "the cache … 30 minutes to 2 hours").
`Test-ServicePrincipalAuthorization` evaluates the *configuration* and gives an answer at once (seen live in T251 spike S3,
2026-10-04). So "verified" here means **configured and authorized**, and the row's `sprk_verificationmessage` says so.

Nothing in the BFF waits on the status:
- `sprk_verificationstatus` is informational. `CommunicationAccountService` filters only on `sprk_sendenabled`,
  `sprk_receiveenabled` and `statecode`.
- `GraphSubscriptionManager` subscribes every receive-enabled account and retries itself.
- `InboundPollingBackupService` polls every 5 minutes.

A user can re-run the BFF's own verify at any time.

**No BFF change.** This keeps the task out of the BFF hot path.

## 2. Naming

All mailbox names derive from `customerId` (`^[a-z][a-z0-9]{2,7}$`):

| Field | Value | Source |
|---|---|---|
| Exchange `Name` and `Alias` | `sprk-{customerId}-mail` | derived (`CustomerMailboxNaming.MailboxName`) — injective, ≤ 18 chars |
| `DisplayName` | intake `displayName` | intake (T259; 1–160 chars, already validated at `POST /api/runs`) |
| `PrimarySmtpAddress` | intake **`communicationDefaultMailbox`** | intake (T245c; required; H4 writes it to KV `Communication-DefaultMailbox`) |
| Scope group `Name` (checked, not created) | `Spaarke-AppAccess-{customerId}` | PRQ-C-08 naming, now **binding** (§4) |

**The address is a deviation from the POML** ("address derived from customerId"). Deriving the address too would give
`Communication-DefaultMailbox` a second producer. The operator's intake value would feed H4 while a derived value fed
H14m, and the BFF's default sender could then differ from the mailbox provisioning created. That breaks the rule of one
producer per input (`provisioning.md` "Run-context contract"). Using the existing required intake value keeps one
producer, and it settles the domain question too. The domain must be an accepted domain of Spaarke's tenant, and Exchange
refuses `New-Mailbox` otherwise. `spaarke.onmicrosoft.com` always qualifies, and a verified vanity domain also works.

## 3. Where it runs and how it behaves

- **Position**: H14m is an in-process sub-step of H14. It is not dispatchable, like H14a (`HandlerIds.Dispatchable`
  unchanged). The run order is H14a, then H14m. H14m is not attempted when H14a fails, because its authorization test
  needs H14a's assignments. The DAG is unchanged (H12c → H14 → H13).
- **Idempotency**: the key is `h14-{customerId}-mailbox-{sha256(name|address|scopeGroup|appId|dataverseUrl)}`. The parent
  skips a sub-step whose key is already recorded.
- **A second run writes nothing**:
  - The sidecar finds the compliant mailbox and returns `AlreadyCompliant`, with no `New-*` or `Add-*` call.
  - The Dataverse step finds exactly one `Verified` row and sends no PATCH or POST.
  - Tests pin both.
- **Get-before-set (T4 semantics)**: everything is inspected before any write. The ensure returns **Drift**, writes
  nothing, and H14 quarantines (`h14m-mailbox-drift`) when:
  - a recipient matching the name, alias or address exists but is not a `SharedMailbox`;
  - such a recipient has a different name, alias or address. This covers another customer's mailbox or a person's
    mailbox at that address;
  - more than one recipient matches;
  - the mailbox exists but is **outside** the scope group;
  - the mailbox is a member of **any other** group;
  - the scope group's name is not `Spaarke-AppAccess-{customerId}`.
- **Permission preflight**: Exchange Online loads only the cmdlets and parameters the caller's roles allow. Before any
  write, the ensure checks that `New-Mailbox -Shared` and `Add-DistributionGroupMember -BypassSecurityGroupManagerCheck`
  are present. If either is missing, the result is `Failure`, nothing is created, and the diagnostic names PRQ-E-16.
  This prevents a half-made mailbox, created but never joined to the group, when only one of the two roles has been
  applied.
- **Unverified after a write**: when Exchange shows the mailbox in the group but `Test-ServicePrincipalAuthorization`
  does not yet report every role `InScope`, the result is Resumable `h14m-mailbox-unverified` and no Dataverse row is
  written. The re-run finds the compliant mailbox, writes nothing, and tests again, so the step converges.
- **Dataverse row** (`sprk_communicationaccounts`, matched by `sprk_emailaddress`; the identity is the BFF app
  registration through the FR-39 chain, the same as H7/H7b):

  | Rows found | What H14m does |
  |---|---|
  | none | POST `sprk_name` = display name, `sprk_emailaddress`, `sprk_displayname`, `sprk_accounttype` = Shared (100000000), `sprk_authmethod` = App-Only (100000000), `sprk_sendenabled`, `sprk_receiveenabled`, `sprk_isdefaultsender` = true, `sprk_securitygroupid` = scope group, `sprk_verificationstatus` = Verified, `sprk_lastverified`, `sprk_verificationmessage` |
  | one, active, shared, `Verified` | adopt it; nothing written |
  | one, active, shared, status empty or `Pending` | PATCH only the three verification fields |
  | one, `Failed` | Resumable `h14m-account-verification-failed`. The BFF's real Graph test failed (usually a user verified within the 30 min – 2 h window). Re-verify in the app, then resume. Never overwritten. |
  | one inactive, one of another type, or more than one | QuarantineRequired `h14m-account-row-conflict`, nothing written |

  Operator flags on an adopted row (send/receive/default) are never rewritten.
- **H13 (step 7c)**: `CustomerMailboxVerifier` returns `Failed`, and H13 quarantines with
  `h13-customer-mailbox-failed`, when:
  - the mailbox is missing;
  - a sidecar conflict is reported;
  - a role is not in scope;
  - the stamp has no active shared row, or more than one;
  - the row is not `Verified`.

  An unreachable sidecar or Dataverse, or a missing input, gives `Inconclusive`, and H13 fails Resumable with
  `h13-customer-mailbox-inconclusive`.

## 4. Scope group membership

H14a scopes the stamp identity's mail roles to the intake group `exchangePolicyScopeGroupId`. **Only direct members
count** ([application-rbac][rbac]). Microsoft Graph cannot change a mail-enabled security group: such groups are
"read-only" in Graph ([groups-overview][graph-groups]; [group-post-members][graph-members]: "supports only security
and Microsoft 365 groups"). Membership therefore goes through Exchange (`Add-DistributionGroupMember`) via the sidecar.

This **supersedes part of the 2026-10-01 rule (T245c)** that L2 never edits the group. L2 now adds exactly **one** member,
its own `sprk-{customerId}-mail`. It never removes a member and never adds anything else. The customer still decides
which *other* mailboxes the stamp may reach.

**New tenant-isolation check**: the scope group's `Name` must be `Spaarke-AppAccess-{customerId}`. A run whose intake
names another customer's group is refused (Drift) **before** this customer's mailbox would join it. Joining would let
the other customer's stamp read and send as this mailbox. Group names are unique in an Exchange organization. The name is
also the filter of the membership scope in §5, so the Exchange role and the code agree.

## 5. Exchange permissions — **OWNER DECISION NEEDED (proposed D32)**

The current role, `Spaarke App RBAC Admin`, is a child of Role Management trimmed to 14 cmdlets. With
`View-Only Recipients`, it covers every read H14m and H13 need: `Get-Recipient` (View-Only Recipients),
`Test-ServicePrincipalAuthorization` (`Spaarke App RBAC Admin`). It does **not** cover `New-Mailbox` or
`Add-DistributionGroupMember`. The owner constraint forbids widening the role in code or defaults, so the code does not
widen it. The narrowest set found is two new custom roles and one scope, assigned with `-App` (non-delegating):

| Need | Cmdlet / parameters | Built-in parent (to confirm with step 0) | Scope |
|---|---|---|---|
| Create the shared mailbox | `New-Mailbox` with only `Shared, Name, Alias, DisplayName, PrimarySmtpAddress` | `Mail Recipient Creation` ([mail-recipient-creation][mrc]; [new-mailbox][new-mailbox]: "The Shared switch is required to create shared mailboxes") | organization (a creation scope is unverified — step 6) |
| Join it to the scope group | `Add-DistributionGroupMember` with only `Identity, Member, BypassSecurityGroupManagerCheck` | `Security Group Creation and Membership` ([add-distributiongroupmember][adgm]: the bypass for mail-enabled security groups needs "Organization Management … or … the Security Group Creation and Membership role") | `Spaarke Customer Scope Groups` = `RecipientTypeDetails -eq 'MailUniversalSecurityGroup' -and Name -like 'Spaarke-AppAccess-*'` |

Not requested:
- `Remove-Mailbox`: decommission is manual in r1 (§7).
- `Remove-DistributionGroupMember`.
- `Set-Mailbox`: it would let the app stamp a marker onto *any* mailbox.
- `Get-Mailbox`, `Get-DistributionGroupMember`: `Get-Recipient` covers both.

The one-time setup, run by an Organization Management admin in `Connect-ExchangeOnline`, is PRQ-E-16:

```powershell
$app = '<Spaarke Exchange Admin client id>'   # the -App value used for its existing assignments (PRQ-E-15)

# 0. Discovery — confirm the parents and the parameter sets BEFORE creating anything
Get-ManagementRoleEntry '*\New-Mailbox' -Parameters Shared | Format-Table Role,Name
Get-ManagementRoleEntry '*\Add-DistributionGroupMember' -Parameters BypassSecurityGroupManagerCheck | Format-Table Role,Name

# 1. Create role: New-Mailbox, shared parameter set only
New-ManagementRole -Name 'Spaarke Shared Mailbox Create' -Parent 'Mail Recipient Creation' -EnabledCmdlets New-Mailbox
Set-ManagementRoleEntry 'Spaarke Shared Mailbox Create\New-Mailbox' -Parameters Shared,Name,Alias,DisplayName,PrimarySmtpAddress

# 2. Membership role: Add-DistributionGroupMember only
New-ManagementRole -Name 'Spaarke Scope Group Membership' -Parent 'Security Group Creation and Membership' -EnabledCmdlets Add-DistributionGroupMember
Set-ManagementRoleEntry 'Spaarke Scope Group Membership\Add-DistributionGroupMember' -Parameters Identity,Member,BypassSecurityGroupManagerCheck

# 3. Scope: only the customers' scope groups
New-ManagementScope -Name 'Spaarke Customer Scope Groups' -RecipientRestrictionFilter "RecipientTypeDetails -eq 'MailUniversalSecurityGroup' -and Name -like 'Spaarke-AppAccess-*'"

# 4. Assignments (the -App parameter set has -CustomResourceScope, not -CustomRecipientWriteScope)
New-ManagementRoleAssignment -Name 'sprk-exoadmin-sharedmbx-create' -App $app -Role 'Spaarke Shared Mailbox Create'
New-ManagementRoleAssignment -Name 'sprk-exoadmin-scopegroup-member' -App $app -Role 'Spaarke Scope Group Membership' -CustomResourceScope 'Spaarke Customer Scope Groups'
```

Citations:
- [new-managementrole][nmr]: `-Parent`, `-EnabledCmdlets` "specifies the cmdlets that are copied from the parent role".
- [create a role][create-role]: "Child roles can't have management role entries that don't exist in the parent role".
- [set-managementroleentry][smre]: `-Parameters` with neither `-AddParameter` nor `-RemoveParameter` keeps only the
  listed parameters.
- [new-managementscope][nms] and [recipientfilter-properties][rfp]: `RecipientTypeDetails`, `Name` with wildcards.
- [new-managementroleassignment][nmra]: `-App` with `-CustomResourceScope`.
- [app-only-auth][app-only]: the only cmdlets app-only excludes are `*-UnifiedGroup`, `*-UnifiedGroupLinks` and eDiscovery.
- [find-exchange-cmdlet-permissions][find-perms].
- A shared mailbox needs no licence up to 50 GB ([about-shared-mailboxes][shared]).

**Residual risk.** With these roles the Worker could:
- create any shared mailbox, until a creation scope is proved (step 6 below);
- add any recipient to any `Spaarke-AppAccess-*` group. Adding a Spaarke staff mailbox to a customer's group would let
  that customer's stamp read it.

Mitigations:
- Only the L2 Worker can obtain the app's token (federated credential, D24).
- The code adds only `sprk-{customerId}-mail`, and the sidecar validates the name and pattern before any write.
- Audit `New-Mailbox` and `Add-DistributionGroupMember` in the unified audit log.

This is narrower than the residual risk already accepted for PRQ-E-15: that role can grant any app organization-wide
mail roles.

**Live checks owed before T186** (owner-approved, as the app, after the assignments are applied; RBAC changes take 30 min – 2 h):

| # | Check | Pass |
|---|---|---|
| M1 | Step 0 shows the two parents | `New-Mailbox -Shared` in Mail Recipient Creation; the bypass parameter in Security Group Creation and Membership |
| M2 | `New-Mailbox` **without** `-Shared` as the app | refused (only the Shared parameter set is completable) |
| M3 | `Add-DistributionGroupMember` on a group **not** named `Spaarke-AppAccess-*` | refused |
| M4 | The ensure route against a test customer | `Success`; second call `AlreadyCompliant`; `Test-ServicePrincipalAuthorization` `InScope True` for the 3 roles |
| M5 | `Get-Recipient -Filter "Members -eq '<mailbox DN>'"` (the "no other group" rule; the in-scope-group rule uses the documented `MemberOfGroup` filter) | returns the scope group. If it returns nothing, the "no other group" rule is inert (the in-scope rule still holds) — replace it with a per-group check over `Get-Recipient -RecipientTypeDetails MailUniversalSecurityGroup -Filter "Name -like 'Spaarke-AppAccess-*'"` + `MemberOfGroup` before relying on it |
| M6 | (optional) creation scope `Alias -like 'sprk-*-mail'` on the create assignment | a create outside the pattern is refused. If it is, add `-CustomResourceScope` to the create assignment |
| M7 | `Get-User <mailbox>` → `AccountDisabled` | record it. Learn conflicts on whether sign-in is blocked by default. Owner decides whether to require a disabled account (that needs `Set-User` — not requested) |

## 6. Identities

| Call | Identity | Endpoint |
|---|---|---|
| Ensure / read mailbox | L2 Worker → `Spaarke Exchange Admin` token (MI-FIC, D24) → sidecar `Connect-ExchangeOnline -AccessToken` | `POST http://127.0.0.1:8091/ensure-customer-mailbox`, `/read-customer-mailbox` |
| Authorization test | same (`Test-ServicePrincipalAuthorization -Identity <stamp UAMI client id> -Resource <mailbox>`) | inside the ensure / read |
| Account row | BFF app registration of the stamp (FR-39 chain; MI-FIC on secret-free Workers) — System Administrator application user (H10) | `{dataverseEnvUrl}/api/data/v9.2/sprk_communicationaccounts` |
| Mail at runtime | stamp UAMI (`GraphClientFactory.ForApp()`), reaching the mailbox through H14a's group-scoped roles | Graph `/users/{address}/…` |

## 7. Decommission (r1: manual; automation is r2 "registry-aware decommission")

Run as an Organization Management admin:

1. `Remove-DistributionGroupMember -Identity 'Spaarke-AppAccess-{customerId}' -Member 'sprk-{customerId}-mail' -BypassSecurityGroupManagerCheck`
2. `Remove-Mailbox 'sprk-{customerId}-mail'`. The mailbox stays soft-deleted for 30 days.
3. Deactivate the `sprk_communicationaccount` row. Do not delete it: deactivate rather than delete, the same rule as the registry rows.

L2 is not granted `Remove-*`. A role nobody runs is a role nobody needs ("needed → build, else remove").

## 8. Known limits (K-class)

- **K2: a crash between `New-Mailbox` and `Add-DistributionGroupMember`** inside one sidecar request leaves a mailbox
  outside the group. The next run quarantines, because the POML acceptance criterion requires a same-named mailbox
  outside the scope group to quarantine. The diagnostic gives `WhenCreated` so the operator can tell it was this run.
  Fix: add it to the group, then clear the quarantine. The permission preflight removes the common cause (a role that
  was never applied), and the join is retried inside the request (6 × 10 s) to cover a new mailbox that has not
  replicated yet.
- **K2: verification is configuration-level.** Graph effect lags 30 min – 2 h. The BFF's own verify (user) and its
  subscription retry cover the effect.
- **Not built**:
  - hiding the mailbox from the address book (needs `Set-Mailbox`; Model 1 guests are not given the GAL — owner may
    revisit);
  - a vanity mail domain (operator's choice, through `communicationDefaultMailbox`).
- **Graph change notifications on a shared mailbox under an RBAC-only grant** are not documented. This was T251's
  open S4. The BFF's 5-minute polling covers inbound mail regardless. Prove it at T186.

## 9. Owner decisions needed

1. **D32 (proposed)**: approve PRQ-E-16, the two custom Exchange roles plus one scope in §5. Exact cmdlets and
   parameters as listed. Then an Organization Management admin applies it and runs live checks M1–M7.
   **Until then H14m fails Resumable at every run (T186 included) and creates nothing.** The mail model (#1562) cannot be
   met without some Exchange write permission. Recommend yes.
2. Confirm the address source is intake `communicationDefaultMailbox` (§2) and not a derived address. Recommend yes:
   one producer.
3. Confirm the scope-group name rule `Spaarke-AppAccess-{customerId}` becomes binding (PRQ-C-08). Recommend yes: it
   closes the cross-customer group mix-up for the mailbox step.

[rbac]: https://learn.microsoft.com/exchange/permissions-exo/application-rbac
[graph-groups]: https://learn.microsoft.com/graph/api/resources/groups-overview
[graph-members]: https://learn.microsoft.com/graph/api/group-post-members
[mrc]: https://learn.microsoft.com/exchange/mail-recipient-creation-role-exchange-2013-help
[new-mailbox]: https://learn.microsoft.com/powershell/module/exchangepowershell/new-mailbox
[adgm]: https://learn.microsoft.com/powershell/module/exchangepowershell/add-distributiongroupmember
[nmr]: https://learn.microsoft.com/powershell/module/exchangepowershell/new-managementrole
[create-role]: https://learn.microsoft.com/exchange/create-a-role-exchange-2013-help
[smre]: https://learn.microsoft.com/powershell/module/exchangepowershell/set-managementroleentry
[nms]: https://learn.microsoft.com/powershell/module/exchangepowershell/new-managementscope
[rfp]: https://learn.microsoft.com/powershell/exchange/recipientfilter-properties
[nmra]: https://learn.microsoft.com/powershell/module/exchangepowershell/new-managementroleassignment
[app-only]: https://learn.microsoft.com/powershell/exchange/app-only-auth-powershell-v2
[find-perms]: https://learn.microsoft.com/powershell/exchange/find-exchange-cmdlet-permissions
[shared]: https://learn.microsoft.com/microsoft-365/admin/email/about-shared-mailboxes
