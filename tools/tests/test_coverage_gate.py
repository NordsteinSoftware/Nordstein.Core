"""Tests for coverage_gate: the per-assembly coverage floors of Core's CI."""

import contextlib
import io
import json
import pathlib
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

import coverage_gate  # noqa: E402

ASSEMBLY = "Nordstein.Core.Common"


def _line(number, hits, condition=None, conditions=None):
    """conditions: [(number, type, coverage percent)], rendered as coverlet's <conditions> element."""
    attribute = f' condition-coverage="{condition}"' if condition else ""
    branch = "True" if condition else "False"
    if not conditions:
        return f'<line number="{number}" hits="{hits}" branch="{branch}"{attribute} />'
    rendered = "".join(f'<condition number="{n}" type="{kind}" coverage="{percent}%" />' for n, kind, percent in conditions)
    return f'<line number="{number}" hits="{hits}" branch="{branch}"{attribute}><conditions>{rendered}</conditions></line>'


def _cobertura(packages, source="/repo/"):
    """packages: {package: {filename: [(number, hits, condition-or-None[, conditions])]}}; each line also appears under a method."""
    body = ""
    for package, files in packages.items():
        classes = ""
        for filename, lines in files.items():
            rendered = "".join(_line(*entry) for entry in lines)
            classes += (f'<class name="C" filename="{filename}"><methods><method name="M"><lines>{rendered}</lines>'
                        f'</method></methods><lines>{rendered}</lines></class>')
        body += f'<package name="{package}"><classes>{classes}</classes></package>'
    return f'<?xml version="1.0"?><coverage><sources><source>{source}</source></sources><packages>{body}</packages></coverage>'


class GateTests(unittest.TestCase):
    def setUp(self):
        self.root = pathlib.Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root)
        self.reports = self.root / "coverage"
        self.floors = self.root / "floors.json"
        self.out = self.root / "coverage.json"

    def write(self, packages, name="guid", source="/repo/"):
        directory = self.reports / name
        directory.mkdir(parents=True)
        (directory / "coverage.cobertura.xml").write_text(_cobertura(packages, source), encoding="utf-8")

    def run_gate(self, floors):
        self.floors.write_text(json.dumps(floors), encoding="utf-8")
        return coverage_gate.run(self.reports, self.floors, self.out)

    def report(self):
        return json.loads(self.out.read_text(encoding="utf-8"))

    def test_run_assemblyBelowLineFloor_fails(self):
        self.write({ASSEMBLY: {"A.cs": [(n, 1 if n <= 8 else 0, None) for n in range(1, 11)]}})

        code, output = self.run_gate({ASSEMBLY: {"line": 90.0, "branch": 0.0}})

        self.assertEqual(code, 1)
        self.assertEqual(output[0], "coverage gate: FAIL — line 80.0 %, branch 100.0 %")
        self.assertIn(f"BELOW {ASSEMBLY}: line 80.0 % < floor 90.0 %", output)

    def test_run_assemblyBelowBranchFloor_fails(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, "50% (1/2)")]}})

        code, output = self.run_gate({ASSEMBLY: {"line": 90.0, "branch": 90.0}})

        self.assertEqual(code, 1)
        self.assertIn(f"BELOW {ASSEMBLY}: branch 50.0 % < floor 90.0 %", output)

    def test_run_exactlyAtTheFloor_passes(self):
        self.write({ASSEMBLY: {"A.cs": [(n, 1 if n <= 9 else 0, None) for n in range(1, 11)]}})

        code, output = self.run_gate({ASSEMBLY: {"line": 90.0, "branch": 90.0}})

        self.assertEqual(code, 0, output)
        self.assertEqual(output, ["coverage gate: PASS — line 90.0 %, branch 100.0 %"])

    def test_run_percentagesTruncateToOneDecimal_neverRoundUpToTheFloor(self):
        # 8999 of 10000 lines is 89.99 %: truncated to 89.9, below a 90.0 floor.
        self.write({ASSEMBLY: {"A.cs": [(n, 1 if n <= 8999 else 0, None) for n in range(1, 10001)]}})

        code, output = self.run_gate({ASSEMBLY: {"line": 90.0, "branch": 0.0}})

        self.assertEqual(code, 1)
        self.assertIn(f"BELOW {ASSEMBLY}: line 89.9 % < floor 90.0 %", output)

    def test_run_noReportFiles_failsAndWritesNothing(self):
        self.reports.mkdir()

        code, output = self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        self.assertEqual(code, 1)
        self.assertEqual(output, [f"coverage gate: FAIL — no coverage.cobertura.xml under {self.reports}"])
        self.assertFalse(self.out.exists())

    def test_run_missingReportDirectory_fails(self):
        code, output = self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        self.assertEqual(code, 1)
        self.assertTrue(output[0].startswith("coverage gate: FAIL — no coverage.cobertura.xml"), output)

    def test_run_unreadableReport_fails(self):
        directory = self.reports / "broken"
        directory.mkdir(parents=True)
        (directory / "coverage.cobertura.xml").write_text("<coverage><packages>", encoding="utf-8")

        code, output = self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        self.assertEqual(code, 1)
        self.assertTrue(output[0].startswith("coverage gate: FAIL — cannot read the coverage report"), output)

    def test_run_reportWithoutAnyPackage_fails(self):
        directory = self.reports / "empty"
        directory.mkdir(parents=True)
        (directory / "coverage.cobertura.xml").write_text('<?xml version="1.0"?><coverage><packages /></coverage>', encoding="utf-8")

        code, output = self.run_gate({})

        self.assertEqual(code, 1)
        self.assertEqual(output, ["coverage gate: FAIL — the coverage reports measured no assembly"])

    def test_run_unreadableFloors_fails(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, None)]}})
        self.floors.write_text("{not json", encoding="utf-8")

        code, output = coverage_gate.run(self.reports, self.floors, self.out)

        self.assertEqual(code, 1)
        self.assertTrue(output[0].startswith("coverage gate: FAIL — cannot read the floors"), output)

    def test_run_floorsMissingAMetric_fails(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, None)]}})

        code, output = self.run_gate({ASSEMBLY: {"line": 90.0}})

        self.assertEqual(code, 1)
        self.assertTrue(output[0].startswith("coverage gate: FAIL — cannot read the floors"), output)

    def test_run_floorsFileMissing_fails(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, None)]}})

        code, output = coverage_gate.run(self.reports, self.root / "absent.json", self.out)

        self.assertEqual(code, 1)
        self.assertTrue(output[0].startswith("coverage gate: FAIL — cannot read the floors"), output)

    def test_run_assemblyWithoutFloor_fails(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, None)]}, "Nordstein.Core.New": {"B.cs": [(1, 1, None)]}})

        code, output = self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        self.assertEqual(code, 1)
        self.assertIn("NO FLOOR Nordstein.Core.New: line 100.0 %, branch 100.0 %", output)

    def test_run_floorWithoutCoverage_fails(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, None)]}})

        code, output = self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}, "Nordstein.Core.Gone": {"line": 90.0, "branch": 90.0}})

        self.assertEqual(code, 1)
        self.assertIn("NO DATA Nordstein.Core.Gone: floor set, no coverage", output)

    def test_run_sameAssemblyInSeveralReports_mergesHitsPerLine(self):
        # Every test project instruments the assemblies it loads, so Common appears in each project's report.
        self.write({ASSEMBLY: {"A.cs": [(1, 3, None), (2, 0, None)]}}, name="one")
        self.write({ASSEMBLY: {"A.cs": [(1, 0, None), (2, 5, None)]}}, name="two")

        code, output = self.run_gate({ASSEMBLY: {"line": 100.0, "branch": 100.0}})

        self.assertEqual(code, 0, output)
        self.assertEqual(self.report()["assemblies"][0]["line"], 100.0)

    def test_run_branchesInSeveralReports_takeTheHigherCoveredCountPerLine(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, "50% (1/2)")]}}, name="one")
        self.write({ASSEMBLY: {"A.cs": [(1, 1, "100% (2/2)")]}}, name="two")

        self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        assembly = self.report()["assemblies"][0]
        self.assertEqual((assembly["branchesCovered"], assembly["branchesValid"]), (2, 2))

    def test_run_splitConditions_unionPerCondition(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, "50% (2/4)", [(7, "jump", 100), (29, "jump", 0)])]}}, name="one")
        self.write({ASSEMBLY: {"A.cs": [(1, 1, "50% (2/4)", [(7, "jump", 0), (29, "jump", 100)])]}}, name="two")

        self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        assembly = self.report()["assemblies"][0]
        self.assertEqual((assembly["branchesCovered"], assembly["branchesValid"]), (4, 4))

    def test_run_switchCondition_takesTheRemainingBranchesOfTheLine(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, "50% (3/6)", [(3, "jump", 50), (9, "switch", 50)])]}}, name="one")
        self.write({ASSEMBLY: {"A.cs": [(1, 1, "50% (3/6)", [(3, "jump", 100), (9, "switch", 25)])]}}, name="two")

        self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        assembly = self.report()["assemblies"][0]
        self.assertEqual((assembly["branchesCovered"], assembly["branchesValid"]), (4, 6))

    def test_run_methodLines_areNotCountedTwice(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, None)]}})

        self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        assembly = self.report()["assemblies"][0]
        self.assertEqual((assembly["linesCovered"], assembly["linesValid"]), (1, 1))

    def test_run_sameFileUnderDifferentSources_isOneFile(self):
        # coverlet roots a report at the common directory of its files: one run names "A.cs", another "Project/A.cs".
        self.write({ASSEMBLY: {"A.cs": [(1, 1, None)]}}, name="one", source="/repo/Nordstein.Core.Common/")
        self.write({ASSEMBLY: {"Nordstein.Core.Common/A.cs": [(1, 1, None)]}}, name="two")

        self.run_gate({ASSEMBLY: {"line": 0.0, "branch": 0.0}})

        self.assertEqual(self.report()["assemblies"][0]["linesValid"], 1)

    def test_run_assemblyWithoutBranches_countsAsFullBranchCoverage(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 1, None)]}})

        code, _ = self.run_gate({ASSEMBLY: {"line": 90.0, "branch": 90.0}})

        self.assertEqual(code, 0)
        self.assertEqual(self.report()["assemblies"][0]["branch"], 100.0)

    def test_run_everyAssemblyAtOrAboveItsFloor_passesAndWritesTheReport(self):
        names = ["Nordstein.Core.AI", ASSEMBLY, "Nordstein.Core.Testing"]
        lines = [(1, 1, "50% (1/2)")] + [(n, 1 if n <= 9 else 0, None) for n in range(2, 11)]
        self.write({name: {f"{name}.cs": lines} for name in names})

        code, output = self.run_gate({name: {"line": 90.0, "branch": 50.0} for name in names})

        self.assertEqual(code, 0, output)
        report = self.report()
        self.assertEqual(report["result"], "PASS")
        self.assertEqual([a["name"] for a in report["assemblies"]], sorted(names))
        self.assertEqual(report["assemblies"][0]["lineFloor"], 90.0)
        self.assertEqual(report["failures"], [])

    def test_main_failingGate_exitsOneAndPrintsTheLines(self):
        self.write({ASSEMBLY: {"A.cs": [(1, 0, None)]}})
        self.floors.write_text(json.dumps({ASSEMBLY: {"line": 90.0, "branch": 90.0}}), encoding="utf-8")

        with contextlib.redirect_stdout(io.StringIO()) as stdout:
            code = coverage_gate.main(["--reports", str(self.reports), "--floors", str(self.floors)])

        self.assertEqual(code, 1)
        self.assertIn(f"BELOW {ASSEMBLY}: line 0.0 % < floor 90.0 %", stdout.getvalue())
        self.assertTrue((self.reports / "coverage.json").is_file())


if __name__ == "__main__":
    unittest.main()
