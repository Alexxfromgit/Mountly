# Security policy

Mountly parses disc images downloaded from anywhere and can run commands as administrator, so
security reports are taken seriously.

## Reporting a vulnerability

Please **do not open a public issue**. Report it privately through
[GitHub's private vulnerability reporting](https://github.com/Alexxfromgit/Mountly/security/advisories/new)
(Security tab → *Report a vulnerability*). Include the version, the Windows version, steps to
reproduce, and the impact you see.

You can expect an acknowledgement within a few days. Once a fix is released, the advisory is
published with credit to the reporter unless you prefer otherwise.

## Supported versions

Only the latest release receives fixes.

## Scope

Of particular interest:

- Parsing of untrusted images and descriptors (`src/Mountly.Formats`, the ISO/UDF reading in
  `src/Mountly.Tools`), for example crashes, hangs or reads outside the image file.
- Paths from images or cue sheets escaping their folder when extracting or resolving files.
- The elevated CLI round trip (`ElevatedCli`, `--result-file`) and anything that lets another user or
  process get administrator rights through Mountly.
- Explorer integration and the installer/update mechanism.
