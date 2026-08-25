# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-24 (task 090 close-out)
> **Recovery**: nothing to recover — **the project is closed.**

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **none — project COMPLETE** |
| **Status** | ✅ **completed 2026-08-24** |
| **Next Action** | **None for this project.** Everything below belongs to *other* owners. |

**26 of 26 active tasks complete** · 3 deferred (Power BI 040–042, owner decision DEF-001).
Merged to master: PR [#814](https://github.com/spaarke-dev/spaarke/pull/814) + [#816](https://github.com/spaarke-dev/spaarke/pull/816). Deployed and **UAT PASSED** on `spaarkedev1`.

### Critical context

`BFF-API-ClientSecret` is gone from app settings **and** Key Vault. Every BFF-identity confidential client —
**including OBO** — authenticates via a Managed-Identity federated credential.

It is verified as **in live use, not merely configured**: Dataverse `createdby` on the UAT rows is
`# mi-bff-api-dev`, and Entra shows MI sign-ins to Dataverse plus the `AAD Token Exchange Endpoint` assertion
exchange, while the app registration's secret-based sign-ins **stopped at the cutover**. Proof + re-run recipe:
[`notes/decisions/mi-proof-dataverse-side.md`](notes/decisions/mi-proof-dataverse-side.md).

⚠️ **Entra sign-in logs retain 30 days.** That note is the durable artifact — the query is not re-runnable after
roughly 2026-09-24.

---

## Rollback, if ever needed

Config-only, ~2 min. **There is no slot to swap back to** — task 032 deleted it, so a code rollback means a
redeploy with a real outage window. Prefer config.

1. `az keyvault secret recover --vault-name spaarke-spekvcert --name BFF-API-ClientSecret` (**until 2026-11-22**)
2. Restore the 4 app settings (fingerprint `b09a140a603e`)
3. Add `ClientSecret` back to `Graph__Credentials__Order`
4. Set `Graph__Credentials__RequireSecretFreeIdentity=false` — **deliberately**, so the deviation is recorded
   rather than hidden

Service Bus / AI Search rollback secrets are recoverable in the same vault **until 2026-11-23**
(fingerprints `3db62606e51e` / `f20f0def4444`).

---

## ⚠️ Do not "fix"

`ClientSecret` remains in the **code-side canonical default** in `AddCredentialSelection` **on purpose**. 033's
own carried-forward obligation said to remove it; doing so **broke every unconfigured environment** (no
`Graph:Credentials` section + no UAMI ⇒ validator fails fast) and was caught by `CredentialOrderingSeamTests`.
Reverted, with the reasoning recorded at the code. The secret-free guarantee is delivered by **configuration** on
deployed environments instead — which is also what keeps local development possible.

---

## Carried forward — NOT this project's to close

| # | Item | Owner |
|---|---|---|
| 1 | **Local `dotnet run` has no OBO credential path for a fresh setup** (criterion #9). Needs a deliberate replacement — a dev-only FIC or a documented `az login` path — **not** restoring the secret | next auth touch |
| 2 | **Provisioning §5.1** — which app registration the shared Model 1 BFF acts as. MI-FIC works either way; it decides whether onboarding gains a per-customer FIC step (criterion #15) | `customer-provisioning-orchestration-r1` ([#779](https://github.com/spaarke-dev/spaarke/pull/779)) |
| 3 | **Rotate `Dataverse-Checkout-20251218`** — partial values (7 and 12 chars) committed in `c1803e99a` (2026-03-09) under a *"First 5 chars"* caption. Redacted in the working tree; **history untouched**. Valid to 2027-12-18. Cheap **because nothing reads it any more** | security |
| 4 | **051-E code half** — remove the Service Bus SAS branch + `PostConfigure` back-fill, updating `ServiceBusClientGuardTests` in the same commit. Deferred deliberately: the credential is unreachable either way; only recovery cost differs | next Service Bus touch |
| 5 | **`AZURE_CLIENT_ID` logs an ERROR every boot** — not bundled into the secret removal because the Azure Identity SDK reads that variable itself | BFF hygiene |
| 6 | **`/api/containers`** — two endpoints that 403 every caller, always. Pre-existing | API owner |
| 7 | **`/healthz/catalog` Unhealthy** — 12 residual findings: 8 binding rows owned by other in-flight projects (agreements, nda, compose, smart-todo) + 4 legacy description-parity rows with no authored JSON. Carries a **design question**: the check treats *row-without-constant* as Unhealthy but *constant-without-row* as Degraded — in a shared dev env with ~17 worktrees the former is the normal steady state, making the gate un-greenable, and a permanently-red gate stops being read | AI-catalog owner |
| 8 | **5 `OfficeEndpoints` handlers still use the raw `NameIdentifier`-first pattern.** None stamps `CreatedBy`, so none is reachable by the 403 fixed in `77f61574b` | Office add-in owner |
| 9 | **CORS config drift has no forcing function** — origins live only in App Service settings; nothing fails in CI when a deployed env omits a live SWA. This caused the UAT blocker. Also remove the **stale** `Cors__AllowedOrigins__2` (`agreeable-hill-…-preview`, HTTP 404): a dead `azurestaticapps.net` host in a **credentialed** allow-list is the exact attacker-registrable risk `66a45cf6a` set out to close | platform |
| 10 | **Word "no extractable content due to unsupported file type"** — the authenticated write completed (record + file profile created); this is content extraction, not auth | owner-routed to a focused project |

---

## Where the reasoning lives

| Document | What it holds |
|---|---|
| [`notes/lessons-learned.md`](notes/lessons-learned.md) | The retrospective. §1 the root cause was a **sentence**; §2 **eight shapes** in which a status code establishes nothing here; §3 forcing functions + the ADR-038 gap (closed as Amendment A1); §4 systematic under-counting; §5 the obligation that would have broken everything |
| [`notes/decisions/mi-proof-dataverse-side.md`](notes/decisions/mi-proof-dataverse-side.md) | Three-layer live proof that MI is in use, + re-run recipe |
| [`notes/decisions/033-secret-removal.md`](notes/decisions/033-secret-removal.md) | The removal, the rung ladder, and the near-miss |
| [`notes/decisions/051-053-live-cutover.md`](notes/decisions/051-053-live-cutover.md) | Service Bus + AI Search cutovers |
| [`notes/uat-findings-2026-08-24.md`](notes/uat-findings-2026-08-24.md) | All six UAT findings — **none an auth-v4 regression** |
| [`notes/test-diet-report.md`](notes/test-diet-report.md) | 73 methods added, **0 scaffolding** |
| [`spec.md` § Success Criteria](spec.md#success-criteria) | The graduation walk, with evidence per criterion |

**The one method worth carrying forward**: *find the log line, the Graph status, or a byte-level artifact — and
if you cannot, **remove the fallback and let success be the proof.*** The second half proved stronger than the
first, and it is how every cutover here was established.
