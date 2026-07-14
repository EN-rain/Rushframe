# Removed Test Outputs

Cleanup performed on 2026-07-14.

Removed generated QA/test artifacts only:

- PNG screenshots and extracted video frames.
- FFmpeg metadata/decode/blackdetect/freezedetect text logs.
- Temporary Rushframe appdata/autosave/preview-cache folders used by QA runs.
- Contact-sheet frame output folder.
- Extra exported MP4 files from retests, except the original `rushframe_showcase_edit.mp4`.
- QA-only temporary project copies generated during non-audio retests.
- QA-only helper output/build folders under `qa testing/harness/bin`, `qa testing/harness/obj`, and `qa testing/scripts/QaExportFallback`.
- Remaining QA helper build outputs under `qa testing/performance/Rushframe.PerfWorkloads/bin|obj` and `qa testing/scripts/BatmanTikTokEdit/bin|obj`.
- QA-only temporary capture helper script: `qa testing/scripts/Capture-RushframeWindow.ps1`.
- Older manual-review generated outputs outside `showcase-edit`, including harness MP4 exports, extracted PNG/JPG frames, intelligence analysis JSON, and SQLite context output.

Kept:

- Markdown QA documentation and runbooks.
- Main showcase project: `rushframe_showcase_edit.rushframe`.
- Original video export: `rushframe_showcase_edit.mp4`.
