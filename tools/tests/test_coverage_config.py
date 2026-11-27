"""Tests for tools/coverage.runsettings and tools/coverage-floors.json against the repository's projects."""

import json
import pathlib
import re
import unittest
import xml.etree.ElementTree as ElementTree

REPO = pathlib.Path(__file__).resolve().parents[2]
TOOLS = REPO / "tools"
# The owner's bar: more than 90 % line and branch coverage per shipped assembly.
MINIMUM_FLOOR = 90.0


def _filters(name):
    """The module patterns of a runsettings filter element, as coverlet reads them: `[module]*`."""
    root = ElementTree.parse(TOOLS / "coverage.runsettings").getroot()
    text = root.findtext(f"DataCollectionRunSettings/DataCollectors/DataCollector/Configuration/{name}") or ""
    return [re.fullmatch(r"\[([^\]]+)\]\*", item.strip()).group(1) for item in text.split(",") if item.strip()]


def _matches(pattern, name):
    """Coverlet's module match: case sensitive, `*` the only wildcard."""
    return re.fullmatch(".*".join(re.escape(part) for part in pattern.split("*")), name) is not None


def _projects():
    """Assembly name of every Nordstein.Core.* project: its AssemblyName, else the project name."""
    names = {}
    for csproj in sorted(REPO.glob("Nordstein.Core.*/Nordstein.Core.*.csproj")):
        match = re.search(r"<AssemblyName>(.*?)</AssemblyName>", csproj.read_text(encoding="utf-8"))
        names[csproj.stem] = match.group(1) if match else csproj.stem
    return names


def _shipped():
    return {project: name for project, name in _projects().items() if not project.endswith(".Tests")}


def _measured(name):
    return any(_matches(p, name) for p in _filters("Include")) and not any(_matches(p, name) for p in _filters("Exclude"))


class CoverageConfigTests(unittest.TestCase):
    def test_shipped_includesEverySeparatelyPackagedAssembly(self):
        # A positive control for the project discovery below: an empty glob would pass every other test.
        self.assertTrue({"Nordstein.Core.Common", "Nordstein.Core.Testing", "Nordstein.Core.Workflows"} <= set(_shipped().values()))

    def test_runsettings_measuresEveryShippedAssemblyTheTestingPackageIncluded(self):
        unmeasured = sorted(name for name in _shipped().values() if not _measured(name))

        self.assertEqual([], unmeasured, "shipped assemblies the coverage filters never instrument")

    def test_runsettings_measuresNoTestProject(self):
        tests = [name for project, name in _projects().items() if project.endswith(".Tests")]

        self.assertTrue(tests, "the repository has test projects")
        self.assertEqual([], [name for name in tests if _measured(name)], "test projects the coverage filters instrument")

    def test_runsettings_noFilterNamesAHyphen(self):
        # coverlet 10 never matches a module through a pattern holding "-"; use `*` for it.
        self.assertEqual([], [p for p in _filters("Include") + _filters("Exclude") if "-" in p])

    def test_floors_everyShippedAssemblyHasAFloorAndNothingElseDoes(self):
        floors = json.loads((TOOLS / "coverage-floors.json").read_text(encoding="utf-8"))

        self.assertEqual(sorted(_shipped().values()), sorted(floors))

    def test_floors_neverBelowTheOwnersMinimum(self):
        floors = json.loads((TOOLS / "coverage-floors.json").read_text(encoding="utf-8"))

        low = {name: entry for name, entry in floors.items() if entry["line"] < MINIMUM_FLOOR or entry["branch"] < MINIMUM_FLOOR}

        self.assertEqual({}, low, f"floors below {MINIMUM_FLOOR} %")


if __name__ == "__main__":
    unittest.main()
