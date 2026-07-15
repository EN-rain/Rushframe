using System.Globalization;
using System.Text.Json;
using Rushframe.Domain;

namespace Rushframe.Infrastructure;

public sealed class MediaIntelligenceImportService
{
    public async Task<MediaIntelligenceAnalysis> ImportAsync(
        string analysisPath,
        MediaAsset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisPath);
        if (!File.Exists(analysisPath))
            throw new FileNotFoundException("Media analysis file was not found", analysisPath);

        await using var stream = File.OpenRead(analysisPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Media analysis JSON root must be an object");

        var analysis = new MediaIntelligenceAnalysis
        {
            MediaAssetId = asset.Id,
            SourcePath = ReadString(root, "source_path") ?? asset.OriginalPath,
            SourceChecksum = ReadString(root, "source_checksum") ?? string.Empty,
            SchemaVersion = ReadString(root, "schema_version") ?? "1.0",
            AnalysisVersion = ReadInt(root, "analysis_version") ?? 1,
            ImportedAt = DateTimeOffset.UtcNow,
            Metadata = ReadMetadata(root),
            Audio = ReadAudio(root),
        };

        ReadScenes(root, analysis);
        ReadTranscript(root, analysis);
        ReadMoments(root, analysis);
        ReadDuplicateTakes(root, analysis);

        if (root.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
        {
            analysis.Warnings.AddRange(warnings.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!));
        }

        return analysis;
    }

    public static void StoreInProject(Project project, MediaIntelligenceAnalysis analysis)
    {
        project.MediaIntelligence.RemoveAll(existing => existing.MediaAssetId == analysis.MediaAssetId);
        project.MediaIntelligence.Add(analysis);
        project.MediaRelationships.Clear();
        project.MediaRelationships.AddRange(MediaRelationshipBuilder.Build(project.MediaIntelligence));
    }

    private static MediaIntelligenceTechnicalMetadata ReadMetadata(JsonElement root)
    {
        if (!TryGetObject(root, "metadata", out var metadata)) return new();
        var duration = ReadDouble(metadata, "duration");
        return new MediaIntelligenceTechnicalMetadata
        {
            Duration = double.IsFinite(duration) && duration >= 0 ? MediaTime.FromSeconds(duration) : MediaTime.Zero,
            Width = ReadInt(metadata, "width"),
            Height = ReadInt(metadata, "height"),
            FramesPerSecond = ReadNullableDouble(metadata, "fps"),
            VideoCodec = ReadString(metadata, "video_codec"),
            AudioCodec = ReadString(metadata, "audio_codec"),
            AudioChannels = ReadInt(metadata, "audio_channels"),
            SampleRate = ReadInt(metadata, "sample_rate"),
            BitRate = ReadLong(metadata, "bit_rate"),
            Orientation = ReadString(metadata, "orientation"),
            VariableFrameRate = ReadNullableBool(metadata, "variable_frame_rate"),
            HasVideo = ReadBool(metadata, "has_video"),
            HasAudio = ReadBool(metadata, "has_audio"),
        };
    }

    private static void ReadScenes(JsonElement root, MediaIntelligenceAnalysis analysis)
    {
        if (!root.TryGetProperty("scenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array) return;
        foreach (var source in scenes.EnumerateArray())
        {
            var start = ReadDouble(source, "start");
            var end = ReadDouble(source, "end");
            start = NormalizeRangeStart(start, end);
            var sceneId = ReadString(source, "scene_id") ?? string.Empty;
            if (!IsValidRange(start, end))
            {
                analysis.Warnings.Add($"Skipped scene '{sceneId}' because its time range is invalid.");
                continue;
            }

            analysis.Scenes.Add(new MediaIntelligenceScene
            {
                SceneId = sceneId,
                Start = MediaTime.FromSeconds(start),
                End = MediaTime.FromSeconds(end),
                FramePath = ReadString(source, "frame_path"),
                FramePaths = ReadStrings(source, "frame_paths"),
                Description = ReadString(source, "description"),
                Summary = ReadString(source, "summary"),
                Tags = ReadStrings(source, "tags"),
                Subjects = ReadStrings(source, "subjects"),
                Actions = ReadStrings(source, "actions"),
                VisibleText = ReadStrings(source, "visible_text"),
                Location = ReadString(source, "location"),
                ShotType = ReadString(source, "shot_type"),
                CameraMotion = ReadString(source, "camera_motion"),
                Mood = ReadString(source, "mood"),
                VisualEnergy = ReadNullableDouble(source, "visual_energy"),
                Usable = ReadBool(source, "usable", fallback: true),
                Confidence = ReadNullableDouble(source, "confidence"),
                EditingRoles = ReadStrings(source, "editing_roles"),
                Quality = ReadQuality(source),
            });
        }
    }

    private static MediaIntelligenceQualityScores ReadQuality(JsonElement source)
    {
        if (!TryGetObject(source, "quality", out var quality)) return new();
        return new MediaIntelligenceQualityScores
        {
            VisualQuality = ReadNullableDouble(quality, "visual_quality"),
            AudioClarity = ReadNullableDouble(quality, "audio_clarity"),
            Sharpness = ReadNullableDouble(quality, "sharpness"),
            Exposure = ReadNullableDouble(quality, "exposure"),
            Stability = ReadNullableDouble(quality, "stability"),
            FaceVisibility = ReadNullableDouble(quality, "face_visibility"),
            TextReadability = ReadNullableDouble(quality, "text_readability"),
        };
    }

    private static void ReadTranscript(JsonElement root, MediaIntelligenceAnalysis analysis)
    {
        if (!root.TryGetProperty("transcript", out var transcript) || transcript.ValueKind != JsonValueKind.Array) return;
        var index = 0;
        foreach (var source in transcript.EnumerateArray())
        {
            index++;
            var start = ReadDouble(source, "start");
            var end = ReadDouble(source, "end");
            start = NormalizeRangeStart(start, end);
            var text = ReadString(source, "text");
            if (!IsValidRange(start, end) || string.IsNullOrWhiteSpace(text))
            {
                analysis.Warnings.Add("Skipped a transcript segment because its text or time range is invalid.");
                continue;
            }

            analysis.Transcript.Add(new MediaIntelligenceTranscriptSegment
            {
                SegmentId = ReadString(source, "segment_id") ?? $"transcript_{index:0000}",
                Start = MediaTime.FromSeconds(start),
                End = MediaTime.FromSeconds(end),
                Text = text.Trim(),
                Words = ReadWords(source, start, end),
                Speaker = ReadString(source, "speaker"),
                Confidence = ReadNullableDouble(source, "confidence"),
                Emotion = ReadString(source, "emotion"),
                Language = ReadString(source, "language"),
                ContainsFiller = ReadBool(source, "contains_filler"),
                RepeatedTake = ReadBool(source, "repeated_take"),
                HookScore = ReadNullableDouble(source, "hook_score"),
                RecommendedUse = ReadStrings(source, "recommended_use"),
            });
        }
    }

    private static List<MediaIntelligenceWord> ReadWords(JsonElement source, double fallbackStart, double fallbackEnd)
    {
        if (!source.TryGetProperty("words", out var words) || words.ValueKind != JsonValueKind.Array) return [];
        var result = new List<MediaIntelligenceWord>();
        foreach (var word in words.EnumerateArray())
        {
            var start = ReadDouble(word, "start");
            var end = ReadDouble(word, "end");
            var text = ReadString(word, "text") ?? ReadString(word, "word");
            if (!double.IsFinite(start)) start = fallbackStart;
            if (!double.IsFinite(end)) end = fallbackEnd;
            start = NormalizeRangeStart(start, end);
            if (end < start || string.IsNullOrWhiteSpace(text)) continue;
            result.Add(new MediaIntelligenceWord
            {
                Start = MediaTime.FromSeconds(Math.Max(0, start)),
                End = MediaTime.FromSeconds(Math.Max(0, end)),
                Text = text.Trim(),
                Confidence = ReadNullableDouble(word, "confidence") ?? ReadNullableDouble(word, "probability"),
            });
        }
        return result;
    }

    private static MediaIntelligenceAudioAnalysis ReadAudio(JsonElement root)
    {
        JsonElement audio;
        var hasAudio = TryGetObject(root, "audio", out audio);
        var music = default(JsonElement);
        var hasMusic = hasAudio && TryGetObject(audio, "music", out music);
        if (!hasMusic) hasMusic = TryGetObject(root, "music", out music);

        return new MediaIntelligenceAudioAnalysis
        {
            IntegratedLoudnessLufs = hasAudio ? ReadNullableDouble(audio, "integrated_loudness_lufs") : null,
            TruePeakDb = hasAudio ? ReadNullableDouble(audio, "true_peak_db") : null,
            MeanVolumeDb = hasAudio ? ReadNullableDouble(audio, "mean_volume_db") : null,
            MaxVolumeDb = hasAudio ? ReadNullableDouble(audio, "max_volume_db") : null,
            ClippingDetected = hasAudio && ReadBool(audio, "clipping_detected"),
            Silence = hasAudio ? ReadSilence(audio) : [],
            Events = hasAudio ? ReadAudioEvents(audio) : [],
            Music = hasMusic ? ReadMusic(music) : null,
        };
    }

    private static List<MediaIntelligenceSilenceRange> ReadSilence(JsonElement audio)
    {
        if (!audio.TryGetProperty("silence", out var ranges) || ranges.ValueKind != JsonValueKind.Array) return [];
        return ranges.EnumerateArray()
            .Select(source =>
            {
                var start = ReadDouble(source, "start");
                var end = ReadDouble(source, "end");
                return new
                {
                    Start = NormalizeRangeStart(start, end),
                    End = end,
                    Duration = ReadDouble(source, "duration"),
                };
            })
            .Where(value => IsValidRange(value.Start, value.End))
            .Select(value => new MediaIntelligenceSilenceRange
            {
                Start = MediaTime.FromSeconds(value.Start),
                End = MediaTime.FromSeconds(value.End),
                Duration = MediaTime.FromSeconds(double.IsFinite(value.Duration) && value.Duration >= 0
                    ? value.Duration
                    : value.End - value.Start),
            })
            .ToList();
    }

    private static List<MediaIntelligenceAudioEvent> ReadAudioEvents(JsonElement audio)
    {
        if (!audio.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) return [];
        var result = new List<MediaIntelligenceAudioEvent>();
        foreach (var source in events.EnumerateArray())
        {
            var start = ReadDouble(source, "start");
            var end = ReadDouble(source, "end");
            start = NormalizeRangeStart(start, end);
            if (!IsValidRange(start, end)) continue;
            result.Add(new MediaIntelligenceAudioEvent
            {
                EventId = ReadString(source, "event_id") ?? string.Empty,
                Start = MediaTime.FromSeconds(start),
                End = MediaTime.FromSeconds(end),
                EventType = ReadString(source, "event_type") ?? "unknown",
                Label = ReadString(source, "label"),
                Confidence = ReadNullableDouble(source, "confidence"),
                Speaker = ReadString(source, "speaker"),
                Clarity = ReadNullableDouble(source, "clarity"),
                Attributes = ReadObjectAsStrings(source, "attributes"),
            });
        }
        return result;
    }

    private static MediaIntelligenceMusicAnalysis ReadMusic(JsonElement music) => new()
    {
        TempoBpm = ReadNullableDouble(music, "tempo_bpm"),
        BeatTimes = ReadDoubles(music, "beat_times"),
        OnsetTimes = ReadDoubles(music, "onset_times"),
        RmsTimes = ReadDoubles(music, "rms_times"),
        RmsEnergy = ReadDoubles(music, "rms_energy"),
        Key = ReadString(music, "key"),
        Energy = ReadNullableDouble(music, "energy"),
    };

    private static void ReadMoments(JsonElement root, MediaIntelligenceAnalysis analysis)
    {
        if (!root.TryGetProperty("moments", out var moments) || moments.ValueKind != JsonValueKind.Array) return;
        foreach (var source in moments.EnumerateArray())
        {
            var start = ReadDouble(source, "start");
            var end = ReadDouble(source, "end");
            start = NormalizeRangeStart(start, end);
            if (!IsValidRange(start, end))
            {
                analysis.Warnings.Add("Skipped an editing moment because its time range is invalid.");
                continue;
            }
            analysis.Moments.Add(new MediaIntelligenceMoment
            {
                MomentId = ReadString(source, "moment_id") ?? string.Empty,
                Start = MediaTime.FromSeconds(start),
                End = MediaTime.FromSeconds(end),
                Summary = ReadString(source, "summary") ?? string.Empty,
                SceneIds = ReadStrings(source, "scene_ids"),
                TranscriptSegmentIds = ReadStrings(source, "transcript_segment_ids"),
                AudioEventIds = ReadStrings(source, "audio_event_ids"),
                Visual = ReadString(source, "visual"),
                Speech = ReadString(source, "speech"),
                Audio = ReadString(source, "audio"),
                EditingRoles = ReadStrings(source, "editing_roles"),
                Tags = ReadStrings(source, "tags"),
                Scores = ReadMomentScores(source),
                Confidence = ReadNullableDouble(source, "confidence") ?? 0,
                Evidence = ReadStrings(source, "evidence"),
                Facts = ReadObjectAsStrings(source, "facts"),
                Interpretation = ReadObjectAsStrings(source, "interpretation"),
            });
        }
    }

    private static MediaIntelligenceMomentScores ReadMomentScores(JsonElement source)
    {
        if (!TryGetObject(source, "scores", out var scores)) return new();
        return new MediaIntelligenceMomentScores
        {
            Importance = ReadNullableDouble(scores, "importance") ?? 0,
            HookPotential = ReadNullableDouble(scores, "hook_potential") ?? 0,
            EmotionalIntensity = ReadNullableDouble(scores, "emotional_intensity") ?? 0,
            Novelty = ReadNullableDouble(scores, "novelty") ?? 0,
            BrollUsefulness = ReadNullableDouble(scores, "broll_usefulness") ?? 0,
            Continuity = ReadNullableDouble(scores, "continuity") ?? 0,
            BrandRelevance = ReadNullableDouble(scores, "brand_relevance") ?? 0,
            Overall = ReadNullableDouble(scores, "overall") ?? 0,
        };
    }

    private static void ReadDuplicateTakes(JsonElement root, MediaIntelligenceAnalysis analysis)
    {
        if (!root.TryGetProperty("duplicate_take_groups", out var groups) || groups.ValueKind != JsonValueKind.Array) return;
        foreach (var source in groups.EnumerateArray())
        {
            var group = new MediaIntelligenceDuplicateTakeGroup
            {
                GroupId = ReadString(source, "group_id") ?? string.Empty,
                Purpose = ReadString(source, "purpose") ?? string.Empty,
            };
            if (source.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
            {
                foreach (var candidate in candidates.EnumerateArray())
                {
                    group.Candidates.Add(new MediaIntelligenceDuplicateTakeCandidate
                    {
                        MomentId = ReadString(candidate, "moment_id") ?? string.Empty,
                        Score = ReadNullableDouble(candidate, "score") ?? 0,
                        Recommended = ReadBool(candidate, "recommended"),
                    });
                }
            }
            analysis.DuplicateTakeGroups.Add(group);
        }
    }

    private static double NormalizeRangeStart(double start, double end) =>
        double.IsFinite(start) && start < 0 && double.IsFinite(end) && end > 0
            ? 0
            : start;

    private static bool IsValidRange(double start, double end) =>
        double.IsFinite(start) && double.IsFinite(end) && start >= 0 && end > start;

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value) =>
        parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double ReadDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var result)
            ? result
            : double.NaN;

    private static double? ReadNullableDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var result)
            ? result
            : null;

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var result)
            ? result
            : null;

    private static long? ReadLong(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var result)
            ? result
            : null;

    private static bool ReadBool(JsonElement parent, string name, bool fallback = false) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static bool? ReadNullableBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static List<string> ReadStrings(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<double> ReadDoubles(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.Number)
            .Select(element => element.TryGetDouble(out var result) ? result : double.NaN)
            .Where(double.IsFinite)
            .ToList();
    }

    private static Dictionary<string, string> ReadObjectAsStrings(JsonElement parent, string name)
    {
        if (!TryGetObject(parent, name, out var value)) return [];
        return value.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText(),
            StringComparer.OrdinalIgnoreCase);
    }
}
