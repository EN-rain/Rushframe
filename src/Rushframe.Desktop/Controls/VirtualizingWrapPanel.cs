using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Rushframe.Desktop.Controls;

public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(108d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(102d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _extent;
    private Size _viewport;
    private Point _offset;

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        var viewportWidth = NormalizeViewportLength(availableSize.Width, ItemWidth);
        var viewportHeight = NormalizeViewportLength(availableSize.Height, ItemHeight);
        var itemsPerRow = GetItemsPerRow(viewportWidth);
        var totalRows = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)itemsPerRow);

        _viewport = new Size(viewportWidth, viewportHeight);
        _extent = new Size(viewportWidth, totalRows * ItemHeight);
        CoerceOffsets();

        if (itemCount == 0)
        {
            RemoveAllChildren();
            ScrollOwner?.InvalidateScrollInfo();
            return availableSize;
        }

        var firstVisibleRow = Math.Max(0, (int)Math.Floor(_offset.Y / ItemHeight));
        var visibleRowCount = Math.Max(1, (int)Math.Ceiling(viewportHeight / ItemHeight) + 1);
        var firstIndex = Math.Min(itemCount - 1, firstVisibleRow * itemsPerRow);
        var lastIndex = Math.Min(itemCount - 1, ((firstVisibleRow + visibleRowCount) * itemsPerRow) - 1);

        CleanUpItems(firstIndex, lastIndex);
        RealizeItems(firstIndex, lastIndex);
        ScrollOwner?.InvalidateScrollInfo();
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemsPerRow = GetItemsPerRow(Math.Max(1, finalSize.Width));
        var generator = ItemContainerGenerator;

        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var child = InternalChildren[childIndex];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0) continue;

            var column = itemIndex % itemsPerRow;
            var row = itemIndex / itemsPerRow;
            child.Arrange(new Rect(
                (column * ItemWidth) - _offset.X,
                (row * ItemHeight) - _offset.Y,
                ItemWidth,
                ItemHeight));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0) return;
        var itemsPerRow = GetItemsPerRow(Math.Max(1, _viewport.Width));
        var itemTop = (index / itemsPerRow) * ItemHeight;
        var itemBottom = itemTop + ItemHeight;

        if (itemTop < VerticalOffset)
            SetVerticalOffset(itemTop);
        else if (itemBottom > VerticalOffset + ViewportHeight)
            SetVerticalOffset(itemBottom - ViewportHeight);
    }

    private void RealizeItems(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        var startPosition = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = startPosition.Offset == 0
            ? startPosition.Index
            : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                        AddInternalChild(child);
                    else
                        InsertInternalChild(childIndex, child);

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(ItemWidth, ItemHeight));
            }
        }
    }

    private void CleanUpItems(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex) continue;

            if (generator is IRecyclingItemContainerGenerator recyclingGenerator)
                recyclingGenerator.Recycle(position, 1);
            else
                generator.Remove(position, 1);

            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void RemoveAllChildren()
    {
        if (InternalChildren.Count == 0) return;
        var generator = ItemContainerGenerator;
        var position = new GeneratorPosition(0, 0);
        if (generator is IRecyclingItemContainerGenerator recyclingGenerator)
            recyclingGenerator.Recycle(position, InternalChildren.Count);
        else
            generator.Remove(position, InternalChildren.Count);
        RemoveInternalChildRange(0, InternalChildren.Count);
    }

    private int GetItemsPerRow(double width) =>
        Math.Max(1, (int)Math.Floor(width / Math.Max(1, ItemWidth)));

    private static double NormalizeViewportLength(double value, double fallback) =>
        double.IsInfinity(value) || double.IsNaN(value) || value <= 0 ? Math.Max(1, fallback) : value;

    private void CoerceOffsets()
    {
        _offset.X = Math.Clamp(_offset.X, 0, Math.Max(0, _extent.Width - _viewport.Width));
        _offset.Y = Math.Clamp(_offset.Y, 0, Math.Max(0, _extent.Height - _viewport.Height));
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(VerticalOffset - (ItemHeight / 3));
    public void LineDown() => SetVerticalOffset(VerticalOffset + (ItemHeight / 3));
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - (ItemWidth / 3));
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + (ItemWidth / 3));
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - ItemWidth);
    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + ItemWidth);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

    public void SetHorizontalOffset(double offset)
    {
        if (!CanHorizontallyScroll) offset = 0;
        var coerced = Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth));
        if (Math.Abs(coerced - _offset.X) < 0.1) return;
        _offset.X = coerced;
        InvalidateArrange();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetVerticalOffset(double offset)
    {
        var coerced = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (Math.Abs(coerced - _offset.Y) < 0.1) return;
        _offset.Y = coerced;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var childIndex = InternalChildren.IndexOf(visual as UIElement);
        if (childIndex < 0) return Rect.Empty;
        var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
        BringIndexIntoView(itemIndex);
        return rectangle;
    }
}
