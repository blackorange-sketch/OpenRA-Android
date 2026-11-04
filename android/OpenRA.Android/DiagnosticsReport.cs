using System;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenRAHost
{
	/// <summary>
	/// Collects what the previous run left behind (managed crash + OpenRA log tails)
	/// so it can be shown on screen without adb.
	/// </summary>
	static class DiagnosticsReport
	{
		const int TailLines = 40;

		static string CrashFile(string filesRoot) => Path.Combine(filesRoot, "support", "host-crash.txt");

		/// <returns>The report text, or null when the previous run did not record a crash.</returns>
		public static string Build(string filesRoot)
		{
			var crashFile = CrashFile(filesRoot);
			if (!File.Exists(crashFile))
				return null;

			return Compose(filesRoot, "OpenRA stopped with an error on the previous run.\n\n" + ReadSafe(crashFile));
		}

		/// <summary>Logs of the last run, whether or not it crashed.</summary>
		public static string BuildAlways(string filesRoot)
		{
			var crashFile = CrashFile(filesRoot);
			var head = File.Exists(crashFile) ? "Last run crashed:\n\n" + ReadSafe(crashFile) : "No crash recorded.";
			return Compose(filesRoot, head);
		}

		static string Compose(string filesRoot, string head)
		{
			var sb = new StringBuilder();
			sb.AppendLine(head);

			foreach (var name in new[] { "graphics.log", "exception.log", "debug.log" })
			{
				var path = Path.Combine(filesRoot, "support", "Logs", name);
				if (!File.Exists(path))
					continue;

				var lines = ReadSafe(path).Split('\n');
				sb.AppendLine();
				sb.AppendLine("--- " + name + " (last " + TailLines + " lines) ---");
				foreach (var line in lines.Skip(Math.Max(0, lines.Length - TailLines)))
					sb.AppendLine(line.TrimEnd('\r'));
			}

			return sb.ToString();
		}

		public static void Clear(string filesRoot)
		{
			try
			{
				File.Delete(CrashFile(filesRoot));
			}
			catch
			{
				// Best effort.
			}
		}

		static string ReadSafe(string path)
		{
			try
			{
				using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var reader = new StreamReader(stream);
				return reader.ReadToEnd();
			}
			catch (Exception e)
			{
				return "(could not read " + path + ": " + e.Message + ")";
			}
		}
	}
}
