# From provisioning — T240d spike S1 partial results (2026-10-10, dev only)

Source: message from the `customer-provisioning-orchestration-r1` session. Full write-up: their `notes/t240d-ciam-external-contacts-design.md` §"S1 results".

| Check | Result | R3 impact |
|---|---|---|
| (i) Keyless provisioning | **PASS.** A Spaarke-tenant multitenant app with an MI-FIC gets an app-only Graph token for `spaarkeextid` with `roles=[User.Create]`. The authority is `https://login.microsoftonline.com/{ciamTenantId}`; the `*.ciamlogin.com` authority refuses app-only (AADSTS500206). `POST /users` with an `emailAddress` identity → 201 | None for SPA sign-in: users still sign in on `ciamlogin.com`. Server-side provisioning is provisioning-owned |
| (ii) Re-inviting the same email | 400 `Request_BadRequest` / `ObjectConflict` on `proxyAddresses` (they set `mail`) | This is the "already has an account" signal. Any R3 invite UX should show "already registered — sign in" rather than an error. R3 adds no CIAM invite surface; note it for the C3 grant/deep-link copy |
| (iii) / (iv) | Pending. They need a user token from a client registered **in** `spaarkeextid`, which the owner will create in the portal | (iv) is the FR-22 acceptance shape. Task 010 takes its token-shape test cases from it when it arrives; until then use the spec's two forms (`{appId}`, `api://{appId}`) |
