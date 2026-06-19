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
		CurrentPreset.CardMemos.Clear();
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
		PruneCurrentMemos();
		SyncActiveAliases();
	}

	public int AppendCurrentCharacters(IEnumerable<SavedCharacter> characters)
	{
		var existingCharacters = CurrentPreset.Characters
			.Select(CreateCharacterKey)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var addedCount = 0;

		foreach (var character in characters)
		{
			if (string.IsNullOrWhiteSpace(character.ServerId) ||
				string.IsNullOrWhiteSpace(character.CharacterName) ||
				!existingCharacters.Add(CreateCharacterKey(character)))
			{
				continue;
			}

			CurrentPreset.Characters.Add(new SavedCharacter
			{
				ServerId = character.ServerId.Trim(),
				CharacterName = character.CharacterName.Trim()
			});
			addedCount++;
		}

		SyncActiveAliases();
		return addedCount;
	}

	public int RemoveCurrentCharacters(IEnumerable<SavedCharacter> characters)
	{
		var charactersToRemove = characters
			.Where(character =>
				!string.IsNullOrWhiteSpace(character.ServerId) &&
				!string.IsNullOrWhiteSpace(character.CharacterName))
			.Select(CreateCharacterKey)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (charactersToRemove.Count == 0)
			return 0;

		var removedCount = CurrentPreset.Characters.RemoveAll(character =>
			charactersToRemove.Contains(CreateCharacterKey(character)));
		CurrentPreset.CachedCards.RemoveAll(card =>
			charactersToRemove.Contains(CreateCharacterKey(card.ServerId, card.CharacterName)));
		CurrentPreset.CardMemos.RemoveAll(memo =>
			charactersToRemove.Contains(CreateCharacterKey(memo.ServerId, memo.CharacterName)));

		if (_rowCollections.TryGetValue(CurrentPreset.Id, out var rows))
		{
			for (var index = rows.Count - 1; index >= 0; index--)
			{
				var row = rows[index];
				if (charactersToRemove.Contains(CreateCharacterKey(row.ServerId, row.CharacterName)))
					rows.RemoveAt(index);
			}
		}

		SyncActiveAliases();
		return removedCount;
	}

	public void PersistRows(IEnumerable<CharacterRow> rows)
	{
		var rowList = rows
			.Where(row => !row.IsDropIndicator && !string.IsNullOrWhiteSpace(row.CharacterName))
			.ToList();
		CurrentPreset.Characters = rowList.Select(ToSavedCharacter).ToList();
		CurrentPreset.CachedCards = rowList.Select(row => row.ToCache()).ToList();
		SyncCurrentMemos(rowList);
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

	public void ApplyCurrentMemos(IEnumerable<CharacterRow> rows)
	{
		foreach (var row in rows.Where(row => !row.IsDropIndicator))
		{
			var memo = CurrentPreset.CardMemos.FirstOrDefault(item =>
				string.Equals(item.ServerId, row.ServerId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(item.CharacterName, row.CharacterName, StringComparison.OrdinalIgnoreCase));
			row.Memo = memo?.Memo ?? "";
		}
	}

	public void UpdateCurrentMemo(CharacterRow row)
	{
		CurrentPreset.CardMemos.RemoveAll(item =>
			string.Equals(item.ServerId, row.ServerId, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(item.CharacterName, row.CharacterName, StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrWhiteSpace(row.Memo))
		{
			CurrentPreset.CardMemos.Add(new CardMemoEntry
			{
				ServerId = row.ServerId,
				CharacterName = row.CharacterName,
				Memo = row.Memo
			});
		}
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

	private void SyncCurrentMemos(IEnumerable<CharacterRow> rows)
	{
		CurrentPreset.CardMemos = rows
			.Where(row => !string.IsNullOrWhiteSpace(row.Memo))
			.Select(row => new CardMemoEntry
			{
				ServerId = row.ServerId,
				CharacterName = row.CharacterName,
				Memo = row.Memo
			})
			.ToList();
	}

	private void PruneCurrentMemos()
	{
		var keys = CurrentPreset.Characters
			.Select(CreateCharacterKey)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		CurrentPreset.CardMemos.RemoveAll(memo =>
			!keys.Contains(CreateCharacterKey(memo.ServerId, memo.CharacterName)));
	}

	private static SavedCharacter ToSavedCharacter(CharacterRow row)
	{
		return new SavedCharacter
		{
			ServerId = row.ServerId,
			CharacterName = row.CharacterName
		};
	}

	private static string CreateCharacterKey(SavedCharacter character) =>
		CreateCharacterKey(character.ServerId, character.CharacterName);

	private static string CreateCharacterKey(string serverId, string characterName) =>
		$"{serverId.Trim()}\u001f{characterName.Trim()}";
}
