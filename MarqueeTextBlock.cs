using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DNFWeeklyWidget;

public class MarqueeTextBlock : FrameworkElement
{
	private const double ScrollSpeed = 24.0;
	private static readonly TimeSpan StartPause = TimeSpan.FromMilliseconds(900);
	private static readonly TimeSpan EndPause = TimeSpan.FromMilliseconds(1200);
	private static readonly HashSet<MarqueeTextBlock> ActiveInstances = [];
	private static readonly List<MarqueeTextBlock> RenderingSnapshot = [];
	private static bool _isRenderingSubscribed;
	private static bool _isAnimationSuspended;

	public static readonly DependencyProperty TextProperty =
		DependencyProperty.Register(
			nameof(Text),
			typeof(string),
			typeof(MarqueeTextBlock),
			new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnTextPropertyChanged));

	public static readonly DependencyProperty ForegroundProperty =
		DependencyProperty.Register(
			nameof(Foreground),
			typeof(System.Windows.Media.Brush),
			typeof(MarqueeTextBlock),
			new FrameworkPropertyMetadata(System.Windows.Media.Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FontSizeProperty =
		TextElement.FontSizeProperty.AddOwner(
			typeof(MarqueeTextBlock),
			new FrameworkPropertyMetadata(13.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FontFamilyProperty =
		TextElement.FontFamilyProperty.AddOwner(
			typeof(MarqueeTextBlock),
			new FrameworkPropertyMetadata(System.Windows.SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FontWeightProperty =
		TextElement.FontWeightProperty.AddOwner(
			typeof(MarqueeTextBlock),
			new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

	private DateTime _lastTick = DateTime.UtcNow;
	private DateTime _pauseUntil = DateTime.MinValue;
	private double _offset;
	private double _textWidth;
	private bool _resetAfterPause;
	private FormattedText? _formattedText;
	private double _formattedTextPixelsPerDip;

	public MarqueeTextBlock()
	{
		IsVisibleChanged += (_, _) => UpdateActiveState();
		SizeChanged += (_, _) => UpdateActiveState();
		Loaded += (_, _) => UpdateActiveState();
		Unloaded += (_, _) => RemoveActiveInstance();
	}

	public static bool IsAnimationSuspended
	{
		get => _isAnimationSuspended;
		set
		{
			if (_isAnimationSuspended == value)
				return;

			_isAnimationSuspended = value;
			var now = DateTime.UtcNow;
			foreach (var marquee in ActiveInstances)
				marquee._lastTick = now;
		}
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public System.Windows.Media.Brush Foreground
	{
		get => (System.Windows.Media.Brush)GetValue(ForegroundProperty);
		set => SetValue(ForegroundProperty, value);
	}

	public double FontSize
	{
		get => (double)GetValue(FontSizeProperty);
		set => SetValue(FontSizeProperty, value);
	}

	public System.Windows.Media.FontFamily FontFamily
	{
		get => (System.Windows.Media.FontFamily)GetValue(FontFamilyProperty);
		set => SetValue(FontFamilyProperty, value);
	}

	public FontWeight FontWeight
	{
		get => (FontWeight)GetValue(FontWeightProperty);
		set => SetValue(FontWeightProperty, value);
	}

	protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
	{
		var text = GetFormattedText();
		_textWidth = text.WidthIncludingTrailingWhitespace;
		return new System.Windows.Size(0, Math.Ceiling(text.Height));
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		base.OnRender(drawingContext);

		var text = GetFormattedText();
		_textWidth = text.WidthIncludingTrailingWhitespace;
		var y = Math.Max(0, (ActualHeight - text.Height) / 2);
		var x = ShouldMarquee() ? -Math.Min(_offset, MaxOffset) : 0;
		drawingContext.DrawText(text, new System.Windows.Point(x, y));
	}

	private static void OnRendering(object? sender, EventArgs e)
	{
		if (IsAnimationSuspended)
			return;

		var now = DateTime.UtcNow;
		RenderingSnapshot.Clear();
		RenderingSnapshot.AddRange(ActiveInstances);
		foreach (var marquee in RenderingSnapshot)
			marquee.Advance(now);
	}

	private void Advance(DateTime now)
	{
		if (!ShouldMarquee())
		{
			RemoveActiveInstance();
			return;
		}

		if (now < _pauseUntil)
		{
			_lastTick = now;
			return;
		}

		if (_resetAfterPause)
		{
			_offset = 0;
			_resetAfterPause = false;
			_pauseUntil = now + StartPause;
			_lastTick = now;
			InvalidateVisual();
			return;
		}

		var elapsed = Math.Max(0, (now - _lastTick).TotalSeconds);
		_lastTick = now;
		_offset += elapsed * ScrollSpeed;
		if (_offset >= MaxOffset)
		{
			_offset = MaxOffset;
			_resetAfterPause = true;
			_pauseUntil = now + EndPause;
		}

		InvalidateVisual();
	}

	private void UpdateActiveState()
	{
		if (!ShouldMarquee())
		{
			RemoveActiveInstance();
			_offset = 0;
			_pauseUntil = DateTime.MinValue;
			_resetAfterPause = false;
			InvalidateVisual();
			return;
		}

		if (_pauseUntil == DateTime.MinValue)
			_pauseUntil = DateTime.UtcNow + StartPause;

		_lastTick = DateTime.UtcNow;
		ActiveInstances.Add(this);
		EnsureRenderingSubscription();
	}

	private void RemoveActiveInstance()
	{
		ActiveInstances.Remove(this);
		if (ActiveInstances.Count == 0 && _isRenderingSubscribed)
		{
			CompositionTarget.Rendering -= OnRendering;
			_isRenderingSubscribed = false;
		}
	}

	private static void EnsureRenderingSubscription()
	{
		if (_isRenderingSubscribed)
			return;

		CompositionTarget.Rendering += OnRendering;
		_isRenderingSubscribed = true;
	}

	private bool ShouldMarquee() =>
		IsLoaded && IsVisible && ActualWidth > 0 && _textWidth > ActualWidth + 2;

	private double MaxOffset => Math.Max(0, _textWidth - ActualWidth);

	protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.Property == TextProperty ||
			e.Property == ForegroundProperty ||
			e.Property == FontSizeProperty ||
			e.Property == FontFamilyProperty ||
			e.Property == FontWeightProperty)
		{
			_formattedText = null;
		}
	}

	private FormattedText GetFormattedText()
	{
		var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
		if (_formattedText is not null && Math.Abs(_formattedTextPixelsPerDip - dpi) < 0.001)
			return _formattedText;

		_formattedTextPixelsPerDip = dpi;
		_formattedText = new FormattedText(
			Text ?? "",
			CultureInfo.CurrentUICulture,
			System.Windows.FlowDirection.LeftToRight,
			new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
			FontSize,
			Foreground,
			dpi);
		return _formattedText;
	}

	private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not MarqueeTextBlock marquee)
			return;

		marquee._offset = 0;
		marquee._pauseUntil = DateTime.UtcNow + StartPause;
		marquee._resetAfterPause = false;
		marquee.InvalidateMeasure();
		marquee.InvalidateVisual();
		marquee.Dispatcher.BeginInvoke(marquee.UpdateActiveState, System.Windows.Threading.DispatcherPriority.Loaded);
	}
}
