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
  --visual-provider qwen --ocr --diarization --audio-events --embeddings
```

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
