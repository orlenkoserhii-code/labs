"""Print a compact shareable summary from actual LR04 runs, not raw API logs."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
summary = {"http": {}, "tests": None, "audit_events": [], "live_audit": []}
for phase in ("before", "fixed"):
    path = ROOT / "docs/evidence/lab-04" / (phase + ".json")
    if path.exists():
        evidence = json.loads(path.read_text(encoding="utf-8"))
        summary["http"][phase] = {"utc": evidence["utc"], "passed": evidence["passed"],
            "checks": [{"scenario": x["scenario"], "passed": x["passed"],
                **({"status": x["response"]["status"]} if "response" in x else {})}
                for x in evidence["checks"]]}
        if "error" in evidence:
            summary["http"][phase]["error"] = evidence["error"]
        if phase == "fixed":
            summary["audit_response_trace"] = evidence.get("audit_trace_id")

trx = ROOT / "tmp/lab04/lab04.trx"
if trx.exists():
    tree = ET.parse(trx)
    counters = tree.find(".//{*}Counters")
    failed = []
    lab04 = []
    for test in tree.findall(".//{*}UnitTestResult"):
        name = test.get("testName", "")
        if test.get("outcome") != "Passed":
            failed.append({"name": name, "outcome": test.get("outcome")})
        if "Lab04" in name:
            lab04.append({"name": name, "outcome": test.get("outcome")})
        for node in test.findall(".//{*}StdOut"):
            for line in (node.text or "").splitlines():
                if "LR04_AUDIT " in line:
                    value = line.split("LR04_AUDIT ", 1)[1]
                    try:
                        summary["audit_events"].append(json.loads(value))
                    except json.JSONDecodeError:
                        pass
    summary["tests"] = {"counters": counters.attrib if counters is not None else {},
        "failed": failed, "lab04": lab04}

log = ROOT / "tmp/lab04/api.log"
trace = summary.get("audit_response_trace")
if log.exists() and trace:
    content = log.read_text(encoding="utf-8-sig", errors="replace")
    # Windows PowerShell 5.1 Tee-Object may write UTF-16.
    if "\x00" in content:
        content = log.read_text(encoding="utf-16", errors="replace")
    summary["live_audit"] = [line.strip() for line in content.splitlines()
        if "authorization.denied" in line and trace in line]

target = ROOT / "docs/evidence/lab-04/summary.json"
if summary["http"] or summary["tests"] is not None:
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(summary, ensure_ascii=False, indent=2))
