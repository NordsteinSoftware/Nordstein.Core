"""Tests for .claude/hooks/guards.py: the edit guard against unjustified suppressions and the null-forgiving `!`."""

import io
import json
import pathlib
import re
import shutil
import sys
import tempfile
import unittest

REPO = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / ".claude" / "hooks"))

import guards  # noqa: E402

FILE = "/repo/Nordstein.Core.Common/A.cs"
# The one sanctioned `!` in the ecosystem (CLAUDE.md, Hard Rules).
SANCTIONED = "Nordstein.Core.Common/Validation/Validation.cs"


class SuppressionTests(unittest.TestCase):
    def test_pragmaDisableWithoutWhy_isDenied(self):
        problem = guards.edit_problem(FILE, "", "#pragma warning disable CS8625\nx = null;\n#pragma warning restore CS8625\n")

        self.assertIsNotNone(problem)
        self.assertIn("// WHY:", problem)

    def test_pragmaDisableWithWhyInTheCommentBlockAbove_isAllowed(self):
        after = "    // WHY: the guard needs the null.\n    // More reasoning.\n#pragma warning disable CS8625\n"

        self.assertIsNone(guards.edit_problem(FILE, "", after))

    def test_pragmaDisableWithWhyOnTheSameLine_isAllowed(self):
        self.assertIsNone(guards.edit_problem(FILE, "", "#pragma warning disable CS1234 // WHY: generated name\n"))

    def test_whyAboveACodeLine_doesNotJustifyALaterSuppression(self):
        after = "// WHY: something else.\nint x = 1;\n#pragma warning disable CS0168\n"

        self.assertIsNotNone(guards.edit_problem(FILE, "", after))

    def test_suppressMessageWithoutWhy_isDenied(self):
        for attribute in ('[SuppressMessage("Style", "IDE0001")]', '[assembly: SuppressMessage("Style", "IDE0001")]',
                          '[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0001")]', '[Obsolete, SuppressMessage("a", "b")]'):
            with self.subTest(attribute=attribute):
                self.assertIsNotNone(guards.edit_problem(FILE, "", attribute + "\nclass A {}\n"))

    def test_excludeFromCodeCoverageWithoutWhy_isDenied(self):
        for attribute in ("[ExcludeFromCodeCoverage]", '[ExcludeFromCodeCoverage(Justification = "x")]', "[assembly: ExcludeFromCodeCoverage]"):
            with self.subTest(attribute=attribute):
                self.assertIsNotNone(guards.edit_problem(FILE, "", attribute + "\nclass A {}\n"))

    def test_nullableDisableWithoutWhy_isDenied(self):
        self.assertIsNotNone(guards.edit_problem(FILE, "", "#nullable disable\nclass A {}\n"))
        self.assertIsNone(guards.edit_problem(FILE, "", "#nullable restore\n#nullable enable\nclass A {}\n"))

    def test_suppressionInAStringOrComment_isNotASuppression(self):
        after = 'string s = "#pragma warning disable CS1";\n// [SuppressMessage("a", "b")]\n/* #nullable disable */\n'

        self.assertIsNone(guards.edit_problem(FILE, "", after))

    def test_existingUnjustifiedSuppression_isNotBlamedOnThisEdit(self):
        before = "#pragma warning disable CS0168\nclass A {}\n"

        self.assertIsNone(guards.edit_problem(FILE, before, before + "class B {}\n"))

    def test_markdownMentioningASuppression_isAllowed(self):
        self.assertIsNone(guards.edit_problem("/repo/docs/code-style.md", "", "Never `#pragma warning disable` or `x!.y` without review.\n"))


class NullForgivingTests(unittest.TestCase):
    def test_nullForgivingOperator_isDeniedWithoutAnyExemption(self):
        for line in ("var x = y!.Name;", "string s = null!;", "T value = default!;", "var a = Get()!;", "var b = items[0]!;",
                     "Use(x!, y);", "var c = x!;", "M(a.b!)", "var d = x! .Name;", "// WHY: justified\nvar e = y!.Name;"):
            with self.subTest(line=line):
                problem = guards.edit_problem(FILE, "", line + "\n")
                self.assertIsNotNone(problem)
                self.assertIn("Validation.Success", problem)

    def test_logicalNotAndInequality_areAllowed(self):
        for line in ("if (!flag && a != b) { }", "if (x!=y) { }", "bool n = !(a || b);", "return !done;", "var t = !!x;",
                     "Func<bool> f = () =>!x;", "if (a&&!b) { }", "x = y ?!z : w;", "return!flag;", "case !true:",
                     "while(!stop) { }", "bool q = a is not null && !b;", "int i = ~x;"):
            with self.subTest(line=line):
                self.assertIsNone(guards.edit_problem(FILE, "", line + "\n"))

    def test_exclamationInStringsCharsAndComments_isAllowed(self):
        after = ('var s = "Hello! World!";\nchar c = \'!\';\nvar v = @"say ""hi!"" now!";\n// done!\n/* wow! */\n'
                 '/// <summary>Stop!</summary>\nvar r = """\n    raw text! x!.y\n    """;\nvar e = "\\"!\\"";\n')

        self.assertIsNone(guards.edit_problem(FILE, "", after))

    def test_interpolatedStrings_scanTheirHolesAsCode(self):
        self.assertIsNone(guards.edit_problem(FILE, "", 'var s = $"Hi {name}! {{x!}} {(a ? "!" : "b")}!";\n'))
        self.assertIsNotNone(guards.edit_problem(FILE, "", 'var s = $"Hi {user!.Name}!";\n'))
        self.assertIsNotNone(guards.edit_problem(FILE, "", 'var s = $@"Hi {user!.Name} ""q""";\n'))

    def test_preprocessorNegation_isAllowed(self):
        self.assertIsNone(guards.edit_problem(FILE, "", "#if !DEBUG\nclass A {}\n#endif\n"))

    def test_existingOperator_isNotBlamedOnThisEdit(self):
        before = "class A { string s = null!; }\n"

        self.assertIsNone(guards.edit_problem(FILE, before, before + "class B { }\n"))
        self.assertIsNotNone(guards.edit_problem(FILE, before, before + "class B { string t = default!; }\n"))

    def test_secondOperatorInTheSanctionedFile_isDenied(self):
        sanctioned = (REPO / SANCTIONED).read_text(encoding="utf-8")

        self.assertIsNotNone(guards.edit_problem(str(REPO / SANCTIONED), sanctioned, sanctioned + "\nclass B { string t = null!; }\n"))

    def test_repository_findsTheSanctionedOperatorAndNoFalseAlarm(self):
        # The positive control and the false-positive check in one, over every C# file of the repository: the detector
        # finds the sanctioned `!`, and each line it flags holds a real null-forgiving operator (a `null!`, `default!`
        # or `Success!`), never a negation, an inequality, a string or a comment.
        flagged = []
        for path in sorted(REPO.glob("**/*.cs")):
            relative = path.relative_to(REPO)
            if {"bin", "obj"} & set(relative.parts):
                continue
            text = path.read_text(encoding="utf-8")
            if guards.null_forgiving_count(text):
                flagged += [(relative.as_posix(), line) for line in text.splitlines() if guards.null_forgiving_count(line)]

        self.assertIn(SANCTIONED, {name for name, _ in flagged})
        self.assertEqual([], [(name, line) for name, line in flagged if not re.search(r"\b(?:null|default|Success)!", line)])


class ApplyEditTests(unittest.TestCase):
    def test_edit_replacesTheFirstOccurrence(self):
        self.assertEqual(guards.apply_edit("Edit", {"old_string": "a", "new_string": "b"}, "a a"), ("a a", "b a"))

    def test_editReplaceAll_replacesEveryOccurrence(self):
        self.assertEqual(guards.apply_edit("Edit", {"old_string": "a", "new_string": "b", "replace_all": True}, "a a"), ("a a", "b b"))

    def test_write_replacesTheWholeText(self):
        self.assertEqual(guards.apply_edit("Write", {"content": "new"}, "old"), ("old", "new"))

    def test_multiEdit_appliesEachEditInOrder(self):
        edits = {"edits": [{"old_string": "a", "new_string": "b"}, {"old_string": "b", "new_string": "c", "replace_all": True}]}

        self.assertEqual(guards.apply_edit("MultiEdit", edits, "a b"), ("a b", "c c"))

    def test_editWithoutOldString_changesNothing(self):
        self.assertEqual(guards.apply_edit("Edit", {"new_string": "x"}, "text"), ("text", "text"))


class MainTests(unittest.TestCase):
    def setUp(self):
        self.root = pathlib.Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root)
        self.file = self.root / "A.cs"
        self.file.write_text("class A { }\n", encoding="utf-8")

    def event(self, tool_name, tool_input):
        return json.dumps({"tool_name": tool_name, "tool_input": tool_input})

    def test_main_editAddingTheOperator_exitsTwoWithTheReason(self):
        stdin = self.event("Edit", {"file_path": str(self.file), "old_string": "{ }", "new_string": "{ string s = null!; }"})

        code, out, err = guards.main(["edit"], stdin)

        self.assertEqual((code, out), (2, ""))
        self.assertIn("null-forgiving", err)

    def test_main_harmlessWrite_exitsZero(self):
        stdin = self.event("Write", {"file_path": str(self.file), "content": "class A { bool b = !true; }\n"})

        self.assertEqual(guards.main(["edit"], stdin), (0, "", ""))

    def test_main_newFile_isJudgedOnItsContent(self):
        stdin = self.event("Write", {"file_path": str(self.root / "New.cs"), "content": "#pragma warning disable CS1\n"})

        self.assertEqual(guards.main(["edit"], stdin)[0], 2)

    def test_main_malformedEventOrUnknownMode_exitsOneAndNeverBlocks(self):
        self.assertEqual(guards.main(["edit"], "{not json")[0], 1)
        self.assertEqual(guards.main([], "{}")[0], 1)
        self.assertEqual(guards.main(["bash"], "{}")[0], 1)

    def test_main_eventWithoutToolInput_allows(self):
        self.assertEqual(guards.main(["edit"], "{}"), (0, "", ""))

    def test_settings_wireTheEditGuardToEveryFileWritingTool(self):
        settings = json.loads((REPO / ".claude" / "settings.json").read_text(encoding="utf-8"))

        entries = settings["hooks"]["PreToolUse"]
        commands = {entry["matcher"]: [hook["command"] for hook in entry["hooks"]] for entry in entries}
        self.assertIn('python3 "$CLAUDE_PROJECT_DIR/.claude/hooks/guards.py" edit', commands.get("Edit|Write|MultiEdit", []))

    def test_script_runsAsACommand(self):
        import subprocess

        stdin = self.event("Write", {"file_path": str(self.file), "content": "string s = null!;\n"})
        result = subprocess.run([sys.executable, str(REPO / ".claude" / "hooks" / "guards.py"), "edit"], input=stdin,
                                capture_output=True, text=True, check=False)

        self.assertEqual(result.returncode, 2)
        self.assertIn("null-forgiving", result.stderr)


if __name__ == "__main__":
    unittest.main()
