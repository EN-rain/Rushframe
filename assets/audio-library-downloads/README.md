# Rushframe free audio starter packs

This folder contains a guarded downloader for four official Kenney audio packs:

- Interface Sounds — 100 files
- Digital Audio — 60 files
- RPG Audio — 50 files
- Impact Sounds — 130 files

All four source pages identify the packs as **Creative Commons CC0 1.0**. Attribution is not required, although retaining source and licence records is recommended.

## Download into the Rushframe folder

From the Rushframe repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File ".\assets\audio-library-downloads\Download-FreeAudioPacks.ps1"
```

The extracted audio will be placed under:

```text
assets\audio-library-downloads\library\
```

The script also writes `download-manifest.json` containing each official source page, direct official download URL, licence, actual SHA-256 archive hash and extracted audio-file count.

Use `-KeepArchives` to retain the ZIP files or `-Force` to replace already extracted packs:

```powershell
powershell -ExecutionPolicy Bypass -File ".\assets\audio-library-downloads\Download-FreeAudioPacks.ps1" -KeepArchives -Force
```

## Safety behavior

The script:

- downloads only from fixed official `kenney.nl` URLs;
- refuses packs above 300 MB and a combined known size above 900 MB;
- downloads through unique `.partial-*` files;
- verifies the ZIP signature before publication;
- extracts through a unique staging directory;
- replaces existing extracted folders only after a valid archive is available;
- records source, licence, file count and SHA-256 information;
- never modifies original audio files after extraction.

## Source and licence pages

- https://kenney.nl/assets/interface-sounds
- https://kenney.nl/assets/digital-audio
- https://kenney.nl/assets/rpg-audio
- https://kenney.nl/assets/impact-sounds
- https://creativecommons.org/publicdomain/zero/1.0/

The downloader intentionally does not automate account-gated sites such as Freesound, ZapSplat or YouTube Audio Library. Assets from those services should be downloaded manually after reviewing the individual item licence, then imported into Rushframe as local media.
