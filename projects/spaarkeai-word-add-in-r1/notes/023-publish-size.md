# Task 023 — BFF publish size (root CLAUDE.md §10 bullet 4)

| | Commit | Compressed size | Method |
|---|---|---|---|
| **master (fresh build)** | `origin/master` = `e0a6f87c4` | **45.35 MB** | measured 2026-09-11 by task 030 on that exact commit; `origin/master` confirmed unchanged at `e0a6f87c4` when this task measured, so reused per the coordinator's instruction |
| **this branch** | project head `37494ef9c` + task 023 working tree | **45.38 MB** (47,583,039 bytes) | `dotnet publish -c Release -o deploy/api-publish` from `src/server/api/Sprk.Bff.Api`, then PowerShell `Compress-Archive -Path "deploy\api-publish\*"` at the default (Optimal) level — the method `scripts/Deploy-BffApi.ps1` uses. PDBs **included** (4), 214 files. |
| **Delta** | | **+0.03 MB** | |

- **Tool:** PowerShell `Compress-Archive`, Optimal, for both figures. Per `.claude/constraints/azure-deployment.md`,
  zips made with different tools differ by ~1.3 MB on identical content, so the two figures are comparable only
  because both used this one.
- **Attribution:** +0.03 MB is branch-versus-master. It includes the project branch's earlier BFF commits (for
  example task 012's identity resolver) as well as this task. This task's own contribution is therefore **≤ +0.03 MB**.
  It adds no package, no DI registration, one filter class, and methods on three existing classes.
- **Thresholds:** well under the +5 MB single-task escalation line. The cumulative size is 45.38 MB, below the
  55 MB architecture-review line and the 60 MB hard ceiling.
- **CVE:** `dotnet list package --vulnerable --include-transitive` → "The given project `Sprk.Bff.Api` has no
  vulnerable packages given the current sources."
