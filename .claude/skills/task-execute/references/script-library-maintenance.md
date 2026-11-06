# Script library maintenance — Step 10.5 procedure

Read this at Step 10.5 when the task used an existing script, changed one, repeated a command sequence 3+ times that could become a script, or made a script's behaviour obsolete. When you finish, return to `SKILL.md` Step 10.6.

```
AFTER task completion, EVALUATE script library updates:

IF task USED existing scripts:
  VERIFY scripts still work as expected
  IF script needed modifications:
    UPDATE script file
    UPDATE scripts/README.md (Last Used, any behavior changes)

IF task CREATED reusable automation (commands used 3+ times):
  EVALUATE: Should this become a script?

  IF yes (repeatable, complex, multi-step):
    1. CREATE new script following naming convention
    2. ADD inline documentation (synopsis, parameters, examples)
    3. ADD entry to scripts/README.md with:
       - Purpose, Usage frequency, Lifecycle status
       - Dependencies, When to use, Command example
    4. Place in: scripts/ (general) or projects/{name}/scripts/ (project-specific)

IF task DEPRECATED script functionality:
  UPDATE scripts/README.md to mark as ⚠️ Deprecated
  NOTE replacement approach
```

A new script is new surface: answer the three component-justification questions (root CLAUDE.md §11) in the task notes before adding it.
