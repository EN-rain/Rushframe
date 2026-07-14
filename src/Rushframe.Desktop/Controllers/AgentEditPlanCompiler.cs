using System.Text.Json;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop.Controllers;

internal sealed record AgentEditPlanCompilation(
    bool Success,
    string PlanId,
    string Summary,
    IEditCommand? Command,
    IReadOnlyList<AgentEditOperationRecord> Operations,
    IReadOnlyList<AgentAffectedRange> AffectedRanges,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public static AgentEditPlanCompilation Fail(string planId, string error) =>
        new(false, planId, string.Empty, null, [], [], [], error);
}

/// <summary>
/// Validates and compiles a multi-operation edit plan into one atomic undo entry.
/// The compiler is intentionally deterministic and does not execute commands.
/// </summary>
internal sealed class AgentEditPlanCompiler
{
    private const int MaximumOperations = 100;
    private readonly AgentEditCommandFactory _factory;

    public AgentEditPlanCompiler(AgentEditCommandFactory factory)
    {
        _factory = factory;
    }

    public AgentEditPlanCompilation Compile(
        Project project,
        Sequence sequence,
        JsonElement payload,
        double playheadSeconds)
    {
        var planId = AgentPayloadReader.ReadString(payload, "plan_id") ?? Guid.NewGuid().ToString("N");
        if (!AgentPayloadReader.TryGetProperty(payload, "operations", out var operationsElement)
            || operationsElement.ValueKind != JsonValueKind.Array)
            return AgentEditPlanCompilation.Fail(planId, "An edit plan requires an operations array");
        if (operationsElement.GetArrayLength() == 0)
            return AgentEditPlanCompilation.Fail(planId, "The edit plan has no operations");
        if (operationsElement.GetArrayLength() > MaximumOperations)
            return AgentEditPlanCompilation.Fail(planId, $"The edit plan exceeds the {MaximumOperations}-operation limit");

        var commands = new List<IEditCommand>();
        var records = new List<AgentEditOperationRecord>();
        var affectedRanges = new List<AgentAffectedRange>();
        var warnings = new List<string>();
        var index = 0;
        foreach (var operation in operationsElement.EnumerateArray())
        {
            index++;
            if (operation.ValueKind != JsonValueKind.Object)
                return AgentEditPlanCompilation.Fail(planId, $"Operation {index} is not an object");
            var action = AgentPayloadReader.ReadString(operation, "action");
            if (string.IsNullOrWhiteSpace(action))
                return AgentEditPlanCompilation.Fail(planId, $"Operation {index} is missing action");
            if (!AgentEditCommandFactory.SupportedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
                return AgentEditPlanCompilation.Fail(planId, $"Operation {index} uses unsupported action '{action}'");

            var protectionError = ValidateProtection(sequence, operation, action);
            if (protectionError != null)
                return AgentEditPlanCompilation.Fail(planId, $"Operation {index}: {protectionError}");

            var result = _factory.Build(project, sequence, operation, action, playheadSeconds);
            if (!result.Success || result.Command == null)
                return AgentEditPlanCompilation.Fail(planId, $"Operation {index} ({action}) failed validation: {result.Error}");

            commands.Add(result.Command);
            records.Add(new AgentEditOperationRecord
            {
                Action = action,
                Summary = result.Summary,
                TargetId = ResolveTargetId(operation),
            });
            affectedRanges.AddRange(ResolveAffectedRanges(sequence, operation, action, result.Summary, playheadSeconds));
            AddOperationWarnings(project, sequence, operation, action, warnings);
        }

        var summary = AgentPayloadReader.ReadString(payload, "summary");
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = records.Count == 1
                ? records[0].Summary
                : $"Apply {records.Count} coordinated timeline edits";
        }

        DeduplicateRanges(affectedRanges);
        DeduplicateStrings(warnings);
        return new AgentEditPlanCompilation(
            true,
            planId,
            summary,
            new CompositeEditCommand(summary, commands),
            records,
            affectedRanges,
            warnings,
            null);
    }

    private static string? ValidateProtection(Sequence sequence, JsonElement operation, string action)
    {
        if (AgentPayloadReader.ReadString(operation, "item_id") is { Length: > 0 } itemText
            && Guid.TryParse(itemText, out var itemGuid))
        {
            var itemId = new TimelineItemId(itemGuid);
            foreach (var track in sequence.Tracks)
            {
                var item = track.Items.FirstOrDefault(candidate => candidate.Id == itemId);
                if (item == null) continue;
                if (track.Locked && !action.Equals("toggle_track_lock", StringComparison.OrdinalIgnoreCase)) return "Target track is locked";
                if (item.Locked) return "Target item is locked";
                break;
            }
        }

        if (AgentPayloadReader.ReadString(operation, "track_id") is { Length: > 0 } trackText
            && Guid.TryParse(trackText, out var trackGuid))
        {
            var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == new TrackId(trackGuid));
            if (track?.Locked == true
                && action is not ("toggle_track_lock" or "rename_track" or "reorder_track"))
                return "Target track is locked";
        }

        return null;
    }

    private static IEnumerable<AgentAffectedRange> ResolveAffectedRanges(
        Sequence sequence,
        JsonElement operation,
        string action,
        string summary,
        double playheadSeconds)
    {
        if (AgentPayloadReader.ReadString(operation, "item_id") is { Length: > 0 } itemText
            && Guid.TryParse(itemText, out var itemGuid))
        {
            var itemId = new TimelineItemId(itemGuid);
            foreach (var track in sequence.Tracks)
            {
                var item = track.Items.FirstOrDefault(candidate => candidate.Id == itemId);
                if (item == null) continue;
                yield return new AgentAffectedRange
                {
                    TrackId = track.Id.ToString(),
                    ItemId = item.Id.ToString(),
                    StartSeconds = item.TimelineStart.Seconds,
                    EndSeconds = item.TimelineEnd.Seconds,
                    Change = summary,
                };
                yield break;
            }
        }

        if (action is "add_text" or "add_caption" or "add_clip" or "add_music"
            or "add_captions_from_transcript" or "create_clip_from_transcript"
            or "assemble_best_moments" or "use_best_take")
        {
            var start = AgentPayloadReader.ReadSeconds(
                operation,
                AgentPayloadReader.HasProperty(operation, "timeline_start") ? "timeline_start" : "start",
                playheadSeconds);
            var duration = AgentPayloadReader.ReadSeconds(operation, "duration", AgentPayloadReader.ReadSeconds(operation, "maximum_duration", 3));
            yield return new AgentAffectedRange
            {
                TrackId = AgentPayloadReader.ReadString(operation, "track_id"),
                StartSeconds = Math.Max(0, start),
                EndSeconds = Math.Max(0, start + Math.Max(0, duration)),
                Change = summary,
            };
            yield break;
        }

        if (action.Contains("track", StringComparison.OrdinalIgnoreCase)
            || action is "update_sequence" or "clear_markers")
        {
            yield return new AgentAffectedRange
            {
                TrackId = AgentPayloadReader.ReadString(operation, "track_id"),
                StartSeconds = 0,
                EndSeconds = sequence.Duration.Seconds,
                Change = summary,
            };
        }
    }

    private static string? ResolveTargetId(JsonElement operation) =>
        AgentPayloadReader.ReadString(operation, "item_id")
        ?? AgentPayloadReader.ReadString(operation, "track_id")
        ?? AgentPayloadReader.ReadString(operation, "marker_id")
        ?? AgentPayloadReader.ReadString(operation, "media_asset_id");

    private static void AddOperationWarnings(
        Project project,
        Sequence sequence,
        JsonElement operation,
        string action,
        ICollection<string> warnings)
    {
        if (action == "remove_silence")
            warnings.Add("Silence removal replaces the target track atomically; inspect pacing and reaction tails before approval.");
        if (action == "assemble_best_moments")
            warnings.Add("Moment scores are suggestions from media analysis, not a substitute for human creative review.");
        if (action is "set_item_properties" or "set_transform" or "set_animation_channels")
        {
            var itemText = AgentPayloadReader.ReadString(operation, "item_id");
            if (Guid.TryParse(itemText, out var itemGuid))
            {
                var itemId = new TimelineItemId(itemGuid);
                var item = sequence.Tracks.SelectMany(track => track.Items).FirstOrDefault(candidate => candidate.Id == itemId);
                if (item?.MediaAssetId is { } assetId
                    && project.MediaLibrary.FirstOrDefault(asset => asset.Id == assetId)?.Kind == MediaKind.Audio)
                    warnings.Add($"Visual operation '{action}' targets an audio source; verify the item is not audio-only.");
            }
        }
    }

    private static void DeduplicateRanges(List<AgentAffectedRange> ranges)
    {
        var distinct = ranges
            .GroupBy(range => $"{range.TrackId}|{range.ItemId}|{range.StartSeconds:0.###}|{range.EndSeconds:0.###}|{range.Change}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        ranges.Clear();
        ranges.AddRange(distinct);
    }

    private static void DeduplicateStrings(List<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        values.Clear();
        values.AddRange(distinct);
    }
}
