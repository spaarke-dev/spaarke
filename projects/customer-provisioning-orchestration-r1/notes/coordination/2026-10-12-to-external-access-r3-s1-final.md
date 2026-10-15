# To external-access-r3 — T240d spike S1 final results (2026-10-12, dev only)

From `customer-provisioning-orchestration-r1`. Full write-up: `notes/t240d-ciam-external-contacts-design.md` §8 "S1 results".
Follows our 2026-10-10 partial results ((i) keyless provisioning PASS; (ii) duplicate email = 400 `ObjectConflict`).

## The design changes from option 3 to option 2

A CIAM local account **cannot** get an access token for an API whose app is registered in the Spaarke (workforce)
tenant: `AADSTS500207: The account type can't be used for the resource you're trying to access` (Microsoft documents this
as unsupported). So the customer BFF app cannot be the CIAM audience. **Each customer gets its own CIAM API app registered
in `spaarkeextid`** (option 2, the agreed fallback). Provisioning creates it keylessly (proven in S1) and pre-authorizes the
CIAM SPA client on it, so contacts see no consent prompt.

## (iii) token shape — use these for FR-22 / task 010

Local account, authority `https://spaarkeextid.ciamlogin.com/{tid}`, scope `api://{ciamApiAppId}/user_impersonation`, API app
with `requestedAccessTokenVersion=2`:

| Claim | Value |
|---|---|
| `aud` | `{ciamApiAppId}` — **bare GUID** (v2 token), not `api://…` |
| `iss` | `https://{tid}.ciamlogin.com/{tid}/v2.0` |
| `ver` | `2.0` |
| `tid` | `7052feba-bfc4-43e0-b09e-65014b429131` (`spaarkeextid`) |
| `scp` | `user_impersonation` |
| `azp` | the SPA client id |

FR-22's "accept both `{appId}` and `api://{appId}`" stays correct and harmless. The value is the **per-customer CIAM API
app id**, not the BFF app id (provisioning sets `Ciam:Audience` per stamp, H4b).

R5: the same session silently gets a second resource (Graph `User.Read`) from its refresh token.

## (iv) — PASS

That token, sent to a BFF whose CIAM audience is a different app ("customer Y", the dev BFF):
- `/api/v1/external/me` → **401** `invalid_token`, "The audience … is invalid";
- workforce `/api/me` → **401** `invalid_token`, "The issuer … is invalid".

A token for customer X is refused by Y's CIAM scheme and by the workforce scheme before any handler runs.

## R3 impact

- The production CIAM SPA client (redirect exactly `https://external.spaarke.com`) requests
  `api://{ciamApiAppId}/user_impersonation` for the customer the contact is using. The SPA's customer selection
  therefore yields a CIAM API app id per customer (the T240c directory returns it; provisioning registers it).
- Contacts sign in on `ciamlogin.com` only. A workforce/admin account gets AADSTS500207 there, which is expected.
