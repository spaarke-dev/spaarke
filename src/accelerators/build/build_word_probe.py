"""build_word_probe.py - a harmless two-skill plugin that shows whether a Copilot host loads and runs our skills.

Usage: <real python> build_word_probe.py [--out DIR]

Owner tests WP-0 to WP-2 (tier-test-script.md, "Word plugin tests"). Two skills, no legal content:
  spaarke-word-probe         instructions only. Its answer carries a code that appears only in the SKILL.md body,
                             so the code proves the host read the body, not just the description.
  spaarke-word-probe-script  must run scripts/probe_hash.py. The script prints a modular power no model can
                             work out in its head, so a matching value proves the script ran.
The expected script output is printed by this build and written to EXPECTED.txt beside the zip (not in it).
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
VERSION = "0.1.0"
INSTRUCTION_CODE = "WPI-1112-A"
SCRIPT_CODE = "WPS-1112-A"
GUARDRAILS = os.path.join(ROOT, "core", "shared-references", "guardrails.md")

BASE, EXPONENT, MODULUS = 7, 1_112_202_611, 1_000_000_007


def probe_value():
    """A modular power: easy for Python, not something a model can work out in its head."""
    return pow(BASE, EXPONENT, MODULUS)


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
                        "full": "Two harmless test skills. One answers from its instructions only; one runs a "
                                "small Python script. Used to test which Copilot apps load third-party skills. "
                                "Reviews nothing and changes nothing. Not legal advice."},
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
          f"Instructions probe: {INSTRUCTION_CODE}\nScript probe (starts with): {expected_line()}\n")
    return z


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", default=os.path.join(BUILD, "out", "word-probe"))
    a = ap.parse_args(argv)
    os.makedirs(a.out, exist_ok=True)
    z = build(a.out)
    print(f"WORD PROBE BUILD OK\n  {z}\n  expected script line starts with: {expected_line()}")


if __name__ == "__main__":
    main()
