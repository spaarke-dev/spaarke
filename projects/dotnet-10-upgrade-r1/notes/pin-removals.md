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

_(to be filled by task 004 — remaining pins + the `NoWarn=NU1903` stale-suppression deletion)_
