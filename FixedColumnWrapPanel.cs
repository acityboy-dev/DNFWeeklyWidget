using System.Windows;
using System.Windows.Controls;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSize = System.Windows.Size;

namespace DNFWeeklyWidget;

public class FixedColumnWrapPanel : WpfPanel
{
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
		var columns = Math.Max(1, Columns);
		var spacing = Math.Max(0, ItemSpacing);
		var panelWidth = double.IsInfinity(availableSize.Width)
			? InternalChildren.Cast<UIElement>().Select(child => child.DesiredSize.Width).DefaultIfEmpty(0).Max()
			: availableSize.Width;
		var itemWidth = GetItemWidth(panelWidth, columns, spacing);
		var totalHeight = 0.0;
		var rowHeight = 0.0;
		var previousRowHeight = 0.0;

		for (var index = 0; index < InternalChildren.Count; index++)
		{
			var child = InternalChildren[index];
			child.Measure(new WpfSize(itemWidth, double.PositiveInfinity));
			rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

			var isRowEnd = (index + 1) % columns == 0 || index == InternalChildren.Count - 1;
			if (!isRowEnd)
				continue;

			var rowStart = index - (index % columns);
			var rowCount = index - rowStart + 1;
			if (rowCount < columns && previousRowHeight > 0)
				rowHeight = Math.Max(rowHeight, previousRowHeight);

			totalHeight += rowHeight;
			previousRowHeight = rowHeight;
			if (index < InternalChildren.Count - 1)
				totalHeight += spacing;
			rowHeight = 0;
		}

		return new WpfSize(panelWidth, totalHeight);
	}

	protected override WpfSize ArrangeOverride(WpfSize finalSize)
	{
		var columns = Math.Max(1, Columns);
		var spacing = Math.Max(0, ItemSpacing);
		var itemWidth = GetItemWidth(finalSize.Width, columns, spacing);
		var y = 0.0;
		var previousRowHeight = 0.0;

		for (var rowStart = 0; rowStart < InternalChildren.Count; rowStart += columns)
		{
			var rowCount = Math.Min(columns, InternalChildren.Count - rowStart);
			var rowHeight = 0.0;
			for (var offset = 0; offset < rowCount; offset++)
				rowHeight = Math.Max(rowHeight, InternalChildren[rowStart + offset].DesiredSize.Height);
			if (rowCount < columns && previousRowHeight > 0)
				rowHeight = Math.Max(rowHeight, previousRowHeight);

			for (var offset = 0; offset < rowCount; offset++)
			{
				var x = offset * (itemWidth + spacing);
				InternalChildren[rowStart + offset].Arrange(new Rect(x, y, itemWidth, rowHeight));
			}

			previousRowHeight = rowHeight;
			y += rowHeight + spacing;
		}

		return finalSize;
	}

	private static double GetItemWidth(double panelWidth, int columns, double spacing)
	{
		var totalSpacing = Math.Max(0, columns - 1) * spacing;
		return Math.Max(0, Math.Floor((panelWidth - totalSpacing) / columns));
	}
}
