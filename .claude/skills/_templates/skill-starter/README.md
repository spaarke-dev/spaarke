# Skill Starter

This is a template skill folder. Copy this entire folder to create a new skill:

```bash
# PowerShell
Copy-Item -Path ".claude/skills/_templates/skill-starter" -Destination ".claude/skills/[new-skill-name]" -Recurse

# Bash
cp -r .claude/skills/_templates/skill-starter .claude/skills/[new-skill-name]
```

## Folder Structure

```
[skill-name]/
├── SKILL.md          # Core instructions (edit from template)
├── scripts/          # Automation scripts (optional)
│   └── .gitkeep
├── references/       # Documentation loaded into context (optional)
│   └── .gitkeep
└── assets/           # Templates and static files (optional)
    └── .gitkeep
```

## After Copying

1. Rename this folder to your skill name (lowercase, hyphenated)
2. Edit `SKILL.md` using the template in `../_templates/SKILL-TEMPLATE.md`
3. Add scripts, references, or assets as needed
4. Delete this README.md file
5. Remove `.gitkeep` files from folders that have content
6. Test the skill with representative requests
