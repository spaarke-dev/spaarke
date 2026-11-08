# Draft PR template (project-pipeline Step 4, optional)

Title: `feat({scope}): {project-name}`

Body (copy, fill the braces, and end with the PR attribution lines the session requires):

```markdown
## Summary
Implementation of {project-name}

## Status
- [x] Project initialized
- [x] Tasks created ({X} tasks)
- [ ] Implementation in progress
- [ ] Code review
- [ ] Ready for merge

## Quick Links
- [Project README](projects/{project-name}/README.md)
- [Implementation Plan](projects/{project-name}/plan.md)
- [Task Index](projects/{project-name}/tasks/TASK-INDEX.md)
```

Command: `gh pr create --draft --title "feat({scope}): {project-name}" --body-file <file>`

Why open one at this point: all artifacts and tasks exist, so the first commit is meaningful; the draft PR gives the team visibility while work stays isolated from master until it merges cleanly.
