using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace DNFWeeklyWidget;

public partial class SettingsWindow : Window
{
	private sealed class ThemeModeOption
	{
		public string Label { get; init; } = "";
		public string Value { get; init; } = "system";
	}

	private sealed class CharacterImageModeOption
	{
		public string Label { get; init; } = "";
		public string Value { get; init; } = "compact";
	}

	private readonly bool _isLightTheme;
	private readonly Action<string>? _previewThemeMode;
	private readonly Action<bool>? _previewLowPerformanceMode;
	private readonly Action<string>? _previewCharacterImageMode;
	private readonly Action<int>? _previewColumns;
	private readonly Action<string>? _previewApiKey;
	private readonly Action<bool>? _previewAutoRefreshOnStartup;
	private readonly Action<bool>? _previewRunAtWindowsStartup;
	private readonly Action<int>? _previewAutoRefreshInterval;
	private readonly Action<bool>? _previewShowInTaskbar;
	private readonly Action<bool>? _previewEnableUserDataCache;
	private readonly Action<WeeklyContentSettings>? _previewWeeklyContents;
	private readonly Func<string, bool>? _resolveIsLightTheme;
	private readonly ManualWindowDrag _windowDrag;
	private bool _lowPerformanceMode;
	private bool _isUpdatingColumns;
	private bool _isUpdatingAutoRefreshInterval;

	public SettingsWindow(
		string apiKey,
		WeeklyContentSettings weeklyContents,
		string themeMode,
		bool lowPerformanceMode,
		string characterImageMode,
		int columns,
		bool autoRefreshOnStartup,
		bool runAtWindowsStartup,
		int autoRefreshIntervalMinutes,
		bool showInTaskbar,
		bool enableUserDataCache,
		bool isLightTheme,
		Action<string>? previewThemeMode = null,
		Action<bool>? previewLowPerformanceMode = null,
		Action<string>? previewCharacterImageMode = null,
		Action<int>? previewColumns = null,
		Action<string>? previewApiKey = null,
		Action<bool>? previewAutoRefreshOnStartup = null,
		Action<bool>? previewRunAtWindowsStartup = null,
		Action<int>? previewAutoRefreshInterval = null,
		Action<bool>? previewShowInTaskbar = null,
		Action<bool>? previewEnableUserDataCache = null,
		Action<WeeklyContentSettings>? previewWeeklyContents = null,
		Func<string, bool>? resolveIsLightTheme = null)
	{
		_isLightTheme = isLightTheme;
		_previewThemeMode = previewThemeMode;
		_previewLowPerformanceMode = previewLowPerformanceMode;
		_previewCharacterImageMode = previewCharacterImageMode;
		_previewColumns = previewColumns;
		_previewApiKey = previewApiKey;
		_previewAutoRefreshOnStartup = previewAutoRefreshOnStartup;
		_previewRunAtWindowsStartup = previewRunAtWindowsStartup;
		_previewAutoRefreshInterval = previewAutoRefreshInterval;
		_previewShowInTaskbar = previewShowInTaskbar;
		_previewEnableUserDataCache = previewEnableUserDataCache;
		_previewWeeklyContents = previewWeeklyContents;
		_resolveIsLightTheme = resolveIsLightTheme;
		InitializeComponent();
		_windowDrag = new ManualWindowDrag(this);
		_lowPerformanceMode = lowPerformanceMode;
		ThemeModeBox.ItemsSource = new List<ThemeModeOption>
		{
			new() { Label = "윈도우 테마 따르기", Value = "system" },
			new() { Label = "라이트", Value = "light" },
			new() { Label = "다크", Value = "dark" }
		};
		ThemeModeBox.SelectedValue = string.IsNullOrWhiteSpace(themeMode) ? "system" : themeMode;
		if (ThemeModeBox.SelectedValue is null)
			ThemeModeBox.SelectedValue = "system";
		ThemeModeBox.SelectionChanged += ThemeModeBox_SelectionChanged;
		LowPerformanceModeBox.IsChecked = lowPerformanceMode;
		CharacterImageModeBox.ItemsSource = new List<CharacterImageModeOption>
		{
			new() { Label = "켜기", Value = "full" },
			new() { Label = "컴팩트", Value = "compact" },
			new() { Label = "끄기", Value = "hidden" }
		};
		CharacterImageModeBox.SelectedValue = CharacterRow.NormalizeImageMode(characterImageMode);
		if (CharacterImageModeBox.SelectedValue is null)
			CharacterImageModeBox.SelectedValue = "compact";
		CharacterImageModeBox.SelectionChanged += CharacterImageModeBox_SelectionChanged;

		ApiKeyBox.Text = apiKey;
		ColumnsBox.Text = ClampColumns(columns).ToString();
		ColumnsSlider.Value = ClampColumns(columns);
		AutoRefreshOnStartupBox.IsChecked = autoRefreshOnStartup;
		RunAtWindowsStartupBox.IsChecked = runAtWindowsStartup;
		AutoRefreshIntervalBox.Text = ClampAutoRefreshInterval(autoRefreshIntervalMinutes).ToString();
		ShowInTaskbarBox.IsChecked = showInTaskbar;
		EnableUserDataCacheBox.IsChecked = enableUserDataCache;
		WeeklyEquipmentLootBox.IsChecked = weeklyContents.ShowWeeklyEquipmentLoot;
		WeeklyOathLootBox.IsChecked = weeklyContents.ShowWeeklyOathLoot;
		WeeklyCrystalLootBox.IsChecked = weeklyContents.ShowWeeklyCrystalLoot;
		VenusBox.IsChecked = weeklyContents.ShowVenus;
		ApocalypseBox.IsChecked = weeklyContents.ShowApocalypse;
		BakalRaidBox.IsChecked = weeklyContents.ShowBakalRaid;
		NormalNabelRaidBox.IsChecked = weeklyContents.ShowNabelRaid && weeklyContents.ShowNormalNabelRaid;
		HardNabelRaidBox.IsChecked = weeklyContents.ShowNabelRaid && weeklyContents.ShowHardNabelRaid;
		TwilightOfInaeBox.IsChecked = weeklyContents.ShowTwilightOfInae;
		DiregieRaidBox.IsChecked = weeklyContents.ShowDiregieRaid;
		HardMistRaidBox.IsChecked = weeklyContents.ShowHardMistRaid;

		ApiKeyBox.TextChanged += (_, _) => _previewApiKey?.Invoke(ApiKey);
		AutoRefreshOnStartupBox.Checked += PreviewAutoRefreshOnStartup;
		AutoRefreshOnStartupBox.Unchecked += PreviewAutoRefreshOnStartup;
		RunAtWindowsStartupBox.Checked += PreviewRunAtWindowsStartup;
		RunAtWindowsStartupBox.Unchecked += PreviewRunAtWindowsStartup;
		ShowInTaskbarBox.Checked += PreviewShowInTaskbar;
		ShowInTaskbarBox.Unchecked += PreviewShowInTaskbar;
		EnableUserDataCacheBox.Checked += PreviewEnableUserDataCache;
		EnableUserDataCacheBox.Unchecked += PreviewEnableUserDataCache;

		foreach (var checkBox in GetWeeklyContentCheckBoxes())
		{
			checkBox.Checked += PreviewWeeklyContents;
			checkBox.Unchecked += PreviewWeeklyContents;
		}
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		WindowBackdrop.Apply(this, _isLightTheme, _lowPerformanceMode);
	}

	public string ApiKey => ApiKeyBox.Text.Trim();
	public string ThemeMode => ThemeModeBox.SelectedValue as string ?? "system";
	public bool LowPerformanceMode => LowPerformanceModeBox.IsChecked == true;
	public string CharacterImageMode => CharacterImageModeBox.SelectedValue as string ?? "compact";
	public int Columns => int.TryParse(ColumnsBox.Text, out var columns)
		? ClampColumns(columns)
		: 4;
	public bool AutoRefreshOnStartup => AutoRefreshOnStartupBox.IsChecked == true;
	public bool RunAtWindowsStartup => RunAtWindowsStartupBox.IsChecked == true;
	public int AutoRefreshIntervalMinutes => int.TryParse(AutoRefreshIntervalBox.Text, out var minutes)
		? ClampAutoRefreshInterval(minutes)
		: 30;
	public bool ShowInTaskbarSetting => ShowInTaskbarBox.IsChecked == true;
	public bool EnableUserDataCache => EnableUserDataCacheBox.IsChecked == true;

	public WeeklyContentSettings WeeklyContents => new()
	{
		ShowWeeklyEquipmentLoot = WeeklyEquipmentLootBox.IsChecked == true,
		ShowWeeklyOathLoot = WeeklyOathLootBox.IsChecked == true,
		ShowWeeklyCrystalLoot = WeeklyCrystalLootBox.IsChecked == true,
		ShowVenus = VenusBox.IsChecked == true,
		ShowApocalypse = ApocalypseBox.IsChecked == true,
		ShowBakalRaid = BakalRaidBox.IsChecked == true,
		ShowNabelRaid = NormalNabelRaidBox.IsChecked == true || HardNabelRaidBox.IsChecked == true,
		ShowNormalNabelRaid = NormalNabelRaidBox.IsChecked == true,
		ShowHardNabelRaid = HardNabelRaidBox.IsChecked == true,
		ShowTwilightOfInae = TwilightOfInaeBox.IsChecked == true,
		ShowDiregieRaid = DiregieRaidBox.IsChecked == true,
		ShowHardMistRaid = HardMistRaidBox.IsChecked == true
	};

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = true;
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
	}

	private void ThemeModeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
		var themeMode = ThemeMode;
		_previewThemeMode?.Invoke(themeMode);

		if (_resolveIsLightTheme is not null)
			WindowBackdrop.Apply(this, _resolveIsLightTheme(themeMode), _lowPerformanceMode);
	}

	private void LowPerformanceModeBox_Changed(object sender, RoutedEventArgs e)
	{
		_lowPerformanceMode = LowPerformanceMode;
		_previewLowPerformanceMode?.Invoke(_lowPerformanceMode);

		if (_resolveIsLightTheme is not null)
			WindowBackdrop.Apply(this, _resolveIsLightTheme(ThemeMode), _lowPerformanceMode);
	}

	private void CharacterImageModeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
	{
		_previewCharacterImageMode?.Invoke(CharacterImageMode);
	}

	private void ColumnsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
	{
		if (_isUpdatingColumns ||
			!int.TryParse(ColumnsBox.Text, out var columns) ||
			columns < 1)
		{
			return;
		}

		columns = ClampColumns(columns);
		_isUpdatingColumns = true;
		try
		{
			if (ColumnsBox.Text != columns.ToString())
				ColumnsBox.Text = columns.ToString();
			ColumnsSlider.Value = columns;
		}
		finally
		{
			_isUpdatingColumns = false;
		}

		_previewColumns?.Invoke(columns);
	}

	private void ColumnsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (_isUpdatingColumns)
			return;

		var columns = ClampColumns((int)Math.Round(e.NewValue));
		_isUpdatingColumns = true;
		try
		{
			ColumnsBox.Text = columns.ToString();
		}
		finally
		{
			_isUpdatingColumns = false;
		}

		_previewColumns?.Invoke(columns);
	}

	private void AutoRefreshIntervalBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
	{
		if (_isUpdatingAutoRefreshInterval ||
			!int.TryParse(AutoRefreshIntervalBox.Text, out var minutes))
		{
			return;
		}

		minutes = ClampAutoRefreshInterval(minutes);
		if (AutoRefreshIntervalBox.Text == minutes.ToString())
		{
			_previewAutoRefreshInterval?.Invoke(minutes);
			return;
		}

		_isUpdatingAutoRefreshInterval = true;
		try
		{
			AutoRefreshIntervalBox.Text = minutes.ToString();
			AutoRefreshIntervalBox.CaretIndex = AutoRefreshIntervalBox.Text.Length;
		}
		finally
		{
			_isUpdatingAutoRefreshInterval = false;
		}

		_previewAutoRefreshInterval?.Invoke(minutes);
	}

	private void PreviewAutoRefreshOnStartup(object sender, RoutedEventArgs e)
	{
		_previewAutoRefreshOnStartup?.Invoke(AutoRefreshOnStartup);
	}

	private void PreviewRunAtWindowsStartup(object sender, RoutedEventArgs e)
	{
		_previewRunAtWindowsStartup?.Invoke(RunAtWindowsStartup);
	}

	private void PreviewShowInTaskbar(object sender, RoutedEventArgs e)
	{
		_previewShowInTaskbar?.Invoke(ShowInTaskbarSetting);
	}

	private void PreviewEnableUserDataCache(object sender, RoutedEventArgs e)
	{
		_previewEnableUserDataCache?.Invoke(EnableUserDataCache);
	}

	private void PreviewWeeklyContents(object sender, RoutedEventArgs e)
	{
		_previewWeeklyContents?.Invoke(WeeklyContents);
	}

	private System.Windows.Controls.CheckBox[] GetWeeklyContentCheckBoxes() =>
	[
		WeeklyEquipmentLootBox,
		WeeklyOathLootBox,
		WeeklyCrystalLootBox,
		VenusBox,
		ApocalypseBox,
		BakalRaidBox,
		HardMistRaidBox,
		NormalNabelRaidBox,
		HardNabelRaidBox,
		TwilightOfInaeBox,
		DiregieRaidBox
	];

	private static int ClampColumns(int columns)
	{
		return Math.Clamp(columns, 1, 10);
	}

	private static int ClampAutoRefreshInterval(int minutes)
	{
		return Math.Clamp(minutes, 5, 180);
	}

	private void NeopleApiLogo_Click(object sender, RoutedEventArgs e)
	{
		Process.Start(new ProcessStartInfo
		{
			FileName = "https://developers.neople.co.kr",
			UseShellExecute = true
		});
	}

	private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource is DependencyObject source &&
			(FindAncestor<System.Windows.Controls.Button>(source) is not null ||
			 FindAncestor<System.Windows.Controls.TextBox>(source) is not null ||
			 FindAncestor<System.Windows.Controls.ComboBox>(source) is not null ||
			 FindAncestor<System.Windows.Controls.CheckBox>(source) is not null ||
			 FindAncestor<System.Windows.Controls.Slider>(source) is not null))
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
