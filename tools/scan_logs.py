#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Scan round-log files for CJK characters.

The storage contract is: JSONL event files contain no Han characters
(U+4E00..U+9FFF). Chinese is generated only by Web/src/logRender.js.

Usage:
    python NET/tools/scan_logs.py Web/logs
    python NET/tools/scan_logs.py Web/logs --all
"""

import argparse
import os
import re
import sys

CJK = re.compile(r"[\u4e00-\u9fff]")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", nargs="?", default=os.path.join("Web", "logs"))
    parser.add_argument("--all", action="store_true",
                        help="scan every text file, not just *.jsonl")
    args = parser.parse_args()

    if not os.path.isdir(args.directory):
        print("not a directory: %s" % args.directory)
        return 2

    files = 0
    matches = 0
    for root, _dirs, names in os.walk(args.directory):
        for name in sorted(names):
            if not args.all and not name.endswith(".jsonl"):
                continue
            path = os.path.join(root, name)
            try:
                with open(path, "r", encoding="utf-8", errors="replace") as handle:
                    for line_no, line in enumerate(handle, 1):
                        found = CJK.findall(line)
                        if found:
                            matches += len(found)
                            print("CJK %s:%d: %s" % (path, line_no, line.strip()[:220]))
            except OSError as error:
                print("skip %s: %s" % (path, error))
                continue
            files += 1

    print("scanned %d file(s), CJK matches: %d" % (files, matches))
    return 1 if matches else 0


if __name__ == "__main__":
    sys.exit(main())
