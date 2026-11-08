# Claude platform: install, configure and run (individual vs team)

> **Researcher report**, 2026-11-07, for `spaarke-legal-ai-accelerator-claude-r2`. Scope: how customers install, configure and
> run a Claude plugin from an emailed zip, on each Claude surface. The Claude for Legal repo contents are out of scope (another agent has them).
> **Labels:** **[C]** cited (source in brackets, fetched 2026-11-07) · **[I]** inferred from cited material · **[U]** undocumented, needs a live test.
> Most claude.com/docs pages show no date. Help Center pages show only "Updated over a week ago" or "this week", unless a date is given.

## Summary (10 lines)

1. **One zip covers chat, Cowork and Claude Code.** Uploaded at **Customize › Plugins › Add › Upload plugin**, it is saved to the user's *account* and syncs into Claude Code (v2.1.273+) at session start. Limits: 200 MB and 5,000 files. [C]
2. **Zip rule:** exactly one `.claude-plugin/plugin.json`, at the root or inside a single top-level folder. Nothing else may sit beside that folder, and there must be **no top-level `bin/`**. [C]
3. **Skills and commands load everywhere. Agents and hooks load only in Cowork and Claude Code.** Local stdio MCP loads only where Cowork runs on the user's machine, plus Claude Code. `userConfig`, output styles and plugin `CLAUDE.md` are Claude Code only (and Claude Code ignores a plugin `CLAUDE.md` too). [C]
4. **Big change since the design doc: Cowork is now cloud by default.** On Pro, Max and Ultra, new tasks have run in the cloud since 2026-10-06, and there is no setting to change it. Team defaults to cloud; Enterprise defaults to cloud off. [C] Design doc §4/§7.2 "Cowork (local) = primary" needs revising.
5. **Where scripts run:** a cloud sandbox for chat and cloud Cowork; a Linux VM on the device for local Cowork (Team and Enterprise only now); the user's own machine for Claude Code. The API sandbox spec is Python **3.11** with **python-docx** preinstalled (so lxml too) and no network. claude.ai's sandbox version isn't published. [C/I]
6. **Folder access:** cloud Cowork reaches *connected* local folders only through the open Desktop app, fetching copies of individual files. Web chat sees uploads only. [C]
7. **Claude for Word** (Pro, Max, Team, Enterprise): it uses the skills enabled in Claude settings and the plugins the user has turned on, does tracked changes and comment replies, and uses org custom connectors. **Whether skill scripts run there is undocumented.** [C/U]
8. **Team rollout, least effort:** an Owner uploads the same zip at Organization settings › Plugins & skills › Add › Upload a plugin and sets **Installed by default** or **Required**. Updates use **Upload new version**, with rollback. No GitHub needed. [C]
9. **Profile:** none of chat, Cowork or Word offers a "plugin settings" store. For v1, a profile file the user keeps (a Cowork folder, or claude.ai Project knowledge) is the only cross-surface option. A team shares it as a shared claude.ai Project or a published "firm profile" plugin. [C/I]
10. **Data terms:** Free, Pro and Max (and, by inference, Ultra) are consumer terms: training is on unless the user opts out, with 5-year retention if on and 30 days if off. Team and Enterprise are Commercial Terms with no training. Word add-in inputs and outputs are deleted within 30 days on every plan. [C/I]

---

## 1. Individual install (Free / Pro / Max / Ultra)

### 1.1 Plans
- Plugins: **Pro, Max, Ultra, Team, Enterprise**. Free isn't listed. [C] [support 13837440; plugins/overview]
- Skills (stand-alone uploads): **Free, Pro, Max, Ultra, Team, Enterprise**. They need **Code execution and file creation** turned on (Settings › Capabilities). [C] [support 12512180; skills/overview]
- "Ultra" is the renamed Max 20x, rolled out from about 2026-10-30. [I] (secondary sources only). It is listed in Anthropic's plugin and skill docs, but the Word page lists only "Pro, Max, Team, and Enterprise". [C] [office-agents/word]

### 1.2 Every install path

| Path | Surface | How | Persists? |
|---|---|---|---|
| Upload plugin zip | claude.ai web, Desktop chat, Cowork | Customize › Plugins › **Add › Upload plugin** (`.zip` or `.plugin`) [C] | Yes, on the account, per org. Shows up in chat, Cowork, Claude Code and the Office add-ins (§5) [C] |
| Add marketplace by URL | same | Customize › Plugins › Add › **Add marketplace** › GitHub/GHE URL, `owner/repo`, or a public GitLab/Bitbucket repo. Private GitHub needs the Claude GitHub App [C] | Yes. Updates via **Check for updates** or **Sync automatically** (github.com) [C] |
| Directory | same | Customize › Plugins › **Discover** › Add [C] | Yes. Auto-updates [C] |
| Upload a single skill zip | same | Customize › Skills › **+ › Create skill › Upload a skill** [C] [support 12512180] | Yes, on the account |
| Synced from account | Claude Code CLI/Desktop Code tab/VS Code | Automatic, as `<name>@synced`, at session start (v2.1.273+); `/reload-plugins` [C] [code.claude.com plugins/install] | One-way, account → machine |
| `claude plugin marketplace add <src>` + `claude plugin install <p>@<mkt>` | Claude Code | Source: GitHub `owner/repo[#ref]`, any git URL, **local directory / marketplace.json path**, or a hosted `marketplace.json` URL. **Not a zip.** Scope: user (default), project or local [C] | Yes, per machine. Not added to the account [C] |
| `claude --plugin-dir <dir-or-.zip>` / `--plugin-url <zip-url>` | Claude Code | **Accepts a `.zip`** [C] [cli-reference] | Session only (`<name>@inline`) [C] |
| `~/.claude/skills/<skill>/` copy | Claude Code | Copy the skill folder [C] [skills/how-to] | Yes, per machine. Cowork does **not** read `~/.claude` [C] [cowork/overview] |

### 1.3 "Someone emailed me a zip": steps [C unless marked]
1. Save the attachment. **Don't unzip and re-zip it.** [I] A re-zip can add a wrapper folder or `__MACOSX/` entries, which trip the "other files beside the plugin folder" error.
2. Claude Desktop or claude.ai › **Customize** (left sidebar) › **Plugins** › **Add** › **Upload plugin** › choose the zip. (In Desktop's Cowork tab, open Cowork first, then Customize.)
3. Turn on Settings › Capabilities › **Code execution and file creation** if it is off. Skills need it.
4. Open the plugin, review its Skills and Connectors tabs, and check the plugin toggle is on.
5. Use it: describe the task, or type `/` and pick `plugin-name:skill`. In Cowork, commands run as `/plugin-name:command`.
6. Claude Code users: start `claude` signed in to the same account. The plugin arrives as `<name>@synced`; run `/reload-plugins`.

### 1.4 Zip format and limits
- **Plugin zip:** the archive holds the plugin folder as its single top-level entry, *or* the folder's contents directly. Upload error text: `plugin.json must be at .claude-plugin/plugin.json at the zip root (or inside a single top-level directory)`. That error fires when the manifest is nested deeper or there are "other files beside the plugin folder". [C] [plugins/build]
- **Top-level `bin/`** makes claude.ai and Cowork refuse the whole plugin. [C] Keep engines under `skills/<x>/scripts/`.
- **Limits:** 200 MB uncompressed and 5,000 files per plugin. **Upload plugin** accepts files up to 200 MB. Up to 25 self-added marketplaces per account per org. Marketplace repo archive up to 512 MB, 500 plugins per marketplace. [C] [platform-support; cowork/guide/plugins]
- **Skill zip (stand-alone):** must contain `<skill-name>/SKILL.md`. A `SKILL.md` at the zip root isn't recognised. `name` must match the folder: lowercase, digits and hyphens, at most 64 characters. `description` up to 1,024 characters. Keep `SKILL.md` under 500 lines. No byte limit is published ("ZIP file exceeds size limits" is a listed failure cause). [C] [skills/how-to; support 12512180]
- Manifest fields every surface reads: `name` (permanent ID), `displayName`, `version`, `description`, `author`, `license`. `claude plugin validate ./dir` checks them. [C]
- Fixed 2026-10-05 in Desktop: `.zip` and `.skill` uploads were keeping only `SKILL.md`. [C] [cowork/changelog] Users on older Desktop builds may still hit this. [I]
- r1's Microsoft-side limits (20 skills, 5 MB per file, 20 companion files) **don't apply on Claude**. No per-skill file cap is published. [I]

### 1.5 Updates for zip installs
- Marketplace or directory plugins update from their source automatically. [C]
- **A self-uploaded zip has no documented update path.** [U] The docs describe no "upload new version" for a personal upload. Expect Remove + re-upload (that re-upload works is [I]). Whether it keeps the toggle and connector state is [U]. Keep `name` stable so it stays the same plugin. [I]
- A Team/Enterprise member's **shared** plugin: recipients "get your new version wherever they use Claude". [C] An org-uploaded plugin: an Owner uses **Upload new version**, with **Version history** and **Revert**. [C]
- Design doc §8.1 says "zip uploads don't auto-update, setup skill checks its version". That holds [I]. A version check that fetches a URL needs network, which the sandbox may not have; prefer telling the user to compare `plugin.json` `version` with the release page.

## 2. Team / Enterprise install

### 2.1 Where and who [C] [plugins/admin]
- **Organization settings › Plugins & skills**, with tabs Inventory, Marketplaces, Organization skills, Requests and Policy. Owners and Primary Owners can use it. On Enterprise, custom roles with **Libraries** can too. The members-list role "Admin" can **not**.
- **Add** menu: Upload a skill · Create a skill · **Upload a plugin (zip)** · Create a plugin · **Sync from GitHub** / **Sync from GitLab** (a marketplace repo) · Add marketplace (Anthropic catalogs).
- **GitHub sync:** reads the default branch through the Claude GitHub App. Repos on github.com or gitlab.com **must be private or internal**. Plugin sources may be `github`, `url`, `git-subdir` or `./relative`. Members don't need repo access. Sync runs on **Re-sync**, or on a push to the default branch when **Sync automatically** is on (webhook). There is no scheduled sync. Synced items can't be reverted in the UI: fix and push. [C] [plugins/org-sync]

### 2.2 Availability levels [C]
| Setting | Member sees | Member can remove |
|---|---|---|
| Not available | hidden; stops loading even if installed | n/a |
| Available to install | under Discover | yes |
| Installed by default | already installed | can turn off |
| Required | always on, "required by your organization" | no |
- Enterprise only: **Group access…** per SCIM or manual group. A group's setting overrides the org's; across several groups the most permissive wins. Team has no groups. **Team pilot:** upload to your own account and **Share** with named people. [C]
- **Availability doesn't add bundled connectors.** An Owner adds them at Organization settings › Connectors, and members then connect with their own account. [C]
- Required plugins also stay on in synced Claude Code sessions. [C]

### 2.3 Member self-service policies [C] [plugins/admin; plugins/share]
- **User-created skills** (Policy tab): when off, members can't upload or create skills, and the plugin options (URL, upload, create) disappear from **Add**.
- **Skill sharing**: members share with named people in the org. It is **on by default on Team** [C] [support 12512180]. Enterprise has **Share with groups**. Recipients get the plugin turned *off* until they turn it on. Later edits propagate. Connector sign-ins aren't shared.
- **Publish to org → Publishing** policy: Requires review / Open / Off. Defaults: **Team = Open**. **Enterprise = Open if security scanning is on, otherwise Requires review.** [C]
- **Skills** master switch (Policy): off disables plugins for everyone, including the Claude Code sync. `syncClaudeAiPlugins:false` in managed settings stops only the Claude Code sync. [C]
- **Custom connectors:** Pro and Max users add their own. On Team, only Owners and Primary Owners. On Enterprise, Owners, Primary Owners and Libraries-manage roles. Members then connect individually. [C] [support 11175166]
- **Code execution** must be on org-wide for skills. Owner: Organization settings › Capabilities [C] [skills/overview]; support 12512180 says Plugins & skills instead (docs disagree [C]). Network-egress defaults for Team are stated inconsistently on one page (off vs package-managers-only). [C] [support 12111783] Check them in the tenant.

### 2.4 Rolling a zip out to 10 lawyers with the least effort (Team) [C/I]
1. An Owner checks **Capabilities › Code execution and file creation** is on, and **Policy › Skills** is on.
2. **Organization settings › Plugins & skills › Add › Upload a plugin**, then pick the emailed zip.
3. Inventory › the plugin's row menu › **Default access › Installed by default** (or **Required**).
4. Done. Each member sees it in Customize › Plugins at the next session, in chat, Cowork, Claude Code (synced) and the Word add-in (§5).
5. Updates: the item's menu › **Upload new version**; Version history › **Revert** if needed.
- **No-Owner alternative:** one lawyer uploads it to their own account and uses **Share** with the other 9. Each must turn it on. Their edits propagate. [C]
- **GitHub alternative (later):** a private repo with `.claude-plugin/marketplace.json` → **Sync from GitHub** with auto-sync. Best when Spaarke issues frequent releases. [C]

## 3. Component support matrix

Sources: [plugins/platform-support], [plugins/overview], [code.claude.com plugins/components], [office-agents/*], [support 15520349, 14479288]. "Chat" covers claude.ai web, the Desktop Chat tab and mobile.

| Component | claude.ai web chat | Desktop chat | Cowork, local session | Cowork, cloud session | Claude Code | Claude for Word / Excel / PPT / Outlook |
|---|---|---|---|---|---|---|
| Skills (`SKILL.md`) | Loads [C] | Loads [C] | Loads [C] | Loads [C] | Loads [C] | Enabled skills, including plugin skills, available [C]; `/` lists the ones relevant to the app [C] |
| Scripts in skills | Run in the code-execution sandbox; the skill folder is copied in; use relative paths [C] | same [C] | Run in the on-device Linux VM [C] | Run in the cloud sandbox [C] (architecture) | Run on the user's machine via the Bash tool (permission prompt) [C] | **[U]** The add-in has code execution with a Claude account [C], and sends "instructions and Markdown files" of a used skill [C]. Script execution isn't stated |
| Commands (`commands/*.md`) | Loads **as a skill** [C] | same [C] | Loads; `/plugin:cmd` [C] | same [C] | Loads [C] | [U]; probably as a skill [I] |
| Agents (`agents/*.md`) | Ignored [C] | Ignored [C] | Loads [C] | Loads [C] | Loads [C] | [I] not loaded |
| Hooks (`hooks/hooks.json`) | Ignored [C] | Ignored [C] | Loads [C]; recipient confirms on share [C] | Loads per matrix [C]; where it runs in cloud isn't stated [U] | Loads [C] | [I] not loaded |
| Remote MCP (`http`/`sse`, fixed URL) | Listed on the plugin's Connectors tab; works once added and connected [C] | same | Loads; connect on the Connectors tab [C] | Loads; calls made server-side [C] | Loads directly [C] | Org-enabled custom connectors available [C]; plugin-bundled ones not specifically documented [U] |
| Local stdio MCP / `.mcpb` | Ignored [C] | Ignored [C] | Loads [C] | **Only through the desktop app while it's open** [C] (15520349); architecture page says "Local MCP servers don't run in sessions in the cloud" [C] | Loads [C] | No [I] |
| MCP with `${user_config.*}` | Ignored if in URL [C] | same | Ignored when an option has no default; no prompt [C] | same | Prompts [C] | n/a |
| `userConfig` options | No [C] | No | No [C] | No | Yes; `/plugin configure` [C] | No |
| Top-level `bin/` | **Refuses plugin** [C] | same | same | same | Loads [C] | n/a |
| LSP, output styles, themes, `settings.json` | Ignored [C] | Ignored | Ignored [C] | Ignored | Loads [C] | n/a |
| Plugin-root `CLAUDE.md` | Not a component [I] | — | — | — | **Not loaded**; validate warns [C] | — |

Notes:
- **Cowork local vs cloud availability** [C] [support 15520349; 14479288; 13455879]: on Pro, Max and Ultra, "new sessions run in the cloud on every surface". Only tasks started on the device before **2026-10-06** stay local, and "Only on your computer" was removed. Team: cloud sessions on by default; an Owner can turn cloud off and keep local desktop Cowork. Enterprise: cloud off by default. Cowork in the cloud is **beta**. "For new tasks that run only on your computer, use Claude Code in the desktop app."
- Agents and hooks "run commands on your computer" in Cowork and Claude Code. [C] [plugins/overview]

## 4. Script runtime

| Surface | Where scripts run | Python | lxml / python-docx | Network | File access |
|---|---|---|---|---|---|
| claude.ai web / Desktop chat | Anthropic "isolated, sandboxed container" [C] [support 12111783] | Not published for claude.ai [U]. The API code-execution tool is **3.11**, Linux x86_64, 5 GiB RAM and disk [C] [platform code-execution-tool] | API sandbox preinstalls **python-docx**, python-pptx, openpyxl, pypdf … [C]. lxml is a python-docx dependency, so present [I]. That claude.ai uses the same image is [I] | Pro/Max: package-manager egress **on**, so `pip install` works [C]. Team/Enterprise: off or package-managers only (docs inconsistent) [C]; Owner sets it at Capabilities [C] | **Uploads only**, max **30 MB per file** up and down [C]. The skill folder is copied into the sandbox [C] |
| Cowork, cloud | Per-session ephemeral cloud sandbox [C] | Not published [U] | Not published [U] | Proxy with allowlist; "No network access is the default for Enterprise" [C]; follows the org's code-execution egress setting [C] | **Connected folders only**, via the open Desktop app; "Claude fetches a copy of just that file"; copies deleted with the session [C]. Claude reads files up to 50 MB [C] (projects page). How writes return to disk isn't documented [U] |
| Cowork, local (Team/Ent; legacy Pro/Max tasks) | Dedicated **Linux VM** on the device (Hyper-V / Apple Virtualization) [C] | Not published [U] | Not published [U] | VM egress filter plus org egress settings; web fetch, search and MCP aren't covered by egress rules [C] | Connected folders, read and write [C] |
| Claude Code | User's machine (Bash tool, permission-gated) [C] | Whatever the user has installed [C]. Windows often has no `python3` [I] | User must `pip install python-docx lxml` [I] | Unrestricted unless the sandbox or managed settings apply [I] | Project dir and allowed paths [C] |
| Claude for Word | Not stated [U] | — | — | Code execution available with a Claude account [C] | Only the **open document**: text, comments, tracked changes, footnotes, tables, bookmarks [C] |

**Can a skill read a folder of .docx on disk?** In Cowork, yes, if the user connects the folder. In cloud sessions this needs the Desktop app open, and files come over as per-file copies. [C] In web chat, no: the user uploads files, 30 MB each. [C] In Claude Code, yes. [C]
**Can the script output a .docx the user can open?** In chat, yes: generated files are downloadable from the conversation. [C] In Cowork, files land in the project's or connected folder. [C] (Cloud write-back mechanics are [U].) In Claude Code, it writes to disk. [C]
**Action for r1 engines:** they were verified on Python 3.12.15 in *Microsoft* Cowork. The Claude-side sandbox may be **3.11** [I], so avoid 3.12-only syntax (PEP 695 generics, PEP 701 f-strings) and test on 3.11. Ship a skill instruction like "if `import docx` fails: `pip install python-docx`" for Claude Code and Pro/Max chat. [I]

## 5. Claude for Word

- **Plans:** generally available to Pro, Max, Team and Enterprise. [C] [office-agents/word]
- **Install, individual:** AppSource "Claude for Microsoft 365" › Get it now › open Word › sign in with the Claude account. [C]
- **Install, org:** M365 Admin Center › Settings › Integrated apps › Add-ins › search "Claude for Microsoft 365" › assign to org, users or groups. This needs "Let users access the Office Store" on; otherwise upload the custom manifest XML (`pivot.claude.ai/manifest-word.xml`). Rollout takes up to 24 h. PIM-activated roles fail (MS issue 11126536). [C]
- **Builds:** Word web; Windows M365 2205+; Mac 16.61+. No iPad or Android, no 2016/2019 perpetual, no `.doc`. [C]
- **Skills and plugins:** "Skills you've enabled in your Claude settings are available in all Claude for M365 add-ins". Invoke with `/`; skills not relevant to the app are filtered. [C] [office-agents/connectors-and-skills] With a Claude sign-in, each chat request includes "the name and description of each plugin the user has turned on, with the names of its skills and connectors". [C] [office-agents/data-storage] So **user- and org-installed third-party plugin skills reach Word.** [C] That they run as reliably as in Cowork is [U], given r1's Microsoft-side finding. Users can also upload skills *inside* the add-in. These are stored per device in browser storage and can be lost after the Sept/Oct 2026 M365 Windows updates; "Save to claude.ai" avoids that. [C]
- **Scripts:** **[U]**. Code execution is available on Claude-account sign-ins [C], but nothing says skill scripts are run. Design Word-facing skills as **instruction-only**, with the playbook in Markdown references. This matches r1's Word approach. [I]
- **Tracked changes and comments:** yes. "Tracked changes mode" makes edits native revisions; it works through comment threads and replies; it summarises counterparty redlines. [C]
- **Connectors:** "+ › Connectors"; "custom connectors your organization has enabled". [C]
- **Per-user instructions:** add-in Settings › Instructions, Word-only. Memory is stored locally (`claude-memory`) when on. [C]
- **Data:** inputs and outputs deleted within 30 days. Custom retention isn't inherited. Not in audit logs; in the Compliance API for Enterprise. Chat history is in local IndexedDB (last 50 per app). Attached files expire after 28 days. [C] The design doc §4 and §7.3 claims are **confirmed**.

## 6. Per-user and per-team configuration (practice profile)

| Mechanism | Surfaces | Read fresh each run? | Team-shareable? | Notes |
|---|---|---|---|---|
| File in a **connected Cowork folder / Cowork project folder** (e.g. `Legal Knowledge/Profile/legal-profile.md`) | Cowork (cloud needs the Desktop app open) [C] | Yes, the skill reads it [I] | Yes, if the folder is a synced OneDrive or SharePoint folder each user connects [I] | Closest to r1's T-06 pattern. Cowork projects live on the computer and aren't shareable [C] |
| **claude.ai Project** knowledge + instructions | Chat (web, Desktop); can be linked into a Cowork project [C] | Always loaded in that project's chats [C] | **Yes, on Team and Enterprise** [C] | Good team "firm profile"; per-user overrides go in a personal project [I] |
| **Personal preferences / instructions** (account) | Chat [C] | Always loaded [C] | No | Short text only [I] |
| **Memory** | Chat; Cowork project memory [C]; Word add-in local memory [C] | Model-managed | No | Not deterministic; don't use as the profile store [I] |
| Word add-in **Instructions** | Word only [C] | Every Word conversation [C] | No | Paste a short profile summary [I] |
| Plugin **`userConfig`** | Claude Code only [C] | Yes; `${user_config.*}` / env [C] | No | Not available in chat or Cowork [C] |
| **`CLAUDE.md`** (`~/.claude/CLAUDE.md` or project) | Claude Code only [C] | Yes [I] | Via repo [I] | Plugin-root CLAUDE.md is ignored [C] |
| `${CLAUDE_PLUGIN_DATA}` | Claude Code only [C] | Yes | No | Survives updates [C] |
| Reference file **inside the plugin** (e.g. `firm-profile.md`) | All | Yes, read-only | Yes, via the org plugin (Owner uploads a new version) [C] | Team defaults. A skill can't persist user edits back into the plugin [I] |

**Recommendation for v1 [I]:** a two-layer profile.
1. **Team defaults** go in the plugin as a reference file, or in a shared Team project.
2. **The user's own profile** goes in `legal-profile.md` in a folder they connect to Cowork, or in a claude.ai Project for chat-only users.

The setup skill writes the user file in Cowork. In chat, it produces the file for the user to download and add to a Project. Same pattern as r1 T-06.

## 7. Data terms, one line per plan (for the README notice)

- **Free, Pro, Max (and Ultra, inferred):** Anthropic's **consumer terms**. Chats may be used for model training unless you turn the setting off. If it's on, data is kept up to **5 years**; if off, **30 days**. Claude Code used from these accounts follows the same terms. [C] [anthropic.com/news/updates-to-our-consumer-terms, 2025-08-28; privacy 10023548, updated 2026-07-01] Ultra isn't named in either source. [I]
- **Team, Enterprise (Claude for Work), API:** **Commercial Terms**. No training on your data, and org retention settings apply. [C] (Cloud Cowork data "isn't used to train Claude" [C] [14479288].)
- **Claude for Word/Excel/PPT/Outlook, any plan:** inputs and outputs are deleted on the backend within 30 days. Org custom retention is **not** inherited. [C]
- Suggested README sentence: "If you use a personal Claude Free, Pro, Max or Ultra plan, Anthropic's consumer terms apply. Turn off 'Help improve Claude' before using client material, or use a Team or Enterprise plan." [I]

## 8. Design-doc claims checked (§4, §8)

| Claim | Verdict |
|---|---|
| §4 / §7.2 "Cowork (local) … Primary surface" | **Outdated.** Pro, Max and Ultra are cloud-only for new tasks since 2026-10-06. Team defaults to cloud on; Enterprise to cloud off. [C] |
| §4 Word row (30 days, no custom retention, not in audit logs, Compliance API, browser storage) | Confirmed [C] |
| §8 Mode B: upload or URL only if "User-created skills" is on | Confirmed [C] |
| §8 Mode B: members can't add custom connectors on Team/Enterprise | Confirmed [C] |
| §8 Mode C: Pro/Max add custom remote connectors themselves | Confirmed [C] |
| §8.1 plugins saved to the account for the org | Confirmed [C] |
| §8.1 Team pilot / Publish to org / policy values | Confirmed. Defaults: Team Open; Enterprise Open with scanning, else Requires review [C] |
| §8.1 "Zip uploads don't auto-update" | Consistent with the docs (no update path documented) [I] |
| §8.2 AppSource self-install; "Let users access the Office Store" | Confirmed [C] |
| §8.2 Word gets skills from installed plugins and skills | Confirmed for availability [C]; script execution [U] |
| §8.3 consumer terms 5 years / 30 days; Ultra unconfirmed | Confirmed; Ultra still not named [C/I] |
| §9.5 guardrail hooks "apply in all modes" | **Not possible.** Hooks are ignored in chat and (inferred) in Word [C/I] |
| research.md §3.3 "A Claude plugin has no CLAUDE.md component" | Confirmed [C] |

## 9. "Email a zip" recipe

### What the emailed zip must be
**Make the attachment itself a valid plugin zip.** Then the same file uploads unchanged everywhere: Customize, the org admin, and `claude --plugin-dir`.

```text
spaarke-legal-v1.0.0.zip
└── spaarke-legal/                     # single top-level folder (no __MACOSX, nothing beside it)
    ├── .claude-plugin/plugin.json     # name (permanent), displayName, version, description, author, license
    ├── README.md                      # START HERE: install steps per plan + data-terms notice (§7)
    ├── LICENSE
    ├── skills/
    │   ├── spaarke-start/SKILL.md     # setup interview → writes/downloads legal-profile.md; plan/terms notice
    │   ├── spaarke-nda-review/
    │   │   ├── SKILL.md               # ${CLAUDE_SKILL_DIR}/scripts/... AND relative scripts/... (chat)
    │   │   ├── references/            # playbook .md (Word path works from these alone)
    │   │   └── scripts/               # Python engines (3.11-safe, stdlib + python-docx/lxml), no network
    │   └── …
    └── assets/legal-profile.template.md
    # NO top-level bin/; NO .mcp.json in v1 (no connector to add); commands/agents/hooks optional, Cowork/Code only
```
- Build it with the build script, not Finder or Explorer re-zips. Gate it: `claude plugin validate`, 200 MB / 5,000 files, no `bin/`, a single top-level entry. [C/I]
- Email risk: many mail gateways quarantine zips that contain `.py`. Also publish the same file at a download URL (which also enables `claude --plugin-url`). [I]

### (a) Individual (Pro / Max / Ultra)
1. Turn on Settings › Capabilities › Code execution and file creation.
2. Customize › Plugins › Add › **Upload plugin** › the zip.
3. Optional, for local files: in Claude Desktop, connect a folder (e.g. `Documents/Legal Knowledge`) and keep the app open.
4. Run `/spaarke-legal:spaarke-start`. It shows the data notice and builds `legal-profile.md` in that folder (Cowork) or as a download to add to a Project (chat).
5. Word: install "Claude for Microsoft 365" from AppSource and sign in. Plugin skills appear under `/`.
6. Claude Code (optional): it syncs automatically. Or run `claude --plugin-dir spaarke-legal-v1.0.0.zip`, and `pip install python-docx` if needed.
7. Update: remove the plugin and upload the new zip (procedure [U]).

### (b) 10-person Team
1. **Owner:** check Capabilities › Code execution is on, and Policy › Skills is on.
2. **Owner:** Organization settings › Plugins & skills › Add › **Upload a plugin** › the zip, then Default access › **Installed by default**.
3. **Owner, Word:** M365 Admin Center › Integrated apps › deploy "Claude for Microsoft 365" to the 10 users. Or let users self-install from AppSource.
4. **Each lawyer:** opens Claude. The plugin is already there. Run `spaarke-start` to create their own profile file. Team defaults come from the plugin's reference file, or a shared claude.ai Project the Owner creates.
5. **Updates:** Owner › item menu › **Upload new version** (Revert is available).
- **Fallback if no Owner is involved:** one lawyer uploads it and uses **Share** with 9 colleagues (sharing is on by default on Team). Each turns it on.

---

## Sources (fetched 2026-11-07; most authoritative first)
- https://claude.com/docs/plugins/platform-support : component × surface matrix, limits (**primary**)
- https://claude.com/docs/plugins/admin : org availability, policies, groups, pilot (**primary**)
- https://claude.com/docs/plugins/build : zip layout, upload error text, testing
- https://claude.com/docs/plugins/overview : install paths, bundled connectors, updates
- https://claude.com/docs/plugins/share : share, publish to org, plugin file to other orgs
- https://claude.com/docs/plugins/org-sync : GitHub/GitLab sync rules, no `bin/`
- https://claude.com/docs/skills/overview · https://claude.com/docs/skills/how-to : skills, code-execution prerequisite, scripts, `${CLAUDE_SKILL_DIR}`, skill zip
- https://claude.com/docs/cowork/overview · /cowork/guide/plugins · /cowork/guide/projects · /cowork/changelog : Cowork plugin limits, projects, Oct 2026 changes
- https://support.claude.com/en/articles/14479288-claude-cowork-architecture-overview ("Updated this week") : cloud vs local, VM, egress
- https://support.claude.com/en/articles/15520349-use-claude-cowork-on-web-desktop-and-mobile : Pro/Max cloud-only since 2026-10-06
- https://support.claude.com/en/articles/13455879-use-claude-cowork-on-team-and-enterprise-plans : Team/Enterprise defaults
- https://support.claude.com/en/articles/13837440-use-plugins-in-claude · /12512180-use-skills-in-claude · /12111783-create-and-edit-files-with-claude ("Updated yesterday") · /11175166-get-started-with-custom-connectors-using-remote-mcp
- https://claude.com/docs/office-agents/word · /connectors-and-skills · /data-storage · /third-party-platforms · /enterprise-readiness : Word add-in
- https://code.claude.com/docs/en/plugins/install · /plugins/components · /plugins/cli-reference : Claude Code install, userConfig, `--plugin-dir` zip
- https://platform.claude.com/docs/en/agents-and-tools/tool-use/code-execution-tool : API sandbox spec (Python 3.11, packages)
- https://www.anthropic.com/news/updates-to-our-consumer-terms (2025-08-28) · https://privacy.claude.com/en/articles/10023548 (updated 2026-07-01)

## Caveats and live tests needed
1. **[U] Word + scripts:** does a plugin skill with `scripts/` run in Claude for Word on a Claude-account sign-in?
2. **[U] Cloud Cowork + local folder + Python engine:** can a script read N `.docx` copies and write a `.docx` back into the connected folder? Record the Python version and whether `import docx, lxml` works.
3. **[U] Chat sandbox Python version and packages** on claude.ai (vs the API's 3.11 spec). Also: does `pip install` work on Team with default egress?
4. **[U] Re-upload of a self-uploaded plugin:** does it update in place or duplicate, and does it keep toggle and connector state?
5. Unified Chat + Cowork in Desktop takes effect on managed devices from 2026-11-10 (MDM `desktopHome`), and is rolling out to Pro, Max and Ultra. Surfaces may shift again. Re-check before the design is frozen.
6. Help Center pages are inconsistent on Team egress defaults and on where Owners enable code execution.
