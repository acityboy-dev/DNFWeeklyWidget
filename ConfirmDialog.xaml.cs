using System.Windows;
using System.Windows.Input;

namespace DNFWeeklyWidget;

public partial class ConfirmDialog : Window
{
	private readonly ManualWindowDrag _windowDrag;
	private readonly bool _isLightTheme;
	private readonly bool _lowPerformanceMode;

	public ConfirmDialog(string title, string message, string confirmText, bool isLightTheme, bool lowPerformanceMode = false)
	{
		_isLightTheme = isLightTheme;
		_lowPerformanceMode = lowPerformanceMode;
		InitializeComponent();
		_windowDrag = new ManualWindowDrag(this);
		TitleText.Text = title;
		MessageText.Text = message;
		ConfirmButton.Content = confirmText;
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		WindowBackdrop.Apply(this, _isLightTheme, _lowPerformanceMode);
	}

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = true;
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
	}

	private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource is DependencyObject source &&
			FindAncestor<System.Windows.Controls.Button>(source) is not null)
		{
			return;
		}

		_windowDrag.Start(e);
	}

	private static T? FindAncestor<T>(DependencyObject current)
		where T : DependencyObject
	{
		var parent = GetParentObject(current);
		while (parent is not null)
		{
			if (parent is T match)
				return match;

			parent = GetParentObject(parent);
		}

		return null;
	}

	private static DependencyObject? GetParentObject(DependencyObject current)
	{
		if (current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
			return System.Windows.Media.VisualTreeHelper.GetParent(current);

		if (current is ContentElement contentElement)
			return ContentOperations.GetParent(contentElement) ??
				(contentElement as FrameworkContentElement)?.Parent;

		return null;
	}
}
