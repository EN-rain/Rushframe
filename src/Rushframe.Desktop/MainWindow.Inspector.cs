using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rushframe.Application;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Desktop.Services;
using Rushframe.Desktop.Timeline;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private void SelectTransition(TransitionSelection? selection)
    {
        if (_suppressTimelineSelectionSync) return;
        if (selection != null && !TryResolvePendingInspectorChanges())
        {
            RestoreInspectorTimelineSelection();
            return;
        }

        if (selection == null)
        {
            _selectedTransitionSelection = null;
            if (_selectedInspectorItem == null) UpdateInspector(null);
            return;
        }

        var transition = selection.Transition;
        _selectedInspectorItem = null;
        _selectedTransitionSelection = selection with { Transition = transition };
        UpdateTransitionInspector(_selectedTransitionSelection);
    }

    private bool TryResolvePendingInspectorChanges()
    {
        if (!_inspectorDirty) return true;

        var result = MessageBox.Show(
            this,
            "The Inspector has unapplied changes. Apply them before changing the selection?",
            "Pending Inspector changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => ApplyInspectorSettings(),
            MessageBoxResult.No => ResetPendingInspectorChanges(),
            _ => false,
        };
    }

    private bool ResetPendingInspectorChanges()
    {
        ResetInspectorEdits();
        return true;
    }

    private void RestoreInspectorTimelineSelection()
    {
        if (_timeline == null) return;
        try
        {
            _suppressTimelineSelectionSync = true;
            if (_selectedTransitionSelection is { } transitionSelection)
            {
                _timeline.SelectTransition(transitionSelection.Transition, transitionSelection.TrackIndex);
                return;
            }

            var item = _selectedInspectorItem;
            var trackIndex = item == null || _project.MainSequence == null
                ? -1
                : _project.MainSequence.Tracks.FindIndex(track => track.Items.Any(candidate => candidate.Id == item.Id));
            _timeline.SelectItem(item, trackIndex);
        }
        finally
        {
            _suppressTimelineSelectionSync = false;
        }
    }

    private void RefreshTransitionInspectorAfterEdit(Sequence sequence)
    {
        var selection = _selectedTransitionSelection;
        if (selection == null) return;
        var track = sequence.Tracks.ElementAtOrDefault(selection.TrackIndex);
        var left = track?.Items.FirstOrDefault(item => item.Id == selection.LeftItem.Id);
        var right = track?.Items.FirstOrDefault(item => item.Id == selection.RightItem.Id);
        if (left == null || right == null)
        {
            _selectedTransitionSelection = null;
            _timeline?.ClearSelection();
            UpdateInspector(null);
            return;
        }

        var transition = sequence.Transitions.FirstOrDefault(candidate =>
            candidate.LeftItemId == left.Id && candidate.RightItemId == right.Id);
        _selectedTransitionSelection = new TransitionSelection(transition, left, right, selection.TrackIndex);
        _timeline?.SelectTransition(transition, selection.TrackIndex);
        UpdateTransitionInspector(_selectedTransitionSelection);
    }

    private void UpdateTransitionInspector(TransitionSelection selection)
    {
        var transition = selection.Transition;
        var track = _project.MainSequence?.Tracks.ElementAtOrDefault(selection.TrackIndex);
        var isLocked = track?.Locked == true || selection.LeftItem.Locked || selection.RightItem.Locked;
        _suppressInspectorChangeTracking = true;
        try
        {
            InspectorPanel.IsEnabled = !isLocked;
            EffectsInspectorPanel.IsEnabled = false;
            AudioInspectorPanel.IsEnabled = false;
            InspectorPanel.Visibility = Visibility.Visible;
            InspectorEmptyState.Visibility = Visibility.Collapsed;
            ApplyInspectorProfile(profile: null, showTransition: true);
            InspectorTitle.Text = transition == null ? "Transition slot" : $"{transition.Kind} transition";
            StatusText.Text = isLocked ? "Selected transition (locked)" : "Selected transition";
            TransitionKindCombo.SelectedItem = transition?.Kind ?? TransitionKind.CrossDissolve;
            TransitionDurationBox.Text = Format(transition?.Duration.Seconds ?? 0.5);
            TransitionAlignmentBox.Text = Format((transition?.Alignment ?? 0.5) * 100);
            TransitionAudioModeCombo.SelectedItem = transition?.AudioMode ?? TransitionAudioMode.None;
        }
        finally
        {
            _suppressInspectorChangeTracking = false;
        }
        SetInspectorDirty(false);
    }

    private void ApplyInspectorProfile(InspectorProfile? profile, bool showTransition = false)
    {
        TransitionInspectorCard.Visibility = showTransition ? Visibility.Visible : Visibility.Collapsed;
        TransformInspectorExpander.Visibility = profile?.ShowTransform == true ? Visibility.Visible : Visibility.Collapsed;
        TextInspectorExpander.Visibility = profile?.ShowText == true ? Visibility.Visible : Visibility.Collapsed;
        TimingInspectorExpander.Visibility = profile?.ShowTiming == true ? Visibility.Visible : Visibility.Collapsed;
        FadesInspectorExpander.Visibility = profile?.ShowFades == true ? Visibility.Visible : Visibility.Collapsed;
        ColorInspectorExpander.Visibility = profile?.ShowColor == true ? Visibility.Visible : Visibility.Collapsed;
        StabilizationInspectorExpander.Visibility = profile?.ShowStabilization == true ? Visibility.Visible : Visibility.Collapsed;
        // Tabs remain enabled so their close controls and the Inspector tab menu are always usable.
        // Unsupported editors keep their own content disabled instead of disabling the tab header.
        PropertiesInspectorTab.IsEnabled = true;
        EffectsInspectorTab.IsEnabled = true;
        AudioInspectorTab.IsEnabled = true;
        EnsureInspectorCoreTabVisible(showTransition ? 0 : profile?.PreferredTabIndex ?? 0);
    }

    private InspectorProfile ResolveInspectorProfile(TimelineItem item, Track? owningTrack = null)
    {
        owningTrack ??= _project.MainSequence?.Tracks.FirstOrDefault(
            track => track.Items.Any(candidate => candidate.Id == item.Id));
        var mediaKind = item.MediaAssetId is { } mediaAssetId
            ? _project.MediaLibrary.FirstOrDefault(asset => asset.Id == mediaAssetId)?.Kind
            : null;
        return InspectorProfile.Resolve(item.Kind, mediaKind, owningTrack?.Kind);
    }

    private void UpdateInspector(TimelineItem? item)
    {
        _suppressInspectorChangeTracking = true;
        try
        {
            var owningTrack = item == null
                ? null
                : _project.MainSequence?.Tracks.FirstOrDefault(track => track.Items.Any(candidate => candidate.Id == item.Id));
            var isLocked = item?.Locked == true || owningTrack?.Locked == true;
            var profile = item == null ? null : ResolveInspectorProfile(item, owningTrack);
            InspectorPanel.IsEnabled = item != null && !isLocked;
            InspectorPanel.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            InspectorEmptyState.Visibility = item == null ? Visibility.Visible : Visibility.Collapsed;
            var itemLabel = profile?.DisplayName;
            InspectorTitle.Text = item == null ? "No clip selected" : itemLabel;
            StatusText.Text = item == null
                ? "Ready"
                : isLocked
                    ? $"Selected {itemLabel!.ToLowerInvariant()} (locked)"
                    : $"Selected {itemLabel!.ToLowerInvariant()}";
            ApplyInspectorProfile(profile);
            EffectsInspectorPanel.IsEnabled = item != null && !isLocked && profile?.ShowEffects == true;
            AudioInspectorPanel.IsEnabled = item != null && !isLocked && profile?.ShowAudio == true;

            PositionXBox.Text = Format(item?.Transform.PositionX ?? 0);
            PositionYBox.Text = Format(item?.Transform.PositionY ?? 0);
            ScaleXBox.Text = Format(item?.Transform.ScaleX ?? 1);
            ScaleYBox.Text = Format(item?.Transform.ScaleY ?? 1);
            RotationBox.Text = Format(item?.Transform.RotationDegrees ?? 0);
            OpacityBox.Text = Format((item?.Opacity ?? 1) * 100);
            SpeedBox.Text = Format(item?.SpeedCurve?.ConstantSpeed ?? item?.Speed ?? 1);
            ReverseToggle.IsChecked = item?.Reversed ?? false;
            VolumeBox.Text = Format((item?.Volume ?? 1) * 100);
            PanBox.Text = Format((item?.Pan ?? 0) * 100);
            FadeInBox.Text = Format(item?.FadeInDuration.Seconds ?? 0);
            FadeOutBox.Text = Format(item?.FadeOutDuration.Seconds ?? 0);
            VisualTransitionInCombo.SelectedItem = item?.VisualTransitionIn ?? ItemTransitionKind.None;
            VisualTransitionInDurationBox.Text = Format(item?.VisualTransitionInDuration.Seconds ?? 0.35);
            VisualTransitionOutCombo.SelectedItem = item?.VisualTransitionOut ?? ItemTransitionKind.None;
            VisualTransitionOutDurationBox.Text = Format(item?.VisualTransitionOutDuration.Seconds ?? 0.35);

            var color = item?.ColorCorrection;
            var brightness = color?.Brightness ?? 0;
            var contrast = color?.Contrast ?? 0;
            var saturation = color?.Saturation ?? 1;
            BrightnessSlider.Value = brightness;
            ContrastSlider.Value = contrast;
            SaturationSlider.Value = saturation;
            BrightnessBox.Text = Format(brightness);
            ContrastBox.Text = Format(contrast);
            SaturationBox.Text = Format(saturation);
            BlackWhiteToggle.IsChecked = color?.BlackAndWhite ?? false;
            StabilizeToggle.IsChecked = item?.Stabilization?.Enabled ?? false;

            var isText = profile?.ShowText == true;
            TextContentBox.Text = isText ? item?.TextContent ?? string.Empty : string.Empty;
            SelectInspectorFont(isText ? item?.FontFamily : null);
            FontSizeBox.Text = Format(isText ? item?.FontSize ?? 48 : 48);
            FontBoldToggle.IsChecked = isText && (item?.FontBold ?? false);
            SelectFontAlignment(isText ? item?.FontAlign : "center");
            TextFillColorBox.Text = isText ? item?.FillColor ?? "#FFFFFF" : "#FFFFFF";
            TextOutlineColorBox.Text = isText ? item?.OutlineColor ?? "#000000" : "#000000";
            TextOutlineWidthBox.Text = Format(isText ? item?.OutlineWidth ?? 0 : 0);
            TextShadowColorBox.Text = isText ? item?.ShadowColor ?? "#000000" : "#000000";
            TextShadowOpacityBox.Text = Format((isText ? item?.ShadowOpacity ?? 0.5 : 0.5) * 100);

            AddEffectButton.IsEnabled = item != null && !isLocked && profile?.ShowEffects == true && EffectCombo.SelectedItem != null;
            OpenAnimationEditorButton.IsEnabled = item != null && !isLocked;
            OpenAnimationEditorButton.ToolTip = item?.AnimationChannels.Count > 0
                ? $"Open animation graph ({item.AnimationChannels.Count} channels)"
                : "Open animation graph";
            AnalyzeStabilizationButton.IsEnabled = item?.MediaAssetId != null && !isLocked && profile?.ShowStabilization == true && !_isMediaOperationRunning;
            var selectedEffectId = (EffectList.SelectedItem as EffectListEntry)?.Effect.Id;
            EffectList.Items.Clear();
            if (item != null && profile?.ShowEffects == true)
            {
                foreach (var effect in item.Effects)
                {
                    var definition = _effectRegistry.Get(effect.EffectTypeId);
                    EffectList.Items.Add(new EffectListEntry(
                        effect,
                        definition?.Name ?? effect.EffectTypeId,
                        definition?.Category ?? "custom"));
                }
            }
            if (selectedEffectId.HasValue)
            {
                EffectList.SelectedItem = EffectList.Items
                    .OfType<EffectListEntry>()
                    .FirstOrDefault(entry => entry.Effect.Id == selectedEffectId.Value);
            }
            UpdateSelectedEffectEditor();
        }
        finally
        {
            _suppressInspectorChangeTracking = false;
        }
        SetInspectorDirty(false);
        UpdateMediaIntelligenceActionState();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OpenAnimationEditor()
    {
        var item = _selectedInspectorItem;
        if (item == null) return;
        var localTime = Math.Clamp(
            (_timeline?.PlayheadTime.Seconds ?? item.TimelineStart.Seconds) - item.TimelineStart.Seconds,
            0,
            Math.Max(0, item.Duration.Seconds));
        var channels = new Dialogs.AnimationEditorDialog(this, item, localTime).Show();
        if (channels == null) return;

        Execute(new UpdateAnimationChannelsCommand
        {
            ItemId = item.Id,
            NewChannels = channels,
        });
        UpdateInspector(item);
        UpdatePreviewInteractionOverlay(item);
        if (_timeline != null)
            _ = EnsureTimelineCompositePreviewAsync(_timeline.PlayheadTime.Seconds);
    }

    private void SelectFontAlignment(string? alignment)
    {
        var normalized = string.IsNullOrWhiteSpace(alignment) ? "center" : alignment.Trim().ToLowerInvariant();
        FontAlignCombo.SelectedItem = FontAlignCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            ?? FontAlignCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private string GetSelectedFontAlignment() =>
        (FontAlignCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "center";

    private void SelectInspectorFont(string? value)
    {
        var resolved = string.IsNullOrWhiteSpace(value) ? "Arial" : value.Trim();
        var choice = _inspectorFontChoices.FirstOrDefault(candidate =>
            string.Equals(candidate.Value, resolved, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.DisplayName, resolved, StringComparison.OrdinalIgnoreCase));
        FontFamilyCombo.SelectedItem = choice;
        FontFamilyCombo.Text = choice?.DisplayName ?? resolved;
    }

    private bool TryResolveInspectorFont(out string fontFamily)
    {
        var text = FontFamilyCombo.Text.Trim();
        if (FontFamilyCombo.SelectedItem is InspectorFontChoice selected
            && (string.Equals(text, selected.DisplayName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, selected.Value, StringComparison.OrdinalIgnoreCase)))
        {
            fontFamily = selected.Value;
            ClearInspectorValidation(FontFamilyCombo);
            return true;
        }

        var choice = _inspectorFontChoices.FirstOrDefault(candidate =>
            string.Equals(candidate.DisplayName, text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Value, text, StringComparison.OrdinalIgnoreCase));
        if (choice != null)
        {
            fontFamily = choice.Value;
            ClearInspectorValidation(FontFamilyCombo);
            return true;
        }

        if (Path.IsPathFullyQualified(text) || File.Exists(text))
        {
            var registered = _project.MediaLibrary.FirstOrDefault(asset =>
                asset.Kind == MediaKind.Font
                && !asset.IsOffline
                && string.Equals(Path.GetFullPath(asset.OriginalPath), Path.GetFullPath(text), StringComparison.OrdinalIgnoreCase));
            if (registered != null)
            {
                fontFamily = registered.OriginalPath;
                ClearInspectorValidation(FontFamilyCombo);
                return true;
            }

            fontFamily = string.Empty;
            SetInspectorValidation(FontFamilyCombo, "Choose a registered project font instead of an arbitrary path.", "font family");
            return false;
        }

        var installed = _systemFontNames.Contains(text, StringComparer.OrdinalIgnoreCase)
            || Fonts.SystemFontFamilies.Any(font => string.Equals(font.Source, text, StringComparison.OrdinalIgnoreCase));
        if (installed)
        {
            fontFamily = text;
            ClearInspectorValidation(FontFamilyCombo);
            return true;
        }

        fontFamily = string.Empty;
        SetInspectorValidation(FontFamilyCombo, "Choose an installed font or an imported project font.", "font family");
        return false;
    }

    private bool TryReadInspectorColor(TextBox textBox, string label, out string value)
    {
        if (InspectorValueLogic.TryNormalizeColor(textBox.Text, out value))
        {
            ClearInspectorValidation(textBox);
            return true;
        }

        SetInspectorValidation(textBox, $"Enter a valid color for {label}.", label);
        return false;
    }

    private static string NormalizeExistingColor(string? value, string fallback) =>
        InspectorValueLogic.TryNormalizeColor(value, out var normalized) ? normalized : fallback;

    private static string GetInspectorItemLabel(ItemKind kind) => kind switch
    {
        ItemKind.Clip => "Media clip",
        ItemKind.Text => "Text clip",
        ItemKind.Image => "Image clip",
        ItemKind.Sticker => "Sticker",
        ItemKind.AdjustmentLayer => "Adjustment layer",
        _ => $"{kind} item",
    };

    private bool ApplyInspectorSettings()
    {
        if (_selectedTransitionSelection != null)
            return ApplyTransitionInspectorSettings(_selectedTransitionSelection);

        var item = _selectedInspectorItem;
        if (item == null) return false;
        var profile = ResolveInspectorProfile(item);
        var commands = new List<IEditCommand>();

        if (profile.ShowTransform)
        {
            if (!TryReadNumber(PositionXBox, "position X", out var positionX)
                || !TryReadNumber(PositionYBox, "position Y", out var positionY)
                || !TryReadNumber(ScaleXBox, "scale X", out var scaleX)
                || !TryReadNumber(ScaleYBox, "scale Y", out var scaleY)
                || !TryReadNumber(RotationBox, "rotation", out var rotation)
                || !TryReadNumber(OpacityBox, "opacity", out var opacityPercent))
                return false;

            var transform = InspectorValueLogic.CloneTransform(item.Transform, positionX, positionY, scaleX, scaleY, rotation);
            if (!InspectorValueLogic.TransformEquals(item.Transform, transform))
            {
                commands.Add(new UpdateTransformCommand
                {
                    ItemId = item.Id,
                    NewTransform = transform,
                });
            }

            AddChangedValue(
                commands,
                item,
                nameof(TimelineItem.Opacity),
                Math.Clamp(opacityPercent / 100, 0, 1),
                current => current.Opacity,
                (current, value) => current.Opacity = (double)value!);
        }

        if (profile.ShowTiming)
        {
            if (!TryReadNumber(SpeedBox, "speed", out var speedValue)) return false;
            var speed = Math.Clamp(speedValue, 0.1, 100);
            var reversed = ReverseToggle.IsChecked ?? false;
            AddChangedValue(
                commands,
                item,
                nameof(TimelineItem.Reversed),
                reversed,
                current => current.Reversed,
                (current, value) => current.Reversed = (bool)value!);

            if (item.SpeedCurve is { } existingCurve)
            {
                var curve = InspectorValueLogic.CloneSpeedCurve(existingCurve, speed);
                if (!InspectorValueLogic.SpeedCurveEquals(existingCurve, curve))
                {
                    commands.Add(SetValue(
                        item,
                        nameof(TimelineItem.SpeedCurve),
                        curve,
                        current => current.SpeedCurve,
                        (current, value) => current.SpeedCurve = (SpeedCurve?)value));
                }
            }
            else
            {
                AddChangedValue(
                    commands,
                    item,
                    nameof(TimelineItem.Speed),
                    speed,
                    current => current.Speed,
                    (current, value) => current.Speed = (double)value!);
            }
        }

        if (profile.ShowFades)
        {
            if (!TryReadNumber(FadeInBox, "audio fade in", out var fadeInSeconds)
                || !TryReadNumber(FadeOutBox, "audio fade out", out var fadeOutSeconds)
                || !TryReadNumber(VisualTransitionInDurationBox, "visual entrance duration", out var visualInSeconds)
                || !TryReadNumber(VisualTransitionOutDurationBox, "visual exit duration", out var visualOutSeconds))
                return false;

            var maximumFadeSeconds = Math.Max(0, item.Duration.Seconds);
            var clampedFadeInSeconds = Math.Clamp(fadeInSeconds, 0, maximumFadeSeconds);
            var clampedFadeOutSeconds = Math.Clamp(fadeOutSeconds, 0, Math.Max(0, maximumFadeSeconds - clampedFadeInSeconds));
            var visualIn = VisualTransitionInCombo.SelectedItem is ItemTransitionKind selectedVisualIn ? selectedVisualIn : ItemTransitionKind.None;
            var visualOut = VisualTransitionOutCombo.SelectedItem is ItemTransitionKind selectedVisualOut ? selectedVisualOut : ItemTransitionKind.None;
            var clampedVisualInSeconds = visualIn == ItemTransitionKind.None ? 0 : Math.Clamp(visualInSeconds, 0.05, maximumFadeSeconds);
            var clampedVisualOutSeconds = visualOut == ItemTransitionKind.None ? 0 : Math.Clamp(visualOutSeconds, 0.05, maximumFadeSeconds);
            AddChangedValue(
                commands,
                item,
                nameof(TimelineItem.FadeInDuration),
                MediaTime.FromSeconds(clampedFadeInSeconds),
                current => current.FadeInDuration,
                (current, value) => current.FadeInDuration = (MediaTime)value!);
            AddChangedValue(
                commands,
                item,
                nameof(TimelineItem.FadeOutDuration),
                MediaTime.FromSeconds(clampedFadeOutSeconds),
                current => current.FadeOutDuration,
                (current, value) => current.FadeOutDuration = (MediaTime)value!);
            AddChangedValue(commands, item, nameof(TimelineItem.VisualTransitionIn), visualIn, current => current.VisualTransitionIn, (current, value) => current.VisualTransitionIn = (ItemTransitionKind)value!);
            AddChangedValue(commands, item, nameof(TimelineItem.VisualTransitionInDuration), MediaTime.FromSeconds(clampedVisualInSeconds), current => current.VisualTransitionInDuration, (current, value) => current.VisualTransitionInDuration = (MediaTime)value!);
            AddChangedValue(commands, item, nameof(TimelineItem.VisualTransitionOut), visualOut, current => current.VisualTransitionOut, (current, value) => current.VisualTransitionOut = (ItemTransitionKind)value!);
            AddChangedValue(commands, item, nameof(TimelineItem.VisualTransitionOutDuration), MediaTime.FromSeconds(clampedVisualOutSeconds), current => current.VisualTransitionOutDuration, (current, value) => current.VisualTransitionOutDuration = (MediaTime)value!);
        }

        if (profile.ShowAudio)
        {
            if (!TryReadNumber(VolumeBox, "volume", out var volumePercent)
                || !TryReadNumber(PanBox, "pan", out var panPercent))
                return false;

            AddChangedValue(
                commands,
                item,
                nameof(TimelineItem.Volume),
                Math.Clamp(volumePercent / 100, 0, 4),
                current => current.Volume,
                (current, value) => current.Volume = (double)value!);
            AddChangedValue(
                commands,
                item,
                nameof(TimelineItem.Pan),
                Math.Clamp(panPercent / 100, -1, 1),
                current => current.Pan,
                (current, value) => current.Pan = (double)value!);
        }

        if (profile.ShowColor)
        {
            if (!TryReadNumber(BrightnessBox, "brightness", out var brightness)
                || !TryReadNumber(ContrastBox, "contrast", out var contrast)
                || !TryReadNumber(SaturationBox, "saturation", out var saturation))
                return false;

            var color = InspectorValueLogic.BuildColorCorrection(
                item.ColorCorrection,
                brightness,
                contrast,
                saturation,
                BlackWhiteToggle.IsChecked ?? false);
            if (!InspectorValueLogic.ColorCorrectionEquals(item.ColorCorrection, color))
            {
                commands.Add(SetValue(
                    item,
                    nameof(TimelineItem.ColorCorrection),
                    color,
                    current => current.ColorCorrection,
                    (current, value) => current.ColorCorrection = (ColorCorrection?)value));
            }
        }

        if (profile.ShowStabilization)
        {
            var enabled = StabilizeToggle.IsChecked ?? false;
            var stabilization = item.Stabilization == null && !enabled
                ? null
                : InspectorValueLogic.BuildStabilization(item.Stabilization, enabled);
            if (!InspectorValueLogic.StabilizationEquals(item.Stabilization, stabilization))
            {
                commands.Add(SetValue(
                    item,
                    nameof(TimelineItem.Stabilization),
                    stabilization,
                    current => current.Stabilization,
                    (current, value) => current.Stabilization = (StabilizationSettings?)value));
            }
        }

        if (profile.ShowText)
        {
            if (!TryReadNumber(FontSizeBox, "font size", out var fontSize)
                || !TryReadNumber(TextOutlineWidthBox, "outline width", out var outlineWidth)
                || !TryReadNumber(TextShadowOpacityBox, "shadow opacity", out var shadowOpacityPercent)
                || !TryReadInspectorColor(TextFillColorBox, "fill color", out var fill)
                || !TryReadInspectorColor(TextOutlineColorBox, "outline color", out var outline)
                || !TryReadInspectorColor(TextShadowColorBox, "shadow color", out var shadow)
                || !TryResolveInspectorFont(out var fontFamily))
                return false;

            var textContent = TextContentBox.Text;
            var alignment = GetSelectedFontAlignment();
            if (!string.Equals(item.TextContent ?? string.Empty, textContent, StringComparison.Ordinal))
                commands.Add(SetValue(item, nameof(TimelineItem.TextContent), textContent, current => current.TextContent, (current, value) => current.TextContent = (string?)value));
            if (!string.Equals(item.FontFamily ?? "Arial", fontFamily, StringComparison.OrdinalIgnoreCase))
                commands.Add(SetValue(item, nameof(TimelineItem.FontFamily), fontFamily, current => current.FontFamily, (current, value) => current.FontFamily = (string?)value));
            AddChangedValue(commands, item, nameof(TimelineItem.FontSize), Math.Clamp(fontSize, 1, 1000), current => current.FontSize, (current, value) => current.FontSize = (double)value!);
            AddChangedValue(commands, item, nameof(TimelineItem.FontBold), FontBoldToggle.IsChecked == true, current => current.FontBold, (current, value) => current.FontBold = (bool)value!);
            AddChangedValue(commands, item, nameof(TimelineItem.FontAlign), alignment, current => current.FontAlign, (current, value) => current.FontAlign = (string)value!);
            AddChangedString(commands, item, nameof(TimelineItem.FillColor), fill, NormalizeExistingColor(item.FillColor, "#FFFFFF"), current => current.FillColor, (current, value) => current.FillColor = value);
            AddChangedString(commands, item, nameof(TimelineItem.OutlineColor), outline, NormalizeExistingColor(item.OutlineColor, "#000000"), current => current.OutlineColor, (current, value) => current.OutlineColor = value);
            AddChangedValue(commands, item, nameof(TimelineItem.OutlineWidth), Math.Clamp(outlineWidth, 0, 100), current => current.OutlineWidth, (current, value) => current.OutlineWidth = (double)value!);
            AddChangedString(commands, item, nameof(TimelineItem.ShadowColor), shadow, NormalizeExistingColor(item.ShadowColor, "#000000"), current => current.ShadowColor, (current, value) => current.ShadowColor = value);
            AddChangedValue(commands, item, nameof(TimelineItem.ShadowOpacity), Math.Clamp(shadowOpacityPercent / 100, 0, 1), current => current.ShadowOpacity, (current, value) => current.ShadowOpacity = (double)value!);
        }

        if (commands.Count == 0)
        {
            SetInspectorDirty(false);
            StatusText.Text = "Inspector values are unchanged";
            return true;
        }

        if (!Execute(new CompositeEditCommand("Apply clip settings", commands), resolvePendingInspectorChanges: false)) return false;
        UpdateInspector(item);
        return true;
    }

    private bool ApplyTransitionInspectorSettings(TransitionSelection selection)
    {
        var kind = TransitionKindCombo.SelectedItem is TransitionKind selectedKind
            ? selectedKind
            : TransitionKind.CrossDissolve;
        if (!TryReadNumber(TransitionDurationBox, "transition duration", out var durationValue)
            || !TryReadNumber(TransitionAlignmentBox, "transition alignment", out var alignmentPercent))
            return false;
        var duration = Math.Clamp(durationValue, 0.05, 10);
        var alignment = Math.Clamp(alignmentPercent / 100, 0, 1);
        var audioMode = TransitionAudioModeCombo.SelectedItem is TransitionAudioMode selectedAudioMode
            ? selectedAudioMode
            : TransitionAudioMode.None;
        var existing = selection.Transition;
        if (existing != null
            && existing.Kind == kind
            && existing.AudioMode == audioMode
            && InspectorValueLogic.NearlyEqual(existing.Duration.Seconds, duration)
            && InspectorValueLogic.NearlyEqual(existing.Alignment, alignment))
        {
            SetInspectorDirty(false);
            StatusText.Text = "Transition values are unchanged";
            return true;
        }

        if (!Execute(new ApplyTransitionCommand
            {
                LeftItemId = selection.LeftItem.Id,
                RightItemId = selection.RightItem.Id,
                Kind = kind,
                Duration = MediaTime.FromSeconds(duration),
                Alignment = alignment,
                AudioMode = audioMode,
            }, resolvePendingInspectorChanges: false))
            return false;

        var updated = _project.MainSequence?.Transitions.FirstOrDefault(candidate =>
            candidate.LeftItemId == selection.LeftItem.Id && candidate.RightItemId == selection.RightItem.Id);
        _selectedTransitionSelection = selection with { Transition = updated };
        _timeline?.SelectTransition(updated, selection.TrackIndex);
        if (_selectedTransitionSelection != null) UpdateTransitionInspector(_selectedTransitionSelection);
        return true;
    }

    private void AddSelectedEffect()
    {
        if (!CanEditSelectedEffects()
            || _selectedInspectorItem == null
            || EffectCombo.SelectedItem is not EffectDefinition effect)
            return;
        Execute(new AddEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectTypeId = effect.EffectTypeId,
            Parameters = effect.Parameters.ToDictionary(p => p.Name, p => p.DefaultValue),
        });
    }

    private bool CanEditInspectorTarget()
    {
        if (_selectedTransitionSelection is { } transitionSelection)
        {
            var track = _project.MainSequence?.Tracks.ElementAtOrDefault(transitionSelection.TrackIndex);
            return track?.Locked != true
                && !transitionSelection.LeftItem.Locked
                && !transitionSelection.RightItem.Locked;
        }

        var item = _selectedInspectorItem;
        if (item == null) return false;
        var owningTrack = _project.MainSequence?.Tracks.FirstOrDefault(candidate => candidate.Items.Any(current => current.Id == item.Id));
        return !item.Locked && owningTrack?.Locked != true;
    }

    private bool CanEditSelectedEffects()
    {
        var item = _selectedInspectorItem;
        return item != null && ResolveInspectorProfile(item).ShowEffects && CanEditInspectorTarget();
    }

    private void UpdateSelectedEffectEditor()
    {
        var entry = EffectList.SelectedItem as EffectListEntry;
        var canEdit = entry != null && CanEditSelectedEffects();
        EffectRemoveButton.IsEnabled = canEdit;
        EffectMoveUpButton.IsEnabled = canEdit && EffectList.SelectedIndex > 0;
        EffectMoveDownButton.IsEnabled = canEdit && EffectList.SelectedIndex >= 0 && EffectList.SelectedIndex < EffectList.Items.Count - 1;
        EffectToggleButton.IsEnabled = canEdit;
        EffectDuplicateButton.IsEnabled = canEdit;
        EffectResetButton.IsEnabled = canEdit;
        EffectApplyParametersButton.IsEnabled = canEdit;
        EffectToggleButton.ToolTip = entry?.Effect.Enabled == true
            ? "Disable selected effect"
            : "Enable selected effect";

        _effectParameterEditors.Clear();
        EffectParameterPanel.Children.Clear();
        if (entry == null)
        {
            EffectParameterPanel.Children.Add(new TextBlock
            {
                Text = "Select an applied effect to edit its parameters.",
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var definition = _effectRegistry.Get(entry.Effect.EffectTypeId);
        if (definition == null || definition.Parameters.Count == 0)
        {
            EffectParameterPanel.Children.Add(new TextBlock
            {
                Text = "This effect has no editable parameters.",
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10.5,
            });
            return;
        }

        foreach (var parameter in definition.Parameters)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = FormatEffectParameterName(parameter.Name),
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var value = entry.Effect.Parameters.TryGetValue(parameter.Name, out var current)
                ? ConvertEffectValue(current, parameter.DefaultValue)
                : Convert.ToDouble(parameter.DefaultValue, CultureInfo.InvariantCulture);
            var editor = new TextBox
            {
                Text = Format(value),
                ToolTip = $"Range: {parameter.Min:0.###} to {parameter.Max:0.###}",
                MinHeight = 28,
            };
            System.Windows.Automation.AutomationProperties.SetName(editor, label.Text);
            Grid.SetColumn(editor, 1);
            grid.Children.Add(label);
            grid.Children.Add(editor);
            EffectParameterPanel.Children.Add(grid);
            _effectParameterEditors[parameter.Name] = editor;
        }
    }

    private void RemoveSelectedEffect()
    {
        if (!CanEditSelectedEffects() || _selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        Execute(new RemoveEffectCommand { ItemId = _selectedInspectorItem.Id, EffectInstanceId = entry.Effect.Id });
    }

    private void MoveSelectedEffect(int offset)
    {
        if (!CanEditSelectedEffects() || _selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        var newIndex = Math.Clamp(EffectList.SelectedIndex + offset, 0, EffectList.Items.Count - 1);
        Execute(new ReorderEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectInstanceId = entry.Effect.Id,
            NewIndex = newIndex,
        });
        EffectList.SelectedIndex = newIndex;
    }

    private void ToggleSelectedEffect()
    {
        if (!CanEditSelectedEffects() || _selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        Execute(new UpdateEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectInstanceId = entry.Effect.Id,
            Enabled = !entry.Effect.Enabled,
            Parameters = new Dictionary<string, object>(entry.Effect.Parameters),
        });
    }

    private void DuplicateSelectedEffect()
    {
        if (!CanEditSelectedEffects() || _selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        Execute(new AddEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectTypeId = entry.Effect.EffectTypeId,
            Enabled = entry.Effect.Enabled,
            Parameters = new Dictionary<string, object>(entry.Effect.Parameters),
        });
        EffectList.SelectedIndex = EffectList.Items.Count - 1;
    }

    private void ResetSelectedEffect()
    {
        if (!CanEditSelectedEffects() || _selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        var definition = _effectRegistry.Get(entry.Effect.EffectTypeId);
        if (definition == null) return;
        Execute(new UpdateEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectInstanceId = entry.Effect.Id,
            Enabled = true,
            Parameters = definition.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue),
        });
    }

    private void ApplySelectedEffectParameters()
    {
        if (!CanEditSelectedEffects() || _selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        var definition = _effectRegistry.Get(entry.Effect.EffectTypeId);
        if (definition == null) return;

        var parameters = new Dictionary<string, object>();
        foreach (var parameter in definition.Parameters)
        {
            if (!_effectParameterEditors.TryGetValue(parameter.Name, out var editor)
                || !InspectorValueLogic.TryParseFiniteNumber(editor.Text, out var parsed))
            {
                StatusText.Text = $"Invalid value for {FormatEffectParameterName(parameter.Name)}";
                editor?.Focus();
                return;
            }

            var clamped = Math.Clamp(parsed, parameter.Min, parameter.Max);
            parameters[parameter.Name] = parameter.Type.Equals("int", StringComparison.OrdinalIgnoreCase)
                ? (object)(int)Math.Round(clamped)
                : clamped;
        }

        Execute(new UpdateEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectInstanceId = entry.Effect.Id,
            Enabled = entry.Effect.Enabled,
            Parameters = parameters,
        });
    }

    private static double ConvertEffectValue(object value, object fallback)
    {
        try
        {
            var converted = value is JsonElement json && json.ValueKind == JsonValueKind.Number && json.TryGetDouble(out var number)
                ? number
                : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(converted) ? converted : Convert.ToDouble(fallback, CultureInfo.InvariantCulture);
        }
        catch
        {
            return Convert.ToDouble(fallback, CultureInfo.InvariantCulture);
        }
    }

    private static string FormatEffectParameterName(string name) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' '));

    private void AddChangedValue<T>(
        ICollection<IEditCommand> commands,
        TimelineItem item,
        string propertyName,
        T value,
        Func<TimelineItem, T> getter,
        Action<TimelineItem, object?> setter)
    {
        var current = getter(item);
        if (current is double currentDouble && value is double valueDouble
            ? InspectorValueLogic.NearlyEqual(currentDouble, valueDouble)
            : EqualityComparer<T>.Default.Equals(current, value))
            return;
        commands.Add(SetValue(item, propertyName, value, getter, setter));
    }

    private void AddChangedString(
        ICollection<IEditCommand> commands,
        TimelineItem item,
        string propertyName,
        string value,
        string comparisonValue,
        Func<TimelineItem, string?> getter,
        Action<TimelineItem, string?> setter)
    {
        if (string.Equals(comparisonValue, value, StringComparison.OrdinalIgnoreCase)) return;
        commands.Add(SetValue(
            item,
            propertyName,
            value,
            getter,
            (current, next) => setter(current, (string?)next)));
    }

    private async Task AnalyzeSelectedStabilizationAsync()
    {
        var item = _selectedInspectorItem;
        if (item?.MediaAssetId == null) return;
        var asset = _project.MediaLibrary.FirstOrDefault(a => a.Id == item.MediaAssetId.Value);
        if (asset == null) return;

        var current = item.Stabilization;
        var settings = new StabilizationSettings
        {
            Enabled = true,
            Strength = current?.Strength ?? 0.5,
            CropZoomCompensation = current?.CropZoomCompensation ?? true,
            AnalysisComplete = current?.AnalysisComplete ?? false,
        };
        AddRenderQueueMessage($"Stabilization: analyzing {Path.GetFileName(asset.OriginalPath)}");
        SetMediaOperationState(true, $"Analyzing motion in {Path.GetFileName(asset.OriginalPath)}…");
        try
        {
            await _stabilizationService.AnalyzeAsync(asset, settings);
            Execute(SetValue(item, nameof(TimelineItem.Stabilization), settings, i => i.Stabilization, (i, v) => i.Stabilization = (StabilizationSettings?)v));
            AddRenderQueueMessage("Stabilization: analysis complete");
        }
        catch (Exception ex)
        {
            AddRenderQueueMessage($"Stabilization failed: {ex.Message}");
        }
        finally
        {
            SetMediaOperationState(false, "Stabilization analysis finished");
        }
    }

    private static SetPropertyCommand SetValue<T>(TimelineItem item, string propertyName, T value, Func<TimelineItem, T> getter, Action<TimelineItem, object?> setter) =>
        new()
        {
            ItemId = item.Id,
            PropertyName = propertyName,
            NewValue = value,
            Getter = i => getter(i),
            Setter = setter,
        };

    private static int ComputeInspectorFingerprint(TimelineItem? item)
    {
        if (item == null) return 0;
        var hash = new HashCode();
        hash.Add(item.Id);
        hash.Add(item.Transform.PositionX);
        hash.Add(item.Transform.PositionY);
        hash.Add(item.Transform.ScaleX);
        hash.Add(item.Transform.ScaleY);
        hash.Add(item.Transform.RotationDegrees);
        hash.Add(item.Transform.AnchorX);
        hash.Add(item.Transform.AnchorY);
        hash.Add(item.Opacity);
        hash.Add(item.Speed);
        hash.Add(item.SpeedCurve?.ConstantSpeed);
        hash.Add(item.SpeedCurve?.PreservePitch);
        foreach (var segment in item.SpeedCurve?.Segments ?? [])
        {
            hash.Add(segment.SourceStart);
            hash.Add(segment.SourceEnd);
            hash.Add(segment.Speed);
        }
        hash.Add(item.Reversed);
        hash.Add(item.Volume);
        hash.Add(item.Pan);
        hash.Add(item.FadeInDuration);
        hash.Add(item.FadeOutDuration);
        hash.Add(item.TextContent, StringComparer.Ordinal);
        hash.Add(item.FontFamily, StringComparer.OrdinalIgnoreCase);
        hash.Add(item.FontSize);
        hash.Add(item.FontBold);
        hash.Add(item.FontAlign, StringComparer.OrdinalIgnoreCase);
        hash.Add(item.FillColor, StringComparer.OrdinalIgnoreCase);
        hash.Add(item.OutlineColor, StringComparer.OrdinalIgnoreCase);
        hash.Add(item.OutlineWidth);
        hash.Add(item.ShadowColor, StringComparer.OrdinalIgnoreCase);
        hash.Add(item.ShadowOffsetX);
        hash.Add(item.ShadowOffsetY);
        hash.Add(item.ShadowBlur);
        hash.Add(item.ShadowOpacity);
        hash.Add(item.ColorCorrection?.Brightness);
        hash.Add(item.ColorCorrection?.Contrast);
        hash.Add(item.ColorCorrection?.Saturation);
        hash.Add(item.ColorCorrection?.Exposure);
        hash.Add(item.ColorCorrection?.Highlights);
        hash.Add(item.ColorCorrection?.Shadows);
        hash.Add(item.ColorCorrection?.Whites);
        hash.Add(item.ColorCorrection?.Blacks);
        hash.Add(item.ColorCorrection?.Tint);
        hash.Add(item.ColorCorrection?.BlackAndWhite);
        hash.Add(item.Stabilization?.Enabled);
        hash.Add(item.Stabilization?.Strength);
        hash.Add(item.Stabilization?.CropZoomCompensation);
        hash.Add(item.Stabilization?.AnalysisComplete);
        hash.Add(item.AnimationChannels.Count);
        foreach (var channel in item.AnimationChannels)
        {
            hash.Add(channel.PropertyName, StringComparer.OrdinalIgnoreCase);
            hash.Add(channel.DefaultValue);
            hash.Add(channel.Keyframes.Count);
            foreach (var keyframe in channel.Keyframes)
            {
                hash.Add(keyframe.Id);
                hash.Add(keyframe.Time);
                hash.Add(keyframe.Value);
                hash.Add(keyframe.Interpolation);
                hash.Add(keyframe.InTangentX);
                hash.Add(keyframe.InTangentY);
                hash.Add(keyframe.OutTangentX);
                hash.Add(keyframe.OutTangentY);
            }
        }
        foreach (var effect in item.Effects)
        {
            hash.Add(effect.Id);
            hash.Add(effect.EffectTypeId, StringComparer.OrdinalIgnoreCase);
            hash.Add(effect.Enabled);
            foreach (var parameter in effect.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                hash.Add(parameter.Key, StringComparer.Ordinal);
                hash.Add(parameter.Value);
            }
        }
        return hash.ToHashCode();
    }

    private static string Format(double value) => value.ToString("0.###############", CultureInfo.InvariantCulture);
}
