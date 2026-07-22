# FR-01 Spike — Azure SignalR BFF Footprint: Serverless vs Default (Go/No-Go)

> **Task**: 001 (Phase 0, Gate-Zero) · **Date**: 2026-07-20/21 · **Author**: task-execute (Opus) · **Spec**: FR-01
> **Status**: ✅ COMPLETE — **decision below is the hard go/no-go gate for all Phase 2 (Layer-C) tasks**
> **Measurement host**: Windows, dotnet SDK 8.0.423, framework-dependent `linux-x64` publish (matches App Service), `Compress-Archive -CompressionLevel Optimal` over the publish dir (the NFR-01 convention).

---

## TL;DR — Decision

| | |
|---|---|
| **GO / NO-GO** | ✅ **GO** — Azure SignalR is viable inside the BFF; footprint is negligible and adds 0 new HIGH CVE. |
| **MODE** | ✅ **Serverless** (`Microsoft.Azure.SignalR.Management`) — the send-only spine topology does not use Default's hosted-hub full-duplex capability, so Default's burden-of-proof (spec FR-01) is **not met**. |
| **Layer-C placement** | ✅ **Stays IN the BFF** — no mode breached even the 55 MB review band (max 47.38 MB, ~12.6 MB under the 60 MB hard ceiling). The >60 MB escalation trigger did **not** fire; hub/negotiate does NOT move out of the BFF. |
| **csproj state left for Phase 2** | `Microsoft.Azure.SignalR.Management` 1.33.1 present but **unwired**; `Microsoft.Azure.SignalR` (Default) reverted. Task 020 adds the negotiate endpoint + DI + Placement Justification. |

---

## Measurements

### Baseline (clean checkout, no SignalR SDK)

| Metric | Value |
|---|---|
| Compressed **incl-PDB** | **47.08 MB** |
| Compressed **excl-PDB** | **46.27 MB** |
| File count / PDB count | 243 / 4 |

> **Baseline divergence note**: the documented repo reference baseline is **49.63 MB incl-PDB / 45.87 excl-PDB** (`azure-deployment.md`, 2026-07-08, task 055). This branch measured **47.08 / 46.27** — ~2.55 MB lower incl-PDB. Likely causes: a slightly different package set on this branch and/or `Compress-Archive` version differences vs the reference run. **The deltas below are anchored to THIS spike's own baseline, which is the correct comparison** (same host, same run, same convention) — not to the repo reference number.

### Serverless mode — `Microsoft.Azure.SignalR.Management` 1.33.1

| Metric | Value | Δ vs this baseline |
|---|---|---|
| Compressed **incl-PDB** | **47.38 MB** | **+0.30 MB** |
| Compressed **excl-PDB** | **46.58 MB** | **+0.31 MB** |
| New HIGH CVE | **0** | — |
| Shipped DLLs | `Microsoft.Azure.SignalR.Common` (281 KB), `.SignalR` (149 KB), `.Management` (143 KB), `.Protocols` (129 KB) = **702 KB uncompressed** | +5 files |

### Default mode — `Microsoft.Azure.SignalR` 1.33.1

| Metric | Value | Δ vs this baseline |
|---|---|---|
| Compressed **incl-PDB** | **47.31 MB** | **+0.23 MB** |
| Compressed **excl-PDB** | **46.50 MB** | **+0.23 MB** |
| New HIGH CVE | **0** | — |
| Shipped DLLs | `Microsoft.Azure.SignalR.Common` (281 KB), `.SignalR` (149 KB), `.Protocols` (129 KB) = **559 KB uncompressed** (no `.Management`) | +3 files |

### Bands (NFR-01)

```
 46 ──────── 47.08 (baseline) ── 47.31 (Default) ── 47.38 (Serverless) ──────────── 55 (review) ──── 60 (HARD STOP)
                                      ▲ both modes here                                 ~7.6 MB gap    ~12.6 MB gap
```

Both modes are ~0.07 MB apart — the footprint difference between them is **noise** and does not drive the mode decision.

### CVE detail

The only HIGH advisory in either graph is `System.Security.Cryptography.Xml 8.0.3` — a **pre-existing direct pin in the baseline csproj** (line ~129, pinned to fix earlier GHSA-37gx/… advisories; four *newer* advisories now apply to 8.0.3 with no listed fixed version). **It is NOT introduced by SignalR** (identical in the baseline graph; no SignalR package appears in the vulnerable list in either mode). ➜ **0 new HIGH CVE from SignalR in either mode.**
> Unrelated baseline finding to hand off: `System.Security.Cryptography.Xml 8.0.3` now carries 4 open HIGH advisories with no fix version — worth a separate defer/issue ticket (out of scope for FR-01).

### Cold-start

Precise App Service cold-start delta was **not** measured live (requires an Azure deploy with full Key Vault/Dataverse config; a local `dotnet run` warm-up won't boot without secrets and would be a misleading proxy). **Documented proxy = assembly-load size + startup-registration structural analysis:**

- **Serverless**: adds **zero** startup registrations. The `ServiceManager`/`ServiceHubContext` is instantiated **on-demand by the producer/background job** at publish time, not at DI/boot. No persistent outbound service connection is held at startup. Assembly-load cost ≈ 702 KB. ➜ **effectively zero cold-start impact.**
- **Default**: `AddSignalR().AddAzureSignalR()` registers a **hosted service** + establishes a **persistent service connection** to Azure SignalR at boot, plus hub lifecycle. Nonzero (though small) added startup cost and an always-on outbound connection the BFF must maintain. Assembly-load ≈ 559 KB.

This structural difference — not the ~0.07 MB size delta — is the real operational distinction between the modes.

### CSP `connect-src` verification (`wss://*.service.signalr.net`)

Checked every CSP surface in the repo; explicit pass/fail per surface (not silently assumed):

| Surface | File | connect-src state | Verdict |
|---|---|---|---|
| BFF API responses | `Api/SecurityHeadersMiddleware.cs` | `default-src 'none'; …` (no connect-src) | ✅ **Not a blocker** — the BFF serves JSON/files, it is **not** the browser host that opens the SignalR WebSocket. Its response CSP does not govern the client connection. |
| External SPA (SWA) | `src/client/external-spa/staticwebapp.config.json` | CSP = `frame-ancestors 'self'` only; **no** `connect-src`/`default-src` restriction | ✅ **Not a blocker** — browser default allows `wss://`. Should add an explicit `connect-src wss://*.service.signalr.net` when this host actually subscribes (task 021 consumer). |
| **Power Platform MDA env** (SpaarkeAi code page host) | ⚠️ **not in repo** — runtime admin setting (Power Platform admin center, per-environment CSP) | Cannot be inspected from source | ⚠️ **ACTION ITEM — must verify/configure per target environment.** This is the "silent-fallback risk" both researcher notes flag: if an admin has env CSP enabled without `wss://*.service.signalr.net` in `connect-src`, the client connection **silently fails and falls back to poll**. Not a code fix — an operator/provisioning check (fits ADR-027 per-customer provisioning). |

**CSP verdict**: no repo-level CSP blocks SignalR. The authoritative gate is the Power Platform environment CSP, which is a runtime admin setting and **must be verified at provisioning** — recorded here as an explicit action item, not assumed passing.

---

## Mode decision rationale — why Serverless (burden-of-proof-on-Default, FR-01)

The spine is **send-only, one-way, dumb transport**: producers write a durable outbox row, then best-effort *ping*; clients fetch pending envelopes (IDs + minimal display metadata) on ping. There is **no client→server hub RPC**, no full-duplex requirement (project `CLAUDE.md` "spine is dumb transport" + "outbox BEFORE ping").

- **Serverless fits this exactly**: publish-to-group/user/all from the background job *after* the outbox write, via the Management SDK. No hosted hub, no boot-time service connection, BFF stays a pure request/response + job app. This is the "fan-out after persist" pattern the earlier researcher note (2026-07-16) recommends.
- **Default's extra capability is unused**: Default hosts a hub server (full-duplex, boot-time hosted service + persistent connection). A dumb one-way spine invokes none of it. Per FR-01, Default must *justify* itself with real numbers — footprint (+0.23 vs +0.30 MB) and CVE (0/0) are effectively tied, so there is **no measured advantage** to offset Default's structural overhead. **Burden of proof not met.**
- **Reconciling the later researcher note** (`assistant-push-channel-2026-07-15`, which leaned Default + hub-in-BFF): that recommendation's core value — "Azure SignalR removes the ARR-affinity/backplane/scaling burden" — is true of **both** modes (both connect clients to the managed service). Its *Default* lean was written for the assistant channel in isolation and does not survive this project's codified send-only + dumb-spine constraints. This is a documented reconciliation, not a silent override.

---

## Deviations (step 9)

1. **Cold-start measured via structural proxy, not live Azure timing** — see Cold-start section; live App Service measurement deferred to deployment (needs full env config). The structural analysis (Serverless = zero startup registration) is the honest and decision-relevant signal.
2. **Step 3's "minimal `MapHub<T>()` registration" not wired** — framework-dependent (`SelfContained=false`, no trimming) publish includes every referenced assembly regardless of usage, so the package reference alone captures the **full** publish-size delta; a `MapHub` wire-up adds **zero** additional shipped DLLs (ASP.NET Core SignalR types live in the shared framework). Wiring throwaway `Program.cs` code and reverting it would add risk without changing the measured number. Directional step-mode permits this adaptation.
3. **csproj left with the chosen package (per step 7 / constraint 1) but intentionally UNWIRED** — `Microsoft.Azure.SignalR.Management` 1.33.1 remains with a justifying comment; `Microsoft.Azure.SignalR` (Default) reverted. This locks the validated version for Phase 2. Per CLAUDE.md §10, the *wiring* (negotiate endpoint + DI + ADR-032 null-object + Placement Justification + `/conflict-check`) is **task 020's** job. **Reviewer option**: if you prefer a fully clean csproj until task 020 adds it fresh, the single line at the `Microsoft.Azure.SignalR.Management` reference can be stripped — it is isolated and unused. Flagging for your call at the gate.

---

## §10 BFF Hygiene / NFR-01 compliance statement

BFF Hygiene §10 + NFR-01 verified: chosen-mode publish size = **47.38 MB incl-PDB** (Serverless), delta **+0.30 MB** vs this-branch baseline (47.08 MB), **no new HIGH CVEs**, ~12.6 MB under the 60 MB hard ceiling. Placement decision: **Layer-C delivery stays in the BFF** (no >60 MB breach). Full Placement Justification is deferred to task 020 (the task that actually wires + registers the service).

---

## Handoff to Phase 2 (what task 020 inherits)

- **Package + version locked**: `Microsoft.Azure.SignalR.Management` 1.33.1 (Serverless).
- **Pattern**: negotiate endpoint (thin BFF endpoint → Management SDK generates client access token/URL); producer publishes to groups/users after the outbox write. No hosted hub, no `AddAzureSignalR()`.
- **DI**: register the delivery service unconditionally with an ADR-032 null-object fallback (SignalR unreachable ⇒ outbox-only, poll picks up).
- **CSP action item**: verify/configure `wss://*.service.signalr.net` in the target Power Platform environment CSP **before** relying on live push (else silent poll fallback).
- **Do NOT re-litigate mode/placement** — this gate resolved it.
