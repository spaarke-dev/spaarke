---
name: work-iq-ga-refresh-2026-07-14
description: Work IQ reached GA 2026-06-16, Copilot-Credits consumption billing, delegated-only (app-only NOT supported); Context API is agent-facing part of GA surface, not a future user-facing feature
metadata:
  type: project
---

# Work IQ GA snapshot refresh (2026-07-14)

**Fact**: Confirmed for the `knowledge/work-iq/` snapshot refresh. Work IQ API reached GA on **2026-06-16** (public preview earlier). Billing = **usage-based / Copilot Credits** consumption model, **independent of M365 Copilot licensing** (licensed users covered in Copilot experiences but billed for custom/3rd-party agents; unlicensed users billed by usage). Auth is **Microsoft Entra delegated only — application-only/app-only auth is NOT supported**; OBO is supported. Scope `WorkIQAgent.Ask`, app ID URI `api://workiq.svc.cloud.microsoft`. Surface = 4 domains: Chat, Context, Tools, Workspaces, over A2A + remote MCP + REST. Work IQ MCP = 10 generic verb tools (fetch/create/update etc.), Rego/OPA policy engine, user-scoped.

**Why:** Refresh of curated 2026-05-14 snapshot (was public-preview, per-user-Copilot-license framing). Design doc dated 2026-07-14 cited the 2026-06-02 M365 blog.

**How to apply:** Two design-doc claims need correction. (1) Design doc calls the **Context API** a "distinct FUTURE user-facing augmentation." Reality: Context API is part of the **GA (2026-06-16) surface**, and it is **agent/server-facing NOT user-facing** — "aggregates the content Copilot would use... returns context in a format designed for agent consumption." Still delegated-only, so still NOT an app-only batch classifier. (2) Docs are mixed preview/GA: overview page (ms.date 2026-06-16) is GA-worded, but several sub-pages still titled "(preview)" — API quickstart, Work IQ MCP overview, Foundry integration. Not fully verified whether ALL surfaces are GA vs some still preview.

**Sources:**
- Blog: https://www.microsoft.com/en-us/microsoft-365/blog/2026/06/02/announcing-the-new-work-iq-apis/
- Overview: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/
- Permissions: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/permissions
- API overview: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/api-overview
- Licensing aka.ms/WorkIQ/licensing
