---
name: copilot-for-legal-mechanics-walkthrough-2026-10-29
description: Mechanical walkthrough of Copilot for Legal — Word desktop review/draft flows (playbook→topics/rules→Run Review→tracked changes), Cowork surfaces/skills/outputs/billing, component map, docs-vs-marketing gaps
metadata:
  type: reference
---

## 2026-10-29: How Copilot for Legal actually works (Word + Cowork)

**Question**: Step-by-step user journey, components, I/O, admin, limits for Copilot for Legal in Word desktop and Legal for Cowork.

**Findings**:
- **Word**: a built-in Copilot-sidebar "plugin" (on by default; Cloud Policy "Legal Skills in M365 Copilot in Word"; user toggle via sidebar ... > Manage plugins & skills > Legal). It auto-detects legal docs. Review flow: "Review this contract using a playbook" → upload or Attach from Cloud a .docx playbook → split into topics (rules + example clauses), which the user can edit → **Run Review** → per-topic analysis streams into the pane → suggested edits are inserted as tracked changes (Accept / Accept All). It reviews from playbooks only (not templates). Draft: `@Draft-new-contract` converts a precedent into a template with placeholders (content controls per the blog) → user fills them → Finish Draft. Answers carry numbered clickable citations. Model is not documented. Platforms: "Word desktop" only; Mac is not documented.
- **Cowork**: runs at copilot.cloud.microsoft, in the Copilot desktop app (Windows/macOS) and in mobile apps. The user adds "Legal for Cowork" via Customize > Roles & Industry. The plugin has 20 skills, invoked by `/`, by `+`, or implicitly. Outputs go to the OneDrive Cowork folder, or Cowork edits Office files in place in OneDrive/SharePoint (version history is kept). Access requires a spending policy (Copilot > Cost Management) with a 2,000-credit/user/month minimum. Model profiles apply per policy. Per-task credit cost is not documented; Microsoft publishes a cost estimator instead.
- **Gaps**: the blog says "precedents in SharePoint" and "matters" via Work IQ, but the docs say the user attaches the files. Reusable playbooks are not documented as a saved object. Partner plugins in Word are a registry-blog claim only; iManage separately claims "Cowork also available in Word". The MC names the plugin "Cowork for Legal" while Learn uses "Legal for Cowork". On Anthropic scoping, the models page says tenant-wide, while the governance and credits pages describe group scoping and model profiles.

**Sources**: support.microsoft.com/en-us/word/get-started-with-copilot-for-legal; techcommunity 4564246 (2026-10-15), 4508974 (2026-04-08), 4559682 (2026-09-30); learn manage-copilot-for-legal (10-15), cowork-available-plugins (10-29), use-cowork (10-21), cowork-projects (10-28), cowork-admin-governance (10-27), whats-new (10-29), cowork-plugins (10-29), cowork-models (10-24), cowork-faq (10-29), cowork-access (10-21), usage-based-billing-manage-copilot-credits (10-28), purview/ai-copilot-cowork (10-05); mc.merill.net MC1509799, RM571884; docs.imanage.com Adding iManage plugin for Cowork.

**Open questions**: Word build number/channel floor; whether Word playbook rule edits persist; where Finish Draft saves; per-skill descriptions (Learn lists names only). Related: [[ms-copilot-for-legal-cowork-registry-2026-10-29]], [[copilot-legal-enablement-kit-deployment-paths-2026-10-29]].
