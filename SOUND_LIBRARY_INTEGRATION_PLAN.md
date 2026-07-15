# Rushframe Sound Library — Implemented Architecture and Remaining Roadmap

## Result

Rushframe now has a local-first, cross-project sound library with durable metadata, semantic-capable search, explicit project registration, undoable timeline placement, license guardrails, watched folders, waveform preview, collections, and controlled agent access.

The implementation preserves the product boundaries:

- Rushframe does not download or scrape sounds.
- Only user-selected files and user-approved local folders are indexed.
- UNC/network roots are rejected.
- Original source audio is never modified.
- Catalog-only results cannot be used by agents or added to the timeline until registered in the open project.
- Timeline mutations use the existing command, revision, autosave, undo/redo, lock, and approval paths.

## Implemented editor workflow

`Preferences > View > Sound Library` opens a modeless window that supports:

- natural-language and lexical search;
- semantic-search status and deterministic local fallback;
- Add Sounds and Add Folder;
- recursive watched-folder reindexing with debounce;
- category, mood, maximum duration, LUFS, BPM, license, favorite, and online/offline filters;
- All, Project used, and Recently used views;
- global/project collection storage and collection filtering;
- cached waveform display and Space/double-click audition;
- favorites and editable license/attribution metadata;
- find-similar search;
- explicit Register state for catalog-only sounds;
- Add at Playhead and drag/drop after registration;
- drag-over validation showing target track, snapped time, duration, or rejection reason;
- locked/incompatible track rejection without mutation;
- automatic creation of an audio track when dropped below existing tracks;
- exact undo and redo.

## Durable catalog

The default database is:

```text
%LocalAppData%\Rushframe\SoundLibrary\catalog.sqlite
```

Current catalog schema version: `4`.

Tables:

- `catalog_meta`
- `library_roots`
- `sounds`
- `sounds_fts`
- `embeddings`
- `collections`
- `collection_items`
- `project_usage`

Stored sound state includes:

- stable sound ID and canonical local path;
- SHA-256 content identity, file size, and modified time;
- duration, codec, channels, and sample rate;
- integrated loudness, peak, leading silence, and trailing silence;
- category, mood, tags, and optional tempo;
- license, attribution, attribution-required state, favorite, and offline state;
- cached waveform and optional normalized/trimmed derivative;
- source root, index time, analyzer version, and exact completed-feature list;
- hash or CLAP embedding provider/version and vector blob.

The index retains separate path records for exact duplicate files while sharing their content hash. Deleted or moved files are surfaced as offline. Existing project media preserves catalog identity and license metadata across save/reopen and relink.

## Ingest implementation

CLI entry points:

```text
python -m rushframe_intelligence index-sfx
python -m rushframe_intelligence search-sfx
python -m rushframe_intelligence sound-library-status
python -m rushframe_intelligence sound-library-get
```

Additional commands manage favorites, licenses, collections, and project usage.

Per-file ingest:

1. Resolve and validate the local path and approved-root containment.
2. Reject video-containing files and require an audio stream.
3. Calculate SHA-256 identity.
4. Probe duration, codec, channels, and sample rate.
5. Measure LUFS, peak, and silence with FFmpeg when available.
6. Generate a cached waveform PNG when FFmpeg is available.
7. Generate optional Essentia tempo metadata when installed.
8. Generate an always-available deterministic local hash embedding.
9. Generate a CLAP audio embedding only when an explicitly installed local checkpoint is configured.
10. Optionally generate normalized or silence-trimmed WAV derivatives in the Rushframe cache.
11. Atomically commit only the features that actually completed.

A source change, analyzer-version change, missing derivative/waveform, or newly requested incomplete feature forces reprocessing. Failed CLAP or Essentia stages are never falsely cached as complete.

## Semantic search implementation

Search applies bounded SQLite filters before ranking. Supported hard filters include:

- online/offline state;
- duration;
- LUFS range;
- BPM range;
- category;
- mood;
- license text;
- favorite state;
- collection;
- current-project usage;
- recent usage.

Ranking combines lexical/tag matching with cosine similarity. Result counts are bounded to 50 and ties are deterministic.

### CLAP policy

Real LAION-CLAP is optional and strictly local. Rushframe never downloads model weights automatically. It activates only when:

- `laion-clap` and its required Python dependencies are installed; and
- `RUSHFRAME_CLAP_CHECKPOINT` points to an existing local checkpoint.

Without that checkpoint, Rushframe uses the deterministic local hash-vector fallback and labels the search mode accordingly.

## Project registration and timeline behavior

The catalog is global, but the open Rushframe project remains the editing source of truth.

Registration creates a `MediaAsset` containing:

- catalog sound ID;
- SHA-256 fingerprint;
- local path;
- duration;
- license/attribution metadata;
- attribution-required state.

Registration uses `AddProjectMediaAssetCommand`. Batch import uses one `CompositeEditCommand`, so it creates one logical revision and undo entry.

Timeline insertion uses `SoundLibraryDropPlanner`, `AddPreparedTrackCommand`, and `AddClipCommand`. It validates all state before mutation, snaps to the sequence frame rate, rejects locked or incompatible tracks, and creates one atomic undoable edit.

## License and export guardrails

A used audible sound whose metadata says attribution is required but whose credit is blank blocks:

- manual export;
- agent timeline render;
- agent variant render.

The guard follows renderer-visible track state, including hidden, mute, solo, and item mute. The error identifies each affected sound and directs the user back to the Sound Library license editor.

License edits for registered project media use `UpdateProjectMediaLicenseCommand` and are exactly undoable.

## Controlled agent integration

The local intelligence MCP backend exposes `rushframe.search_sfx`.

Security behavior:

- the MCP caller cannot supply a catalog path or arbitrary file path;
- results contain stable IDs and safe metadata, not raw local file paths;
- the backend asks the editor for `sound-library-registrations`;
- a result is marked `can_use=true` only when its file is online and registered in the open project;
- catalog-only results remain read-only;
- actual timeline mutation still uses the existing revision, approval, audit, registered-media, and command contracts.

No sound-library route downloads media or exposes raw FFmpeg execution.

## Verification completed

Focused automated coverage includes:

- WAV indexing and real Python subprocess integration;
- metadata, loudness, waveform, and hash generation;
- semantic/hash ranking and lexical fallback;
- exact duplicate handling;
- duration, LUFS, BPM, category, mood, license, favorite, collection, project, recent, and offline filters;
- result bounds and deterministic tie ordering;
- path containment and UNC restrictions;
- stale analyzer/version retry;
- failed CLAP retry and no false completion state;
- local-checkpoint-only CLAP policy;
- watched-folder FileSystemWatcher debounce/reindex;
- project serializer roundtrip;
- undoable license updates;
- export attribution blocking;
- drag target planning, lock rejection, snapping, undo, and redo;
- MCP path hiding and registered-media-only result state.

A headed WPF run with `samplevid_audio.wav` verified:

- the real menu path and modeless window;
- indexed search result with measured LUFS;
- visible cached waveform;
- explicit project registration;
- Add at Playhead to `A1`;
- undo and redo;
- manual export blocked when required attribution was missing.

## Optional future enhancements

These are intentionally not required for the implemented sound-library foundation:

1. **Near-duplicate grouping** — persist CLAP-neighbor groups for alternate encodes/edits instead of only on-demand Similar search.
2. **Sound replacement plan** — choose a registered similar sound and preview an atomic media-reference replacement while preserving timeline timing.
3. **Auto-duck suggestion** — generate a previewable volume-animation plan for overlapping music/dialogue/SFX; require explicit approval before applying.
4. **Beat-grid storage and placement** — persist analyzed beat timestamps, then offer optional beat snapping in addition to normal frame snapping.
5. **Catalog relink management** — dedicated bulk relink UI for offline catalog entries.
6. **Large-catalog acceleration** — evaluate `sqlite-vec` only after measured latency shows the current bounded in-process cosine search is insufficient.

These additions must continue to use registered local media, controlled commands, exact undo, project revisions, approval, and canonical rendering.
