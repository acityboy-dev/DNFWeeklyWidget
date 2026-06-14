using System.Collections.ObjectModel;

namespace DNFWeeklyWidget;

internal sealed class PresetService
{
	private readonly AppSettings _settings;
	private readonly Dictionary<string, ObservableCollection<CharacterRow>> _rowCollections = new();

	public PresetService(AppSettings settings)
	{
		_settings = settings;
		_settings.EnsurePresets();
	}

	public CardEntryPreset CurrentPreset => _settings.ActivePreset;

	public void SelectPreset(string presetId)
	{
		_settings.ActivePresetId = presetId;
		_settings.EnsurePresets();
	}

	public CardEntryPreset AddPreset(string name)
	{
		var preset = new CardEntryPreset
		{
			Id = Guid.NewGuid().ToString("N"),
			Name = name
		};

		_settings.Presets.Add(preset);
		SelectPreset(preset.Id);
		_rowCollections[preset.Id] = new ObservableCollection<CharacterRow>();
		return preset;
	}

	public bool RemovePreset(CardEntryPreset preset)
	{
		var removeIndex = _settings.Presets.FindIndex(item => item.Id == preset.Id);
		if (removeIndex < 0)
			return false;

		var removedActivePreset = _settings.ActivePresetId == preset.Id;
		_settings.Presets.RemoveAt(removeIndex);
		_rowCollections.Remove(preset.Id);

		if (removedActivePreset)
		{
			var nextIndex = Math.Clamp(removeIndex, 0, _settings.Presets.Count - 1);
			_settings.ActivePresetId = _settings.Presets[nextIndex].Id;
		}

		_settings.EnsurePresets();
		return removedActivePreset;
	}

	public void ClearCurrentPreset()
	{
		CurrentPreset.Characters.Clear();
		CurrentPreset.CachedCards.Clear();
		_rowCollections[CurrentPreset.Id] = new ObservableCollection<CharacterRow>();
		SyncActiveAliases();
	}

	public void ReplaceCurrentCharacters(IEnumerable<SavedCharacter> characters)
	{
		CurrentPreset.Characters = characters
			.Select(character => new SavedCharacter
			{
				ServerId = character.ServerId,
				CharacterName = character.CharacterName
			})
			.ToList();
		SyncActiveAliases();
	}

	public void PersistRows(IEnumerable<CharacterRow> rows)
	{
		var rowList = rows
			.Where(row => !row.IsDropIndicator && !string.IsNullOrWhiteSpace(row.CharacterName))
			.ToList();
		CurrentPreset.Characters = rowList.Select(ToSavedCharacter).ToList();
		CurrentPreset.CachedCards = rowList.Select(row => row.ToCache()).ToList();
		_rowCollections[CurrentPreset.Id] = new ObservableCollection<CharacterRow>(rowList);
		SyncActiveAliases();
	}

	public void UpdateCharacters(IEnumerable<CharacterRow> rows)
	{
		CurrentPreset.Characters = rows
			.Where(row => !row.IsDropIndicator && !string.IsNullOrWhiteSpace(row.CharacterName))
			.Select(ToSavedCharacter)
			.ToList();
		SyncActiveAliases();
	}

	public void UpdateCachedCards(IEnumerable<CharacterRow> rows)
	{
		var rowList = rows
			.Where(row => !row.IsDropIndicator && !string.IsNullOrWhiteSpace(row.CharacterName))
			.ToList();
		CurrentPreset.CachedCards = rowList.Select(row => row.ToCache()).ToList();
		_rowCollections[CurrentPreset.Id] = new ObservableCollection<CharacterRow>(rowList);
		SyncActiveAliases();
	}

	public bool TryGetRows(string presetId, out ObservableCollection<CharacterRow> rows)
	{
		return _rowCollections.TryGetValue(presetId, out rows!);
	}

	public void SetRows(string presetId, IEnumerable<CharacterRow> rows)
	{
		_rowCollections[presetId] = new ObservableCollection<CharacterRow>(rows);
	}

	public void SetRows(string presetId, ObservableCollection<CharacterRow> rows)
	{
		_rowCollections[presetId] = rows;
	}

	private void SyncActiveAliases()
	{
		_settings.Characters = CurrentPreset.Characters;
		_settings.CachedCards = CurrentPreset.CachedCards;
	}

	private static SavedCharacter ToSavedCharacter(CharacterRow row)
	{
		return new SavedCharacter
		{
			ServerId = row.ServerId,
			CharacterName = row.CharacterName
		};
	}
}
