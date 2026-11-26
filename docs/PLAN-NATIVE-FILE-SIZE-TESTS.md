# File-size checks in the native test suites

1. [complete] Confirm the existing test runners and file-size policy.
2. [complete] Move the checks into both suites and wire them into CI.
3. [in progress] Run both suites, verify the Release build, and confirm CI.

Acceptance. The normal C# and JavaScript suites enforce the 400-line policy
with shared exceptions and regression coverage. CI uses those suites.

Next action. Run both suites and the Release publish, then confirm the GitHub Actions results.
