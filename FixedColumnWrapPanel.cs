using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfPanel = System.Windows.Controls.VirtualizingPanel;
using WpfSize = System.Windows.Size;

namespace DNFWeeklyWidget;

public class FixedColumnWrapPanel : WpfPanel
{
	private const int BufferRows = 4;
	private const double DefaultEstimatedRowHeight = 220.0;

	public static readonly DependencyProperty ColumnsProperty =
		DependencyProperty.Register(
			nameof(Columns),
			typeof(int),
			typeof(FixedColumnWrapPanel),
			new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

	public static readonly DependencyProperty ItemSpacingProperty =
		DependencyProperty.Register(
			nameof(ItemSpacing),
			typeof(double),
			typeof(FixedColumnWrapPanel),
			new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

	private readonly Dictionary<int, double> _rowHeights = [];
	private ScrollViewer? _scrollViewer;
	private double _estimatedRowHeight = DefaultEstimatedRowHeight;
	private double _lastItemWidth = double.NaN;
	private bool _isRemeasureQueued;

	public FixedColumnWrapPanel()
	{
		Loaded += (_, _) => AttachScrollViewer();
		Unloaded += (_, _) => DetachScrollViewer();
	}

	public int Columns
	{
		get => (int)GetValue(ColumnsProperty);
		set => SetValue(ColumnsProperty, value);
	}

	public double ItemSpacing
	{
		get => (double)GetValue(ItemSpacingProperty);
		set => SetValue(ItemSpacingProperty, value);
	}

	protected override WpfSize MeasureOverride(WpfSize availableSize)
	{
		AttachScrollViewer();

		var owner = ItemsControl.GetItemsOwner(this);
		var itemCount = owner?.Items.Count ?? 0;
		var columns = Math.Max(1, Columns);
		var spacing = Math.Max(0, ItemSpacing);
		var rowCount = (int)Math.Ceiling(itemCount / (double)columns);
		var panelWidth = ResolvePanelWidth(availableSize);
		var itemWidth = GetItemWidth(panelWidth, columns, spacing);
		if (double.IsNaN(_lastItemWidth) || Math.Abs(_lastItemWidth - itemWidth) >= 0.5)
		{
			_lastItemWidth = itemWidth;
			_rowHeights.Clear();
		}

		if (itemCount == 0)
		{
			CleanUpItems(0, -1);
			return new WpfSize(panelWidth, 0);
		}

		var viewportTop = _scrollViewer?.VerticalOffset ?? 0;
		var viewportHeight = _scrollViewer?.ViewportHeight ?? 0;
		if (viewportHeight <= 0 || double.IsInfinity(viewportHeight))
			viewportHeight = ActualHeight > 0 ? ActualHeight : 720;

		var firstVisibleRow = FindRowAtOffset(viewportTop, rowCount, spacing);
		var lastVisibleRow = FindRowAtOffset(viewportTop + viewportHeight, rowCount, spacing);
		var firstRow = Math.Max(0, firstVisibleRow - BufferRows);
		var lastRow = Math.Min(rowCount - 1, lastVisibleRow + BufferRows);
		var firstItem = firstRow * columns;
		var lastItem = Math.Min(itemCount - 1, ((lastRow + 1) * columns) - 1);

		CleanUpItems(firstItem, lastItem);
		var generatedNewContainers = GenerateAndMeasureItems(firstItem, lastItem, columns, itemWidth);
		if (UpdateEstimatedRowHeight() || generatedNewContainers)
			QueueRemeasure();

		return new WpfSize(panelWidth, GetTotalHeight(rowCount, spacing));
	}

	protected override WpfSize ArrangeOverride(WpfSize finalSize)
	{
		var columns = Math.Max(1, Columns);
		var spacing = Math.Max(0, ItemSpacing);
		var itemWidth = GetItemWidth(finalSize.Width, columns, spacing);
		var generator = ItemsControl.GetItemsOwner(this)?.ItemContainerGenerator;
		if (generator is null)
			return finalSize;

		foreach (UIElement child in InternalChildren)
		{
			var itemIndex = generator.IndexFromContainer(child);
			if (itemIndex < 0)
				continue;

			var row = itemIndex / columns;
			var column = itemIndex % columns;
			var x = column * (itemWidth + spacing);
			var y = GetRowTop(row, spacing);
			var height = GetRowHeight(row);
			child.Arrange(new Rect(x, y, itemWidth, height));
		}

		return finalSize;
	}

	protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
	{
		base.OnItemsChanged(sender, args);
		if (args.Action is NotifyCollectionChangedAction.Reset or
			NotifyCollectionChangedAction.Add or
			NotifyCollectionChangedAction.Remove or
			NotifyCollectionChangedAction.Replace or
			NotifyCollectionChangedAction.Move)
		{
			_rowHeights.Clear();
			_estimatedRowHeight = DefaultEstimatedRowHeight;
		}
	}

	private bool GenerateAndMeasureItems(int firstItem, int lastItem, int columns, double itemWidth)
	{
		var generator = (IItemContainerGenerator)ItemContainerGenerator;
		var startPosition = generator.GeneratorPositionFromIndex(firstItem);
		var childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;
		var measuredRowHeights = new Dictionary<int, double>();
		var generatedNewContainers = false;

		using (generator.StartAt(startPosition, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
		{
			for (var itemIndex = firstItem; itemIndex <= lastItem; itemIndex++, childIndex++)
			{
				var child = (UIElement)generator.GenerateNext(out var newlyRealized);
				if (newlyRealized)
				{
					generatedNewContainers = true;
					if (childIndex >= InternalChildren.Count)
						AddInternalChild(child);
					else
						InsertInternalChild(childIndex, child);

					generator.PrepareItemContainer(child);
				}

				child.Measure(new WpfSize(itemWidth, double.PositiveInfinity));
				var row = itemIndex / columns;
				var measuredHeight = Math.Max(1, child.DesiredSize.Height);
				if (!measuredRowHeights.TryGetValue(row, out var currentHeight) || measuredHeight > currentHeight)
					measuredRowHeights[row] = measuredHeight;
			}
		}

		foreach (var (row, height) in measuredRowHeights)
			_rowHeights[row] = height;

		return generatedNewContainers;
	}

	private void CleanUpItems(int firstItem, int lastItem)
	{
		var ownerGenerator = ItemsControl.GetItemsOwner(this)?.ItemContainerGenerator;
		if (ownerGenerator is null)
			return;
		var generator = (IItemContainerGenerator)ItemContainerGenerator;
		for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
		{
			var child = InternalChildren[childIndex];
			var itemIndex = ownerGenerator.IndexFromContainer(child);
			if (itemIndex >= firstItem && itemIndex <= lastItem)
				continue;

			if (itemIndex >= 0)
			{
				var generatorPosition = generator.GeneratorPositionFromIndex(itemIndex);
				if (generatorPosition.Index >= 0 && generatorPosition.Offset == 0)
					generator.Remove(generatorPosition, 1);
			}

			RemoveInternalChildRange(childIndex, 1);
		}
	}

	private double ResolvePanelWidth(WpfSize availableSize)
	{
		if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0)
			return availableSize.Width;

		if (_scrollViewer?.ViewportWidth > 0)
			return _scrollViewer.ViewportWidth;

		return Math.Max(0, ActualWidth);
	}

	private int FindRowAtOffset(double offset, int rowCount, double spacing)
	{
		var currentTop = 0.0;
		for (var row = 0; row < rowCount; row++)
		{
			var rowBottom = currentTop + GetRowHeight(row);
			if (offset <= rowBottom)
				return row;

			currentTop = rowBottom + spacing;
		}

		return Math.Max(0, rowCount - 1);
	}

	private double GetRowTop(int targetRow, double spacing)
	{
		var top = 0.0;
		for (var row = 0; row < targetRow; row++)
			top += GetRowHeight(row) + spacing;

		return top;
	}

	private double GetTotalHeight(int rowCount, double spacing)
	{
		if (rowCount == 0)
			return 0;

		var height = 0.0;
		for (var row = 0; row < rowCount; row++)
			height += GetRowHeight(row);

		return height + Math.Max(0, rowCount - 1) * spacing;
	}

	private double GetRowHeight(int row) =>
		_rowHeights.TryGetValue(row, out var height) ? height : _estimatedRowHeight;

	private bool UpdateEstimatedRowHeight()
	{
		if (_rowHeights.Count == 0)
			return false;

		var estimatedRowHeight = Math.Max(1, _rowHeights.Values.Average());
		if (Math.Abs(_estimatedRowHeight - estimatedRowHeight) < 0.5)
			return false;

		_estimatedRowHeight = estimatedRowHeight;
		return true;
	}

	private void QueueRemeasure()
	{
		if (_isRemeasureQueued)
			return;

		_isRemeasureQueued = true;
		Dispatcher.BeginInvoke(() =>
		{
			_isRemeasureQueued = false;
			InvalidateMeasure();
		}, System.Windows.Threading.DispatcherPriority.Loaded);
	}

	private void AttachScrollViewer()
	{
		if (_scrollViewer is not null)
			return;

		_scrollViewer = FindAncestorScrollViewer(this);
		if (_scrollViewer is not null)
			_scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
	}

	private void DetachScrollViewer()
	{
		if (_scrollViewer is not null)
			_scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

		_scrollViewer = null;
	}

	private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		if (Math.Abs(e.VerticalChange) > 0 || Math.Abs(e.ViewportHeightChange) > 0)
			InvalidateMeasure();
	}

	private static ScrollViewer? FindAncestorScrollViewer(DependencyObject current)
	{
		var parent = VisualTreeHelper.GetParent(current);
		while (parent is not null)
		{
			if (parent is ScrollViewer scrollViewer)
				return scrollViewer;

			parent = VisualTreeHelper.GetParent(parent);
		}

		return null;
	}

	private static double GetItemWidth(double panelWidth, int columns, double spacing)
	{
		var totalSpacing = Math.Max(0, columns - 1) * spacing;
		return Math.Max(0, Math.Floor((panelWidth - totalSpacing) / columns));
	}
}
