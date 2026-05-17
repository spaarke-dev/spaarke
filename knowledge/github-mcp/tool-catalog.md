# GitHub MCP Server — Tool Catalog

**Source:** `github/github-mcp-server` `README.md` (sections: "Available Toolsets", "Additional Toolsets in Remote GitHub MCP Server", "Tools", "Additional Tools in Remote GitHub MCP Server")
**Commit:** `39d86b80af711a3277ffab08fa7d3068b3652913`
**Captured:** 2026-05-14

This is a verbatim snapshot of the auto-generated tool catalog from the upstream README. It lists every toolset (logical grouping) and every individual tool exposed by the GitHub MCP server, with each tool's required OAuth scopes and input parameters.

Use this as the authoritative inventory when:
- Selecting a `--tools` allow-list for a Spaarke MCP host config
- Choosing toolsets for `--toolsets` / `GITHUB_TOOLSETS`
- Verifying which scopes a PAT needs for a given operation

For configuration recipes (read-only, exclude-tools, dynamic discovery, lockdown, scope filtering behavior), see [`docs/server-configuration.md`](docs/server-configuration.md) and [`docs/scope-filtering.md`](docs/scope-filtering.md) in this folder.

---

### Available Toolsets

The following sets of tools are available:

<!-- START AUTOMATED TOOLSETS -->
|     | Toolset                 | Description                                                   |
| --- | ----------------------- | ------------------------------------------------------------- |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/person-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/person-light.png"><img src="pkg/octicons/icons/person-light.png" width="20" height="20" alt="person"></picture> | `context`               | **Strongly recommended**: Tools that provide context about the current user and GitHub context you are operating in |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/workflow-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/workflow-light.png"><img src="pkg/octicons/icons/workflow-light.png" width="20" height="20" alt="workflow"></picture> | `actions` | GitHub Actions workflows and CI/CD operations |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/codescan-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/codescan-light.png"><img src="pkg/octicons/icons/codescan-light.png" width="20" height="20" alt="codescan"></picture> | `code_security` | Code security related tools, such as GitHub Code Scanning |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/copilot-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/copilot-light.png"><img src="pkg/octicons/icons/copilot-light.png" width="20" height="20" alt="copilot"></picture> | `copilot` | Copilot related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/dependabot-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/dependabot-light.png"><img src="pkg/octicons/icons/dependabot-light.png" width="20" height="20" alt="dependabot"></picture> | `dependabot` | Dependabot tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/comment-discussion-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/comment-discussion-light.png"><img src="pkg/octicons/icons/comment-discussion-light.png" width="20" height="20" alt="comment-discussion"></picture> | `discussions` | GitHub Discussions related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/logo-gist-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/logo-gist-light.png"><img src="pkg/octicons/icons/logo-gist-light.png" width="20" height="20" alt="logo-gist"></picture> | `gists` | GitHub Gist related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/git-branch-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/git-branch-light.png"><img src="pkg/octicons/icons/git-branch-light.png" width="20" height="20" alt="git-branch"></picture> | `git` | GitHub Git API related tools for low-level Git operations |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/issue-opened-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/issue-opened-light.png"><img src="pkg/octicons/icons/issue-opened-light.png" width="20" height="20" alt="issue-opened"></picture> | `issues` | GitHub Issues related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/tag-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/tag-light.png"><img src="pkg/octicons/icons/tag-light.png" width="20" height="20" alt="tag"></picture> | `labels` | GitHub Labels related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/bell-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/bell-light.png"><img src="pkg/octicons/icons/bell-light.png" width="20" height="20" alt="bell"></picture> | `notifications` | GitHub Notifications related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/organization-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/organization-light.png"><img src="pkg/octicons/icons/organization-light.png" width="20" height="20" alt="organization"></picture> | `orgs` | GitHub Organization related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/project-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/project-light.png"><img src="pkg/octicons/icons/project-light.png" width="20" height="20" alt="project"></picture> | `projects` | GitHub Projects related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/git-pull-request-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/git-pull-request-light.png"><img src="pkg/octicons/icons/git-pull-request-light.png" width="20" height="20" alt="git-pull-request"></picture> | `pull_requests` | GitHub Pull Request related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/repo-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/repo-light.png"><img src="pkg/octicons/icons/repo-light.png" width="20" height="20" alt="repo"></picture> | `repos` | GitHub Repository related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/shield-lock-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/shield-lock-light.png"><img src="pkg/octicons/icons/shield-lock-light.png" width="20" height="20" alt="shield-lock"></picture> | `secret_protection` | Secret protection related tools, such as GitHub Secret Scanning |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/shield-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/shield-light.png"><img src="pkg/octicons/icons/shield-light.png" width="20" height="20" alt="shield"></picture> | `security_advisories` | Security advisories related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/star-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/star-light.png"><img src="pkg/octicons/icons/star-light.png" width="20" height="20" alt="star"></picture> | `stargazers` | GitHub Stargazers related tools |
| <picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/people-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/people-light.png"><img src="pkg/octicons/icons/people-light.png" width="20" height="20" alt="people"></picture> | `users` | GitHub User related tools |
<!-- END AUTOMATED TOOLSETS -->

### Additional Toolsets in Remote GitHub MCP Server

| Toolset                 | Description                                                   |
| ----------------------- | ------------------------------------------------------------- |
| `copilot` | Copilot related tools (e.g. Copilot Coding Agent) |
| `copilot_spaces` | Copilot Spaces related tools |
| `github_support_docs_search` | Search docs to answer GitHub product and support questions |

## Tools

<!-- START AUTOMATED TOOLS -->
<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/workflow-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/workflow-light.png"><img src="pkg/octicons/icons/workflow-light.png" width="20" height="20" alt="workflow"></picture> Actions</summary>

- **actions_get** - Get details of GitHub Actions resources (workflows, workflow runs, jobs, and artifacts)
  - **Required OAuth Scopes**: `repo`
  - `method`: The method to execute (string, required)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)
  - `resource_id`: The unique identifier of the resource. This will vary based on the "method" provided, so ensure you provide the correct ID:
    - Provide a workflow ID or workflow file name (e.g. ci.yaml) for 'get_workflow' method.
    - Provide a workflow run ID for 'get_workflow_run', 'get_workflow_run_usage', and 'get_workflow_run_logs_url' methods.
    - Provide an artifact ID for 'download_workflow_run_artifact' method.
    - Provide a job ID for 'get_workflow_job' method.
     (string, required)

- **actions_list** - List GitHub Actions workflows in a repository
  - **Required OAuth Scopes**: `repo`
  - `method`: The action to perform (string, required)
  - `owner`: Repository owner (string, required)
  - `page`: Page number for pagination (default: 1) (number, optional)
  - `per_page`: Results per page for pagination (default: 30, max: 100) (number, optional)
  - `repo`: Repository name (string, required)
  - `resource_id`: The unique identifier of the resource. This will vary based on the "method" provided, so ensure you provide the correct ID:
    - Do not provide any resource ID for 'list_workflows' method.
    - Provide a workflow ID or workflow file name (e.g. ci.yaml) for 'list_workflow_runs' method, or omit to list all workflow runs in the repository.
    - Provide a workflow run ID for 'list_workflow_jobs' and 'list_workflow_run_artifacts' methods.
     (string, optional)
  - `workflow_jobs_filter`: Filters for workflow jobs. **ONLY** used when method is 'list_workflow_jobs' (object, optional)
  - `workflow_runs_filter`: Filters for workflow runs. **ONLY** used when method is 'list_workflow_runs' (object, optional)

- **actions_run_trigger** - Trigger GitHub Actions workflow actions
  - **Required OAuth Scopes**: `repo`
  - `inputs`: Inputs the workflow accepts. Only used for 'run_workflow' method. (object, optional)
  - `method`: The method to execute (string, required)
  - `owner`: Repository owner (string, required)
  - `ref`: The git reference for the workflow. The reference can be a branch or tag name. Required for 'run_workflow' method. (string, optional)
  - `repo`: Repository name (string, required)
  - `run_id`: The ID of the workflow run. Required for all methods except 'run_workflow'. (number, optional)
  - `workflow_id`: The workflow ID (numeric) or workflow file name (e.g., main.yml, ci.yaml). Required for 'run_workflow' method. (string, optional)

- **get_job_logs** - Get GitHub Actions workflow job logs
  - **Required OAuth Scopes**: `repo`
  - `failed_only`: When true, gets logs for all failed jobs in the workflow run specified by run_id. Requires run_id to be provided. (boolean, optional)
  - `job_id`: The unique identifier of the workflow job. Required when getting logs for a single job. (number, optional)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)
  - `return_content`: Returns actual log content instead of URLs (boolean, optional)
  - `run_id`: The unique identifier of the workflow run. Required when failed_only is true to get logs for all failed jobs in the run. (number, optional)
  - `tail_lines`: Number of lines to return from the end of the log (number, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/codescan-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/codescan-light.png"><img src="pkg/octicons/icons/codescan-light.png" width="20" height="20" alt="codescan"></picture> Code Security</summary>

- **get_code_scanning_alert** - Get code scanning alert
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `alertNumber`: The number of the alert. (number, required)
  - `owner`: The owner of the repository. (string, required)
  - `repo`: The name of the repository. (string, required)

- **list_code_scanning_alerts** - List code scanning alerts
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `owner`: The owner of the repository. (string, required)
  - `ref`: The Git reference for the results you want to list. (string, optional)
  - `repo`: The name of the repository. (string, required)
  - `severity`: Filter code scanning alerts by severity (string, optional)
  - `state`: Filter code scanning alerts by state. Defaults to open (string, optional)
  - `tool_name`: The name of the tool used for code scanning. (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/person-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/person-light.png"><img src="pkg/octicons/icons/person-light.png" width="20" height="20" alt="person"></picture> Context</summary>

- **get_me** - Get my user profile
  - No parameters required

- **get_team_members** - Get team members
  - **Required OAuth Scopes**: `read:org`
  - **Accepted OAuth Scopes**: `admin:org`, `read:org`, `write:org`
  - `org`: Organization login (owner) that contains the team. (string, required)
  - `team_slug`: Team slug (string, required)

- **get_teams** - Get teams
  - **Required OAuth Scopes**: `read:org`
  - **Accepted OAuth Scopes**: `admin:org`, `read:org`, `write:org`
  - `user`: Username to get teams for. If not provided, uses the authenticated user. (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/copilot-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/copilot-light.png"><img src="pkg/octicons/icons/copilot-light.png" width="20" height="20" alt="copilot"></picture> Copilot</summary>

- **assign_copilot_to_issue** - Assign Copilot to issue
  - **Required OAuth Scopes**: `repo`
  - `base_ref`: Git reference (e.g., branch) that the agent will start its work from. If not specified, defaults to the repository's default branch (string, optional)
  - `custom_instructions`: Optional custom instructions to guide the agent beyond the issue body. Use this to provide additional context, constraints, or guidance that is not captured in the issue description (string, optional)
  - `issue_number`: Issue number (number, required)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

- **request_copilot_review** - Request Copilot review
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `pullNumber`: Pull request number (number, required)
  - `repo`: Repository name (string, required)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/dependabot-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/dependabot-light.png"><img src="pkg/octicons/icons/dependabot-light.png" width="20" height="20" alt="dependabot"></picture> Dependabot</summary>

- **get_dependabot_alert** - Get dependabot alert
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `alertNumber`: The number of the alert. (number, required)
  - `owner`: The owner of the repository. (string, required)
  - `repo`: The name of the repository. (string, required)

- **list_dependabot_alerts** - List dependabot alerts
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `owner`: The owner of the repository. (string, required)
  - `repo`: The name of the repository. (string, required)
  - `severity`: Filter dependabot alerts by severity (string, optional)
  - `state`: Filter dependabot alerts by state. Defaults to open (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/comment-discussion-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/comment-discussion-light.png"><img src="pkg/octicons/icons/comment-discussion-light.png" width="20" height="20" alt="comment-discussion"></picture> Discussions</summary>

- **get_discussion** - Get discussion
  - **Required OAuth Scopes**: `repo`
  - `discussionNumber`: Discussion Number (number, required)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

- **get_discussion_comments** - Get discussion comments
  - **Required OAuth Scopes**: `repo`
  - `after`: Cursor for pagination. Use the endCursor from the previous page's PageInfo for GraphQL APIs. (string, optional)
  - `discussionNumber`: Discussion Number (number, required)
  - `owner`: Repository owner (string, required)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name (string, required)

- **list_discussion_categories** - List discussion categories
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name. If not provided, discussion categories will be queried at the organisation level. (string, optional)

- **list_discussions** - List discussions
  - **Required OAuth Scopes**: `repo`
  - `after`: Cursor for pagination. Use the endCursor from the previous page's PageInfo for GraphQL APIs. (string, optional)
  - `category`: Optional filter by discussion category ID. If provided, only discussions with this category are listed. (string, optional)
  - `direction`: Order direction. (string, optional)
  - `orderBy`: Order discussions by field. If provided, the 'direction' also needs to be provided. (string, optional)
  - `owner`: Repository owner (string, required)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name. If not provided, discussions will be queried at the organisation level. (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/logo-gist-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/logo-gist-light.png"><img src="pkg/octicons/icons/logo-gist-light.png" width="20" height="20" alt="logo-gist"></picture> Gists</summary>

- **create_gist** - Create Gist
  - **Required OAuth Scopes**: `gist`
  - `content`: Content for simple single-file gist creation (string, required)
  - `description`: Description of the gist (string, optional)
  - `filename`: Filename for simple single-file gist creation (string, required)
  - `public`: Whether the gist is public (boolean, optional)

- **get_gist** - Get Gist Content
  - `gist_id`: The ID of the gist (string, required)

- **list_gists** - List Gists
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `since`: Only gists updated after this time (ISO 8601 timestamp) (string, optional)
  - `username`: GitHub username (omit for authenticated user's gists) (string, optional)

- **update_gist** - Update Gist
  - **Required OAuth Scopes**: `gist`
  - `content`: Content for the file (string, required)
  - `description`: Updated description of the gist (string, optional)
  - `filename`: Filename to update or create (string, required)
  - `gist_id`: ID of the gist to update (string, required)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/git-branch-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/git-branch-light.png"><img src="pkg/octicons/icons/git-branch-light.png" width="20" height="20" alt="git-branch"></picture> Git</summary>

- **get_repository_tree** - Get repository tree
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (username or organization) (string, required)
  - `path_filter`: Optional path prefix to filter the tree results (e.g., 'src/' to only show files in the src directory) (string, optional)
  - `recursive`: Setting this parameter to true returns the objects or subtrees referenced by the tree. Default is false (boolean, optional)
  - `repo`: Repository name (string, required)
  - `tree_sha`: The SHA1 value or ref (branch or tag) name of the tree. Defaults to the repository's default branch (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/issue-opened-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/issue-opened-light.png"><img src="pkg/octicons/icons/issue-opened-light.png" width="20" height="20" alt="issue-opened"></picture> Issues</summary>

- **add_issue_comment** - Add comment to issue
  - **Required OAuth Scopes**: `repo`
  - `body`: Comment content (string, required)
  - `issue_number`: Issue number to comment on (number, required)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

- **get_label** - Get a specific label from a repository.
  - **Required OAuth Scopes**: `repo`
  - `name`: Label name. (string, required)
  - `owner`: Repository owner (username or organization name) (string, required)
  - `repo`: Repository name (string, required)

- **issue_read** - Get issue details
  - **Required OAuth Scopes**: `repo`
  - `issue_number`: The number of the issue (number, required)
  - `method`: The read operation to perform on a single issue.
    Options are:
    1. get - Get details of a specific issue.
    2. get_comments - Get issue comments.
    3. get_sub_issues - Get sub-issues of the issue.
    4. get_labels - Get labels assigned to the issue.
     (string, required)
  - `owner`: The owner of the repository (string, required)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: The name of the repository (string, required)

- **issue_write** - Create or update issue.
  - **Required OAuth Scopes**: `repo`
  - `assignees`: Usernames to assign to this issue (string[], optional)
  - `body`: Issue body content (string, optional)
  - `duplicate_of`: Issue number that this issue is a duplicate of. Only used when state_reason is 'duplicate'. (number, optional)
  - `issue_number`: Issue number to update (number, optional)
  - `labels`: Labels to apply to this issue (string[], optional)
  - `method`: Write operation to perform on a single issue.
    Options are:
    - 'create' - creates a new issue.
    - 'update' - updates an existing issue.
     (string, required)
  - `milestone`: Milestone number (number, optional)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)
  - `state`: New state (string, optional)
  - `state_reason`: Reason for the state change. Ignored unless state is changed. (string, optional)
  - `title`: Issue title (string, optional)
  - `type`: Type of this issue. Only use if the repository has issue types configured. Use list_issue_types tool to get valid type values for the organization. If the repository doesn't support issue types, omit this parameter. (string, optional)

- **list_issue_types** - List available issue types
  - **Required OAuth Scopes**: `read:org`
  - **Accepted OAuth Scopes**: `admin:org`, `read:org`, `write:org`
  - `owner`: The organization owner of the repository (string, required)

- **list_issues** - List issues
  - **Required OAuth Scopes**: `repo`
  - `after`: Cursor for pagination. Use the endCursor from the previous page's PageInfo for GraphQL APIs. (string, optional)
  - `direction`: Order direction. If provided, the 'orderBy' also needs to be provided. (string, optional)
  - `labels`: Filter by labels (string[], optional)
  - `orderBy`: Order issues by field. If provided, the 'direction' also needs to be provided. (string, optional)
  - `owner`: Repository owner (string, required)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name (string, required)
  - `since`: Filter by date (ISO 8601 timestamp) (string, optional)
  - `state`: Filter by state, by default both open and closed issues are returned when not provided (string, optional)

- **search_issues** - Search issues
  - **Required OAuth Scopes**: `repo`
  - `order`: Sort order (string, optional)
  - `owner`: Optional repository owner. If provided with repo, only issues for this repository are listed. (string, optional)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `query`: Search query using GitHub issues search syntax (string, required)
  - `repo`: Optional repository name. If provided with owner, only issues for this repository are listed. (string, optional)
  - `sort`: Sort field by number of matches of categories, defaults to best match (string, optional)

- **sub_issue_write** - Change sub-issue
  - **Required OAuth Scopes**: `repo`
  - `after_id`: The ID of the sub-issue to be prioritized after (either after_id OR before_id should be specified) (number, optional)
  - `before_id`: The ID of the sub-issue to be prioritized before (either after_id OR before_id should be specified) (number, optional)
  - `issue_number`: The number of the parent issue (number, required)
  - `method`: The action to perform on a single sub-issue
    Options are:
    - 'add' - add a sub-issue to a parent issue in a GitHub repository.
    - 'remove' - remove a sub-issue from a parent issue in a GitHub repository.
    - 'reprioritize' - change the order of sub-issues within a parent issue in a GitHub repository. Use either 'after_id' or 'before_id' to specify the new position.
    				 (string, required)
  - `owner`: Repository owner (string, required)
  - `replace_parent`: When true, replaces the sub-issue's current parent issue. Use with 'add' method only. (boolean, optional)
  - `repo`: Repository name (string, required)
  - `sub_issue_id`: The ID of the sub-issue to add. ID is not the same as issue number (number, required)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/tag-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/tag-light.png"><img src="pkg/octicons/icons/tag-light.png" width="20" height="20" alt="tag"></picture> Labels</summary>

- **get_label** - Get a specific label from a repository.
  - **Required OAuth Scopes**: `repo`
  - `name`: Label name. (string, required)
  - `owner`: Repository owner (username or organization name) (string, required)
  - `repo`: Repository name (string, required)

- **label_write** - Write operations on repository labels.
  - **Required OAuth Scopes**: `repo`
  - `color`: Label color as 6-character hex code without '#' prefix (e.g., 'f29513'). Required for 'create', optional for 'update'. (string, optional)
  - `description`: Label description text. Optional for 'create' and 'update'. (string, optional)
  - `method`: Operation to perform: 'create', 'update', or 'delete' (string, required)
  - `name`: Label name - required for all operations (string, required)
  - `new_name`: New name for the label (used only with 'update' method to rename) (string, optional)
  - `owner`: Repository owner (username or organization name) (string, required)
  - `repo`: Repository name (string, required)

- **list_label** - List labels from a repository
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (username or organization name) - required for all operations (string, required)
  - `repo`: Repository name - required for all operations (string, required)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/bell-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/bell-light.png"><img src="pkg/octicons/icons/bell-light.png" width="20" height="20" alt="bell"></picture> Notifications</summary>

- **dismiss_notification** - Dismiss notification
  - **Required OAuth Scopes**: `notifications`
  - `state`: The new state of the notification (read/done) (string, required)
  - `threadID`: The ID of the notification thread (string, required)

- **get_notification_details** - Get notification details
  - **Required OAuth Scopes**: `notifications`
  - `notificationID`: The ID of the notification (string, required)

- **list_notifications** - List notifications
  - **Required OAuth Scopes**: `notifications`
  - `before`: Only show notifications updated before the given time (ISO 8601 format) (string, optional)
  - `filter`: Filter notifications to, use default unless specified. Read notifications are ones that have already been acknowledged by the user. Participating notifications are those that the user is directly involved in, such as issues or pull requests they have commented on or created. (string, optional)
  - `owner`: Optional repository owner. If provided with repo, only notifications for this repository are listed. (string, optional)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Optional repository name. If provided with owner, only notifications for this repository are listed. (string, optional)
  - `since`: Only show notifications updated after the given time (ISO 8601 format) (string, optional)

- **manage_notification_subscription** - Manage notification subscription
  - **Required OAuth Scopes**: `notifications`
  - `action`: Action to perform: ignore, watch, or delete the notification subscription. (string, required)
  - `notificationID`: The ID of the notification thread. (string, required)

- **manage_repository_notification_subscription** - Manage repository notification subscription
  - **Required OAuth Scopes**: `notifications`
  - `action`: Action to perform: ignore, watch, or delete the repository notification subscription. (string, required)
  - `owner`: The account owner of the repository. (string, required)
  - `repo`: The name of the repository. (string, required)

- **mark_all_notifications_read** - Mark all notifications as read
  - **Required OAuth Scopes**: `notifications`
  - `lastReadAt`: Describes the last point that notifications were checked (optional). Default: Now (string, optional)
  - `owner`: Optional repository owner. If provided with repo, only notifications for this repository are marked as read. (string, optional)
  - `repo`: Optional repository name. If provided with owner, only notifications for this repository are marked as read. (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/organization-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/organization-light.png"><img src="pkg/octicons/icons/organization-light.png" width="20" height="20" alt="organization"></picture> Organizations</summary>

- **search_orgs** - Search organizations
  - **Required OAuth Scopes**: `read:org`
  - **Accepted OAuth Scopes**: `admin:org`, `read:org`, `write:org`
  - `order`: Sort order (string, optional)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `query`: Organization search query. Examples: 'microsoft', 'location:california', 'created:>=2025-01-01'. Search is automatically scoped to type:org. (string, required)
  - `sort`: Sort field by category (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/project-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/project-light.png"><img src="pkg/octicons/icons/project-light.png" width="20" height="20" alt="project"></picture> Projects</summary>

- **projects_get** - Get details of GitHub Projects resources
  - **Required OAuth Scopes**: `read:project`
  - **Accepted OAuth Scopes**: `project`, `read:project`
  - `field_id`: The field's ID. Required for 'get_project_field' method. (number, optional)
  - `fields`: Specific list of field IDs to include in the response when getting a project item (e.g. ["102589", "985201", "169875"]). If not provided, only the title field is included. Only used for 'get_project_item' method. (string[], optional)
  - `item_id`: The item's ID. Required for 'get_project_item' method. (number, optional)
  - `method`: The method to execute (string, required)
  - `owner`: The owner (user or organization login). The name is not case sensitive. (string, optional)
  - `owner_type`: Owner type (user or org). If not provided, will be automatically detected. (string, optional)
  - `project_number`: The project's number. (number, optional)
  - `status_update_id`: The node ID of the project status update. Required for 'get_project_status_update' method. (string, optional)

- **projects_list** - List GitHub Projects resources
  - **Required OAuth Scopes**: `read:project`
  - **Accepted OAuth Scopes**: `project`, `read:project`
  - `after`: Forward pagination cursor from previous pageInfo.nextCursor. (string, optional)
  - `before`: Backward pagination cursor from previous pageInfo.prevCursor (rare). (string, optional)
  - `fields`: Field IDs to include when listing project items (e.g. ["102589", "985201"]). CRITICAL: Always provide to get field values. Without this, only titles returned. Only used for 'list_project_items' method. (string[], optional)
  - `method`: The action to perform (string, required)
  - `owner`: The owner (user or organization login). The name is not case sensitive. (string, required)
  - `owner_type`: Owner type (user or org). If not provided, will automatically try both. (string, optional)
  - `per_page`: Results per page (max 50) (number, optional)
  - `project_number`: The project's number. Required for 'list_project_fields', 'list_project_items', and 'list_project_status_updates' methods. (number, optional)
  - `query`: Filter/query string. For list_projects: filter by title text and state (e.g. "roadmap is:open"). For list_project_items: advanced filtering using GitHub's project filtering syntax. (string, optional)

- **projects_write** - Modify GitHub Project items
  - **Required OAuth Scopes**: `project`
  - `body`: The body of the status update (markdown). Used for 'create_project_status_update' method. (string, optional)
  - `issue_number`: The issue number (use when item_type is 'issue' for 'add_project_item' method). Provide either issue_number or pull_request_number. (number, optional)
  - `item_id`: The project item ID. Required for 'update_project_item' and 'delete_project_item' methods. (number, optional)
  - `item_owner`: The owner (user or organization) of the repository containing the issue or pull request. Required for 'add_project_item' method. (string, optional)
  - `item_repo`: The name of the repository containing the issue or pull request. Required for 'add_project_item' method. (string, optional)
  - `item_type`: The item's type, either issue or pull_request. Required for 'add_project_item' method. (string, optional)
  - `method`: The method to execute (string, required)
  - `owner`: The project owner (user or organization login). The name is not case sensitive. (string, required)
  - `owner_type`: Owner type (user or org). If not provided, will be automatically detected. (string, optional)
  - `project_number`: The project's number. (number, required)
  - `pull_request_number`: The pull request number (use when item_type is 'pull_request' for 'add_project_item' method). Provide either issue_number or pull_request_number. (number, optional)
  - `start_date`: The start date of the status update in YYYY-MM-DD format. Used for 'create_project_status_update' method. (string, optional)
  - `status`: The status of the project. Used for 'create_project_status_update' method. (string, optional)
  - `target_date`: The target date of the status update in YYYY-MM-DD format. Used for 'create_project_status_update' method. (string, optional)
  - `updated_field`: Object consisting of the ID of the project field to update and the new value for the field. To clear the field, set value to null. Example: {"id": 123456, "value": "New Value"}. Required for 'update_project_item' method. (object, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/git-pull-request-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/git-pull-request-light.png"><img src="pkg/octicons/icons/git-pull-request-light.png" width="20" height="20" alt="git-pull-request"></picture> Pull Requests</summary>

- **add_comment_to_pending_review** - Add review comment to the requester's latest pending pull request review
  - **Required OAuth Scopes**: `repo`
  - `body`: The text of the review comment (string, required)
  - `line`: The line of the blob in the pull request diff that the comment applies to. For multi-line comments, the last line of the range (number, optional)
  - `owner`: Repository owner (string, required)
  - `path`: The relative path to the file that necessitates a comment (string, required)
  - `pullNumber`: Pull request number (number, required)
  - `repo`: Repository name (string, required)
  - `side`: The side of the diff to comment on. LEFT indicates the previous state, RIGHT indicates the new state (string, optional)
  - `startLine`: For multi-line comments, the first line of the range that the comment applies to (number, optional)
  - `startSide`: For multi-line comments, the starting side of the diff that the comment applies to. LEFT indicates the previous state, RIGHT indicates the new state (string, optional)
  - `subjectType`: The level at which the comment is targeted (string, required)

- **add_reply_to_pull_request_comment** - Add reply to pull request comment
  - **Required OAuth Scopes**: `repo`
  - `body`: The text of the reply (string, required)
  - `commentId`: The ID of the comment to reply to (number, required)
  - `owner`: Repository owner (string, required)
  - `pullNumber`: Pull request number (number, required)
  - `repo`: Repository name (string, required)

- **create_pull_request** - Open new pull request
  - **Required OAuth Scopes**: `repo`
  - `base`: Branch to merge into (string, required)
  - `body`: PR description (string, optional)
  - `draft`: Create as draft PR (boolean, optional)
  - `head`: Branch containing changes (string, required)
  - `maintainer_can_modify`: Allow maintainer edits (boolean, optional)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)
  - `title`: PR title (string, required)

- **list_pull_requests** - List pull requests
  - **Required OAuth Scopes**: `repo`
  - `base`: Filter by base branch (string, optional)
  - `direction`: Sort direction (string, optional)
  - `head`: Filter by head user/org and branch (string, optional)
  - `owner`: Repository owner (string, required)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name (string, required)
  - `sort`: Sort by (string, optional)
  - `state`: Filter by state (string, optional)

- **merge_pull_request** - Merge pull request
  - **Required OAuth Scopes**: `repo`
  - `commit_message`: Extra detail for merge commit (string, optional)
  - `commit_title`: Title for merge commit (string, optional)
  - `merge_method`: Merge method (string, optional)
  - `owner`: Repository owner (string, required)
  - `pullNumber`: Pull request number (number, required)
  - `repo`: Repository name (string, required)

- **pull_request_read** - Get details for a single pull request
  - **Required OAuth Scopes**: `repo`
  - `method`: Action to specify what pull request data needs to be retrieved from GitHub. 
    Possible options: 
     1. get - Get details of a specific pull request.
     2. get_diff - Get the diff of a pull request.
     3. get_status - Get combined commit status of a head commit in a pull request.
     4. get_files - Get the list of files changed in a pull request. Use with pagination parameters to control the number of results returned.
     5. get_review_comments - Get review threads on a pull request. Each thread contains logically grouped review comments made on the same code location during pull request reviews. Returns threads with metadata (isResolved, isOutdated, isCollapsed) and their associated comments. Use cursor-based pagination (perPage, after) to control results.
     6. get_reviews - Get the reviews on a pull request. When asked for review comments, use get_review_comments method. Use with pagination parameters to control the number of results returned.
     7. get_comments - Get comments on a pull request. Use this if user doesn't specifically want review comments. Use with pagination parameters to control the number of results returned.
     8. get_check_runs - Get check runs for the head commit of a pull request. Check runs are the individual CI/CD jobs and checks that run on the PR.
     (string, required)
  - `owner`: Repository owner (string, required)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `pullNumber`: Pull request number (number, required)
  - `repo`: Repository name (string, required)

- **pull_request_review_write** - Write operations (create, submit, delete) on pull request reviews.
  - **Required OAuth Scopes**: `repo`
  - `body`: Review comment text (string, optional)
  - `commitID`: SHA of commit to review (string, optional)
  - `event`: Review action to perform. (string, optional)
  - `method`: The write operation to perform on pull request review. (string, required)
  - `owner`: Repository owner (string, required)
  - `pullNumber`: Pull request number (number, required)
  - `repo`: Repository name (string, required)
  - `threadId`: The node ID of the review thread (e.g., PRRT_kwDOxxx). Required for resolve_thread and unresolve_thread methods. Get thread IDs from pull_request_read with method get_review_comments. (string, optional)

- **search_pull_requests** - Search pull requests
  - **Required OAuth Scopes**: `repo`
  - `order`: Sort order (string, optional)
  - `owner`: Optional repository owner. If provided with repo, only pull requests for this repository are listed. (string, optional)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `query`: Search query using GitHub pull request search syntax (string, required)
  - `repo`: Optional repository name. If provided with owner, only pull requests for this repository are listed. (string, optional)
  - `sort`: Sort field by number of matches of categories, defaults to best match (string, optional)

- **update_pull_request** - Edit pull request
  - **Required OAuth Scopes**: `repo`
  - `base`: New base branch name (string, optional)
  - `body`: New description (string, optional)
  - `draft`: Mark pull request as draft (true) or ready for review (false) (boolean, optional)
  - `maintainer_can_modify`: Allow maintainer edits (boolean, optional)
  - `owner`: Repository owner (string, required)
  - `pullNumber`: Pull request number to update (number, required)
  - `repo`: Repository name (string, required)
  - `reviewers`: GitHub usernames to request reviews from (string[], optional)
  - `state`: New state (string, optional)
  - `title`: New title (string, optional)

- **update_pull_request_branch** - Update pull request branch
  - **Required OAuth Scopes**: `repo`
  - `expectedHeadSha`: The expected SHA of the pull request's HEAD ref (string, optional)
  - `owner`: Repository owner (string, required)
  - `pullNumber`: Pull request number (number, required)
  - `repo`: Repository name (string, required)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/repo-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/repo-light.png"><img src="pkg/octicons/icons/repo-light.png" width="20" height="20" alt="repo"></picture> Repositories</summary>

- **create_branch** - Create branch
  - **Required OAuth Scopes**: `repo`
  - `branch`: Name for new branch (string, required)
  - `from_branch`: Source branch (defaults to repo default) (string, optional)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

- **create_or_update_file** - Create or update file
  - **Required OAuth Scopes**: `repo`
  - `branch`: Branch to create/update the file in (string, required)
  - `content`: Content of the file (string, required)
  - `message`: Commit message (string, required)
  - `owner`: Repository owner (username or organization) (string, required)
  - `path`: Path where to create/update the file (string, required)
  - `repo`: Repository name (string, required)
  - `sha`: The blob SHA of the file being replaced. Required if the file already exists. (string, optional)

- **create_repository** - Create repository
  - **Required OAuth Scopes**: `repo`
  - `autoInit`: Initialize with README (boolean, optional)
  - `description`: Repository description (string, optional)
  - `name`: Repository name (string, required)
  - `organization`: Organization to create the repository in (omit to create in your personal account) (string, optional)
  - `private`: Whether repo should be private (boolean, optional)

- **delete_file** - Delete file
  - **Required OAuth Scopes**: `repo`
  - `branch`: Branch to delete the file from (string, required)
  - `message`: Commit message (string, required)
  - `owner`: Repository owner (username or organization) (string, required)
  - `path`: Path to the file to delete (string, required)
  - `repo`: Repository name (string, required)

- **fork_repository** - Fork repository
  - **Required OAuth Scopes**: `repo`
  - `organization`: Organization to fork to (string, optional)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

- **get_commit** - Get commit details
  - **Required OAuth Scopes**: `repo`
  - `include_diff`: Whether to include file diffs and stats in the response. Default is true. (boolean, optional)
  - `owner`: Repository owner (string, required)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name (string, required)
  - `sha`: Commit SHA, branch name, or tag name (string, required)

- **get_file_contents** - Get file or directory contents
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (username or organization) (string, required)
  - `path`: Path to file/directory (string, optional)
  - `ref`: Accepts optional git refs such as `refs/tags/{tag}`, `refs/heads/{branch}` or `refs/pull/{pr_number}/head` (string, optional)
  - `repo`: Repository name (string, required)
  - `sha`: Accepts optional commit SHA. If specified, it will be used instead of ref (string, optional)

- **get_latest_release** - Get latest release
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

- **get_release_by_tag** - Get a release by tag name
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)
  - `tag`: Tag name (e.g., 'v1.0.0') (string, required)

- **get_tag** - Get tag details
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)
  - `tag`: Tag name (string, required)

- **list_branches** - List branches
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name (string, required)

- **list_commits** - List commits
  - **Required OAuth Scopes**: `repo`
  - `author`: Author username or email address to filter commits by (string, optional)
  - `owner`: Repository owner (string, required)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `path`: Only commits containing this file path will be returned (string, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name (string, required)
  - `sha`: Commit SHA, branch or tag name to list commits of. If not provided, uses the default branch of the repository. If a commit SHA is provided, will list commits up to that SHA. (string, optional)
  - `since`: Only commits after this date will be returned (ISO 8601 format: YYYY-MM-DDTHH:MM:SSZ or YYYY-MM-DD) (string, optional)
  - `until`: Only commits before this date will be returned (ISO 8601 format: YYYY-MM-DDTHH:MM:SSZ or YYYY-MM-DD) (string, optional)

- **list_releases** - List releases
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name (string, required)

- **list_tags** - List tags
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `repo`: Repository name (string, required)

- **push_files** - Push files to repository
  - **Required OAuth Scopes**: `repo`
  - `branch`: Branch to push to (string, required)
  - `files`: Array of file objects to push, each object with path (string) and content (string) (object[], required)
  - `message`: Commit message (string, required)
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

- **search_code** - Search code
  - **Required OAuth Scopes**: `repo`
  - `order`: Sort order for results (string, optional)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `query`: Search query using GitHub's powerful code search syntax. Examples: 'content:Skill language:Java org:github', 'NOT is:archived language:Python OR language:go', 'repo:github/github-mcp-server'. Supports exact matching, language filters, path filters, and more. (string, required)
  - `sort`: Sort field ('indexed' only) (string, optional)

- **search_repositories** - Search repositories
  - **Required OAuth Scopes**: `repo`
  - `minimal_output`: Return minimal repository information (default: true). When false, returns full GitHub API repository objects. (boolean, optional)
  - `order`: Sort order (string, optional)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `query`: Repository search query. Examples: 'machine learning in:name stars:>1000 language:python', 'topic:react', 'user:facebook'. Supports advanced search syntax for precise filtering. (string, required)
  - `sort`: Sort repositories by field, defaults to best match (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/shield-lock-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/shield-lock-light.png"><img src="pkg/octicons/icons/shield-lock-light.png" width="20" height="20" alt="shield-lock"></picture> Secret Protection</summary>

- **get_secret_scanning_alert** - Get secret scanning alert
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `alertNumber`: The number of the alert. (number, required)
  - `owner`: The owner of the repository. (string, required)
  - `repo`: The name of the repository. (string, required)

- **list_secret_scanning_alerts** - List secret scanning alerts
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `owner`: The owner of the repository. (string, required)
  - `repo`: The name of the repository. (string, required)
  - `resolution`: Filter by resolution (string, optional)
  - `secret_type`: A comma-separated list of secret types to return. All default secret patterns are returned. To return generic patterns, pass the token name(s) in the parameter. (string, optional)
  - `state`: Filter by state (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/shield-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/shield-light.png"><img src="pkg/octicons/icons/shield-light.png" width="20" height="20" alt="shield"></picture> Security Advisories</summary>

- **get_global_security_advisory** - Get a global security advisory
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `ghsaId`: GitHub Security Advisory ID (format: GHSA-xxxx-xxxx-xxxx). (string, required)

- **list_global_security_advisories** - List global security advisories
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `affects`: Filter advisories by affected package or version (e.g. "package1,package2@1.0.0"). (string, optional)
  - `cveId`: Filter by CVE ID. (string, optional)
  - `cwes`: Filter by Common Weakness Enumeration IDs (e.g. ["79", "284", "22"]). (string[], optional)
  - `ecosystem`: Filter by package ecosystem. (string, optional)
  - `ghsaId`: Filter by GitHub Security Advisory ID (format: GHSA-xxxx-xxxx-xxxx). (string, optional)
  - `isWithdrawn`: Whether to only return withdrawn advisories. (boolean, optional)
  - `modified`: Filter by publish or update date or date range (ISO 8601 date or range). (string, optional)
  - `published`: Filter by publish date or date range (ISO 8601 date or range). (string, optional)
  - `severity`: Filter by severity. (string, optional)
  - `type`: Advisory type. (string, optional)
  - `updated`: Filter by update date or date range (ISO 8601 date or range). (string, optional)

- **list_org_repository_security_advisories** - List org repository security advisories
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `direction`: Sort direction. (string, optional)
  - `org`: The organization login. (string, required)
  - `sort`: Sort field. (string, optional)
  - `state`: Filter by advisory state. (string, optional)

- **list_repository_security_advisories** - List repository security advisories
  - **Required OAuth Scopes**: `security_events`
  - **Accepted OAuth Scopes**: `repo`, `security_events`
  - `direction`: Sort direction. (string, optional)
  - `owner`: The owner of the repository. (string, required)
  - `repo`: The name of the repository. (string, required)
  - `sort`: Sort field. (string, optional)
  - `state`: Filter by advisory state. (string, optional)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/star-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/star-light.png"><img src="pkg/octicons/icons/star-light.png" width="20" height="20" alt="star"></picture> Stargazers</summary>

- **list_starred_repositories** - List starred repositories
  - **Required OAuth Scopes**: `repo`
  - `direction`: The direction to sort the results by. (string, optional)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `sort`: How to sort the results. Can be either 'created' (when the repository was starred) or 'updated' (when the repository was last pushed to). (string, optional)
  - `username`: Username to list starred repositories for. Defaults to the authenticated user. (string, optional)

- **star_repository** - Star repository
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

- **unstar_repository** - Unstar repository
  - **Required OAuth Scopes**: `repo`
  - `owner`: Repository owner (string, required)
  - `repo`: Repository name (string, required)

</details>

<details>

<summary><picture><source media="(prefers-color-scheme: dark)" srcset="pkg/octicons/icons/people-dark.png"><source media="(prefers-color-scheme: light)" srcset="pkg/octicons/icons/people-light.png"><img src="pkg/octicons/icons/people-light.png" width="20" height="20" alt="people"></picture> Users</summary>

- **search_users** - Search users
  - **Required OAuth Scopes**: `repo`
  - `order`: Sort order (string, optional)
  - `page`: Page number for pagination (min 1) (number, optional)
  - `perPage`: Results per page for pagination (min 1, max 100) (number, optional)
  - `query`: User search query. Examples: 'john smith', 'location:seattle', 'followers:>100'. Search is automatically scoped to type:user. (string, required)
  - `sort`: Sort users by number of followers or repositories, or when the person joined GitHub. (string, optional)

</details>
<!-- END AUTOMATED TOOLS -->

### Additional Tools in Remote GitHub MCP Server

<details>

<summary>Copilot</summary>

- **create_pull_request_with_copilot** - Perform task with GitHub Copilot coding agent
  - `owner`: Repository owner. You can guess the owner, but confirm it with the user before proceeding. (string, required)
  - `repo`: Repository name. You can guess the repository name, but confirm it with the user before proceeding. (string, required)
  - `problem_statement`: Detailed description of the task to be performed (e.g., 'Implement a feature that does X', 'Fix bug Y', etc.) (string, required)
  - `title`: Title for the pull request that will be created (string, required)
  - `base_ref`: Git reference (e.g., branch) that the agent will start its work from. If not specified, defaults to the repository's default branch (string, optional)

</details>

<details>

<summary>Copilot Spaces</summary>

- **get_copilot_space** - Get Copilot Space
  - `owner`: The owner of the space. (string, required)
  - `name`: The name of the space. (string, required)

- **list_copilot_spaces** - List Copilot Spaces

</details>

<details>

<summary>GitHub Support Docs Search</summary>

- **github_support_docs_search** - Retrieve documentation relevant to answer GitHub product and support questions. Support topics include: GitHub Actions Workflows, Authentication, GitHub Support Inquiries, Pull Request Practices, Repository Maintenance, GitHub Pages, GitHub Packages, GitHub Discussions, Copilot Spaces
  - `query`: Input from the user about the question they need answered. This is the latest raw unedited user message. You should ALWAYS leave the user message as it is, you should never modify it. (string, required)

</details>
