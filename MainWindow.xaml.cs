using System.ComponentModel;
using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DNFWeeklyWidget;

public partial class MainWindow : Window
{
	private const double CardGap = 12.0;
	private const double CardLayoutSafetyWidth = 0.0;
	private const double DeleteDropZoneRevealDistance = 260.0;
	private const double DropIndicatorFadeMilliseconds = 140.0;
	private const double CardScrollWheelStep = 168.0;
	private const double CompactHeaderWidthThreshold = 765.0;
	private const int WmEnterSizeMove = 0x0231;
	private const int WmExitSizeMove = 0x0232;

	private static class LogText
	{
		public const string LoadingComplete = "로딩 완료";
		public const string EnterCharacterName = "추가할 캐릭터명을 입력하세요.";
		public const string EnterAdventureName = "모험단명을 입력하세요.";
		public const string ImportAdventureLoading = "던담에서 불러오는 중입니다.";
		public const string SearchingAdventure = "던담에서 모험단을 검색하는 중입니다.";
		public const string NoAdventureCharacters = "던담에서 불러올 캐릭터가 없습니다. 모험단명이 맞는지 확인하세요.";
		public const string SettingsSavedPrefix = "설정을 저장했습니다.";
		public const string EnterApiKey = "API Key를 입력하세요.";
		public const string AddCharacterPrompt = "캐릭터를 추가하세요.";
		public const string RefreshLoading = "갱신 중입니다.";
		public const string CardOrderSavedPrefix = "카드 순서를 저장했습니다.";
		public const string IncompleteFilterOn = "미완료 콘텐츠가 있는 카드만 표시합니다.";
		public const string IncompleteFilterOff = "전체 카드를 표시합니다.";
		public const string FameSortOn = "명성순 자동 정렬을 켰습니다.";
		public const string FameSortOff = "명성순 자동 정렬을 껐습니다.";
		public const string AllCharactersCleared = "전체 카드 엔트리를 삭제했습니다.";
		public const string UserDataCacheDisabled = "사용자 데이터 저장이 꺼져 있어 이번 실행의 변경사항은 저장되지 않습니다.";
		public const string LastPresetCannotBeRemoved = "마지막 프리셋은 삭제할 수 없습니다.";

		public static string DuplicateCharacter(string characterName) =>
			$"{characterName} 캐릭터는 이미 추가되어 있습니다.";

		public static string ImportedAdventureCharacters(int count) =>
			$"던담에서 가져온 캐릭터 {count}개를 저장했습니다.";

		public static string DundamImportError(string message) =>
			"던담 불러오기 오류: " + message;

		public static string AddCharacterLoading(string characterName) =>
			$"{characterName} 캐릭터를 추가하는 중입니다.";

		public static string CharacterAdded(string characterName) =>
			$"{characterName} 캐릭터를 추가했습니다.";

		public static string Error(string message) =>
			"오류: " + message;

		public static string RefreshingCharacters(int count) =>
			$"저장된 캐릭터 {count}개를 갱신하는 중입니다.";

		public static string LastRefresh(DateTime refreshedAt) =>
			$"갱신 완료: {refreshedAt:HH:mm:ss}";

		public static string SettingsSaved(DateTime savedAt) =>
			$"{SettingsSavedPrefix}: {savedAt:HH:mm:ss}";

		public static string CardOrderSaved(DateTime savedAt) =>
			$"{CardOrderSavedPrefix}: {savedAt:HH:mm:ss}";

		public static string CachedCardsLoaded(int count) =>
			$"저장된 카드 {count}개를 불러왔습니다.";

		public static string CharacterRemoved(string characterName) =>
			$"{characterName} 캐릭터를 제거했습니다.";

		public static string PresetSelected(string presetName) =>
			$"{presetName} 프리셋을 열었습니다.";
	}

	private readonly record struct DropIndicatorPosition(double X, double Y, double Height);
	private readonly record struct DropTarget(int Index, DropIndicatorPosition Indicator);
	private readonly record struct ItemLayout(int Index, Rect ListBounds, Rect OverlayBounds);
	private sealed record VisualRow(List<ItemLayout> Items, double Top, double Bottom, double OverlayTop, double OverlayBottom);

	private readonly AppSettings _settings;
	private readonly DundamClient _dundam = new();
	private readonly DispatcherTimer _timer = new();
	private readonly DispatcherTimer _weeklyResetTimer = new() { Interval = TimeSpan.FromMinutes(1) };
	private readonly DispatcherTimer _weeklyResetNoticeTimer = new() { Interval = TimeSpan.FromMinutes(30) };
	private readonly DispatcherTimer _resizeHeaderTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
	private readonly DispatcherTimer _windowPlacementTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
	private readonly SettingsPersistenceService _settingsPersistence;
	private readonly PresetService _presetService;
	private readonly CharacterCardService _characterCardService;
	private readonly WeeklyResetNoticeService _weeklyResetNoticeService = new();
	private readonly Forms.NotifyIcon _trayIcon = new();
	private readonly ManualWindowDrag _windowDrag;
	private System.Windows.Point? _dragStartPoint;
	private CharacterRow? _draggingRow;
	private int? _dropIndicatorIndex;
    private List<CharacterRow>? _dragBaseRows;
    private Popup? _dragPreviewPopup;
	private Border? _dragPreviewElement;
	private System.Windows.Point _dragPreviewOffset;
	private bool _isDragCompleted;
	private bool _isOverDeleteZone;
	private bool _isDropIndicatorVisible;
	private double? _dropIndicatorLeft;
	private double? _dropIndicatorTop;
	private double? _dropIndicatorHeight;
	private bool? _themeOverrideIsLight;
	private Forms.ContextMenuStrip? _trayContextMenu;
	private Forms.ToolStripMenuItem? _systemThemeMenuItem;
	private Forms.ToolStripMenuItem? _lightThemeMenuItem;
	private Forms.ToolStripMenuItem? _darkThemeMenuItem;
	private bool _isRefreshing;
	private bool _isExitRequested;
	private bool _hasShownTrayRestoreHint;
	private bool _isRestoringWindowPlacement;
	private bool _isSummaryPanelExpanded = true;
	private bool _hasSummaryContent;
	private DateTime? _weeklyResetAt;
	private DateTime _weeklyResetDate;
	private bool _isWeeklyResetNoticeApplied;
	private bool _isWeeklyResetNoticeRefreshing;
	private bool _isWeeklyResetDebugOverride;
	private bool _isUpdatingPresetList;
	private bool _suppressNextPresetPanelClick;
	private bool _isMutatingPresets;
	private List<CharacterRow> _allCharacterRows = new();
	private readonly ObservableCollection<CharacterRow> _characterRows = new();
	private int _characterRenderGeneration;
	private int _busyOverlayDepth;
	private int _loadingOverlayGeneration;
	private double _lastCardWidth;
	private HwndSource? _windowSource;
	private bool _isNativeSizeMove;
	private bool _isCardWidthUpdateQueued;
	private double? _queuedCardLayoutWidth;
	private bool? _lastHeaderCompactMode;
	private bool? _lastCompactOptionButtons;
	private CardEntryPreset CurrentPreset => _presetService.CurrentPreset;

	public MainWindow()
	{
		InitializeComponent();
		_windowDrag = new ManualWindowDrag(this);
		_settings = AppSettings.Load();
		_settings.AutoSortByFame = false;
		_presetService = new PresetService(_settings);
		_settingsPersistence = new SettingsPersistenceService(_settings, () => CurrentPreset);
		_characterCardService = new CharacterCardService(_settings);
		if (!StartupRegistrationService.TryApply(_settings.RunAtWindowsStartup, out _))
			_settings.RunAtWindowsStartup = false;
		_resizeHeaderTimer.Tick += (_, _) =>
		{
			_resizeHeaderTimer.Stop();
			ApplyHeaderButtonLayout();
		};
		_windowPlacementTimer.Tick += (_, _) =>
		{
			_windowPlacementTimer.Stop();
			SaveWindowPlacement();
		};
		ShowInTaskbar = _settings.ShowInTaskbar;
		RestoreWindowPlacement();
		_themeOverrideIsLight = ThemeModeToOverride(_settings.ThemeMode);
		ApplyCurrentTheme();
		SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

		ConfigureTrayIcon();

		CharacterList.ItemsSource = _characterRows;
		ServerComboBox.ItemsSource = ServerOptions.All;
		ServerComboBox.SelectedValue = string.IsNullOrWhiteSpace(_settings.ServerId) ? "cain" : _settings.ServerId;
		if (ServerComboBox.SelectedValue is null)
			ServerComboBox.SelectedValue = "cain";

		_settings.Columns = ClampColumns(_settings.Columns);
		CharacterList.Tag = _settings.Columns;
		ApplyCompactMode();
		ApplyHeaderButtonLayout(force: true);
		UpdatePresetList();
		LoadCachedCharacterRows();
		UpdateWeeklyResetText();
		_weeklyResetTimer.Tick += (_, _) => UpdateWeeklyResetText();
		_weeklyResetTimer.Start();
		_weeklyResetNoticeTimer.Tick += async (_, _) => await RefreshWeeklyResetNoticeAsync();
		_weeklyResetNoticeTimer.Start();
		Loaded += async (_, _) => await RefreshWeeklyResetNoticeAsync();

		ApplyAutoRefreshInterval();
		_timer.Tick += async (_, _) => await RefreshAsync();
		_timer.Start();

		if (_settings.AutoRefreshOnStartup)
			Loaded += async (_, _) => await RefreshAsync();
	}

	private void CharacterScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
	}

	private void CharacterScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (CharacterScrollViewer.ScrollableHeight <= 0)
			return;

		var wheelSteps = e.Delta / 120.0;
		var targetOffset = Math.Clamp(
			CharacterScrollViewer.VerticalOffset - wheelSteps * CardScrollWheelStep,
			0,
			CharacterScrollViewer.ScrollableHeight);

		CharacterScrollViewer.ScrollToVerticalOffset(targetOffset);
		e.Handled = true;
	}

	private void ShowLoadingOverlay(string message)
	{
		_loadingOverlayGeneration++;
		_busyOverlayDepth++;
		LoadingOverlayText.Text = message;
		LoadingOverlay.BeginAnimation(OpacityProperty, null);
		LoadingOverlay.Visibility = Visibility.Visible;
		CharacterScrollViewer.IsEnabled = false;
		LoadingOverlay.BeginAnimation(
			OpacityProperty,
			new DoubleAnimation(0.88, TimeSpan.FromMilliseconds(140))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
				FillBehavior = FillBehavior.HoldEnd
			});

		var animation = new DoubleAnimation
		{
			From = 0,
			To = 360,
			Duration = TimeSpan.FromMilliseconds(900),
			RepeatBehavior = RepeatBehavior.Forever
		};
		LoadingSpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
	}

	private async Task HideLoadingOverlayAsync()
	{
		if (_busyOverlayDepth > 0)
			_busyOverlayDepth--;

		if (_busyOverlayDepth > 0)
			return;

		var generation = ++_loadingOverlayGeneration;
		LoadingOverlayText.Text = LogText.LoadingComplete;
		await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
		await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
		await Task.Delay(180);
		await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
		if (generation != _loadingOverlayGeneration || _busyOverlayDepth > 0)
			return;

		CharacterScrollViewer.IsEnabled = true;
		var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
			FillBehavior = FillBehavior.Stop
		};
		fadeOut.Completed += (_, _) =>
		{
			if (generation != _loadingOverlayGeneration)
				return;

			LoadingSpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);
			LoadingOverlay.Opacity = 0;
			LoadingOverlay.Visibility = Visibility.Collapsed;
		};
		LoadingOverlay.BeginAnimation(OpacityProperty, fadeOut);
	}

    private enum DWMWINDOWATTRIBUTE
    {
        DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_WINDOW_CORNER_PREFERENCE = 33
    }

    private enum ACCENT_STATE
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    }

    private enum WINDOWCOMPOSITIONATTRIB
    {
        WCA_ACCENT_POLICY = 19
    }

    private enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public ACCENT_STATE AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public WINDOWCOMPOSITIONATTRIB Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        DWMWINDOWATTRIBUTE dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WINDOWCOMPOSITIONATTRIBDATA data);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
		if (PresentationSource.FromVisual(this) is HwndSource source)
		{
			_windowSource = source;
			source.CompositionTarget.BackgroundColor = Colors.Transparent;
			source.AddHook(WindowMessageHook);
		}

        int corner = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;

        DwmSetWindowAttribute(
            hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref corner,
            sizeof(int));

        ApplyCurrentTheme(hwnd);
    }

	protected override void OnClosing(CancelEventArgs e)
	{
		if (!_isExitRequested)
		{
			_settingsPersistence.CancelPendingSave();
			SaveWindowPlacement();
			e.Cancel = true;
			Hide();
			ShowTrayRestoreHint();
			return;
		}

		_settingsPersistence.FlushPendingSave();
		PersistCurrentPresetFromRows();
		SaveWindowPlacement();
		SaveSettings();
		WaitForQueuedSettingsSave();
		base.OnClosing(e);
	}

	protected override void OnClosed(EventArgs e)
	{
		_windowSource?.RemoveHook(WindowMessageHook);
		_windowSource = null;
		_resizeHeaderTimer.Stop();
		_windowPlacementTimer.Stop();
		MarqueeTextBlock.IsAnimationSuspended = false;
		_timer.Stop();
		_weeklyResetTimer.Stop();
		_weeklyResetNoticeTimer.Stop();
		CloseTrayContextMenu();
		SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
		_trayIcon.Visible = false;
		_trayIcon.Dispose();
		base.OnClosed(e);
	}

	private IntPtr WindowMessageHook(
		IntPtr hwnd,
		int message,
		IntPtr wParam,
		IntPtr lParam,
		ref bool handled)
	{
		switch (message)
		{
			case WmEnterSizeMove:
				_isNativeSizeMove = true;
				MarqueeTextBlock.IsAnimationSuspended = true;
				break;

			case WmExitSizeMove:
				_isNativeSizeMove = false;
				MarqueeTextBlock.IsAnimationSuspended = false;
				ScheduleCardWidthUpdate(GetCardLayoutContentWidth(CharacterScrollViewer.ActualWidth));

				ScheduleHeaderLayoutUpdate();
				ScheduleWindowPlacementSave();
				break;
		}

		return IntPtr.Zero;
	}

	private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
	{
		if (e.Category != UserPreferenceCategory.General)
			return;

		Dispatcher.Invoke(() =>
		{
			ApplyCurrentTheme();
		});
	}

	private void ApplyCurrentTheme()
	{
		ApplyCurrentTheme(new WindowInteropHelper(this).Handle);
	}

	private void ApplyCurrentTheme(IntPtr hwnd)
	{
		var isLightTheme = CurrentIsLightTheme;
		ApplyTheme(isLightTheme);
		ApplyDwmTheme(hwnd, isLightTheme);
		ApplyAccentAcrylic(hwnd, isLightTheme, _settings.LowPerformanceMode);
	}

	private bool CurrentIsLightTheme => _themeOverrideIsLight ?? IsWindowsLightTheme();

	private bool ResolveThemeIsLight(string themeMode)
	{
		return ThemeModeToOverride(themeMode) ?? IsWindowsLightTheme();
	}

	private void PreviewThemeMode(string themeMode)
	{
		_themeOverrideIsLight = ThemeModeToOverride(themeMode);
		UpdateThemeMenuChecks();
		ApplyCurrentTheme();
	}

	private void PreviewLowPerformanceMode(bool lowPerformanceMode)
	{
		_settings.LowPerformanceMode = lowPerformanceMode;
		ApplyCurrentTheme();
	}

	private void PreviewCharacterImageMode(string characterImageMode)
	{
		if (CharacterList.ItemsSource is not IEnumerable<CharacterRow> rows)
			return;

		ApplyCharacterImageModeToRows(rows, characterImageMode);
	}

	private void PreviewColumns(int columns)
	{
		_settings.Columns = ClampColumns(columns);
		CharacterList.Tag = _settings.Columns;
		UpdateCurrentCardWidths(force: true);
	}

	private void PreviewApiKey(string apiKey)
	{
		_settings.ApiKey = apiKey;
	}

	private void PreviewAutoRefreshOnStartup(bool autoRefreshOnStartup)
	{
		_settings.AutoRefreshOnStartup = autoRefreshOnStartup;
	}

	private void PreviewRunAtWindowsStartup(bool runAtWindowsStartup)
	{
		if (StartupRegistrationService.TryApply(runAtWindowsStartup, out var errorMessage))
		{
			_settings.RunAtWindowsStartup = runAtWindowsStartup;
			return;
		}

		StatusText.Text = "자동 실행 설정 오류: " + errorMessage;
	}

	private void PreviewAutoRefreshInterval(int minutes)
	{
		_settings.AutoRefreshIntervalMinutes = ClampAutoRefreshInterval(minutes);
		ApplyAutoRefreshInterval();
	}

	private void PreviewShowInTaskbar(bool showInTaskbar)
	{
		_settings.ShowInTaskbar = showInTaskbar;
		ShowInTaskbar = showInTaskbar;
	}

	private void PreviewEnableUserDataCache(bool enableUserDataCache)
	{
		_settings.EnableUserDataCache = enableUserDataCache;
	}

	private void PreviewWeeklyContents(WeeklyContentSettings weeklyContents)
	{
		_settings.WeeklyContents = CloneWeeklyContentSettings(weeklyContents);
		ApplyWeeklyContentSettingsToCurrentRows();
	}

	private static WeeklyContentSettings CloneWeeklyContentSettings(WeeklyContentSettings settings) => new()
	{
		ShowWeeklyEquipmentLoot = settings.ShowWeeklyEquipmentLoot,
		ShowWeeklyOathLoot = settings.ShowWeeklyOathLoot,
		ShowWeeklyCrystalLoot = settings.ShowWeeklyCrystalLoot,
		ShowVenus = settings.ShowVenus,
		ShowApocalypse = settings.ShowApocalypse,
		ShowBakalRaid = settings.ShowBakalRaid,
		ShowNabelRaid = settings.ShowNabelRaid,
		ShowNormalNabelRaid = settings.ShowNormalNabelRaid,
		ShowHardNabelRaid = settings.ShowHardNabelRaid,
		ShowTwilightOfInae = settings.ShowTwilightOfInae,
		ShowDiregieRaid = settings.ShowDiregieRaid,
		ShowHardMistRaid = settings.ShowHardMistRaid
	};

	private void ApplyAutoRefreshInterval()
	{
		_timer.Interval = TimeSpan.FromMinutes(ClampAutoRefreshInterval(_settings.AutoRefreshIntervalMinutes));
	}

	private static int ClampColumns(int columns)
	{
		return Math.Clamp(columns, 1, 10);
	}

	private static int ClampAutoRefreshInterval(int minutes)
	{
		return Math.Clamp(minutes, 5, 180);
	}

	private void SetThemeOverride(bool? isLightTheme)
	{
		_themeOverrideIsLight = isLightTheme;
		_settings.ThemeMode = OverrideToThemeMode(isLightTheme);
		SaveSettings();
		UpdateThemeMenuChecks();
		ApplyCurrentTheme();
	}

	private void UpdateThemeMenuChecks()
	{
		if (_systemThemeMenuItem is not null)
			_systemThemeMenuItem.Checked = _themeOverrideIsLight is null;
		if (_lightThemeMenuItem is not null)
			_lightThemeMenuItem.Checked = _themeOverrideIsLight == true;
		if (_darkThemeMenuItem is not null)
			_darkThemeMenuItem.Checked = _themeOverrideIsLight == false;
	}

	private void ApplyTheme(bool isLightTheme)
	{
		var lowPerformance = _settings.LowPerformanceMode;
		SetThemeBrush("PrimaryTextBrush", isLightTheme ? MediaColor.FromRgb(0x11, 0x11, 0x11) : Colors.White);
		SetThemeBrush("SecondaryTextBrush", isLightTheme ? MediaColor.FromArgb(0xAA, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("TertiaryTextBrush", isLightTheme ? MediaColor.FromArgb(0x99, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("CompletedTextBrush", isLightTheme ? MediaColor.FromRgb(0x1F, 0x7A, 0x3A) : MediaColor.FromRgb(0x7E, 0xDC, 0x9A));
		SetThemeBrush("BlockedTextBrush", isLightTheme ? MediaColor.FromRgb(0xC8, 0x2B, 0x2B) : MediaColor.FromRgb(0xFF, 0x6B, 0x6B));
		SetThemeBrush("CardBackgroundBrush", lowPerformance
			? isLightTheme ? MediaColor.FromArgb(0xF4, 0xE9, 0xEA, 0xEC) : MediaColor.FromArgb(0xF2, 0x25, 0x27, 0x2D)
			: isLightTheme ? MediaColor.FromArgb(0x16, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("RemoveButtonBackgroundBrush", isLightTheme ? MediaColor.FromArgb(0xCC, 0xF5, 0xF5, 0xF5) : MediaColor.FromArgb(0xDD, 0x2A, 0x2D, 0x33));
		SetThemeBrush("RemoveButtonBorderBrush", isLightTheme ? MediaColor.FromArgb(0x44, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("ButtonBackgroundBrush", lowPerformance
			? isLightTheme ? MediaColor.FromArgb(0xF0, 0xEF, 0xEF, 0xEF) : MediaColor.FromArgb(0xF0, 0x30, 0x33, 0x39)
			: isLightTheme ? MediaColor.FromArgb(0x0E, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("ButtonHoverBackgroundBrush", lowPerformance
			? isLightTheme ? MediaColor.FromArgb(0xFF, 0xE6, 0xE6, 0xE6) : MediaColor.FromArgb(0xFF, 0x3A, 0x3D, 0x44)
			: isLightTheme ? MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("ButtonPressedBackgroundBrush", lowPerformance
			? isLightTheme ? MediaColor.FromArgb(0xFF, 0xDC, 0xDC, 0xDC) : MediaColor.FromArgb(0xFF, 0x44, 0x47, 0x4F)
			: isLightTheme ? MediaColor.FromArgb(0x28, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("ScrollThumbBrush", isLightTheme ? MediaColor.FromArgb(0x55, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("ScrollThumbHoverBrush", isLightTheme ? MediaColor.FromArgb(0x75, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("ScrollThumbPressedBrush", isLightTheme ? MediaColor.FromArgb(0x95, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("InputBackgroundBrush", lowPerformance
			? isLightTheme ? MediaColor.FromArgb(0xFF, 0xFA, 0xFA, 0xFA) : MediaColor.FromArgb(0xFF, 0x21, 0x23, 0x28)
			: isLightTheme ? MediaColor.FromArgb(0x38, 0xFF, 0xFF, 0xFF) : MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00));
		SetThemeBrush("InputBorderBrush", isLightTheme ? MediaColor.FromArgb(0x28, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("InputFocusedBorderBrush", isLightTheme ? MediaColor.FromArgb(0x66, 0x00, 0x00, 0x00) : MediaColor.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
		SetThemeBrush("InputSelectionBrush", isLightTheme ? MediaColor.FromArgb(0x66, 0x1E, 0x6F, 0xC8) : MediaColor.FromArgb(0x66, 0x88, 0xC7, 0xFF));
		SetThemeBrush("InputDropDownBackgroundBrush", lowPerformance
			? isLightTheme ? MediaColor.FromArgb(0xFF, 0xF8, 0xF8, 0xF8) : MediaColor.FromArgb(0xFF, 0x2A, 0x2D, 0x33)
			: isLightTheme ? MediaColor.FromArgb(0xF0, 0xF8, 0xF8, 0xF8) : MediaColor.FromArgb(0xF0, 0x2A, 0x2D, 0x33));
		ApplyLootTextTheme(isLightTheme);
	}

	private void SetThemeBrush(string key, MediaColor color)
	{
		var brush = CreateBrush(color);
		Resources[key] = brush;
		var appResources = System.Windows.Application.Current?.Resources;
		if (appResources is not null)
			appResources[key] = brush;
	}

	private static SolidColorBrush CreateBrush(MediaColor color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	private void ApplyLootTextTheme(bool isLightTheme)
	{
		SetThemeBrush("PrimevalLootTextBrush", isLightTheme
			? MediaColor.FromRgb(0x12, 0x70, 0xA6)
			: MediaColor.FromRgb(0x58, 0xC8, 0xFA));
		SetThemeBrush("EpicLootTextBrush", isLightTheme
			? MediaColor.FromRgb(0xE6, 0x66, 0x00)
			: MediaColor.FromRgb(0xFF, 0xB4, 0x00));
	}

	private void SetThemeResource(string key, object value)
	{
		Resources[key] = value;
		var appResources = System.Windows.Application.Current?.Resources;
		if (appResources is not null)
			appResources[key] = value;
	}

	private static bool IsWindowsLightTheme()
	{
		const string personalizationKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
		var value = Registry.GetValue(personalizationKey, "AppsUseLightTheme", 1);
		return value is not int intValue || intValue != 0;
	}

	private static bool? ThemeModeToOverride(string? themeMode)
	{
		return themeMode?.Trim().ToLowerInvariant() switch
		{
			"light" => true,
			"dark" => false,
			_ => null
		};
	}

	private static string OverrideToThemeMode(bool? isLightTheme)
	{
		return isLightTheme switch
		{
			true => "light",
			false => "dark",
			_ => "system"
		};
	}

	private static void ApplyDwmTheme(IntPtr hwnd, bool isLightTheme)
	{
		if (hwnd == IntPtr.Zero)
			return;

		int useDarkMode = isLightTheme ? 0 : 1;
		DwmSetWindowAttribute(
			hwnd,
			DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
			ref useDarkMode,
			sizeof(int));
	}

	private static void ApplyAccentAcrylic(IntPtr hwnd, bool isLightTheme, bool lowPerformanceMode)
	{
		if (hwnd == IntPtr.Zero)
			return;

		var accent = new ACCENT_POLICY
		{
			AccentState = lowPerformanceMode
				? ACCENT_STATE.ACCENT_ENABLE_GRADIENT
				: ACCENT_STATE.ACCENT_ENABLE_ACRYLICBLURBEHIND,
			AccentFlags = lowPerformanceMode ? 0 : 2,
			GradientColor = isLightTheme
				? ToAbgr(lowPerformanceMode ? (byte)0xFA : (byte)0x55, 0xF8, 0xF8, 0xF8)
				: ToAbgr(lowPerformanceMode ? (byte)0xFA : (byte)0x55, 0x20, 0x22, 0x28),
			AnimationId = 0
		};

		var accentSize = Marshal.SizeOf<ACCENT_POLICY>();
		var accentPtr = Marshal.AllocHGlobal(accentSize);

		try
		{
			Marshal.StructureToPtr(accent, accentPtr, false);

			var data = new WINDOWCOMPOSITIONATTRIBDATA
			{
				Attribute = WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
				Data = accentPtr,
				SizeOfData = accentSize
			};

			SetWindowCompositionAttribute(hwnd, ref data);
		}
		finally
		{
			Marshal.FreeHGlobal(accentPtr);
		}
	}

	private static int ToAbgr(byte alpha, byte red, byte green, byte blue)
	{
		return alpha << 24 | blue << 16 | green << 8 | red;
	}

	private async void AddCharacter_Click(object sender, RoutedEventArgs e)
	{
		await AddCharacterAsync();
	}

	private async Task AddCharacterAsync()
	{
		ApplyInputToSettings();
		var name = CharacterNameBox.Text.Trim();
		var serverId = GetServerId();

		if (string.IsNullOrWhiteSpace(name))
		{
			StatusText.Text = LogText.EnterCharacterName;
			return;
		}

		if (CurrentPreset.Characters.Any(x =>
				string.Equals(x.ServerId, serverId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(x.CharacterName, name, StringComparison.OrdinalIgnoreCase)))
		{
			StatusText.Text = LogText.DuplicateCharacter(name);
			return;
		}

		var saved = new SavedCharacter
		{
			ServerId = serverId,
			CharacterName = name
		};

		CurrentPreset.Characters.Add(saved);
		SaveSettings();
		CharacterNameBox.Text = "";

		await AddCharacterRowAsync(saved);
	}

	private async void CharacterNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key != Key.Enter)
			return;

		e.Handled = true;
		await AddCharacterAsync();
	}

	private async void AdventureNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key != Key.Enter)
			return;

		e.Handled = true;
		await ImportAdventureAsync();
	}

	private void HudTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key != Key.Tab)
			return;

		e.Handled = true;
		var fields = new[] { AdventureNameBox, CharacterNameBox };
		var currentIndex = Array.IndexOf(fields, sender);
		if (currentIndex < 0)
			return;

		var offset = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
		var nextIndex = (currentIndex + offset + fields.Length) % fields.Length;
		fields[nextIndex].Focus();
		fields[nextIndex].SelectAll();
	}

	private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key != Key.F5)
			return;

		e.Handled = true;
		await RefreshAsync();
	}

	private async void RemoveCharacter_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not System.Windows.Controls.Button { Tag: CharacterRow row })
			return;

		await RemoveCharacterAsync(row, GetOrderedCharacterRows());
	}

	private async void RefreshNow_Click(object sender, RoutedEventArgs e)
	{
		await RefreshAsync();
	}

	private void CompactMode_Click(object sender, RoutedEventArgs e)
	{
		_settings.IsCompactMode = !_settings.IsCompactMode;
		SaveSettings();
		ApplyCompactMode(animate: true);
	}

	private void IncompleteFilter_Click(object sender, RoutedEventArgs e)
	{
		_settings.FilterIncompleteOnly = !_settings.FilterIncompleteOnly;
		RenderCharacterRows();
		ApplyHeaderButtonLayout(force: true);
		SaveSettings();
		StatusText.Text = _settings.FilterIncompleteOnly
			? LogText.IncompleteFilterOn
			: LogText.IncompleteFilterOff;
	}

	private void FameSort_Click(object sender, RoutedEventArgs e)
	{
		_settings.AutoSortByFame = false;
		_allCharacterRows = _allCharacterRows
			.OrderByDescending(row => row.Fame)
			.ThenBy(row => row.CharacterName, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
		SyncSettingsCharactersFromRows(_allCharacterRows);
		RenderCharacterRows();
		SaveCardCache(_allCharacterRows);
		StatusText.Text = "명성순으로 정렬했습니다.";
	}

	private void ClearAllCharacters_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new ConfirmDialog(
			"전체 카드 삭제",
			"등록된 캐릭터 카드, 카드 배열, 캐시된 카드 정보를 모두 비웁니다.",
			"삭제",
			CurrentIsLightTheme,
			_settings.LowPerformanceMode)
		{
			Owner = this
		};
		if (dialog.ShowDialog() != true)
			return;

		_presetService.ClearCurrentPreset();
		_allCharacterRows.Clear();
		RenderCharacterRows();
		ClearIncompleteContentSummary();
		SaveSettings();
		StatusText.Text = LogText.AllCharactersCleared;
	}

	private void PresetPanelButton_Click(object sender, RoutedEventArgs e)
	{
		if (_suppressNextPresetPanelClick)
		{
			_suppressNextPresetPanelClick = false;
			e.Handled = true;
			return;
		}

		UpdatePresetList();
		PresetPanelPopup.IsOpen = !PresetPanelPopup.IsOpen;
		UpdatePresetPanelButtonState(PresetPanelPopup.IsOpen);
	}

	private void PresetPanelButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (!PresetPanelPopup.IsOpen)
			return;

		PresetPanelPopup.IsOpen = false;
		UpdatePresetPanelButtonState(isOpen: false);
		_suppressNextPresetPanelClick = true;
		e.Handled = true;
	}

	private void PresetPanelPopup_Opened(object sender, EventArgs e)
	{
		UpdatePresetPanelButtonState(isOpen: true);
		PresetPanelPopup.VerticalOffset = Math.Max(44, (ActualHeight - PresetPanelHost.ActualHeight) / 2);

		if (PresetPanelHost.RenderTransform is TranslateTransform transform)
		{
			transform.BeginAnimation(TranslateTransform.XProperty, null);
			transform.X = 12;
			transform.BeginAnimation(
				TranslateTransform.XProperty,
				new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
				{
					EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
				});
		}
	}

	private void PresetPanelPopup_Closed(object sender, EventArgs e)
	{
		UpdatePresetPanelButtonState(isOpen: false);
		if (PresetPanelButton.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed)
			_suppressNextPresetPanelClick = true;
	}

	private void UpdatePresetPanelButtonState(bool isOpen)
	{
		PresetPanelArrow.Data = Geometry.Parse(isOpen
			? "M 7 4 L 11 8 L 7 12"
			: "M 11 4 L 7 8 L 11 12");
	}

	private async void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_isUpdatingPresetList ||
			_isMutatingPresets ||
			PresetListBox.SelectedItem is not CardEntryPreset preset ||
			preset.Id == _settings.ActivePresetId)
		{
			return;
		}

		PersistCurrentPresetFromRows();
		_presetService.SelectPreset(preset.Id);
		var selectedPresetId = preset.Id;
		ClearIncompleteContentSummary();
		if (!await RestoreActivePresetRowsAsync(deferSummary: true, batchRender: true))
		{
			if (_settings.ActivePresetId != selectedPresetId)
				return;

			_allCharacterRows.Clear();
			await RenderCharacterRowsAsync(batch: true);
		}

		if (_settings.ActivePresetId != selectedPresetId)
			return;

		_settingsPersistence.ScheduleSave();
		StatusText.Text = LogText.PresetSelected(CurrentPreset.Name);
	}

	private async void AddPreset_Click(object sender, RoutedEventArgs e)
	{
		if (_isMutatingPresets)
			return;

		_isMutatingPresets = true;
		try
		{
			PersistCurrentPresetFromRows();
			var nextNumber = _settings.Presets.Count + 1;
			var preset = _presetService.AddPreset($"프리셋 {nextNumber}");
			_allCharacterRows.Clear();
			await RenderCharacterRowsAsync(batch: true);
			ClearIncompleteContentSummary();
			UpdatePresetList();
			_settingsPersistence.ScheduleSave();
			StatusText.Text = LogText.PresetSelected(preset.Name);
		}
		finally
		{
			_isMutatingPresets = false;
		}
	}

	private async void RemovePreset_Click(object sender, RoutedEventArgs e)
	{
		if (_isMutatingPresets)
			return;

		if (_settings.Presets.Count <= 1)
		{
			StatusText.Text = LogText.LastPresetCannotBeRemoved;
			return;
		}

		_isMutatingPresets = true;
		try
		{
			var preset = PresetListBox.SelectedItem as CardEntryPreset ?? CurrentPreset;
			if (_settings.Presets.All(item => item.Id != preset.Id))
				return;

			if (_presetService.RemovePreset(preset))
			{
				ClearIncompleteContentSummary();
				if (!await RestoreActivePresetRowsAsync(deferSummary: true, batchRender: true))
				{
					_allCharacterRows.Clear();
					await RenderCharacterRowsAsync(batch: true);
				}
			}
			UpdatePresetList();
			_settingsPersistence.ScheduleSave();
			StatusText.Text = $"{preset.Name} 프리셋을 삭제했습니다.";
		}
		finally
		{
			_isMutatingPresets = false;
		}
	}

	private void EditPreset_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not System.Windows.Controls.Button { Tag: CardEntryPreset preset })
			return;

		e.Handled = true;
		foreach (var item in _settings.Presets)
			item.IsEditing = false;

		preset.IsEditing = true;
		PresetListBox.SelectedItem = preset;
		Dispatcher.BeginInvoke(() => FocusPresetNameBox(preset), DispatcherPriority.Loaded);
	}

	private void PresetNameBox_LostFocus(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: CardEntryPreset preset })
			FinishPresetNameEdit(preset);
	}

	private void PresetNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (sender is not FrameworkElement { DataContext: CardEntryPreset preset })
			return;

		if (e.Key != Key.Enter && e.Key != Key.Escape)
			return;

		e.Handled = true;
		FinishPresetNameEdit(preset);
		Keyboard.ClearFocus();
	}

	private void FinishPresetNameEdit(CardEntryPreset preset)
	{
		preset.Name = string.IsNullOrWhiteSpace(preset.Name)
			? "프리셋"
			: preset.Name.Trim();
		preset.IsEditing = false;
		SaveSettings();
	}

	private void FocusPresetNameBox(CardEntryPreset preset)
	{
		if (!preset.IsEditing || !_settings.Presets.Contains(preset))
			return;

		PresetListBox.ScrollIntoView(preset);
		PresetListBox.UpdateLayout();

		if (PresetListBox.ItemContainerGenerator.ContainerFromItem(preset) is not DependencyObject container)
		{
			Dispatcher.BeginInvoke(() => FocusPresetNameBox(preset), DispatcherPriority.ContextIdle);
			return;
		}

		var textBox = FindDescendant<System.Windows.Controls.TextBox>(container);
		if (textBox is null)
		{
			Dispatcher.BeginInvoke(() => FocusPresetNameBox(preset), DispatcherPriority.ContextIdle);
			return;
		}

		textBox.Focus();
		Keyboard.Focus(textBox);
		textBox.SelectAll();
	}

	private static T? FindDescendant<T>(DependencyObject parent)
		where T : DependencyObject
	{
		var childCount = VisualTreeHelper.GetChildrenCount(parent);
		for (var index = 0; index < childCount; index++)
		{
			var child = VisualTreeHelper.GetChild(parent, index);
			if (child is T match)
				return match;

			var descendant = FindDescendant<T>(child);
			if (descendant is not null)
				return descendant;
		}

		return null;
	}

	private void UpdatePresetList()
	{
		_isUpdatingPresetList = true;
		try
		{
			var selectedPreset = _settings.Presets.FirstOrDefault(x => x.Id == _settings.ActivePresetId);
			PresetListBox.ItemsSource = null;
			PresetListBox.ItemsSource = _settings.Presets.ToList();
			PresetListBox.SelectedItem = selectedPreset;
		}
		finally
		{
			_isUpdatingPresetList = false;
		}
	}

	private async Task<bool> RestoreActivePresetRowsAsync(bool deferSummary = false, bool batchRender = false)
	{
		CharacterScrollViewer.BeginAnimation(OpacityProperty, null);
		IncompleteSummaryPanel.BeginAnimation(OpacityProperty, null);
		CharacterScrollViewer.Opacity = 1;
		IncompleteSummaryPanel.Opacity = 1;

		if (_presetService.TryGetRows(CurrentPreset.Id, out var collection))
		{
			_allCharacterRows = collection
				.Where(x => !x.IsDropIndicator)
				.ToList();
			ApplyCharacterImageModeToRows(_allCharacterRows);
			ApplyWeeklyContentSettingsToRows(_allCharacterRows, deferSummary);
			await RenderCharacterRowsAsync(batchRender);
			return true;
		}

		await LoadCachedCharacterRowsAsync(deferSummary, batchRender);
		return _allCharacterRows.Count > 0;
	}

	private void PersistCurrentPresetFromRows()
	{
		if (_allCharacterRows.Count == 0)
			return;

		_presetService.PersistRows(_allCharacterRows);
	}

	private void UpdateToolbarOptionButtons(bool useCompactButtons)
	{
		IncompleteFilterButton.Content = _settings.FilterIncompleteOnly
			? useCompactButtons ? "전" : "전체보기"
			: useCompactButtons ? "미" : "미완료만";
		IncompleteFilterButton.Width = useCompactButtons ? 30 : 76;
		IncompleteFilterButton.Height = useCompactButtons ? 26 : 34;
		IncompleteFilterButton.FontSize = useCompactButtons ? 12 : 13;
		IncompleteFilterButton.Padding = new Thickness(0);
		IncompleteFilterButton.Opacity = _settings.FilterIncompleteOnly ? 1.0 : 0.68;

		FameSortButton.Content = useCompactButtons ? "명" : "명성순";
		FameSortButton.Width = useCompactButtons ? 30 : 64;
		FameSortButton.Height = useCompactButtons ? 26 : 34;
		FameSortButton.FontSize = useCompactButtons ? 12 : 13;
		FameSortButton.Padding = new Thickness(0);
		FameSortButton.Opacity = 1.0;

		ClearAllButton.Content = useCompactButtons ? "삭" : "전체삭제";
		ClearAllButton.Width = useCompactButtons ? 30 : 70;
		ClearAllButton.Height = useCompactButtons ? 26 : 34;
		ClearAllButton.FontSize = useCompactButtons ? 12 : 13;
		ClearAllButton.Padding = new Thickness(0);
		ClearAllButton.Margin = new Thickness(0, 0, useCompactButtons ? 12 : 18, 0);
	}

	private void ApplyCompactMode(bool animate = false)
	{
		var isCompact = _settings.IsCompactMode;

		ApplyHudPanelCompactState(isCompact, animate);
		TitleBar.Margin = isCompact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 18);
		TitleText.FontSize = isCompact ? 15 : 24;

		CompactModeIcon.Data = Geometry.Parse(isCompact
			? "M 2 6 L 8 12 L 14 6 M 2 1 L 14 1"
			: "M 2 10 L 8 4 L 14 10 M 2 15 L 14 15");

		ApplyHeaderButtonLayout(force: true);
		UpdateCurrentCardWidths(force: true);
	}

	private void ApplyHeaderButtonLayout(bool force = false)
	{
		var isCompactMode = _settings.IsCompactMode;
		var useCompactOptionButtons = isCompactMode || ActualWidth <= CompactHeaderWidthThreshold;
		if (!force &&
			_lastHeaderCompactMode == isCompactMode &&
			_lastCompactOptionButtons == useCompactOptionButtons)
		{
			return;
		}

		_lastHeaderCompactMode = isCompactMode;
		_lastCompactOptionButtons = useCompactOptionButtons;

		RefreshNowButton.Width = isCompactMode ? 28 : 88;
		RefreshNowButton.Height = isCompactMode ? 26 : 34;
		RefreshNowButton.FontSize = isCompactMode ? 11 : 14;
		RefreshNowButton.Margin = new Thickness(0, 0, 8, 0);
		RefreshNowButton.Padding = isCompactMode ? new Thickness(0) : new Thickness(10, 4, 10, 4);
		RefreshNowText.Visibility = isCompactMode ? Visibility.Collapsed : Visibility.Visible;
		RefreshNowIcon.Visibility = isCompactMode ? Visibility.Visible : Visibility.Collapsed;

		SettingsButton.Width = isCompactMode ? 28 : 68;
		SettingsButton.Height = isCompactMode ? 26 : 34;
		SettingsButton.FontSize = isCompactMode ? 12 : 14;
		SettingsButton.Margin = new Thickness(0, 0, 8, 0);
		SettingsButton.Padding = isCompactMode ? new Thickness(0) : new Thickness(10, 4, 10, 4);
		SettingsText.Visibility = isCompactMode ? Visibility.Collapsed : Visibility.Visible;
		SettingsIcon.Visibility = isCompactMode ? Visibility.Visible : Visibility.Collapsed;

		CompactModeButton.Width = isCompactMode ? 28 : 36;
		CompactModeButton.Height = isCompactMode ? 26 : 34;
		CompactModeButton.Margin = new Thickness(0, 0, 8, 0);
		CompactModeIcon.Width = isCompactMode ? 13 : 16;
		CompactModeIcon.Height = isCompactMode ? 13 : 16;

		CloseButton.Width = isCompactMode ? 28 : 36;
		CloseButton.Height = isCompactMode ? 26 : 34;
		CloseIcon.Width = isCompactMode ? 13 : 16;
		CloseIcon.Height = isCompactMode ? 13 : 16;

		UpdateToolbarOptionButtons(useCompactOptionButtons);
	}

	private void ApplyHudPanelCompactState(bool isCompact, bool animate)
	{
		HudPanel.BeginAnimation(HeightProperty, null);
		HudPanel.BeginAnimation(OpacityProperty, null);

		if (!animate)
		{
			HudPanel.Height = isCompact ? 0 : double.NaN;
			HudPanel.Opacity = isCompact ? 0 : 1;
			HudPanel.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
			return;
		}

		var duration = TimeSpan.FromMilliseconds(170);
		var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

		if (!isCompact)
		{
			HudPanel.Visibility = Visibility.Visible;
			HudPanel.Height = double.NaN;
			HudPanel.Measure(new System.Windows.Size(
				Math.Max(1, HudPanel.ActualWidth),
				double.PositiveInfinity));
			var targetHeight = Math.Max(1, HudPanel.DesiredSize.Height);
			HudPanel.Height = 0;
			HudPanel.Opacity = 0;

			var expandAnimation = new DoubleAnimation(targetHeight, duration)
			{
				EasingFunction = easing,
				FillBehavior = FillBehavior.Stop
			};
			expandAnimation.Completed += (_, _) =>
			{
				HudPanel.Height = double.NaN;
				HudPanel.Opacity = 1;
			};

			HudPanel.BeginAnimation(HeightProperty, expandAnimation);
			HudPanel.BeginAnimation(
				OpacityProperty,
				new DoubleAnimation(1, duration)
				{
					EasingFunction = easing,
					FillBehavior = FillBehavior.Stop
				});
			return;
		}

		var startHeight = Math.Max(0, HudPanel.ActualHeight);
		HudPanel.Height = startHeight;
		HudPanel.Opacity = 1;

		var collapseAnimation = new DoubleAnimation(0, duration)
		{
			EasingFunction = easing,
			FillBehavior = FillBehavior.HoldEnd
		};
		collapseAnimation.Completed += (_, _) =>
		{
			HudPanel.Visibility = Visibility.Collapsed;
			HudPanel.Height = 0;
			HudPanel.Opacity = 0;
		};

		HudPanel.BeginAnimation(HeightProperty, collapseAnimation);
		HudPanel.BeginAnimation(
			OpacityProperty,
			new DoubleAnimation(0, duration)
			{
				EasingFunction = easing,
				FillBehavior = FillBehavior.HoldEnd
			});
	}

	private async void ImportAdventure_Click(object sender, RoutedEventArgs e)
	{
		await ImportAdventureAsync();
	}

	private async Task ImportAdventureAsync()
	{
		var adventureName = AdventureNameBox.Text.Trim();
		if (string.IsNullOrWhiteSpace(adventureName))
		{
			StatusText.Text = LogText.EnterAdventureName;
			return;
		}

		ShowLoadingOverlay(LogText.ImportAdventureLoading);
		try
		{
			StatusText.Text = LogText.SearchingAdventure;

			var characters = await _dundam.SearchAdventureCharactersAsync(adventureName);
			if (characters.Count == 0)
			{
				StatusText.Text = LogText.NoAdventureCharacters;
				return;
			}

			_presetService.ReplaceCurrentCharacters(characters);

			ApplyInputToSettings();
			SaveSettings();
			AdventureNameBox.Text = "";

			await RefreshAsync();
			StatusText.Text = LogText.ImportedAdventureCharacters(characters.Count);
		}
		catch (Exception ex)
		{
			StatusText.Text = LogText.DundamImportError(ex.Message);
		}
		finally
		{
			await HideLoadingOverlayAsync();
		}
	}

	private async void Settings_Click(object sender, RoutedEventArgs e)
	{
		var originalApiKey = _settings.ApiKey;
		var originalWeeklyContents = CloneWeeklyContentSettings(_settings.WeeklyContents);
		var originalThemeMode = _settings.ThemeMode;
		var originalLowPerformanceMode = _settings.LowPerformanceMode;
		var originalCharacterImageMode = _settings.CharacterImageMode;
		var originalColumns = _settings.Columns;
		var originalAutoRefreshOnStartup = _settings.AutoRefreshOnStartup;
		var originalRunAtWindowsStartup = _settings.RunAtWindowsStartup;
		var originalAutoRefreshInterval = _settings.AutoRefreshIntervalMinutes;
		var originalShowInTaskbar = _settings.ShowInTaskbar;
		var originalEnableUserDataCache = _settings.EnableUserDataCache;
		var settingsWindow = new SettingsWindow(
			_settings.ApiKey,
			_settings.WeeklyContents,
			_settings.ThemeMode,
			_settings.LowPerformanceMode,
			_settings.CharacterImageMode,
			_settings.Columns,
			_settings.AutoRefreshOnStartup,
			_settings.CheckForUpdatesOnStartup,
			_settings.RunAtWindowsStartup,
			_settings.AutoRefreshIntervalMinutes,
			_settings.ShowInTaskbar,
			_settings.EnableUserDataCache,
			CurrentIsLightTheme,
			PreviewThemeMode,
			PreviewLowPerformanceMode,
			PreviewCharacterImageMode,
			PreviewColumns,
			PreviewApiKey,
			PreviewAutoRefreshOnStartup,
			PreviewRunAtWindowsStartup,
			PreviewAutoRefreshInterval,
			PreviewShowInTaskbar,
			PreviewEnableUserDataCache,
			PreviewWeeklyContents,
			ResolveThemeIsLight)
		{
			Owner = this
		};

		if (settingsWindow.ShowDialog() != true)
		{
			PreviewApiKey(originalApiKey);
			PreviewWeeklyContents(originalWeeklyContents);
			PreviewThemeMode(originalThemeMode);
			PreviewLowPerformanceMode(originalLowPerformanceMode);
			PreviewCharacterImageMode(originalCharacterImageMode);
			PreviewColumns(originalColumns);
			PreviewAutoRefreshOnStartup(originalAutoRefreshOnStartup);
			PreviewRunAtWindowsStartup(originalRunAtWindowsStartup);
			PreviewAutoRefreshInterval(originalAutoRefreshInterval);
			PreviewShowInTaskbar(originalShowInTaskbar);
			PreviewEnableUserDataCache(originalEnableUserDataCache);
			return;
		}

		var shouldRefresh = !string.Equals(originalApiKey, settingsWindow.ApiKey, StringComparison.Ordinal);
		_settings.ApiKey = settingsWindow.ApiKey;
		_settings.WeeklyContents = settingsWindow.WeeklyContents;
		_settings.ThemeMode = settingsWindow.ThemeMode;
		_settings.LowPerformanceMode = settingsWindow.LowPerformanceMode;
		_settings.CharacterImageMode = CharacterRow.NormalizeImageMode(settingsWindow.CharacterImageMode);
		_settings.Columns = settingsWindow.Columns;
		_settings.AutoRefreshOnStartup = settingsWindow.AutoRefreshOnStartup;
		_settings.CheckForUpdatesOnStartup = settingsWindow.CheckForUpdatesOnStartup;
		if (StartupRegistrationService.TryApply(settingsWindow.RunAtWindowsStartup, out var startupError))
			_settings.RunAtWindowsStartup = settingsWindow.RunAtWindowsStartup;
		else
			StatusText.Text = "자동 실행 설정 오류: " + startupError;
		_settings.AutoRefreshIntervalMinutes = ClampAutoRefreshInterval(settingsWindow.AutoRefreshIntervalMinutes);
		_settings.ShowInTaskbar = settingsWindow.ShowInTaskbarSetting;
		_settings.EnableUserDataCache = settingsWindow.EnableUserDataCache;
		ShowInTaskbar = _settings.ShowInTaskbar;
		SaveSettings(allowWhenDisabled: true);
		ApplyAutoRefreshInterval();
		PreviewThemeMode(_settings.ThemeMode);
		ApplyCharacterImageModeToCurrentRows();
		PreviewColumns(_settings.Columns);

		if (shouldRefresh)
		{
			await RefreshAsync();
		}
		else
		{
			ApplyWeeklyContentSettingsToCurrentRows();
			SaveCurrentCardCache();
			StatusText.Text = LogText.SettingsSaved(DateTime.Now);
		}
	}

	private async Task AddCharacterRowAsync(SavedCharacter saved)
	{
		if (_isRefreshing)
			return;

		if (string.IsNullOrWhiteSpace(_settings.ApiKey))
		{
			StatusText.Text = LogText.EnterApiKey;
			return;
		}

		try
		{
			_isRefreshing = true;
			ShowLoadingOverlay(LogText.AddCharacterLoading(saved.CharacterName));

			var cardWidth = CalculateCardWidth();
			var row = await _characterCardService.CreateRowAsync(saved, cardWidth);
			var rows = GetOrderedCharacterRows();
			rows.RemoveAll(x =>
				string.Equals(x.ServerId, row.ServerId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(x.CharacterName, row.CharacterName, StringComparison.OrdinalIgnoreCase));
			rows.Add(row);

			ApplyWeeklyContentSettingsToRows(rows);
			SetCharacterRows(rows);
			SyncSettingsCharactersFromRows(_allCharacterRows);
			UpdateCurrentCardWidths(force: true);
			await AnimateCharacterAddedAsync(row);
			SaveCardCache(_allCharacterRows);
			StatusText.Text = LogText.CharacterAdded(row.CharacterName);
		}
		catch (Exception ex)
		{
			StatusText.Text = LogText.Error(ex.Message);
		}
		finally
		{
			await HideLoadingOverlayAsync();
			_isRefreshing = false;
		}
	}

	private async Task RefreshAsync()
	{
		if (_isRefreshing)
			return;

		try
		{
			_isRefreshing = true;
			ShowLoadingOverlay(LogText.RefreshLoading);

			if (string.IsNullOrWhiteSpace(_settings.ApiKey))
			{
				if (!HasDisplayedCharacterRows())
					SetCharacterRows(Array.Empty<CharacterRow>());
				ClearIncompleteContentSummary();
				StatusText.Text = LogText.EnterApiKey;
				return;
			}

			if (CurrentPreset.Characters.Count == 0)
			{
				SetCharacterRows(Array.Empty<CharacterRow>());
				ClearIncompleteContentSummary();
				StatusText.Text = LogText.AddCharacterPrompt;
				return;
			}

			StatusText.Text = LogText.RefreshingCharacters(CurrentPreset.Characters.Count);

			var cardWidth = CalculateCardWidth();
			var rows = await _characterCardService.CreateRowsAsync(CurrentPreset.Characters, cardWidth);

			ApplyWeeklyContentSettingsToRows(rows);

			await SetCharacterRowsAsync(rows, batchRender: rows.Count >= 20);
			SyncSettingsCharactersFromRows(_allCharacterRows);
			SaveCardCache(_allCharacterRows);
			StatusText.Text = LogText.LastRefresh(DateTime.Now);
		}
		catch (Exception ex)
		{
			StatusText.Text = LogText.Error(ex.Message);
		}
		finally
		{
			await HideLoadingOverlayAsync();
			_isRefreshing = false;
		}
	}

	private void ApplyWeeklyContentSettingsToCurrentRows()
	{
		var rows = _allCharacterRows.Count > 0
			? _allCharacterRows
			: CharacterList.ItemsSource as IEnumerable<CharacterRow>;
		if (rows is null)
			return;

		ApplyWeeklyContentSettingsToRows(rows.Where(x => !x.IsDropIndicator).ToList());
		RenderCharacterRows();
	}

	private void ApplyWeeklyContentSettingsToRows(IReadOnlyList<CharacterRow> rows, bool deferSummary = false)
	{
		var context = CreateWeeklyDisplayContext(rows);

		foreach (var row in rows)
			row.ApplyWeeklyContentSettings(_settings.WeeklyContents, context);

		if (deferSummary)
		{
			var snapshot = rows.ToList();
			Dispatcher.BeginInvoke(
				() => UpdateBottomSummaryPanel(snapshot, context),
				DispatcherPriority.Background);
			return;
		}

		UpdateBottomSummaryPanel(rows, context);
	}

	private static WeeklyDisplayContext CreateWeeklyDisplayContext(IEnumerable<CharacterRow> rows)
	{
		return new WeeklyDisplayContext
		{
			TwilightOfInaeClearedCount = CountClearedWeeklyContent(rows, WeeklyContentDefinition.TwilightOfInaeId),
			HardNabelClearedCount = CountClearedWeeklyContent(rows, WeeklyContentDefinition.HardNabelId)
		};
	}

	private static bool HasGlobalContentLimitStateChanged(
		WeeklyDisplayContext previous,
		WeeklyDisplayContext current)
	{
		return (previous.TwilightOfInaeClearedCount >= 8) != (current.TwilightOfInaeClearedCount >= 8) ||
			(previous.HardNabelClearedCount >= 4) != (current.HardNabelClearedCount >= 4);
	}

	private static int CountClearedWeeklyContent(IEnumerable<CharacterRow> rows, string contentId)
	{
		return rows.Count(row =>
			row.WeeklyStatus?.Contents.Any(content =>
				string.Equals(content.Id, contentId, StringComparison.OrdinalIgnoreCase) &&
			content.IsCleared == true) == true);
	}

	private void ClearIncompleteContentSummary()
	{
		IncompleteSummaryList.ItemsSource = Array.Empty<IncompleteContentGroup>();
		WeeklyLootSummaryList.ItemsSource = Array.Empty<WeeklyLootSummaryGroup>();
		UpdateSummaryContentAvailability(false);
	}

	private void UpdateBottomSummaryPanel(IReadOnlyList<CharacterRow> rows, WeeklyDisplayContext context)
	{
		var incompleteGroups = BuildIncompleteContentGroups(rows, context);
		var lootGroups = BuildWeeklyLootSummaryGroups(rows);
		IncompleteSummaryList.ItemsSource = incompleteGroups;
		WeeklyLootSummaryList.ItemsSource = lootGroups;
		UpdateWeeklyLootSummaryVisibility(lootGroups.Count > 0);
		UpdateSummaryContentAvailability(incompleteGroups.Count > 0 || lootGroups.Count > 0);
	}

	private void UpdateWeeklyResetText()
	{
		var now = DateTime.Now;
		var expectedResetDate = GetUpcomingThursday(now);
		var nextReset = _weeklyResetAt is { } noticeReset && noticeReset.Date == expectedResetDate
			? noticeReset
			: expectedResetDate.AddHours(6);
		if (nextReset <= now)
		{
			expectedResetDate = GetUpcomingThursday(now.AddDays(1));
			_weeklyResetAt = null;
			_isWeeklyResetNoticeApplied = false;
			_isWeeklyResetDebugOverride = false;
			_weeklyResetNoticeTimer.Start();
			nextReset = expectedResetDate.AddHours(6);
		}
		_weeklyResetDate = expectedResetDate;

		var remaining = nextReset - now;
		var remainingText = remaining.TotalMinutes < 1
			? "곧 초기화"
			: remaining.Days > 0
				? $"{remaining.Days}일 {remaining.Hours}시간 {remaining.Minutes}분 후"
				: remaining.Hours > 0
					? $"{remaining.Hours}시간 {remaining.Minutes}분 후"
					: $"{Math.Max(1, remaining.Minutes)}분 후";

		var maintenanceStatus = _isWeeklyResetNoticeApplied || _isWeeklyResetDebugOverride
			? "점검 반영"
			: "점검 미반영";
		WeeklyResetText.Text = $"주간 초기화 ({maintenanceStatus}) · {remainingText} · {nextReset:M/d(ddd) HH:mm}";
	}

	private async Task RefreshWeeklyResetNoticeAsync()
	{
		if (_isWeeklyResetNoticeApplied || _isWeeklyResetDebugOverride || _isWeeklyResetNoticeRefreshing)
			return;

		var now = DateTime.Now;
		var expectedResetDate = _weeklyResetDate;
		var noticeCheckStartsAt = expectedResetDate.AddDays(-2).AddHours(17);
		if (now < noticeCheckStartsAt)
			return;

		_isWeeklyResetNoticeRefreshing = true;
		try
		{
			var noticeReset = await _weeklyResetNoticeService.GetUpcomingWeeklyResetAsync(expectedResetDate);
			if (noticeReset is null)
				return;

			_weeklyResetAt = noticeReset;
			_isWeeklyResetNoticeApplied = true;
			_weeklyResetNoticeTimer.Stop();
			UpdateWeeklyResetText();
		}
		finally
		{
			_isWeeklyResetNoticeRefreshing = false;
		}
	}

	private async Task ApplyPreviousMaintenanceTimeAsync(int weeksAgo)
	{
		var upcomingThursday = _weeklyResetDate;
		var previousMaintenanceDate = upcomingThursday.AddDays(-7 * weeksAgo);
		var maintenanceStart = await _weeklyResetNoticeService.GetMaintenanceStartAsync(previousMaintenanceDate);
		if (maintenanceStart is null)
		{
			StatusText.Text = $"{weeksAgo}주 전 정기점검 시간을 불러오지 못했습니다.";
			return;
		}

		_weeklyResetAt = upcomingThursday.Add(maintenanceStart.Value.TimeOfDay);
		_isWeeklyResetNoticeApplied = false;
		_isWeeklyResetDebugOverride = true;
		_weeklyResetNoticeTimer.Stop();
		UpdateWeeklyResetText();
		StatusText.Text = $"디버그: {weeksAgo}주 전 점검 시작 시각 {maintenanceStart.Value:HH:mm}을 강제 적용했습니다.";
	}

	private async Task ClearMaintenanceTimeOverrideAsync()
	{
		_weeklyResetAt = null;
		_isWeeklyResetNoticeApplied = false;
		_isWeeklyResetDebugOverride = false;
		_weeklyResetNoticeTimer.Start();
		UpdateWeeklyResetText();
		StatusText.Text = "디버그 점검시간 강제 적용을 해제했습니다.";

		await RefreshWeeklyResetNoticeAsync();
	}

	private static DateTime GetUpcomingThursday(DateTime now)
	{
		var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)now.DayOfWeek + 7) % 7;
		return now.Date.AddDays(daysUntilThursday);
	}

	private void UpdateSummaryContentAvailability(bool hasContent)
	{
		_hasSummaryContent = hasContent;
		SummaryPanelHeader.Cursor = hasContent
			? System.Windows.Input.Cursors.Hand
			: System.Windows.Input.Cursors.Arrow;
		SummaryPanelToggleIcon.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
		UpdateSummaryPanelExpansion();
	}

	private void UpdateWeeklyLootSummaryVisibility(bool isVisible)
	{
		WeeklyLootSummarySection.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
		SummaryColumnGap.Width = isVisible ? new GridLength(14) : new GridLength(0);
		WeeklyLootSummaryColumn.Width = isVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
		IncompleteSummaryColumn.Width = new GridLength(1, GridUnitType.Star);
	}

	private void SummaryPanelToggle_Click(object sender, RoutedEventArgs e)
	{
		ToggleSummaryPanel();
	}

	private void SummaryPanelHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		ToggleSummaryPanel();
	}

	private void ToggleSummaryPanel()
	{
		if (!_hasSummaryContent)
			return;

		_isSummaryPanelExpanded = !_isSummaryPanelExpanded;
		UpdateSummaryPanelExpansion(animate: true);
	}

	private void UpdateSummaryPanelExpansion(bool animate = false)
	{
		var isExpanded = _hasSummaryContent && _isSummaryPanelExpanded;
		SummaryPanelToggleIcon.Data = Geometry.Parse(_isSummaryPanelExpanded
			? "M 4 7 L 8 11 L 12 7"
			: "M 4 10 L 8 6 L 12 10");

		if (!animate)
		{
			SummaryPanelBody.BeginAnimation(HeightProperty, null);
			SummaryPanelBody.BeginAnimation(OpacityProperty, null);
			SummaryPanelBody.Height = isExpanded ? double.NaN : 0;
			SummaryPanelBody.Opacity = isExpanded ? 1 : 0;
			SummaryPanelBody.Visibility = isExpanded
				? Visibility.Visible
				: Visibility.Collapsed;
			return;
		}

		AnimateSummaryPanelBody(isExpanded);
	}

	private void AnimateSummaryPanelBody(bool expand)
	{
		SummaryPanelBody.BeginAnimation(HeightProperty, null);
		SummaryPanelBody.BeginAnimation(OpacityProperty, null);

		var duration = TimeSpan.FromMilliseconds(160);
		var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

		if (expand)
		{
			SummaryPanelBody.Visibility = Visibility.Visible;
			SummaryPanelBody.Height = double.NaN;
			SummaryPanelBody.Measure(new System.Windows.Size(
				Math.Max(1, SummaryPanelBody.ActualWidth),
				double.PositiveInfinity));
			var targetHeight = Math.Max(1, SummaryPanelBody.DesiredSize.Height);
			SummaryPanelBody.Height = 0;
			SummaryPanelBody.Opacity = 0;

			var heightAnimation = new DoubleAnimation(targetHeight, duration)
			{
				EasingFunction = easing,
				FillBehavior = FillBehavior.Stop
			};
			heightAnimation.Completed += (_, _) =>
			{
				SummaryPanelBody.Height = double.NaN;
				SummaryPanelBody.Opacity = 1;
			};

			SummaryPanelBody.BeginAnimation(HeightProperty, heightAnimation);
			SummaryPanelBody.BeginAnimation(
				OpacityProperty,
				new DoubleAnimation(1, duration)
				{
					EasingFunction = easing,
					FillBehavior = FillBehavior.Stop
				});
			return;
		}

		var startHeight = Math.Max(0, SummaryPanelBody.ActualHeight);
		SummaryPanelBody.Height = startHeight;
		SummaryPanelBody.Opacity = 1;

		var collapseAnimation = new DoubleAnimation(0, duration)
		{
			EasingFunction = easing,
			FillBehavior = FillBehavior.HoldEnd
		};
		collapseAnimation.Completed += (_, _) =>
		{
			SummaryPanelBody.Visibility = Visibility.Collapsed;
			SummaryPanelBody.Height = 0;
			SummaryPanelBody.Opacity = 0;
		};

		SummaryPanelBody.BeginAnimation(HeightProperty, collapseAnimation);
		SummaryPanelBody.BeginAnimation(
			OpacityProperty,
			new DoubleAnimation(0, duration)
			{
				EasingFunction = easing,
				FillBehavior = FillBehavior.HoldEnd
			});
	}

	private List<IncompleteContentGroup> BuildIncompleteContentGroups(
		IReadOnlyList<CharacterRow> rows,
		WeeklyDisplayContext context)
	{
		var visibleRows = rows
			.Where(row => !row.IsDropIndicator && row.WeeklyStatus is not null)
			.ToList();
		if (visibleRows.Count == 0)
			return new List<IncompleteContentGroup>();

		var groups = new List<IncompleteContentGroup>();
		var enabledDefinitions = WeeklyContentDefinition.GetEnabled(_settings.WeeklyContents).ToList();
		AddLimitedIncompleteGroup(
			groups,
			enabledDefinitions,
			WeeklyContentDefinition.HardNabelId,
			"하드나벨",
			4,
			context.HardNabelClearedCount);
		AddLimitedIncompleteGroup(
			groups,
			enabledDefinitions,
			WeeklyContentDefinition.TwilightOfInaeId,
			"이내",
			8,
			context.TwilightOfInaeClearedCount);

		var addedLegionGroup = false;
		foreach (var definition in enabledDefinitions)
		{
			if (IsLimitedSummaryContent(definition))
				continue;

			var contentName = definition.Name;
			var definitions = new[] { definition };
			if (IsLegionContent(definition))
			{
				if (addedLegionGroup)
					continue;

				addedLegionGroup = true;
				contentName = "레기온";
				definitions = enabledDefinitions
					.Where(IsLegionContent)
					.ToArray();
			}

			var characters = visibleRows
				.Where(row => definitions.Any(item =>
					IsActionableIncompleteContent(row, item, context, _settings.WeeklyContents)))
				.Select(row => row.CharacterName)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (characters.Count == 0)
				continue;

			groups.Add(new IncompleteContentGroup
			{
				ContentName = contentName,
				Characters = characters
			});
		}

		return groups;
	}

	private static void AddLimitedIncompleteGroup(
		List<IncompleteContentGroup> groups,
		IEnumerable<WeeklyContentDefinition> enabledDefinitions,
		string contentId,
		string displayName,
		int limit,
		int clearedCount)
	{
		if (!enabledDefinitions.Any(definition =>
				string.Equals(definition.Id, contentId, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		var remainingCount = Math.Max(0, limit - clearedCount);
		if (remainingCount == 0)
			return;

		groups.Add(new IncompleteContentGroup
		{
			ContentName = displayName,
			TitleOverride = $"{displayName} {remainingCount}캐릭 미완료"
		});
	}

	private static bool IsLegionContent(WeeklyContentDefinition definition)
	{
		return definition.Id == WeeklyContentDefinition.VenusId ||
			definition.Id == WeeklyContentDefinition.ApocalypseId;
	}

	private static bool IsLimitedSummaryContent(WeeklyContentDefinition definition)
	{
		return definition.Id == WeeklyContentDefinition.HardNabelId ||
			definition.Id == WeeklyContentDefinition.TwilightOfInaeId;
	}

	private List<WeeklyLootSummaryGroup> BuildWeeklyLootSummaryGroups(IEnumerable<CharacterRow> rows)
	{
		var totals = new Dictionary<string, (int Primeval, int Epic)>(StringComparer.Ordinal)
			{
				["주간 중천장비"] = (0, 0),
				["주간 서약"] = (0, 0),
				["주간 결정"] = (0, 0)
			}
			.Where(item => IsWeeklyLootSummaryEnabled(item.Key))
			.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

		if (totals.Count == 0)
			return new List<WeeklyLootSummaryGroup>();

		foreach (var row in rows.Where(row => !row.IsDropIndicator))
		{
			foreach (var line in row.BaseSummary.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				if (!TryParseWeeklyLootSummaryLine(line, out var title, out var primeval, out var epic) ||
					!totals.ContainsKey(title))
				{
					continue;
				}

				var current = totals[title];
				totals[title] = (current.Primeval + primeval, current.Epic + epic);
			}
		}

		return totals
			.Select(item => new WeeklyLootSummaryGroup
			{
				Title = item.Key,
				PrimevalCount = item.Value.Primeval,
				EpicCount = item.Value.Epic
			})
			.ToList();
	}

	private bool IsWeeklyLootSummaryEnabled(string title)
	{
		return title switch
		{
			"주간 중천장비" => _settings.WeeklyContents.ShowWeeklyEquipmentLoot,
			"주간 서약" => _settings.WeeklyContents.ShowWeeklyOathLoot,
			"주간 결정" => _settings.WeeklyContents.ShowWeeklyCrystalLoot,
			_ => true
		};
	}

	private static bool TryParseWeeklyLootSummaryLine(string line, out string title, out int primeval, out int epic)
	{
		title = "";
		primeval = 0;
		epic = 0;

		var separatorIndex = line.IndexOf(':');
		if (separatorIndex < 0)
			return false;

		title = line[..separatorIndex].Trim();
		var values = line[(separatorIndex + 1)..]
			.Trim()
			.Split('/', StringSplitOptions.TrimEntries);
		return values.Length == 2 &&
			int.TryParse(values[0], out primeval) &&
			int.TryParse(values[1], out epic);
	}

	private static bool IsActionableIncompleteContent(
		CharacterRow row,
		WeeklyContentDefinition definition,
		WeeklyDisplayContext context,
		WeeklyContentSettings settings)
	{
		var status = row.WeeklyStatus;
		if (status is null)
			return false;
		if (definition.IsFameLocked(row.Fame))
			return false;

		var content = status.Contents.FirstOrDefault(x =>
			string.Equals(x.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
		if (content?.IsCleared != false)
			return false;

		var hardNabelCleared = status.Contents.Any(x =>
			string.Equals(x.Id, WeeklyContentDefinition.HardNabelId, StringComparison.OrdinalIgnoreCase) &&
			x.IsCleared == true);
		var apocalypseCleared = status.Contents.Any(x =>
			string.Equals(x.Id, WeeklyContentDefinition.ApocalypseId, StringComparison.OrdinalIgnoreCase) &&
			x.IsCleared == true);

		return !content.IsLimitedOut(
			settings,
			context,
			hardNabelCleared,
			apocalypseCleared);
	}

	private void LoadCachedCharacterRows(bool deferSummary = false)
	{
		LoadCachedCharacterRowsAsync(deferSummary, batchRender: false)
			.GetAwaiter()
			.GetResult();
	}

	private async Task LoadCachedCharacterRowsAsync(bool deferSummary = false, bool batchRender = false)
	{
		if (!_settings.EnableUserDataCache)
			return;

		if (CurrentPreset.CachedCards.Count == 0 || CurrentPreset.Characters.Count == 0)
			return;

		var cardWidth = CalculateCardWidth();
		var rows = new List<CharacterRow>();

		foreach (var saved in CurrentPreset.Characters)
		{
			var serverId = string.IsNullOrWhiteSpace(saved.ServerId) ? _settings.ServerId : saved.ServerId;
			var cache = CurrentPreset.CachedCards.FirstOrDefault(x =>
				string.Equals(x.ServerId, serverId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(x.CharacterName, saved.CharacterName, StringComparison.OrdinalIgnoreCase));

			if (cache is null)
				continue;

			rows.Add(CharacterRow.FromCache(cache, cardWidth));
		}

		if (rows.Count == 0)
			return;

		ApplyCharacterImageModeToRows(rows);
		ApplyWeeklyContentSettingsToRows(rows, deferSummary);
		await SetCharacterRowsAsync(rows, batchRender);
		StatusText.Text = LogText.CachedCardsLoaded(rows.Count);
	}

	private void ApplyCharacterImageModeToCurrentRows()
	{
		var rows = _allCharacterRows.Count > 0
			? _allCharacterRows
			: CharacterList.ItemsSource as IEnumerable<CharacterRow>;
		if (rows is null)
			return;

		ApplyCharacterImageModeToRows(rows);
		RenderCharacterRows();
	}

	private void ApplyCharacterImageModeToRows(IEnumerable<CharacterRow> rows)
	{
		ApplyCharacterImageModeToRows(rows, _settings.CharacterImageMode);
	}

	private static void ApplyCharacterImageModeToRows(IEnumerable<CharacterRow> rows, string characterImageMode)
	{
		var imageMode = CharacterRow.NormalizeImageMode(characterImageMode);
		foreach (var row in rows.Where(x => !x.IsDropIndicator))
			row.ImageMode = imageMode;
	}

	private bool HasDisplayedCharacterRows()
	{
		return _allCharacterRows.Any(x => !x.IsDropIndicator) ||
			CharacterList.ItemsSource is IEnumerable<CharacterRow> rows &&
			rows.Any(x => !x.IsDropIndicator);
	}

	private void SaveCurrentCardCache()
	{
		var rows = _allCharacterRows.Count > 0
			? _allCharacterRows
			: CharacterList.ItemsSource as IEnumerable<CharacterRow>;
		if (rows is null)
			return;

		SaveCardCache(rows);
	}

	private void SaveCardCache(IEnumerable<CharacterRow> rows)
	{
		_presetService.UpdateCachedCards(rows);
		SaveSettings();
	}

	private void SyncSettingsCharactersFromRows(IEnumerable<CharacterRow> rows)
	{
		_presetService.UpdateCharacters(rows);
	}

	private void SetCharacterRows(IEnumerable<CharacterRow> rows)
	{
		SetCharacterRowsAsync(rows, batchRender: false)
			.GetAwaiter()
			.GetResult();
	}

	private async Task SetCharacterRowsAsync(IEnumerable<CharacterRow> rows, bool batchRender = false)
	{
		_allCharacterRows = rows
			.Where(x => !x.IsDropIndicator)
			.ToList();
		SortAllCharacterRowsIfNeeded();
		_presetService.SetRows(CurrentPreset.Id, _allCharacterRows);
		await RenderCharacterRowsAsync(batchRender);
	}

	private void RenderCharacterRows()
	{
		RenderCharacterRowsAsync(batch: false)
			.GetAwaiter()
			.GetResult();
	}

	private async Task RenderCharacterRowsAsync(bool batch = false)
	{
		var renderGeneration = ++_characterRenderGeneration;
		var rows = _allCharacterRows.AsEnumerable();
		if (_settings.FilterIncompleteOnly)
			rows = rows.Where(HasActionableIncompleteContent);

		CharacterList.Margin = new Thickness(0);
		if (!ReferenceEquals(CharacterList.ItemsSource, _characterRows))
			CharacterList.ItemsSource = _characterRows;

		var visibleRows = rows.ToList();
		UpdateCurrentCardWidths(force: true, rows: visibleRows);
		await ReplaceDisplayedCharacterRowsAsync(visibleRows, batch, renderGeneration);
	}

	private async Task ReplaceDisplayedCharacterRowsAsync(IReadOnlyList<CharacterRow> rows, bool batch, int renderGeneration)
	{
		if (renderGeneration != _characterRenderGeneration)
			return;

		_characterRows.Clear();

		if (!batch || rows.Count < 20)
		{
			foreach (var row in rows)
			{
				if (renderGeneration != _characterRenderGeneration)
					return;

				_characterRows.Add(row);
			}
			return;
		}

		for (var index = 0; index < rows.Count; index++)
		{
			if (renderGeneration != _characterRenderGeneration)
				return;

			_characterRows.Add(rows[index]);

			if ((index + 1) % 10 == 0)
			{
				await Dispatcher.Yield(DispatcherPriority.Background);

				if (renderGeneration != _characterRenderGeneration)
					return;
			}
		}
	}

	private static bool HasSameCharacterSequence(IReadOnlyList<CharacterRow> left, IReadOnlyList<CharacterRow> right)
	{
		if (left.Count != right.Count)
			return false;

		for (var index = 0; index < left.Count; index++)
		{
			if (!IsSameCharacter(left[index], right[index]))
				return false;
		}

		return true;
	}

	private void SortAllCharacterRowsIfNeeded()
	{
		if (!_settings.AutoSortByFame)
			return;

		_allCharacterRows = _allCharacterRows
			.OrderByDescending(row => row.Fame)
			.ThenBy(row => row.CharacterName, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	private bool HasActionableIncompleteContent(CharacterRow row)
	{
		return row.SummaryLines.Any(line =>
			!line.IsSeparator &&
			!line.IsWeeklyLoot &&
			!string.IsNullOrWhiteSpace(line.Marker) &&
			!line.IsCleared &&
			!line.IsLimitedOut &&
			!line.IsFameLocked);
	}

	private void SaveSettings(bool allowWhenDisabled = false)
	{
		if (!_settingsPersistence.Save(allowWhenDisabled))
		{
			StatusText.Text = LogText.UserDataCacheDisabled;
		}
	}

	private void MarkUserDataDirty()
	{
		_settingsPersistence.ScheduleSave();
	}

	private System.Collections.ObjectModel.ObservableCollection<CharacterRow>? GetCharacterCollection()
	{
		if (!ReferenceEquals(CharacterList.ItemsSource, _characterRows))
			CharacterList.ItemsSource = _characterRows;

		return _characterRows;
	}

	private void WaitForQueuedSettingsSave()
	{
		try
		{
			_settingsPersistence.WaitForQueuedSave();
		}
		catch (Exception ex)
		{
			StatusText.Text = LogText.Error(ex.Message);
		}
	}

	private void ApplyInputToSettings()
	{
		_settings.ServerId = GetServerId();
		ServerComboBox.SelectedValue = _settings.ServerId;
	}

	private string GetServerId()
	{
		return ServerComboBox.SelectedValue as string ?? "cain";
	}

	private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource is DependencyObject source && IsInteractiveArea(source))
			return;

		_windowDrag.Start(e);
	}

	private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (_settings is null)
			return;

		ScheduleHeaderLayoutUpdate();
		if (!_isNativeSizeMove)
			ScheduleWindowPlacementSave();
	}

	private void Window_LocationChanged(object? sender, EventArgs e)
	{
		if (_isNativeSizeMove)
			return;

		ScheduleWindowPlacementSave();
	}

	private void RestoreWindowPlacement()
	{
		if (_settings.WindowWidth is null ||
			_settings.WindowHeight is null ||
			_settings.WindowLeft is null ||
			_settings.WindowTop is null)
		{
			return;
		}

		var width = Math.Max(MinWidth, _settings.WindowWidth.Value);
		var height = Math.Max(MinHeight, _settings.WindowHeight.Value);
		var left = _settings.WindowLeft.Value;
		var top = _settings.WindowTop.Value;

		if (!IsUsableWindowPlacement(left, top, width, height))
			return;

		_isRestoringWindowPlacement = true;
		try
		{
			Width = width;
			Height = height;
			Left = left;
			Top = top;
		}
		finally
		{
			_isRestoringWindowPlacement = false;
		}
	}

	private void SaveWindowPlacement()
	{
		if (_isRestoringWindowPlacement || _settings is null || WindowState != WindowState.Normal)
			return;

		if (double.IsNaN(Left) ||
			double.IsNaN(Top) ||
			double.IsNaN(Width) ||
			double.IsNaN(Height) ||
			Width < MinWidth ||
			Height < MinHeight)
		{
			return;
		}

		_settings.WindowLeft = Left;
		_settings.WindowTop = Top;
			_settings.WindowWidth = Width;
			_settings.WindowHeight = Height;
			if (_settings.EnableUserDataCache)
				_settingsPersistence.ScheduleSave();
	}

	private static bool IsUsableWindowPlacement(double left, double top, double width, double height)
	{
		var target = new System.Drawing.Rectangle(
			(int)Math.Round(left),
			(int)Math.Round(top),
			Math.Max(1, (int)Math.Round(width)),
			Math.Max(1, (int)Math.Round(height)));

		return Forms.Screen.AllScreens.Any(screen =>
			System.Drawing.Rectangle.Intersect(screen.WorkingArea, target).Width >= 80 &&
			System.Drawing.Rectangle.Intersect(screen.WorkingArea, target).Height >= 80);
	}

	private void CharacterScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		ScheduleCardWidthUpdate(GetCardLayoutContentWidth(e.NewSize.Width));
	}

	private void ScheduleHeaderLayoutUpdate()
	{
		_resizeHeaderTimer.Stop();
		_resizeHeaderTimer.Start();
	}

	private void ScheduleWindowPlacementSave()
	{
		_windowPlacementTimer.Stop();
		_windowPlacementTimer.Start();
	}

	private void ScheduleCardWidthUpdate(double contentWidth)
	{
		_queuedCardLayoutWidth = contentWidth;
		if (_isCardWidthUpdateQueued)
			return;

		_isCardWidthUpdateQueued = true;
		Dispatcher.BeginInvoke(() =>
		{
			_isCardWidthUpdateQueued = false;
			var queuedWidth = _queuedCardLayoutWidth;
			_queuedCardLayoutWidth = null;
			if (queuedWidth.HasValue)
				UpdateCurrentCardWidths(contentWidth: queuedWidth.Value);
		}, DispatcherPriority.Render);
	}

	private void CharacterCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_dragStartPoint = e.GetPosition(this);
	}

	private async void CharacterCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint is null)
			return;

		var position = e.GetPosition(this);
		var diff = position - _dragStartPoint.Value;
		if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
			Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}

		var row = GetCharacterRow(sender);
		if (row is null || row.IsDropIndicator)
			return;
		if (_settings.FilterIncompleteOnly)
		{
			StatusText.Text = "미완료 필터 중에는 수동 카드 이동을 사용할 수 없습니다.";
			return;
		}

		_draggingRow = row;
        _dragBaseRows = GetOrderedCharacterRows();
		_isDragCompleted = false;
		_isOverDeleteZone = false;
		HideDeleteDropZone();

        if (sender is FrameworkElement element)
			OpenDragPreview(element, e.GetPosition(element));

		row.IsDragging = true;

		try
		{
			DragDrop.DoDragDrop((DependencyObject)sender, row, System.Windows.DragDropEffects.Move);
			await CompleteDragFromCursorAsync(row);
		}
		finally
		{
			CloseDragPreview();
			HideDragOverlays();
			RestoreDragOriginalRowsIfNeeded();
			row.IsDragging = false;
			_draggingRow = null;
			_dragStartPoint = null;
            _dragBaseRows = null;
			_isDragCompleted = false;
			_isOverDeleteZone = false;
        }
	}

    private List<CharacterRow> GetDropCalculationRows()
    {
        return (_dragBaseRows ?? GetOrderedCharacterRows())
            .Where(x => !x.IsDropIndicator)
            .ToList();
    }

    private void CharacterCard_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		HandleCharacterDragOver(e);
	}

	private void CharacterList_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		HandleCharacterDragOver(e);
	}

	private void HandleCharacterDragOver(System.Windows.DragEventArgs e)
	{
		e.Effects = e.Data.GetDataPresent(typeof(CharacterRow))
			? System.Windows.DragDropEffects.Move
			: System.Windows.DragDropEffects.None;

		if (e.Effects == System.Windows.DragDropEffects.Move &&
			!_isOverDeleteZone)
		{
			ShowDropIndicator(e.GetPosition(CharacterList));
		}

		e.Handled = true;
	}

	private async void CharacterCard_Drop(object sender, System.Windows.DragEventArgs e)
	{
		await HandleCharacterDropAsync(e);
	}

	private async void CharacterList_Drop(object sender, System.Windows.DragEventArgs e)
	{
		await HandleCharacterDropAsync(e);
	}

	private async Task HandleCharacterDropAsync(System.Windows.DragEventArgs e)
	{
		if (!e.Data.GetDataPresent(typeof(CharacterRow)))
			return;

		var source = e.Data.GetData(typeof(CharacterRow)) as CharacterRow ?? _draggingRow;
		if (source is null)
			return;

		var targetIndex = GetDropTarget(GetCursorPositionInCharacterList(), GetDropCalculationRows()).Index;

		await ReorderCharacterRowsAsync(source, targetIndex);
		e.Handled = true;
	}

	private async Task CompleteDragFromCursorAsync(CharacterRow row)
	{
		if (_isDragCompleted)
			return;

		if (IsCursorInside(DeleteDropZone) && DeleteDropZone.Visibility == Visibility.Visible)
		{
			await RemoveCharacterByDropAsync(row);
			return;
		}

		if (!IsCursorInside(CharacterList))
			return;

		var targetIndex = GetDropTarget(GetCursorPositionInCharacterList(), GetDropCalculationRows()).Index;
		await ReorderCharacterRowsAsync(row, targetIndex);
	}

	private static bool IsCursorInside(FrameworkElement element)
	{
		if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
			return false;

		var cursor = Forms.Cursor.Position;
		var position = element.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
		return position.X >= 0 &&
			position.Y >= 0 &&
			position.X <= element.ActualWidth &&
			position.Y <= element.ActualHeight;
	}

	private void CharacterCard_GiveFeedback(object sender, System.Windows.GiveFeedbackEventArgs e)
	{
		UpdateDragPreviewPosition();
		UpdateDeleteDropZoneVisibility();
		UpdateDropIndicatorFromCursor();
		Mouse.SetCursor(System.Windows.Input.Cursors.Hand);
		e.UseDefaultCursors = false;
		e.Handled = true;
	}

	private void CharacterCard_QueryContinueDrag(object sender, System.Windows.QueryContinueDragEventArgs e)
	{
		if (e.Action != System.Windows.DragAction.Continue)
			CloseDragPreview();
	}

	private void DeleteDropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
	{
		HandleDeleteDropZoneDrag(e);
	}

	private void DeleteDropZone_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		HandleDeleteDropZoneDrag(e);
	}

	private void DeleteDropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
	{
		_isOverDeleteZone = false;
		HideDropIndicator();
	}

	private async void DeleteDropZone_Drop(object sender, System.Windows.DragEventArgs e)
	{
		if (!e.Data.GetDataPresent(typeof(CharacterRow)))
			return;

		var source = e.Data.GetData(typeof(CharacterRow)) as CharacterRow ?? _draggingRow;
		if (source is null)
			return;

		await RemoveCharacterByDropAsync(source);
		e.Handled = true;
	}

	private void HandleDeleteDropZoneDrag(System.Windows.DragEventArgs e)
	{
		e.Effects = e.Data.GetDataPresent(typeof(CharacterRow))
			? System.Windows.DragDropEffects.Move
			: System.Windows.DragDropEffects.None;

		_isOverDeleteZone = e.Effects == System.Windows.DragDropEffects.Move;
		if (_isOverDeleteZone)
			HideDropIndicator();

		e.Handled = true;
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Hide();
		ShowTrayRestoreHint();
	}

	private void ShowTrayRestoreHint()
	{
		if (_hasShownTrayRestoreHint || !_trayIcon.Visible)
			return;

		_hasShownTrayRestoreHint = true;
		_trayIcon.BalloonTipTitle = "DNF Weekly Widget";
		_trayIcon.BalloonTipText = "트레이 아이콘을 더블 클릭하면 위젯을 다시 열 수 있습니다.";
		_trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
		_trayIcon.ShowBalloonTip(4000);
	}

	private void ConfigureTrayIcon()
	{
		_trayIcon.Text = "DNF Weekly Widget";
		using (var iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream(
			"DNFWeeklyWidget.Assets.Icons.TrayColorIcon.ico"))
		{
			if (iconStream is not null)
			{
				using var icon = new System.Drawing.Icon(iconStream);
				_trayIcon.Icon = (System.Drawing.Icon)icon.Clone();
			}
		}
		_trayContextMenu = CreateTrayContextMenu();
		_trayIcon.ContextMenuStrip = _trayContextMenu;
		_trayIcon.Visible = true;
		_trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWidget);
	}

	private Forms.ContextMenuStrip CreateTrayContextMenu()
	{
		var menu = new Forms.ContextMenuStrip();
		menu.Opening += (_, _) => UpdateThemeMenuChecks();

		menu.Items.Add(CreateTrayMenuItem("\uC5F4\uAE30", ShowWidget));
		menu.Items.Add(CreateTrayMenuItem("\uC228\uAE30\uAE30", Hide));

		var themeMenu = new Forms.ToolStripMenuItem("\uD14C\uB9C8");
		_systemThemeMenuItem = CreateTrayThemeMenuItem("\uC708\uB3C4\uC6B0 \uD14C\uB9C8 \uB530\uB974\uAE30", () => SetThemeOverride(null));
		_lightThemeMenuItem = CreateTrayThemeMenuItem("\uB77C\uC774\uD2B8", () => SetThemeOverride(true));
		_darkThemeMenuItem = CreateTrayThemeMenuItem("\uB2E4\uD06C", () => SetThemeOverride(false));
		themeMenu.DropDownItems.Add(_systemThemeMenuItem);
		themeMenu.DropDownItems.Add(_lightThemeMenuItem);
		themeMenu.DropDownItems.Add(_darkThemeMenuItem);
		menu.Items.Add(themeMenu);

		var weeklyResetDebugMenu = new Forms.ToolStripMenuItem("점검시간 강제 적용 (디버그)");
		weeklyResetDebugMenu.DropDownItems.Add(CreateTrayAsyncMenuItem("1주 전 점검시간", () => ApplyPreviousMaintenanceTimeAsync(1)));
		weeklyResetDebugMenu.DropDownItems.Add(CreateTrayAsyncMenuItem("2주 전 점검시간", () => ApplyPreviousMaintenanceTimeAsync(2)));
		weeklyResetDebugMenu.DropDownItems.Add(CreateTrayAsyncMenuItem("3주 전 점검시간", () => ApplyPreviousMaintenanceTimeAsync(3)));
		weeklyResetDebugMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
		weeklyResetDebugMenu.DropDownItems.Add(CreateTrayAsyncMenuItem("강제 적용 해제", ClearMaintenanceTimeOverrideAsync));
		menu.Items.Add(weeklyResetDebugMenu);

		menu.Items.Add(new Forms.ToolStripSeparator());
		menu.Items.Add(CreateTrayMenuItem("\uC885\uB8CC", ExitApplication));
		return menu;
	}

	private Forms.ToolStripMenuItem CreateTrayMenuItem(string header, Action action)
	{
		var item = new Forms.ToolStripMenuItem(header);
		item.Click += (_, _) =>
		{
			CloseTrayContextMenu();
			Dispatcher.Invoke(action);
		};
		return item;
	}

	private Forms.ToolStripMenuItem CreateTrayAsyncMenuItem(string header, Func<Task> action)
	{
		var item = new Forms.ToolStripMenuItem(header);
		item.Click += async (_, _) =>
		{
			CloseTrayContextMenu();
			await action();
		};
		return item;
	}

	private Forms.ToolStripMenuItem CreateTrayThemeMenuItem(string header, Action action)
	{
		var item = CreateTrayMenuItem(header, action);
		item.CheckOnClick = false;
		return item;
	}

	private void CloseTrayContextMenu()
	{
		_trayContextMenu?.Close();
	}
	private void ShowWidget()
	{
		Show();
		Activate();
	}

	private void ExitApplication()
	{
		CloseTrayContextMenu();
		_isExitRequested = true;
		_trayIcon.Visible = false;
		Close();
	}

	private double CalculateCardWidth(double? contentWidth = null)
    {
        const double MinCardWidth = 48.0;

        var columns = Math.Max(1, _settings.Columns);

		contentWidth ??= CharacterScrollViewer.ActualWidth > 0
				? GetCardLayoutContentWidth(CharacterScrollViewer.ActualWidth)
			: CharacterScrollViewer.ViewportWidth > 0
				? Math.Max(120, CharacterScrollViewer.ViewportWidth - RootPanel.Padding.Right)
				: Math.Max(260, ActualWidth - RootPanel.Padding.Left - RootPanel.Padding.Right);

        var availableWidth = Math.Max(120, contentWidth.Value - CardLayoutSafetyWidth);
        var totalItemMargins = Math.Max(0, columns - 1) * CardGap;
        var targetWidth = Math.Floor((availableWidth - totalItemMargins) / columns);

        return Math.Max(MinCardWidth, targetWidth);
    }

	private double GetCardLayoutContentWidth(double scrollViewerWidth)
	{
		const double VerticalScrollBarReserveWidth = 12.0;

		return Math.Max(
			120,
			scrollViewerWidth -
			CharacterScrollViewer.Padding.Left -
			CharacterScrollViewer.Padding.Right -
			VerticalScrollBarReserveWidth);
	}

    private void UpdateCurrentCardWidths(bool force = false, double? contentWidth = null, IEnumerable<CharacterRow>? rows = null)
    {
        rows ??= _allCharacterRows.Count > 0
			? _allCharacterRows
			: CharacterList.ItemsSource as IEnumerable<CharacterRow>;
		if (rows is null)
			return;

        var cardWidth = CalculateCardWidth(contentWidth);

        if (!force && Math.Abs(cardWidth - _lastCardWidth) < 0.5)
            return;

        _lastCardWidth = cardWidth;

        foreach (var row in rows)
            row.CardWidth = cardWidth;

        // Binding updates card widths without forcing a full item refresh.
    }

	private async Task ReorderCharacterRowsAsync(CharacterRow source, int targetIndex)
	{
		IEnumerable<CharacterRow> baseRows = _dragBaseRows ?? GetOrderedCharacterRows();
		var orderedRows = baseRows
			.Where(x => !x.IsDropIndicator)
			.ToList();
		var sourceIndex = orderedRows.FindIndex(x => IsSameCharacter(x, source));
		if (sourceIndex < 0)
			return;

		var movingRow = orderedRows[sourceIndex];
		orderedRows.RemoveAt(sourceIndex);
		if (sourceIndex < targetIndex)
			targetIndex--;

		targetIndex = Math.Clamp(targetIndex, 0, orderedRows.Count);
		if (sourceIndex == targetIndex)
			return;
		orderedRows.Insert(targetIndex, movingRow);
		var collection = GetCharacterCollection();
		if (collection is null)
			return;

		var currentIndex = collection.ToList().FindIndex(x => IsSameCharacter(x, source));
		if (currentIndex < 0)
			return;

		_allCharacterRows = orderedRows;
		_isDragCompleted = true;
		collection.Move(currentIndex, targetIndex);
		UpdateCurrentCardWidths();
		await AnimateCharacterReorderHintAsync(movingRow);

		_presetService.PersistRows(orderedRows);
		_presetService.SetRows(CurrentPreset.Id, collection);
		MarkUserDataDirty();
		StatusText.Text = "카드 순서를 변경했습니다.";
	}

	private async Task AnimateCharacterReorderHintAsync(CharacterRow row)
	{
		await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
		if (FindCharacterCardContainer(row) is not FrameworkElement container)
			return;

		var transform = new ScaleTransform(0.985, 0.985);
		container.BeginAnimation(OpacityProperty, null);
		container.Opacity = 0.58;
		container.RenderTransform = transform;
		container.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

		var duration = TimeSpan.FromMilliseconds(120);
		var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
		var opacityAnimation = new DoubleAnimation(1, duration)
		{
			EasingFunction = easing,
			FillBehavior = FillBehavior.Stop
		};
		var scaleAnimation = new DoubleAnimation(1, duration)
		{
			EasingFunction = easing,
			FillBehavior = FillBehavior.Stop
		};

		opacityAnimation.Completed += (_, _) =>
		{
			container.Opacity = 1;
			transform.ScaleX = 1;
			transform.ScaleY = 1;
			container.RenderTransform = Transform.Identity;
		};

		container.BeginAnimation(OpacityProperty, opacityAnimation);
		transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
		transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
	}

	private async Task RemoveCharacterByDropAsync(CharacterRow row)
	{
		var rows = _dragBaseRows ?? GetOrderedCharacterRows();
		if (!rows.Any(x => IsSameCharacter(x, row)))
			return;

		_isDragCompleted = true;
		await RemoveCharacterAsync(row, rows);
	}

	private async Task RemoveCharacterAsync(CharacterRow row, IReadOnlyList<CharacterRow> rows)
	{
		var orderedRows = rows
			.Where(x => !IsSameCharacter(x, row))
			.ToList();

		if (rows.Count == orderedRows.Count)
			return;

		var previousContext = CreateWeeklyDisplayContext(rows);
		var currentContext = CreateWeeklyDisplayContext(orderedRows);
		var globalLimitStateChanged = HasGlobalContentLimitStateChanged(previousContext, currentContext);

		await AnimateCharacterRemovalAsync(row);

		_allCharacterRows = orderedRows;
		if (globalLimitStateChanged)
		{
			ApplyWeeklyContentSettingsToRows(orderedRows);
			_presetService.PersistRows(orderedRows);
			if (_settings.FilterIncompleteOnly)
			{
				await RenderCharacterRowsAsync();
			}
			else
			{
				RemoveCharacterFromDisplayedCollection(row);
			}
		}
		else
		{
			_presetService.PersistRows(orderedRows);
			RemoveCharacterFromDisplayedCollection(row);
			UpdateBottomSummaryPanel(orderedRows, currentContext);
		}
		MarkUserDataDirty();

		_presetService.SetRows(CurrentPreset.Id, _allCharacterRows);
		UpdateCurrentCardWidths();
		StatusText.Text = LogText.CharacterRemoved(row.CharacterName);
	}

	private void RemoveCharacterFromDisplayedCollection(CharacterRow row)
	{
		if (GetCharacterCollection() is not { } collection)
			return;

		var index = collection.ToList().FindIndex(item => IsSameCharacter(item, row));
		if (index >= 0)
			collection.RemoveAt(index);
	}

	private Task AnimateCharacterRemovalAsync(CharacterRow row)
	{
		if (FindCharacterCardContainer(row) is not FrameworkElement container)
			return Task.CompletedTask;

		var transform = new ScaleTransform(1, 1);
		container.RenderTransform = transform;
		container.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

		var completion = new TaskCompletionSource();
		var duration = TimeSpan.FromMilliseconds(140);
		var opacityAnimation = new DoubleAnimation(0, duration)
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
			FillBehavior = FillBehavior.HoldEnd
		};
		var scaleAnimation = new DoubleAnimation(0.96, duration)
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
			FillBehavior = FillBehavior.HoldEnd
		};
		opacityAnimation.Completed += (_, _) => completion.TrySetResult();

		container.BeginAnimation(OpacityProperty, opacityAnimation);
		transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
		transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

		return completion.Task;
	}

	private Task AnimateCharacterAddedAsync(CharacterRow row)
	{
		CharacterList.UpdateLayout();
		if (FindCharacterCardContainer(row) is not FrameworkElement container)
			return Task.CompletedTask;

		var transform = new ScaleTransform(0.96, 0.96);
		container.Opacity = 0;
		container.RenderTransform = transform;
		container.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);

		var completion = new TaskCompletionSource();
		var duration = TimeSpan.FromMilliseconds(160);
		var opacityAnimation = new DoubleAnimation(1, duration)
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.Stop
		};
		var scaleAnimation = new DoubleAnimation(1, duration)
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.Stop
		};
		opacityAnimation.Completed += (_, _) =>
		{
			container.Opacity = 1;
			transform.ScaleX = 1;
			transform.ScaleY = 1;
			completion.TrySetResult();
		};

		container.BeginAnimation(OpacityProperty, opacityAnimation);
		transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
		transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

		return completion.Task;
	}

	private void RestoreDragOriginalRowsIfNeeded()
	{
		if (_isDragCompleted || _dragBaseRows is null)
			return;

		var currentRows = GetDisplayedCharacterRows();
		if (AreSameCharacterOrder(currentRows, _dragBaseRows))
			return;

		SetCharacterRows(_dragBaseRows);
	}

	private List<CharacterRow> GetDisplayedCharacterRows()
	{
		var rows = _allCharacterRows.Count > 0
			? _allCharacterRows
			: CharacterList.ItemsSource as IEnumerable<CharacterRow>;
		if (rows is null)
			return new List<CharacterRow>();

		return rows
			.Where(x => !x.IsDropIndicator)
			.ToList();
	}

	private static bool AreSameCharacterOrder(
		IReadOnlyList<CharacterRow> currentRows,
		IReadOnlyList<CharacterRow> baseRows)
	{
		if (currentRows.Count != baseRows.Count)
			return false;

		for (var index = 0; index < currentRows.Count; index++)
		{
			if (!IsSameCharacter(currentRows[index], baseRows[index]))
				return false;
		}

		return true;
	}

    private void ShowDropIndicator(System.Windows.Point position)
    {
        var orderedRows = GetDropCalculationRows();

        var target = GetDropTarget(position, orderedRows);
        _dropIndicatorIndex = target.Index;
		UpdateDropIndicatorPosition(target);
    }

	private void UpdateDropIndicatorFromCursor()
	{
		if (_draggingRow is null ||
			_isOverDeleteZone)
		{
			return;
		}

		ShowDropIndicator(GetCursorPositionInCharacterList());
	}

	private System.Windows.Point GetCursorPositionInCharacterList()
	{
		var cursor = Forms.Cursor.Position;
		return CharacterList.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
	}

    private List<CharacterRow> GetOrderedCharacterRows()
	{
		var rows = _allCharacterRows.Count > 0
			? _allCharacterRows
			: CharacterList.ItemsSource as IEnumerable<CharacterRow>;
		if (rows is null)
			return new List<CharacterRow>();

		return rows
			.Where(x => !x.IsDropIndicator)
			.ToList();
	}

	private DropTarget GetDropTarget(System.Windows.Point position, IReadOnlyList<CharacterRow> rows)
	{
		var visualRows = GetVisualRows(rows);
		if (visualRows.Count == 0)
		{
			var height = GetDraggingCardHeight();
			return new DropTarget(0, new DropIndicatorPosition(0, 0, height));
		}

		var row = GetNearestVisualRow(position, visualRows);
		var items = row.Items.OrderBy(x => x.ListBounds.Left).ToList();

		for (var i = 0; i < items.Count; i++)
		{
			var item = items[i];
			if (position.X < item.ListBounds.Left + item.ListBounds.Width / 2)
			{
				var indicatorX = i == 0
					? item.OverlayBounds.Left + DropInsertIndicator.Width / 2
					: item.OverlayBounds.Left - CardGap / 2;

				return new DropTarget(
					item.Index,
					new DropIndicatorPosition(
						indicatorX,
						row.OverlayTop,
						Math.Max(48, row.OverlayBottom - row.OverlayTop)));
			}
		}

		var last = items[^1];
		return new DropTarget(
			last.Index + 1,
			new DropIndicatorPosition(
				last.OverlayBounds.Right - DropInsertIndicator.Width / 2,
				row.OverlayTop,
				Math.Max(48, row.OverlayBottom - row.OverlayTop)));
	}

	private FrameworkElement? FindCharacterCardContainer(CharacterRow row)
	{
		return CharacterList.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement;
	}

	private List<VisualRow> GetVisualRows(IReadOnlyList<CharacterRow> rows)
	{
		var layouts = new List<ItemLayout>();

		for (var index = 0; index < rows.Count; index++)
		{
			if (FindCharacterCardContainer(rows[index]) is not FrameworkElement container ||
				container.ActualWidth <= 0 ||
				container.ActualHeight <= 0)
			{
				continue;
			}

			var screenPoint = container.PointToScreen(new System.Windows.Point(0, 0));
			var listTopLeft = CharacterList.PointFromScreen(screenPoint);
			var overlayTopLeft = DragOverlay.PointFromScreen(screenPoint);
			var size = new System.Windows.Size(container.ActualWidth, container.ActualHeight);

			layouts.Add(new ItemLayout(
				index,
				new Rect(listTopLeft, size),
				new Rect(overlayTopLeft, size)));
		}

		var visualRows = new List<VisualRow>();
		foreach (var items in GroupLayoutsByRow(layouts))
		{
			visualRows.Add(new VisualRow(
				items,
				items.Min(x => x.ListBounds.Top),
				items.Max(x => x.ListBounds.Bottom),
				items.Min(x => x.OverlayBounds.Top),
				items.Max(x => x.OverlayBounds.Bottom)));
		}

		return visualRows
			.OrderBy(x => x.Top)
			.ToList();
	}

	private static List<List<ItemLayout>> GroupLayoutsByRow(IEnumerable<ItemLayout> layouts)
	{
		const double TopTolerance = 10.0;
		var rows = new List<List<ItemLayout>>();

		foreach (var layout in layouts.OrderBy(x => x.ListBounds.Top).ThenBy(x => x.ListBounds.Left))
		{
			var row = rows.FirstOrDefault(items =>
				Math.Abs(items[0].ListBounds.Top - layout.ListBounds.Top) <= TopTolerance);

			if (row is null)
			{
				rows.Add([layout]);
			}
			else
			{
				row.Add(layout);
			}
		}

		foreach (var row in rows)
			row.Sort((left, right) => left.ListBounds.Left.CompareTo(right.ListBounds.Left));

		return rows;
	}

	private static VisualRow GetNearestVisualRow(System.Windows.Point position, IReadOnlyList<VisualRow> rows)
	{
		if (position.Y <= rows[0].Top)
			return rows[0];

		if (position.Y >= rows[^1].Bottom)
			return rows[^1];

		return rows
			.OrderBy(row =>
			{
				if (position.Y >= row.Top && position.Y <= row.Bottom)
					return 0;

				return Math.Min(Math.Abs(position.Y - row.Top), Math.Abs(position.Y - row.Bottom));
			})
			.First();
	}

	private void ClearDropIndicator()
	{
		_dropIndicatorIndex = null;
		_dropIndicatorLeft = null;
		_dropIndicatorTop = null;
		_dropIndicatorHeight = null;
		HideDropIndicator();
	}

	private void UpdateDropIndicatorPosition(DropTarget target)
	{
		if (_draggingRow is null)
		{
			HideDropIndicator();
			return;
		}

		var clipOrigin = UpdateDropIndicatorClip();
		var left = target.Indicator.X - clipOrigin.X - DropInsertIndicator.Width / 2;
		var top = target.Indicator.Y - clipOrigin.Y;
		var height = target.Indicator.Height;
		var positionChanged = IsDropIndicatorPositionChanged(left, top, height);

		if (_isDropIndicatorVisible && positionChanged && DropInsertIndicator.Opacity > 0)
		{
			CreateDropIndicatorFadeOutCopy(
				Canvas.GetLeft(DropInsertIndicator),
				Canvas.GetTop(DropInsertIndicator),
				DropInsertIndicator.Height,
				DropInsertIndicator.Opacity);
			DropInsertIndicator.BeginAnimation(OpacityProperty, null);
			DropInsertIndicator.Opacity = 0;
		}

		Canvas.SetLeft(DropInsertIndicator, left);
		Canvas.SetTop(DropInsertIndicator, top);
		DropInsertIndicator.Height = height;
		_dropIndicatorLeft = left;
		_dropIndicatorTop = top;
		_dropIndicatorHeight = height;
		ShowDropIndicatorHost(positionChanged);
	}

	private bool IsDropIndicatorPositionChanged(double left, double top, double height)
	{
		const double PositionTolerance = 0.5;

		return _dropIndicatorLeft is null ||
			_dropIndicatorTop is null ||
			_dropIndicatorHeight is null ||
			Math.Abs(_dropIndicatorLeft.Value - left) > PositionTolerance ||
			Math.Abs(_dropIndicatorTop.Value - top) > PositionTolerance ||
			Math.Abs(_dropIndicatorHeight.Value - height) > PositionTolerance;
	}

	private void CreateDropIndicatorFadeOutCopy(double left, double top, double height, double opacity)
	{
		if (double.IsNaN(left) || double.IsNaN(top) || height <= 0 || opacity <= 0)
			return;

		var fadingIndicator = new Border
		{
			Width = DropInsertIndicator.Width,
			Height = height,
			Opacity = opacity,
			Background = DropInsertIndicator.Background,
			CornerRadius = DropInsertIndicator.CornerRadius,
			IsHitTestVisible = false
		};

		Canvas.SetLeft(fadingIndicator, left);
		Canvas.SetTop(fadingIndicator, top);
		System.Windows.Controls.Panel.SetZIndex(fadingIndicator, 0);
		System.Windows.Controls.Panel.SetZIndex(DropInsertIndicator, 1);
		DropIndicatorClipHost.Children.Add(fadingIndicator);

		var animation = new DoubleAnimation
		{
			From = opacity,
			To = 0,
			Duration = TimeSpan.FromMilliseconds(DropIndicatorFadeMilliseconds),
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.Stop
		};
		animation.Completed += (_, _) => DropIndicatorClipHost.Children.Remove(fadingIndicator);
		fadingIndicator.BeginAnimation(OpacityProperty, animation);
	}

	private System.Windows.Point UpdateDropIndicatorClip()
	{
		var topLeft = DragOverlay.PointFromScreen(CharacterScrollViewer.PointToScreen(new System.Windows.Point(0, 0)));
		var width = Math.Max(0, CharacterScrollViewer.ActualWidth);
		var height = Math.Max(0, CharacterScrollViewer.ActualHeight);

		Canvas.SetLeft(DropIndicatorClipHost, topLeft.X);
		Canvas.SetTop(DropIndicatorClipHost, topLeft.Y);
		DropIndicatorClipHost.Width = width;
		DropIndicatorClipHost.Height = height;
		DropIndicatorClipHost.Clip = new RectangleGeometry(new Rect(0, 0, width, height));

		return topLeft;
	}

	private double GetDraggingCardHeight()
	{
		if (_draggingRow is not null &&
			FindCharacterCardContainer(_draggingRow) is FrameworkElement container &&
			container.ActualHeight > 0)
		{
			return container.ActualHeight;
		}

		return 120;
	}

	private void HideDropIndicator()
	{
		if (!_isDropIndicatorVisible && DropIndicatorClipHost.Visibility != Visibility.Visible)
			return;

		_isDropIndicatorVisible = false;
		_dropIndicatorLeft = null;
		_dropIndicatorTop = null;
		_dropIndicatorHeight = null;
		DropInsertIndicator.BeginAnimation(OpacityProperty, null);

		var animation = new DoubleAnimation
		{
			From = DropInsertIndicator.Opacity,
			To = 0,
			Duration = TimeSpan.FromMilliseconds(DropIndicatorFadeMilliseconds),
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.Stop
		};

		animation.Completed += (_, _) =>
		{
			if (_isDropIndicatorVisible)
				return;

			DropInsertIndicator.Opacity = 0;
			DropIndicatorClipHost.Visibility = Visibility.Collapsed;
			DropIndicatorClipHost.Children
				.OfType<Border>()
				.Where(indicator => !ReferenceEquals(indicator, DropInsertIndicator))
				.ToList()
				.ForEach(indicator => DropIndicatorClipHost.Children.Remove(indicator));
		};

		DropInsertIndicator.BeginAnimation(OpacityProperty, animation);
	}

	private void ShowDropIndicatorHost(bool restartFade)
	{
		if (_isDropIndicatorVisible && DropIndicatorClipHost.Visibility == Visibility.Visible && !restartFade)
			return;

		_isDropIndicatorVisible = true;
		DropIndicatorClipHost.Visibility = Visibility.Visible;
		DropInsertIndicator.BeginAnimation(OpacityProperty, null);

		var animation = new DoubleAnimation
		{
			From = DropInsertIndicator.Opacity,
			To = 1,
			Duration = TimeSpan.FromMilliseconds(DropIndicatorFadeMilliseconds),
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.Stop
		};

		animation.Completed += (_, _) =>
		{
			if (_isDropIndicatorVisible)
				DropInsertIndicator.Opacity = 1;
		};

		DropInsertIndicator.BeginAnimation(OpacityProperty, animation);
	}

	private void ShowDeleteDropZone(double opacity)
	{
		DeleteDropZone.Opacity = opacity;
		DeleteDropZone.Visibility = Visibility.Visible;
	}

	private void HideDeleteDropZone()
	{
		DeleteDropZone.Opacity = 0;
		DeleteDropZone.Visibility = Visibility.Collapsed;
	}

	private void UpdateDeleteDropZoneVisibility()
	{
		if (_draggingRow is null)
		{
			HideDeleteDropZone();
			return;
		}

		var cursor = Forms.Cursor.Position;
		var cursorInWindow = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
		var revealStart = ActualHeight - DeleteDropZoneRevealDistance;
		var revealProgress = Math.Clamp(
			(cursorInWindow.Y - revealStart) / DeleteDropZoneRevealDistance,
			0,
			1);

		if (revealProgress > 0)
		{
			ShowDeleteDropZone(Math.Pow(revealProgress, 1.2));
		}
		else
		{
			_isOverDeleteZone = false;
			HideDeleteDropZone();
		}
	}

	private void HideDragOverlays()
	{
		ClearDropIndicator();
		HideDeleteDropZone();
	}

	private void OpenDragPreview(FrameworkElement sourceElement, System.Windows.Point offset)
	{
		CloseDragPreview();

		var width = Math.Max(1, sourceElement.ActualWidth);
		var height = Math.Max(1, sourceElement.ActualHeight);
		var bitmap = new RenderTargetBitmap(
			(int)Math.Ceiling(width),
			(int)Math.Ceiling(height),
			96,
			96,
			PixelFormats.Pbgra32);
		bitmap.Render(sourceElement);

		_dragPreviewOffset = offset;
		_dragPreviewElement = new Border
		{
			Width = width,
			Height = height,
			Opacity = 0.78,
			CornerRadius = new CornerRadius(14),
			Background = TryFindResource("CardBackgroundBrush") as System.Windows.Media.Brush,
			Clip = new RectangleGeometry(new Rect(0, 0, width, height), 14, 14),
			RenderTransform = new ScaleTransform(0.98, 0.98),
			RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
			IsHitTestVisible = false,
			Child = new System.Windows.Controls.Image
			{
				Source = bitmap,
				Stretch = Stretch.Fill
			}
		};

		DragOverlay.Children.Add(_dragPreviewElement);
		UpdateDragPreviewPosition();
	}

	private void UpdateDragPreviewPosition()
	{
		if (_dragPreviewElement is null)
			return;

		var cursor = Forms.Cursor.Position;
		var position = DragOverlay.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));

		Canvas.SetLeft(_dragPreviewElement, position.X - _dragPreviewOffset.X);
		Canvas.SetTop(_dragPreviewElement, position.Y - _dragPreviewOffset.Y);
	}

	private void CloseDragPreview()
	{
		if (_dragPreviewElement is not null)
		{
			DragOverlay.Children.Remove(_dragPreviewElement);
			_dragPreviewElement = null;
		}

		if (_dragPreviewPopup is not null)
		{
			_dragPreviewPopup.IsOpen = false;
			_dragPreviewPopup.Child = null;
			_dragPreviewPopup = null;
		}
	}

	private static CharacterRow? GetCharacterRow(object sender)
	{
		return sender switch
		{
			FrameworkElement { DataContext: CharacterRow row } => row,
			DependencyObject dependencyObject => FindAncestor<FrameworkElement>(dependencyObject)?.DataContext as CharacterRow,
			_ => null
		};
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
		if (current is Visual or System.Windows.Media.Media3D.Visual3D)
			return VisualTreeHelper.GetParent(current);

		if (current is ContentElement contentElement)
			return ContentOperations.GetParent(contentElement) ??
				(contentElement as FrameworkContentElement)?.Parent;

		return null;
	}

	private static bool IsSameCharacter(CharacterRow left, CharacterRow right)
	{
		return string.Equals(left.ServerId, right.ServerId, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(left.CharacterName, right.CharacterName, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsInteractiveArea(DependencyObject source)
	{
		return FindAncestor<System.Windows.Controls.Button>(source) is not null ||
			FindAncestor<System.Windows.Controls.TextBox>(source) is not null ||
			FindAncestor<System.Windows.Controls.ComboBox>(source) is not null ||
			FindAncestor<System.Windows.Controls.ScrollViewer>(source) is not null ||
			FindAncestor<System.Windows.Controls.ItemsControl>(source) is not null;
	}
}
