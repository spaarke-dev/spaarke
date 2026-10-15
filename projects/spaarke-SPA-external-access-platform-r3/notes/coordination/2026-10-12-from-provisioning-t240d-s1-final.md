# 2026-10-12: T240d spike S1 final (from customer-provisioning-orchestration-r1)

Source: `projects/customer-provisioning-orchestration-r1/notes/coordination/2026-10-12-to-external-access-r3-s1-final.md` on `origin/work/customer-provisioning-orchestration-r1` (received as a cross-session message; facts below are theirs, measured in dev).

## Their findings

- **Option 3 does not work.** A CIAM local account cannot get a token for an API registered in the Spaarke workforce tenant (AADSTS500207). So the customer BFF app cannot be the CIAM audience.
- **Option 2 adopted:** each customer gets its own CIAM API app registered in `spaarkeextid`. Provisioning creates it keylessly and pre-authorizes the CIAM SPA client on it (no consent prompt).
- **Token shape** (local account, authority `https://spaarkeextid.ciamlogin.com/{tid}`, scope `api://{ciamApiAppId}/user_impersonation`, `requestedAccessTokenVersion=2`): `aud` = `{ciamApiAppId}` (bare GUID), `iss` = `https://{tid}.ciamlogin.com/{tid}/v2.0`, `ver` 2.0, `scp` `user_impersonation`, `azp` = SPA client id.
- **(iv) PASS:** that token at a BFF configured for another audience gets 401 "audience invalid" on `/api/v1/external/me` and 401 "issuer invalid" on workforce `/api/me`.
- **R5 PASS:** silent second-resource token (Graph `User.Read`) in the same session.

## R3 impact (checked against code 2026-10-12)

- **SPA: already supported.** `VITE_BACKENDS` entries take an optional `ciamBffAppId`; on the CIAM plane it replaces `bffAppId` for the scope (`src/client/external-spa/src/auth/backend-selection.ts:186-230`). The T240c directory must return that id per customer. Comment at `backend-selection.ts:40` still says "T240d option 3"; update when next touched.
- **BFF: config only.** `Ciam:Audience` on each stamp is the per-customer CIAM API app id. FR-22's dual form (`{appId}` and `api://{appId}`) stays right; task 010's `BuildCiamValidAudiences` accepts both.
- **ADR-028 A8 wording:** A8 says "`api://{customer BFF app id}/user_impersonation` on both planes". Under option 2 the CIAM-plane app is the per-customer CIAM API app, not the BFF app. **Owner decision needed:** a one-line clarification to A8 (an ADR edit).
- **Directory contract agreed (provisioning reply, 2026-10-12):** each T240c directory entry emits `ciamBffAppId` (the per-customer CIAM API app id) alongside `bffAppId` (workforce scope). Each stamp's `Ciam:Audience` = that CIAM API app id, written by provisioning H4b. Recorded in their t240d design note §10 "Directory contract". They have no objection to the A8 clarification; it stays an owner decision here.
- **Task 038 probe FR-22:** use a token for the dev CIAM API app id that `Ciam__Audience` on `spaarke-bff-dev` names (checked in phase A3).
