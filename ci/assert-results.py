"""Compares the observed outcome of each case with ci/expected.json, writes results.json and a Markdown table.

A case that never logged START, or never requested its icon stream, fails instead of passing vacuously.
"""
import argparse
import json
import os
import re
import sys

parser = argparse.ArgumentParser()
parser.add_argument("--expected", required=True)
parser.add_argument("--results", required=True)
parser.add_argument("--api-level", default="?")
parser.add_argument("--summary")
arguments = parser.parse_args()

expected = json.load(open(arguments.expected))

LOGCAT_LINE = re.compile(r"^\S+\s+\S+\s+(\d+)\s+\d+\s+([A-Z])\s+(\S+)\s*: ?(.*)$")

# What the thrown exception must look like for each crashing case
EXCEPTION_SIGNATURE = {
    "flyouticon-throws": r"simulated offline",
    "iconoverride-throws": r"simulated offline",
    "flyouticon-canceled-by-maui": r"TaskCanceledException|OperationCanceledException",
}
MINIMUM_REQUESTS = {"flyouticon-canceled-by-maui": 2, "flyouticon-canceled-by-maui-safe": 2, "control-image-throws": 2}
CANCELED_CASES = {"flyouticon-canceled-by-maui", "flyouticon-canceled-by-maui-safe"}


def read(path):
    try:
        return open(path, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def observe(case, folder):
    logcat = read(f"{folder}/logcat.txt")
    crash = read(f"{folder}/crash.txt")
    start = re.search(rf"^\S+\s+\S+\s+(\d+)\s+\d+\s+I ShellIconRepro\s*: START case={case} ", logcat, re.MULTILINE)
    if not start:
        return "did-not-run", ["no START line: the app did not run this case"], {}
    pid = start.group(1)

    own_lines = []
    for line in dict.fromkeys(logcat.splitlines() + crash.splitlines()):
        match = LOGCAT_LINE.match(line)
        if match and match.group(1) == pid:
            own_lines.append((match.group(2), match.group(3), match.group(4)))
    own_text = "\n".join(message for _, _, message in own_lines)
    marker_lines = [message for _, tag, message in own_lines if tag == "ShellIconRepro"]

    requests = sum(1 for m in marker_lines if m.startswith(f"STREAM_REQUESTED case={case} "))
    alive = any(m.startswith(f"ALIVE case={case}") for m in marker_lines)
    done = any(m.startswith(f"DONE case={case}") for m in marker_lines)
    unhandled = any(m.startswith(f"UNHANDLED case={case}") for m in marker_lines)
    fatal = "FATAL EXCEPTION" in own_text
    fallbacks = sum(1 for m in marker_lines if m.startswith(f"FALLBACK case={case}"))
    process = read(f"{folder}/process.txt").strip() or "unknown"
    details = {"process": process, "pid": pid, "streamRequests": requests, "alive": alive, "done": done, "unhandledLogged": unhandled, "fatalException": fatal, "fallbacks": fallbacks}

    problems = []
    wanted_requests = MINIMUM_REQUESTS.get(case, 1)
    if requests < wanted_requests:
        problems.append(f"only {requests} STREAM_REQUESTED lines, expected at least {wanted_requests}: the icon load did not happen as planned")
        return "icon-not-loaded", problems, details

    if case in CANCELED_CASES:
        second_update = next((i for i, m in enumerate(marker_lines) if m.startswith(f"SECOND_UPDATE case={case}")), None)
        second_request_after = second_update is not None and any(
            m.startswith(f"STREAM_REQUESTED case={case} n=2") for m in marker_lines[second_update:])
        first_served = any(m.startswith(f"STREAM_SERVED case={case} n=1") for m in marker_lines)
        if not second_request_after or first_served:
            problems.append("the first load was not canceled by a second load that started after SECOND_UPDATE (needs STREAM_REQUESTED n=2 after the marker and no STREAM_SERVED n=1)")
            return "cancel-not-isolated", problems, details

    if alive and done and not fatal and not unhandled and process == "alive":
        return "alive", problems, details

    base_case = case.removesuffix("-safe")
    signature = EXCEPTION_SIGNATURE.get(base_case)
    if process == "gone" and not alive and not done and signature and re.search(signature, own_text) and "UpdateLeftBarButtonItem" in own_text:
        return "crash-in-UpdateLeftBarButtonItem", problems, details

    problems.append(f"process={process}; crash verdicts need a dead process plus the expected exception text and an UpdateLeftBarButtonItem frame")
    return "other-failure", problems, details


rows = []
failed = False
for case, want in expected.items():
    got, problems, details = observe(case, f"{arguments.results}/cases/{case}")
    match = got == want and not problems
    failed |= not match
    rows.append({"case": case, "expected": want, "observed": got, "match": match, "problems": problems, **details})

os.makedirs(arguments.results, exist_ok=True)
json.dump({"apiLevel": arguments.api_level, "cases": rows}, open(f"{arguments.results}/results.json", "w"), indent=2)

wrong = [row["case"] for row in rows if not row["match"]]
verdict = ("Bug reproduced: the three failing icon loads killed the app inside UpdateLeftBarButtonItem; controls and workaround stayed alive"
           if not wrong else f"NOT as expected: {', '.join(wrong)}")
lines = [f"### Android API {arguments.api_level}", "", f"**{verdict}**", "",
         "| Case | Expected | Observed | Match | Process | Stream requests | Notes |", "|---|---|---|---|---|---|---|"]
for row in rows:
    lines.append(f"| {row['case']} | {row['expected']} | {row['observed']} | {'yes' if row['match'] else 'NO'} | {row.get('process', '-')} | {row.get('streamRequests', '-')} | {'; '.join(row['problems']).replace('|', '/')} |")
table = "\n".join(lines) + "\n"
print(table)
if arguments.summary:
    open(arguments.summary, "a").write(table)

sys.exit(1 if failed else 0)
