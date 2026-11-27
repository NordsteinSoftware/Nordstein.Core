"""Tests for the dependency policy (docs/dependencies.md): actions pinned by commit SHA, Dependabot's cooldown."""

import pathlib
import re
import unittest

REPO = pathlib.Path(__file__).resolve().parents[2]
WORKFLOWS = REPO / ".github" / "workflows"
USES = re.compile(r"^\s*(?:-\s+)?uses:\s*(\S+)(.*)$")
PINNED = re.compile(r"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$")
VERSION_COMMENT = re.compile(r"^\s+# v\d+(?:\.\d+){0,2}\s*$")
# Actions no pinned SHA was verified for yet; each stays on its tag until one is (docs/dependencies.md).
ON_TAG = {"github/codeql-action/init@v4", "github/codeql-action/analyze@v4"}


def unpinned(text):
    """The `uses:` values of a workflow text that are neither a SHA with a version comment, a local action, nor listed."""
    problems = []
    for line in text.splitlines():
        match = USES.match(line)
        if not match:
            continue
        action, rest = match.group(1).strip("'\""), match.group(2)
        if action.startswith("./") or action in ON_TAG:
            continue
        if not PINNED.match(action) or not VERSION_COMMENT.match(rest):
            problems.append(action + rest)
    return problems


def ecosystems_without_cooldown(text):
    """The package ecosystems of a dependabot.yml text whose entry sets no cooldown of at least 7 days."""
    entries = re.split(r"\n\s*-\s+package-ecosystem:\s*", "\n" + text)[1:]
    missing = []
    for entry in entries:
        ecosystem = entry.split()[0]
        days = re.search(r"\n\s+cooldown:\s*\n\s+default-days:\s*(\d+)", entry)
        if days is None or int(days.group(1)) < 7:
            missing.append(ecosystem)
    return missing


class PinTests(unittest.TestCase):
    def test_unpinned_findsATagAMissingCommentAndAShortSha(self):
        # The positive control: the check flags each form it exists to refuse.
        text = """
        steps:
          - uses: actions/checkout@v7
          - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1
          - uses: actions/checkout@3d3c42e # v7.0.1
          - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
          - uses: ./.github/actions/local
            uses: github/codeql-action/init@v4
        """

        self.assertEqual(unpinned(text), ["actions/checkout@v7", "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
                                          "actions/checkout@3d3c42e # v7.0.1"])

    def test_workflows_pinEveryActionByCommitSha(self):
        files = sorted(WORKFLOWS.glob("*.yml"))

        self.assertTrue(files, "the repository has workflows")
        problems = {path.name: unpinned(path.read_text(encoding="utf-8")) for path in files}
        self.assertEqual({}, {name: found for name, found in problems.items() if found})


class CooldownTests(unittest.TestCase):
    def test_ecosystemsWithoutCooldown_findsAMissingAndAShortOne(self):
        text = """
version: 2
updates:
  - package-ecosystem: nuget
    cooldown:
      default-days: 7
  - package-ecosystem: npm
    schedule:
      interval: weekly
  - package-ecosystem: docker
    cooldown:
      default-days: 3
"""

        self.assertEqual(ecosystems_without_cooldown(text), ["npm", "docker"])

    def test_dependabot_delaysEveryEcosystemSevenDays(self):
        text = (REPO / ".github" / "dependabot.yml").read_text(encoding="utf-8")

        self.assertEqual(ecosystems_without_cooldown(text), [])
        self.assertEqual(sorted(re.findall(r"package-ecosystem:\s*(\S+)", text)), ["github-actions", "nuget"])


if __name__ == "__main__":
    unittest.main()
