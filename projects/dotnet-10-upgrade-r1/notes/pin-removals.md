# Pin-Removal Evidence Ledger (tasks 002 → 003 → 004)

> **Purpose**: Record every superseded CVE-pin `PackageReference` removed during the net10 retarget, with the inbox-version-≥-CVE-fixed-version justification required by spec FR-04. The canary evidence (task 002) is reused by 003 (Core/Dataverse) and 004 (BFF).
> **Rule (FR-04)**: a pin is removable ONLY when the net10 inbox/shared-framework version is ≥ the pinned CVE-fixed version. If not confirmable → keep the pin + escalate (CLAUDE.md §6).

---

## Task 002 — `Spaarke.Scheduling` (the canary)

**Date**: 2026-08-11 · **Outcome**: retargeted `net8.0 → net10.0`, `dotnet build -c Release` green with **0 warnings / 0 errors** (warnings-as-errors satisfied). **No pins removed.**

### Finding: Scheduling carries NO superseded framework pins

`Spaarke.Scheduling.csproj` package refs:

| Package | Version | net10 disposition |
|---|---|---|
| `Cronos` | 0.13.0 | Third-party (cron parsing); not framework-provided → no NU1510. Keep. |
| `Microsoft.Extensions.Hosting.Abstractions` | 8.0.0 | For a `Microsoft.NET.Sdk` **class library**, Extensions.* are ordinary NuGet refs (NOT shared-framework-provided as they are for Web SDK apps) → **no NU1510** on net10. Left at 8.0.x — see deferral below. |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.3 | same | 
| `Microsoft.Extensions.Options` | 8.0.2 | same |

**Deviation from design §5 H4**: H4 enumerated the superseded CVE pins (`System.Text.Json 8.0.5`, `System.Formats.Asn1 8.0.1`, `System.Security.Cryptography.Pkcs 8.0.1`, `System.Text.RegularExpressions 4.3.1`, `System.Security.Cryptography.Xml 8.0.4`) as the warnings-as-errors risk that made Scheduling "handle first." **Those pins are NOT in `Spaarke.Scheduling` — they live in Core / Dataverse / BFF.** Scheduling was still the right canary (it is the only lib with `TreatWarningsAsErrors=true` set locally *and* the smallest), but the actual NU1510 pin-cleanup work lands in **task 003 (Core + Dataverse)** and **task 004 (BFF)**. This ledger is pre-seeded here for them.

### Deferral: solution-wide `Microsoft.Extensions.*` → 10.0.x alignment

Design §6.1 calls for aligning `Microsoft.Extensions.*` to 10.0.x. **Not done in 002** — deliberately. Bumping only Scheduling's Extensions to 10.0.x while Core/Dataverse (its ProjectReferences) remain net8/8.0.x would *increase* cross-project version skew mid-migration, against the design §4 "one coherent change" principle. The Extensions family alignment is done atomically across Core/Dataverse/BFF in **tasks 003/004** where the whole graph moves together. Scheduling builds clean at 8.0.x Extensions on net10 in the meantime (verified).

### Escalation trigger status

`<escalation>` (a pin that can't be confirmed inbox-≥-CVE) — **did NOT fire**. No pin was removed, so no inbox-≥-CVE decision was needed here. First real evaluations happen in task 003.

---

## Task 003 — `Spaarke.Core` + `Spaarke.Dataverse`

**Date**: 2026-08-11 · **Outcome**: both retargeted `net8.0 → net10.0`, `dotnet build -c Release` green (**0 warnings / 0 errors**, 0 NU1605). Escalation trigger (ServiceClient API break) did **not** fire — 1.2.26 compiled clean.

### Package moves landed (design §6.1)

| Package | From | To | Notes |
|---|---|---|---|
| `Microsoft.Extensions.DependencyInjection.Abstractions` (Core) | 8.0.2 | 10.0.1 | ends 8.0/10.0 split-brain |
| `Microsoft.Extensions.Logging.Abstractions` (Core+Dv) | 8.0.3 | 10.0.1 | |
| `Microsoft.Extensions.Configuration.Abstractions` (Dv) | 8.0.0 | 10.0.1 | |
| `Microsoft.Extensions.Caching.Abstractions` (both) | 10.0.1 | 10.0.1 | already aligned; unchanged |
| `Microsoft.Identity.Client` (MSAL, Dv) | 4.79.2 | 4.87.0 | ≥ 4.84.2 floor dragged by Dataverse.Client 1.2.26 ✓ |
| `Microsoft.PowerPlatform.Dataverse.Client` (Dv) | 1.1.32 | 1.2.26 | same ServiceClient API surface (ADR-028 OBO/MI unchanged) |

**Deviation from POML step 3**: `Microsoft.Identity.Web` (+`.MicrosoftGraph`) → 4.14.2 is **N/A here** — neither Core nor Dataverse references Identity.Web; it's a BFF package → **task 004**.

### Pin removals — inbox-≥-CVE evidence (the forcing function)

Retargeted with the 4 CVE pins still present, then built: `TreatWarningsAsErrors` turned **NU1510** into build errors for exactly the framework-superseded packages. NU1510 = "the net10 framework provides this; the pin is unnecessary" → framework version ≥ the CVE-fixed pin. Evidence per pin:

| Pin | CVE (fixed-in) | NU1510 fired? | Action | Justification |
|---|---|---|---|---|
| `System.Formats.Asn1` 8.0.1 | CVE-2024-38095 (8.0.1) | ✅ yes (Core+Dv) | **REMOVED** | net10 shared framework supplies Asn1 (10.0.x ≥ 8.0.1) |
| `System.Text.Json` 8.0.5 | CVE-2024-30105 (8.0.5) | ✅ yes (Core+Dv) | **REMOVED** | net10 supplies STJ (10.0.x ≥ 8.0.5) |
| `System.Text.RegularExpressions` 4.3.1 | CVE-2019-0820 (4.3.1) | ✅ yes (Core+Dv) | **REMOVED** | net10 supplies RegEx (inbox ≥ 4.3.1) |
| `System.Security.Cryptography.Pkcs` 8.0.1 | CVE-2023-29331 (8.0.1) | ❌ **no** | **KEPT** | framework does NOT prune Pkcs → constraint "keep any pin the framework does not supply" applies. Build resolves 8.0.1 with 0 NU1605. |

### ⚠️ Carry-forward finding for task 004 + task 032 (NOT a task-003 regression)

`dotnet list package --vulnerable --include-transitive` on the **isolated** Core/Dataverse libs flags **`System.Security.Cryptography.Xml 8.0.2` (8× HIGH advisories** — GHSA-37gx-xxp4-5rgx, w3x6-4m5h-cxqf, g8r8-53c2-pm3f, 8q5v-6pqq-x66h, cvvh-rhrc-wg4q, 23rf-6693-g89p, mmjf-rqrv-855v, 6588-8gv4-xfgh), pulled transitively (Dataverse.Client/MSAL/Azure.Identity).

- **Pre-existing, NOT introduced by task 003**: Core/Dataverse have never pinned S.S.C.Xml (net8 baseline `git show HEAD~2` confirms) → the isolated net8 build pulled a vulnerable transitive Xml too. No regression → **NFR-03 satisfied for task 003**.
- **Resolved at composition today**: the **BFF pins `System.Security.Cryptography.Xml 8.0.4`** (`Sprk.Bff.Api.csproj:147`). Not duplicated into the libs by design (avoids double-pin; keeping task-003 scope to Core+Dataverse).
- **ACTION for task 004 / 032**: confirm the BFF's `8.0.4` pin actually clears all 8 advisories (several S.S.C.Xml HIGH advisories are fixed only **above** 8.0.4). If 8.0.4 is still in-range → **task 004 must bump** S.S.C.Xml (to the fixed 8.0.x / 10.0.x) and task 032 re-audits the composed graph.

### Step 6 (Dataverse seam integration test)

Not runnable at this stage (needs BFF + test projects retargeted — tasks 004/005). **Deferred to task 030** (full test-suite green) per the POML note. FR-03 acceptance proof lands there.

## Task 004 — `Sprk.Bff.Api`

**Date**: 2026-08-11 · **Outcome**: BFF retargeted `net8.0 → net10.0`, `dotnet build -c Release` green (0 errors, 22 warnings = 8 NU1510 framework-provided-Extensions + SYSLIB/CS source obsoletions owned by tasks 012/013). Escalation trigger (a §6.3 same-major bump carrying an API break) did **not** fire — 0 compile errors.

### Landed cleanly
- TFM net8.0 → net10.0. `SelfContained=false` + `RuntimeIdentifier=linux-x64` + wwwroot sourcemap exclusion all preserved. No `PublishTrimmed`/`PublishAot`/`RuntimeFrameworkVersion` added.
- §6.1 required: `Hosting.Abstractions` 8.0.0→10.0.3; `Identity.Client` 4.79.2→**4.87.0**; `Identity.Web`(+`.MicrosoftGraph`) 4.3.0→**4.14.2**.
- §6.3 same-major catch-ups: `Azure.Identity` 1.17.1→**1.21.0** (also fixed an NU1605 — Identity.Web 4.14.2→Certificate wants Azure.Identity ≥1.17.2); `Cosmos` 3.47.0→3.62.1; `Handlebars.Net` 2.1.6→2.4.3; `Caching.StackExchangeRedis` 10.0.1→10.0.10; `Graph` 5.101.0→**5.105.0** (stays 5.x — Graph 6/Kiota 2 is task 033); `MimeKit` 4.15.1→4.17.0; `MsgReader` 6.0.6→6.1.0; `HtmlSanitizer` 9.1.973→9.2.995; `OpenTelemetry`(+Extensions.Hosting) 1.15.0→1.17.0; `OpenTelemetry.Api` 1.15.3→1.17.0; `Instrumentation.AspNetCore` 1.15.0→1.15.2 + `Instrumentation.Http` 1.14.0→1.15.1 (NU1605 floors from Azure.Monitor.OTel 1.6.0); `Azure.Monitor.OpenTelemetry.AspNetCore` 1.4.0→1.6.0; `Polly` 8.6.5→8.7.0; `OpenMcdf` 3.1.4→3.2.0.
- **Kept per constraint**: 7 Kiota `1.22.0` direct pins (no downgrade, no Graph 6/Kiota 2 here); M.E.AI 10.3.0 (not bumped — would drag OpenAI ≥2.12); `Azure.AI.Projects` beta.8, `PowerBI.Api` 4.x, `Search.Documents` 11.x (§6.4 deferred majors). Left `Blobs`/`ServiceBus`/`KeyVault.Secrets` at current (already recent same-major; design gave no explicit target).
- **BFF pin removals (NU1510-confirmed framework-superseded on the net10 Web framework)**: `System.Text.RegularExpressions` 4.3.1 AND `System.Security.Cryptography.Xml` 8.0.4 both REMOVED — the net10 ASP.NET shared framework supplies 10.0.x (non-vulnerable). NU1510, not NU1903, confirmed for the BFF.

### Forced deviation (documented): `Spaarke.Scheduling` Extensions → 10.0.1
Composing the BFF pulled an **NU1605** downgrade: `Spaarke.Scheduling` still pinned `Logging.Abstractions 8.0.3` while `Spaarke.Core` is 10.0.1. This is the task-002 "align Extensions atomically across the graph, deferred to 003/004" note coming due. Bumped Scheduling's `Hosting.Abstractions`/`Logging.Abstractions`/`Options` 8.0.x → 10.0.1. (Touches a task-002 file — legitimate cross-task fix forced by the composed graph; noted in the Scheduling csproj.)

### 🔴🔴 BLOCKED — NU1903 deletion revealed a live HIGH CVE (task premise was FALSE)

The task constraint + `notes/kiota-cve-finding.md` asserted `NoWarn=NU1903` was a **stale no-op** masking only the already-fixed Kiota 1.21.2 CVE, and told me to "gate the delete on a clean `dotnet restore` showing zero NU1903." **I removed it and the gate FAILED — but not for Kiota.** The clean restore surfaced:

> **`System.Security.Cryptography.Xml 8.0.2` — 8 HIGH advisories** (GHSA-37gx-xxp4-5rgx, w3x6-4m5h-cxqf, g8r8-53c2-pm3f, 8q5v-6pqq-x66h, cvvh-rhrc-wg4q, 23rf-6693-g89p, mmjf-rqrv-855v, 6588-8gv4-xfgh), transitive in **Core, Dataverse, AND Scheduling** (which have `TreatWarningsAsErrors=true` → NU1903 = build error).

So `NoWarn=NU1903` was **actively masking a live HIGH crypto CVE** in the shared libs, NOT a no-op. This is exactly the task-003 carry-forward S.S.C.Xml finding — the shared libs never pinned it and relied on this suppression; the BFF composed its own 8.0.4 pin. On net10 the BFF's S.S.C.Xml is now framework-provided (clean), but the shared libs (non-web) don't get the framework version and still resolve transitive 8.0.2.

**✅ RESOLVED — owner approved Option 1 (2026-08-11).** Applied:
- Pinned `System.Security.Cryptography.Xml` **10.0.11** (latest 10.0.x, non-vulnerable) in Core + Dataverse + Scheduling. This drags `System.Security.Cryptography.Pkcs ≥ 10.0.11`, so the task-003 Pkcs pin (8.0.1) was bumped 8.0.1 → **10.0.11** in Core + Dataverse to avoid NU1605 (an upgrade — still the CVE-fixed line, now net10-aligned).
- Deleted `NoWarn=NU1903` from `Directory.Build.props`.
- **Verified**: `dotnet build -c Release` green; **NU1903 count = 0**; `dotnet list package --vulnerable --include-transitive` reports **"no vulnerable packages"** for BFF + Core + Dataverse + Scheduling (all 4). The 8 S.S.C.Xml HIGH advisories are genuinely CLOSED, not masked.
- **Retroactively resolves the task-003 S.S.C.Xml carry-forward** — the whole composed graph is now clean, so task 032 has no S.S.C.Xml regression to chase.

**Premise correction recorded**: the task constraint + `notes/kiota-cve-finding.md` "NU1903 = stale no-op" claim was FALSE (it masked a live HIGH crypto CVE). `kiota-cve-finding.md` corrected 2026-08-11. Escalated + resolved per CLAUDE.md §6.
