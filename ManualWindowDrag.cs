using System.Windows;
using System.Windows.Input;

namespace DNFWeeklyWidget;

internal sealed class ManualWindowDrag
{
	private readonly Window _window;
	private System.Windows.Point _startMouseScreenPosition;
	private double _startLeft;
	private double _startTop;
	private bool _isDragging;

	public ManualWindowDrag(Window window)
	{
		_window = window;
	}

	public void Start(MouseButtonEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed)
			return;

		_startMouseScreenPosition = _window.PointToScreen(e.GetPosition(_window));
		_startLeft = _window.Left;
		_startTop = _window.Top;
		_isDragging = true;

		_window.CaptureMouse();
		_window.MouseMove += Window_MouseMove;
		_window.MouseLeftButtonUp += Window_MouseLeftButtonUp;
		_window.LostMouseCapture += Window_LostMouseCapture;
		e.Handled = true;
	}

	private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
		{
			Stop();
			return;
		}

		var currentMouseScreenPosition = _window.PointToScreen(e.GetPosition(_window));
		var offset = currentMouseScreenPosition - _startMouseScreenPosition;
		_window.Left = _startLeft + offset.X;
		_window.Top = _startTop + offset.Y;
	}

	private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		Stop();
	}

	private void Window_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
	{
		Stop();
	}

	private void Stop()
	{
		if (!_isDragging)
			return;

		_isDragging = false;
		_window.MouseMove -= Window_MouseMove;
		_window.MouseLeftButtonUp -= Window_MouseLeftButtonUp;
		_window.LostMouseCapture -= Window_LostMouseCapture;

		if (_window.IsMouseCaptured)
			_window.ReleaseMouseCapture();
	}
}
