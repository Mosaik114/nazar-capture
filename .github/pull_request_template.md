## What and why

<!-- What does this change, and which problem does it solve? Link the issue: Fixes #123 -->

## How it was tested

<!-- Windows version and display scale you ran it on, new or changed tests, manual checks -->

## Checklist

- [ ] `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings` and `cargo test -- --include-ignored` pass
- [ ] `dotnet format NazarCapture.sln --verify-no-changes` and `dotnet test NazarCapture.sln -c Release` pass
- [ ] Tests added or updated for changed behaviour; new pixel references reviewed
- [ ] `contract/contract.json` changed together with both sides, if the host–engine contract changed
- [ ] User-visible changes noted in `CHANGELOG.md` under *Unreleased*
- [ ] No personal data (paths, names, screenshots of your desktop) in the diff
