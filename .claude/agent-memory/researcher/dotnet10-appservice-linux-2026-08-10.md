---
name: dotnet10-appservice-linux-2026-08-10
description: .NET 10 on App Service Linux — GA timing, exact runtime strings (colon vs pipe), FDD version matching, slot-swap runtime bump, port 8080, setup-dotnet
metadata:
  type: reference
---

## 2026-08-10: .NET 10 on Azure App Service Linux (FDD deploy)

**Question**: Is .NET 10 GA on App Service Linux; exact runtime strings/CLI; FDD version-match rules; zero-downtime runtime bump; Linux gotchas.

**Findings**: .NET 10 GA'd 2025-11-11; App Service GA announced at Ignite 2025 (2025-11-18) for Windows+Linux, but rollout was PHASED — portal kept "(Preview)" tag for weeks in some regions (users reported .NET 8's rollout took 1-2 days vs .NET 10 taking months in stragglers). Verified empirically 2026-08-10 on this machine: `az webapp list-runtimes --os-type linux` returns `DOTNETCORE:10.0` (plus 9.0, 8.0). **Delimiter trap**: `az webapp create --runtime` / `list-runtimes` use COLON (`DOTNETCORE:10.0`); `az webapp config set --linux-fx-version` / the stored `linuxFxVersion` site-config property use PIPE (`DOTNETCORE|10.0`). FDD apps: blessed image carries only its own major; host roll-forward is minor-within-major, so net10.0 app on DOTNETCORE|8.0 image dies with "It was not possible to find any compatible framework version. …'Microsoft.AspNetCore.App', version '10.0.x' was not found" → container exits → "Application Error :(" / 503. Zero-downtime bump: `linuxFxVersion` IS a swapped setting ("Language framework settings such as .NET version" in swapped list) → set 10.0 on staging slot + deploy net10.0 build there + validate + swap = atomic. Auto swap NOT supported on Linux — manual swap or `--action preview`. Blessed .NET image listens on 8080 (PORT env; .NET 8+ container default changed 80→8080); don't hardcode ASPNETCORE_URLS. Container start limit 230s default, `WEBSITES_CONTAINER_START_TIME_LIMIT` up to 1800. setup-dotnet current major v6; `dotnet-version: 10.0.x` works; global.json overrides; `dotnet-quality: preview` no longer needed post-GA.

**Sources**:
- https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnetcore (CLI commands, DOTNETCORE|8.0 pattern)
- https://learn.microsoft.com/en-us/azure/app-service/deploy-staging-slots (swapped-settings list incl. framework version; auto-swap-no-Linux; swap-with-preview)
- https://azure.github.io/AppService/2025/08/26/dotnet-10-preview-on-App-Service.html (preview announcement)
- https://techcommunity.microsoft.com/blog/appsonazureblog/whats-new-in-azure-app-service-at-msignite-2025/4468207 (GA at Ignite 2025)
- https://learn.microsoft.com/en-us/answers/questions/5619307/ (phased rollout / Preview-tag lag)
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/8.0/aspnet-port (80→8080)
- https://github.com/actions/setup-dotnet (v6, version syntax)
- Local `az webapp list-runtimes --os-type linux` (empirical, 2026-08-10)

**Open questions**: Whether every region has dropped the Preview tag by now (verify with list-runtimes against the target region/subscription at design time — my local CLI shows GA-style plain `DOTNETCORE:10.0`).
