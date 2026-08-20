#!/usr/bin/env python3

import argparse
import glob
import json
import os
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

PRODUCT_ASSEMBLIES = (
    "DotNetRepoInspector.Cli",
    "DotNetRepoInspector.Core",
    "DotNetRepoInspector.Engine",
    "DotNetRepoInspector.Git",
    "DotNetRepoInspector.MSBuild",
    "DotNetRepoInspector.Persistence",
    "DotNetRepoInspector.Persistence.Http",
)


def parse_args():
    parser = argparse.ArgumentParser(
        description="Consolidate Cobertura reports and optionally enforce the repository coverage baseline."
    )
    parser.add_argument(
        "--reports",
        default="artifacts/test-results/**/coverage.cobertura.xml",
        help="Glob used to find Cobertura reports.",
    )
    parser.add_argument(
        "--output-dir",
        default="artifacts/coverage",
        help="Directory for consolidated coverage outputs.",
    )
    parser.add_argument(
        "--baseline",
        default=None,
        help="Optional JSON baseline. When supplied, coverage thresholds are enforced.",
    )
    return parser.parse_args()


def normalize_filename(value):
    return value.replace("\\", "/")


def collect_lines(reports):
    assembly_lines = defaultdict(dict)

    for report in reports:
        root = ET.parse(report).getroot()

        for package in root.findall(".//package"):
            assembly = package.attrib.get("name", "").strip()
            if assembly not in PRODUCT_ASSEMBLIES:
                continue

            lines = assembly_lines[assembly]
            for class_node in package.findall("./classes/class"):
                filename = normalize_filename(class_node.attrib.get("filename", ""))
                if not filename:
                    continue

                for line in class_node.findall("./lines/line"):
                    number = line.attrib.get("number")
                    hits = line.attrib.get("hits")
                    if number is None or hits is None:
                        continue

                    key = (filename, int(number))
                    lines[key] = max(lines.get(key, 0), int(hits))

    return assembly_lines


def calculate_summary(assembly_lines):
    assemblies = {}
    global_lines = {}

    for assembly in PRODUCT_ASSEMBLIES:
        lines = assembly_lines.get(assembly, {})
        for key, hits in lines.items():
            global_lines[key] = max(global_lines.get(key, 0), hits)

        total = len(lines)
        covered = sum(1 for hits in lines.values() if hits > 0)
        assemblies[assembly] = coverage_entry(covered, total)

    total = len(global_lines)
    covered = sum(1 for hits in global_lines.values() if hits > 0)

    if total == 0:
        raise SystemExit("No production source lines were found in the Cobertura reports.")

    return {
        "schemaVersion": 1,
        "global": coverage_entry(covered, total),
        "assemblies": assemblies,
    }


def coverage_entry(covered, total):
    percentage = 0.0 if total == 0 else covered / total * 100.0
    return {
        "coveredLines": covered,
        "totalLines": total,
        "lineCoverage": round(percentage, 2),
    }


def enforce_baseline(summary, baseline_path):
    with open(baseline_path, encoding="utf-8") as baseline_file:
        baseline = json.load(baseline_file)

    if baseline.get("schemaVersion") != 1:
        raise SystemExit("Unsupported coverage baseline schema version.")

    minimum = float(baseline["minimumLineCoverage"])
    baseline_coverage = float(baseline["baselineLineCoverage"])
    maximum_drop = float(baseline["maximumDropPercentagePoints"])
    required = max(minimum, baseline_coverage - maximum_drop)
    actual = float(summary["global"]["lineCoverage"])

    summary["gate"] = {
        "minimumLineCoverage": minimum,
        "baselineLineCoverage": baseline_coverage,
        "maximumDropPercentagePoints": maximum_drop,
        "requiredLineCoverage": round(required, 2),
        "passed": actual + 1e-9 >= required,
    }

    if not summary["gate"]["passed"]:
        raise SystemExit(
            f"Coverage gate failed: {actual:.2f}% is below the required {required:.2f}%."
        )


def render_markdown(summary):
    rows = [
        "## Code coverage",
        "",
        "| Scope | Covered lines | Total lines | Line coverage |",
        "| --- | ---: | ---: | ---: |",
    ]

    for assembly in PRODUCT_ASSEMBLIES:
        value = summary["assemblies"][assembly]
        rows.append(
            f"| `{assembly}` | {value['coveredLines']} | {value['totalLines']} | {value['lineCoverage']:.2f}% |"
        )

    global_value = summary["global"]
    rows.append(
        f"| **Global** | **{global_value['coveredLines']}** | **{global_value['totalLines']}** | **{global_value['lineCoverage']:.2f}%** |"
    )

    if "gate" in summary:
        gate = summary["gate"]
        rows.extend(
            [
                "",
                "### Coverage gate",
                "",
                f"- Baseline: **{gate['baselineLineCoverage']:.2f}%**",
                f"- Absolute minimum: **{gate['minimumLineCoverage']:.2f}%**",
                f"- Maximum allowed drop: **{gate['maximumDropPercentagePoints']:.2f} p.p.**",
                f"- Effective requirement: **{gate['requiredLineCoverage']:.2f}%**",
                f"- Result: **{'PASS' if gate['passed'] else 'FAIL'}**",
            ]
        )

    return "\n".join(rows) + "\n"


def main():
    args = parse_args()
    reports = sorted(glob.glob(args.reports, recursive=True))
    if not reports:
        raise SystemExit("No Cobertura coverage reports were generated.")

    assembly_lines = collect_lines(reports)
    summary = calculate_summary(assembly_lines)

    failure = None
    if args.baseline:
        try:
            enforce_baseline(summary, args.baseline)
        except SystemExit as error:
            failure = error

    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    json_path = output_dir / "coverage-summary.json"
    markdown_path = output_dir / "coverage-summary.md"
    json_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    markdown = render_markdown(summary)
    markdown_path.write_text(markdown, encoding="utf-8")

    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as github_summary:
            github_summary.write(markdown)

    print(markdown, end="")

    if failure is not None:
        raise failure


if __name__ == "__main__":
    main()
