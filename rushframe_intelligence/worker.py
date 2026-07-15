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
from rushframe_intelligence.sound_library import SoundLibraryCatalog, default_catalog_path, serialize


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
    analyze.add_argument(
        "--visual-provider",
        choices=["none", "groq", "cloudflare"],
        default="none",
    )
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

    index_sfx = subparsers.add_parser("index-sfx", help="Index local audio into the durable Rushframe sound library")
    index_sfx.add_argument("--catalog", type=Path, default=None)
    index_sfx.add_argument("--path", type=Path, action="append", default=[])
    index_sfx.add_argument("--root", type=Path, action="append", default=[])
    index_sfx.add_argument("--no-recursive", action="store_true")
    index_sfx.add_argument("--no-watch", action="store_true")
    index_sfx.add_argument("--ffmpeg")
    index_sfx.add_argument("--ffprobe")
    index_sfx.add_argument("--no-clap", action="store_true")
    index_sfx.add_argument("--no-essentia", action="store_true")
    index_sfx.add_argument("--normalize", action="store_true")
    index_sfx.add_argument("--trim-silence", action="store_true")

    search_sfx = subparsers.add_parser("search-sfx", help="Search the local Rushframe sound library")
    search_sfx.add_argument("query", nargs="?", default="")
    search_sfx.add_argument("--catalog", type=Path, default=None)
    search_sfx.add_argument("--limit", type=int, default=20)
    search_sfx.add_argument("--max-duration", type=float)
    search_sfx.add_argument("--min-lufs", type=float)
    search_sfx.add_argument("--max-lufs", type=float)
    search_sfx.add_argument("--min-tempo", type=float)
    search_sfx.add_argument("--max-tempo", type=float)
    search_sfx.add_argument("--category")
    search_sfx.add_argument("--mood")
    search_sfx.add_argument("--license")
    search_sfx.add_argument("--favorites-only", action="store_true")
    search_sfx.add_argument("--include-offline", action="store_true")
    search_sfx.add_argument("--lexical-only", action="store_true")
    search_sfx.add_argument("--similar-to")
    search_sfx.add_argument("--collection-id")
    search_sfx.add_argument("--project-id")
    search_sfx.add_argument("--recently-used", action="store_true")

    sound_status = subparsers.add_parser("sound-library-status", help="Report local sound-library roots and index state")
    sound_status.add_argument("--catalog", type=Path, default=None)

    sound_get = subparsers.add_parser("sound-library-get", help="Read one indexed sound by stable ID or local path")
    sound_get.add_argument("--catalog", type=Path, default=None)
    sound_get_group = sound_get.add_mutually_exclusive_group(required=True)
    sound_get_group.add_argument("--sound-id")
    sound_get_group.add_argument("--path", type=Path)

    sound_favorite = subparsers.add_parser("sound-library-favorite", help="Set a sound favorite state")
    sound_favorite.add_argument("sound_id")
    sound_favorite.add_argument("--catalog", type=Path, default=None)
    sound_favorite.add_argument("--value", choices=["true", "false"], required=True)

    sound_license = subparsers.add_parser("sound-library-license", help="Update local license and attribution metadata")
    sound_license.add_argument("sound_id")
    sound_license.add_argument("--catalog", type=Path, default=None)
    sound_license.add_argument("--license", default="")
    sound_license.add_argument("--attribution", default="")
    sound_license.add_argument("--requires-attribution", action="store_true")

    sound_usage = subparsers.add_parser("sound-library-usage", help="Record project use of a catalog sound")
    sound_usage.add_argument("sound_id")
    sound_usage.add_argument("project_id")
    sound_usage.add_argument("media_asset_id")
    sound_usage.add_argument("--catalog", type=Path, default=None)

    collection_list = subparsers.add_parser("sound-library-collections", help="List global and project sound collections")
    collection_list.add_argument("--catalog", type=Path, default=None)
    collection_list.add_argument("--project-id")

    collection_create = subparsers.add_parser("sound-library-create-collection", help="Create a sound collection")
    collection_create.add_argument("name")
    collection_create.add_argument("--catalog", type=Path, default=None)
    collection_create.add_argument("--project-id")

    collection_delete = subparsers.add_parser("sound-library-delete-collection", help="Delete a sound collection")
    collection_delete.add_argument("collection_id")
    collection_delete.add_argument("--catalog", type=Path, default=None)

    collection_add = subparsers.add_parser("sound-library-add-to-collection", help="Add a sound to a collection")
    collection_add.add_argument("collection_id")
    collection_add.add_argument("sound_id")
    collection_add.add_argument("--catalog", type=Path, default=None)

    collection_remove = subparsers.add_parser("sound-library-remove-from-collection", help="Remove a sound from a collection")
    collection_remove.add_argument("collection_id")
    collection_remove.add_argument("sound_id")
    collection_remove.add_argument("--catalog", type=Path, default=None)

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
            visual_provider=args.visual_provider if args.visual_provider != "none" else "groq",
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

    if args.command == "index-sfx":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        aggregate = None
        for root in args.root:
            current = catalog.index_root(
                root,
                recursive=not args.no_recursive,
                watch_enabled=not args.no_watch,
                ffprobe=args.ffprobe,
                ffmpeg=args.ffmpeg,
                enable_clap=not args.no_clap,
                enable_essentia=not args.no_essentia,
                create_normalized_derivative=args.normalize,
                trim_silence=args.trim_silence,
            )
            if aggregate is None:
                aggregate = current
            else:
                aggregate.indexed.extend(current.indexed)
                aggregate.duplicates.extend(current.duplicates)
                aggregate.skipped.extend(current.skipped)
                aggregate.warnings.extend(current.warnings)
                aggregate.roots = current.roots
                aggregate.embedding_provider = current.embedding_provider
        if args.path:
            current = catalog.index_paths(
                args.path,
                ffprobe=args.ffprobe,
                ffmpeg=args.ffmpeg,
                enable_clap=not args.no_clap,
                enable_essentia=not args.no_essentia,
                create_normalized_derivative=args.normalize,
                trim_silence=args.trim_silence,
            )
            if aggregate is None:
                aggregate = current
            else:
                aggregate.indexed.extend(current.indexed)
                aggregate.duplicates.extend(current.duplicates)
                aggregate.skipped.extend(current.skipped)
                aggregate.warnings.extend(current.warnings)
                aggregate.roots = current.roots
                aggregate.embedding_provider = current.embedding_provider
        if aggregate is None:
            raise ValueError("index-sfx requires at least one --path or --root")
        print(json.dumps(serialize(aggregate), ensure_ascii=False, indent=2))
        return 0

    if args.command == "search-sfx":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        response = catalog.search(
            args.query,
            max_results=args.limit,
            max_duration=args.max_duration,
            min_lufs=args.min_lufs,
            max_lufs=args.max_lufs,
            min_tempo=args.min_tempo,
            max_tempo=args.max_tempo,
            category=args.category,
            mood=args.mood,
            license_name=args.license,
            favorites_only=args.favorites_only,
            online_only=not args.include_offline,
            semantic=not args.lexical_only,
            similar_to_sound_id=args.similar_to,
            collection_id=args.collection_id,
            project_id=args.project_id,
            recently_used=args.recently_used,
        )
        print(json.dumps(serialize(response), ensure_ascii=False, indent=2))
        return 0

    if args.command == "sound-library-status":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        print(json.dumps(serialize(catalog.status()), ensure_ascii=False, indent=2))
        return 0

    if args.command == "sound-library-get":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        sound = catalog.get_sound(sound_id=args.sound_id, path=args.path)
        if sound is None:
            raise ValueError("Indexed sound was not found")
        print(json.dumps(serialize(sound), ensure_ascii=False, indent=2))
        return 0

    if args.command == "sound-library-favorite":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        catalog.set_favorite(args.sound_id, args.value == "true")
        print(json.dumps({"ok": True, "sound_id": args.sound_id, "favorite": args.value == "true"}))
        return 0

    if args.command == "sound-library-license":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        catalog.update_license(
            args.sound_id,
            license_name=args.license,
            attribution=args.attribution,
            requires_attribution=args.requires_attribution,
        )
        print(json.dumps({"ok": True, "sound_id": args.sound_id}))
        return 0

    if args.command == "sound-library-usage":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        catalog.record_project_usage(args.project_id, args.media_asset_id, args.sound_id)
        print(json.dumps({"ok": True, "sound_id": args.sound_id}))
        return 0

    if args.command == "sound-library-collections":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        print(json.dumps(serialize(catalog.list_collections(args.project_id)), ensure_ascii=False, indent=2))
        return 0

    if args.command == "sound-library-create-collection":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        collection_id = catalog.create_collection(args.name, args.project_id)
        print(json.dumps({"ok": True, "collection_id": collection_id}))
        return 0

    if args.command == "sound-library-delete-collection":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        catalog.delete_collection(args.collection_id)
        print(json.dumps({"ok": True, "collection_id": args.collection_id}))
        return 0

    if args.command == "sound-library-add-to-collection":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        catalog.add_to_collection(args.collection_id, args.sound_id)
        print(json.dumps({"ok": True, "collection_id": args.collection_id, "sound_id": args.sound_id}))
        return 0

    if args.command == "sound-library-remove-from-collection":
        catalog = SoundLibraryCatalog(args.catalog or default_catalog_path())
        catalog.remove_from_collection(args.collection_id, args.sound_id)
        print(json.dumps({"ok": True, "collection_id": args.collection_id, "sound_id": args.sound_id}))
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
