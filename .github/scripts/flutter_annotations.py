"""Ошибки flutter analyze и упавшие flutter test → аннотации GitHub Actions."""
import json
import re
import sys


def esc(text: str) -> str:
    return text.replace("%", "%25").replace("\r", "").replace("\n", "%0A")


def analyze(path: str) -> None:
    # Строки вида: "  error • Undefined name 'x' • lib/a.dart:3:5 • undefined_identifier"
    line_re = re.compile(r"^\s*(error|warning|info)\s+•\s+(.+?)\s+•\s+(\S+?):(\d+):(\d+)\s+•\s+(\S+)")
    for line in open(path, encoding="utf-8", errors="replace"):
        m = line_re.match(line)
        if m and m.group(1) != "info":
            level, message, file, row, col, code = m.groups()
            print(f"::error file=client/{file},line={row},col={col},title={code}::{esc(message)}")


def tests(path: str) -> None:
    names, errors = {}, {}
    for raw in open(path, encoding="utf-8", errors="replace"):
        try:
            event = json.loads(raw)
        except ValueError:
            continue
        if not isinstance(event, dict):
            continue
        if event.get("type") == "testStart":
            names[event["test"]["id"]] = event["test"]["name"]
        elif event.get("type") == "error":
            errors.setdefault(event["testID"], []).append(event.get("error", "") + "\n" + event.get("stackTrace", "")[:800])
        elif event.get("type") == "testDone" and event.get("result") != "success" and not event.get("hidden"):
            body = "\n".join(errors.get(event["testID"], ["failed"]))[:1800]
            print(f"::error title={esc(names.get(event['testID'], '?'))[:200]}::{esc(body)}")


if __name__ == "__main__":
    {"analyze": analyze, "tests": tests}[sys.argv[1]](sys.argv[2])
