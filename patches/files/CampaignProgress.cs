// Campaign flow for the d2k missions. Added by the OpenRA-Android port (not part of upstream).
//
// Remembers which missions were won (stored in the support dir), picks the mission that follows a victory
// (next level of the same house; the "a" variant first, the "b" variant is one tap away in the list),
// reopens a failed mission for another try, and names the ending / defeat videos to play.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenRA.Mods.Common
{
	public static class CampaignProgress
	{
		static readonly Regex Name = new(@"^(Atreides|Harkonnen|Ordos) (\d\d)([ab]?)$", RegexOptions.Compiled);

		static readonly HashSet<string> Done = new();
		static bool loaded;
		static string pendingUid;
		static bool autoBriefing;

		static string SavePath => Path.Combine(Platform.SupportDir, "campaign-progress.txt");

		static void Load()
		{
			if (loaded)
				return;

			loaded = true;
			try
			{
				if (File.Exists(SavePath))
					foreach (var line in File.ReadAllLines(SavePath))
						if (line.Trim().Length > 0)
							Done.Add(line.Trim());
			}
			catch
			{
				// Progress is a convenience; never break the game over it.
			}
		}

		public static bool IsDone(string title)
		{
			Load();
			return title != null && Done.Contains(title);
		}

		static IEnumerable<MapPreview> Missions() =>
			Game.ModData.MapCache.Where(p => p.Status == MapStatus.Available
				&& p.Visibility.HasFlag(MapVisibility.MissionSelector) && p.Title != null);

		/// <summary>Called when a campaign mission ends. A win queues the next mission, a loss queues a retry.</summary>
		public static void RecordResult(string title, bool won)
		{
			Load();
			var m = title == null ? null : Name.Match(title);
			if (m == null || !m.Success)
				return;

			var house = m.Groups[1].Value;
			var level = int.Parse(m.Groups[2].Value);

			MapPreview target;
			if (won)
			{
				if (Done.Add(title))
				{
					try
					{
						File.WriteAllLines(SavePath, Done.OrderBy(x => x, StringComparer.Ordinal));
					}
					catch
					{
						// ignore
					}
				}

				target = Missions()
					.Where(p => Name.Match(p.Title) is { Success: true } n && n.Groups[1].Value == house && int.Parse(n.Groups[2].Value) == level + 1)
					.OrderBy(p => p.Title, StringComparer.Ordinal)
					.FirstOrDefault();
			}
			else
				target = Missions().FirstOrDefault(p => p.Title == title);

			pendingUid = target?.Uid;
			autoBriefing = target != null;
		}

		/// <summary>The mission the menu should open on after a game, or null.</summary>
		public static string TakeNextMapUid()
		{
			var uid = pendingUid;
			pendingUid = null;
			return uid;
		}

		/// <summary>True once after a mission was queued: the browser then starts its briefing video by itself.</summary>
		public static bool TakeAutoBriefing()
		{
			var v = autoBriefing;
			autoBriefing = false;
			return v;
		}

		/// <summary>Defeat video for any mission, ending video after the last level of a house. Null if there is none.</summary>
		public static string EndVideo(string title, bool won)
		{
			var m = title == null ? null : Name.Match(title);
			if (m == null || !m.Success)
				return null;

			var letter = m.Groups[1].Value[0];
			if (!won)
				return letter + "_LOSE_E.VQA";

			return m.Groups[2].Value == "09" ? letter + "_FINL_E.VQA" : null;
		}
	}
}
