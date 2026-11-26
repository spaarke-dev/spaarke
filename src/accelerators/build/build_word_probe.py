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
VERSION = "0.4.0"
# Word's code runner refuses code that uses these (WP-5 run 1, 2026-11-26: "rejected ... because it uses
# toString"). The others are guesses at the same denylist; the probe avoids them all.
JS_FORBIDDEN = ("toString", "padStart", "eval", "Function", "constructor", "prototype", "__proto__",
                "fetch", "XMLHttpRequest", "import", "require")
INSTRUCTION_CODE = "WPI-1112-A"
SCRIPT_CODE = "WPS-1112-A"
JS_CODE = "WPJ-1126-A"
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
}


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
        "version": VERSION,
        "id": str(uuid.uuid5(uuid.NAMESPACE_URL, f"https://spaarke.com/accelerators/{PACKAGE}")),
        "developer": {"name": "Spaarke", "websiteUrl": "https://spaarke.com", "privacyUrl": "https://spaarke.com",
                      "termsOfUseUrl": "https://spaarke.com"},
        "name": {"short": "Spaarke Word Probe", "full": "Spaarke Word Probe (test only)"},
        "description": {"short": "Test only: shows whether this app loads and runs Spaarke skills.",
                        "full": "Three harmless test skills. One answers from its instructions only; one runs a "
                                "small Python script; one runs fixed Word JavaScript that adds a test paragraph "
                                "with one tracked change and one comment. Used to test which Copilot apps load "
                                "and run third-party skills. Reviews nothing. Not legal advice."},
        "icons": {"color": "color.png", "outline": "outline.png"},
        "accentColor": "#1F3864",
        "agentSkills": [{"folder": f"./skills/{n}"} for n in SKILLS],
    }
    write(os.path.join(stage, "manifest.json"), json.dumps(manifest, indent=2))
    problems = gates.lint_plugin(os.path.join(stage, "manifest.json"))
    if problems:
        raise SystemExit("plugin gate: " + "; ".join(f"{r}: {m}" for r, m in problems))
    z = zip_dir(stage, os.path.join(out, f"Spaarke Word probe plugin v{VERSION[:3]}.zip"))
    write(os.path.join(out, "EXPECTED.txt"),
          f"Instructions probe: {INSTRUCTION_CODE}\nScript probe (starts with): {expected_line()}\n"
          f"JavaScript probe returns: Spaarke JS probe ran: {js_marker()}\n"
          f"JavaScript probe document: last paragraph 'Spaarke JavaScript probe: alpha [bravo deleted][charlie "
          f"inserted] delta. {js_marker()}', one comment on 'charlie': 'Spaarke JS probe comment {js_marker()}'\n")
    return z


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", default=os.path.join(BUILD, "out", "word-probe"))
    a = ap.parse_args(argv)
    os.makedirs(a.out, exist_ok=True)
    z = build(a.out)
    print(f"WORD PROBE BUILD OK\n  {z}\n  expected script line starts with: {expected_line()}\n"
          f"  expected JavaScript marker: {js_marker()}")


if __name__ == "__main__":
    main()
