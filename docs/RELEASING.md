# Releasing

Releases are automated with [Release Please](https://github.com/googleapis/release-please) and
[Velopack](https://velopack.io), like Emma Space and Ferry.

1. Merge PRs with Conventional Commit titles (`pr-title.yml` checks them).
2. Every push to `main` updates the **release PR**, which bumps `Version` in `Directory.Build.props`
   and updates `CHANGELOG.md`.
3. Merging the release PR creates the `vX.Y.Z` tag and a draft release. The same `release.yml` run
   then does the following:
   - tests on `win-x64`
   - publishes the app and the CLI into one self-contained folder (`build/publish.ps1`) for
     `win-x64` and `win-arm64`
   - packs them with Velopack (installer, portable zip, full and delta update packages)
   - uploads `SHA256SUMS.txt`
   - attests build provenance
   - publishes the release

To rebuild an existing release's assets, run the **Release** workflow by hand with its tag.

## One-time setup

- **`RELEASE_TOKEN`** (optional secret): a fine-grained PAT or GitHub App token with contents and
  pull-request write access. CI runs on the release PR only when it is opened with this token; the
  default `GITHUB_TOKEN` works otherwise.
- **Private vulnerability reporting:** turn it on under Settings → Code security.
- **Branch protection on `main`:** require the CI checks (`Windows`, `Windows ARM64`,
  `Linux (core libraries)`, `Workflow lint`) and squash merging.

## Code signing

Unsigned builds work, but SmartScreen warns on first run. Two options:

- **Azure Trusted Signing** (built into `vpk`): set the repository variables `AZURE_SIGNING_ENDPOINT`,
  `AZURE_SIGNING_ACCOUNT`, `AZURE_SIGNING_PROFILE`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and
  `AZURE_SUBSCRIPTION_ID`. The release workflow then signs everything it packs.
- **[SignPath Foundation](https://signpath.org)**: free certificates for open-source projects. Add a
  SignPath signing step for the publish folder before `vpk pack` (see SignPath's GitHub Actions docs);
  `vpk` then packs the signed files.

## Local packaging

```powershell
./build.ps1 -Pack     # artifacts/releases/win-x64: Setup.exe, Portable.zip, update packages
```
