---
name: net10-upgrade-package-compat-2026-08-10
description: .NET 8→10 upgrade NuGet compatibility audit (2026-08-10) — zero blockers; Dataverse.Client 1.2.26 runs on net10 via net8 asset; Graph v6/Kiota 2.0, Search 12, AppInsights 3.x, PowerBI 5.x are OPTIONAL majors
metadata:
  type: project
---

# .NET 10 upgrade — NuGet package compatibility audit (2026-08-10)

**Question**: Do the BFF's ~35 key NuGet deps have net10-compatible releases; any blockers or forced major bumps?

**Findings** (verified on nuget.org 2026-08-10):
- **ZERO blockers.** Every package either ships a net10.0 asset or targets net8.0/netstandard2.0 (runtime-compatible on net10).
- **Dataverse.Client**: latest **1.2.26** (2026-07-02). TFMs = net8.0 + net462/472/48 — NO dedicated net9/net10 target, runs on net10 via net8.0 asset. WCF deps are `System.ServiceModel.Http/Primitives ≥4.10.3` client packages (netstandard2.0) — supported on modern .NET, not a blocker. Project's pinned **1.1.32 predates the net8.0 target** (net8 added in 1.2.1/1.2.2, net6 removed in 1.2.5) → bump to 1.2.x strongly recommended; forces MSAL ≥4.84.2.
- **Majors AVAILABLE but not REQUIRED by the runtime upgrade**: Microsoft.Graph 5→**6.5.0** (v6.0.0 2026-05-12, explicit net10 TFM, Graph.Core 4.x + Kiota **2.0.0** move together); Azure.Search.Documents 11.6→**12.0.0** (2026-05-01); ApplicationInsights.AspNetCore 2.23→**3.1.2** (2.21/2.22 deprecated); Microsoft.PowerBI.Api 4.21→**5.1.0**; Azure.AI.Projects beta.8→**2.0.1 GA**; JsonSchema.Net 7→**9.4.0**.
- **Agent stack**: Microsoft.Agents.AI GA **1.17.0** (from rc1); Agents.Hosting.AspNetCore **1.7.129**; M.E.AI **10.8.3** (requires OpenAI ≥2.12 → OpenAI **2.13.0**). Azure.AI.OpenAI stable still 2.1.0; beta line at 2.9.0-beta.1.
- **Microsoft.Extensions.*** 8.0.x → 10.0.x is a MANUAL csproj/CPM bump (patch-level within .NET 10 wave; StackExchangeRedis 10.0.10). Microsoft.Extensions.Http.Polly is formally **deprecated** (10.0.10 exists, still works) → migrate to Microsoft.Extensions.Http.Resilience when convenient.
- Everything else = same-major catch-up: Azure.Identity 1.21.0, Azure.Core 1.61.0, Blobs 12.29.1, ServiceBus 7.20.2, KV Secrets 4.11.0, Identity.Web 4.14.2 (explicit net10 TFM), MSAL 4.87.0, OTel 1.17.0, Azure.Monitor.OTel.AspNetCore 1.6.0, MimeKit 4.17.0, MsgReader 6.1.0, OpenMcdf 3.2.0, Handlebars 2.4.3, QuestPDF 2026.7.2, Polly 8.7.0, HtmlSanitizer 9.2.995. DocumentFormat.OpenXml 3.5.1, ACS Chat 1.4.0, ACS Identity 1.3.1, DocumentIntelligence 1.0.0 already latest.

**Sources**: nuget.org package pages (all above), github.com/microsoftgraph/msgraph-sdk-dotnet/releases (v6.0.0 notes), github.com/microsoft/PowerPlatform-DataverseServiceClient/releases.

**Open questions**: AppInsights 3.x internals (assumed OTel-based, unverified) — project double-instruments AppInsights + Azure.Monitor.OTel, consolidation candidate. rc1→GA breaking changes in Microsoft.Agents.AI not enumerated.

## Follow-up resolved 2026-08-10: Graph 5.x servicing status (v6 NOT forced into net10 scope)

- **Official policy** (learn.microsoft.com/graph/versioning-and-support): previous major gets **security fixes only for 12 months** from latest-major release. v6.0.0 GA 2026-05-12 → Graph 5.x security-fix window runs to **~2027-05-12**.
- **Releases**: last 5.x = **5.105.0 (2026-04-30)**; zero 5.x releases after v6 GA (consistent with security-only mode, no security issue since). Latest 6.x = 6.5.0 (2026-08-06).
- **Only known CVE in the stack**: **CVE-2026-44503 / GHSA-7j59-v9qr-6fq9** (Kiota RedirectHandler leaks Cookie/Proxy-Authorization/custom headers on cross-host 3xx; CVSS4 7.0 High; published 2026-04-30). NuGet patched version = **Microsoft.Kiota.Abstractions 1.22.0** — fixed WITHIN the 1.x line; Spaarke's explicit Kiota 1.22.0 pin is PATCHED. The 5.x-era Graph.Core 3.2.5 transitively pulled vulnerable 1.21.1 (msgraph-sdk-dotnet-core#1047); explicit pin overrides. No advisories against Microsoft.Graph / Microsoft.Graph.Core themselves.
- **CVE-2026-41134** (Kiota <1.31.1 codegen literal injection) = Kiota CLI generator only, not the runtime libs — irrelevant.
- **Verdict**: Graph 5.101.0 + Kiota 1.22.0 on net10 is an ACCEPTABLE posture through ~May 2027. Recommend cheap bump to 5.105.0 (final 5.x). Plan v6 + Kiota 2.0 migration as its own project before ~2027-05-12.
