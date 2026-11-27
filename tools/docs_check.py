"""Check the repository's Markdown: links resolve, and every docs/ page is indexed in CLAUDE.md.

Usage: docs_check.py --root <repo>

Prints one problem per line and exits 1 when there is any, 0 otherwise:
- every docs/*.md page but docs/README.md has a row in the root CLAUDE.md's index table (a table row linking it);
- every relative link in every Markdown file resolves to an existing file or directory: inline links
  `[text](target "title")` and reference definitions `[label]: target`, the part before `#` checked (anchors are not),
  a target starting with `/` read from the repository root, percent-escapes decoded. Links inside fenced code blocks
  and inline code spans are not links. URLs with a scheme (`https:`, `mailto:`) and bare `#anchor` links are skipped.

In a git checkout the files are the tracked and the untracked-but-not-ignored ones; elsewhere every file. Build output
(bin, obj, artifacts, TestResults) and .git are skipped either way. Python standard library only.
"""

import argparse
import pathlib
import re
import subprocess
import sys
import urllib.parse

SKIPPED_DIRS = {".git", "bin", "obj", "artifacts", "TestResults", "node_modules"}
INLINE_LINK = re.compile(r"\]\(\s*<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\s*\)")
REFERENCE_LINK = re.compile(r"^\s{0,3}\[[^\]]+\]:\s*<?(\S+?)>?(?:\s+.*)?$")
FENCE = re.compile(r"^\s{0,3}(`{3,}|~{3,})")
INLINE_CODE = re.compile(r"(`+)(?:(?!\1).)+?\1")
SCHEME = re.compile(r"^[A-Za-z][A-Za-z0-9+.-]*:")
INDEX = "CLAUDE.md"


def _walk(root):
    for path in sorted(root.iterdir()):
        if path.is_dir():
            if path.name not in SKIPPED_DIRS:
                yield from _walk(path)
        elif path.is_file():
            yield path


def _files(root):
    """The files in scope: git's tracked and untracked-not-ignored ones in a checkout, every file otherwise."""
    if (root / ".git").exists():
        try:
            listed = subprocess.run(["git", "-C", str(root), "ls-files", "--cached", "--others", "--exclude-standard"],
                                    check=True, capture_output=True, text=True).stdout.splitlines()
        except (subprocess.CalledProcessError, OSError):
            listed = None
        if listed is not None:
            files = []
            for name in sorted(set(listed)):
                path = root / name
                if path.is_file() and not set(pathlib.PurePosixPath(name).parts[:-1]) & SKIPPED_DIRS:
                    files.append(path)
            return files
    return list(_walk(root))


def _prose_lines(text):
    """The text's lines with fenced code blocks blanked and inline code spans removed; line numbers are kept."""
    lines = []
    fence = None
    for line in text.splitlines():
        match = FENCE.match(line)
        if fence is None and match:
            fence = match.group(1)[0] * 3
            lines.append("")
        elif fence is not None:
            if line.strip().startswith(fence):
                fence = None
            lines.append("")
        else:
            lines.append(INLINE_CODE.sub("", line))
    return lines


def _targets(text):
    """(line number, link target) of every inline link and reference definition outside code."""
    for number, line in enumerate(_prose_lines(text), 1):
        for target in INLINE_LINK.findall(line):
            yield number, target
        match = REFERENCE_LINK.match(line)
        if match:
            yield number, match.group(1)


def _resolve(root, document, target):
    """The path a relative link points to, or None for a URL or an anchor of the same page."""
    if SCHEME.match(target) or target.startswith("#"):
        return None
    file_part = urllib.parse.unquote(target.split("#", 1)[0])
    if not file_part:
        return None
    base = root if file_part.startswith("/") else document.parent
    return base / file_part.lstrip("/")


def _read(path):
    try:
        return path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None


def link_problems(root, files):
    problems = []
    for path in files:
        if path.suffix.lower() != ".md":
            continue
        text = _read(path)
        if text is None:
            continue
        relative = path.relative_to(root).as_posix()
        for number, target in _targets(text):
            resolved = _resolve(root, path, target)
            if resolved is not None and not resolved.exists():
                problems.append(f"{relative}:{number}: broken link {target}")
    return problems


def index_problems(root):
    """A docs/*.md page (docs/README.md aside) without a row in the root CLAUDE.md's index table."""
    pages = sorted(path for path in (root / "docs").glob("*.md") if path.name != "README.md") if (root / "docs").is_dir() else []
    index = root / INDEX
    text = _read(index) if index.is_file() else None
    if text is None:
        return [f"{INDEX}: missing, so no docs/ page has an index row"] if pages else []
    indexed = set()
    for line in _prose_lines(text):
        if line.lstrip().startswith("|"):
            for target in INLINE_LINK.findall(line):
                resolved = _resolve(root, index, target)
                if resolved is not None:
                    indexed.add(resolved.resolve())
    return [f"{page.relative_to(root).as_posix()}: no row in the {INDEX} index table" for page in pages if page.resolve() not in indexed]


def check(root):
    """Every problem: the index rows first, then the broken links in file order."""
    root = pathlib.Path(root)
    return index_problems(root) + link_problems(root, _files(root))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", required=True, type=pathlib.Path, help="the repository root")
    args = parser.parse_args(argv)
    problems = check(args.root)
    for problem in problems:
        print(problem)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
