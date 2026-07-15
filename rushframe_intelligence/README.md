# Rushframe Media Intelligence

This package converts source media into a versioned, searchable editing-context bundle for Rushframe and local agents.

## Outputs

Each analyzed asset receives:

- `media-analysis.json` — complete backward-compatible document
- `summary.json` — small always-loadable context
- `manifest.json` — source checksum, version and enabled features
- `scenes.json` and sampled `frames/`
- `transcript.json` with word timestamps
- `audio-events.json`
- `moments.json` — cross-modal editing moments and scores
- `duplicate-takes.json`
- `context.sqlite` — FTS5 search, plus optional semantic embeddings

## Install

```powershell
py -3 -m pip install -r requirements-intelligence.txt
```

Optional heavyweight models:

```powershell
py -3 -m pip install -r requirements-intelligence-advanced.txt
```

FFmpeg and FFprobe must be available through Rushframe's `.tools/bin` directory or `PATH`.

## Run

```powershell
py -3 -m rushframe_intelligence analyze input.mp4 output-folder --whisper-model small
```

Enable selected advanced stages only when their models are installed:

```powershell
py -3 -m rushframe_intelligence analyze input.mp4 output-folder `
  --visual-provider groq --ocr --alignment --diarization --audio-events --embeddings
```

Remote visual providers use sampled scene frames and structured JSON responses:

```powershell
$env:GROQ_API_KEY = "..."
py -3 -m rushframe_intelligence analyze input.mp4 output-folder --visual-provider groq

$env:CLOUDFLARE_ACCOUNT_ID = "..."
$env:CLOUDFLARE_API_TOKEN = "..."
py -3 -m rushframe_intelligence analyze input.mp4 output-folder --visual-provider cloudflare
```

The desktop settings dialog accepts multiple Groq keys and multiple Cloudflare account/token pairs. Credentials are protected with Windows DPAPI. GroqCloud is the default visual provider. The first use of a provider in an app session selects one credential and advances a persisted round-robin cursor; repeated requests in that session keep the same credential, while the next app session that uses the provider selects the next credential.

Search generated editing context:

```powershell
py -3 -m rushframe_intelligence search output-folder/context.sqlite "strong product reveal" --role payoff
```

Build a bounded JSON context bundle that any local agent can consume:

```powershell
py -3 -m rushframe_intelligence context output-folder/media-analysis.json `
  --query "fast product short" --role hook --limit 20 --output agent-context.json
```

Inspect installed capabilities:

```powershell
py -3 -m rushframe_intelligence doctor --ffmpeg .tools/bin/ffmpeg.exe
```

The pipeline is source-media based. Timeline edits do not invalidate analysis. A changed source checksum, analysis version, or newly requested feature invalidates the cache.

## Controlled agent-editing workflow

While the Rushframe desktop editor is open, the local MCP backend exposes a revision-safe workflow:

1. `rushframe.capabilities` returns the authoritative edit-skill catalog, including action parameters, preconditions, and warnings.
2. `rushframe.get_editing_context` returns a bounded, path-safe snapshot of the campaign, structured editing brief, tasks, playhead, selected item, timeline locks, registered-media readiness, and projected quality issues. It can include focused analyzed-media moments without exposing arbitrary local files.
3. `rushframe.preview_edit_plan` validates a multi-operation plan against `base_revision`, projects the full operation sequence on an isolated snapshot, and reports affected ranges and quality findings without changing the live project.
4. `rushframe.review_edit_plan` optionally renders the projected snapshot for a corrective review pass. The live project remains unchanged.
5. `rushframe.apply_edit_plan` shows the plan for user approval and applies all operations atomically as one undoable edit only when the revision still matches.

Use `rushframe.apply_timeline_edit` only for a single isolated operation. Coordinated edits should use an edit plan so partial application cannot occur. Refresh editing context after every successful mutation or revision conflict; manual edits always win through the project revision boundary.

## Local sound library

Rushframe also maintains a cross-project, local-only audio catalog. The desktop application stores it under:

```text
%LocalAppData%\Rushframe\SoundLibrary\catalog.sqlite
```

Use `RUSHFRAME_SOUND_LIBRARY_CATALOG` only to select another local catalog path for an isolated editor or QA process.

Index individual sounds or an approved local folder:

```powershell
py -3 -m rushframe_intelligence index-sfx `
  --catalog "$env:LOCALAPPDATA\Rushframe\SoundLibrary\catalog.sqlite" `
  --path .\sounds\impact.wav `
  --ffprobe .\.tools\bin\ffprobe.exe `
  --ffmpeg .\.tools\bin\ffmpeg.exe

py -3 -m rushframe_intelligence index-sfx `
  --catalog "$env:LOCALAPPDATA\Rushframe\SoundLibrary\catalog.sqlite" `
  --root .\sounds
```

Search by natural language plus hard metadata filters:

```powershell
py -3 -m rushframe_intelligence search-sfx "tense cinematic whoosh" `
  --max-duration 1.5 `
  --category transition `
  --min-lufs -24 `
  --max-tempo 140
```

Inspect catalog and watched-root state:

```powershell
py -3 -m rushframe_intelligence sound-library-status
```

The catalog stores resolved local paths, SHA-256 identity, probe metadata, loudness, peak, silence bounds, optional tempo, cached waveform paths, license/attribution, favorites, collections, project usage, and versioned embeddings. Originals are never modified. Optional normalized or silence-trimmed files are generated under the catalog derivative cache.

Semantic search always has an offline deterministic hash-vector fallback. Real LAION-CLAP embeddings are enabled only when both the package and an explicitly installed local checkpoint are available:

```powershell
$env:RUSHFRAME_CLAP_CHECKPOINT = "C:\Models\clap\music_audioset_epoch_15_esc_90.14.pt"
```

Rushframe does not download CLAP weights automatically. A failed CLAP, Essentia, waveform, loudness, or derivative stage is not recorded as complete; a later ingest retries it. Search results exposed through MCP omit raw file paths. Agents may use only results already registered in the open project, and timeline edits still require the editor's revision, approval, audit, and undoable command path.
