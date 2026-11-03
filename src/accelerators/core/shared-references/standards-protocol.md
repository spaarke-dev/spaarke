# Standards protocol: which positions apply

Look for standards **before** reading the agreement in detail, and say what you used.

## Order of precedence

1. **A playbook the user names or attaches in this conversation.**
2. **The user's saved playbook** for this agreement type and side. Look in the user's OneDrive,
   in this order, and list what you find:
   - `Documents/Cowork/Playbooks/<practice>/` (for example `Documents/Cowork/Playbooks/nda/`);
   - the folder named in the attorney profile (`Documents/Cowork/Profile/legal-profile.md`,
     line `Playbook folder:`).
   A playbook file is Markdown in our format (`references/playbook-format.md`) or a `.docx` Word
   playbook. Prefer Markdown: it carries topic ids.
3. **The Spaarke baseline** shipped in `references/` (for the NDA: `baseline-mutual.md`).

More than one candidate at the same level: ask which one (one question card). None at levels 1–2:
use the baseline and say so.

## Validate before use

Run `python scripts/playbook_parse.py <playbook.md> --topics references/topics.json --side <side>`.

- Exit 3 (format violation): show the listed errors, do not guess. Offer to use the baseline instead.
- `silent_topics`: for each, apply the baseline position and mark the topic "playbook silent;
  baseline applied" in the review result.
- A `.docx` playbook cannot be validated by script in this version: read it, map each position to
  a topic id from `references/topics.json`, and say "Word playbook mapped by the model; not
  validated".

## Provenance line (mandatory)

Every deliverable states, on page 1 and in chat, exactly one of:

- `Standards: <playbook title> v<version> (customer playbook)`
- `Standards: Spaarke baseline positions (not tailored) v<version>`
- `Standards: Spaarke baseline positions (not tailored) v<version>, applied to a <side> review` when
  the baseline's side differs from the review side

followed by `· Skill: <name> v<version>`.
