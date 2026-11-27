# Architecture

## Mounting

```
App / CLI ──▶ MountService (Core) ──▶ IMountBackend ──▶ StorageMountBackend (Windows)
                   │                                         └─ MSFT_DiskImage (CIM), root/Microsoft/Windows/Storage
                   └─ IImageConverter ──▶ IsoConverter (Formats) ──▶ cached ISO in %LOCALAPPDATA%\Mountly\cache
```

- **Native formats** (ISO, VHD, VHDX) go straight to the backend.
- **Other optical formats** are converted to an ISO first (`AppPaths.CachedIsoFor`). The same source
  reuses its ISO while it is unchanged. `mounts.json` maps the cached ISO back to the user's file, so
  listing and ejecting work with the file the user picked. Ejecting deletes the ISO, and a converted
  ISO whose mount disappeared (ejected in Explorer, say) is cleaned up on the next listing.
- `MountService` is shared by the app and the CLI. Its state lives on disk, guarded by a named mutex
  (`JsonFile.Update`), so both can run at once.

### The Windows Storage provider

`MSFT_DiskImage` is the class behind Explorer's "Mount" and PowerShell's `Mount-DiskImage`.

- **Looking an image up:** `GetInstance` with the class's keys, `ImagePath` and `StorageType` (1 ISO,
  2 VHD, 3 VHDX). This works whether or not the image is attached.
- **Mounting and ejecting:** call `Mount(Access, NoDriveLetter)` and `Dismount()` on that instance.
- **Listing:** the provider doesn't enumerate attached images by itself. `ListAsync` walks
  `MSFT_Volume` → `MSFT_DiskImageToVolume` instead.
- **Drive letters:** `IOCTL_STORAGE_GET_DEVICE_NUMBER` on the image's device (`\\.\CDROMn`) is
  matched against each drive letter's volume.
- **Paths:** the service runs as SYSTEM, so `NormalizePath` resolves the file's final path from a
  handle first. That covers subst drives, links, and the per-package file redirection a packaged
  parent process can impose.
- **Rights:** ISO mounting needs no administrator rights. VHD/VHDX mounting and moving drive letters
  do. The app runs `mountly-cli` elevated for those (`ElevatedCli`, one UAC prompt, result passed back
  in a JSON file).

## Image formats

Every reader produces a `DiscLayout`: tracks with file, offset, sector size, user-data offset and
sector count. `TrackStream` then exposes the first data track's 2048-byte user data as a seekable stream.

| Format | Descriptor | Notes |
|---|---|---|
| CUE/BIN | text cue sheet | `FILE` names are resolved only inside the cue's folder; a renamed `.bin` with the cue's name is accepted |
| NRG | chunks at the end (`NER5` v2 / `NERO` v1 footer, big-endian) | DAOX/DAOI (disc-at-once) and ETN2/ETNF (track-at-once); falls back to the 150-sector Nero pregap |
| MDS/MDF | binary `MEDIA DESCRIPTOR` (little-endian) | sessions → track blocks → extra blocks for lengths; a lone `.mdf` is probed |
| CCD/IMG | INI | raw 2352-byte sectors; `MODE` per track |
| Raw BIN/IMG | none | `RawLayout.Detect` looks for CD001/BEA01/NSR0x at sector 16 under each layout, then for sync headers |

Raw sectors keep their user data at offset 16 (Mode 1), 24 (Mode 2 Form 1, after the subheader) or
8 (2336-byte Mode 2). Where possible the mode comes from each track's first sector rather than from
the descriptor.

## App

Avalonia 12 with MVVM (CommunityToolkit.Mvvm source generators).

- **Pages:** `DrivesViewModel`, `CatalogViewModel`, `ToolsViewModel` and `SettingsViewModel`, held by
  `MainWindowViewModel`.
- **Drives:** Windows only has a device while an image is attached, so a drive in Mountly is a
  `DriveSlot` in `drives.json` holding its preferred letter and last image. The drives page reconciles
  slots with what Windows reports every 3 seconds while the window is visible. It also shows images
  mounted outside Mountly.
- **Single instance:** `SingleInstance` uses a named mutex plus a named pipe scoped to the current
  user. Explorer verbs and double-clicked images start a second process, which hands its request over
  and exits.
- **Tray:** the tray icon's menu is rebuilt after every refresh.
- **Updates:** Velopack, with `GithubSource` reading the GitHub releases of this repository.
