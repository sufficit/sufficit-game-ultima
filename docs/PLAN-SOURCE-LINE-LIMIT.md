# Repository line limit and inline documentation

1. [complete] Inventory tracked files and review humanoid contracts and browser module boundaries.
2. [complete] Document those contracts in English and add the line-limit gate with explicit exceptions.
3. [in progress] Verify the gate and the Release build, then deliver and verify CI.

Acceptance. Files above 400 lines fail unless explicitly listed with a reason
and a per-file ceiling. The check is repeatable locally and in CI. Inline
documentation explains the humanoid pipeline and the project records how to
split JavaScript by responsibility.

Next action. Run the gate fixtures, repository check and Release publish. Confirm the CI gate after pushing.
