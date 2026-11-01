# Branches

Every change reaches `main` through a **pull request on GitHub** (Ali, 1 Nov 2026), so parallel work never collides on `main`. Released versions are in [CHANGELOG.md](CHANGELOG.md).

## Active branches

The open pull requests are the list of active branches: [github.com/RLcafe/OpenSidecar/pulls](https://github.com/RLcafe/OpenSidecar/pulls). Each PR's description says what it's for, its status (**Draft** while in progress, **Ready for review** when built and waiting for Ali), its checkout (`.worktrees\<name>`) and anything to watch for when merging. Its CI run holds the test build (exe, setup and APK). A pushed branch with no PR yet is unfinished work that isn't ready to show.

## Closed without merging

None yet. When Ali drops a branch, close its PR without merging and add a line here naming the branch and why.

## Where branches live

- Every new branch gets a worktree **inside this folder**, at `.worktrees\<name>`. The folder is git-ignored. Never create folders next to `Widget screen` in `C:\Users\PCD\Documents\AI Apps\Claud`.
- Name branches `feature/<name>` for new behavior and `fix/<name>` for bug fixes. `<name>` is a few lowercase words joined by hyphens, and the worktree folder uses the same `<name>`.
- Start from the latest `main` on GitHub:

  ```powershell
  git fetch origin
  git worktree add ".worktrees\<name>" -b feature/<name> origin/main
  ```

- Build and test inside the worktree. The scripts resolve paths from their own folder, so the builds land in `.worktrees\<name>\artifacts\app\WidgetScreen.exe` and `.worktrees\<name>\artifacts\android\WidgetScreen.apk` and never lock the main app. `build.ps1` only compiles `src\` and `tests\`, so the worktrees under `.worktrees\` never leak into the main build.
- Push the branch with `git push -u origin <branch>`, then open a **draft pull request** to `main` (GitHub's "Compare & pull request"). Push again after each commit worth keeping; every push runs CI on the PR (see [GitHub and CI](#github-and-ci)).
- A branch build uses your real preferences unless you start it with `--settings <path>`.

## Merging a pull request

1. **Never commit or push to `main` directly.** Everything goes through a PR.
2. Keep the branch current: when `main` has moved, merge it into the branch (`git fetch origin` then `git merge origin/main`), fix any conflicts there, and push. Conflicts are always solved on the branch, never on `main`.
3. As the PR's last commit, pick the version (see [Versions](#versions)) and move the branch's changes into a new `## vX.Y.Z` section at the top of [CHANGELOG.md](CHANGELOG.md). If another PR takes that version first, merge `main` again and take the next one.
4. Mark the PR **Ready for review**. Merge it on GitHub with **Create a merge commit** (not squash or rebase, so history keeps each branch) only when its CI run is green, it has no conflicts, and Ali has approved it where it changes what he sees.
5. Tag the merge commit and push the tag, which publishes the GitHub Release (see [Releases](#releases)):

   ```powershell
   git checkout main
   git pull origin main
   git tag -a vX.Y.Z -m "<one-line summary>"
   git push origin vX.Y.Z
   ```

   Check that the Release run on the Actions tab finished green and the release has its three files.
6. Clean up: `git worktree remove ".worktrees\<name>"` (if git refuses because of untracked files, look at them first, and use `--force` only if nothing in them is worth keeping), delete any `artifacts\<name>` build, `git branch -d <branch>`, `git worktree prune`, and delete the branch on GitHub (the PR's **Delete branch** button). Lowercase `-d` refuses to delete unmerged work, which is the point. Use `-D` only for a branch Ali has dropped.
7. Make the PC's own release copy with `scripts\release.ps1 -UpdateShortcut` (see [Releases](#releases)).

## GitHub and CI

The repository lives at [RLcafe/OpenSidecar](https://github.com/RLcafe/OpenSidecar) (remote `origin`). `main`, every `vX.Y.Z` tag and every open branch are pushed there; this folder stays the working copy.

- `.github/workflows/ci.yml` runs on every push to any branch and on pull requests: it fetches the pinned sensor files, then runs `build.ps1 -Test`, `package.ps1 -Test` and `build-android.ps1 -Test` on a GitHub Windows runner in one job (the Android checks read the phone-link sample the Windows checks save). The exe, setup exe and APK are kept with the run for 14 days.
- `.github/workflows/release.yml` runs when a `vX.Y.Z` tag is pushed. It checks the tag is on `main`, builds it, runs the same checks with the sensor files required, and publishes a GitHub Release named `Widget Screen vX.Y.Z` with the tag's CHANGELOG section as its notes. Actions → Release → Run workflow publishes (or refreshes) a tag pushed earlier.
- Pushes that only change Markdown files, `docs\` or `design specfication\` don't start a run.
- Tests run on GitHub (Ali, 1 Nov 2026): push and read the PR's CI run. A PR is ready to merge only when its latest run is green. Local runs are still fine for quick feedback and renders, and CI doesn't replace trying the app on the PC.
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
- **Unreleased** in the changelog stays empty: every change arrives through a PR, and each PR writes its own version section.
