# Legal playbook format (Markdown)

One file per agreement type and side. `playbook_parse.py` enforces this format; anything it
rejects must be fixed, not guessed around.

```markdown
# Playbook: NDA - Disclosing Party
Version: 1.0 | Owner: <name> | Effective: 2026-11-01 | Jurisdiction default: Florida | Side: disclosing | Status: <text>

Optional introduction text.

## topic: nda.definition-unmarked
Preferred: <the position we want>
Fallback: <what we accept if pushed>
Prohibited / escalate: <what needs a senior attorney>
Model language: "<clause text to insert>"
Rationale: <why>
Priority: High
```

| Rule | |
|---|---|
| Title | `# Playbook: <name>` |
| Header | `Version` and `Side` required; `Side` is `mutual`, `disclosing` or `receiving`. Labels: Version, Owner, Effective, Jurisdiction default, Side, Status |
| Topics | `## topic: <id>`; the id must be in `topics.json`; each id once |
| Fields | Exactly: Preferred (required), Fallback, Prohibited / escalate, If missing, Model language, Rationale, Priority (required). "If missing" (optional) says what to do when the document has no clause on the topic; give Model language with it |
| Priority | High, Medium or Low |
| Empty field | Write `n/a` |
| Continuation | A field continues on the next lines until a blank line |

Topics in the catalogue that the playbook omits are **silent**: the skill applies the baseline for
them and says so.

## Where playbooks live

`Documents/Legal Knowledge/<Type>/Playbook master/<name>.md` in the user's OneDrive (Type: `NDA`, `Legal Hold Notice`). The Word legal plugin
uses a rendered `.docx` copy of the same playbook, attached to each review.
