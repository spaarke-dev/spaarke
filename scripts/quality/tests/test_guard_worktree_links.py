"""Must-fire / must-not-fire controls for scripts/quality/guard-worktree-links.py.

Builds a real temp repo with two worktrees so `git worktree list` is real.
Run: python -m unittest discover -s scripts/quality/tests
"""
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import unittest

HOOK = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "guard-worktree-links.py")


def git(cwd, *args):
    subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@example.com", *args], cwd=cwd, check=True,
                   capture_output=True, text=True)


def msys(path):
    """C:\\x\\y -> /c/x/y (Git Bash spelling); unchanged on other platforms."""
    m = re.match(r"^([A-Za-z]):[\\/](.*)$", path)
    return "/%s/%s" % (m.group(1).lower(), m.group(2).replace("\\", "/")) if m else path


def run(event, project_dir, raw=None):
    env = dict(os.environ, CLAUDE_PROJECT_DIR=project_dir)
    payload = raw if raw is not None else json.dumps(event)
    out = subprocess.run([sys.executable, HOOK], input=payload, capture_output=True, text=True, env=env, timeout=20)
    return out.returncode, out.stdout.strip()


def denied(stdout):
    return bool(stdout) and json.loads(stdout)["hookSpecificOutput"]["permissionDecision"] == "deny"


class GuardWorktreeLinks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        root = os.path.realpath(cls.tmp.name)
        cls.main = os.path.join(root, "repo")
        cls.wta = os.path.join(root, "wtA")
        cls.wtb = os.path.join(root, "wtB")
        cls.outside = os.path.join(root, "elsewhere")
        os.makedirs(cls.main)
        os.makedirs(cls.outside)
        git(cls.main, "init", "-q", "-b", "main")
        with open(os.path.join(cls.main, "f.txt"), "w") as fh:
            fh.write("x")
        git(cls.main, "add", "-A")
        git(cls.main, "commit", "-q", "-m", "init")
        git(cls.main, "worktree", "add", "-q", "-b", "a", cls.wta)
        git(cls.main, "worktree", "add", "-q", "-b", "b", cls.wtb)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def check(self, tool, command, expect_deny, cwd=None):
        event = {"tool_name": tool, "tool_input": {"command": command}, "cwd": cwd or self.wta}
        code, out = run(event, self.main)
        self.assertEqual(code, 0, command)
        self.assertEqual(denied(out), expect_deny, f"{command!r} -> {out!r}")
        if expect_deny:
            self.assertTrue(json.loads(out)["hookSpecificOutput"]["permissionDecisionReason"])
        else:
            self.assertEqual(out, "", command)

    def ps(self, command, expect_deny, cwd=None):
        self.check("PowerShell", command, expect_deny, cwd)

    def sh(self, command, expect_deny, cwd=None):
        self.check("Bash", command, expect_deny, cwd)

    # --- A: links into another worktree: must fire -----------------------------------------------------------
    def test_new_item_junction_arg_orders_and_quotes(self):
        a, b = self.wta, self.wtb
        link = os.path.join(a, "node_modules")
        tgt = os.path.join(b, "node_modules")
        for cmd in (
            f"New-Item -ItemType Junction -Path '{link}' -Target '{tgt}'",
            f"New-Item -Target {tgt} -Path {link} -ItemType Junction",
            f'New-Item -Path "{link}" -ItemType Junction -Value "{tgt}"',
            f"New-Item -ItemType SymbolicLink -Path {link} -Value {tgt}",
            f"New-Item -ItemType HardLink -Path {link} -Value {tgt}",
            f"New-Item -ItemType Junction -Path {a} -Name node_modules -Target {tgt}",
            f"New-Item {link} -ItemType Junction -Value {tgt}",
            f"ni -Type Junction -Path {link} -Target {tgt} -Force",
            f"cd {a}; New-Item -ItemType Junction -Path node_modules -Target {tgt}",
        ):
            with self.subTest(cmd=cmd):
                self.ps(cmd, True)

    def test_new_item_junction_forward_slash_and_via_bash_wrapper(self):
        link = os.path.join(self.wta, "node_modules").replace("\\", "/")
        tgt = os.path.join(self.wtb, "node_modules").replace("\\", "/")
        self.ps(f"New-Item -ItemType Junction -Path {link} -Target {tgt}", True)
        self.sh(f'powershell -NoProfile -Command "New-Item -ItemType Junction -Path {link} -Target {tgt}"', True)

    def test_mklink(self):
        link = os.path.join(self.wta, "node_modules")
        tgt = os.path.join(self.wtb, "node_modules")
        self.sh(f'cmd /c mklink /J "{link}" "{tgt}"', True)
        self.sh(f'cmd /c "mklink /D {link} {tgt}"', True)
        self.ps(f"cmd /c mklink /J {link} {tgt}", True)
        self.sh(f"mklink /H {link} {tgt}", True)

    def test_ln_s(self):
        link = os.path.join(self.wta, "nm")
        tgt = os.path.join(self.wtb, "node_modules")
        self.sh(f"ln -s {tgt} {link}", True)
        self.sh(f"ln -sf {msys(tgt)} {msys(link)}", True)
        self.sh(f"ln -s ../wtB/node_modules nm", True, cwd=self.wta)

    # --- A: must not fire ------------------------------------------------------------------------------------
    def test_link_inside_one_worktree_allowed(self):
        a = self.wta
        self.ps(f"New-Item -ItemType Junction -Path {a}\\x -Target {a}\\y", False)
        self.sh(f"ln -s {a}/y {a}/x", False)
        self.sh(f"cmd /c mklink /J {a}\\x {a}\\y", False)

    def test_link_to_folder_outside_all_worktrees_allowed(self):
        a, out = self.wta, self.outside
        self.ps(f"New-Item -ItemType Junction -Path {a}\\x -Target {out}", False)
        self.sh(f"ln -s {out} {a}/x", False)
        self.ps(f"New-Item -ItemType Junction -Path {out}\\x -Target {self.wtb}", False)
        self.sh(f"mklink /J {out}\\x {a}", False)

    def test_new_item_that_is_not_a_link_allowed(self):
        self.ps(f"New-Item -ItemType Directory -Path {self.wta}\\x", False)
        self.ps(f"New-Item -ItemType File -Path {self.wta}\\x.txt -Value {self.wtb}\\y", False)
        self.sh(f"ln {self.wtb}/f {self.wta}/f", False)       # hard link without -s is outside the rule

    # --- B: recursive delete / git worktree remove of a root: must fire --------------------------------------
    def test_rm_rf_root_in_all_spellings(self):
        b = self.wtb
        for cmd in (
            f"rm -rf {b}",
            f"rm -rf {b.replace(chr(92), '/')}",
            f"rm -rf {msys(b)}",
            f"rm -rf {b}/",
            f'rm -rf "{b}"',
            f"rm -fr {b}",
            f"rm -R {b}",
            f"rm --recursive --force {b}",
            f"rm -r -f -- {b}",
            f"cd /tmp && rm -rf {b}",
            f"rm -rf {b}/../wtB",
            f"rm -rf {self.main}",
        ):
            with self.subTest(cmd=cmd):
                self.sh(cmd, True)
        self.sh("rm -rf ../wtB", True, cwd=self.wta)

    def test_remove_item_recurse(self):
        b = self.wtb
        for cmd in (
            f"Remove-Item -Recurse -Force {b}",
            f"Remove-Item -Force -Recurse -LiteralPath '{b}'",
            f"Remove-Item -Path {b} -r",
            f"ri {b} -Recurse",
            f"rm -Recurse -Force {b}",
            f"Remove-Item {self.wta}, {b} -Recurse -ErrorAction SilentlyContinue",
            f"Remove-Item -Recurse -Force -ea SilentlyContinue {b}",
            f"rmdir /s /q {b}",
        ):
            with self.subTest(cmd=cmd):
                self.ps(cmd, True)
        self.sh(f"cmd /c rd /s /q {b}", True)
        self.sh(f'cmd /c "rmdir /s /q {b}"', True)
        self.sh(f'pwsh -c "Remove-Item -Recurse -Force {b}"', True)

    def test_git_worktree_remove(self):
        b = self.wtb
        self.sh(f"git worktree remove --force {b}", True)
        self.sh(f"git worktree remove {b}", True)
        self.sh(f"git -C {self.main} worktree remove -f -f {msys(b)}", True)
        self.ps(f"git worktree remove --force {b}", True)
        self.sh("git worktree remove ../wtB", True, cwd=self.wta)

    def test_claude_worktrees_container(self):
        c = os.path.join(self.main, ".claude", "worktrees")
        self.sh(f"rm -rf {c}", True)
        self.sh(f"rm -rf {msys(c)}", True)
        self.ps(f"Remove-Item -Recurse -Force {c}", True)

    # --- B: must not fire ------------------------------------------------------------------------------------
    def test_delete_inside_a_worktree_allowed(self):
        b = self.wtb
        self.sh(f"rm -rf {b}/publish", False)
        self.sh(f"rm -rf {msys(b)}/publish", False)
        self.sh(f"rm -rf {b}/node_modules", False)
        self.ps(f"Remove-Item -Recurse -Force {b}\\publish", False)
        self.ps(f"Remove-Item {b}/file.txt", False)
        self.ps(f"Remove-Item -Force {b}", False)                # not recursive
        self.sh(f"rm {b}/file.txt", False)
        self.sh(f"rm -f {b}", False)                              # not recursive
        self.sh(f"rm -rf {self.outside}", False)
        self.sh(f"rm -rf {os.path.join(self.main, '.claude', 'worktrees', 'other')}", False)
        self.sh("rm -rf publish", False, cwd=self.wtb)
        self.sh(f"git worktree remove {b}/sub", False)

    def test_safe_script_and_read_only_commands_allowed(self):
        b = self.wtb
        self.sh(f"pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path {b}", False)
        self.sh(f"pwsh -File scripts/worktree/Remove-WorktreeSafe.ps1 -Path {b} -Force", False)
        self.ps(f"pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path {b} -WhatIf", False)
        self.sh("git worktree list", False)
        self.sh("git worktree list --porcelain", False)
        self.sh(f"git -C {b} status --porcelain", False)
        self.sh(f"ls {b}", False)
        self.sh(f"echo rm -rf {b}", False)

    def test_unrelated_and_unsure_commands_allowed(self):
        for cmd in ("npm install --legacy-peer-deps --no-audit --no-fund", "dotnet build", "echo hi", "ln", "rm",
                    "rm -rf $TARGET", "rm -rf ~/x", "rm -rf *", "New-Item -ItemType Junction",
                    "New-Item -ItemType Junction -Path $p -Target $t",
                    "Remove-Item -Recurse -Force $wt", "git worktree remove", "mklink /J a",
                    "ln -s a b c", "rm -rf /c/definitely/not/a/worktree"):
            with self.subTest(cmd=cmd):
                self.sh(cmd, False)
                self.ps(cmd, False)

    def test_data_that_mentions_a_command_is_not_a_command(self):
        b = self.wtb
        nl = chr(10)
        self.sh(nl.join(["git commit -F - <<'EOF'", "fix: never", f"rm -rf {b}", "EOF"]), False)
        self.sh(f'git commit -m "do not rm -rf {b}"', False)
        self.sh(nl.join([f"# rm -rf {b}", "ls"]), False)
        self.sh(nl.join(["python - <<EOF", f"rm -rf {b}", "EOF", "ls"]), False)
        self.sh(nl.join(["cat <<'EOF'", "x", "EOF", f"rm -rf {b}"]), True)   # the command AFTER the body still counts

    def test_non_shell_tool_ignored(self):
        event = {"tool_name": "Edit", "tool_input": {"command": f"rm -rf {self.wtb}"}}
        code, out = run(event, self.main)
        self.assertEqual((code, out), (0, ""))
        event = {"tool_name": "Write", "tool_input": {"file_path": "x", "content": f"rm -rf {self.wtb}"}}
        self.assertEqual(run(event, self.main), (0, ""))

    def test_malformed_input_fails_open(self):
        for raw in ("", "not json", "{", "[]", "null", '{"tool_name": "Bash"}', '{"tool_name": "Bash", "tool_input": 5}',
                    '{"tool_name": "Bash", "tool_input": {"command": 7}}'):
            with self.subTest(raw=raw):
                self.assertEqual(run(None, self.main, raw=raw), (0, ""))

    def test_not_a_git_repo_fails_open(self):
        with tempfile.TemporaryDirectory() as plain:
            event = {"tool_name": "Bash", "tool_input": {"command": f"rm -rf {self.wtb}"}, "cwd": plain}
            self.assertEqual(run(event, plain), (0, ""))

    def test_latency_under_five_seconds(self):
        event = {"tool_name": "Bash", "tool_input": {"command": f"rm -rf {self.wtb}"}, "cwd": self.wta}
        start = time.time()
        run(event, self.main)
        self.assertLess(time.time() - start, 5)


if __name__ == "__main__":
    unittest.main()
