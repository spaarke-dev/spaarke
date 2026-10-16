"""Must-fire / must-not-fire controls for scripts/quality/guard-python3-stub.py.

The resolved `python3` path is injected with SPRK_PYTHON3_RESOLVES_TO, so these controls do not depend on the
machine running them (CI runs them on windows-latest).
Run: python -m unittest discover -s scripts/quality/tests
"""
import json
import os
import subprocess
import sys
import time
import unittest

HOOK = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "guard-python3-stub.py")
STUB = r"C:\Users\someone\AppData\Local\Microsoft\WindowsApps\python3.EXE"
REAL = "/usr/bin/python3"


def run(command, tool="Bash", resolves_to=STUB, raw=None):
    env = dict(os.environ, SPRK_PYTHON3_RESOLVES_TO=resolves_to)
    payload = raw if raw is not None else json.dumps({"tool_name": tool, "tool_input": {"command": command}})
    out = subprocess.run([sys.executable, HOOK], input=payload, capture_output=True, text=True, env=env, timeout=20)
    return out.returncode, out.stdout.strip()


def denied(stdout):
    return bool(stdout) and json.loads(stdout)["hookSpecificOutput"]["permissionDecision"] == "deny"


class MustFire(unittest.TestCase):
    CASES = [
        'python3 -c "print(1)"',
        "python3 - <<'EOF'\nprint(1)\nEOF",
        "python3 - C:/tmp/a.md C:/tmp/b.md",
        "cd /c/code_files/spaarke && python3 scripts/x.py",
        "echo '{}' | python3 -c 'import json,sys; print(json.load(sys.stdin))'",
        "FOO=1 BAR=2 python3 x.py",
        "timeout 5 python3 x.py",
        "x=$(python3 -V)",
        "python3.exe -V",
        "ls; python3 x.py",
        "  python3 x.py",
        "if true; then python3 x.py; fi",
        "echo 'x' && python3 x.py",
        'echo "it\'s" && python3 x.py',
        "git commit -m 'a\nb' && python3 x.py",
    ]

    def test_bash_commands_denied(self):
        for cmd in self.CASES:
            with self.subTest(cmd=cmd):
                code, out = run(cmd)
                self.assertEqual(code, 0)
                self.assertTrue(denied(out), out)

    def test_powershell_call_operator_denied(self):
        for cmd in ["& python3 x.py", "python3 -c \"print(1)\"", "$v = python3 -V"]:
            with self.subTest(cmd=cmd):
                code, out = run(cmd, tool="PowerShell")
                self.assertTrue(denied(out), out)

    def test_bash_string_assignment_allowed(self):
        self.assertEqual(run("PY=python3")[1], "")

    def test_reason_names_the_fix(self):
        _, out = run("python3 x.py")
        reason = json.loads(out)["hookSpecificOutput"]["permissionDecisionReason"]
        self.assertIn("Use `python` instead", reason)
        self.assertIn("G-22", reason)


class MustNotFire(unittest.TestCase):
    CASES = [
        'python -c "print(1)"',
        "python - <<'EOF'\nprint(1)\nEOF",
        'git commit -m "fix: lint hook skips the python3 Store stub"',
        "git commit -m 'never use python3 here'",
        "grep -n python3 scripts/quality/post-edit-lint.sh",
        "echo python3",
        "cat > notes.md <<'EOF'\nrun python3 x.py\nEOF",
        "ls python3-notes",
        "mypython3 x.py",
        "python3x --help",
        "# python3 x.py",
        "echo done # then python3",
        "py -3 x.py",
        "",
        # Multi-line quoted strings (review F1 of #2003): a line inside them is text, not a command.
        "git commit -m 'fix: hook\npython3 was the stub'",
        'git commit -m "fix: hook\n\nUse `python` not `python3`; see G-22."',
        'gh pr create --title "x" --body "Line one\npython3 at line start\nmore"',
        'git commit -m "don\'t run\npython3; it\'s the stub"',
        "command -v python3",
        "which python3",
    ]

    def test_powershell_multiline_string_and_block_comment_allowed(self):
        for cmd in ['git commit -m "fix: a\n\npython3 b"', "<#\npython3 x.py is bad\n#>\nWrite-Output hi"]:
            with self.subTest(cmd=cmd):
                self.assertEqual(run(cmd, tool="PowerShell")[1], "")

    def test_text_mentions_allowed(self):
        for cmd in self.CASES:
            with self.subTest(cmd=cmd):
                code, out = run(cmd)
                self.assertEqual(code, 0)
                self.assertEqual(out, "", cmd)

    def test_powershell_here_string_allowed(self):
        code, out = run("$body = @'\npython3 x.py\n'@\nWrite-Output $body", tool="PowerShell")
        self.assertEqual(out, "")

    def test_real_python3_allowed(self):
        for resolves in (REAL, r"C:\Python313\python3.exe", ""):
            with self.subTest(resolves=resolves):
                _, out = run("python3 - <<'EOF'\nprint(1)\nEOF", resolves_to=resolves)
                self.assertEqual(out, "")

    def test_other_tools_and_bad_input_allowed(self):
        self.assertEqual(run("python3 x.py", tool="Read")[1], "")
        self.assertEqual(run("", raw="not json")[1], "")
        self.assertEqual(run("", raw=json.dumps({"tool_name": "Bash", "tool_input": {}}))[1], "")


class Budget(unittest.TestCase):
    def test_fast(self):
        start = time.time()
        run("python3 x.py")
        self.assertLess(time.time() - start, 3.0)


if __name__ == "__main__":
    unittest.main()
