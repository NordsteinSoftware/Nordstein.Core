"""Tests for docs_check: relative Markdown links resolve and every docs/ page has its CLAUDE.md index row."""

import contextlib
import io
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

import docs_check  # noqa: E402

REPO = pathlib.Path(__file__).resolve().parents[2]

INDEX = """# Root

| Doc | Read before… |
|-----|--------------|
| [`docs/a.md`](docs/a.md) | A |
"""


class DocsCheckTests(unittest.TestCase):
    def setUp(self):
        self.root = pathlib.Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root)
        self.write("CLAUDE.md", INDEX)
        self.write("docs/a.md", "# A\n\nBack to [the index](../CLAUDE.md).\n")
        self.write("docs/README.md", "# Docs\n\n[a](a.md)\n")

    def write(self, relative, text):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def problems(self):
        return docs_check.check(self.root)

    def test_check_conformingTree_reportsNothing(self):
        self.assertEqual(self.problems(), [])

    def test_check_brokenRelativeLink_reportsItWithItsLine(self):
        self.write("docs/a.md", "# A\n\nSee [b](b.md#section) and [index](../CLAUDE.md).\n")

        self.assertEqual(self.problems(), ["docs/a.md:3: broken link b.md#section"])

    def test_check_linkInAnyMarkdownFile_isChecked(self):
        self.write("Project/README.md", "[gone](../docs/gone.md)\n")
        self.write(".claude/agents/reviewer.md", "[gone](../../docs/gone.md)\n")

        self.assertEqual(self.problems(), [".claude/agents/reviewer.md:1: broken link ../../docs/gone.md",
                                           "Project/README.md:1: broken link ../docs/gone.md"])

    def test_check_linkToADirectoryOrWithATitle_resolves(self):
        self.write("README.md", '[docs](docs/) [a](docs/a.md "the A page") [root](./CLAUDE.md)\n')

        self.assertEqual(self.problems(), [])

    def test_check_rootRelativeAndEncodedLinks_resolve(self):
        self.write("docs/with space.md", "# Spaced\n")
        self.write("CLAUDE.md", INDEX + "| [`docs/with space.md`](docs/with%20space.md) | S |\n")
        self.write("Project/README.md", "[a](/docs/a.md) [s](../docs/with%20space.md)\n")

        self.assertEqual(self.problems(), [])

    def test_check_externalAnchorAndMailLinks_areIgnored(self):
        self.write("docs/a.md", "[x](https://example.com/a.md) [y](#top) [z](mailto:a@example.com) [w](http://example.com)\n")

        self.assertEqual(self.problems(), [])

    def test_check_linksInCode_areIgnored(self):
        self.write("docs/a.md", "# A\n\n```markdown\n[gone](gone.md)\n```\n\nInline `[gone](gone.md)` too.\n\n~~~\n[gone](gone.md)\n~~~\n")

        self.assertEqual(self.problems(), [])

    def test_check_referenceStyleLink_isChecked(self):
        self.write("docs/a.md", "# A\n\nSee [the page][p].\n\n[p]: missing.md\n")

        self.assertEqual(self.problems(), ["docs/a.md:5: broken link missing.md"])

    def test_check_docsPageWithoutAnIndexRow_reportsIt(self):
        self.write("docs/b.md", "# B\n")

        self.assertEqual(self.problems(), ["docs/b.md: no row in the CLAUDE.md index table"])

    def test_check_docsPageLinkedOnlyOutsideTheTable_stillNeedsARow(self):
        self.write("docs/b.md", "# B\n")
        self.write("CLAUDE.md", INDEX + "\nSee also [b](docs/b.md).\n")

        self.assertEqual(self.problems(), ["docs/b.md: no row in the CLAUDE.md index table"])

    def test_check_docsReadmeAndNestedPages_needNoRow(self):
        self.write("docs/adr/0001.md", "# ADR\n")

        self.assertEqual(self.problems(), [])

    def test_check_missingRootClaudeMd_reportsIt(self):
        (self.root / "CLAUDE.md").unlink()

        self.assertEqual(self.problems(), ["CLAUDE.md: missing, so no docs/ page has an index row",
                                           "docs/a.md:3: broken link ../CLAUDE.md"])

    def test_check_buildOutputAndGitignoredScratch_areIgnored(self):
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        self.write(".gitignore", "scratch/\n")
        self.write("scratch/notes.md", "[gone](gone.md)\n")
        self.write("Project/bin/Debug/README.md", "[gone](gone.md)\n")
        self.write("Project/obj/x.md", "[gone](gone.md)\n")

        self.assertEqual(self.problems(), [])

    def test_check_outsideAGitCheckout_walksTheTreeWithoutBuildOutput(self):
        self.write("Project/bin/x.md", "[gone](gone.md)\n")
        self.write("Project/notes.md", "[gone](gone.md)\n")

        self.assertEqual(self.problems(), ["Project/notes.md:1: broken link gone.md"])

    def test_main_problems_exitOneAndPrintThem(self):
        self.write("docs/a.md", "[b](b.md)\n")

        with contextlib.redirect_stdout(io.StringIO()) as stdout:
            code = docs_check.main(["--root", str(self.root)])

        self.assertEqual(code, 1)
        self.assertEqual(stdout.getvalue(), "docs/a.md:1: broken link b.md\n")

    def test_main_noProblems_exitZeroAndPrintNothing(self):
        with contextlib.redirect_stdout(io.StringIO()) as stdout:
            code = docs_check.main(["--root", str(self.root)])

        self.assertEqual((code, stdout.getvalue()), (0, ""))


class RepositoryTests(unittest.TestCase):
    def test_repository_followsTheDocRules(self):
        self.assertEqual(docs_check.check(REPO), [])


if __name__ == "__main__":
    unittest.main()
