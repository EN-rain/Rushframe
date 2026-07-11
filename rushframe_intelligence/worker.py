"""Command-line worker used by Rushframe.Desktop and local agents."""

from __future__ import annotations

import argparse
import json
from dataclasses import asdict
from pathlib import Path

from rushframe_intelligence.agent_context import build_agent_context
from rushframe_intelligence.backend import serve
from rushframe_intelligence.capabilities import discover_capabilities
from rushframe_intelligence.context_index import MediaContextIndex
from rushframe_intelligence.pipeline import MediaIntelligencePipeline
from rushframe_intelligence.serialization import load_analysis


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="rushframe-intelligence")
    subparsers = parser.add_subparsers(dest="command", required=True)

    analyze = subparsers.add_parser("analyze", help="Analyze one local media file")
    analyze.add_argument("source", type=Path)
    analyze.add_argument("output", type=Path)
    analyze.add_argument("--ffmpeg")
    analyze.add_argument("--ffprobe")
    analyze.add_argument("--whisper-model", default="small")
    analyze.add_argument("--language")
    analyze.add_argument("--max-input-seconds", type=float, default=900.0)
    analyze.add_argument("--no-scenes", action="store_true")
    analyze.add_argument("--no-transcript", action="store_true")
    analyze.add_argument("--no-audio", action="store_true")
    analyze.add_argument("--visual-provider", choices=["none", "gemini", "qwen"], default="none")
    analyze.add_argument("--ocr", action="store_true")
    analyze.add_argument("--alignment", action="store_true")
    analyze.add_argument("--diarization", action="store_true")
    analyze.add_argument("--audio-events", action="store_true")
    analyze.add_argument("--embeddings", action="store_true")
    analyze.add_argument("--force", action="store_true")

    doctor = subparsers.add_parser("doctor", help="Report installed local analysis capabilities")
    doctor.add_argument("--ffmpeg")
    doctor.add_argument("--ffprobe")

    context = subparsers.add_parser("context", help="Build a compact context bundle for an editing agent")
    context.add_argument("analysis", type=Path)
    context.add_argument("--query", default="")
    context.add_argument("--role", action="append", default=[])
    context.add_argument("--limit", type=int, default=20)
    context.add_argument("--output", type=Path)

    search = subparsers.add_parser("search", help="Search an existing context index")
    search.add_argument("index", type=Path)
    search.add_argument("query")
    search.add_argument("--limit", type=int, default=12)
    search.add_argument("--role", action="append", default=[])
    search.add_argument("--min-score", type=float, default=0.0)
    search.add_argument("--max-duration", type=float)
    search.add_argument("--lexical-only", action="store_true")

    server = subparsers.add_parser("serve", help="Start the local Rushframe intelligence backend")
    server.add_argument("--host", default="127.0.0.1")
    server.add_argument("--port", type=int, default=7319)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    if args.command == "analyze":
        pipeline = MediaIntelligencePipeline(args.ffmpeg, args.ffprobe)
        result = pipeline.run(
            args.source,
            args.output,
            detect_visual_scenes=not args.no_scenes,
            transcribe_speech=not args.no_transcript,
            analyze_audio=not args.no_audio,
            understand_frames=args.visual_provider != "none",
            visual_provider=args.visual_provider if args.visual_provider != "none" else "gemini",
            whisper_model=args.whisper_model,
            language=args.language,
            max_input_seconds=args.max_input_seconds,
            enable_ocr=args.ocr,
            enable_alignment=args.alignment,
            enable_diarization=args.diarization,
            enable_audio_events=args.audio_events,
            enable_embeddings=args.embeddings,
            force=args.force,
        )
        print(json.dumps({
            "analysis_path": str(args.output / "media-analysis.json"),
            "scene_count": len(result.scenes),
            "transcript_count": len(result.transcript),
            "moment_count": len(result.moments),
            "warning_count": len(result.warnings),
        }))
        return 0

    if args.command == "doctor":
        print(json.dumps(discover_capabilities(args.ffmpeg, args.ffprobe), indent=2))
        return 0

    if args.command == "context":
        bundle = build_agent_context(
            load_analysis(args.analysis),
            query=args.query,
            roles=args.role,
            limit=args.limit,
        )
        rendered = json.dumps(bundle, ensure_ascii=False, indent=2)
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(rendered, encoding="utf-8")
            print(str(args.output))
        else:
            print(rendered)
        return 0

    if args.command == "serve":
        serve(args.host, args.port)
        return 0

    if args.command == "search":
        index = MediaContextIndex(args.index)
        results = index.search(
            args.query,
            limit=args.limit,
            roles=args.role,
            min_overall_score=args.min_score,
            max_duration=args.max_duration,
            semantic=not args.lexical_only,
        )
        print(json.dumps([asdict(result) for result in results], ensure_ascii=False, indent=2))
        return 0
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
