"""build_word_probe.py - a harmless three-skill plugin that shows whether a Copilot host loads and runs our skills.

Usage: <real python> build_word_probe.py [--out DIR]

Owner tests WP-0 to WP-2 and WP-5 (tier-test-script.md, "Word plugin tests"). Three skills, no legal content:
  spaarke-word-probe         instructions only. Its answer carries a code that appears only in the SKILL.md body,
                             so the code proves the host read the body, not just the description.
  spaarke-word-probe-script  must run scripts/probe_hash.py. The script prints a modular power no model can
                             work out in its head, so a matching value proves the script ran.
  spaarke-word-probe-js      instructions only, carrying fixed Office.js code for the host to run on the open
                             document (WP-5). Word desktop ran no skill scripts (WP-1, 2026-11-26) but says it runs
                             JavaScript on the open document. The code makes one tracked replacement and one
                             comment, both carrying an FNV-1a hash no model can work out in its head, so an exact
                             match shows our code ran as written.
  WP-6 (v0.6; v0.5 was refused at upload): three more instructions-only skills carrying fixed Word JavaScript in
  the runner's own form (`async function main(context) { ... }`, seen in WP-5 run 3):
  spaarke-word-probe-edits   Copilot fills ONE data line (`const EDITS = [...]`) from the prompt; the fixed code
                             checks each edit, applies it as a tracked change with a hashed comment (WX-5).
  spaarke-word-probe-error   writes nothing; logs one hash and throws an error carrying another, to learn whether
                             errors or console output reach Copilot (WX-6).
  spaarke-word-probe-size-*  the same small job padded with data to about 5, 10 and 17 KB of code (17 KB is the
                             most a SKILL.md under 20,000 characters can carry; the upload refused 22k and 63k), to find the
                             runner's size limit (WX-4).
The expected outputs are printed by this build and written to EXPECTED.txt beside the zip (not in it).
Stdlib only; no network, no model calls.
"""
import argparse
import json
import os
import shutil
import sys
import uuid

BUILD = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(BUILD)
sys.path.insert(0, BUILD)

import gates  # noqa: E402
from build_accelerator import make_icons, write, zip_dir  # noqa: E402

PACKAGE = "spaarke-word-probe"
VERSION = "0.7.0"   # the probe's label (zip name). v0.5 and v0.6 were refused by the admin-center upload
# The manifest version must not start with "0": Microsoft's validation rules refuse it (atk validate, 2026-11-27), and
# the admin-center upload refused v0.5/v0.6 (0.x) while admin-deployed spaarke-decisions (1.0.0) installed.
MANIFEST_VERSION = "1." + VERSION.split(".")[1] + ".0"
# Word's code runner refuses code that uses these (WP-5 run 1, 2026-11-26: "rejected ... because it uses
# toString"). The others are guesses at the same denylist; the probe avoids them all.
JS_FORBIDDEN = ("toString", "padStart", "eval", "Function", "constructor", "prototype", "__proto__",
                "fetch", "XMLHttpRequest", "import", "require")
INSTRUCTION_CODE = "WPI-1112-A"
SCRIPT_CODE = "WPS-1112-A"
JS_CODE = "WPJ-1126-A"
EDITS_CODE = "WPE-1127-A"
ERROR_CODE = "WPX-1127-A"
SIZE_CODE = "WPZ-1127-A"
SIZES_KB = (5, 10, 17)   # 17 KB: the most code a SKILL.md under 20,000 characters can carry
GUARDRAILS = os.path.join(ROOT, "core", "shared-references", "guardrails.md")

BASE, EXPONENT, MODULUS = 7, 1_112_202_611, 1_000_000_007


def probe_value():
    """A modular power: easy for Python, not something a model can work out in its head."""
    return pow(BASE, EXPONENT, MODULUS)


def fnv1a32(text):
    """32-bit FNV-1a, the same arithmetic as the JavaScript in JS_PROBE (Math.imul, then >>> 0)."""
    h = 0x811C9DC5
    for ch in text:
        h ^= ord(ch)
        h = (h * 0x01000193) & 0xFFFFFFFF
    return f"{h:08x}"


def js_marker():
    return f"{JS_CODE} {fnv1a32(JS_CODE + ' Spaarke')}"


# Office.js (Word API 1.4+): add a plain paragraph, then with tracking on replace one word and comment on it,
# then put the tracking mode back. The paragraph is added with tracking OFF so the replacement is an ordinary
# tracked deletion + insertion of existing text, the way a redline edits an agreement.
JS_PROBE = f"""Word.run(async (context) => {{
  const doc = context.document;
  doc.load("changeTrackingMode");
  await context.sync();
  const before = doc.changeTrackingMode;
  let h = 0x811c9dc5;
  for (const ch of "{JS_CODE} Spaarke") {{ h ^= ch.charCodeAt(0); h = Math.imul(h, 0x01000193) >>> 0; }}
  const digits = "0123456789abcdef";
  let hex = "";
  for (let i = 28; i >= 0; i -= 4) {{ hex += digits[(h >>> i) & 15]; }}
  const marker = "{JS_CODE} " + hex;
  doc.changeTrackingMode = Word.ChangeTrackingMode.off;
  const p = doc.body.insertParagraph("Spaarke JavaScript probe: alpha bravo delta. " + marker, "End");
  await context.sync();
  const hits = p.search("bravo", {{ matchCase: true, matchWholeWord: true }});
  hits.load("items");
  await context.sync();
  doc.changeTrackingMode = Word.ChangeTrackingMode.trackAll;
  const r = hits.items[0].insertText("charlie", "Replace");
  r.insertComment("Spaarke JS probe comment " + marker);
  await context.sync();
  doc.changeTrackingMode = before;
  await context.sync();
  return "Spaarke JS probe ran: " + marker;
}});"""

# ---- WP-6 (v0.6) -------------------------------------------------------------------------------------------
# Written in the form Word's runner used in WP-5 run 3, so no wrapper change should be needed. The FNV-1a helper
# sits inside main() so each skill's code is one function, as the runner expects.
JS_FNV = """  function fnv(s) {
    let h = 0x811c9dc5;
    for (const ch of s) { h ^= ch.charCodeAt(0); h = Math.imul(h, 0x01000193) >>> 0; }
    const digits = "0123456789abcdef";
    let hex = "";
    for (let i = 28; i >= 0; i -= 4) { hex += digits[(h >>> i) & 15]; }
    return hex;
  }"""

# WX-5: the one line Copilot fills; everything else is fixed. A real Word edition would work the same way: the
# model decides the edits, fixed code checks and applies them.
EDITS_LINE = "  const EDITS = [];"
EDITS_TEXT = "The term is two (2) years. Governing law is Delaware. Notice is 30 days."
EXPECTED_EDITS = (("two (2) years", "three (3) years"), ("Delaware", "New York"), ("30 days", "60 days"))

JS_EDITS = f"""async function main(context) {{
{EDITS_LINE}
  // Everything below stays exactly as written.
{JS_FNV}
  const tag = "{EDITS_CODE}";
  const doc = context.document;
  doc.load("changeTrackingMode");
  await context.sync();
  const before = doc.changeTrackingMode;
  doc.changeTrackingMode = Word.ChangeTrackingMode.off;
  const p = doc.body.insertParagraph("Spaarke edit probe: {EDITS_TEXT}", "End");
  await context.sync();
  const fail = async (why) => {{
    doc.changeTrackingMode = before;
    await context.sync();
    throw new Error("Spaarke edit probe " + tag + ": " + why);
  }};
  if (!Array.isArray(EDITS) || EDITS.length === 0) {{ await fail("EDITS is empty"); }}
  const found = [];
  for (const e of EDITS) {{
    const hits = p.search(e.find, {{ matchCase: true }});
    hits.load("items");
    found.push(hits);
  }}
  await context.sync();
  for (let i = 0; i < EDITS.length; i++) {{
    if (found[i].items.length !== 1) {{ await fail("edit " + (i + 1) + " matched " + found[i].items.length + " times"); }}
  }}
  doc.changeTrackingMode = Word.ChangeTrackingMode.trackAll;
  let all = tag;
  for (let i = 0; i < EDITS.length; i++) {{
    const e = EDITS[i];
    const h = fnv(tag + "|" + e.find + "|" + e.replace);
    all += "|" + h;
    const r = found[i].items[0].insertText(e.replace, "Replace");
    r.insertComment("Spaarke edit " + (i + 1) + "/" + EDITS.length + " " + tag + " " + h);
  }}
  await context.sync();
  doc.changeTrackingMode = before;
  const mark = tag + " " + EDITS.length + " " + fnv(all);
  p.insertText(" [" + mark + "]", "End");
  await context.sync();
  return "Spaarke edit probe ran: " + mark;
}}"""


def edit_hashes(edits=EXPECTED_EDITS):
    """(per-edit comment hashes, final mark) that JS_EDITS produces for these edits."""
    per = [fnv1a32(f"{EDITS_CODE}|{f}|{r}") for f, r in edits]
    return per, f"{EDITS_CODE} {len(edits)} {fnv1a32(EDITS_CODE + ''.join('|' + h for h in per))}"


# WX-6: writes nothing. Two different hashes, one per channel, so the reply shows which channel reached Copilot.
JS_ERROR = f"""async function main(context) {{
{JS_FNV}
  const tag = "{ERROR_CODE}";
  console.log("Spaarke probe log " + tag + " " + fnv(tag + " log"));
  throw new Error("Spaarke probe error " + tag + " " + fnv(tag + " error"));
}}"""


def error_lines():
    return (f"Spaarke probe error {ERROR_CODE} {fnv1a32(ERROR_CODE + ' error')}",
            f"Spaarke probe log {ERROR_CODE} {fnv1a32(ERROR_CODE + ' log')}")


# WX-4: the same small job behind a data list sized to about 5, 20 or 60 KB. The checksum is order-sensitive, so
# a shortened or reordered list gives a different hash. Note: Copilot re-types the code into its tool call, so the
# size limit is also a limit on what the model will copy out verbatim.
def pad_numbers(kb):
    x, nums, size = 20261127 + kb, [], 0
    while size < kb * 1024 - 900:      # leave room for the fixed lines around the list
        x = (x * 1103515245 + 12345) % 2 ** 31
        nums.append(x)
        size += len(str(x)) + 2 + (4 if len(nums) % 10 == 1 else 0)
    return nums


def js_size(kb):
    nums = pad_numbers(kb)
    rows = ",\n".join("    " + ", ".join(str(n) for n in nums[i:i + 10]) for i in range(0, len(nums), 10))
    return f"""async function main(context) {{
  const PAD = [
{rows}
  ];
{JS_FNV}
  let s = 0;
  for (const n of PAD) {{ s = (s * 31 + n) % 1000000007; }}
  const tag = "{SIZE_CODE}";
  const mark = tag + " {kb}k " + PAD.length + " " + fnv(tag + " " + PAD.length + " " + s);
  context.document.body.insertParagraph("Spaarke size probe: " + mark, "End");
  await context.sync();
  return "Spaarke size probe ran: " + mark;
}}"""


def size_mark(kb):
    nums, s = pad_numbers(kb), 0
    for n in nums:
        s = (s * 31 + n) % 1_000_000_007
    return f"{SIZE_CODE} {kb}k {len(nums)} {fnv1a32(f'{SIZE_CODE} {len(nums)} {s}')}"


WRAPPER_RULE = ("Run the code below on the open document **exactly as written**: do not change, shorten, reformat "
                "or rewrite any part of it, and do not run any other code before or after it. One exception: if "
                "your code runner needs a different outer form, you may change **only the first line and the last "
                "line** to fit it, and then quote both changed lines exactly.")

PROBE_SCRIPT = f'''"""Print the Spaarke Word probe line. Built-ins only; reads and writes nothing."""
import platform
import sys

value = pow({BASE}, {EXPONENT}, {MODULUS})
print(f"{SCRIPT_CODE} {{value}} python {{sys.version_info.major}}.{{sys.version_info.minor}} {{platform.system()}}")
'''

SKILLS = {
    "spaarke-word-probe": (
        "Test probe for Spaarke. Answers the phrase \"run the Spaarke Word probe\" with a fixed test line and "
        "the headings of the open document. Use for \"run the Spaarke Word probe\", \"Spaarke probe test\". "
        "Do NOT use for reviewing, editing or drafting any document, or for any other request.",
        f"""# Spaarke Word probe (instructions only)

A test of whether this host loads a third-party skill's instructions. It reviews nothing and changes nothing.
This is not legal advice. Guardrails: `references/guardrails.md`.

When the user asks to run the Spaarke Word probe:
1. Reply first with this exact line: `Spaarke Word probe · instructions loaded · {INSTRUCTION_CODE}`
2. Then list the headings of the open document, numbered, or say "No document is open".
3. Then say which app you are running in (Word, Cowork, Copilot Chat or other).

Do not edit the document, create files or search the web.
"""),
    "spaarke-word-probe-script": (
        "Test probe for Spaarke. Answers the phrase \"run the Spaarke script probe\" by running a small Python "
        "script and reporting its output line. Use for \"run the Spaarke script probe\", \"Spaarke script "
        "test\". Do NOT use for reviewing, editing or drafting any document, or for any other request.",
        f"""# Spaarke script probe (runs a Python script)

A test of whether this host runs a third-party skill's scripts. It reviews nothing and changes nothing.
This is not legal advice. Guardrails: `references/guardrails.md`.

When the user asks to run the Spaarke script probe:
1. Run `python scripts/probe_hash.py` and copy the line it prints, exactly, in a code block.
2. If you cannot run Python here, reply with exactly `Spaarke script probe · scripts not run · {SCRIPT_CODE}`
   and say why. Never write the output line yourself: a made-up value fails the test.
3. Then say which app you are running in (Word, Cowork, Copilot Chat or other).

Do not edit the document, create files or search the web.
"""),
    "spaarke-word-probe-js": (
        "Test probe for Spaarke. Answers the phrase \"run the Spaarke JavaScript probe\" by running a fixed piece "
        "of Word JavaScript that adds one test paragraph with one tracked change and one comment. Use for \"run "
        "the Spaarke JavaScript probe\", \"Spaarke JS probe\". Do NOT use for reviewing, editing or drafting any "
        "document, or for any other request.",
        f"""# Spaarke JavaScript probe (runs fixed Word JavaScript)

A test of whether this host runs JavaScript supplied by a third-party skill exactly as written. It adds one test
paragraph at the end of the open document; it reviews nothing. Run it only in a blank test document.
This is not legal advice. Guardrails: `references/guardrails.md`.

When the user asks to run the Spaarke JavaScript probe:
1. Run the code below on the open document **exactly as written**: do not change, shorten, reformat or rewrite
   any part of it, and do not run any other code before or after it. One exception: if your code runner needs
   a different outer form (for example it already provides `context`, wants a function body, or needs
   `await`/`return` in front), you may change **only the first line and the last line** to fit it. Everything
   between them stays exactly as written. Then quote both changed lines exactly.
2. Reply with the line the code returns, exactly, in a code block. If nothing is returned, quote the last
   paragraph of the document instead. Say whether you changed anything in the code, and what.
3. If you cannot run JavaScript here, reply with exactly `Spaarke JS probe · code not run · {JS_CODE}` and say
   why. Never write the returned line yourself, and never add the paragraph, change or comment any other way:
   either fails the test.
4. Then say which app you are running in (Word, Cowork, Copilot Chat or other).

```javascript
{JS_PROBE}
```

Do not create files or search the web.
"""),
    "spaarke-word-probe-edits": (
        "Test probe for Spaarke. Answers \"run the Spaarke edit probe\" followed by a list of find-and-replace "
        "edits, by running fixed Word JavaScript that adds one test paragraph and applies those edits to it as "
        "tracked changes with comments. Use for \"run the Spaarke edit probe\". Do NOT use for reviewing, editing "
        "or drafting any real document, or for any other request.",
        f"""# Spaarke edit probe (fixed Word JavaScript, one data line filled in)

A test of whether this host runs fixed JavaScript from a third-party skill when you fill in one line of data. It
adds one test paragraph at the end of the open document; it reviews nothing. Run it only in a blank test document.
This is not legal advice. Guardrails: `references/guardrails.md`.

When the user asks to run the Spaarke edit probe with a list of edits:
1. Fill in the `EDITS` line, and nothing else. Replace `const EDITS = [];` with
   `const EDITS = [{{ find: "...", replace: "..." }}, ...];`: one entry per edit, in the user's order, with the
   text copied exactly as the user wrote it (no added or changed quotes, spaces or punctuation).
2. {WRAPPER_RULE} Filling in the `EDITS` line is the only other change allowed.
3. Reply with the line the code returns, exactly, in a code block. If nothing comes back, quote the last paragraph
   of the document instead. If the code stops with an error, quote the error message exactly; do not fix the code
   or run it again. Also quote the `EDITS` line you used, and say whether you changed anything else.
4. If you cannot run JavaScript here, reply with exactly `Spaarke edit probe · code not run · {EDITS_CODE}` and
   say why. Never make the edits, comments or the returned line any other way: that fails the test.
5. Then say which app you are running in (Word, Cowork, Copilot Chat or other).

```javascript
{JS_EDITS}
```

Do not create files or search the web.
"""),
    "spaarke-word-probe-error": (
        "Test probe for Spaarke. Answers \"run the Spaarke error probe\" by running fixed Word JavaScript that "
        "writes nothing, prints one line and then stops with a deliberate error, and reporting exactly what came "
        "back. Use for \"run the Spaarke error probe\". Do NOT use for reviewing, editing or drafting any "
        "document, or for any other request.",
        f"""# Spaarke error probe (fixed Word JavaScript that fails on purpose)

A test of what this host passes back to you from code it runs: errors, printed output, return values. The code
changes nothing in the document. This is not legal advice. Guardrails: `references/guardrails.md`.

When the user asks to run the Spaarke error probe:
1. {WRAPPER_RULE}
2. The code is meant to fail. Do not fix it, change it or run it again.
3. Reply with these three things, each exactly as you received it, each in its own code block:
   - the error message;
   - any printed (console) output;
   - any return value.
   For any of them you did not receive, write "not received". Never write any of them yourself: a made-up value
   fails the test.
4. If you cannot run JavaScript here, reply with exactly `Spaarke error probe · code not run · {ERROR_CODE}` and
   say why.
5. Then say which app you are running in (Word, Cowork, Copilot Chat or other).

```javascript
{JS_ERROR}
```

Do not create files or search the web.
"""),
}

for _kb in SIZES_KB:
    SKILLS[f"spaarke-word-probe-size-{_kb}k"] = (
        f"Test probe for Spaarke. Answers \"run the Spaarke size probe {_kb}k\" by running a long piece of fixed "
        f"Word JavaScript (about {_kb} KB) that adds one test paragraph. Use for \"run the Spaarke size probe "
        f"{_kb}k\" only. Do NOT use for reviewing, editing or drafting any document, or for any other request.",
        f"""# Spaarke size probe, about {_kb} KB of fixed Word JavaScript

A test of how much code this host will run from a third-party skill. The code is long on purpose: a list of
numbers, then a few lines that add one test paragraph at the end of the open document. Run it only in a blank test
document. This is not legal advice. Guardrails: `references/guardrails.md`.

When the user asks to run the Spaarke size probe {_kb}k:
1. {WRAPPER_RULE} Do not shorten the list of numbers: every number counts.
2. Reply with the line the code returns, exactly, in a code block. If nothing comes back, quote the last paragraph
   of the document instead. If the runner refuses the code or it fails, quote the message exactly.
3. If you cannot run JavaScript here, reply with exactly `Spaarke size probe · code not run · {SIZE_CODE} {_kb}k`
   and say why. Never write the returned line or the paragraph yourself: that fails the test.
4. Then say which app you are running in (Word, Cowork, Copilot Chat or other).

```javascript
{js_size(_kb)}
```

Do not create files or search the web.
""")


def expected_line():
    return f"{SCRIPT_CODE} {probe_value()}"


def build(out):
    stage = os.path.join(out, "plugin")
    if os.path.exists(stage):
        shutil.rmtree(stage)
    os.makedirs(os.path.join(stage, "skills"))
    for name, (desc, body) in SKILLS.items():
        d = os.path.join(stage, "skills", name)
        os.makedirs(os.path.join(d, "references"))
        shutil.copy2(GUARDRAILS, os.path.join(d, "references", "guardrails.md"))
        write(os.path.join(d, "SKILL.md"),
              f"---\nname: {name}\ndescription: {desc}\nlicense: Proprietary\n---\n\n{body}")
        if name.endswith("-script"):
            os.makedirs(os.path.join(d, "scripts"))
            write(os.path.join(d, "scripts", "probe_hash.py"), PROBE_SCRIPT)
        problems = gates.lint_skill(d, set())
        if problems:
            raise SystemExit(f"{name}: " + "; ".join(f"{r}: {m}" for r, m in problems))
    make_icons(stage)
    manifest = {
        "$schema": "https://developer.microsoft.com/json-schemas/teams/v1.30/MicrosoftTeams.schema.json",
        "manifestVersion": "1.30",
        "version": MANIFEST_VERSION,
        "id": str(uuid.uuid5(uuid.NAMESPACE_URL, f"https://spaarke.com/accelerators/{PACKAGE}")),
        "developer": {"name": "Spaarke", "websiteUrl": "https://spaarke.com", "privacyUrl": "https://spaarke.com",
                      "termsOfUseUrl": "https://spaarke.com"},
        "name": {"short": "Spaarke Word Probe", "full": "Spaarke Word Probe (test only)"},
        "description": {"short": "Test only: shows whether this app loads and runs Spaarke skills.",
                        "full": "Harmless test skills. One answers from its instructions only; one runs a small "
                                "Python script; the others run fixed Word JavaScript that adds a test paragraph "
                                "with tracked changes and comments, reports a deliberate error, or tests how "
                                "much code runs. Used to test which Copilot apps load and run third-party skills. "
                                "Reviews nothing. Not legal advice."},
        "icons": {"color": "color.png", "outline": "outline.png"},
        "accentColor": "#1F3864",
        "agentSkills": [{"folder": f"./skills/{n}"} for n in SKILLS],
    }
    write(os.path.join(stage, "manifest.json"), json.dumps(manifest, indent=2))
    problems = gates.lint_plugin(os.path.join(stage, "manifest.json"))
    if problems:
        raise SystemExit("plugin gate: " + "; ".join(f"{r}: {m}" for r, m in problems))
    z = zip_dir(stage, os.path.join(out, f"Spaarke Word probe plugin v{VERSION[:3]}.zip"))
    per, mark = edit_hashes()
    edits = "; ".join(f"'{f}' -> '{r}'" for f, r in EXPECTED_EDITS)
    err, log = error_lines()
    write(os.path.join(out, "EXPECTED.txt"),
          f"Instructions probe: {INSTRUCTION_CODE}\nScript probe (starts with): {expected_line()}\n"
          f"JavaScript probe returns: Spaarke JS probe ran: {js_marker()}\n"
          f"JavaScript probe document: last paragraph 'Spaarke JavaScript probe: alpha [bravo deleted][charlie "
          f"inserted] delta. {js_marker()}', one comment on 'charlie': 'Spaarke JS probe comment {js_marker()}'\n"
          f"\nWP-6 (v{VERSION[:3]})\n"
          f"Edit probe, prompt edits: {edits}\n"
          f"  returns / paragraph ends: [{mark}]\n"
          + "".join(f"  comment {i}: 'Spaarke edit {i}/3 {EDITS_CODE} {h}' on '{r}'\n"
                    for i, (h, (_, r)) in enumerate(zip(per, EXPECTED_EDITS), 1))
          + f"Error probe: error message '{err}'; console '{log}'; no document change\n"
          + "".join(f"Size probe {kb}k ({len(js_size(kb)):,} chars of code): paragraph 'Spaarke size probe: "
                    f"{size_mark(kb)}'\n" for kb in SIZES_KB))
    return z


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", default=os.path.join(BUILD, "out", "word-probe"))
    a = ap.parse_args(argv)
    os.makedirs(a.out, exist_ok=True)
    z = build(a.out)
    print(f"WORD PROBE BUILD OK\n  {z}\n  expected script line starts with: {expected_line()}\n"
          f"  expected JavaScript marker: {js_marker()}\n  expected edit mark: {edit_hashes()[1]}\n"
          f"  sizes: " + ", ".join(f"{kb}k={len(js_size(kb)):,} chars" for kb in SIZES_KB))


if __name__ == "__main__":
    main()
