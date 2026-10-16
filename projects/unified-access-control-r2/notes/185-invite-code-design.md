# Task 185: invite-code proof for the E11 email bind (design note)

> **Status:** DRAFT for owner review (owner round 97: design and owner review come before any code). Nothing is implemented.
> **Task:** `tasks/185-invite-code-proof-for-e11-email-bind.poml` (FULL rigor; tags auth, security, tenant-isolation).
> **Production gate:** provisioning's **T240d**. No production customer onboards external contacts until 185 ships.
> **Built on:** task 184 / PR #1801, merged to master as `06b3654a0`.
> - The code cited below is master at that commit. There has been no change since in `Infrastructure/ExternalAccess`, `Api/ExternalAccess`, `Services/Registration`, the schema script or `src/client/external-spa` (checked 2026-10-13, `git diff 06b3654a0 origin/master` is empty for those paths).
> - **This worktree's branch does NOT contain `06b3654a0`.** It has no `InviteMarkerWindow`. The POML's "branch from master" is therefore required, not optional.

## 0. Recommendations at a glance

| # | Decision | Recommendation |
|---|---|---|
| 1 | Code | 32 bytes (256 bits) from `RandomNumberGenerator`, base64url without padding (43 chars), carried as `invite=` in the portal link of BOTH invite emails |
| 2 | Hash | **Salted SHA-256**, domain-separated and bound to the contact id. No HMAC: no new Key Vault secret, no key rotation, no Key Vault on the sign-in path, and no loss of protection (§2) |
| 3 | Column | `contact.sprk_externalinvitecodehash`, text, max 100 chars, field-secured. Readers: no read. Writers (BFF only): read/create/update. Auditing off. New script `scripts/Set-ExternalInviteCodeHashSchema.ps1`. Written in the SAME PATCH as `sprk_externalinvitedon` |
| 4 | SPA carriage | Query string → sessionStorage (and the URL is cleaned at once) → request header `X-Spaarke-Invite-Code` on CIAM-plane BFF calls, until the first 2xx |
| 5 | Binding | Marker leg = marker current (30 days) **and** code hash matches (`FixedTimeEquals`). The bind PATCH clears the hash in the same request, under `If-Match`. Missing or wrong code → `contact_not_invited`. No attempt counter |
| 6 | Grant-only leg | **(b): the grant leg stops being a bind leg.** Only an invite binds |
| 7 | Unchanged | The new-account path (create and bind in one request, no code) and the workforce/systemuser planes |
| 8 | Migration | Dev: **0** pending markers (0 contacts carry `sprk_externalinvitedon`), **0** grant-only contacts that can bind. Release note: re-invite pending contacts |

## 1. The invite code

**Format.**
- `RandomNumberGenerator.GetBytes(32)`, which is 256 bits from the OS CSPRNG.
- Encoded with `Base64Url` (.NET 9+ `System.Buffers.Text.Base64Url`), no padding. That gives exactly 43 characters from `[A-Za-z0-9_-]`, which is URL-safe without escaping.
- The task asks for at least 128 bits. 256 bits costs nothing (21 more characters in a link) and makes §5's no-counter argument hold with a very wide margin. Owner question Q7 offers 128.
- Each invite generates a new code. The code is never logged, and never stored except as the hash (§2). The one other place it ends up is the sent email (§1.3).

**One helper (new, pure, static): `Infrastructure/ExternalAccess/InviteCode.cs`.**
- `Create()`: returns a new code and its stored hash.
- `IsWellFormed(string?)`: exactly 43 base64url characters.
- `Matches(presented, storedHash, contactId)`: the constant-time check (§5).
- `InviteCodeSecret`: a wrapper type whose `ToString()` returns `"[redacted]"`. Both the presented code and the stored hash travel as this type. A compiler-generated record `ToString()` (`BindingRequest`, `BindIntentLookup`) then cannot print them. This is enforcement by type (CLAUDE.md §16 ladder).

### 1.1 Where the code goes in the link (both emails)

The link builder copies `GrantAccessNotifier.BuildPortalLink` (`Api/ExternalAccess/GrantAccessNotifier.cs:409-431`), the existing trusted-config link pattern:
- It is built only from `ExternalAccess:PortalUrl` (https, no user info), this stamp's customer key (`CustomerIdentity`, when resolved) and the code. Each value is URL-encoded.
- `{portal}/?customer={key}&invite={code}`, or `{portal}/?invite={code}` when the customer key is not resolved (dev before `Customer:Id`). This differs from `BuildPortalLink`, which returns null without a key: an invite must still be sendable on dev.
- The SPA's `?customer=` handling (`backend-selection.ts:375`, `readCustomerQueryKey`) already selects the backend from this parameter.

**Templates.** In `Services/Registration/EmailTemplates/`:
- `CiamOnboardingTemplate.html` and `CiamExistingAccountTemplate.html` (lines 45, 57 and 66 use `{{PortalUrl}}`) gain `{{InviteUrl}}` for every `href`.
- The visible link text stays `{{PortalUrl}}`, so the code is not printed as text.
- `{{InviteUrl}}` is HTML-attribute-encoded (`WebUtility.HtmlEncode`, as `SendPortalNotificationEmailAsync` does). `{{PortalUrl}}` is inserted un-encoded today because it is trusted config, but the link now carries a query.

**Callers.**
- `RegistrationEmailService.SendCiamOnboardingEmailAsync` (`:203-232`) and `SendCiamExistingAccountEmailAsync` (`:242-271`) take the built invite link as a new parameter.
- `InviteExternalUserEndpoint.ProvisionAsync` passes it. That method serves `/invite` and `/invite-and-grant`.

**The response.** `InviteExternalUserResponse` and `InviteAndGrantResponse` keep returning the BASE `portalUrl`. The admin's API response never carries the code (a test pins this, §9).

### 1.2 When the code is created and written

**One write.**
- `ContactIdentityBinder.MarkInviteSentAsync` (`ContactIdentityBinder.cs:405-422`) creates the code.
- It writes the marker AND the hash in ONE PATCH, through `IContactIdentityStore.MarkInvitedAsync`, which gains a hash argument. Its payload is `InviteMarkerPayload`, `ContactIdentityStore.cs:382-386`.
- The write happens where 184 writes the marker: before the CIAM create (`InviteExternalUserEndpoint.cs:258`), on every path that provisions.
- It returns the code to the endpoint for the email only.

**Re-invite.** It overwrites both columns: a new marker time, a new salt and a new hash. So a re-invite refreshes the 30-day window AND makes every earlier code fail (the "superseded" case). `AlreadyProvisioned` still writes nothing.

**A stamp that has the marker column but not the hash column** (Deploy-Release Phase 2 before Phase 3):
- The combined PATCH fails on the hash column. `IsWriteColumnMissing` (`:404-409`) is extended to match EITHER column name, so this answers `ColumnMissing`.
- A new-account invite proceeds as in 184: bound in the same request, no code needed.
- ExistingAccount, and a failed oid write, answer 184's retryable 503 `invite_marker_not_written`. A marker without a hash can never bind under 185, so it is not written on its own.

### 1.3 Where the plaintext code also ends up (owner question Q4)

`RegistrationEmailService` sends through `CommunicationService.SendAsync` (shared mailbox). That path stores two copies:
- **The email body**, in `sprk_communication.sprk_body` of THIS environment (`CommunicationService.cs`, Step 5 "Create Dataverse communication record"; the body is `request.Body`).
- **The shared mailbox's Sent Items** (`EmailChannelSender.cs:73`, `SaveToSentItems = true`).

Both hold the link, and so the code.

**Assessment.** To use a stored copy, the attacker needs to (i) own a CIAM account with the invitee's email (the squat, which needs `User.Create` in `spaarkeextid`) AND (ii) read THIS environment's `sprk_communication` or this stamp's mailbox. Provisioning's three named attackers:
- **A compromised stamp X.** It cannot read Y's Dataverse (it has another app user) or Y's mailbox. Exchange RBAC is scoped to the customer's group, and H13 T4 checks "none outside".
- **The misused provisioner.** In environments it provisions it is an administrator, so it can already write `sprk_externalobjectid` directly.
- **A `spaarkeextid` admin.** It has no access to Y's Dataverse.

So the stored copies give none of them a capability it lacks. The same holds for an identity that can do both (a Spaarke-tenant Global/Exchange admin, or Y's own BFF identity), because each can already bind directly.

**Recommendation:** accept for 185, and file a follow-up issue for a "secret-bearing body" option in `CommunicationService`: persist a body with the invite link replaced, and `SaveToSentItems = false`. Those files belong to the email-communication project, and the change is defence in depth, not a gap 185 leaves open.

**Link scanners** (Safe Links and similar) fetch the URL but never sign in, so they cannot consume the code. Consumption happens only at an authenticated bind (§5).

## 2. The hash: salted SHA-256 (recommended) vs HMAC-SHA256 with a Key Vault key

### 2.1 The recommended construction

```
salt    = RandomNumberGenerator.GetBytes(16)               // per invite
digest  = SHA256( "spaarke.invite-code.v1\n"
                  || contactId.ToString("D") || "\n"
                  || salt || base64url-decode(code) )      // 32 bytes
stored  = "v1." + b64url(salt) + "." + b64url(digest)      // 3 + 22 + 1 + 43 = 69 chars
```

- **Domain separation** (`spaarke.invite-code.v1`): the digest cannot be confused with any other SHA-256 use.
- **The contact id is in the input.** A hash copied onto another contact never verifies, so one code opens one contact.
- **A fast hash is correct here.** PBKDF2/Argon2 exist to slow a guess at a low-entropy password. A 256-bit random code has nothing to slow down: one guess has probability 2^-256.
- **The decision stays pure and self-contained.** `Matches` needs no key and no I/O, so it runs inside `ContactBindingDecision`, as `IsInviteMarkerCurrent` does. HMAC would need the key passed into the pure decision, or a Key Vault read in the binder before every E11 decision.

### 2.2 ADR-028 / CLAUDE.md §9 assessment

| | Salted SHA-256 | HMAC-SHA256, Key Vault key |
|---|---|---|
| New identity / credential | **None.** No secret, no Key Vault read | **No new identity** (the BFF managed identity reads KV, ADR-028 MUST "DefaultAzureCredential … Key Vault"). But a **new Key Vault secret per environment**: dev and every stamp, which means H4 / canonical-secret-catalog work for provisioning |
| Who creates it | Nobody | **The owner** on dev; agents make no Key Vault writes. CLAUDE.md §9 requires reading `.claude/constraints/provisioning.md` "KV credential lifecycle" and ADR-028 A4/E-1–E-3 first. Stamps: H4 seeds it |
| Rotation | Nothing to rotate | Rotation breaks every outstanding code, unless the stored value carries a key id and verification tries the previous key for 30 days (more code and more state). The precedent `TrackingTokenSigner` deliberately does not support dual keys (its remarks) |
| A Dataverse READ leak of the hash (backup, export, a System Administrator, an audit row) | Salt + digest. Inverting it costs 2^256 work: **nothing usable** | The digest only, and the key is also needed: **nothing usable** |
| A Dataverse WRITE by someone who can bypass the field security | A System Administrator can store SHA-256 of a code they chose. **But** the System Administrator field-security profile can also write `sprk_externalobjectid` directly (platform default; dev's "Spaarke Demo" team is #1343, recorded as O-184-2). So this adds no capability | Forgery needs the key. But the same System Administrator can still bind directly |
| Availability | No new dependency | A Key Vault outage makes every invite and every invite bind fail (closed) |

**Existing keys checked for reuse.**
- **`TrackingTokenSigner`** (`Services/Communication/Tracking/TrackingTokenSigner.cs`) reads an HMAC key from Key Vault by secret name, through `SecretClient` over the central `TokenCredential`, with `KeyVaultUri` and a 10-minute cache. Its key is optional, per tenant and off by default (`Communication:TrackingFooter`).
- **The webhook keys** (`Communication:WebhookSigningKey` / `communication-webhook-signing-key`, `compose-webhook-signingkey`, `Email-WebhookSigningKey`, `Onboarding:HmacSigningKey`) are KV references shared with the systems that call the webhooks.
- Reusing any of them would break key separation, and would make sign-in depend on another feature's configuration. **None is reusable.**

**If the owner picks HMAC anyway:**
- the secret would be `ExternalAccess-InviteCodeHashKey` (32 random bytes, base64) in the BFF's vault (`KeyVaultUri`);
- it would be read the way `TrackingTokenSigner` reads its key, or bound as an App Service Key Vault reference (the `@Microsoft.KeyVault(VaultName=…;SecretName=…)` pattern in `appsettings.template.json`);
- creating it on dev and adding it to H4's catalog is an owner/provisioning action, per `provisioning.md` "KV credential lifecycle".

**Recommendation: salted SHA-256.** No ADR-028 conflict arises under it: there is no new credential and no Key Vault access, so §6.5 does not apply.

## 3. The column

| Property | Value |
|---|---|
| Logical / schema name | `sprk_externalinvitecodehash` / `sprk_ExternalInviteCodeHash` |
| Display name | "External invite code hash" |
| Type | Single line of text, Format Text, **MaxLength 100** (the stored value is 69 chars; the room is for a `v2`) |
| Required | None |
| Field security | `IsSecured = true`. **Spaarke BFF-Managed Field Readers: read 0, create 0, update 0.** **Spaarke BFF-Managed Field Writers: read 4, create 4, update 4.** The BFF must READ the hash to compare it. This differs from the marker column, where Readers have read 4 |
| Auditing | `IsAuditEnabled = false`: an audit row would otherwise keep old hashes after consumption. `IsValidForAdvancedFind = false` |
| Solutions | SpaarkeCore AND SpaarkeMaster. `contact` sits in SpaarkeMaster with `rootcomponentbehavior = 1`, so the column travels only when it is added explicitly (as for 184) |
| Script | **`scripts/Set-ExternalInviteCodeHashSchema.ps1`**, a copy of `Set-ExternalInviteMarkerSchema.ps1`'s structure (dry run / `-Apply` / `-Verify`). It never creates a profile (H7b does). It reports Writer membership and never writes it: a stranger is FAIL. **A Readers profile with ANY read on the column is FAIL.** Any other profile that can read, create or update it is FAIL (System Administrator, the platform default, is reported) |
| Agreement test | `ExternalInviteCodeHashSchemaAgreementTests`, like `ExternalInviteMarkerSchemaAgreementTests`. It pins table, column, type, length, `$Secured`, Readers 0/0/0, Writers 4/4/4, auditing off, both solutions, and "never POSTs `fieldsecurityprofiles`" |

**Next to `sprk_externalinvitedon`: yes.**
- Same table, same writer profile, written together by `MarkInvitedAsync` in one PATCH.
- Read together by the marker leg's own query: `BuildInviteMarkerPath` (`:300-301`) selects `contactid, sprk_externalinvitedon, sprk_externalinvitecodehash`. That query is kept out of `ContactSelect`, so a BFF deployed before the column breaks nothing else (184's reasoning).
- A re-invite replaces both in the same request.
- The bind clears only the hash (§5). The marker is never cleared (184).

**Why a second script, not an extension of 184's.**
- 184's script is applied and verified on dev, and its agreement test pins exactly one column with Readers read 4.
- Giving it a second column with the opposite Readers grant would need per-column grant parameters, and would put a verified script back into review.
- The new script's `-Verify` also checks that the marker column exists, because the code means nothing without it.

## 4. The SPA carriage

### 4.1 Flow

1. **Landing.** `main()` (`src/client/external-spa/src/main.tsx:401-416`) first calls the new `captureInviteCode()` from the new `src/auth/invite-code.ts`. That is before `isJoinPath`, before Teams detection and before any MSAL instance. It:
   - reads `invite` from `window.location.search`;
   - drops it unless `IsWellFormed` (43 base64url chars);
   - stores `{ code, customerKey: readCustomerQueryKey() }` in **sessionStorage** under `spaarke.ext.inviteCode`. This is per tab, like the realm and the MSAL cache (ADR-028 A1/A3: do not use localStorage);
   - **removes `invite` from the URL at once** with `history.replaceState`, keeping `customer` and every other parameter.

   The clean-up must run first. `captureReturnTo()` (`auth/msal-auth.ts:49-55`) saves `pathname + search` and later restores it, and MSAL's `navigateToLoginRequestUrl` returns to the login-start URL. Without the clean-up, both would carry the code across the redirect and back into the address bar and history.
2. **Sign-in.** It is unchanged: realm chooser ("Partner"), backend selection, MSAL redirect to CIAM. The sessionStorage entry survives the redirect, exactly as `spaarke.ext.realm` does.
3. **The binding call.** Binding runs in `CallerPrincipalAuthorizationFilter` on EVERY `/api/v1/external` request (`ExternalAccessEndpoints.cs:60-63`), through `CiamContactPrincipalStrategy.ResolveAsync` (`CallerPrincipalResolver.cs:458-544`) and then `ContactIdentityBinder.ResolveCiamCallerAsync` (`ContactIdentityBinder.cs:208-215`). So the code travels as a **header** on BFF calls, not in one endpoint's body. GET bodies are not an option, and a dedicated bind endpoint would race the page's other first calls.
   - In `auth/bff-client.ts` `authorizedFetch` (`:238-259`), add `X-Spaarke-Invite-Code: {code}` when all of these hold:
     - an entry exists;
     - `backend.plane === 'ciam'`;
     - the entry's `customerKey` is null or equals `backend.customerKey`. A code for customer A is never sent to customer B's origin; the URL and the scope already come from the same backend snapshot.
   - **The call that triggers E11 in practice** is the first `/api/v1/external` call after sign-in: `WorkspaceHomePage`'s `fetchMeEntitlements` → `GET /api/v1/external/me/entitlements` (`pages/WorkspaceHomePage.tsx:79-98`, `api/me-client.ts:121`). Any concurrent call that carries the header behaves the same way (§5.3).
4. **Clearing.**
   - The entry is removed after the first **2xx** from a CIAM-plane call. The contact is then resolved by oid, whether this call bound it or not, and the code has no further use.
   - It is kept on a 403, so a "Sign in again" with the right account (`BffAccessState`, same tab) still presents it.
   - It is removed on sign-out (`App.tsx:236-241`, next to `clearStoredRealm`) and on "Choose a different sign-in" (`main.tsx` `handleResetRealm`, `:190-198`).
5. **BFF side.**
   - `CiamContactPrincipalStrategy` reads the header and keeps it only if `InviteCode.IsWellFormed`; otherwise it is absent.
   - It passes it as `InviteCodeSecret` to `ResolveCiamCallerAsync(oid, email, inviteCode, ct)`.
   - The header is added to the CORS allow-list (`Infrastructure/DI/CorsModule.cs:118-143`). The SPA origin is cross-origin, so the preflight would otherwise refuse it.
   - The BFF has no HTTP or header logging middleware (checked: no `UseHttpLogging`), and App Insights request telemetry does not collect request headers.
6. **The deny message.**
   - `CiamDenyDetail(DenyContactNotInvited)` (`CallerPrincipalResolver.cs:546+`) becomes: "This sign-in has not been invited to this workspace, or the invitation link was not used. Open the invitation link from your email in this browser and sign in again. If it has expired, ask the person who works with you here to invite you again."
   - It never says whether a code was missing or wrong.
   - The SPA's denied state shows the server's `detail` for `reasonCode = sdap.access.deny.contact_not_invited`: `problemMessage` in `bff-client.ts`, passed as `deniedMessage` to `BffAccessState` from `WorkspaceHomePage`.

### 4.2 A different device or browser

- The code lives only in the tab where the link was opened.
- Signing in anywhere else WITHOUT opening the link gives a 403 `contact_not_invited`, with the message above.
- Opening the link on the second device works there. The code is single-use, so whichever device binds first wins, and the other then signs in normally: once bound, every device resolves by oid (D4) and no code is needed.
- A link opened in tab A while the person signs in in an already-open tab B is the same case ("open the link again").
- A forwarded link is useless without a CIAM account carrying the invitee's email, which is exactly the squat the task closes. The remaining risk is that someone controls the invitee's mailbox, and that is email ownership itself.

### 4.3 Files 185 would touch in the SPA, and the overlap with SPA-r3 (ADR-028 A9) for the main session to coordinate

| File | 185 change | Overlap with SPA-r3 |
|---|---|---|
| `src/auth/invite-code.ts` (new) + `tests/invite-code.test.ts` (new) | capture, check, clean up, read, clear | none |
| `src/main.tsx` | one `captureInviteCode()` call in `main()`; one `clearInviteCode()` in `handleResetRealm` | **YES.** R3 task 040 (re-planned) reworks `StandaloneBootstrap` / realm handling for the multitenant workforce client. Small textual conflict; sequence the PRs |
| `src/auth/bff-client.ts` | the header in `authorizedFetch`; clear on the first 2xx | **Possible.** R3 041 owns backend selection, and A9 workforce token acquisition may touch `authorizedFetch` |
| `src/App.tsx` | `clearInviteCode()` in the sign-out handler | low |
| `src/pages/WorkspaceHomePage.tsx` (and optionally `components/auth/BffAccessState.tsx`) | show the server's detail for `contact_not_invited` | low |
| **Not touched:** `auth/msal-auth.ts`, `auth/msal-config.ts`, `auth/realm.ts`, `auth/backend-selection.ts`, `auth/standalone-plane.ts`, `auth/registration-client.ts`, `pages/JoinPage.tsx`, `components/auth/RealmChooser.tsx`, `CustomerPicker.tsx` | — | R3's A9 surface; 185 stays out of it |

**BFF files with A9 overlap** (R3 builds `WorkforcePrincipalResolver`, the registration endpoint and the `ContactIdentityBinder` (tid, oid) branch, on top of #1801):

| BFF file | 185 change | Overlap |
|---|---|---|
| `Infrastructure/ExternalAccess/ContactIdentityBinder.cs` | `ResolveCiamCallerAsync` signature (+ code); `RunUnlinkedAsync` `BindByEmail` case (the consuming write when the decision asks for it); `DecideUnlinkedAsync` intent block; `MarkInviteSentAsync` (code + hash); `BindInvitedContactAsync` (optional hash clear, Q5) | **YES.** R3's (tid, oid) branch is in this file. Agreed condition (R3 coordination note §2): R3's path is a NEW branch and does not reshape E10/E11. 185 edits only the CIAM/E11 methods above. Sequence: one PR rebases on the other; UAC reviews R3's |
| `Infrastructure/ExternalAccess/ContactBindingDecision.cs` | `BindingRequest.InviteCode`, `BindIntentLookup.InviteCodeHash`, `BindingDecision.ConsumeInviteCode`, `DecideIntent` | **Possible**, if R3 adds decision types for (tid, oid) |
| `Infrastructure/ExternalAccess/ContactIdentityStore.cs` | marker read/write gains the hash; the grant-leg read is removed (§6); new `BindOidAndConsumeInviteAsync` | **Possible**, if R3 puts its stored link in this store (the agreed shape is "its own field or store") |
| `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` | `CiamContactPrincipalStrategy` (header) and `CiamDenyDetail` | Low: `WorkforcePrincipalStrategy` lives in the same file |
| `Infrastructure/DI/CorsModule.cs` | one allowed header | Low |
| `WorkforcePrincipalResolver.cs`, `WorkforceIdentityOptions.cs`, the registration endpoint | **not touched** | none |

Checked 2026-10-13: R3's branch (`work/spaarke-SPA-external-access-platform-r3`) has no committed change (`git diff origin/master...HEAD`) in `src/client/external-spa/src`, `Infrastructure/ExternalAccess`, `Api/ExternalAccess` or `CorsModule.cs` relative to master.

## 5. Binding

### 5.1 E11 marker leg

**The pure decision.** In `ContactBindingDecision.DecideIntent` (`ContactBindingDecision.cs:754-765`), on the CIAM plane only:

```
if intent.ContactId != contactId
   || intent.InviteStatus is not (Read or ColumnMissing)          -> Deny contact_lookup_failed   (fail closed, as 184)
invited = intent.InviteStatus == Read
          && IsInviteMarkerCurrent(intent.InvitedOn, intent.AsOf)  // 184: <= 30 days, <= 5 min in the future
          && InviteCode.Matches(request.InviteCode, intent.InviteCodeHash, contactId)
invited ? BindByEmail { ConsumeInviteCode = true } : Deny contact_not_invited
```

**`InviteCode.Matches`:**
- It returns false when the presented code is absent or malformed, when the stored value is absent, has the wrong version or does not parse, and when the digest differs.
- The digest compare is `CryptographicOperations.FixedTimeEquals` over two 32-byte spans. This is the rule ADR-028 / `auth.md` already sets for API keys.
- Parsing the stored value works on public framing (version and lengths), so its timing says nothing about the code.

**The deny code.**
- Every refusal is `sdap.access.deny.contact_not_invited` (184's code, unchanged): no code, a wrong code, a code for another contact, an expired marker, a superseded code, and a marker with no hash (an invite from before 185).
- Nothing is written or flagged, as in 184.
- The binder's log line names a non-secret reason (`no-marker`, `marker-expired`, `no-stored-hash`, `no-code-presented`, `code-mismatch`) for operators and the migration. It never logs the code or the hash. The HTTP answer does not tell the reasons apart, so it gives an attacker no oracle.

**Where the hash comes from.** `IContactIdentityStore.FindBindIntentAsync` (`ContactIdentityStore.cs:742-761`) reads it in the marker leg's own query, and `InterpretMarkerLeg` (`:357-379`) parses it into `BindIntentLookup.InviteCodeHash`. The decision stays pure: no key, no I/O.

### 5.2 Consuming the code in the same write as the oid bind

**A new store method: `IContactIdentityStore.BindOidAndConsumeInviteAsync(contactId, etag, oid, plane)`.**
- One `PATCH contacts(id)` carrying `BindPayload` (oid in the binding AND the key mirror, plus the plane; `:453-458`) plus `sprk_externalinvitecodehash = null`.
- It is sent with `If-Match: <the row version the decision was made on>`, through `RowVersionPrecondition`: never `*`, and no version means no write (as `BindOidAsync`, `:793-796`).
- The marker is not touched.
- It never clears or re-points an oid or a link. Task 141's invariant holds: only the hash is cleared, together with a bind onto an Unbound row.

**In the binder.** In `RunUnlinkedAsync`'s `BindByEmail` case (`ContactIdentityBinder.cs:475-507`), the binder calls this method when `decision.ConsumeInviteCode` is set, and `BindOidAsync` otherwise. The workforce plane's bind stays byte-identical. **Superseded 2026-10-16 (#2005, PR #2007):** every bind onto an existing contact (CIAM, workforce and systemuser) now calls this method, because the grant flows (§11) put a pending hash on contacts that may then bind on another plane. The store falls back to the plain bind only when the hash column is missing. The result handling is unchanged: Written, KeyConflict (K1), a 412 that re-decides once, or `contact_bind_failed`.

**Why a new method and not a flag on `BindOidAsync`.** A flag would put a CIAM-only column into the signature and payload of every workforce and systemuser bind (the 141 surface). A separate method keeps those binds byte-identical. *(Superseded 2026-10-16, #2005: those binds now consume too. The invariant is "a bound contact never holds an unused hash" on every plane. The invite mark is also conditional on the row version of a re-read, unbound contact, never `If-Match: *`.)*

### 5.3 Races and replays

- **Two sign-ins racing with the same code** (two accounts, or two tabs):
  - Both read version v1 and both decide BindByEmail. The first PATCH wins (v2, hash cleared). The second gets 412 and re-decides on fresh rows.
  - For the **same** oid, the oid lookup now finds the contact: D4, ResolveByOid. The person's parallel page calls all succeed.
  - For a **different** oid, the email row is now Bound to another oid: E7 Collision, `contact_bound_to_different_oid`, and the contact is flagged (141, unchanged).
  - Exactly one binds.
- **Replay after consumption.** The contact is Bound, so any later account is decided by E6/E7 (141), never by E11. If an operator ever unbinds the contact by hand, the hash is already null, so the old code is denied `contact_not_invited`.
- **A re-invite while someone signs in.** The intent read sees either the old hash with v1, or the new hash. A bind under v1 after the re-invite (v2) answers 412 and re-decides against the new hash, so the old code fails. A superseded code never binds.
- The row version used is the email-lookup row's `ETag` (`ContactSelect`). Any write to the contact after that read, including a re-invite, invalidates it.

### 5.4 Why there is no attempt counter

- **Entropy.** A guess succeeds with probability 2^-256 (2^-128 at the minimum size). Each guess is an authenticated BFF request from a CIAM account that already carries the invitee's email. Even 10^6 guesses a second for the whole 30-day window (about 2.6 × 10^12 guesses) leaves odds near 10^-65 (about 10^-26 at 128 bits).
- **A counter would hurt.**
  - It would put a Dataverse write on the deny path. Task 141 removed exactly that, so a retried denied sign-in cannot become a stream of writes.
  - It would let anyone holding a squatted account LOCK OUT the real invitee by failing on purpose, which is denial of service against the person the invite is for.
- **Existing protections.** Authentication and the 30-day window remain. BFF rate limiting is per endpoint and covers only some `/api/v1/external` routes (for example `dataverse-query` on intake, messages and feeds; not `/me/entitlements`). The entropy argument does not depend on it.

## 6. The grant-only leg: recommendation (b)

**Option (a) collapses into (b) and adds a loophole.**
- A code exists only because an invite was sent, and the invite also writes the marker. So "grant AND valid code" can differ from "current marker AND valid code" only when the marker has expired but a grant is active.
- In that case (a) would let a code bind after its 30 days. That contradicts "the code expires with the marker window".

**Option (b): only invites bind.**
- `DecideIntent` drops the grant term.
- `FindBindIntentAsync` stops reading grants: no `BuildActiveContactGrantPath` / `InterpretGrantLeg` call. The fewer reads there are, the fewer faults can deny, and the shared filter in `ExternalParticipationService.BuildContactGrantFilter` loses this consumer.
- `BindIntentLookup.GrantStatus` / `HasActiveGrant` are removed.
- A typed-in contact holding a grant can then never be bound by an unproven account.

**Effects on people granted without an invite:**
- **Already bound** (oid set): no effect. Oid-first resolution (D4).
- **Unbound** (typed in, then `/grant`): their CIAM sign-in is denied `contact_not_invited` until an internal user runs `/invite` (or `/invite-and-grant`), which sends a code. 184 bound them on the grant alone.
- **Contact-side grants** (task 140: a partner grants a colleague in its own organization): the colleague, if unbound, now needs an INTERNAL invite. Contacts cannot invite (task 140 removed it).
- **Grant notifications** (`GrantAccessNotifier`, "you were given access", with a portal link): sent to an unbound contact, the email leads to a denied sign-in.
- Owner question Q3: file a follow-up so the grant flows offer or trigger an invite for an unbound contact.
- **Dev today:** 2 active, unbound contacts hold active contact-level grants. **Neither has an email**, so E1 already denies them on the CIAM plane (`contact_not_found`). The real effect on dev is **0 contacts** (§8).

**Tests whose expectations change under (b), not just their seeds** (owner question Q2). The AC6 wording, "184 tests unchanged apart from seeds", cannot hold for these:

| Test | Today | Under (b) |
|---|---|---|
| `ContactBindingDecisionTests.E11_Ciam_AnActiveGrantHere_Binds` | binds | **denied `contact_not_invited`** |
| `ContactBindingDecisionTests.E11_Ciam_AnExpiredMarker_WithAnActiveGrant_BindsThroughTheGrant` | binds | **denied** (and becomes the "grant does not extend a code" proof) |
| `ContactBindingDecisionTests.E11_Ciam_AFarFutureMarker_WithAnActiveGrant_StillBindsThroughTheGrant` | binds | **denied** |
| `ContactBindingDecisionTests.E11_Ciam_AMarkerColumnNotProvisionedYet_IsNoInviteEvidence_AndTheGrantLegStillDecides` | grant decides | ColumnMissing → **denied `contact_not_invited`** |
| `ContactBindingDecisionTests.E11_Ciam_AFaultedIntentRead_NeverBinds_EvenWhenTheOtherLegShowsIntent` | grant-fault cases | the grant-fault cases are removed (no grant read); the marker-fault cases stay |
| `CiamContactBindingTests.AnUnboundContactWithAnActiveGrantHere_IsEmailBound` | bound | **denied, nothing written** |
| `CiamContactBindingTests.AFailedGrantRead_NeverBinds_EvenWithAnInviteMarker` | denied | replaced by "no grant read is made" |
| `ExistingCiamAccountInviteTests.AFailedOidWrite_WithTheMarker_KeepsThe500_AndTheFirstSignInRepairsIt` | the sign-in repairs | no email was sent, so there is no code. The sign-in is denied; a **retried invite** answers ExistingAccount with a new code, and that code repairs it (Q6) |

Seed-only changes (a valid code is added): `E11_Ciam_APendingInviteHere_Binds`, `E11_Ciam_AMarker_CountsOnlyWithinItsWindow`, `CiamContactBindingTests.AnUnboundContactWithAPendingInviteHere_IsEmailBound`, `AMarkerAtTheEdgeOfItsThirtyDays_IsMeasuredAtTheBindersClock`, `TheInviteRepairPath_BindsTheOidOntoTheOneUnboundActiveContact_PlaneExternal`, `AReInvite_OverwritesTheMarkerWithTheLatestTime` (now also asserts a new hash), and the marker-payload assertions in the invite tests.

## 7. Unchanged: the new-account path and the workforce plane (where the tests pin them)

**New-account path.** `InviteExternalUserEndpoint.ProvisionAsync` (`Api/ExternalAccess/InviteExternalUserEndpoint.cs:202-356`):
- marker (+ hash) at `:258`;
- CIAM create at `:280`;
- the oid bound at `:320` through `ContactIdentityBinder.BindInvitedContactAsync` (`ContactIdentityBinder.cs:369-397`, which calls `BindOidAsync`).

That path never runs `ContactBindingDecision.Decide`, so it needs no code: the account is bound in the same request, before anyone signs in. The onboarding email carries a code (goal (1)), and it is inert there, because a bound contact never reaches E11.

- **Q5 (recommended: yes).** When the marker+hash PATCH reported `Written` (so both columns exist), the invite's bind PATCH also sets the hash to null, so no unused hash is left on a bound contact.
  - The behaviour is unchanged: same request, no code, same statuses.
  - When the marker did not land (`ColumnMissing`), the bind payload is exactly today's. A null for a column that does not exist would fail the bind, which is why this is conditional.
- **Pins:** `ExistingCiamAccountInviteTests.ANewAccountInvite_StampsTheMarkerBeforeTheCiamAccount_AndStillBindsTheOid` and `BeforeTheSchema_ANewAccountInvite_StillSucceeds_ItsOidIsBoundInTheSameRequest` (for the second, ColumnMissing on either column), plus new: `ANewAccountInvite_Binds_WithNoCodePresented_AndItsEmailCarriesACode`.

**Workforce and systemuser planes.**
- `ContactIdentityBinder.ResolveWorkforceCallerAsync` (`:170-198`, `BindingPlane.WorkforceToken`) and `EnsureSystemUserLinkAsync` (`:243-323`) never reach the CIAM-only E11 block (`ContactBindingDecision.cs:729-740`, `if (request.Plane == BindingPlane.CiamToken)`).
- 185 changes nothing outside that block, the CIAM strategy and the invite path.
- **Pins:** `E11_TheWorkforceAndSystemUserPlanes_BindWithoutAnyIntent`, `TheWorkforcePlane_EmailBindsAMember_WithoutReadingAnyIntent` (both unchanged), plus new: `TheWorkforcePlane_IgnoresAnInviteCodeHeader_AndItsBindPayloadIsUnchanged`.
- Model 1 workforce sign-in (A9) is R3's, and 185 does not touch it.

## 8. Migration

**Read-only count on spaarkedev1, 2026-10-13.**
- Pattern: the admin `az` token, as the earlier gates' Dataverse reads used (`notes/batch5-live-gates-2026-10-08.md`, #1801 gate). GET only, token held in memory. Script: session scratchpad `gate185/count.py`.

| Query | Result |
|---|---|
| `contact.sprk_externalinvitedon` metadata | exists (184 schema applied) |
| `contact.sprk_externalinvitecodehash` metadata | 404, not yet created (expected) |
| contacts with `sprk_externalinvitedon ne null` | **0** |
| pending markers (`sprk_externalinvitedon ne null and sprk_externalobjectid eq null`) | **0** (so 0 within 30 days) |
| active, unexpired contact-level grants | 52 rows on 10 distinct contacts |
| of those, active and unbound (today's grant-only E11 candidates) | 2. Both have **no email**, and no systemuser links them, so they cannot bind on the CIAM plane today either |

**Stamps.** No production customer onboards external contacts before T240d (this task), so no stamp should hold a pre-185 pending marker. The release step still covers them.

**Release-note step.** "Invites sent before this release carry no invite code and no longer bind on the CIAM plane. Contacts invited but not yet signed in must be re-invited."
1. As a System Administrator, find them: `contacts?$select=contactid,emailaddress1,sprk_externalinvitedon&$filter=sprk_externalinvitedon ne null and sprk_externalobjectid eq null and sprk_externalinvitecodehash eq null and statecode eq 0`.
2. Re-run `/invite` for each. The re-invite answers ExistingAccount (the CIAM account exists) and sends a new code.
3. Under (b), also: "Contacts that were only granted access, without an invite, must be invited before they can sign in."

**Deploy order** (as 184):
- **Dev:** the owner runs `Set-ExternalInviteCodeHashSchema.ps1 -Apply`; `-Verify` must exit 0; then the BFF is deployed, then the SPA. Before the SPA ships, a new invite's code simply goes unused, and E11 denies until the person uses the new link from the new SPA.
- **Stamps:** a SpaarkeMaster version bump plus release export. Between Phase 2 (BFF) and Phase 3 (import), new-account invites work, and ExistingAccount answers 503 (§1.2).

## 9. Test plan, files, publish size, §11

All tests follow ADR-038: the in-memory store (`InMemoryContactIdentityStore`, which already enforces `If-Match` versions) and the pure builders/parsers. No HTTP doubles.

### 9.1 Per acceptance criterion

| AC | Tests (new unless noted) |
|---|---|
| 1. A squatted account with a pending marker is denied | Decision: `E11_Ciam_APendingInvite_WithNoCode_IsDeniedNotInvited`. Binder: `ASquattedAccount_PresentingNoCode_IsDeniedNotInvited_NothingWritten_NothingFlagged` (the store records no PATCH) |
| 2. The invitee binds and the code is consumed; a replay is denied | `TheInvitee_WithTheCode_Binds_AndOnePatchCarriesOidMirrorPlaneAndHashNull_UnderIfMatch`; `TheSameCode_FromAnotherAccount_AfterTheBind_IsDeniedAsBoundToDifferentOid` (E7, 141 code); `TwoRacingSignIns_WithOneCode_BindExactlyOne` (two decisions on v1, the second write gets 412 and re-decides); `AnOperatorUnbind_AfterConsumption_LeavesTheOldCodeDenied` |
| 3. Wrong, expired and superseded codes are denied | Decision theory: wrong code; a code hashed for another contact; marker 30 days + 1 s with the right code (and exactly 30 days binds); a re-invite overwrote the hash and the old code is presented; a malformed presented code; a malformed / `v2` / truncated stored hash; marker without hash (pre-185). `InviteCode` unit tests: 43 chars, base64url alphabet, two calls differ, the hash format round-trips |
| 4. Field security on the hash | `ExternalInviteCodeHashSchemaAgreementTests` (§3), plus the live `-Verify` on dev after the owner's `-Apply` (the agent may run `-Verify`, which is read-only). Live: testuser1 (Readers) reading `sprk_externalinvitecodehash` gets no value |
| 5. The grant-only leg (b) | `AnUnboundContactWithOnlyAnActiveGrant_IsDeniedNotInvited`; `AnActiveGrant_DoesNotExtendAnExpiredCode`; `FindBindIntent_ReadsOnlyTheMarkerLeg` (path-builder test: no `sprk_externalrecordaccesses` path); the table in §6 |
| 6. The new-account path and the workforce plane are unchanged | §7's pins; 141 tests untouched; 184 tests as listed in §6 |
| 7. Mutation proofs | **(i) Drop the code check** (`Matches` → true): AC1, AC3 (wrong code) and AC5 fail. **(ii) Do not consume** (the binder calls `BindOidAsync`): AC2 "one PATCH … hash null" and "operator unbind" fail. **(iii) Ignore the window** (drop `IsInviteMarkerCurrent` from the leg): AC3 "30 days + 1 s" fails. **(iv) Non-constant-time compare** (`SequenceEqual` / `==`): review only. Optional guard (ladder): a source-scan test that `InviteCode.Matches` calls `CryptographicOperations.FixedTimeEquals` and that the file has no `SequenceEqual`, with must-fire and must-not-fire controls. **SPA:** removing the header, or removing the URL clean-up, fails `invite-code.test.ts` |
| 8. Builds | `dotnet build src/server/api/Sprk.Bff.Api/` with 0 errors; full `Spaarke.ArchTests`; External SPA `npm run build` + `npm test`. **The SPA has no `build:prod` script** (`package.json`: `build` = `vite build`); the AC's "build:prod" means that (Q8). Provisioning reviews the PR (T240d) |

**More tests:**
- `TheInviteResponse_NeverCarriesTheCode`. `/invite` and `/invite-and-grant` return the base `portalUrl`.
- `TheInviteEmails_CarryTheCodeInTheHrefOnly`. The template is rendered and attribute-encoded.
- `NoLogLine_CarriesTheCodeOrHash`. A log-capture assertion over the invite and bind paths.
- `BindingRequest_ToString_RedactsTheCode`.
- `CiamStrategy_IgnoresAMalformedHeader`.
- `ColumnMissing_OnEitherColumn_LetsANewAccountInviteProceed_And503sExistingAccount`.

**SPA vitest:**
- the code is captured and the URL cleaned before any MSAL call;
- the header goes only on CIAM, and only for the matching customer key;
- clearing on the first 2xx, on sign-out and on realm reset;
- a 403 keeps the code.

**Live gate (owner checklist).** A real CIAM sign-in is needed, and the agent cannot perform one (O-184-3):
- invite a throwaway contact and use the link: it binds and the hash is null;
- sign in to the same contact without the link (another browser): `contact_not_invited`.

### 9.2 Files to change

**BFF:**
- `Infrastructure/ExternalAccess/InviteCode.cs` (new);
- `ContactBindingDecision.cs`, `ContactIdentityStore.cs`, `ContactIdentityBinder.cs`, `CallerPrincipalResolver.cs`;
- `Api/ExternalAccess/InviteExternalUserEndpoint.cs`;
- `Services/Registration/RegistrationEmailService.cs` and its 2 templates;
- `Infrastructure/DI/CorsModule.cs`.

**Script:** `scripts/Set-ExternalInviteCodeHashSchema.ps1` (new); `docs/data-model/oob-customizations.yaml` (the column).

**Tests:**
- `tests/integration/auth/UnifiedAccessControl/IdentityBinding/{ContactBindingDecisionTests, CiamContactBindingTests, InMemoryContactIdentityStore, ExternalInviteCodeHashSchemaAgreementTests (new)}.cs`;
- `tests/integration/auth/UnifiedAccessControl/{ExistingCiamAccountInviteTests, AssignedAccessTestDoubles}.cs`;
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/{DataverseContactIdentityStoreTests, CallerPrincipalResolverTests, InviteCodeTests (new)}.cs`;
- `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs`.

**SPA:** §4.3.

**Project docs:** `design.md` Placement Justification ("in the BFF": it extends the existing identity binder and invite endpoint; there is no background work, so ADR-052 does not apply) and a `<hot-path-declaration>` for the files above. Also the deployment guide's §6.5 (invite / E11) text.

### 9.3 Publish size

- No new package. SHA-256, `RandomNumberGenerator`, `FixedTimeEquals` and `Base64Url` are in the BCL. `Azure.Security.KeyVault.Secrets` is already referenced, but is not needed under §2.
- Expected delta: under **+0.1 MB** (IL only). It must be measured against a fresh master build in the implementation task (`bff-hygiene.md` item 4).

### 9.4 CLAUDE.md §11: one sentence per question per new surface

| New surface | 1. Existing | 2. Extension | 3. Cost of doing nothing |
|---|---|---|---|
| Column `sprk_externalinvitecodehash` | `sprk_externalinvitedon` (184 marker) is a DateTime that Readers may read | The marker cannot hold a digest, and every other contact text column has different readers or a different meaning | E11's marker leg cannot tell the invited person from a squatter holding a CIAM account for the email (F1 stays open; T240d is blocked) |
| `InviteCode` (static: create / format / hash / fixed-time match; `InviteCodeSecret` redacting wrapper) | `TrackingTokenSigner` (Key Vault-keyed HMAC tokens), `WebhookSignatureFilter` (HMAC check) | Neither generates a random secret or stores a salted digest; coupling sign-in to the tracking-footer key would make binds depend on an optional feature's config | Generation, hashing and comparison would spread over the endpoint and the decision, with no single place to pin CSPRNG, format and constant time |
| Header `X-Spaarke-Invite-Code` (+ CORS entry) | No request field carries sign-in proof; binding runs in the caller-principal filter before any handler | Read in `CiamContactPrincipalStrategy`, which already reads the token's identity claims | The code cannot reach E11 on the call that binds |
| `IContactIdentityStore.BindOidAndConsumeInviteAsync` | `BindOidAsync` | A flag there would put a CIAM-only column into every workforce and systemuser bind payload (141 surface) | The hash outlives the bind, so after any later unbind the same code binds again (fails AC2 "consumed") |
| `BindingRequest.InviteCode`, `BindIntentLookup.InviteCodeHash`, `BindingDecision.ConsumeInviteCode` | 184's `BindIntentLookup` / `AsOf` pattern | They extend those records (no new decision type) | The pure decision cannot compare the code or tell the binder to consume it |
| `MarkInvitedAsync` hash argument; marker query selects the hash | 184's marker write and read | Modification of existing surface | — |
| Email `{{InviteUrl}}` and the invite link builder | `{{PortalUrl}}`; `GrantAccessNotifier.BuildPortalLink` | Copies `BuildPortalLink`'s rules; that builder needs a record and a resolved key, which an invite does not have | The email cannot deliver the code |
| `Set-ExternalInviteCodeHashSchema.ps1` + agreement test | `Set-ExternalInviteMarkerSchema.ps1` | Extending it means per-column grant parameters in a verified script (§3) | The column and its field security do not exist on dev or stamps |
| SPA `invite-code.ts` (sessionStorage `spaarke.ext.inviteCode`) | `realm.ts` / `spaarke.ext.returnTo` hold other per-tab state | Different lifetime (cleared on first 2xx) and a secret (the URL clean-up) | The code does not survive the MSAL redirect |
| Key Vault read | **None** under the recommendation | — | — |

## 10. Open owner questions (each with a recommendation)

1. **Q1, the hash.** Recommended: **salted SHA-256** (§2): no new secret, nothing to rotate, a pure decision, and no weaker against a read leak. The alternative, HMAC, needs an owner-created KV secret `ExternalAccess-InviteCodeHashKey` on dev and an H4 catalog entry for every stamp.
2. **Q2, the grant leg.** Recommended: **(b), only invites bind** (§6). Accept that the 184 grant-leg tests listed in §6 change their expectations: AC6's "unchanged apart from seeds" cannot hold for them.
3. **Q3, granted-but-uninvited contacts** (`/grant` of a typed-in contact, task 140 contact grants, `GrantAccessNotifier` emails to unbound contacts). Recommended: **file a follow-up** so those flows offer or trigger an invite; not in 185.
4. **Q4, plaintext copies of the code** in `sprk_communication.sprk_body` and the mailbox's Sent Items (§1.3). Recommended: **accept for 185**, because none of the named attackers gains a capability, and **file a follow-up** for a redacted-body / no-Sent-Items send option in `CommunicationService` (the email-communication project's files).
5. **Q5, the new-account bind clears the inert hash** in its PATCH when the marker landed. Recommended: **yes**; the behaviour is unchanged (§7).
6. **Q6, the failed-oid-write 500 path.** Today "the first sign-in repairs it"; under 185 that sign-in has no code, because no email was sent. Recommended: **keep the 500** and change its message and the deployment-guide text to "retry the invite". The retry answers ExistingAccount with a new code. The alternative, sending the onboarding email with the code on that path, changes an error path in scope.
7. **Q7, code size.** Recommended: **256 bits** (43 chars). 128 bits (22 chars) also meets the task.
8. **Q8, the AC wording "External SPA build:prod".** The SPA has no such script. Recommended: read it as `npm run build` (`vite build`, production mode) plus `npm test`.
9. **Q9, the SPA shows the server's `contact_not_invited` detail** (the "open the invitation link in this browser" guidance) in the denied state. Recommended: **yes**, a small change in `WorkspaceHomePage` / `BffAccessState`.
10. **Q10, coordination with SPA-r3** (for the main session). The overlapping files are listed in §4.3. Recommended: 185's BFF PR goes first, because it is the T240d gate and R3 has agreed to build on #1801 and its follow-ups. R3 rebases onto it, and UAC reviews R3's `ContactIdentityBinder` change. 185's SPA edits to `main.tsx` and `bff-client.ts` are a few lines each; whichever PR lands second resolves them.

## 11. Owner decisions (round 98, 2026-10-13): BINDING, these supersede §10's recommendations where they differ

- **Q1, the hash:** salted SHA-256 (§2), as recommended. No Key Vault secret.
- **Q2/Q3, the grant leg: option (c), "granting sends the invite".**
  - The owner's intent: a contact that is manually granted access but never invited must still be matched by email on first sign-in.
  - Only an invite creates a CIAM account in this environment. An account made anywhere else (another customer's invite, or a squat) can't be told apart. So: when access is granted to an **unbound contact that has an email**, the grant flows send the invite automatically, with the same code. That covers `/grant`, `/invite-and-grant` (already invites), the internal-share grant, and task 140's contact grants (`GrantExternalAccessEndpoint`, `InternalShareEndpoints`, `InviteAndGrantExternalUserEndpoint`, `GrantAccessNotifier`).
  - The result is a new CIAM account (onboarding email) or ExistingAccount (the existing-account email), each carrying the code. First sign-in from that email binds through the marker leg.
  - E11's grant leg itself stops being a bind leg, as in §6 (b): a grant with no code never binds.
  - A contact that is already bound is unchanged: it gets the existing "you were given access" notification and no invite.
  - The §6 tests change their expectations, as listed there (accepted). Add tests for each grant flow: an unbound contact with an email gets an invite, an unbound contact without an email gets none, and a bound contact gets none.
- **Q4, the plaintext copies: a small fix in 185's scope.** Add a "system mail" (secret-bearing) send option to `CommunicationService`, set by `RegistrationEmailService` on both invite sends:
  - (i) **skip `CommunicationEnrichmentService.EnrichAsync`**. The code confirms the gap provisioning raised as R1: outbound sends are enriched direction-symmetrically, `CommunicationService.cs` ~745, so today the invite body and its link reach the LLM, triage summaries and proposals;
  - (ii) persist the body with the invite link removed (or no body);
  - (iii) `SaveToSentItems = false`.
  - The email-communication project owns those files, so coordinate with it. This is a named part of provisioning's T240d gate.
- **Provisioning R2, adopted as a design precondition (§4.2/§5).** The ExistingAccount bind is safe only while the `spaarkeextid` sign-in method stays **email + password only**: email-OTP *sign-in* must never be enabled. Provisioning owns that tenant setting (T240d §7 step 4) and the live check that SSPR revokes the earlier refresh tokens.
  - The ExistingAccount email tells the person: "If you don't recognise this account or its password, use **Forgot password** on the sign-in page."
- **Q5–Q9: accepted as recommended.**
  - Q5: the new-account bind also clears the hash.
  - Q6: keep the 500, with the message "retry the invite".
  - Q7: 256-bit codes.
  - Q8: `npm run build` plus `npm test`.
  - Q9: the SPA shows the server's deny detail.
- **The live gate** needs a real CIAM sign-in, so it goes on the owner checklist.
- **Q10: 185's BFF PR lands first.** SPA-r3 agreed on 2026-10-13 and rebases onto it. Keep E11's bypass for non-CIAM planes as it is (`ContactIdentityBinder` :725-728), at R3's request.

## 12. Implementation decisions (main session, 2026-10-13; technical defaults, consistent with §11)

The implementation is split into slices A, B1, B2, C, D, E and F, committed in order on `uac/185-invite-code` and delivered as ONE PR.
- **I-1. A grant whose automatic invite fails still succeeds.**
  - The grant is the internal user's decision, and with no code there is no bind, so it fails closed.
  - The grant response gains an ADDITIVE field, `invite`, with `status` set to one of `Sent`, `ExistingAccount`, `AlreadyPending`, `NotNeeded` (already bound), `NoEmail` or `Failed`, plus the failure's error code. Existing fields are unchanged.
  - A failed invite is logged at Warning, without the code. The user can retry with Invite.
- **I-2. A re-grant to an unbound contact whose marker is current** (under 30 days) sends NO new invite (`AlreadyPending`). A new code would break a link the person may be about to use. Only an explicit Invite replaces the code.
- **I-3. For an unbound contact, the invite REPLACES `GrantAccessNotifier`'s "you were given access" email**, so no contact gets two emails. Bound contacts keep the existing notification.
- **I-4. Task 140 contact grants (OWNER, round 98b, 2026-10-13): no automatic invite.** Task 140's rule that contacts cannot invite stands. The partner's grant is saved. The colleague is matched once an INTERNAL user sends the invite, and the record's internal owner is notified that the colleague needs one, through the existing `GrantAccessNotifier`/`appnotification` path. The response `invite.status` is `NeedsInternalInvite`.
- **I-5. Shared invite service.** `InviteExternalUserEndpoint.ProvisionAsync` is extracted into one internal service, called by the invite endpoint and by the grant flows. §11:
  - (1) Existing: the provision logic in `InviteExternalUserEndpoint`.
  - (2) Extension: it is the same logic moved, not new behaviour; the endpoint becomes a thin caller.
  - (3) Cost of doing nothing: without it, the grant flows cannot send the invite the owner required in round 98, short of copying 184's marker-first ordering into four places.
  - Its placement and DI registration go next to the existing external-access registrations.
- **I-6. Removing the link from the stored body.** The caller (`RegistrationEmailService`) passes the body to store, with the link replaced by "[invitation link removed]", in a system-mail option. `CommunicationService` never parses bodies. The email-communication project is asked on the PR to agree this shape.
- **I-7 (main session, after slice E). The I-4 notification on a team-owned record (every secure record).** No single owner can be notified, so the fallback order is:
  1. the internal user who granted the partner contact its own access;
  2. the record's `createdby`, if that is a person;
  3. a Warning log.

  The response stays `NeedsInternalInvite`.
- **I-8 (main session). ADR-010, path C (comply).** Slice E resolved the CIAM provisioner through `IServiceProvider` because its constructor throws on hosts without `Ciam:*`. Slice F moves that validation to first use and injects the provisioner directly, so no service locator is needed. The behaviour on configured hosts is unchanged.
- **Known limit.** When an invite fails after its marker was written, a re-grant within 30 days answers `AlreadyPending`. The first grant's response reported `Failed`, and an explicit Invite retries it.
- **I-9 (main session, after slice F). The assigned-access materializer follows the same rule.** `AssignedAccessMaterializer.WriteGrantAsync` sends the invite when an INTERNAL assignment names an unbound contact that has an email. A partner assignment notifies, as in I-4/I-7. A background re-materialization never sends an invite.
- **Dev schema (2026-10-13).** The main session ran `Set-ExternalInviteCodeHashSchema.ps1 -Apply` on spaarkedev1, then `-Verify`: VERIFY PASS. The column is field-secured; Readers have no permission; Writers have 4/4/4, with only the 2 BFF app users as members; auditing and Advanced Find are off; and the column is in SpaarkeCore and SpaarkeMaster.
- **I-10 (OWNER, round 98c, 2026-10-13). The inviter never sees ExistingAccount.** Provisioning's PR review (F1) found that returning `ExistingAccount` to the inviter, from `/invite`, `/invite-and-grant` or the grant flows' `invite.status`, is a cross-customer email-enumeration oracle. The inviter now sees `Sent` on both branches. ExistingAccount stays in the operator log and telemetry only, and the person still receives the existing-account email. This supersedes 184's response shape.
- **I-11 (main session). With no CIAM host, `/invite` and `/invite-and-grant` return a 503 ProblemDetails** with `GrantInviteStatus.CiamNotConfiguredReasonCode`, the same code `/grant` uses, instead of a global-handler 500 (provisioning's F3).
- **I-12 (main session). The code moves to the URL fragment, `#invite=`,** so the SPA host, CDN, WAF and front-door access logs never see it (provisioning's K1). The SPA still accepts `?invite=` as a fallback and strips both before MSAL runs.
- **Provisioning-owned.** `CiamGraphClientFactory.IsConfigured` / `EnsureConfigured` require `Ciam:GraphProvisioner:CertificateName`. T240d will make it optional when MI-FIC is configured. 185 adds only a one-line comment pointing to T240d.
- **I-13 (OWNER, round 98d, 2026-10-14). The two-call oracle is ACCEPTED as a known limit.** Provisioning's re-check of I-10 found that inviting the same email twice still tells the branches apart. A new account binds its oid at once, so the repeat answers AlreadyProvisioned / NotNeeded. An existing account stays pending, so the repeat answers Provisioned with a re-send, or AlreadyPending.
  - Every internal user can also read the binding in Dataverse: `sprk_externalobjectid` is readable through "Spaarke Identity Link Readers" on every business unit's default team, and its mirror `sprk_externalobjectidkey` is not secured.
  - The owner accepted it as low-value ("this email has a Spaarke external account somewhere"), and it is visible in Dataverse anyway.
  - The rejected alternative was to defer the new-account bind to the first sign-in with the code. It would have stopped new users signing in from a device without the link, and changed 184's new-account path.
  - Provisioning records it as K in the T240d gate.

**Release record (2026-10-14, provisioning):** SpaarkeMaster **1.2.3.0** is published by CI run 38603706393 from master `bd52b6290` (PR #1860). It carries `contact.sprk_externalinvitecodehash`, secured (Writers 4/4/4, Readers no row), with nothing removed from the package. The T240d production release gate is met.
