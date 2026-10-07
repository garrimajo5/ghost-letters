"""Упавшие тесты из .trx → аннотации GitHub Actions (видны без скачивания логов)."""
import glob
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def esc(text: str) -> str:
    return text.replace("%", "%25").replace("\r", "").replace("\n", "%0A")


failed = 0
for path in glob.glob(sys.argv[1] if len(sys.argv) > 1 else "server/tests/**/TestResults/*.trx", recursive=True):
    root = ET.parse(path).getroot()
    for result in root.iterfind(".//t:UnitTestResult", NS):
        if result.get("outcome") != "Failed":
            continue
        failed += 1
        message = result.findtext(".//t:Message", default="", namespaces=NS)
        stack = result.findtext(".//t:StackTrace", default="", namespaces=NS)
        body = (message.strip() + "\n" + "\n".join(stack.strip().splitlines()[:6]))[:1800]
        print(f"::error title={esc(result.get('testName', '?'))[:200]}::{esc(body)}")
print(f"failed tests: {failed}")
