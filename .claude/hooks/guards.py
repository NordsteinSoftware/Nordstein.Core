"""Write-time guard for Claude Code sessions in Nordstein.Core (docs/testing.md, Repository checks).

Usage: guards.py edit, with the PreToolUse hook event of an Edit, Write or MultiEdit as JSON on stdin
(.claude/settings.json wires it).

Denies an edit to a C# file that adds
- a `#pragma warning disable`, `#nullable disable`, `[SuppressMessage]` or `[ExcludeFromCodeCoverage]` without a
  `// WHY:` comment on its line or in the comment block directly above it;
- a null-forgiving `!` (a postfix `!` after an identifier, `)` or `]` that is not `!=`), with no exemption: Core's one
  sanctioned use is `Validation.Success` in Nordstein.Core.Common/Validation/Validation.cs, and a second is never added.
Only what the edit adds counts: an existing occurrence is not blamed on it. Strings, characters and comments are not
code; the holes of an interpolated string are. A denial exits 2 with the reason on stderr; a malformed event exits 1,
which never blocks. The guard catches accidents, not adversaries: review and CI remain the backstop.
Python standard library only.
"""

import json
import pathlib
import re
import sys

WHY = "// WHY:"
SUPPRESSION = re.compile(
    r"#\s*pragma\s+warning\s+disable\b|#\s*nullable\s+disable\b"
    r"|\[[^\]]*?\b(?:SuppressMessage|ExcludeFromCodeCoverage)(?:Attribute)?\b")
# A postfix `!` after an identifier, `)` or `]`, not the `!=` operator; keywords before a prefix `!` are excluded below.
NULL_FORGIVING = re.compile(r"(?<=[\w)\]])\s*!(?!=)")
WORD_BEFORE = re.compile(r"(\w+)\s*$")
KEYWORDS = frozenset("return case when await throw yield in is and or not else do".split())
PREPROCESSOR = re.compile(r"^\s*#")


def _skip_string(text, start, verbatim, interpolated, out):
    """Blanks a regular or verbatim string starting at its opening quote; returns the index after it. The holes of an
    interpolated string are copied as code (and lexed, nested strings included)."""
    index = start + 1
    out.append(" ")
    while index < len(text):
        char = text[index]
        if verbatim and char == '"' and text.startswith('""', index):
            out.append("  ")
            index += 2
            continue
        if not verbatim and char == "\\":
            out.append("".join("\n" if escaped == "\n" else " " for escaped in text[index:index + 2]))
            index += 2
            continue
        if char == '"':
            out.append(" ")
            return index + 1
        if interpolated and char == "{":
            if text.startswith("{{", index):
                out.append("  ")
                index += 2
                continue
            index = _code(text, index + 1, out_prefix="{", out=out, until_brace=True)
            continue
        out.append("\n" if char == "\n" else " ")
        index += 1
    return index


def _skip_raw(text, start, quotes, out):
    """Blanks a raw string literal of `quotes` quotes starting at its first quote; returns the index after it."""
    end = text.find('"' * quotes, start + quotes)
    end = len(text) if end < 0 else end + quotes
    out.append("".join("\n" if char == "\n" else " " for char in text[start:end]))
    return end


def _code(text, index, out, out_prefix="", until_brace=False):
    """Copies code from `index`, blanking comments, strings and characters (newlines kept). With `until_brace`, stops
    after the `}` that closes an interpolation hole and returns the index after it."""
    out.append(out_prefix)
    depth = 0
    while index < len(text):
        char = text[index]
        if text.startswith("//", index):
            end = text.find("\n", index)
            end = len(text) if end < 0 else end
            out.append(" " * (end - index))
            index = end
        elif text.startswith("/*", index):
            end = text.find("*/", index + 2)
            end = len(text) if end < 0 else end + 2
            out.append("".join("\n" if c == "\n" else " " for c in text[index:end]))
            index = end
        elif char == "'":
            end = index + 1
            while end < len(text) and text[end] != "'" and text[end] != "\n":
                end += 2 if text[end] == "\\" else 1
            end = min(end + 1, len(text))
            out.append(" " * (end - index))
            index = end
        elif char in '$@"':
            prefix = re.match(r'[$@]*', text[index:]).group(0)
            quote = index + len(prefix)
            if quote < len(text) and text[quote] == '"' and (prefix or char == '"'):
                out.append(" " * len(prefix))
                quotes = len(re.match(r'"*', text[quote:]).group(0))
                if quotes >= 3:
                    index = _skip_raw(text, quote, quotes, out)
                else:
                    index = _skip_string(text, quote, "@" in prefix, "$" in prefix, out)
            else:
                out.append(char)
                index += 1
        elif until_brace and char == "{":
            depth += 1
            out.append(char)
            index += 1
        elif until_brace and char == "}":
            if depth == 0:
                out.append("}")
                return index + 1
            depth -= 1
            out.append(char)
            index += 1
        else:
            out.append(char)
            index += 1
    return index


def code_only(text):
    """The C# text with comments, strings and character literals blanked; line structure kept."""
    out = []
    _code(text, 0, out)
    return "".join(out)


def null_forgiving_count(text):
    """How many null-forgiving `!` operators the C# text holds outside strings, characters, comments and directives."""
    count = 0
    for line in code_only(text).splitlines():
        if PREPROCESSOR.match(line):
            continue
        for match in NULL_FORGIVING.finditer(line):
            word = WORD_BEFORE.search(line[:match.start()])
            if word is None or word.group(1) not in KEYWORDS:
                count += 1
    return count


def unjustified_suppressions(text):
    """How many suppressions the C# text holds without a `// WHY:` on their line or in the comment block above."""
    raw = text.splitlines()
    code = code_only(text).splitlines()
    count = 0
    for index, line in enumerate(code):
        if not SUPPRESSION.search(line) or WHY in raw[index]:
            continue
        above = index - 1
        justified = False
        while above >= 0 and raw[above].strip().startswith("//"):
            if WHY in raw[above]:
                justified = True
                break
            above -= 1
        count += not justified
    return count


def edit_problem(file_path, before, after):
    """Why an edit from `before` to `after` must not happen, or None. Only what the edit adds counts."""
    if pathlib.PurePath(file_path).suffix != ".cs":
        return None
    if unjustified_suppressions(after) > unjustified_suppressions(before):
        return ("A `#pragma warning disable`, `#nullable disable`, `[SuppressMessage]` or `[ExcludeFromCodeCoverage]` "
                "needs a `// WHY:` comment on its line or in the comment block directly above it (docs/code-style.md).")
    if null_forgiving_count(after) > null_forgiving_count(before):
        return ("This edit adds a null-forgiving `!`, which Core forbids outright (CLAUDE.md, Hard Rules): the one "
                "sanctioned use is Validation.Success in Nordstein.Core.Common/Validation/Validation.cs. Guard the value "
                "(ArgumentNullException.ThrowIfNull, a pattern match) or return Validation.Success instead.")
    return None


def apply_edit(tool_name, tool_input, current):
    """(before, after) of a file an Edit, Write or MultiEdit is about to change, from its current text."""
    if tool_name == "Write":
        return current, tool_input.get("content", "")
    edits = tool_input.get("edits") if tool_name == "MultiEdit" else [tool_input]
    after = current
    for edit in edits or []:
        old, new = edit.get("old_string", ""), edit.get("new_string", "")
        if old:
            after = after.replace(old, new) if edit.get("replace_all") else after.replace(old, new, 1)
    return current, after


def _read(path):
    try:
        return pathlib.Path(path).read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError, ValueError):
        return ""


def main(argv, stdin_text):
    """Returns (exit code, stdout, stderr)."""
    try:
        mode = argv[0]
        event = json.loads(stdin_text)
        tool_input = event.get("tool_input") or {}
    except (IndexError, ValueError, AttributeError) as error:
        return 1, "", f"guards: cannot read the hook event ({error})"
    if mode != "edit":
        return 1, "", f"guards: unknown mode {mode}"
    path = tool_input.get("file_path", "")
    problem = edit_problem(path, *apply_edit(event.get("tool_name"), tool_input, _read(path))) if path else None
    return (2, "", problem) if problem else (0, "", "")


if __name__ == "__main__":
    code, out, err = main(sys.argv[1:], sys.stdin.read())
    if out:
        print(out)
    if err:
        print(err, file=sys.stderr)
    sys.exit(code)
