using System;
using System.IO;
using System.IO.Compression;
using Android.Content;

namespace OpenRAHost
{
	/// <summary>
	/// OpenRA reads its assemblies, mods and shaders from the file system, so the
	/// payload (built by scripts/prepare-openra.sh) is unpacked from the APK once per version.
	/// </summary>
	static class PayloadInstaller
	{
		public static string Install(Context context)
		{
			var root = Path.Combine(context.FilesDir.AbsolutePath, "engine");
			var stampFile = Path.Combine(root, ".stamp");

			string stamp;
			using (var s = context.Assets.Open("payload.stamp"))
			using (var r = new StreamReader(s))
				stamp = r.ReadToEnd().Trim();

			if (File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == stamp)
				return root;

			if (Directory.Exists(root))
				Directory.Delete(root, true);
			Directory.CreateDirectory(root);

			// Asset streams are not seekable, so spool the zip to disk first.
			var tmp = Path.Combine(context.CacheDir.AbsolutePath, "payload.zip");
			using (var assetStream = context.Assets.Open("payload.zip"))
			using (var file = File.Create(tmp))
				assetStream.CopyTo(file);

			try
			{
				using var zip = ZipFile.OpenRead(tmp);
				var rootWithSeparator = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
				foreach (var entry in zip.Entries)
				{
					if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
						continue;

					var dest = Path.GetFullPath(Path.Combine(root, entry.FullName));
					if (!dest.StartsWith(rootWithSeparator, StringComparison.Ordinal))
						throw new InvalidDataException("Unsafe payload entry: " + entry.FullName);

					Directory.CreateDirectory(Path.GetDirectoryName(dest));
					entry.ExtractToFile(dest, true);
				}
			}
			finally
			{
				File.Delete(tmp);
			}

			File.WriteAllText(stampFile, stamp);
			return root;
		}
	}
}
