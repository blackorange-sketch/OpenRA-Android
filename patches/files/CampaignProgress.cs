// Campaign flow for the d2k, Red Alert and Tiberian Dawn missions. Added by the OpenRA-Android port (not part of upstream).
//
// Remembers which missions were won (stored in the support dir), picks the mission that follows a victory
// (next level of the same side; the "a" variant first, the others are one tap away in the list),
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
		// Dune 2000 maps are told apart by title ("Atreides 01a"); Red Alert / Tiberian Dawn by folder name ("allies-03b").
		static readonly Regex DuneName = new(@"^(Atreides|Harkonnen|Ordos) (\d\d)([ab]?)$", RegexOptions.Compiled);
		static readonly Regex FolderName = new(@"^(allies|soviet|gdi|nod)-?(\d\d)([abc]?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		sealed class Mission
		{
			public string Key, Side, Variant;
			public int Level;
		}

		static Mission Parse(MapPreview p)
		{
			if (p == null)
				return null;

			if (p.Title != null && DuneName.Match(p.Title) is { Success: true } d)
				return new Mission { Key = p.Title, Side = d.Groups[1].Value, Level = int.Parse(d.Groups[2].Value), Variant = d.Groups[3].Value };

			var folder = p.Path == null ? null : Path.GetFileName(p.Path.TrimEnd('/', '\\'));
			if (folder != null && FolderName.Match(folder) is { Success: true } f)
				return new Mission
				{
					Key = folder.ToLowerInvariant(),
					Side = f.Groups[1].Value.ToLowerInvariant(),
					Level = int.Parse(f.Groups[2].Value),
					Variant = f.Groups[3].Value.ToLowerInvariant()
				};

			return null;
		}

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

		public static bool IsDone(MapPreview preview)
		{
			Load();
			var m = Parse(preview);
			return m != null && Done.Contains(m.Key);
		}

		static IEnumerable<MapPreview> Missions() =>
			Game.ModData.MapCache.Where(p => p.Status == MapStatus.Available
				&& p.Visibility.HasFlag(MapVisibility.MissionSelector) && p.Title != null);

		/// <summary>Called when a campaign mission ends. A win queues the next mission, a loss queues a retry.</summary>
		public static void RecordResult(string mapUid, bool won)
		{
			Load();
			var current = Parse(Game.ModData.MapCache[mapUid]);
			if (current == null)
				return;

			MapPreview target;
			if (won)
			{
				if (Done.Add(current.Key))
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

				// The next level of the same side that exists (Red Alert skips 11 and 12); its "a" variant comes first.
				var candidates = Missions()
					.Select(p => (Preview: p, Info: Parse(p)))
					.Where(x => x.Info != null && x.Info.Side == current.Side && x.Info.Level > current.Level)
					.ToList();

				var nextLevel = candidates.Count == 0 ? 0 : candidates.Min(x => x.Info.Level);
				target = candidates
					.Where(x => x.Info.Level == nextLevel)
					.OrderBy(x => x.Info.Variant, StringComparer.Ordinal)
					.Select(x => x.Preview)
					.FirstOrDefault();
			}
			else
				target = Missions().FirstOrDefault(p => p.Uid == mapUid);

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

		/// <summary>True when the last result queued a follow-up mission (without consuming it).</summary>
		public static bool HasNext => pendingUid != null;

		/// <summary>True once after a mission was queued: the browser then starts its briefing video by itself.</summary>
		public static bool TakeAutoBriefing()
		{
			var v = autoBriefing;
			autoBriefing = false;
			return v;
		}

		/// <summary>Dune 2000 only: defeat video for any mission, ending video after the last level of a house.</summary>
		public static string EndVideo(string mapUid, bool won)
		{
			var preview = Game.ModData.MapCache[mapUid];
			if (preview?.Title == null || !DuneName.IsMatch(preview.Title))
				return null;

			var m = Parse(preview);
			var letter = m.Side[0];
			if (!won)
				return letter + "_LOSE_E.VQA";

			return m.Level == 9 ? letter + "_FINL_E.VQA" : null;
		}
	}
}
