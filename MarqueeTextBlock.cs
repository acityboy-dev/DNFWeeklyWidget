using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace DNFWeeklyWidget;

public class MarqueeTextBlock : FrameworkElement
{
    private const double ScrollSpeed = 24.0;
    private static readonly TimeSpan StartPause = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan EndPause = TimeSpan.FromMilliseconds(1200);

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

    private readonly DispatcherTimer _timer;
    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _pauseUntil = DateTime.MinValue;
    private double _offset;
    private double _textWidth;
    private bool _resetAfterPause;

    public MarqueeTextBlock()
    {
        ClipToBounds = true;
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += Timer_Tick;
        IsVisibleChanged += (_, _) => UpdateTimer();
        SizeChanged += (_, _) => UpdateTimer();
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
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
        var text = CreateFormattedText();
        _textWidth = text.WidthIncludingTrailingWhitespace;
        var height = Math.Ceiling(text.Height);
        return new System.Windows.Size(0, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var text = CreateFormattedText();
        _textWidth = text.WidthIncludingTrailingWhitespace;
        var y = Math.Max(0, (ActualHeight - text.Height) / 2);

        if (!ShouldMarquee())
        {
            drawingContext.DrawText(text, new System.Windows.Point(0, y));
            return;
        }

        var x = -Math.Min(_offset, MaxOffset);
        drawingContext.DrawText(text, new System.Windows.Point(x, y));
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
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

    private void UpdateTimer()
    {
        if (ShouldMarquee())
        {
            if (_pauseUntil == DateTime.MinValue)
                _pauseUntil = DateTime.UtcNow + StartPause;

            _lastTick = DateTime.UtcNow;
            if (!_timer.IsEnabled)
                _timer.Start();
        }
        else
        {
            _timer.Stop();
            _offset = 0;
            _pauseUntil = DateTime.MinValue;
            _resetAfterPause = false;
            InvalidateVisual();
        }
    }

    private bool ShouldMarquee()
    {
        return IsVisible && ActualWidth > 0 && _textWidth > ActualWidth + 2;
    }

    private double MaxOffset => Math.Max(0, _textWidth - ActualWidth);

    private FormattedText CreateFormattedText()
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new FormattedText(
            Text ?? "",
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
            FontSize,
            Foreground,
            dpi);
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock marquee)
        {
            marquee._offset = 0;
            marquee._pauseUntil = DateTime.UtcNow + StartPause;
            marquee._resetAfterPause = false;
            marquee.InvalidateMeasure();
            marquee.InvalidateVisual();
            marquee.Dispatcher.BeginInvoke(marquee.UpdateTimer, DispatcherPriority.Loaded);
        }
    }
}
