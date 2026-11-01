# Branches

Every branch that hasn't been merged into `main` is listed here: its purpose, where it's checked out, where its test build lives, and anything to watch for when merging it. Released versions are in [CHANGELOG.md](CHANGELOG.md).

## Active branches

| Branch | Purpose | Based on | Status | Checkout | Test build |
| --- | --- | --- | --- | --- | --- |
| `feature/github-releases` | A GitHub Release for every merge (release.yml), docs and decision 022 | bf255bd | In progress | `.worktrees\github-releases` | CI artifacts on GitHub |
| `feature/sticker-defaults` | Sticker Sheet phase 2 (paused; checkpoint 00463a5 doesn't build yet) | b3c6e0c | In progress | `.worktrees\sticker-defaults` | `.worktrees\sticker-defaults\artifacts\app\WidgetScreen.exe` |
| `fix/claude-review` | Claude watcher fixes for v0.35.1 (paused, no commits yet) | b3c6e0c | In progress | `.worktrees\claude-review` | `.worktrees\claude-review\artifacts\app\WidgetScreen.exe` |

Statuses: **In progress**, **Ready for review** (built and waiting for the user to try it), **Merged** (remove the row once it's in the changelog), **Dropped** (move it to "Closed without merging").

## Closed without merging

None yet.

## Where branches live

- Every new branch gets a worktree **inside this folder**, at `.worktrees\<name>`. The folder is git-ignored. Never create folders next to `Widget screen` in `C:\Users\PCD\Documents\AI Apps\Claud`.
- Name branches `feature/<name>` for new behavior and `fix/<name>` for bug fixes. `<name>` is a few lowercase words joined by hyphens, and the worktree folder uses the same `<name>`.
- Start from the latest `main`:

  ```powershell
  git worktree add ".worktrees\<name>" -b feature/<name> main
  ```

- Build and test inside the worktree. The scripts resolve paths from their own folder, so the builds land in `.worktrees\<name>\artifacts\app\WidgetScreen.exe` and `.worktrees\<name>\artifacts\android\WidgetScreen.apk` and never lock the main app. `build.ps1` only compiles `src\` and `tests\`, so the worktrees under `.worktrees\` never leak into the main build.
- Push the new branch to GitHub with `git push -u origin <branch>`, and push again after each commit worth keeping. Every push runs CI (see [GitHub and CI](#github-and-ci)).
- Add a row to the table above when the branch is created, and keep its status and test build path current.
- A branch build uses your real preferences unless you start it with `--settings <path>`.

## After a merge

1. From the main folder, on `main`: `git merge --no-ff <branch>`.
2. Check that the branch's latest CI run on GitHub is green, then push `main` (`git push origin main`) and wait for its CI run (`build.ps1 -Test`, `package.ps1 -Test` and `build-android.ps1 -Test`) to pass before tagging.
3. Pick the new version (see [Versions](#versions)), move the branch's changes into a new section of [CHANGELOG.md](CHANGELOG.md), and remove its row here. Commit that, then tag it: `git tag -a vX.Y.Z -m "<one-line summary>"`, and push both: `git push origin main vX.Y.Z`. Pushing the tag publishes the GitHub Release by itself (see [Releases](#releases)); check that the Release run on the Actions tab finished green and the release has its three files.
4. Remove the worktree with `git worktree remove ".worktrees\<name>"`. Its `artifacts\` goes with it. If git refuses because of untracked files, look at them first, then use `--force` only if nothing in them is worth keeping.
5. Delete any build of the branch made from the main folder, such as `artifacts\<name>`.
6. Delete the branch with `git branch -d <branch>`, and on GitHub with `git push origin --delete <branch>`. Lowercase `-d` refuses to delete unmerged work, which is the point. Use `-D` only for a branch the user has dropped.
7. Run `git worktree prune` to clear stale worktree records.
8. Make the PC's own release copy with `scripts\release.ps1 -UpdateShortcut` (see [Releases](#releases)).

## GitHub and CI

The repository lives at [RLcafe/OpenSidecar](https://github.com/RLcafe/OpenSidecar) (remote `origin`). `main`, every `vX.Y.Z` tag and every open branch are pushed there; this folder stays the working copy.

- `.github/workflows/ci.yml` runs on every push to any branch and on pull requests: it fetches the pinned sensor files, then runs `build.ps1 -Test`, `package.ps1 -Test` and `build-android.ps1 -Test` on a GitHub Windows runner in one job (the Android checks read the phone-link sample the Windows checks save). The exe, setup exe and APK are kept with the run for 14 days.
- `.github/workflows/release.yml` runs when a `vX.Y.Z` tag is pushed. It checks the tag is on `main`, builds it, runs the same checks with the sensor files required, and publishes a GitHub Release named `Widget Screen vX.Y.Z` with the tag's CHANGELOG section as its notes. Actions → Release → Run workflow publishes (or refreshes) a tag pushed earlier.
- Pushes that only change Markdown files, `docs\` or `design specfication\` don't start a run.
- Tests run on GitHub (Ali, 1 Nov 2026): push and read the branch's CI run. A branch is ready to merge, and `main` ready to tag, only when its latest run is green. Local runs are still fine for quick feedback and renders, and CI doesn't replace trying the app on the PC.
- The runner has no touchscreen, audio devices, GPU sensors or Claude Code, so those integrations still need checking on the PC. CI's APK is signed with the runner's own debug key and won't install over a PC-built one. Releases use the PC's debug key when the `ANDROID_DEBUG_KEYSTORE_BASE64` repository secret holds it (`%USERPROFILE%\.android\debug.keystore` in base64); otherwise the release notes say to uninstall first.
- Release folders (`releases\`) and every build output stay out of git (`.gitignore`). Built files reach GitHub only as GitHub Release files and CI artifacts.

## Releases

Every merge into `main` gets a version and a **GitHub Release** (Ali, 1 Nov 2026) on [the Releases page](https://github.com/RLcafe/OpenSidecar/releases), made by `release.yml` from the pushed tag, with three files:

- `WidgetScreen-Setup-vX.Y.Z.exe`, the per-user installer with the hardware-sensor files.
- `WidgetScreen-vX.Y.Z-portable.zip`, the app folder to run without installing.
- `WidgetScreen-vX.Y.Z.apk`, the phone remote.

Never upload release files by hand or commit them. A failed Release run means the tag has no release yet: fix the cause on a branch, merge it as the next version, or rerun the workflow for the same tag if the failure wasn't in the code.

On the PC, the copy you actually use lives in `releases\vX.Y.Z\` inside this folder (git-ignored), never in `artifacts\` and never next to `Widget screen`. Each release folder is self-contained: `WidgetScreen.exe`, `WidgetScreen.exe.config`, `WidgetScreen.apk` (from v0.4.0, when the Android SDK is installed), `WidgetScreen-Setup-vX.Y.Z.exe` (from v0.12.0) and a `release.txt` naming the version and commit. Settings and the Spotify session stay in `%LOCALAPPDATA%\WidgetScreen\`, so every build shares them.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\release.ps1 -UpdateShortcut
```

- With no `-Version`, it builds the newest `vX.Y.Z` tag on `main`. It checks the tag out into a temporary `.worktrees\release-vX.Y.Z`, runs `build.ps1 -Test` there, copies the app into `releases\vX.Y.Z\` and removes the checkout, so uncommitted or later work never ends up in a release.
- `-UpdateShortcut` points the desktop "Widget Screen" shortcut at the new release. `-Force` rebuilds a release folder that already exists (close the app first).
- `release.ps1` also runs `scripts\package.ps1` in the tag's checkout, which packs the app into `WidgetScreen-Setup-vX.Y.Z.exe` and runs the sandboxed setup checks (`tests\Installer\installer-checks.ps1`). Running that setup on the PC installs or updates the copy that starts with Windows (see `docs\design\setup.md`); the desktop shortcut still opens the release folder.
- Keep older release folders until the new one has been used for a while, then delete them by hand.

## Versions

Versions follow [Semantic Versioning](https://semver.org/) as annotated tags `vMAJOR.MINOR.PATCH` on `main`.

- **MAJOR** stays `0` until the user calls a version 1.0 (for example, once it's been verified on the real touchscreen). After that, bump it when a change breaks something you rely on: settings that can't be migrated, or a widget or feature removed.
- **MINOR** for a new widget, a new feature, or a visible change in how something works (for example, the mixer output picker or the header clock). While MAJOR is `0`, a removed widget is a MINOR bump too.
- **PATCH** for bug fixes, touch and visual polish, and performance work that doesn't change behavior.
- **Every merge into `main` gets its own version, tag and GitHub Release** (Ali, 1 Nov 2026). A merge that only touches docs, tests, scripts or CI is a **PATCH**. If two branches are merged together, one tag covers both, and the highest bump wins.
- **Unreleased** in the changelog is only for commits made straight on `main` between merges (for example, a typo fix in a doc); they ship with the next tag.
