---
name: claude-plugin-install-runtime-2026-11-07
description: Anthropic Claude (not MS) plugin/skill install paths, zip rules, component x surface matrix, Cowork cloud-default shift, script runtime, Claude for Word skills, data terms
metadata:
  type: reference
---

## 2026-11-07: Claude plugin install + runtime (individual vs team)
**Question**: How do customers install, configure and run a Claude plugin zip (emailed kit) across chat, Cowork, Claude Code and Claude for Word, for individuals vs Team?
**Findings**:
- **Upload:** a zip uploaded at Customize > Plugins > Add > Upload plugin is account-scoped and syncs to Claude Code (v2.1.273+ as `name@synced`).
- **Zip rules:** one `.claude-plugin/plugin.json`, at the root or in a single top-level folder. No top-level `bin/`. Limits 200 MB / 5,000 files.
- **Claude Code:** `--plugin-dir` accepts a .zip, but session-only. A persistent CLI install needs a marketplace (git, local directory or marketplace.json), not a zip.
- **Matrix:** skills and commands load everywhere (commands load as skills in chat). Agents and hooks load only in Cowork and Code. `userConfig`, output styles and LSP are Code only. Plugin-root CLAUDE.md is ignored.
- **Cowork moved to cloud:** Pro/Max/Ultra new tasks run in the cloud since 2026-10-06, with no setting. Team defaults cloud on; Enterprise cloud off. Cloud reaches connected local folders only through the open Desktop app, as per-file copies.
- **Team rollout:** Owner > Org settings > Plugins & skills > Upload a plugin > Installed by default/Required. Upload new version + Revert. Team default Publishing = Open; skill sharing is on by default.
- **Word:** gets enabled skills plus turned-on plugins (names sent per request). It does tracked changes. Script execution there is UNDOCUMENTED.
- **Sandbox:** the API code-exec sandbox is Python 3.11 with python-docx preinstalled. The claude.ai sandbox version is unpublished.
**Sources**: claude.com/docs/plugins/{platform-support,admin,build,overview,share,org-sync}; claude.com/docs/skills/{overview,how-to}; claude.com/docs/cowork/*; support.claude.com 14479288, 15520349, 13455879, 13837440, 12512180, 12111783, 11175166; claude.com/docs/office-agents/{word,connectors-and-skills,data-storage,third-party-platforms}; code.claude.com/docs/en/plugins/{install,components,cli-reference}; platform.claude.com code-execution-tool. Report: projects/spaarke-legal-ai-accelerator-claude-r2/notes/research/claude-install-and-runtime.md
**Open questions**: Word runs skill scripts? Cloud Cowork Python version and lxml, and write-back to a connected folder? Self-uploaded plugin re-upload = in-place update? Is Ultra under consumer terms (not named in privacy pages)?
