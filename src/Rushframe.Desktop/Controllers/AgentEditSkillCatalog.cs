namespace Rushframe.Desktop.Controllers;

internal sealed record AgentEditParameterDefinition(
    string Name,
    string Type,
    bool Required,
    string Description);

internal sealed record AgentEditSkillDefinition(
    string Action,
    string Category,
    string Summary,
    IReadOnlyList<AgentEditParameterDefinition> Parameters,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Authoritative, machine-readable documentation for the controlled agent edit
/// protocol. Keep this catalog aligned with <see cref="AgentEditCommandFactory"/>
/// so external agents do not have to infer payload fields from failures.
/// </summary>
internal static class AgentEditSkillCatalog
{
    public const string SchemaVersion = "1.0";

    public static IReadOnlyList<AgentEditSkillDefinition> Skills { get; } =
    [
        Skill("add_text", "text", "Add a styled text item, creating an unlocked text track when necessary.",
            [Req("text", "string", "Text content."), Opt("track_id", "uuid", "Unlocked text or overlay track."), Opt("start", "number", "Timeline seconds; defaults to playhead."), Opt("duration", "number", "Duration seconds; defaults to 3."), ..TextStyle(), ..TextTransform()]),
        Skill("add_caption", "text", "Add a styled caption item; payload is the same as add_text.",
            [Req("text", "string", "Caption content."), Opt("track_id", "uuid", "Unlocked text or overlay track."), Opt("start", "number", "Timeline seconds; defaults to playhead."), Opt("duration", "number", "Duration seconds; defaults to 3."), ..TextStyle(), ..TextTransform()]),
        Skill("add_clip", "media", "Insert registered local media without modifying the source file.",
            [Req("media_asset_id", "uuid", "Registered project media asset."), Opt("track_id", "uuid", "Compatible unlocked destination track."), Opt("start", "number", "Timeline seconds; defaults to playhead."), Opt("source_start", "number", "Source seconds; defaults to 0."), Opt("duration", "number", "Timeline/source duration seconds."), Opt("fade_in", "number", "Fade-in seconds."), Opt("fade_out", "number", "Fade-out seconds."), Opt("volume", "number", "Linear gain from 0 to 4.")],
            ["The media asset must be registered, online, and compatible with the destination track."]),
        Skill("add_music", "media", "Insert registered local audio/music; payload is the same as add_clip.",
            [Req("media_asset_id", "uuid", "Registered project audio asset."), Opt("track_id", "uuid", "Compatible unlocked destination track."), Opt("start", "number", "Timeline seconds; defaults to playhead."), Opt("source_start", "number", "Source seconds; defaults to 0."), Opt("duration", "number", "Timeline/source duration seconds."), Opt("fade_in", "number", "Fade-in seconds."), Opt("fade_out", "number", "Fade-out seconds."), Opt("volume", "number", "Linear gain from 0 to 4.")],
            ["The media asset must be registered, online, and compatible with the destination track."]),
        Skill("move_clip", "timing", "Move an item in time, to another compatible track, or to a new collection index.",
            [Req("item_id", "uuid", "Timeline item to move."), Opt("track_id", "uuid", "Unlocked compatible destination track."), Opt("start", "number", "New timeline start seconds."), Opt("index", "integer", "New destination collection index.")],
            ["Source item, source track, destination track, and item must be unlocked."]),
        Skill("trim_clip", "timing", "Trim an item's timeline start, duration, and/or source start.",
            [Req("item_id", "uuid", "Timeline item to trim."), Opt("start", "number", "New timeline start seconds."), Opt("duration", "number", "New duration seconds."), Opt("source_start", "number", "New source start seconds.")],
            ["At least one trim field should be supplied.", "The item and containing track must be unlocked."]),
        Skill("split_clip", "timing", "Split an item at an absolute timeline time.",
            [Req("item_id", "uuid", "Timeline item to split."), Opt("time", "number", "Absolute timeline seconds; defaults to item midpoint.")],
            ["Split time must fall inside the item and the item/track must be unlocked."]),
        Skill("delete_clip", "timing", "Delete one timeline item without shifting later items.", [Req("item_id", "uuid", "Timeline item to delete.")], ["The item and containing track must be unlocked."]),
        Skill("ripple_delete_clip", "timing", "Delete one item and close the resulting gap according to the domain ripple rules.", [Req("item_id", "uuid", "Timeline item to delete.")], ["The item and all affected content must be unlocked."]),
        Skill("duplicate_clip", "timing", "Duplicate a timeline item with all supported properties preserved.", [Req("item_id", "uuid", "Timeline item to duplicate.")], ["The item and containing track must be unlocked."]),
        Skill("set_transform", "visual", "Replace an item's 2D transform values.",
            [Req("item_id", "uuid", "Timeline item to transform."), ..Transform()],
            ["The item and containing track must be unlocked."]),
        Skill("set_item_properties", "visual", "Update one or more visual, audio, speed, crop, color, or stabilization properties atomically.",
            [Req("item_id", "uuid", "Timeline item to update."), Opt("opacity", "number", "0 to 1."), Opt("volume", "number", "0 to 4."), Opt("pan", "number", "-1 to 1."), Opt("muted", "boolean", "Mute item audio."), Opt("reversed", "boolean", "Reverse playback."), Opt("speed", "number", "Constant playback speed from 0.05 to 100."), Opt("fade_in", "number", "Fade-in seconds."), Opt("fade_out", "number", "Fade-out seconds."), Opt("blend_mode", "string", "BlendMode enum name."), Opt("crop_left", "number", "Normalized crop 0 to 1."), Opt("crop_top", "number", "Normalized crop 0 to 1."), Opt("crop_right", "number", "Normalized crop 0 to 1."), Opt("crop_bottom", "number", "Normalized crop 0 to 1."), Opt("color", "object", "Color-correction object."), Opt("stabilization", "object", "Stabilization settings object.")],
            ["At least one property besides item_id is required.", "The item and containing track must be unlocked."]),
        Skill("set_text_content", "text", "Replace the content of an existing text item.", [Req("item_id", "uuid", "Text item."), Req("text", "string", "New text content.")], ["Target must be an unlocked text item on an unlocked track."]),
        Skill("set_text_properties", "text", "Update styling on an existing text item.",
            [Req("item_id", "uuid", "Text item."), ..TextStyle(), Opt("shadow_offset_x", "number", "Shadow X offset."), Opt("shadow_offset_y", "number", "Shadow Y offset."), Opt("shadow_blur", "number", "Shadow blur amount.")],
            ["Target must be an unlocked text item on an unlocked track."]),
        Skill("add_transition", "transition", "Create or replace a transition between two adjacent timeline items.",
            [Req("left_item_id", "uuid", "Left item."), Req("right_item_id", "uuid", "Right item."), Opt("kind", "string", "TransitionKind enum; defaults to CrossDissolve."), Opt("duration", "number", "Duration seconds; defaults to 0.5."), Opt("alignment", "number", "0 to 1 alignment."), Opt("audio_mode", "string", "TransitionAudioMode enum.")],
            ["Both items and their tracks must be unlocked and transition-compatible."]),
        Skill("add_effect", "effect", "Append a registered effect instance to an item.", [Req("item_id", "uuid", "Timeline item."), Req("effect_type_id", "string", "Registered effect type ID."), Opt("parameters", "object", "Effect parameter values.")], ["The item and containing track must be unlocked."]),
        Skill("remove_effect", "effect", "Remove an effect instance from an item.", [Req("item_id", "uuid", "Timeline item."), Req("effect_id", "uuid", "Effect instance ID.")], ["The item and containing track must be unlocked."]),
        Skill("update_effect", "effect", "Update an effect instance's enabled state and parameters.", [Req("item_id", "uuid", "Timeline item."), Req("effect_id", "uuid", "Effect instance ID."), Opt("enabled", "boolean", "Enabled state; defaults to true."), Opt("parameters", "object", "Replacement parameter values.")], ["The item and containing track must be unlocked."]),
        Skill("reorder_effect", "effect", "Move an effect instance within an item's effect stack.", [Req("item_id", "uuid", "Timeline item."), Req("effect_id", "uuid", "Effect instance ID."), Req("index", "integer", "New zero-based effect index.")], ["The item and containing track must be unlocked."]),
        Skill("set_animation_channels", "animation", "Replace all animation channels on an item.", [Req("item_id", "uuid", "Timeline item."), Req("channels", "array", "AnimationChannel objects with normalized keyframes.")], ["The item and containing track must be unlocked."], ["This replaces the complete animation-channel collection."]),
        Skill("set_masks", "visual", "Replace all masks on an item.", [Req("item_id", "uuid", "Timeline item."), Req("masks", "array", "Mask objects.")], ["The item and containing track must be unlocked."], ["This replaces the complete mask collection."]),
        Skill("set_chroma_key", "visual", "Set or clear chroma-key settings on an item.", [Req("item_id", "uuid", "Timeline item."), Opt("clear", "boolean", "Clear chroma key when true."), Opt("color", "string", "Key color; defaults to #00FF00."), Opt("similarity", "number", "0 to 1."), Opt("intensity", "number", "0 to 1."), Opt("edge_softness", "number", "0 to 1."), Opt("spill_suppression", "number", "0 to 1."), Opt("shadow_suppression", "boolean", "Enable shadow suppression.")], ["The item and containing track must be unlocked."]),
        Skill("add_marker", "marker", "Add a timeline marker.", [Opt("time", "number", "Timeline seconds; defaults to playhead."), Opt("label", "string", "Marker label."), Opt("duration", "number", "Optional range duration."), Opt("note", "string", "Marker note."), Opt("color", "string", "Marker color.")]),
        Skill("edit_marker", "marker", "Replace a marker's editable state.", [Req("marker_id", "uuid", "Marker ID."), Req("label", "string", "Desired label."), Req("time", "number", "Desired timeline seconds."), Opt("duration", "number", "Desired range duration."), Opt("note", "string", "Desired note."), Opt("color", "string", "Desired color.")], warnings: ["Provide the complete desired marker state because omitted values use protocol defaults."]),
        Skill("delete_marker", "marker", "Delete a marker.", [Req("marker_id", "uuid", "Marker ID.")]),
        Skill("clear_markers", "marker", "Delete every marker in the active sequence.", [], warnings: ["This affects the entire active sequence."]),
        Skill("add_track", "track", "Add a timeline track.", [Opt("kind", "string", "TrackKind enum; defaults to Video."), Opt("index", "integer", "Optional insertion index.")]),
        Skill("delete_track", "track", "Delete a track and its contents.", [Req("track_id", "uuid", "Track ID.")], ["The track must be unlocked."], ["This removes every item on the track."]),
        Skill("duplicate_track", "track", "Duplicate a track and all supported item state.", [Req("track_id", "uuid", "Track ID.")], ["The source track must be unlocked."]),
        Skill("rename_track", "track", "Rename a track.", [Req("track_id", "uuid", "Track ID."), Req("name", "string", "New track name.")], ["The track must be unlocked."]),
        Skill("reorder_track", "track", "Move a track to a new zero-based index.", [Req("track_id", "uuid", "Track ID."), Req("index", "integer", "New track index.")], ["The track must be unlocked."]),
        Skill("toggle_track_mute", "track", "Toggle a track's muted state.", [Req("track_id", "uuid", "Track ID.")]),
        Skill("toggle_track_solo", "track", "Toggle a track's solo state.", [Req("track_id", "uuid", "Track ID.")]),
        Skill("toggle_track_lock", "track", "Toggle a track's lock state.", [Req("track_id", "uuid", "Track ID.")], warnings: ["Lock changes alter which later edits are permitted."]),
        Skill("update_sequence", "sequence", "Update canvas dimensions, frame rate, background, and/or layout guides.", [Opt("width", "integer", "Canvas width."), Opt("height", "integer", "Canvas height."), Opt("frame_rate", "number|object", "FPS number or {numerator, denominator}."), Opt("background", "object", "CanvasBackground object."), Opt("layout_guides", "array", "Complete LayoutGuide collection.")], ["At least one sequence setting should be supplied."], ["layout_guides replaces the complete guide collection."]),
        Skill("add_captions_from_transcript", "intelligence", "Create chunked captions from imported transcript words.", [Opt("media_asset_id", "uuid", "Required when multiple analyzed assets exist."), Opt("track_id", "uuid", "Unlocked text/overlay track."), Opt("source_start", "number", "Source range start seconds."), Opt("source_end", "number", "Source range end seconds."), Opt("timeline_start", "number", "Timeline insertion seconds; defaults to playhead."), Opt("words_per_chunk", "integer", "1 to 12."), Opt("uppercase", "boolean", "Uppercase generated captions."), ..TextStyle(), ..TextTransform()], ["The selected media must be registered and have imported transcript analysis."]),
        Skill("create_clip_from_transcript", "intelligence", "Create a source clip from a transcript segment or explicit source range.", [Opt("media_asset_id", "uuid", "Required when multiple analyzed assets exist."), Opt("track_id", "uuid", "Compatible unlocked destination track."), Opt("segment_id", "string", "Transcript segment ID."), Opt("source_start", "number", "Source range start when segment_id is omitted."), Opt("source_end", "number", "Source range end when segment_id is omitted."), Opt("timeline_start", "number", "Timeline insertion seconds; defaults to playhead.")], ["The selected media must be registered, online, and have imported transcript analysis.", "Provide segment_id or a valid source_start/source_end range."]),
        Skill("assemble_best_moments", "intelligence", "Assemble diverse high-scoring analyzed moments into a rough cut.", [Opt("media_asset_id", "uuid", "Required when multiple analyzed assets exist."), Opt("track_id", "uuid", "Compatible unlocked destination track."), Opt("timeline_start", "number", "Timeline insertion seconds; defaults to playhead."), Opt("count", "integer", "Requested clip count, 1 to 50."), Opt("maximum_duration", "number", "Maximum assembled duration seconds."), Opt("role", "string", "Optional editing-role filter.")], ["The selected media must be registered, online, and have imported moment analysis."], ["Moment ranking is advisory and requires creative review."]),
        Skill("use_best_take", "intelligence", "Insert the recommended candidate from a duplicate-take group.", [Req("group_id", "string", "Duplicate-take group ID."), Opt("media_asset_id", "uuid", "Required when multiple analyzed assets exist."), Opt("track_id", "uuid", "Compatible unlocked destination track."), Opt("timeline_start", "number", "Timeline insertion seconds; defaults to playhead.")], ["The selected media must be registered, online, and have imported duplicate-take analysis."]),
        Skill("remove_silence", "intelligence", "Replace one clip with retained ranges after removing analyzed silence, optionally rippling later items.", [Req("item_id", "uuid", "Timeline clip."), Opt("minimum_silence", "number", "Minimum removable silence duration."), Opt("ripple", "boolean", "Shift later items; defaults to true.")], ["The clip must use analyzed media, play forward at 1x, and the item/track must be unlocked."], ["This atomically replaces the target track's item collection; review pacing and reaction tails."]),
    ];

    public static IReadOnlyList<string> SupportedActions { get; } =
        Skills.Select(skill => skill.Action).ToArray();

    public static AgentEditSkillDefinition? Find(string? action) =>
        string.IsNullOrWhiteSpace(action)
            ? null
            : Skills.FirstOrDefault(skill => skill.Action.Equals(action, StringComparison.OrdinalIgnoreCase));

    private static AgentEditSkillDefinition Skill(
        string action,
        string category,
        string summary,
        IReadOnlyList<AgentEditParameterDefinition> parameters,
        IReadOnlyList<string>? preconditions = null,
        IReadOnlyList<string>? warnings = null) =>
        new(action, category, summary, parameters, preconditions ?? [], warnings ?? []);

    private static AgentEditParameterDefinition Req(string name, string type, string description) =>
        new(name, type, true, description);

    private static AgentEditParameterDefinition Opt(string name, string type, string description) =>
        new(name, type, false, description);

    private static AgentEditParameterDefinition[] TextStyle() =>
    [
        Opt("font_family", "string", "Installed font family or registered project font path."),
        Opt("font_size", "number", "Font size."),
        Opt("font_bold", "boolean", "Bold text."),
        Opt("font_align", "string", "Text alignment."),
        Opt("fill_color", "string", "Fill color."),
        Opt("outline_color", "string", "Outline color."),
        Opt("outline_width", "number", "Outline width."),
        Opt("shadow_color", "string", "Shadow color."),
        Opt("shadow_opacity", "number", "Shadow opacity from 0 to 1."),
    ];

    private static AgentEditParameterDefinition[] TextTransform() =>
    [
        Opt("x", "number", "Position X."),
        Opt("y", "number", "Position Y."),
        Opt("scale_x", "number", "Scale X, greater than 0."),
        Opt("scale_y", "number", "Scale Y, greater than 0."),
        Opt("rotation", "number", "Rotation degrees."),
    ];

    private static AgentEditParameterDefinition[] Transform() =>
    [
        ..TextTransform(),
        Opt("anchor_x", "number", "Anchor X."),
        Opt("anchor_y", "number", "Anchor Y."),
    ];
}
