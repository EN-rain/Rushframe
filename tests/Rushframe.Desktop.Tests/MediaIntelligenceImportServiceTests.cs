using Rushframe.Infrastructure;
using Rushframe.Domain;

namespace Rushframe.Desktop.Tests;

public sealed class MediaIntelligenceImportServiceTests
{
    [Fact]
    public async Task import_reads_pipeline_json_and_skips_invalid_segments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"media-analysis-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
        {
          "source_path": "sample.mp4",
          "schema_version": "1.0",
          "scenes": [
            { "scene_id": "scene-1", "start": 1.0, "end": 3.5, "description": "Opening", "tags": ["wide", "wide"], "visual_energy": 0.8 },
            { "scene_id": "bad", "start": 4.0, "end": 2.0 }
          ],
          "transcript": [
            { "start": 1.2, "end": 2.4, "text": "  Hello there  " },
            { "start": 3.0, "end": 3.0, "text": "invalid" }
          ],
          "warnings": ["test warning"]
        }
        """);

        try
        {
            var asset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = "sample.mp4" };
            var service = new MediaIntelligenceImportService();

            var analysis = await service.ImportAsync(path, asset);

            Assert.Equal(asset.Id, analysis.MediaAssetId);
            Assert.Single(analysis.Scenes);
            Assert.Equal("Opening", analysis.Scenes[0].Description);
            Assert.Single(analysis.Scenes[0].Tags);
            Assert.Single(analysis.Transcript);
            Assert.Equal("Hello there", analysis.Transcript[0].Text);
            Assert.Equal(3, analysis.Warnings.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task import_reads_v2_moments_audio_and_word_timestamps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"media-analysis-v2-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
        {
          "source_path": "sample.mp4",
          "source_checksum": "sha256:test",
          "schema_version": "2.0",
          "analysis_version": 2,
          "metadata": {
            "duration": 12.5,
            "width": 1920,
            "height": 1080,
            "fps": 30,
            "has_video": true,
            "has_audio": true
          },
          "scenes": [
            {
              "scene_id": "scene_0001",
              "start": 0,
              "end": 4,
              "subjects": ["presenter"],
              "actions": ["shows product"],
              "editing_roles": ["demonstration"],
              "quality": { "visual_quality": 0.9, "sharpness": 0.8 }
            }
          ],
          "transcript": [
            {
              "segment_id": "transcript_0001",
              "start": 0.5,
              "end": 3.5,
              "text": "This is the result",
              "speaker": "SPEAKER_00",
              "words": [
                { "start": 0.5, "end": 0.8, "text": "This", "confidence": 0.95 }
              ]
            }
          ],
          "audio": {
            "integrated_loudness_lufs": -16.2,
            "events": [
              { "event_id": "audio_0001", "start": 2, "end": 4, "event_type": "music", "label": "music rise" }
            ]
          },
          "moments": [
            {
              "moment_id": "moment_0001",
              "start": 0,
              "end": 4,
              "summary": "Presenter reveals the result",
              "editing_roles": ["hook", "payoff"],
              "tags": ["product"],
              "scores": { "hook_potential": 0.9, "overall": 0.85 },
              "confidence": 0.92,
              "facts": { "duration": 4 }
            }
          ],
          "duplicate_take_groups": [
            {
              "group_id": "take_group_0001",
              "purpose": "Product reveal",
              "candidates": [
                { "moment_id": "moment_0001", "score": 0.85, "recommended": true }
              ]
            }
          ]
        }
        """);

        try
        {
            var asset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = "sample.mp4" };
            var service = new MediaIntelligenceImportService();

            var analysis = await service.ImportAsync(path, asset);

            Assert.Equal("sha256:test", analysis.SourceChecksum);
            Assert.Equal(2, analysis.AnalysisVersion);
            Assert.Equal(12.5, analysis.Metadata.Duration.Seconds, 3);
            Assert.Equal(1920, analysis.Metadata.Width);
            Assert.Equal(0.9, analysis.Scenes[0].Quality.VisualQuality);
            Assert.Equal("SPEAKER_00", analysis.Transcript[0].Speaker);
            Assert.Single(analysis.Transcript[0].Words);
            Assert.Equal(-16.2, analysis.Audio.IntegratedLoudnessLufs);
            Assert.Single(analysis.Audio.Events);
            Assert.Equal("moment_0001", Assert.Single(analysis.Moments).MomentId);
            Assert.True(analysis.DuplicateTakeGroups[0].Candidates[0].Recommended);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void store_in_project_replaces_analysis_for_same_asset()
    {
        var project = new Project();
        var asset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = "sample.mp4" };
        project.MediaLibrary.Add(asset);
        project.MediaIntelligence.Add(new MediaIntelligenceAnalysis { MediaAssetId = asset.Id, SourcePath = "old" });

        MediaIntelligenceImportService.StoreInProject(project, new MediaIntelligenceAnalysis
        {
            MediaAssetId = asset.Id,
            SourcePath = "new",
        });

        var stored = Assert.Single(project.MediaIntelligence);
        Assert.Equal("new", stored.SourcePath);
    }
}
