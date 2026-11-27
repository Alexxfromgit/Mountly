# Contributing to Mountly

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Getting started

You need the .NET 10 SDK (`global.json` pins the version) on Windows 10 or 11.

```powershell
./build.ps1 -Test
dotnet run --project src/Mountly.App
dotnet run --project src/Mountly.Cli -- --help
```

The integration tests (`tests/Mountly.Windows.Tests`) really mount images. They create ISO and BIN/CUE
files in your temp folder, mount them through Windows, and eject them again. They need no
administrator rights.

Set `MOUNTLY_HOME` to a folder to keep a development copy's settings, drives and cache away from your
real ones (`%APPDATA%\Mountly`, `%LOCALAPPDATA%\Mountly`).

## Pull requests

- Keep each PR to one change and describe what it does and how you tested it.
- Title the PR as a [Conventional Commit](https://www.conventionalcommits.org). PRs are squash-merged,
  and the title becomes the commit that drives versioning and release notes.
  - `feat: mount images into a fixed drive letter` → new feature, minor release
  - `fix: keep the tray menu after a refresh`, `perf: …`, `deps: …` → patch release
  - `feat!: …`, or a `BREAKING CHANGE:` line in the description → breaking change
  - `docs:`, `refactor:`, `test:`, `build:`, `ci:`, `chore:` → no release on their own, not in the notes

  Write the subject for users: it shows up as-is in the changelog and the in-app update notes.
- Add or update tests. Format readers are tested against images built at test time (`TestImages.cs`);
  please don't commit binary images.
- Follow the existing style; `.editorconfig` covers formatting. Builds must stay warning-free (warnings are errors).

## Safety rules

Mountly reads files people download from anywhere, and it runs commands as administrator when asked to:

- Parsers treat every image as hostile. Check bounds, cap sizes, and never trust an offset or length
  without validating it against the file.
- Paths that come from inside an image or a descriptor (a cue sheet's `FILE`, names in an ISO) must
  never reach outside the folder they belong to (see `CueSheet.ResolveFile` and `ImageInspector.SafeJoin`).
- Images are opened read-only, and ISOs always mount read-only.
- Anything that needs administrator rights goes through the CLI with one UAC prompt
  (`ElevatedCli`). The app itself always runs as the user.

## Reporting bugs

Use the issue templates. For security problems, follow [SECURITY.md](SECURITY.md) instead of opening
a public issue.
