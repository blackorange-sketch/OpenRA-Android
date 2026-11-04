using System.IO;
using System.Linq;

namespace OpenRAHost
{
	/// <summary>One game the launcher can start (an OpenRA mod).</summary>
	sealed class GameInfo
	{
		public string Id;          // OpenRA mod id
		public string Name;        // shown on the picker button
		public string Line1, Line2; // big title
		public string ContentDir;  // below support/Content
		public string[] Test;      // files whose presence means the game files are installed
	}

	static class Games
	{
		public static readonly GameInfo[] All =
		{
			new GameInfo
			{
				Id = "d2k", Name = "Dune 2000", Line1 = "DUNE", Line2 = "2000",
				ContentDir = "d2k/v3", Test = new string[0]
			},
			new GameInfo
			{
				Id = "ra", Name = "Red Alert", Line1 = "RED", Line2 = "ALERT",
				ContentDir = "ra/v2",
				Test = new[] { "allies.mix", "conquer.mix", "interior.mix", "hires.mix", "lores.mix", "local.mix", "speech.mix", "russian.mix", "snow.mix", "sounds.mix", "temperat.mix" }
			},
			new GameInfo
			{
				Id = "cnc", Name = "Tiberian Dawn", Line1 = "TIBERIAN", Line2 = "DAWN",
				ContentDir = "cnc",
				Test = new[] { "conquer.mix", "desert.mix", "sounds.mix", "speech.mix", "temperat.mix", "tempicnh.mix", "winter.mix" }
			},
			new GameInfo
			{
				Id = "ts", Name = "Tiberian Sun", Line1 = "TIBERIAN", Line2 = "SUN",
				ContentDir = "ts",
				Test = new[] { "cache.mix", "conquer.mix", "isosnow.mix", "isotemp.mix", "local.mix", "sidec01.mix", "sidec02.mix", "sno.mix", "snow.mix", "sounds.mix", "speech01.mix", "tem.mix", "temperat.mix" }
			}
		};

		public static GameInfo ById(string id) => All.FirstOrDefault(g => g.Id == id) ?? All[0];

		public static bool IsInstalled(GameInfo g, string filesRoot)
		{
			if (g.Id == "d2k")
				return ContentImporter.IsInstalled(filesRoot);

			var dir = Path.Combine(filesRoot, "support", "Content", g.ContentDir);
			return g.Test.All(f => File.Exists(Path.Combine(dir, f)));
		}
	}
}
