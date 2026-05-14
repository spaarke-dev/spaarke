# Dataverse MCP — Standard Tool Invocations

> **Source**: Verbatim transcripts from `microsoft/Dataverse-MCP/lab02/README.md` (commit `2bc5d0098b8ff49a7228aaf2a08df426a060ef6c`, captured 2026-05-14).
> Microsoft-authored examples showing how the Dataverse MCP server's built-in tools respond when called from Claude desktop. These are the canonical "shape" of standard MCP tool invocations against a Dataverse environment.

## Example 1 — `list_tables`

### Prompt
```
List tables in Dataverse
```

### Response (verbatim, from Lab 02)
```
Great! I've retrieved a list of all tables in your Dataverse database. There are many tables available. Here's a selection of some common tables that might be useful:

1. account
2. contact
3. annotation
4. email
5. task
6. appointment
7. opportunity
8. lead
9. product
10. queue
11. systemuser
12. team
13. businessunit
14. workflow
15. activitypointer

These are just a few of the many tables available in your Dataverse. If you'd like to explore a specific table in more detail, I can help you describe its schema or run queries to see the data it contains. Is there a particular table you're interested in learning more about?
```

### What this demonstrates
- `list_tables` returns the **logical entity names** of all tables visible to the authenticated user.
- The agent typically follows up by calling `describe_table` on a specific table before issuing a `read_query` — this is the recommended multi-tool pattern.

> **Lab 02 note**: *"It could be that Claude will first trigger the list_knowledge_source tool. Don't worry about that, because it will later trigger the list_tables tool."*

---

## Example 2 — `read_query` (returns empty / schema-only result)

### Prompt
```
List the rows in the contacts table.
```

### Response (verbatim, from Lab 02)
```
I've checked the contacts table in your Dataverse database, and currently there are no records in the table. The table exists with a comprehensive schema that includes fields for contact information such as:

- First name, last name, and full name
- Email addresses (up to 3)
- Phone numbers (multiple options including mobile, business, home)
- Job title and department
- Address information (up to 3 addresses with details like city, state, country)
- And many other standard CRM contact fields

However, the query returned a count of 0, indicating that no contact records have been created yet in the system.

Would you like me to help you create a new contact record in the table?
```

### What this demonstrates
- The `read_query` tool executes **SELECT-style** queries (T-SQL grammar against the Dataverse SQL endpoint).
- Per Lab 02 commentary: *"You might have noticed Claude performed a lot of calls to the tools. It will try out a bunch of steps to improve the action it will take. For instance, it will first list the tables to see if there is a table called contacts, and then it will describe the contacts table to get the columns in that table."*
- This **list → describe → query** chain is consistent with the agent-instruction guidance in `data-platform-mcp-disable.md` (see `docs/`):
  > *"Whenever you have to use logical table name, call the list_tables tools to get that logical table name."*
  > *"Whenever you have to use column/attribute name, call the describe_table tool to get the column/attribute name."*

---

## Example 3 — `create_record` (bulk via repeated single calls)

### Prompt
```
Please create 10 example items in the contact table in Dataverse. Clearly mark them with (sample).
```

### Response (verbatim, from Lab 02)
```
I've successfully created 10 sample contacts in your Dataverse contacts table. Here's a summary of the contacts created, all clearly marked with "(sample)" in their first names:

1. John (sample) Smith - Marketing Manager in Marketing Dept
    - Email: john.smith@example.com
    - Phone: 555-123-4567

2. Sarah (sample) Johnson - Sales Director in Sales Dept
    - Email: sarah.johnson@example.com
    - Phone: 555-234-5678

3. Michael (sample) Brown - IT Manager in Information Technology Dept
    - Email: michael.brown@example.com
    - Phone: 555-345-6789

[... 7 more contacts ...]

All contacts have been successfully created and stored in the Dataverse contacts table with their unique contact IDs.
```

### What this demonstrates
- `create_record` is called **once per row** — the MCP server has **no bulk-create tool**.
- For bulk loads (CSV imports, sample data > ~50 rows), this is the wrong tool: use the Dataverse Web API / Python SDK / PAC CLI instead. See `knowledge/dataverse-mcp/NOTES.md` for the routing decision tree.
- Claude maintains conversational state across calls — after the create loop it implicitly issued a query to verify all 10 records existed.

---

## Tool inventory (as of 2026-05-14)

These are the Dataverse MCP server's **standard built-in tools** (Learn page: `data-platform-mcp.md` in `docs/`):

| Tool | Description |
| --- | --- |
| `create_record` | Inserts a new row into a Dataverse table and returns the GUID. |
| `describe_table` | Retrieves the T-SQL schema of a specified table. |
| `list_tables` | Lists all tables in the Dataverse environment. |
| `read_query` | Executes SELECT queries to fetch data from Dataverse. |
| `update_record` | Updates an existing row in a Dataverse table. |
| `Create Table` | Creates a new table with a specified schema. |
| `Update Table` | Modifies schema or metadata of an existing table. |
| `Delete Table` | Deletes a table from Dataverse. |
| `Delete Record` | Deletes a row from a Dataverse table. |
| `Search` | Searches through keywords over Dataverse for specific record. |
| `Fetch` | Retrieves full content of record in Dataverse using entity name and ID. |

> **Metering note**: Starting **December 15, 2025**, these tools are charged when called by agents **outside** Microsoft Copilot Studio. `Search` is billed at the *Tenant graph grounding* rate; everything else at the *Text and generative AI tools (basic)* rate. Premium Dynamics 365 licenses and the M365 Copilot USL exempt access to Dynamics 365 data. See `docs/data-platform-mcp.md`.
