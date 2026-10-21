"""Apply a reviewable local before/fixed patch; never commits or changes Git refs."""
import argparse
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("state", choices=["before", "fixed"])
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    patch = "tests/http/lab-04-before.patch"
    direction = [] if args.state == "before" else ["--reverse"]
    check = subprocess.run(["git", "apply", "--check", *direction, "--", patch],
        cwd=root, capture_output=True, text=True)
    if check.returncode:
        reverse = ["--reverse"] if args.state == "before" else []
        already = subprocess.run(["git", "apply", "--check", *reverse, "--", patch],
            cwd=root, capture_output=True, text=True)
        if already.returncode == 0:
            print("Already in " + args.state + " state. Git refs unchanged.")
            return 0
        print("State switch refused: files no longer match the prepared patch.")
        print(check.stderr.strip())
        return 1
    result = subprocess.run(["git", "apply", *direction, "--", patch], cwd=root)
    if result.returncode:
        return result.returncode
    print("Selected " + args.state + " state. Stop/restart the API to use it.")
    print("No commit, staging, branch or tag operations were performed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
