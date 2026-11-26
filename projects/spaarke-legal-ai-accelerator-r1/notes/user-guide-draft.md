# Spaarke Legal AI Accelerator: setup and user guide (individual edition) — DRAFT for owner review

> Draft 2026-11-26, from the tests to date (`notes/poc-e2e/results.md`, through Word desktop round 2). Once the owner
> approves it, this text replaces the Word and Cowork sections of the package's *Read me first*
> (`src/accelerators/build/package_docs.py`, `WORD_STEPS` and `render_readme`), and becomes the source for the
> one-page quick cards. Items marked **[TO CONFIRM]** need an answer from the owner or one more test.

---

## 1. What the accelerator is

The Spaarke Legal AI Accelerator is **a set of files you put in your OneDrive**. It gives Microsoft 365 Copilot two
things it doesn't have by itself:

- **Playbooks**: your positions on each document type (for example a mutual NDA, or a legal hold notice for
  employees), topic by topic, with what is acceptable, what is a fallback and what must be escalated.
- **Review procedures**: step-by-step instructions that make Copilot review a document against a playbook the same
  way every time, rate each topic, and prepare tracked changes.

There is no software to install and nothing for IT to do, as long as your Copilot licence, Microsoft's Legal plugin
and Cowork are turned on. You use it in two places:

| Where | What it is | What you use it for |
|---|---|---|
| **Word** (the desktop app for Windows) | The Copilot pane inside Word | Review the contract you have open and apply the changes as tracked changes with comments. Draft from a template or precedent |
| **Cowork** (in the Microsoft 365 Copilot app) | Copilot's workspace for longer tasks | Build a full review package (redline, clean copy, summary page) without opening Word. Review a folder of NDAs at once |

Everything Copilot produces is **a draft for attorney review, not legal advice**.

## 2. Three words to keep apart

People get confused because "playbook" and "skill" each mean more than one thing in Copilot.

| Word | What it is | Where it lives |
|---|---|---|
| **Playbook file** | Our file for one document type, for example `Spaarke NDA – Mutual v1.2.docx`. This is the master you keep up to date | `Documents/Cowork/Playbooks/<document type>/` in your OneDrive |
| **Saved playbook** | A copy Word keeps after you choose *Save playbook*. It is **not a file**: you won't find it in OneDrive. It **does not change** when the file changes | Inside Copilot. You see the list when you choose *Review contract with a playbook* |
| **Skill** | A procedure Copilot follows. **Microsoft's** skills come with the Legal plugin (in Word: *Review contract*, *Draft new contract from a precedent*, *Create playbook*). **Ours** run in Cowork (`/nda-review`, `/legal-hold-review`, `/nda-library-review`, `/accelerator-setup`) | Microsoft's: built into Copilot. Ours: `Documents/Cowork/Skills/` |

Other Word files with "playbook" in their name (for example `mutual-nda-review-playbook.docx`) are drafts that
Word's *Create playbook* made, or old test copies. They are not your playbooks. Don't use them.

## 3. Which tool for which job

| I want to… | Use | Start with |
|---|---|---|
| Review a contract and get tracked changes in the document I have open | Word | `/` → **Review contract with a playbook** → pick the saved playbook |
| Review a legal hold notice (or another document that is not a contract) | Word | Agent mode, *Chat only*, attach the playbook file |
| Get a redlined copy, a clean copy and a one-page summary without opening Word | Cowork | `/nda-review` or `/legal-hold-review` |
| Review a whole folder of NDAs | Cowork | `/nda-library-review` |
| Draft a contract from our approved form | Word | `/` → **Draft new contract from a precedent** |
| Draft a notice from a template and the matter's facts | Word | Agent mode, *Allow editing*, attach the template and the facts |
| Check who must approve and sign | Word or Copilot chat | Attach the approval policy and ask |

## 4. What's in the package and where each file goes

You get one zip file. Extracting it into your OneDrive **Documents** folder puts every file in the right place:

```
Documents/
└─ Cowork/                         ← Cowork's own folder (Microsoft made it)
   ├─ Skills/                      ← OURS. Cowork loads these by itself. Don't open or change them
   │  ├─ nda-review/
   │  ├─ legal-hold-review/
   │  ├─ nda-library-review/
   │  └─ accelerator-setup/
   ├─ Playbooks/                   ← OURS. Your playbook files
   │  ├─ Start here/               ← Read me first, the custom instruction text, two agent recipes
   │  ├─ NDA/
   │  │  ├─ Spaarke NDA – Mutual v1.2.docx                  ← for Word (attach it, or save it as a saved playbook)
   │  │  ├─ Spaarke NDA – Mutual v1.2 (agent knowledge).docx ← for the optional agents only
   │  │  └─ Playbook master/Spaarke NDA – Mutual v1.2.md     ← for Cowork (our skills read it)
   │  ├─ Legal Hold Notice/        ← the same three files for the legal hold notice playbook
   │  └─ Review method/
   │     └─ Spaarke Review Method.docx  ← for the rated chart in Word (optional)
   ├─ Profile/                     ← OURS. Your name and defaults, written by /accelerator-setup
   └─ Projects/, Tasks/, AGENTS.md ← COWORK'S. Leave them alone
```

Two other folders you may notice, both **Copilot's own**. Put nothing of yours in them:

- `Documents/Copilot/Created/`: files Copilot makes, including drafts from Word's *Create playbook*.
- `Microsoft Copilot Chat Files/`: copies of files you upload into a chat **from your PC** **[TO CONFIRM folder]**.
  This is why you should attach from OneDrive rather than from your PC: each upload makes another copy that search
  later finds.

## 5. Set up (once, about 20 minutes)

1. **Check the prerequisites** (ask IT if unsure): a Microsoft 365 Copilot licence, Microsoft's **Legal** plugin
   turned on, and **Cowork** turned on with credits. Word reviews need **Word for Windows**; Word in the browser
   has no Legal plugin.
2. **Extract the zip into your OneDrive `Documents` folder.** If Windows asks, choose *Replace the files in the
   destination*. Keep the folder and file names as they are.
3. **Wait about 15 minutes** so Copilot's search can find the new files.
4. **Save each contract playbook in Word** (once per playbook version):
   1. Open Word for Windows and any NDA.
   2. In the Copilot pane, type `/` and choose **Review contract with a playbook** → **New playbook**.
   3. Attach `Documents/Cowork/Playbooks/NDA/Spaarke NDA – Mutual v1.2.docx` **from OneDrive** (**+** › attach a
      cloud file), not from your PC.
   4. When Copilot shows the playbook card, choose **Save playbook**. (If it asks what to do with the file, choose
      **Extract playbook only**.)
   5. If an older saved playbook for the same document type is in the list, delete it: **[TO CONFIRM: the owner's
      route]**.
5. **Run setup in Cowork** (optional, recommended): open the Microsoft 365 Copilot app, switch to **Cowork**, and
   type `/accelerator-setup`. It checks your setup and saves your name and defaults in `Cowork/Profile/`.
6. **Custom instruction** (optional): open `Start here/Spaarke – Custom instruction.txt`, copy all of it, and paste
   it in Copilot › Settings › Personalization › Custom instructions, below anything you already have. Save. It only
   affects **Copilot chat and agents**: Word and Cowork don't read it.
7. **Agents** (advanced edition only, optional): build them from the two recipes in `Start here/`.

## 6. Using it in Word

**The one rule: start with `/`.** Copilot in Word uses your playbook only when you pick the skill and the playbook.
If you just type *review this NDA*, you get a general review that ignores your playbook.

Before you start, save a copy of the document under a new name (File › Save a copy) and work on the copy.

### Review a contract

1. Open the contract in Word for Windows. (In the browser: **Editing › Open in Desktop App**.)
2. In the Copilot pane, type `/` and choose **Review contract with a playbook** (or click that suggested prompt).
3. Pick the saved playbook **with the newest version in its name** (for example *Spaarke NDA – Mutual v1.2*).
   No saved playbook yet? Choose **New playbook** and attach the file from `Cowork/Playbooks/<type>/`.
4. Wait. A review takes about 3 to 5 minutes. Copilot lists each topic as acceptable, needs review or not
   acceptable, with a suggested edit.
5. To put the changes in the document: **Apply all › Apply edits with comments**. They appear as tracked changes
   with a comment on each, in your name.
6. Optional rated chart: in the same chat, attach `Cowork/Playbooks/Review method/Spaarke Review Method.docx` and
   type *Follow the attached review method to produce the topic-by-topic chart for the review you just ran.*

Good to know:
- A contract review takes **one** playbook and nothing else. Attaching a second file gives *"I can only use one
  attached file as a playbook"*.
- Don't add other plugins or connectors to the chat; they can take the request over and give a general answer.
- To see deletions struck through in the text rather than in balloons: Review › Show Markup › Balloons › Show All
  Revisions Inline.

### Review a document that is not a contract (for example a legal hold notice)

1. Open the notice. Switch the Copilot pane to agent mode and **Chat only**.
2. Attach `Cowork/Playbooks/Legal Hold Notice/Spaarke Legal Hold Notice – Employee Custodians v1.0.docx` and type
   *Review this notice against the attached playbook.* The review method is inside that file, so you get the rated
   table with 🟢🟡🔴.
3. You can attach more than one file here, such as a policy or the matter's facts.
4. To apply the changes: in the same chat, switch to **Allow editing** and ask for them as tracked changes with a
   comment on each.

### Draft a contract from an approved form

1. Open a blank document. Type `/` and choose **Draft new contract from a precedent**.
2. Attach your approved form (keep one per agreement type in `Cowork/Playbooks/<type>/Templates/`) and say who the
   other party is. Copilot drafts in the document.

### Draft a notice from a template

Open a blank document, agent mode, **Allow editing**. Attach the approved template **and** the matter's facts (for
example the demand letter) and ask Copilot to draft from the template using those facts. Use a template rather than
an old notice from another matter: names and dates from the old matter can carry over. Then review the draft as
above.

### Ask about a policy

Attach the policy (for example `Acme NDA Approval and Signature Authority Policy v1.0.docx`) and ask, for example,
*who must approve and sign this NDA?* Copilot answers from the policy and cites it.

## 7. Using it in Cowork

1. Open the Microsoft 365 Copilot app and switch to **Cowork**.
2. Type `/nda-review` (or `/legal-hold-review`) and attach or name the document. Unlike Word, Cowork finds the
   playbook in `Cowork/Playbooks/` by itself.
3. Cowork shows the review table and the list of changes **in the chat**, then one card asking you to approve. If the
   card offers email, choose **No email**.
4. After you approve, it builds the files: a redline with a comment on each change, a clean copy, and a summary
   page. The reply says where it saved them: **next to your original document, or in your Cowork task folder**
   (`Cowork/Tasks/…`).
5. To keep working on the redline with the Legal plugin, open it in **Word for Windows**.

Other Cowork skills: `/nda-library-review` reviews every NDA in a folder and gives one dashboard;
`/accelerator-setup` checks your setup.

## 8. Keeping playbooks current

- **The playbook file is the master.** When you receive a new version, extract the new package into `Documents`
  (replace the files). Cowork uses the new file straight away; agents within about 15 minutes.
- **Word does not.** A saved playbook is a copy taken when you saved it. For each new version: save the new file as
  a saved playbook (section 5, step 4) and **delete the old one**.
- The version is in every name (*v1.2*). If the saved playbook's version doesn't match the file in
  `Cowork/Playbooks/`, it is out of date.
- Keep only the current file. Copilot's search finds old copies anywhere in your OneDrive, so don't keep copies or
  old versions next to them, and don't rename the files.

## 9. Where things end up

| What | Where |
|---|---|
| Word review edits | Tracked changes and comments in the document you have open |
| Saved playbooks | Inside Copilot, not a file. List: `/` → Review contract with a playbook (and **View all**) |
| Word's *Create playbook* drafts | `Documents/Copilot/Created/` |
| Files you upload into a chat from your PC | A new copy in OneDrive **[TO CONFIRM folder]** |
| Cowork redline, clean copy, summary | Next to the original, or in `Cowork/Tasks/<task>/`. The reply names the place |
| Your profile | `Documents/Cowork/Profile/legal-profile.md` |

## 10. When something looks wrong

| You see | Why | Do this |
|---|---|---|
| A general review with no topics from your playbook | You didn't pick the skill and playbook | Start again with `/` → Review contract with a playbook |
| An old position (for example 3 years where the playbook says 2) | You picked an out-of-date saved playbook | Delete it and save the current file |
| No *Review contract* in Word | You're in Word in the browser | Editing › Open in Desktop App |
| *"I can only use one attached file as a playbook"* | Contract reviews take one file | Attach only the playbook |
| Copilot offers another plugin or connector | It matched a word in your prompt | Decline, and start again with `/` |
| Your custom instruction seems ignored in Word or Cowork | It applies only to Copilot chat and agents | Nothing to fix; the playbooks and skills carry the method |
| Cowork's card says "changes listed above" with nothing above | An old version of our skill | Extract the current package (fixed 2026-11-25) |

---

## Open items for the owner (not part of the guide)

1. **Deleting a saved playbook:** what is the route you found (View all › …)? The guide needs the clicks.
2. **"There were 24"** after Apply edits with comments: 24 topics or 24 comments?
3. **Custom skills in Word** (new panel, "your Skills folder"): when convenient, click *Skills folder* and note the
   path. If it is `Documents/Cowork/Skills/`, our skills may run in Word too, and section 6 changes.
4. **Script probe in Word desktop (WP-1)**: did you run *run the script probe*, and what did it answer?
5. **Where an upload from the PC lands**: the guide says `Microsoft Copilot Chat Files/`; I can check that in
   OneDrive if you want.
6. **Format**: keep this as the *Read me first* (Word file in `Start here/`), plus one-page quick cards per task?
