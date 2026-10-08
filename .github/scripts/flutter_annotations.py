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
    names, errors, prints = {}, {}, {}
    for raw in open(path, encoding="utf-8", errors="replace"):
        try:
            event = json.loads(raw)
        except ValueError:
            continue
        if not isinstance(event, dict):
            continue
        if event.get("type") == "testStart":
            names[event["test"]["id"]] = event["test"]["name"]
        elif event.get("type") == "print":
            # Виджет-тесты печатают само исключение отдельным print, а error — только «See exception logs above».
            prints.setdefault(event["testID"], []).append(event.get("message", ""))
        elif event.get("type") == "error":
            errors.setdefault(event["testID"], []).append(event.get("error", "") + "\n" + event.get("stackTrace", "")[:800])
        elif event.get("type") == "testDone" and event.get("result") != "success":
            # Скрытые «тесты» — загрузка файла тестов: их падение = ошибка компиляции или сбой вне теста.
            printed = "\n".join(prints.get(event["testID"], []))
            start = printed.find("EXCEPTION CAUGHT")
            body = (printed[start:start + 2500] + "\n" if start >= 0 else "") + "\n".join(errors.get(event["testID"], ["failed"]))
            body = body[:3000]
            print(f"::error title={esc(names.get(event['testID'], '?'))[:200]}::{esc(body)}")


if __name__ == "__main__":
    {"analyze": analyze, "tests": tests}[sys.argv[1]](sys.argv[2])
