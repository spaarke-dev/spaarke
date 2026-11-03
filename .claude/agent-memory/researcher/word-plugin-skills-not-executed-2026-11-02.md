---
name: word-plugin-skills-not-executed-2026-11-02
description: Why a user-uploaded agentSkills plugin runs in Cowork but not Copilot in Word (2026-11-02) - no host field in manifest/SKILL.md; Word lists registry plugins (MC1484008 shared Plugins UI) but has no third-party skill loader; Legal-in-Word is first-party Cloud Policy; PPT org skills = admin center Agents>All agents>Add agent (same zip); Excel Office.js .js only; no Word ETA; duplicate/remove mechanics
metadata:
  type: reference
---

# Word vs Cowork: plugin agentSkills execution (2026-11-02)

**Question**: Our skills-only plugin (devPreview manifest, 2 agentSkills, Python scripts) runs in Cowork; Word lists it but says "skill isn't available in this session". Why, and what could make it run in Word?

**Findings** (grades: C cited, O observed in tenant, I inferred, U unknown)
- **No host scoping exists.** `agentSkills[]` items allow only `folder` (`additionalProperties:false`) in both vDevPreview and v1.30 schemas (C, schema JSON fetched 2026-11-02). SKILL.md: MS requires only name/description; agentskills.io has free-text `compatibility` (≤500) and `metadata`; Excel docs recommend `metadata.tags: excel` (C) — enforcement by any host U. No display-name field anywhere (C); copilot-camp sample uses undocumented `cowork: {category, icon}`.
- **Host matrix**: Cowork = runs plugin skills + scripts (C). Excel = plugin agentSkills packages run, scripts = Office.js `.js` only, preview, Beta/CC-Preview build 2608+, `atk install --scope Personal` (C, excel-skills 2026-08-13; sample manifest 1.29 skills-only). PowerPoint = personal OneDrive skills + **org skills deployed as the same unified-manifest zip via M365 admin center Agents > All agents > ... > Add agent**, scoped users/groups, GA Win/Mac/web Sept 2026 (C: support "Deploy organization skills for Copilot in PowerPoint"; MC1478961 pub 2026-09-24; MC1478960; MC1463509). PPT skills described as "instruction-based"; script execution undocumented (U). Copilot Chat = skills inside declarative agents (sandbox, no network) (C); standalone plugin skills in Chat U. **Word = no custom/org/plugin skill loader documented**; Legal-in-Word is first-party, governed by M365 Apps Cloud Policy "Legal Skills in M365 Copilot in Word", Word desktop (C, manage-copilot-for-legal upd 2026-10-31) — not a registry plugin. Outlook U.
- **Why Word shows it**: MC1484008 (2026-09-30) replaces Agents/Skills/Customize buttons with one Plugins button across apps incl. Word, UI rollout late Oct -> late Nov 2026; registry is cross-app. Listing != loading (O + I).
- **No Word ETA**: no MC/roadmap/release-notes item for Word custom or org skills (release notes upd 2026-10-31 mention PPT only); `OfficeDev/office-js-docs-pr` main `docs/word/` has no skills page (last commit 2026-10-30); FiveForward 2026-10-16 "Microsoft has not yet published guidance for custom skills in Word".
- **Dupes/removal**: Cowork uploaded skill with same name -> "keeps both and adds a number" (C cowork-customize). Plugin removal: Customize > plugin > **Remove from Cowork**; revoked/removed packages drop "on the next sync cycle", active conversations continue (C cowork-plugins / plugin-dev FAQ). Sideloaded packages also visible in Teams > Apps > Manage your apps (Excel doc's uninstall path) and `atk uninstall` with TitleId/AppId (C). Keep app `id` GUID stable across versions (C) — new GUIDs create parallel installs (I).
- PPT/Excel personal skills folder path is not documented by Microsoft (created via Manage skills > Custom skills > Create OneDrive folder); a 3rd party says `Documents/Copilot/Microsoft Excel/skills` (S, unverified). Not the Cowork `Documents/Cowork/skills` folder (C).

**Sources**
- https://developer.microsoft.com/json-schemas/teams/vDevPreview/MicrosoftTeams.schema.json , .../v1.30/... (fetched 2026-11-02)
- https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-skills (2026-08-13/08-14)
- https://github.com/OfficeDev/Office-Add-in-samples/tree/main/Samples/excel-copilot-skill (manifest 1.29)
- https://support.microsoft.com/en-us/powerpoint/copilot/deploy-organization-skills-for-copilot-in-powerpoint (undated)
- https://support.microsoft.com/en-us/PowerPoint/copilot/copilot-in-powerpoint-skills ; https://support.microsoft.com/en-us/excel/copilot/copilot-in-excel-skills (undated)
- https://mc.merill.net/message/MC1478961 (2026-09-24), MC1478960, MC1463509, MC1484008 (2026-09-30)
- https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal (2026-10-13/10-31)
- https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development (2026-10-02/10-21); cowork-customize, cowork-plugins (upd 2026-10-21 / 10-29)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-type-skills (2026-09-30/10-27) "host support vary by Microsoft experience"
- https://learn.microsoft.com/en-us/copilot/microsoft-365/release-notes (upd 2026-10-31)
- https://fiveforward.co.uk/insights/skills-arrive-in-copilot-in-word/ (2026-10-16); https://agentskills.io/specification

**Open questions**: Does an admin-deployed package (Agents > All agents > Add agent) appear under "Organization" in Excel/PPT and change Word's behaviour? Does PPT execute `.py` scripts? Does Word Beta Channel have a Custom skills entry? Does `metadata.tags` gate hosts? Related: [[word-copilot-pane-plugins-playbooks-addin-2026-10-31]], [[cowork-skill-runtime-and-mvp-claims-2026-10-30]], [[word-vs-cowork-legal-boundary-and-onboarding-2026-10-31]].
