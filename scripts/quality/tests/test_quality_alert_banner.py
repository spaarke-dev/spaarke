"""Must-fire / must-not-fire controls for scripts/quality/quality-alert-banner.py.

Run: python -m unittest discover -s scripts/quality/tests
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

HOOK = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "quality-alert-banner.py")


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


if __name__ == "__main__":
    unittest.main()
