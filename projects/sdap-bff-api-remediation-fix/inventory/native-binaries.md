# Native Binary Inventory by RID (Task 016)

> **Source**: `deploy/api-publish/runtimes/` after `dotnet publish -c Release` (NO RID specified)
> **Captured**: 2026-05-24
> **Status**: Multi-platform RIDs present (framework-dependent publish without `--runtime`)

---

## Per-RID native binary count + size

| RID | Files | Size (MB) | Size (bytes) | Status vs FR-A1 |
|---|---:|---:|---:|---|
| `browser/` | 1 | 0 | 66,360 | TRIM CANDIDATE (not a server runtime) |
| `linux-arm64/` | 2 | 9 | 10,017,880 | TRIM if Linux target (prod uses x64) |
| `linux-musl-x64/` | 2 | 10 | 10,745,016 | TRIM (Alpine; not used) |
| **`linux-x64/`** | 2 | 9 | 10,450,872 | **KEEP for prod** (`spaarke-bff-prod` is Linux) |
| `osx-arm64/` | 2 | 6 | 6,887,080 | TRIM (no macOS target) |
| `osx-x64/` | 2 | 6 | 7,297,400 | TRIM (no macOS target) |
| `unix/` | 1 | 0 | 427,112 | TRIM if Linux-only |
| `win/` | 6 | 1 | 1,082,136 | KEEP if dev (Windows) needs them |
| **`win-x64/`** | 11 | 21 | 22,811,220 | **KEEP for dev** (`spe-api-dev-67e2xz` is Windows) |
| `win-x86/` | 6 | 10 | 11,244,531 | TRIM (no 32-bit target) |
| **TOTAL** | **35** | **~77** | **81,029,607** | |

---

## 🚨 CRITICAL FINDING: dev vs prod App Service OS mismatch

**Captured during Task 017:**
- **`spe-api-dev-67e2xz`** (dev) — **Windows** App Service (`kind: "app"`, `reserved: false`, `netFrameworkVersion: "v8.0"`, SKU PremiumV3)
- **`spaarke-bff-prod`** (prod) — **Linux** App Service (`kind: "app,linux"`, `reserved: true`)

**Implications for Outcome A (FR-A1 — `--runtime linux-x64`)**:

The design.md §2.4 statement "App Service is unambiguously Linux" is **wrong for dev**. The remediation strategy needs adjustment:

| Environment | Current RID needed | After FR-A1 trim |
|---|---|---|
| dev (`spe-api-dev-67e2xz`) | `win-x64` (21 MB native) | Should trim to `win-x64` ONLY |
| prod (`spaarke-bff-prod`) | `linux-x64` (9 MB native) | Should trim to `linux-x64` ONLY (design's stated target) |

**FR-A1 must split**: either (a) different CI publish flag per-env, or (b) consolidate dev to Linux App Service to match prod, or (c) keep multi-RID publish and accept the ~70 MB overhead.

**Recommendation for Phase 2 candidate categorization**: Add this as a HIGH-tier candidate requiring explicit owner decision. Defer FR-A1 SAFE-tier classification pending resolution.

---

## Trim savings estimate (if Phase 2 chooses single-RID per env)

| Strategy | Removed RIDs | Bytes saved | MB saved |
|---|---|---:|---:|
| **Prod-only `linux-x64`** | browser, linux-arm64, linux-musl-x64, osx-*, unix, win, win-x64, win-x86 | 70,578,735 | ~67 |
| **Dev-only `win-x64`** | browser, linux-*, osx-*, unix, win-x86 | 56,775,255 | ~54 |

Note: `win/` (1 MB, 6 files) likely shared with `win-x64/`; verify before trim.

---

## ServiceInterop.dll (FR-A3 target)

**Result**: NOT FOUND in current publish. The previously-suspected Cosmos ServiceInterop.dll duplication is NOT present. FR-A3 ("Remove duplicate Cosmos ServiceInterop.dll") may be already-resolved by upstream Cosmos SDK changes — verify before Phase 4 task 042.

---

## Other duplicates (count > 1 across publish tree)

| Filename | Copy count | Notes |
|---|---:|---|
| `System.ServiceModel.Primitives.resources.dll` | 13 | One per locale; normal |
| `System.ServiceModel.Http.resources.dll` | 13 | One per locale; normal |
| `MsgReader.resources.dll` | 9 | One per locale; normal |
| `System.Drawing.Common.dll` | 3 | Investigate — may be path/RID variance |
| `zlib1.dll` | 2 | win/ + win-x64/ — normal Windows runtime variance |
| `vcruntime140_1.dll` | 2 | Same |
| `vcruntime140.dll` | 2 | Same |
| `qpdf.dll` | 2 | QuestPDF native; verify per-arch |
| `msvcp140.dll` | 2 | Same |
| `libwinpthread-1.dll` | 2 | Same |

Native runtime duplicates trim out automatically when RID-specific publish is used.
