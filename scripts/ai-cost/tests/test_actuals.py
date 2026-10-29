"""Invoiced actuals (spend-report.py --actuals): the invoice list, billing cycles, months and the allocation
of each cycle's invoices to projects. Pure functions plus two CLI runs against a sandbox home.

Run: python -m unittest discover -s scripts/ai-cost/tests
"""
import os
import tempfile
import unittest

import helpers

sr = helpers.load("spend-report.py", "spend_report_actuals")


def inv(date, amount, kind="credits"):
    return {"date": date, "amount": amount, "kind": kind, "invoice_id": "", "notes": ""}


def arow(day, project, cost):
    r = dict.fromkeys(sr.CSV_COLUMNS, 0)
    r.update(day=day, project=project, model="m", kind="main", tz="local")
    r.update({k: 0.0 for k in sr.COST_FIELDS})
    r["cost_output"] = cost
    return r


def write(path, text):
    with open(path, "w", encoding="utf-8", newline="") as fh:
        fh.write(text)


class ReadInvoices(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = os.path.join(self.tmp.name, "invoices.csv")

    def test_parses_sorts_and_accepts_dollar_signs_and_commas(self):
        write(self.path, "date,amount,kind,invoice_id,notes\n"
                         "2026-10-06,200,plan,B-0149,\n2026-10-02,\"$1,225.99\",Credits,,\n2026-10-03,-5,other,,refund\n")
        rows = sr.read_invoices(self.path)
        self.assertEqual([(r["date"], r["amount"], r["kind"]) for r in rows],
                         [("2026-10-02", 1225.99, "credits"), ("2026-10-03", -5.0, "other"), ("2026-10-06", 200.0, "plan")])
        self.assertEqual(rows[2]["invoice_id"], "B-0149")

    def test_missing_file_is_empty(self):
        self.assertEqual(sr.read_invoices(self.path), [])

    def test_malformed_rows_are_refused(self):
        for body in ("date,amount\n2026-10-02,5\n",                       # no kind column
                     "date,amount,kind\n10/02/2026,5,credits\n",          # not ISO
                     "date,amount,kind\n2026-10-02,five,credits\n",
                     "date,amount,kind\n2026-10-02,5,refund\n"):          # unknown kind
            write(self.path, body)
            with self.assertRaises(sr.ArchiveError, msg=body):
                sr.read_invoices(self.path)


class Periods(unittest.TestCase):
    def test_each_plan_charge_starts_a_cycle_and_the_last_runs_to_until(self):
        invoices = [inv("2026-08-06", 200, "plan"), inv("2026-08-20", 90), inv("2026-09-06", 200, "plan")]
        self.assertEqual(sr.billing_cycles(invoices, "2026-09-20"),
                         [("2026-08-06", "2026-09-05"), ("2026-09-06", "2026-09-20")])

    def test_charges_before_the_first_plan_charge_get_a_leading_cycle(self):
        invoices = [inv("2026-07-30", 90), inv("2026-08-06", 200, "plan")]
        self.assertEqual(sr.billing_cycles(invoices, "2026-08-10"),
                         [("2026-07-30", "2026-08-05"), ("2026-08-06", "2026-08-10")])

    def test_no_plan_charge_is_one_cycle(self):
        self.assertEqual(sr.billing_cycles([inv("2026-08-02", 90), inv("2026-08-09", 90)], "2026-08-09"),
                         [("2026-08-02", "2026-08-09")])

    def test_months_roll_over_the_year_and_stop_at_until(self):
        self.assertEqual(sr.calendar_months("2025-12-15", "2026-02-10"),
                         [("2025-12", "2025-12-01", "2025-12-31"), ("2026-01", "2026-01-01", "2026-01-31"),
                          ("2026-02", "2026-02-01", "2026-02-10")])

    def test_coverage(self):
        self.assertEqual(sr.coverage("2026-08-06", "2026-09-05", "2026-08-11"), "partial")
        self.assertEqual(sr.coverage("2026-09-06", "2026-10-05", "2026-08-11"), "full")
        self.assertEqual(sr.coverage("2026-07-06", "2026-08-05", "2026-08-11"), "none")
        self.assertEqual(sr.coverage("2026-07-06", "2026-08-05", None), "none")


class Allocate(unittest.TestCase):
    def setUp(self):
        self.invoices = [inv("2026-07-06", 200, "plan"), inv("2026-07-10", 100),              # before the archive
                         inv("2026-08-06", 200, "plan"), inv("2026-08-10", 400),              # cycle 2: 600
                         inv("2026-09-06", 200, "plan"), inv("2026-09-08", 100)]              # cycle 3: 300
        self.arows = [arow("2026-08-11", "alpha", 300), arow("2026-08-20", "beta", 100),
                      arow("2026-09-07", "alpha", 50), arow("2026-09-08", "beta", 0)]
        self.cycles = sr.billing_cycles(self.invoices, "2026-09-08")

    def test_split_by_each_cycles_share_and_the_rest_is_unallocated(self):
        projects, unallocated = sr.allocate(self.invoices, self.arows, self.cycles)
        self.assertEqual(projects["alpha"], {"est": 350.0, "allocated": 750.0})     # 600 * 3/4 + 300 * 1
        self.assertEqual(projects["beta"], {"est": 100.0, "allocated": 150.0})      # 600 * 1/4
        self.assertEqual(unallocated, 300.0)                                         # the July cycle

    def test_every_invoiced_dollar_is_allocated_or_unallocated(self):
        projects, unallocated = sr.allocate(self.invoices, self.arows, self.cycles)
        self.assertAlmostEqual(sum(t["allocated"] for t in projects.values()) + unallocated,
                               sum(r["amount"] for r in self.invoices))

    def test_a_project_with_no_estimate_in_a_cycle_gets_nothing_from_it(self):
        projects, _ = sr.allocate(self.invoices, [arow("2026-08-11", "alpha", 10), arow("2026-08-12", "gamma", 0)],
                                  self.cycles)
        self.assertNotIn("gamma", projects)


class Report(unittest.TestCase):
    def test_months_have_running_totals_per_year_and_no_ratio(self):
        invoices = [inv("2025-12-06", 200, "plan"), inv("2026-01-06", 200, "plan"), inv("2026-02-06", 200, "plan"),
                    inv("2026-02-07", 50)]
        rep = sr.actuals_report(invoices, [arow("2026-02-08", "alpha", 100)])
        self.assertEqual([(m["period"], m["actual"], m["ytd"]) for m in rep["months"]],
                         [("2025-12", 200.0, 200.0), ("2026-01", 200.0, 200.0), ("2026-02", 250.0, 450.0)])
        self.assertTrue(all(m["ratio"] is None for m in rep["months"]))
        self.assertEqual([(y["period"], y["actual"]) for y in rep["years"]], [("2025", 200.0), ("2026", 450.0)])

    def test_the_effective_rate_uses_only_fully_covered_cycles(self):
        invoices = [inv("2026-08-06", 200, "plan"), inv("2026-08-20", 800),
                    inv("2026-09-06", 200, "plan"), inv("2026-09-10", 300)]
        arows = [arow("2026-08-11", "alpha", 400), arow("2026-09-07", "alpha", 1000)]
        rep = sr.actuals_report(invoices, arows)
        self.assertEqual([c["coverage"] for c in rep["cycles"]], ["partial", "full"])
        self.assertEqual(rep["rate"], {"since": "2026-09-06", "actual": 500.0, "est": 1000.0, "ratio": 0.5})

    def test_no_fully_covered_cycle_has_no_rate(self):
        rep = sr.actuals_report([inv("2026-08-06", 200, "plan")], [arow("2026-08-11", "alpha", 400)])
        self.assertIsNone(rep["rate"])
        self.assertIn("no billing cycle is fully covered", sr.actuals_text(rep, [], "i", "a", "UTC"))


class Cli(unittest.TestCase):
    def setUp(self):
        self.home = tempfile.TemporaryDirectory()
        self.addCleanup(self.home.cleanup)
        self.dir = os.path.join(self.home.name, ".claude", "spend-archive")
        os.makedirs(self.dir)

    def test_actuals_reads_the_default_files_under_home(self):
        sr.write_archive(os.path.join(self.dir, "spend-daily.csv"), [arow("2026-09-06", "alpha-r1", 1000)])
        write(os.path.join(self.dir, "invoices.csv"),
              "date,amount,kind,invoice_id,notes\n2026-09-06,200,plan,,\n2026-09-10,300,credits,,\n")
        r = helpers.run_cli("spend-report.py", ["--actuals"], self.home.name)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("Effective rate, fully covered cycles since 2026-09-06: $500 invoiced / $1,000 estimated = 0.50",
                      r.stdout)
        self.assertRegex(r.stdout, r"alpha-r1\s+1,000\s+500\s+100%")

    def test_no_invoices_says_so(self):
        r = helpers.run_cli("spend-report.py", ["--actuals"], self.home.name)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("no invoices yet", r.stdout)

    def test_window_options_are_refused(self):
        r = helpers.run_cli("spend-report.py", ["--actuals", "--days", "3"], self.home.name)
        self.assertEqual(r.returncode, 2)
        self.assertIn("--actuals covers the whole history", r.stderr)


if __name__ == "__main__":
    unittest.main()
