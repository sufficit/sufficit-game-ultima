# File-size checks in the native test suites

## Investigation

The 400-line policy originally ran as a Python check with separate Python
regression tests. The project's existing suites use the C# RulesSmoke runner
and Node's built-in test runner.

## Changes

- Added the repository scan and isolated Git fixtures to
  [RulesSmoke](../../tests/RulesSmoke/RepositoryFileSizeChecks.cs) and
  [Node's test suite](../../tests/repository-file-size.test.mjs).
- Both suites read the same
  [exception list](../../scripts/file-size-exceptions.json). They cover the
  400/401-line boundary, CRLF, an unterminated final line, ignored and binary
  files, justified ceilings, and stale exceptions.
- Replaced the Python checks in CI with the normal C# and JavaScript commands.
  The repository policy still checks tracked and new unignored text files.

## Validation and delivery

- Local RulesSmoke passed all 241 checks. Node passed all seven tests.
  The local Blazor Release publish also passed.
- GitHub Actions ran both suites successfully. The public Pages build manifest
  matched `9d3e67a530bf95313765d0e83071d22cc9691371`.
  [GitHub Actions run](https://github.com/sufficit/sufficit-game-ultima/actions/runs/45871884398)
