using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Rushframe.Desktop.Services;
using Rushframe.Domain;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private const int MaximumRealtimeMediaPlayers = 8;
    private readonly Stopwatch _realtimePreviewClock = new();
    private readonly Dictionary<TimelineItemId, RealtimePreviewLayer> _realtimePreviewLayers = [];
    private readonly Dictionary<TimelineItemId, RealtimeAudioLayer> _realtimeAudioLayers = [];
    private readonly List<RealtimeRenderPlan.VisualEntry> _activeVisualBuffer = [];
    private readonly List<RealtimeRenderPlan.AudioEntry> _activeAudioBuffer = [];
    private readonly HashSet<TimelineItemId> _activeVisualIds = [];
    private readonly HashSet<TimelineItemId> _activeAudioIds = [];
    private readonly List<TimelineItemId> _layerRemovalBuffer = [];
    private RealtimeRenderPlan? _realtimeRenderPlan;
    private double _realtimePreviewPositionSeconds;
    private double _realtimePreviewClockBaseSeconds;
    private bool _isRealtimeTimelinePreview;

    private bool TryShowRealtimeTimelinePreview(double timelineSeconds)
    {
        var sequence = _project.MainSequence;
        if (sequence == null || sequence.Duration.Seconds <= 0 || !CanUseRealtimeTimelinePreview(sequence))
            return false;
        var renderPlan = GetRealtimeRenderPlan(sequence);
        if (renderPlan.MaxConcurrentMediaPlayers > MaximumRealtimeMediaPlayers) return false;

        try
        {
            if (_isRealtimeTimelinePreview)
                StopRealtimeTimelinePreview(clearSurface: true);
            _isRealtimeTimelinePreview = true;
            _isTimelineCompositePreview = true;
            _previewTimelineItemId = null;
            _previewTimelineItemCache = null;
            _previewAsset = null;
            _realtimePreviewClock.Stop();
            _realtimePreviewPositionSeconds = Math.Clamp(timelineSeconds, 0, sequence.Duration.Seconds);
            _realtimePreviewClockBaseSeconds = _realtimePreviewPositionSeconds;

            ClearPreviewMedia();
            PreviewPlayer.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            RealtimePreviewCanvas.Width = sequence.Width;
            RealtimePreviewCanvas.Height = sequence.Height;
            RealtimePreviewCanvas.Background = CreateRealtimeBackground(sequence.Background);
            RealtimePreviewViewbox.Visibility = Visibility.Visible;

            PreviewSeekSlider.Minimum = 0;
            PreviewSeekSlider.Maximum = Math.Max(0.001, sequence.Duration.Seconds);
            SetPreviewSeekSliderValue(_realtimePreviewPositionSeconds);
            PreviewDurationText.Text = FormatPreviewTime(TimeSpan.FromSeconds(sequence.Duration.Seconds));
            PreviewSourceNameText.Text = "Timeline · Real-time layered preview";
            PreviewTimeText.Text = FormatPreviewTime(TimeSpan.FromSeconds(_realtimePreviewPositionSeconds));
            PreviewTimeBox.Text = PreviewTimeText.Text;
            RenderRealtimeTimelineFrame(_realtimePreviewPositionSeconds, forceMediaSeek: true);
            SetPreviewControlsEnabled(true);
            UpdatePreviewInteractionOverlay(_selectedInspectorItem);
            RefreshPreviewGuidesOverlay();
            return true;
        }
        catch (Exception ex)
        {
            StopRealtimeTimelinePreview(clearSurface: true);
            var message = $"Realtime preview unavailable: {ex.Message}. Using exact preview.";
            StatusText.Text = message;
            AddRenderQueueMessage(message);
            return false;
        }
    }

    private RealtimeRenderPlan GetRealtimeRenderPlan(Sequence sequence)
    {
        if (_realtimeRenderPlan == null
            || _realtimeRenderPlan.Revision != _project.Revision
            || !ReferenceEquals(_realtimeRenderPlan.Sequence, sequence))
            _realtimeRenderPlan = RealtimeRenderPlan.Build(_project, sequence);
        return _realtimeRenderPlan;
    }

    private static bool CanUseRealtimeTimelinePreview(Sequence sequence)
    {
        if (sequence.Background.Kind == CanvasBackgroundKind.BlurSource) return false;

        foreach (var item in sequence.Tracks.SelectMany(track => track.Items))
        {
            if (item.Kind == ItemKind.AdjustmentLayer || item.Reversed) return false;
            if (item.BlendMode != BlendMode.Normal) return false;
            if (item.CropLeft + item.CropTop + item.CropRight + item.CropBottom > 0.0001) return false;
            if (item.ChromaKey != null || item.ColorCorrection != null || item.Stabilization?.Enabled == true)
                return false;
            if (item.Masks.Count > 1
                || item.Masks.Any(mask =>
                    mask.Shape is not (MaskShape.Rectangle or MaskShape.Ellipse)
                    || mask.Inverted
                    || mask.Feather > 0.001
                    || Math.Abs(mask.RotationDegrees) > 0.001
                    || Math.Abs(mask.Expansion) > 0.001))
                return false;
            if (item.Effects.Any(effect => effect.Enabled && !string.Equals(effect.EffectTypeId, "blur", StringComparison.OrdinalIgnoreCase)))
                return false;
            if (item.Kind == ItemKind.Text
                && (item.OutlineWidth > 0.001
                    || (item.ShadowOpacity > 0.001 && item.Effects.Any(effect => effect.Enabled && effect.EffectTypeId == "blur"))))
                return false;

            var speed = item.SpeedCurve?.ConstantSpeed ?? item.Speed;
            if (Math.Abs(speed - 1) > 0.0001 && item.SpeedCurve?.PreservePitch == true)
                return false;
            if (item.Volume > 1.0001
                || item.GetAnimationChannel(AnimationPropertyNames.Volume)?.Keyframes.Any(keyframe => keyframe.Value > 1.0001) == true)
                return false;
        }

        return sequence.Transitions.All(transition =>
            transition.Kind is TransitionKind.CrossDissolve or TransitionKind.Slide or TransitionKind.Zoom);
    }

    private void RenderRealtimeTimelineFrame(double timelineSeconds, bool forceMediaSeek)
    {
        var sequence = _project.MainSequence;
        if (!_isRealtimeTimelinePreview || sequence == null) return;

        var started = Stopwatch.GetTimestamp();
        var renderPlan = GetRealtimeRenderPlan(sequence);
        timelineSeconds = Math.Clamp(timelineSeconds, 0, renderPlan.DurationSeconds);
        renderPlan.CollectWarmVisuals(
            timelineSeconds,
            Math.Clamp(_settings.PreviewLookAheadSeconds, 0, 2),
            _activeVisualBuffer);
        renderPlan.CollectActiveAudio(timelineSeconds, _activeAudioBuffer);

        _activeVisualIds.Clear();
        foreach (var entry in _activeVisualBuffer) _activeVisualIds.Add(entry.Item.Id);
        _layerRemovalBuffer.Clear();
        foreach (var itemId in _realtimePreviewLayers.Keys)
            if (!_activeVisualIds.Contains(itemId)) _layerRemovalBuffer.Add(itemId);
        foreach (var itemId in _layerRemovalBuffer)
        {
            var layer = _realtimePreviewLayers[itemId];
            if (layer.Player != null)
            {
                try { layer.Player.Stop(); } catch { }
                layer.Player.Source = null;
            }
            RealtimePreviewCanvas.Children.Remove(layer.Element);
            _realtimePreviewLayers.Remove(itemId);
        }

        foreach (var entry in _activeVisualBuffer)
        {
            var presentation = renderPlan.GetPresentation(entry, timelineSeconds);
            if (!_realtimePreviewLayers.TryGetValue(entry.Item.Id, out var layer))
            {
                layer = CreateRealtimeVisualLayer(sequence, entry, presentation);
                if (layer == null) continue;
                _realtimePreviewLayers[entry.Item.Id] = layer;
                Panel.SetZIndex(layer.Element, entry.Track.Order);
                RealtimePreviewCanvas.Children.Add(layer.Element);
                forceMediaSeek = true;
            }
            UpdateRealtimeVisualLayer(sequence, layer, presentation, timelineSeconds, forceMediaSeek);
        }

        _activeAudioIds.Clear();
        foreach (var entry in _activeAudioBuffer) _activeAudioIds.Add(entry.Item.Id);
        _layerRemovalBuffer.Clear();
        foreach (var itemId in _realtimeAudioLayers.Keys)
            if (!_activeAudioIds.Contains(itemId)) _layerRemovalBuffer.Add(itemId);
        foreach (var itemId in _layerRemovalBuffer)
        {
            var layer = _realtimeAudioLayers[itemId];
            try { layer.Player.Stop(); } catch { }
            layer.Player.Source = null;
            RealtimePreviewCanvas.Children.Remove(layer.Player);
            _realtimeAudioLayers.Remove(itemId);
        }

        foreach (var entry in _activeAudioBuffer)
        {
            if (!_realtimeAudioLayers.TryGetValue(entry.Item.Id, out var layer))
            {
                layer = CreateRealtimeAudioLayer(entry);
                _realtimeAudioLayers[entry.Item.Id] = layer;
                RealtimePreviewCanvas.Children.Add(layer.Player);
                forceMediaSeek = true;
            }
            UpdateRealtimeMediaPlayer(layer.Player, layer.Track, layer.Item, timelineSeconds, forceMediaSeek);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var frameBudget = TimeSpan.FromSeconds(1 / GetPreviewTargetFramesPerSecond());
        _performanceTelemetry.RecordPreviewFrame(
            elapsed,
            _realtimePreviewLayers.Count + _realtimeAudioLayers.Count,
            elapsed > frameBudget);
    }

    private RealtimePreviewLayer? CreateRealtimeVisualLayer(
        Sequence sequence,
        RealtimeRenderPlan.VisualEntry entry,
        RealtimeRenderPlan.RealtimePresentation presentation)
    {
        var item = entry.Item;
        FrameworkElement? element = item.Kind switch
        {
            ItemKind.Text => CreateRealtimeTextElement(item),
            ItemKind.Sticker when item.StickerId?.StartsWith("builtin.shape.", StringComparison.OrdinalIgnoreCase) == true
                => CreateRealtimeBuiltInSticker(item),
            _ => CreateRealtimeMediaVisual(sequence, item, entry.Media),
        };
        if (element == null) return null;

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.IsHitTestVisible = false;
        ApplyRealtimeClip(element, item);
        var scale = new ScaleTransform();
        var rotation = new RotateTransform();
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(rotation);
        element.RenderTransform = transforms;

        var blurDefinition = item.Effects.FirstOrDefault(effect =>
            effect.Enabled && string.Equals(effect.EffectTypeId, "blur", StringComparison.OrdinalIgnoreCase));
        BlurEffect? blur = null;
        if (blurDefinition != null && element is not TextBlock { Effect: not null })
        {
            blur = new BlurEffect
            {
                Radius = Math.Clamp(GetRealtimeEffectParameter(blurDefinition, "strength", 5), 0, 50),
            };
            element.Effect = blur;
        }

        var media = element as MediaElement;
        var layer = new RealtimePreviewLayer(entry.Track, item, element, media, scale, rotation, blur);
        UpdateRealtimeVisualLayer(sequence, layer, presentation, _realtimePreviewPositionSeconds, forceMediaSeek: true);
        return layer;
    }

    private FrameworkElement? CreateRealtimeMediaVisual(Sequence sequence, TimelineItem item, MediaAsset? asset)
    {
        if (asset == null || asset.IsOffline || !File.Exists(asset.OriginalPath)) return null;

        if (asset.Kind == MediaKind.Image)
        {
            var image = new Image { Stretch = Stretch.Fill };
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            var decodeWidth = Math.Max(2, Math.Min(
                asset.PixelWidth > 0 ? asset.PixelWidth : sequence.Width,
                Math.Clamp(_settings.PreviewMaxWidth, 480, 1920)));
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(asset.OriginalPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
            image.Width = asset.PixelWidth > 0 ? asset.PixelWidth : bitmap.PixelWidth;
            image.Height = asset.PixelHeight > 0 ? asset.PixelHeight : bitmap.PixelHeight;
            return image;
        }

        var player = new MediaElement
        {
            Source = new Uri(asset.OriginalPath, UriKind.Absolute),
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            ScrubbingEnabled = true,
            Stretch = Stretch.Fill,
            Width = asset.PixelWidth > 0 ? asset.PixelWidth : sequence.Width,
            Height = asset.PixelHeight > 0 ? asset.PixelHeight : sequence.Height,
        };
        player.MediaOpened += (_, _) =>
        {
            SeekRealtimeMediaPlayer(player, item, GetPreviewPosition().TotalSeconds);
            if (_isPreviewPlaying) player.Play(); else player.Pause();
        };
        return player;
    }

    private static FrameworkElement CreateRealtimeTextElement(TimelineItem item)
    {
        var measured = TextLayoutMetrics.Measure(item);
        var padding = Math.Max(1, Math.Max(0, item.OutlineWidth) + Math.Max(0, item.ShadowBlur) + Math.Max(Math.Abs(item.ShadowOffsetX), Math.Abs(item.ShadowOffsetY)));
        var text = new TextBlock
        {
            Text = item.TextContent ?? "Text",
            FontSize = Math.Max(1, item.FontSize),
            FontWeight = item.FontBold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = ParseBrush(item.FillColor, Colors.White),
            TextAlignment = item.FontAlign?.ToLowerInvariant() switch
            {
                "left" => TextAlignment.Left,
                "right" => TextAlignment.Right,
                _ => TextAlignment.Center,
            },
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(padding),
            Width = measured.Width,
            Height = measured.Height,
        };
        if (!string.IsNullOrWhiteSpace(item.FontFamily))
        {
            try
            {
                text.FontFamily = File.Exists(item.FontFamily)
                    ? ResolveFontFileFamily(item.FontFamily) ?? text.FontFamily
                    : new FontFamily(item.FontFamily);
            }
            catch
            {
                // The exact renderer will report an invalid registered font during render validation.
            }
        }
        if (item.ShadowOpacity > 0)
        {
            text.Effect = new DropShadowEffect
            {
                Color = ParseColor(item.ShadowColor, Colors.Black),
                Opacity = Math.Clamp(item.ShadowOpacity, 0, 1),
                BlurRadius = Math.Clamp(item.ShadowBlur, 0, 50),
                ShadowDepth = Math.Sqrt((item.ShadowOffsetX * item.ShadowOffsetX) + (item.ShadowOffsetY * item.ShadowOffsetY)),
                Direction = Math.Atan2(-item.ShadowOffsetY, item.ShadowOffsetX) * 180 / Math.PI,
            };
        }
        return text;
    }

    private static FontFamily? ResolveFontFileFamily(string fontPath)
    {
        var absolutePath = Path.GetFullPath(fontPath);
        var glyph = new GlyphTypeface(new Uri(absolutePath, UriKind.Absolute));
        var familyName = glyph.FamilyNames.TryGetValue(System.Globalization.CultureInfo.CurrentUICulture, out var localized)
            ? localized
            : glyph.FamilyNames.Values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(familyName)) return null;
        var directory = Path.GetDirectoryName(absolutePath);
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var baseUri = new Uri(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, UriKind.Absolute);
        return new FontFamily(baseUri, $"./#{familyName}");
    }

    private static FrameworkElement CreateRealtimeBuiltInSticker(TimelineItem item)
    {
        var glyph = item.StickerId switch
        {
            "builtin.shape.star" => "★",
            "builtin.shape.circle" => "●",
            "builtin.shape.triangle" => "▲",
            "builtin.shape.diamond" => "◆",
            "builtin.shape.arrow" => "➜",
            "builtin.shape.heart" => "♥",
            "builtin.shape.speech" => "▰",
            _ => "◆",
        };
        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 360,
            Foreground = ParseBrush(item.FillColor, Colors.White),
            TextAlignment = TextAlignment.Center,
            Width = 512,
            Height = 512,
        };
    }

    private void UpdateRealtimeVisualLayer(
        Sequence sequence,
        RealtimePreviewLayer layer,
        RealtimeRenderPlan.RealtimePresentation presentation,
        double timelineSeconds,
        bool forceMediaSeek)
    {
        var item = layer.Item;
        var becameActive = presentation.IsActive && !layer.WasActive;
        if (!presentation.IsActive)
        {
            layer.Element.Visibility = Visibility.Collapsed;
            if (layer.Player != null && layer.WasActive) layer.Player.Pause();
            layer.WasActive = false;
            return;
        }

        layer.Element.Visibility = Visibility.Visible;
        forceMediaSeek |= becameActive;
        var localTime = MediaTime.FromSeconds(Math.Clamp(timelineSeconds - item.TimelineStart.Seconds, 0, Math.Max(item.Duration.Seconds, 0)));
        var scaleX = item.GetAnimatedValue(AnimationPropertyNames.ScaleX, localTime, item.Transform.ScaleX) * presentation.ScaleMultiplier;
        var scaleY = item.GetAnimatedValue(AnimationPropertyNames.ScaleY, localTime, item.Transform.ScaleY) * presentation.ScaleMultiplier;
        var positionX = item.GetAnimatedValue(AnimationPropertyNames.PositionX, localTime, item.Transform.PositionX) + presentation.OffsetX;
        var positionY = item.GetAnimatedValue(AnimationPropertyNames.PositionY, localTime, item.Transform.PositionY) + presentation.OffsetY;
        var rotation = item.GetAnimatedValue(AnimationPropertyNames.Rotation, localTime, item.Transform.RotationDegrees);
        var opacity = item.GetAnimatedValue(AnimationPropertyNames.Opacity, localTime, item.Opacity)
                      * presentation.Opacity
                      * ComputeRealtimeItemFade(item, localTime.Seconds);

        var naturalWidth = Math.Max(1, layer.Element.Width > 0 ? layer.Element.Width : sequence.Width);
        var naturalHeight = Math.Max(1, layer.Element.Height > 0 ? layer.Element.Height : sequence.Height);
        Canvas.SetLeft(layer.Element, ((sequence.Width - naturalWidth) / 2) + positionX);
        Canvas.SetTop(layer.Element, ((sequence.Height - naturalHeight) / 2) + positionY);
        layer.Element.Opacity = Math.Clamp(opacity, 0, 1);
        layer.Scale.ScaleX = scaleX;
        layer.Scale.ScaleY = scaleY;
        layer.Rotation.Angle = rotation;

        if (layer.Blur != null)
        {
            var blur = item.Effects.FirstOrDefault(effect =>
                effect.Enabled && string.Equals(effect.EffectTypeId, "blur", StringComparison.OrdinalIgnoreCase));
            if (blur != null)
                layer.Blur.Radius = Math.Clamp(GetRealtimeEffectParameter(blur, "strength", 5), 0, 50);
        }

        if (layer.Player != null)
        {
            UpdateRealtimeMediaPlayer(
                layer.Player,
                layer.Track,
                item,
                timelineSeconds,
                forceMediaSeek,
                presentation.Opacity);
            if (becameActive && _isPreviewPlaying) layer.Player.Play();
        }
        layer.WasActive = true;
    }

    private RealtimeAudioLayer CreateRealtimeAudioLayer(RealtimeRenderPlan.AudioEntry entry)
    {
        var player = new MediaElement
        {
            Source = new Uri(entry.Media.OriginalPath, UriKind.Absolute),
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Width = 1,
            Height = 1,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        player.MediaOpened += (_, _) =>
        {
            SeekRealtimeMediaPlayer(player, entry.Item, GetPreviewPosition().TotalSeconds);
            if (_isPreviewPlaying) player.Play(); else player.Pause();
        };
        return new RealtimeAudioLayer(entry.Track, entry.Item, player);
    }

    private void UpdateRealtimeMediaPlayer(
        MediaElement player,
        Track track,
        TimelineItem item,
        double timelineSeconds,
        bool forceSeek,
        double presentationOpacity = 1)
    {
        var speed = Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 16);
        player.SpeedRatio = Math.Clamp(speed * _previewTransportSpeed, 0.1, 16);
        player.IsMuted = PreviewMuteToggle.IsChecked == true || track.Muted || item.Muted;

        var localSeconds = Math.Clamp(
            timelineSeconds - item.TimelineStart.Seconds,
            0,
            Math.Max(0, item.Duration.Seconds));
        var localTime = MediaTime.FromSeconds(localSeconds);
        var animatedVolume = item.GetAnimatedValue(AnimationPropertyNames.Volume, localTime, item.Volume);
        var animatedPan = item.GetAnimatedValue(AnimationPropertyNames.Pan, localTime, item.Pan);
        var gain = animatedVolume
                   * ComputeRealtimeItemFade(item, localSeconds)
                   * presentationOpacity;
        player.Volume = Math.Clamp(PreviewVolumeSlider.Value * gain, 0, 1);
        player.Balance = Math.Clamp(animatedPan, -1, 1);

        if (forceSeek && player.NaturalDuration.HasTimeSpan)
            SeekRealtimeMediaPlayer(player, item, timelineSeconds);
    }

    private static void SeekRealtimeMediaPlayer(MediaElement player, TimelineItem item, double timelineSeconds)
    {
        var elapsed = timelineSeconds - item.TimelineStart.Seconds;
        var speed = Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 100);
        var source = item.Reversed
            ? item.SourceStart.Seconds + Math.Max(0, item.SourceDuration.Seconds - elapsed * speed)
            : item.SourceStart.Seconds + elapsed * speed;
        if (player.NaturalDuration.HasTimeSpan)
            source = Math.Clamp(source, 0, player.NaturalDuration.TimeSpan.TotalSeconds);
        player.Position = TimeSpan.FromSeconds(Math.Max(0, source));
    }

    private static double ComputeRealtimeItemFade(TimelineItem item, double localSeconds)
    {
        var opacity = 1.0;
        if (item.FadeInDuration.Seconds > 0)
            opacity *= Math.Clamp(localSeconds / item.FadeInDuration.Seconds, 0, 1);
        if (item.FadeOutDuration.Seconds > 0)
            opacity *= Math.Clamp((item.Duration.Seconds - localSeconds) / item.FadeOutDuration.Seconds, 0, 1);
        return opacity;
    }

    private static void ApplyRealtimeClip(FrameworkElement element, TimelineItem item)
    {
        var mask = item.Masks.FirstOrDefault();
        if (mask == null) return;
        var width = element.Width > 0 ? element.Width : 100;
        var height = element.Height > 0 ? element.Height : 100;
        var maskWidth = Math.Max(1, width * Math.Clamp(mask.ScaleX, 0.001, 1));
        var maskHeight = Math.Max(1, height * Math.Clamp(mask.ScaleY, 0.001, 1));
        var x = ((width - maskWidth) / 2) + mask.PositionX;
        var y = ((height - maskHeight) / 2) + mask.PositionY;
        element.Clip = mask.Shape == MaskShape.Ellipse
            ? new EllipseGeometry(new Rect(x, y, maskWidth, maskHeight))
            : new RectangleGeometry(new Rect(x, y, maskWidth, maskHeight));
    }

    private static Brush CreateRealtimeBackground(CanvasBackground background)
    {
        var opacity = Math.Clamp(background.Opacity, 0, 1);
        if (background.Kind == CanvasBackgroundKind.Transparent)
            return Brushes.Transparent;
        if (background.Kind is CanvasBackgroundKind.LinearGradient or CanvasBackgroundKind.RadialGradient)
        {
            GradientBrush brush = background.Kind == CanvasBackgroundKind.RadialGradient
                ? new RadialGradientBrush()
                : new LinearGradientBrush
                {
                    StartPoint = new Point(0.5 - Math.Cos(background.GradientAngleDegrees * Math.PI / 180) / 2,
                        0.5 - Math.Sin(background.GradientAngleDegrees * Math.PI / 180) / 2),
                    EndPoint = new Point(0.5 + Math.Cos(background.GradientAngleDegrees * Math.PI / 180) / 2,
                        0.5 + Math.Sin(background.GradientAngleDegrees * Math.PI / 180) / 2),
                };
            brush.GradientStops.Add(new GradientStop(ParseColor(background.PrimaryColor, Colors.Black), 0));
            brush.GradientStops.Add(new GradientStop(ParseColor(background.SecondaryColor, Colors.Black), 1));
            brush.Opacity = opacity;
            return brush;
        }
        var solid = new SolidColorBrush(ParseColor(background.PrimaryColor, Colors.Black)) { Opacity = opacity };
        return solid;
    }

    private static SolidColorBrush ParseBrush(string? value, Color fallback) => new(ParseColor(value, fallback));

    private static Color ParseColor(string? value, Color fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color) return color;
        }
        catch (FormatException)
        {
        }
        return fallback;
    }

    private static double GetRealtimeEffectParameter(EffectInstance effect, string name, double fallback)
    {
        if (!effect.Parameters.TryGetValue(name, out var value)) return fallback;
        return value switch
        {
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            System.Text.Json.JsonElement json when json.ValueKind == System.Text.Json.JsonValueKind.Number && json.TryGetDouble(out var number) => number,
            _ => fallback,
        };
    }

    private void StartRealtimePlayback()
    {
        _realtimePreviewClockBaseSeconds = _realtimePreviewPositionSeconds;
        _realtimePreviewClock.Restart();
        foreach (var player in EnumerateRealtimePlayers()) player.Play();
    }

    private void PauseRealtimePlayback()
    {
        _realtimePreviewPositionSeconds = GetRealtimePreviewPositionSeconds();
        _realtimePreviewClockBaseSeconds = _realtimePreviewPositionSeconds;
        _realtimePreviewClock.Stop();
        foreach (var player in EnumerateRealtimePlayers()) player.Pause();
    }

    private void StopRealtimeTimelinePreview(bool clearSurface)
    {
        _realtimePreviewClock.Stop();
        StopRealtimeMediaElements();
        _realtimePreviewLayers.Clear();
        _realtimeAudioLayers.Clear();
        if (clearSurface) RealtimePreviewCanvas.Children.Clear();
        RealtimePreviewViewbox.Visibility = Visibility.Collapsed;
        _isRealtimeTimelinePreview = false;
    }

    private void StopRealtimeMediaElements()
    {
        foreach (var player in EnumerateRealtimePlayers())
        {
            try { player.Stop(); } catch { }
            player.Source = null;
        }
    }

    private IEnumerable<MediaElement> EnumerateRealtimePlayers()
    {
        foreach (var layer in _realtimePreviewLayers.Values)
            if (layer.Player != null) yield return layer.Player;
        foreach (var layer in _realtimeAudioLayers.Values)
            yield return layer.Player;
    }

    private double GetRealtimePreviewPositionSeconds()
    {
        if (!_isRealtimeTimelinePreview || !_isPreviewPlaying || !_realtimePreviewClock.IsRunning)
            return _realtimePreviewPositionSeconds;
        return _realtimePreviewClockBaseSeconds + (_realtimePreviewClock.Elapsed.TotalSeconds * _previewTransportSpeed);
    }

    private void SetRealtimePreviewPosition(double seconds, bool forceSeek = true)
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return;
        var duration = GetRealtimeRenderPlan(sequence).DurationSeconds;
        _realtimePreviewPositionSeconds = Math.Clamp(seconds, 0, duration);
        _realtimePreviewClockBaseSeconds = _realtimePreviewPositionSeconds;
        if (_isPreviewPlaying) _realtimePreviewClock.Restart();
        RenderRealtimeTimelineFrame(_realtimePreviewPositionSeconds, forceSeek);
    }

    private void UpdateRealtimePreviewFrame()
    {
        if (!_isRealtimeTimelinePreview) return;
        var sequence = _project.MainSequence;
        if (sequence == null) return;
        var duration = GetRealtimeRenderPlan(sequence).DurationSeconds;
        var position = GetRealtimePreviewPositionSeconds();
        if (position >= duration)
        {
            if (PreviewLoopToggle.IsChecked == true)
            {
                SetRealtimePreviewPosition(0);
                position = 0;
            }
            else
            {
                _realtimePreviewPositionSeconds = duration;
                PausePreview();
                position = duration;
            }
        }
        _realtimePreviewPositionSeconds = Math.Clamp(position, 0, duration);
        RenderRealtimeTimelineFrame(_realtimePreviewPositionSeconds, forceMediaSeek: false);
    }

    private void SaveRealtimePreviewSnapshot(string outputPath)
    {
        var sequence = _project.MainSequence
                       ?? throw new InvalidOperationException("No active sequence is available.");
        var width = Math.Max(2, sequence.Width);
        var height = Math.Max(2, sequence.Height);
        RealtimePreviewCanvas.Measure(new Size(width, height));
        RealtimePreviewCanvas.Arrange(new Rect(0, 0, width, height));
        RealtimePreviewCanvas.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(RealtimePreviewCanvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private void UpdateRealtimePreviewProgressDisplay(bool renderFrame = true)
    {
        if (!_isRealtimeTimelinePreview) return;
        if (renderFrame && _isPreviewPlaying) UpdateRealtimePreviewFrame();
        var position = Math.Clamp(GetRealtimePreviewPositionSeconds(), PreviewSeekSlider.Minimum, PreviewSeekSlider.Maximum);
        if (!_isPreviewSeeking && Math.Abs(PreviewSeekSlider.Value - position) >= 0.001)
            SetPreviewSeekSliderValue(position);
        var formatted = FormatPreviewTime(TimeSpan.FromSeconds(position));
        SetTextIfChanged(PreviewTimeText, formatted);
        if (!PreviewTimeBox.IsKeyboardFocusWithin && !string.Equals(PreviewTimeBox.Text, formatted, StringComparison.Ordinal))
            PreviewTimeBox.Text = formatted;
        if (_timeline != null) _timeline.PlayheadTime = MediaTime.FromSeconds(position);
    }

    private void ApplyRealtimeAudioSettings()
    {
        if (!_isRealtimeTimelinePreview) return;
        foreach (var layer in _realtimePreviewLayers.Values.Where(layer => layer.Player != null))
            UpdateRealtimeMediaPlayer(layer.Player!, layer.Track, layer.Item, GetRealtimePreviewPositionSeconds(), forceSeek: false);
        foreach (var layer in _realtimeAudioLayers.Values)
            UpdateRealtimeMediaPlayer(layer.Player, layer.Track, layer.Item, GetRealtimePreviewPositionSeconds(), forceSeek: false);
    }

    private sealed class RealtimePreviewLayer
    {
        public RealtimePreviewLayer(
            Track track,
            TimelineItem item,
            FrameworkElement element,
            MediaElement? player,
            ScaleTransform scale,
            RotateTransform rotation,
            BlurEffect? blur)
        {
            Track = track;
            Item = item;
            Element = element;
            Player = player;
            Scale = scale;
            Rotation = rotation;
            Blur = blur;
        }

        public Track Track { get; }
        public TimelineItem Item { get; }
        public FrameworkElement Element { get; }
        public MediaElement? Player { get; }
        public ScaleTransform Scale { get; }
        public RotateTransform Rotation { get; }
        public BlurEffect? Blur { get; }
        public bool WasActive { get; set; }
    }
    private sealed record RealtimeAudioLayer(Track Track, TimelineItem Item, MediaElement Player);
}
