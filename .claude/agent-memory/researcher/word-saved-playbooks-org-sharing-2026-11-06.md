---
name: word-saved-playbooks-org-sharing-2026-11-06
description: Word Copilot legal "saved playbook" storage/selection/sharing - Microsoft docs (as of 2026-11-06) still document ONLY per-review .docx attach; Frontier-era Legal Agent docs said playbook converted to a saved skill; no org/admin playbook library, no sharing, no Create-playbook format rules documented; contract-review-only scope
metadata:
  type: reference
---

## 2026-11-06: Where Word saves playbooks, how they are picked, org-wide sharing

**Question**: Storage/selection/sharing/import rules/scope of playbooks in Word's Copilot legal experience (Create playbook, saved cards) and Legal for Cowork.

**Findings**: Current Microsoft docs (support "Get started with Copilot for legal"; transparency note; Learn manage-copilot-for-legal ms.date 2026-10-13/upd 2026-10-31; RM571884 v3 2026-10-20; launch blog 4564246) document ONLY: attach a `.docx` per review (Upload file / Attach from Cloud) -> topics+rules -> Run Review; "can currently only perform contract reviews from playbooks"; transparency note lists "Draft precedent playbook. Create a playbook from a set of precedents." Nothing documents a saved-playbook list, its storage location (no hit for Documents/Copilot/Created anywhere), snapshot-vs-link, sharing, an admin/org library, import-vs-synthesise rules, or a column format. Only documented persistence ever: the Frontier-era Legal Agent page (pre-release, Word 2604+) said the agent converts the playbook "into a skill" and "will save the skill, including any changes you've made, for later" (seen via search snippet of the older page; current page dropped it); Reddit r/legaltech 2026-05-04 reported re-upload needed and an empty picker then. So the saved-card behaviour the owner observes is undocumented/newer than the docs. Personal Office skills elsewhere (PowerPoint, MC1434580) are OneDrive-stored and "personal, visible only to the user who uploads them" (no sharing) - the closest documented analogue; Cowork skills use OneDrive/Documents/Cowork/skills with share Only you / Specific users.

**Sources**: support.microsoft.com/en-us/word/get-started-with-copilot-for-legal; .../copilot-for-legal-transparency-documentation; learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-copilot-for-legal; mc.merill.net/message/RM571884, MC1434580; techcommunity 4564246, 4516218; support.microsoft.com/en-us/office/get-started-with-the-legal-agent-frontier-c72cc686-...; eva.mino.law/sources/73b02205-... (Reddit 2026-05-04); third-party Oct 2026 articles (fyve.co.jp, aiswitch.co.kr, azurebrasil.cloud, note.com) all silent on saving/sharing.

**Open questions (hands-on)**: storage object (OneDrive file vs Graph/skill store), snapshot vs link, picker UX and `/` reference, share/admin publish, the exact import-vs-synthesise heuristic, non-contract playbooks. Related: [[word-copilot-pane-plugins-playbooks-addin-2026-10-31]], [[word-vs-cowork-legal-boundary-and-onboarding-2026-10-31]].
