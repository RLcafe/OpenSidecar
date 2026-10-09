# Branches

Every branch that hasn't been merged into `main` is listed here: its purpose, where it's checked out, where its test build lives, and anything to watch for when merging it. Released versions are in [CHANGELOG.md](CHANGELOG.md).

## Active branches

| Branch | Purpose | Based on | Status | Checkout | Test build |
| --- | --- | --- | --- | --- | --- |

None right now. `feature/mixer-output-devices` and `feature/header-clock-stats` were merged into `v0.2.0`.

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

- Build and test inside the worktree. The scripts resolve paths from their own folder, so the build lands in `.worktrees\<name>\artifacts\app\WidgetScreen.exe` and never locks the main app. `build.ps1` only compiles `src\` and `tests\`, so the worktrees under `.worktrees\` never leak into the main build.
- Add a row to the table above when the branch is created, and keep its status and test build path current.
- A branch build uses your real preferences unless you start it with `--settings <path>`.

## After a merge

1. From the main folder, on `main`: `git merge --no-ff <branch>`.
2. Run `scripts\build.ps1 -Test` on `main` and confirm every check passes.
3. Pick the new version (see [Versions](#versions)), move the branch's changes into a new section of [CHANGELOG.md](CHANGELOG.md), and remove its row here. Commit that, then tag it: `git tag -a vX.Y.Z -m "<one-line summary>"`.
4. Remove the worktree with `git worktree remove ".worktrees\<name>"`. Its `artifacts\` goes with it. If git refuses because of untracked files, look at them first, then use `--force` only if nothing in them is worth keeping.
5. Delete any build of the branch made from the main folder, such as `artifacts\<name>`.
6. Delete the branch with `git branch -d <branch>`. Lowercase `-d` refuses to delete unmerged work, which is the point. Use `-D` only for a branch the user has dropped.
7. Run `git worktree prune` to clear stale worktree records.

## Versions

Versions follow [Semantic Versioning](https://semver.org/) as annotated tags `vMAJOR.MINOR.PATCH` on `main`.

- **MAJOR** stays `0` until the user calls a version 1.0 (for example, once it's been verified on the real touchscreen). After that, bump it when a change breaks something you rely on: settings that can't be migrated, or a widget or feature removed.
- **MINOR** for a new widget, a new feature, or a visible change in how something works (for example, the mixer output picker or the header clock). While MAJOR is `0`, a removed widget is a MINOR bump too.
- **PATCH** for bug fixes, touch and visual polish, and performance work that doesn't change behavior.
- Changes that only touch docs, tests or scripts don't need a version. List them under **Unreleased** in the changelog, and they ship with the next tag.
- Every merge that changes the app gets its own tag. If two branches are merged together, one tag covers both, and the highest bump wins.
