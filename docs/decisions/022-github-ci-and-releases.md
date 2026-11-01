# 022: GitHub, CI and a release for every merge

Date: 1 Nov 2026. Requested by Ali.

## Decision

- The repository lives on GitHub at [RLcafe/OpenSidecar](https://github.com/RLcafe/OpenSidecar) (remote `origin`). `main`, every `vX.Y.Z` tag and every open branch are pushed there. The PC folder stays the working copy, and worktrees still live in `.worktrees\`.
- **Tests run on GitHub.** `.github/workflows/ci.yml` runs `build.ps1 -Test`, `package.ps1 -Test` and `build-android.ps1 -Test` in one job on `windows-latest` for every push and pull request. Pushes that only change Markdown, `docs/` or `design specfication/` skip it. A branch merges, and a tag is pushed, only after a green run.
- **Every merge into `main` gets a version and a GitHub Release.** `.github/workflows/release.yml` runs on a pushed `vX.Y.Z` tag. It checks that the tag is on `main`, builds it, runs the same checks (with the pinned sensor files required), and publishes `Widget Screen vX.Y.Z` with the setup exe, a portable zip and the APK. The notes come from the version's CHANGELOG section. Merges that only touch docs, tests, scripts or CI are PATCH versions.
- **Every change goes through a pull request** (Ali, 1 Nov 2026, added in v0.35.2): branches open a draft PR to `main`, stay current by merging `origin/main`, and are merged on GitHub with a merge commit once CI is green. Nothing is pushed to `main` directly. The open PRs replace the branch table in BRANCHES.md, which every branch used to edit and so caused conflicts.
- `releases\` and all build output stay git-ignored. Built files reach GitHub only as release files and CI artifacts. `scripts\release.ps1 -UpdateShortcut` still makes the PC's own copy for the desktop shortcut.

## Rationale

- The repo has no `.csproj` or Gradle, so CI runs the same PowerShell scripts as the PC. A Windows runner has the Framework's `csc.exe`, a JDK and the Android SDK, which is everything the scripts need, and it downloads nothing beyond the pinned LibreHardwareMonitor zip.
- The Windows and Android checks run in one job because the Android checks parse the phone-link answer that the Windows checks save (`artifacts\tests\remote-state-sample.json`).
- Building releases from the tag on GitHub means every release is reproducible from history and is tested before it's published.

## Tradeoffs

- The runner has no touchscreen, audio devices, GPU sensors, Spotify or Claude Code. Live integrations still need checking on the PC, and checks that read live devices report them as unavailable there.
- Android APKs must keep one signing key to install over each other. CI artifacts use the runner's throwaway debug key. Releases use the PC's debug key only if Ali stores it as the `ANDROID_DEBUG_KEYSTORE_BASE64` repository secret; without it, the release notes say to uninstall a PC-built phone app first. It is a debug key, not a Play Store key, so keeping it in a GitHub secret is acceptable.
- A PATCH release for docs-only merges publishes binaries identical in behavior to the last one. That's the price of the "one release per merge" rule, which keeps versions and merges one to one.
- GitHub doesn't start workflows for pushes of more than three tags at once, so the 45 tags pushed during the move have no releases. Actions → Release → Run workflow publishes one by hand.
