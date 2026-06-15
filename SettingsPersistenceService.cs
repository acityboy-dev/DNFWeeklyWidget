using System.Windows.Threading;

namespace DNFWeeklyWidget;

internal sealed class SettingsPersistenceService
{
	private readonly AppSettings _settings;
	private readonly Func<CardEntryPreset> _currentPreset;
	private readonly DispatcherTimer _saveTimer;
	private readonly object _saveQueueLock = new();
	private Task _saveQueue = Task.CompletedTask;
	private bool _hasPendingSave;

	public SettingsPersistenceService(AppSettings settings, Func<CardEntryPreset> currentPreset)
	{
		_settings = settings;
		_currentPreset = currentPreset;
		_saveTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(450)
		};
		_saveTimer.Tick += (_, _) => FlushPendingSave();
	}

	public bool Save(bool allowWhenDisabled = false)
	{
		if (!_settings.EnableUserDataCache && !allowWhenDisabled)
			return false;

		EnqueueSave(CreateSnapshot());
		return true;
	}

	public void ScheduleSave()
	{
		if (!_settings.EnableUserDataCache)
			return;

		_hasPendingSave = true;
		_saveTimer.Stop();
		_saveTimer.Start();
	}

	public void FlushPendingSave()
	{
		_saveTimer.Stop();
		if (!_hasPendingSave)
			return;

		_hasPendingSave = false;
		if (!_settings.EnableUserDataCache)
			return;

		EnqueueSave(CreateSnapshot());
	}

	public void CancelPendingSave()
	{
		_saveTimer.Stop();
		_hasPendingSave = false;
	}

	public void WaitForQueuedSave()
	{
		Task saveQueue;
		lock (_saveQueueLock)
			saveQueue = _saveQueue;

		saveQueue.GetAwaiter().GetResult();
	}

	private void EnqueueSave(AppSettings settingsSnapshot)
	{
		lock (_saveQueueLock)
		{
			_saveQueue = _saveQueue.ContinueWith(
				previous =>
				{
					_ = previous.Exception;
					settingsSnapshot.Save();
				},
				CancellationToken.None,
				TaskContinuationOptions.None,
				TaskScheduler.Default);
		}
	}

	private AppSettings CreateSnapshot()
	{
		var currentPreset = _currentPreset();
		return new AppSettings
		{
			ApiKey = _settings.ApiKey,
			ServerId = _settings.ServerId,
			ThemeMode = _settings.ThemeMode,
			CharacterImageMode = _settings.CharacterImageMode,
			IsCompactMode = _settings.IsCompactMode,
			FilterIncompleteOnly = _settings.FilterIncompleteOnly,
			AutoSortByFame = _settings.AutoSortByFame,
			LowPerformanceMode = _settings.LowPerformanceMode,
			AutoRefreshOnStartup = _settings.AutoRefreshOnStartup,
			RunAtWindowsStartup = _settings.RunAtWindowsStartup,
			AutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes,
			ShowInTaskbar = _settings.ShowInTaskbar,
			EnableUserDataCache = _settings.EnableUserDataCache,
			Columns = _settings.Columns,
			WindowLeft = _settings.WindowLeft,
			WindowTop = _settings.WindowTop,
			WindowWidth = _settings.WindowWidth,
			WindowHeight = _settings.WindowHeight,
			ActivePresetId = _settings.ActivePresetId,
			Characters = CloneCharacters(currentPreset.Characters),
			CachedCards = CloneCachedCards(currentPreset.CachedCards),
			Presets = _settings.Presets
				.Select(preset => new CardEntryPreset
				{
					Id = preset.Id,
					Name = preset.Name,
					Characters = CloneCharacters(preset.Characters),
					CachedCards = CloneCachedCards(preset.CachedCards)
				})
				.ToList(),
			WeeklyContents = CloneWeeklyContents(_settings.WeeklyContents)
		};
	}

	private static List<SavedCharacter> CloneCharacters(IEnumerable<SavedCharacter> characters)
	{
		return characters
			.Select(character => new SavedCharacter
			{
				ServerId = character.ServerId,
				CharacterName = character.CharacterName
			})
			.ToList();
	}

	private static List<CachedCharacterCard> CloneCachedCards(IEnumerable<CachedCharacterCard> cards)
	{
		return cards
			.Select(card => new CachedCharacterCard
			{
				ServerId = card.ServerId,
				ServerName = card.ServerName,
				CharacterName = card.CharacterName,
				JobName = card.JobName,
				Fame = card.Fame,
				JobSummary = card.JobSummary,
				FameSummary = card.FameSummary,
				MetaSummary = card.MetaSummary,
				BaseSummary = card.BaseSummary,
				Summary = card.Summary,
				WeeklyStatus = CloneWeeklyStatus(card.WeeklyStatus),
				SummaryLines = card.SummaryLines.Select(CloneCachedSummaryLine).ToList(),
				ImagePath = card.ImagePath,
				CompactImageOffsetX = card.CompactImageOffsetX,
				CompactImageOffsetY = card.CompactImageOffsetY
			})
			.ToList();
	}

	private static CachedSummaryLine CloneCachedSummaryLine(CachedSummaryLine line)
	{
		return new CachedSummaryLine
		{
			Text = line.Text,
			Marker = line.Marker,
			Body = line.Body,
			DetailedBody = line.DetailedBody,
			IsWeeklyLoot = line.IsWeeklyLoot,
			LootTitle = line.LootTitle,
			PrimevalCount = line.PrimevalCount,
			EpicCount = line.EpicCount,
			IsCleared = line.IsCleared,
			IsLimitedOut = line.IsLimitedOut,
			IsFameLocked = line.IsFameLocked,
			IsSeparator = line.IsSeparator
		};
	}

	private static CharacterWeeklyStatus? CloneWeeklyStatus(CharacterWeeklyStatus? status)
	{
		if (status is null)
			return null;

		return new CharacterWeeklyStatus
		{
			IsAvailable = status.IsAvailable,
			Contents = status.Contents
				.Select(content => new WeeklyContentResult
				{
					Id = content.Id,
					Name = content.Name,
					IsCleared = content.IsCleared
				})
				.ToList()
		};
	}

	private static WeeklyContentSettings CloneWeeklyContents(WeeklyContentSettings settings)
	{
		return new WeeklyContentSettings
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
	}
}
