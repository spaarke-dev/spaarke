# File delivery probe: can a Spaarke file reach a Cowork skill's sandbox? (#3257 gap 1)

**Why.** The review skills need the real .docx on disk to build a redline. Cowork can't read SharePoint Embedded
files itself (`notes/research/cowork-access-to-spe-files-2026-11.md`), so the file has to arrive another way. This
probe settles the two routes no Microsoft page documents:

- **Route 3:** the Spaarke MCP tool hands the file back to Cowork, and Cowork saves it as a file the script can open.
- **Route 4:** the skill's Python script downloads the file itself, which only works if scripts can reach the internet.

If route 3 works, a skill can find a Spaarke Document record with the MCP tools and receive its file in one step. If
only route 4 works, the BFF would hand out a short-lived, single-use download link (new endpoint, security review).
If neither works, the user downloads and attaches the file (works today), or the BFF stages a copy in OneDrive.

**The probe files** contain no client content: a title, the probe code `FDP-1126-A` and filler text. They are built
by `make_probe_docx.py` (same bytes every time). The SHA-256 proves the real file arrived: a model can't guess it.

| File | Size | SHA-256 (first 12) |
|---|---|---|
| `spaarke-file-probe-small.docx` | 5,673 bytes | `2c05271788b4` |
| `spaarke-file-probe-large.docx` | 191,602 bytes (a real contract's size) | `6d7939541ab0` |

The large file matters: Microsoft warns that passing a file through the chat as text "appears to work on small test
files and fails on real ones", and the only documented inline limit for a tool result is 64 KiB.

## Part A. Can a Cowork script reach the internet? (5 minutes, can run now)

1. Open a new Cowork chat (any licensed user).
2. Paste this prompt:

   > Run this Python exactly and show me its full output, including any error. Don't change the code and don't
   > make up output.
   > ```
   > import urllib.request
   > for u in ["https://learn.microsoft.com/", "https://spaarke-bff-dev.azurewebsites.net/healthz"]:
   >     try:
   >         r = urllib.request.urlopen(u, timeout=15)
   >         print("OK", r.status, u)
   >     except Exception as e:
   >         print("FAIL", u, type(e).__name__, e)
   > ```

3. If Cowork shows a card asking to allow network access, choose **Allow once** and note that it asked.

| Output | Means |
|---|---|
| `OK 200` for both | Scripts can reach the internet and our dev server: route 4 is possible |
| `OK` for Microsoft, `FAIL` for ours | Outbound access is limited to an allow-list; route 4 would need ours added (unlikely) |
| `FAIL` for both | Scripts can't reach the internet: route 4 is out |

**Record:** the two output lines exactly, and whether a card asked first.

## Part B. Can an MCP tool result become a file? (20 minutes, after the tool is on the dev server)

**Who:** the demo user, or any user with **Spaarke MCP** connected (as in WP-3). **Needs:** the test tool
`SpaarkeMCP_spike_return_file` on the dev server (requested from spaarkeai-mcp-bff-api-r2; this README says when it's
there). It returns one probe file in one of three shapes:

| Variant | Shape of the result |
|---|---|
| `a` | The file as base64 text in an ordinary text result |
| `b` | An embedded MCP resource: the file's bytes (`blob`) with the Word MIME type |
| `c` | An MCP resource link: a pointer the client fetches over the same connection (`resources/read`) |

1. New Cowork chat. Check that **Spaarke MCP** is on.
2. For each run below, a **new** chat, with this prompt (fill in variant and size):

   > Use Spaarke MCP to run SpaarkeMCP_spike_return_file with variant **b** and size **small**. Then run Python that
   > lists every file in your working folders, and for each .docx prints its path, its size in bytes, the first 12
   > characters of its SHA-256, and whether its word/document.xml contains FDP-1126-A. Show me the full output. If
   > there is no .docx on disk, say exactly that; don't create one from the text of the tool result.

3. Runs, in this order (stop a variant at its first failure):

| Run | Variant | Size |
|---|---|---|
| B1 | b | small |
| B2 | b | large |
| B3 | c | small |
| B4 | c | large |
| B5 | a | small |
| B6 | a | large |

| Output | Means |
|---|---|
| A path, the right size and hash, `FDP-1126-A` found, **and** Copilot didn't write the file itself | Route 3 works for this shape and size |
| Right hash, but Copilot says it decoded the text into a file | The file went through the chat text; expect the large size to fail |
| No .docx, or a wrong hash | This shape doesn't deliver a file |
| The tool call fails | Retry once after a minute (a server update causes about a minute of errors), then record the error |

**Record:** for each run, the output lines, whether Copilot said it saved or decoded the file itself, and any card
or prompt Cowork showed.

## After the test

- Results go in `notes/poc-e2e/results.md` (new section) and the answer in `notes/copilot-app-components.md` under
  #3257 gap 1; then tell spaarkeai-mcp-bff-api-r2, which can remove the test tool.
- Nothing is uploaded to the tenant for this probe: the tool sits on the existing Spaarke MCP dev connector.
