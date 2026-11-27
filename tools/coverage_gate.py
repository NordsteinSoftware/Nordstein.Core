"""Hold each Nordstein.Core assembly's line and branch coverage to its floor.

Usage: coverage_gate.py --reports <dir> --floors <file> [--out <file>]

Reads every coverage.cobertura.xml under <dir> (one per test project, written by
`dotnet test Nordstein.Core.sln --settings tools/coverage.runsettings --collect "XPlat Code Coverage"`) and merges them
per line: an assembly appears in the report of every test project that loads it, so hits are summed and each branch
condition keeps the highest coverage any report gives it. Each assembly's line and branch percentage, truncated to one
decimal, is compared with its floor in <file>: {"<assembly>": {"line": 90.0, "branch": 90.0}}.

Prints the summary line and one line per failure, writes the result to --out (default <dir>/coverage.json), and exits
0 on PASS and 1 on FAIL (2 on a usage error). An assembly without a floor, a floor without coverage, no report file,
a report that cannot be read or measures nothing, and floors that cannot be read all fail: the gate is never green on
a missing result. Python standard library only.
"""

import argparse
import json
import pathlib
import posixpath
import re
import sys
import xml.etree.ElementTree as ElementTree

REPORT = "coverage.cobertura.xml"
CONDITION = re.compile(r"\((\d+)/(\d+)\)")
JUMP_BRANCHES = 2
REST = "rest"
METRICS = ("line", "branch")


def _tenths(covered, valid):
    """The percentage truncated to one decimal, as an integer count of tenths; nothing to cover is 100.0 %."""
    return 1000 if valid == 0 else covered * 1000 // valid


def _merge_value(left, right):
    """(hits, {condition: (covered, total)}): hits summed, each condition's highest covered count kept."""
    conditions = dict(left[1])
    for key, (covered, total) in right[1].items():
        known = conditions.get(key, (0, 0))
        conditions[key] = (max(known[0], covered), max(known[1], total))
    return (left[0] + right[0], conditions)


def _conditions(line):
    """{condition: (covered, total)} of one <line>.

    Coverlet gives each <condition> only a percentage. A jump has two branches; the line's other branches (switches)
    are one group, sized by the line's condition-coverage total minus the jumps. A line without <condition> elements
    is that one group alone.
    """
    match = CONDITION.search(line.get("condition-coverage", ""))
    if not match:
        return {}
    covered, total = int(match.group(1)), int(match.group(2))
    conditions = {}
    for condition in line.iterfind("conditions/condition"):
        if condition.get("type") != "jump":
            continue
        percent = int(condition.get("coverage", "0%").rstrip("%"))
        conditions[condition.get("number", "")] = (percent * JUMP_BRANCHES // 100, JUMP_BRANCHES)
    rest_total = total - sum(value[1] for value in conditions.values())
    if rest_total > 0:
        conditions[REST] = (covered - sum(value[0] for value in conditions.values()), rest_total)
    return conditions


def parse_cobertura(path):
    """Map (package, file, line number) -> (hits, {condition: (covered, total)}), class lines only.

    The file is resolved against the report's <source> and normalized: coverlet roots a report at the common directory
    of its files, so one run names a file `A.cs` and another `Nordstein.Core.Common/A.cs`.
    """
    root = ElementTree.parse(path).getroot()
    source = root.findtext("sources/source") or ""
    result = {}
    for package in root.iterfind("packages/package"):
        name = package.get("name", "")
        for cls in package.iterfind("classes/class"):
            filename = posixpath.normpath(posixpath.join(source, cls.get("filename", "")))
            for line in cls.iterfind("lines/line"):
                value = (int(line.get("hits", "0")), _conditions(line))
                key = (name, filename, int(line.get("number", "0")))
                result[key] = _merge_value(result[key], value) if key in result else value
    return result


def merge(reports):
    merged = {}
    for report in reports:
        for key, value in report.items():
            merged[key] = _merge_value(merged[key], value) if key in merged else value
    return merged


def _entry(lines_covered, lines_valid, branches_covered, branches_valid):
    return {"linesValid": lines_valid, "linesCovered": lines_covered, "branchesValid": branches_valid,
            "branchesCovered": branches_covered, "line": _tenths(lines_covered, lines_valid) / 10,
            "branch": _tenths(branches_covered, branches_valid) / 10}


def summarize(merged):
    totals = {}
    for (package, _, _), (hits, conditions) in merged.items():
        counts = totals.setdefault(package, [0, 0, 0, 0])
        counts[0] += 1 if hits > 0 else 0
        counts[1] += 1
        counts[2] += sum(covered for covered, _ in conditions.values())
        counts[3] += sum(total for _, total in conditions.values())
    return {package: _entry(*counts) for package, counts in sorted(totals.items())}


def _fmt(value):
    return f"{value:.1f}"


def evaluate(summary, floors):
    """The failures: an assembly below or without a floor, and a floor without coverage."""
    failures = []
    for assembly, measured in summary.items():
        if assembly not in floors:
            failures.append(f"NO FLOOR {assembly}: line {_fmt(measured['line'])} %, branch {_fmt(measured['branch'])} %")
            continue
        for metric in METRICS:
            if round(measured[metric] * 10) < round(floors[assembly][metric] * 10):
                failures.append(f"BELOW {assembly}: {metric} {_fmt(measured[metric])} % < floor {_fmt(floors[assembly][metric])} %")
    for assembly in sorted(floors):
        if assembly not in summary:
            failures.append(f"NO DATA {assembly}: floor set, no coverage")
    return failures


def read_floors(path):
    """{assembly: {"line": float, "branch": float}}; raises ValueError for any other shape."""
    values = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    if not isinstance(values, dict):
        raise ValueError("the floors are an object of assemblies")
    floors = {}
    for name, entry in values.items():
        if not isinstance(entry, dict) or any(not isinstance(entry.get(metric), (int, float)) or isinstance(entry.get(metric), bool)
                                              for metric in METRICS):
            raise ValueError(f"{name} needs a numeric line and branch floor")
        floors[name] = {metric: float(entry[metric]) for metric in METRICS}
    return floors


def _fail(message):
    return 1, [f"coverage gate: FAIL — {message}"]


def run(reports, floors, out):
    """Returns (exit code, the lines to print); writes the result JSON to `out` once every input was read."""
    reports = pathlib.Path(reports)
    files = sorted(reports.rglob(REPORT)) if reports.is_dir() else []
    if not files:
        return _fail(f"no {REPORT} under {reports}")
    parsed = []
    for path in files:
        try:
            parsed.append(parse_cobertura(path))
        except (OSError, ElementTree.ParseError, ValueError) as error:
            return _fail(f"cannot read the coverage report {path} ({error})")
    summary = summarize(merge(parsed))
    if not summary:
        return _fail("the coverage reports measured no assembly")
    try:
        floor_values = read_floors(floors)
    except (OSError, ValueError) as error:
        return _fail(f"cannot read the floors {floors} ({error})")
    failures = evaluate(summary, floor_values)
    overall = _entry(*(sum(entry[key] for entry in summary.values())
                       for key in ("linesCovered", "linesValid", "branchesCovered", "branchesValid")))
    result = "FAIL" if failures else "PASS"
    assemblies = [{"name": name, "line": entry["line"], "branch": entry["branch"],
                   "linesCovered": entry["linesCovered"], "linesValid": entry["linesValid"],
                   "branchesCovered": entry["branchesCovered"], "branchesValid": entry["branchesValid"],
                   "lineFloor": floor_values.get(name, {}).get("line"),
                   "branchFloor": floor_values.get(name, {}).get("branch")} for name, entry in summary.items()]
    out = pathlib.Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"result": result, "line": overall["line"], "branch": overall["branch"],
                               "assemblies": assemblies, "failures": failures}, indent=2) + "\n", encoding="utf-8")
    head = f"coverage gate: {result} — line {_fmt(overall['line'])} %, branch {_fmt(overall['branch'])} %"
    return (1 if failures else 0), [head, *failures]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--reports", required=True, type=pathlib.Path, help="the directory holding the coverage reports")
    parser.add_argument("--floors", required=True, type=pathlib.Path, help="the JSON file of floors per assembly")
    parser.add_argument("--out", type=pathlib.Path, help="where to write the result JSON (default <reports>/coverage.json)")
    args = parser.parse_args(argv)
    code, lines = run(args.reports, args.floors, args.out or args.reports / "coverage.json")
    print("\n".join(lines))
    return code


if __name__ == "__main__":
    sys.exit(main())
