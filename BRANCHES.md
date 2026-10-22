# Branches

Every branch that hasn't been merged into `main` is listed here: its purpose, where it's checked out, where its test build lives, and anything to watch for when merging it. Released versions are in [CHANGELOG.md](CHANGELOG.md).

## Active branches

| Branch | Purpose | Based on | Status | Checkout | Test build |
| --- | --- | --- | --- | --- | --- |
| `feature/confirm-sheet` | Audit branch 5: themed confirm sheet on both apps, first-launch tips, Phone tab before Discord, tray mode off by default | `bf0af20` (v0.6.0) | In progress | `.worktrees\confirm-sheet` | `.worktrees\confirm-sheet\artifacts\app\WidgetScreen.exe`, `.worktrees\confirm-sheet\artifacts\android\WidgetScreen.apk` |

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
- Add a row to the table above when the branch is created, and keep its status and test build path current.
- A branch build uses your real preferences unless you start it with `--settings <path>`.

## After a merge

1. From the main folder, on `main`: `git merge --no-ff <branch>`.
2. Run `scripts\build.ps1 -Test` and `scripts\build-android.ps1 -Test` on `main` and confirm every check passes.
3. Pick the new version (see [Versions](#versions)), move the branch's changes into a new section of [CHANGELOG.md](CHANGELOG.md), and remove its row here. Commit that, then tag it: `git tag -a vX.Y.Z -m "<one-line summary>"`.
4. Remove the worktree with `git worktree remove ".worktrees\<name>"`. Its `artifacts\` goes with it. If git refuses because of untracked files, look at them first, then use `--force` only if nothing in them is worth keeping.
5. Delete any build of the branch made from the main folder, such as `artifacts\<name>`.
6. Delete the branch with `git branch -d <branch>`. Lowercase `-d` refuses to delete unmerged work, which is the point. Use `-D` only for a branch the user has dropped.
7. Run `git worktree prune` to clear stale worktree records.
8. Make the release copy with `scripts\release.ps1 -UpdateShortcut` (see [Releases](#releases)).

## Releases

The copy you actually use lives in `releases\vX.Y.Z\` inside this folder (git-ignored), never in `artifacts\` and never next to `Widget screen`. Each release folder is self-contained: `WidgetScreen.exe`, `WidgetScreen.exe.config`, `WidgetScreen.apk` (from v0.4.0, when the Android SDK is installed) and a `release.txt` naming the version and commit. Settings and the Spotify session stay in `%LOCALAPPDATA%\WidgetScreen\`, so every build shares them.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\release.ps1 -UpdateShortcut
```

- With no `-Version`, it builds the newest `vX.Y.Z` tag on `main`. It checks the tag out into a temporary `.worktrees\release-vX.Y.Z`, runs `build.ps1 -Test` there, copies the app into `releases\vX.Y.Z\` and removes the checkout, so uncommitted or later work never ends up in a release.
- `-UpdateShortcut` points the desktop "Widget Screen" shortcut at the new release. `-Force` rebuilds a release folder that already exists (close the app first).
- Keep older release folders until the new one has been used for a while, then delete them by hand.

## Versions

Versions follow [Semantic Versioning](https://semver.org/) as annotated tags `vMAJOR.MINOR.PATCH` on `main`.

- **MAJOR** stays `0` until the user calls a version 1.0 (for example, once it's been verified on the real touchscreen). After that, bump it when a change breaks something you rely on: settings that can't be migrated, or a widget or feature removed.
- **MINOR** for a new widget, a new feature, or a visible change in how something works (for example, the mixer output picker or the header clock). While MAJOR is `0`, a removed widget is a MINOR bump too.
- **PATCH** for bug fixes, touch and visual polish, and performance work that doesn't change behavior.
- Changes that only touch docs, tests or scripts don't need a version. List them under **Unreleased** in the changelog, and they ship with the next tag.
- Every merge that changes the app gets its own tag. If two branches are merged together, one tag covers both, and the highest bump wins.
