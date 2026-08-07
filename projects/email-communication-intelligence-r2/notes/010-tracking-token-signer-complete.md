# Task 010 — HMAC tracking-token signer (FR-A1 / NFR-07) — COMPLETE (code)

> 2026-08-06. FULL rigor · opus·xhigh · security-critical crypto. Committed to the branch.

## What shipped

- **NEW** `Services/Communication/Tracking/ITrackingTokenSigner.cs` — the seam + `TrackingTokenPayload`
  (`recordType, recordId, tenantId, issued`) + `TrackingTokenVerification` (`IsValid` + optional payload).
- **NEW** `Services/Communication/Tracking/TrackingTokenSigner.cs` — HMAC-SHA256 sign/verify.
  - Token = `base64url(payloadJson).base64url(HMAC(key, payloadJson))`; the signature covers the exact
    carried bytes, so verify is independent of JSON serialization determinism.
  - `VerifyAsync` recomputes the HMAC and compares with `CryptographicOperations.FixedTimeEquals`
    (constant-time) **before** trusting the payload; every parse/decode/tamper/forgery path returns
    `Invalid` and NEVER throws (NFR-07).
  - Key resolved from **Key Vault by secret NAME** (`TrackingFooterOptions.SigningKeySecretName`, per-tenant
    override supported — from task 011) using the **central injected `TokenCredential`** (Program.cs) — no
    credential is `new`-ed (ADR-028). Key is **cached per secret name (10-min TTL)** so the hot path never
    round-trips Key Vault; a rotated secret still propagates without a redeploy.
  - Best-effort/non-fatal (NFR-04): unconfigured / KV failure → `SignAsync` returns null (no footer token),
    `VerifyAsync` returns `Invalid`. The key is never logged.
- **MOD** `Infrastructure/DI/CommunicationModule.cs` — `AddSingleton<ITrackingTokenSigner, TrackingTokenSigner>()`
  unconditional (ADR-010; the FEATURE gate is 011's `TrackingFooterOptions.Enabled`, not the registration).
- **NEW** `tests/…/Services/Communication/TrackingTokenSignerTests.cs` — 18 tests through the public API via a
  key-override subclass (no Key Vault, no transport mocks): round-trip (incl. null tenant), tampered signature,
  tampered payload, forgery (different key), 10 malformed inputs (never throws), key-unavailable → null/Invalid,
  wrong-length signature. **18/18 green.**

## Deviation from the POML (directional)

POML step 1 named a synchronous `TryVerify(token, out payload) → bool`. **Implemented async**
(`SignAsync` / `VerifyAsync` returning `TrackingTokenVerification`) because the signing key is resolved from
Key Vault (async I/O) — a sync `out`-param cannot await the key load without a fragile pre-warm, and
sync-over-async on a hot path risks deadlock. The no-throw + bool-valid + payload semantics are preserved.
All acceptance criteria (round-trip, tamper, forgery, malformed, FixedTimeEquals, unconditional DI) are met.
Callers 012/013 are already async.

## Escalation triggers — evaluated, neither fired

- **#1 (no central credential to reuse):** a central `TokenCredential` IS registered (Program.cs:46,
  UAMI-pinned) + the runtime KV `GetSecretAsync` pattern exists — reused, no second credential path.
- **#2 (tamper-evidence needs > HMAC / dual-key rotation):** single-key HMAC-SHA256 satisfies NFR-07
  tamper-evidence. Graceful dual-key rotation is deliberately out of scope (spec names a single secret);
  if ever needed → ADR-028 Path-A escalation, not a silent change. Documented as the rotation-scope boundary.

## Verification

- BFF build 0 errors. Signer tests 18/18 green. CVE scan: no vulnerable packages; **zero new NuGet**
  (`Azure.Security.KeyVault.Secrets` already referenced by SpeAdminModule). Publish **48.33 MB compressed
  (incl 4 PDBs)** — +0.01 MB vs the 48.32 baseline (source-only). `/conflict-check`: no open-PR overlap on
  `CommunicationModule.cs` or `Services/Communication/Tracking`.
- Step 9.5: adr-check 0 violations; code-review clean.

## Operator activation (runtime config — NOT code; needed before the footer feature works)

The code is complete + inert until configured. To ACTIVATE (per firm/tenant, no redeploy — ADR-018):
1. **Generate the key** — `openssl rand -base64 32` (32 random bytes, base64). **Never print/commit its value.**
2. **Store in Key Vault** as a secret (choose a name, e.g. `communication-trackingfooter-signingkey`).
3. **App config**: set `KeyVaultUri` (already used by SpeAdminModule) + `Communication:TrackingFooter:SigningKeySecretName`
   = that secret name, and `Communication:TrackingFooter:Enabled = true` when ready to stamp outbound mail.
4. The App Service **managed identity** needs `get` on Key Vault secrets (Key Vault access policy / RBAC
   "Key Vault Secrets User").

## Unblocks

- **012** (inject the signed footer on the outbound send path) — now code-runnable.
- **013** (`TrackingTokenRung` — verify inbound footer tokens) — now code-runnable.
Both consume `ITrackingTokenSigner`; both remain FEATURE-inert until the operator activation above.
