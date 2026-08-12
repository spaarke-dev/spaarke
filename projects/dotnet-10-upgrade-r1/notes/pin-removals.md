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

_(to be filled by task 003 — this is where the H4 CVE pins are actually removed, each with inbox-≥-CVE evidence)_

## Task 004 — `Sprk.Bff.Api`

_(to be filled by task 004 — remaining pins + the `NoWarn=NU1903` stale-suppression deletion)_
