"""Must-fire / must-not-fire controls for scripts/quality/quality-alert-banner.py.

Run: python -m unittest discover -s scripts/quality/tests
"""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest

HOOK = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "quality-alert-banner.py")


def load_hook():
    spec = importlib.util.spec_from_file_location("quality_alert_banner", HOOK)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


RED_TITLE = "\U0001F534 Quality alert: client-tests — café"
MOJIBAKE = "ðŸ"  # what a cp1252-decoded UTF-8 red-circle emoji starts with


def make_stub_gh(directory, stdout_json, exit_code=0):
    """A stub `gh` that prints UTF-8 JSON regardless of the console code page."""
    payload = os.path.join(directory, "payload.json")
    with open(payload, "w", encoding="utf-8") as f:
        f.write(stdout_json)
    impl = os.path.join(directory, "gh_stub.py")
    with open(impl, "w", encoding="utf-8") as f:
        f.write("import sys\n"
                f"sys.stdout.buffer.write(open({payload!r}, 'rb').read())\n"
                f"sys.exit({exit_code})\n")
    if os.name == "nt":
        with open(os.path.join(directory, "gh.cmd"), "w") as f:
            f.write(f'@"{sys.executable}" "{impl}" %*\r\n')
    else:
        shim = os.path.join(directory, "gh")
        with open(shim, "w") as f:
            f.write(f'#!/bin/sh\nexec "{sys.executable}" "{impl}" "$@"\n')
        os.chmod(shim, 0o755)


def run_refresh(cache, stub_dir):
    env = dict(os.environ)
    env["PATH"] = stub_dir + os.pathsep + env.get("PATH", "")
    return subprocess.run([sys.executable, "-c", load_hook().REFRESH_SCRIPT, cache],
                          capture_output=True, text=True, encoding="utf-8", env=env, timeout=60)


def run(cache):
    out = subprocess.run([sys.executable, HOOK, "--cache", cache, "--no-refresh"],
                         capture_output=True, text=True, encoding="utf-8", timeout=20)
    return out.returncode, out.stdout.strip()


class QualityAlertBanner(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.cache = os.path.join(self.dir.name, "alerts.json")

    def tearDown(self):
        self.dir.cleanup()

    def write(self, text):
        with open(self.cache, "w", encoding="utf-8") as f:
            f.write(text)

    def test_open_alerts_are_shown_to_user_and_agent(self):
        self.write(json.dumps([
            {"number": 12, "title": "Quality alert: client-tests", "updatedAt": "2026-10-13T07:00:00Z"},
            {"number": 11, "title": "Quality alert: mutation-nightly", "updatedAt": "2026-10-13T07:00:00Z"},
        ]))
        code, out = run(self.cache)
        self.assertEqual(code, 0)
        payload = json.loads(out)
        msg = payload["systemMessage"]
        self.assertIn("2 open quality alert(s)", msg)
        self.assertLess(msg.index("#11"), msg.index("#12"))  # sorted by number
        ctx = payload["hookSpecificOutput"]
        self.assertEqual(ctx["hookEventName"], "SessionStart")
        self.assertIn("#12 Quality alert: client-tests", ctx["additionalContext"])

    def test_no_open_alerts_prints_nothing(self):
        self.write("[]")
        self.assertEqual(run(self.cache), (0, ""))

    def test_missing_cache_prints_nothing(self):
        self.assertEqual(run(self.cache), (0, ""))

    def test_corrupt_cache_never_fails_the_session(self):
        self.write("not json")
        self.assertEqual(run(self.cache), (0, ""))

    def test_non_ascii_title_is_shown_in_banner(self):
        self.write(json.dumps([{"number": 7, "title": RED_TITLE, "updatedAt": "2026-10-13T07:00:00Z"}],
                              ensure_ascii=False))
        code, out = run(self.cache)
        self.assertEqual(code, 0)
        payload = json.loads(out)
        self.assertIn("#7 " + RED_TITLE, payload["systemMessage"])
        self.assertIn("#7 " + RED_TITLE, payload["hookSpecificOutput"]["additionalContext"])


class RefreshScript(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.stub_dir = os.path.join(self.dir.name, "bin")
        os.mkdir(self.stub_dir)
        self.cache = os.path.join(self.dir.name, "alerts.json")

    def tearDown(self):
        self.dir.cleanup()

    def test_utf8_title_round_trips_exactly_through_cache(self):
        alerts = [{"number": 7, "title": RED_TITLE, "updatedAt": "2026-10-13T07:00:00Z"}]
        make_stub_gh(self.stub_dir, json.dumps(alerts, ensure_ascii=False))
        r = run_refresh(self.cache, self.stub_dir)
        self.assertEqual(r.returncode, 0, r.stderr)
        with open(self.cache, encoding="utf-8") as f:
            self.assertEqual(json.load(f)[0]["title"], RED_TITLE)
        # and the banner built from that cache carries the emoji, not mojibake
        code, out = run(self.cache)
        self.assertEqual(code, 0)
        self.assertIn(RED_TITLE, json.loads(out)["systemMessage"])
        self.assertNotIn(MOJIBAKE, out)

    def test_refresh_leaves_no_temp_files(self):
        make_stub_gh(self.stub_dir, "[]")
        run_refresh(self.cache, self.stub_dir)
        self.assertEqual(os.listdir(self.dir.name).count("alerts.json"), 1)
        self.assertEqual([n for n in os.listdir(self.dir.name) if n.endswith(".tmp")], [])

    def test_failed_gh_keeps_previous_cache(self):
        with open(self.cache, "w", encoding="utf-8") as f:
            f.write('[{"number": 1, "title": "old", "updatedAt": "x"}]')
        make_stub_gh(self.stub_dir, "garbage", exit_code=1)
        run_refresh(self.cache, self.stub_dir)
        with open(self.cache, encoding="utf-8") as f:
            self.assertEqual(json.load(f)[0]["title"], "old")
        self.assertEqual([n for n in os.listdir(self.dir.name) if n.endswith(".tmp")], [])

    def test_refresh_script_uses_unique_tmp_and_no_window_flag(self):
        script = load_hook().REFRESH_SCRIPT
        self.assertIn("os.getpid()", script)
        self.assertIn("CREATE_NO_WINDOW", script)
        self.assertIn('encoding="utf-8"', script)


if __name__ == "__main__":
    unittest.main()
