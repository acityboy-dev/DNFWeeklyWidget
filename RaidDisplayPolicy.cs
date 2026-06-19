namespace DNFWeeklyWidget;

internal static class RaidDisplayPolicy
{
	public static bool IsAutoHideWindow(DateTime localTime)
	{
		var daysSinceThursday = ((int)localTime.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
		var thursday = localTime.Date.AddDays(-daysSinceThursday).AddHours(6);
		return localTime >= thursday && localTime < thursday.AddDays(2);
	}

	public static WeeklyContentSettings CreateEffectiveSettings(
		WeeklyContentSettings configured,
		bool hideRaids)
	{
		var effective = new WeeklyContentSettings
		{
			ShowWeeklyEquipmentLoot = configured.ShowWeeklyEquipmentLoot,
			ShowWeeklyOathLoot = configured.ShowWeeklyOathLoot,
			ShowWeeklyCrystalLoot = configured.ShowWeeklyCrystalLoot,
			ShowVenus = configured.ShowVenus,
			ShowApocalypse = configured.ShowApocalypse,
			ShowBakalRaid = configured.ShowBakalRaid,
			ShowNabelRaid = configured.ShowNabelRaid,
			ShowNormalNabelRaid = configured.ShowNormalNabelRaid,
			ShowHardNabelRaid = configured.ShowHardNabelRaid,
			ShowTwilightOfInae = configured.ShowTwilightOfInae,
			ShowDiregieRaid = configured.ShowDiregieRaid,
			ShowHardMistRaid = configured.ShowHardMistRaid
		};
		if (!hideRaids)
			return effective;

		effective.ShowBakalRaid = false;
		effective.ShowNabelRaid = false;
		effective.ShowNormalNabelRaid = false;
		effective.ShowHardNabelRaid = false;
		effective.ShowTwilightOfInae = false;
		effective.ShowDiregieRaid = false;
		effective.ShowHardMistRaid = false;
		return effective;
	}
}
