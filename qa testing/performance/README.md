# Rushframe Performance QA

This directory contains repeatable workloads and capture scripts for the editor optimization gates. Generated projects and trace output belong under `qa testing/results/performance/`.

## Workloads

Run:

```powershell
& '.\qa testing\performance\Generate-Workloads.ps1'
```

Generated projects:

| Project | Purpose |
|---|---|
| `perf-small.rushframe` | 50 clips, 4 tracks; basic interaction baseline |
| `perf-medium.rushframe` | 500 clips, 12 tracks; normal large project |
| `perf-large.rushframe` | 1,200 clips, 20 tracks; stress timeline culling/indexes |
| `perf-animation.rushframe` | 8 clips, 8 channels/clip, 100 keyframes/channel |
| `perf-audio.rushframe` | 288 audio clips across 12 tracks |
| `perf-exact-preview.rushframe` | color/mask composition that forces exact FFmpeg preview |

The generator uses `samplevid.mp4` and `samplevid_audio.wav` by default. Pass different paths when testing larger media.

## Baseline package

```powershell
& '.\qa testing\performance\Run-PerformanceBaseline.ps1'
```

This records machine metadata, regenerates workloads, runs Release build/tests, and creates a timestamped baseline folder.

## Runtime telemetry

Set before launching the app:

```powershell
$env:RUSHFRAME_PERF = '1'
```

Rushframe then retains rolling detailed samples in addition to its `System.Diagnostics.Metrics` instruments. Relevant meters:

- `Rushframe.Editor`
- `Rushframe.Media`

Metrics cover timeline rendering, input handlers, preview frames/drops, active layers, project snapshot/write time, coalesced saves, thumbnail-cache hit/miss, startup milestones, FFmpeg job duration, failures, and active jobs.

## Trace capture

Launch Rushframe Release, note its PID, then run:

```powershell
& '.\qa testing\performance\Collect-PerformanceTrace.ps1' -ProcessId 12345 -DurationSeconds 60
```

Open `.nettrace` files with PerfView, Visual Studio, or SpeedScope-compatible conversion. For Windows compositor/GPU diagnosis, capture an additional Windows Performance Recorder trace using the General Profile + GPU Activity profiles.

## Memory retention

After warming the project and looping preview for ten minutes:

```powershell
& '.\qa testing\performance\Capture-MemoryDump.ps1' -ProcessId 12345
```

Repeat after project close/reopen and compare retained `MediaElement`, `BitmapSource`, timeline layer, cancellation-source, and project-model counts.

## Manual scenarios

For every project, capture:

1. Cold and warm startup to interactive shell.
2. Timeline horizontal pan and zoom.
3. Rapid clip drag and trim for 30 seconds.
4. Scrubbing across the entire timeline.
5. Playback for 60 seconds with dropped-frame observation.
6. Media search with 10 successive keystrokes.
7. Save and autosave during continued interaction.
8. Exact-preview seeks across at least five chunk boundaries.
9. Ten-minute loop and memory dump.
10. Preview/export equivalence at three timestamps.

## Gates

- Normal input handler P95 `< 16 ms`; drag/trim P95 `< 8 ms`.
- No normal-edit UI stall `> 100 ms`.
- Preview frame P95 `< 33.3 ms` at 30 fps; dropped frames `< 2%`.
- Play/pause `< 100 ms`; cached seek `< 150 ms`.
- Media filtering `< 100 ms` after debounce for 5,000 items.
- Project edit execution performs no file I/O.
- Ten-minute loop retained-memory growth `< 10%` after warm-up.
- Exact preview cache remains within its 1 GB / 160-file cap.
- Preview/export visual correctness remains unchanged.

Hardware-sensitive performance gates should run on a fixed baseline machine. CI should enforce functional tests, allocation checks, cache behavior, and deterministic pure-code benchmarks.
