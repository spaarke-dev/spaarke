---
name: cowork-monthly-limit-spending-policy-2026-11-05
description: Cowork "% of monthly limit remaining" banner = per-user limit in an M365 admin center Cost Management spending policy; paths, roles, reset, price, gaps
metadata:
  type: reference
---

**Question**: Where does an admin see/change the per-user monthly Cowork limit behind the "8% of monthly limit remaining" banner; unit, default, cost, user visibility, reset, cost drivers.

**Findings** (all Learn, ms.date Oct-Nov 2026):
- Limit lives in an **identity-based spending policy**: M365 admin center > **Copilot > Cost Management > Configuration** > "+ Add spending policy" (or edit existing; details fly-out > Save changes). Steps: scope (All users / specific security groups) > select agents and services (tick **Cowork**) > "Select the monthly spending limit for users (optional)" > billing method > create. Unit = **Copilot Credits**.
- Roles: create = Global admin or Billing admin; AI admin / License admin can edit limits/alerts but not create; AI Reader/Global Reader read-only dashboards.
- A spending policy that selects Cowork is the ACCESS grant (low limit does not block); GA deprecated the Frontier "All Agents > Cowork" control. Enforcement is asynchronous (can start tasks after limit).
- Reset: `/cost` page says 00:00 UTC on the 1st (calendar month); policy page says "first of the month". No carry-over. Not licence anniversary.
- User sees own usage only via `/cost` in Cowork (approx, current task + month used/remaining, free). Admin per-user: Cost Management > Consumption > Users view (refresh 2h); Viva Insights Consumption Dashboard; M365 admin center > Copilot > Cowork (usage report, task counts only, no credits per user).
- Cost: billing method = capacity packs > P3 prepay > pay-as-you-go on linked Azure sub. $0.01/credit is Copilot Studio PAYG rate per licensing guide; no Cowork-specific rate stated.
- Cost drivers: models, runtime, context, tools (licensing guide). Light 100-300, medium 300-700, heavy >700 credits per task. Higher reasoning effort = more credits. Counted: model responses, tool/skill calls, image gen, browser tasks. Custom-skill authoring/testing consumes credits.

**Docs silent / unresolved**: default per-user limit value (Learn: "at least 2,000 credits per user per month" min; third parties say 7,000 recommended / 200 default - unverified); banner text itself not documented; whether skills running Python cost more than other tool calls; per-credit Cowork meter; whether an over-limit running task is billed (Learn: at MS discretion, not billed, not shown).

**Sources**:
- https://learn.microsoft.com/en-us/copilot/implement/usage-based-billing-manage-copilot-credits
- https://learn.microsoft.com/en-us/copilot/implement/usage-based-billing-copilot-credits-setup
- https://learn.microsoft.com/en-us/copilot/implement/usage-based-billing-copilot-credits-monitor-spending
- https://learn.microsoft.com/en-us/copilot/implement/usage-based-billing-copilot-credits-cost
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-admin-governance , cowork-access , cowork-models
- https://www.microsoft.com/licensing/guidance/CopilotCreditsGuide
- https://learn.microsoft.com/en-us/viva/insights/org-team-insights/ai-cost-dashboard
