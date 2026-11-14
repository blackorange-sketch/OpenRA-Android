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
		// This branch builds the Red Alert 2 APK: one game, files imported by the player (RA2 is not freeware).
		public static readonly GameInfo[] All =
		{
			new GameInfo
			{
				Id = "ra2", Name = "Red Alert 2", Line1 = "RED ALERT", Line2 = "2",
				ContentDir = "ra2",
				Test = new[] { "ra2.mix", "language.mix" }
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
