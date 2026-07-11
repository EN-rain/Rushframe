# Rushframe

A modern video editor for Windows, built with .NET 10 and WPF.

This repo is local-only. It does not include the old FastAPI/web editor runtime or publish/installer scripts.

## Features

- Multi-track timeline with drag-to-move, trim, split, ripple delete
- Video, image, text, and audio clips
- Undo/redo across all edits
- Ripple and snap modes
- Keyframe animation with 6 easing types
- Transitions, blend modes, masks, chroma key
- Color correction, effects stack, speed curves
- Legacy project import
- Background autosave

## Getting started

```
dotnet build Rushframe.slnx
dotnet run --project src/Rushframe.Desktop
```

## Requirements

- .NET 10 SDK
- Windows (WPF)
- FFmpeg and FFprobe
- Python 3.11+ for optional media intelligence

## Local media intelligence

Rushframe can turn source media into structured scenes, word-timed transcripts, audio measurements, editing moments, repeated-take groups, and a searchable SQLite context index.

Install the local core:

```powershell
.\scripts\setup-intelligence.ps1
```

Install optional OCR, speaker diarization, sound-event recognition, precise WhisperX alignment, and local Qwen visual understanding:

```powershell
.\scripts\setup-intelligence.ps1 -Advanced
```

The desktop Intelligence panel runs the worker locally and stores versioned analysis under Rushframe's local application-data directory. See `rushframe_intelligence/README.md` for CLI and output details.
