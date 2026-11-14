using System;
using System.Collections.Generic;
using System.IO;
using Android.Content;
using Android.Provider;

namespace OpenRAHost
{
	/// <summary>
	/// Red Alert 2 is not freeware, so the player picks the folder of their own install. The mod reads loose
	/// MIX archives from support/Content/ra2 (see Packages in mods/ra2/mod.yaml), so the files are copied
	/// there directly, with lower-case names because the engine looks them up case-sensitively.
	/// </summary>
	static class Ra2Importer
	{
		// Same names as the optional packages in mods/ra2/mod.yaml.
		static readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"ra2.mix", "language.mix", "multi.mix", "audio.mix", "cache.mix", "cameo.mix", "conquer.mix",
			"generic.mix", "isogen.mix", "isosnow.mix", "isotemp.mix", "isourb.mix", "load.mix", "local.mix",
			"neutral.mix", "sidec01.mix", "sidec02.mix", "sno.mix", "snow.mix", "tem.mix", "temperat.mix",
			"theme.mix", "urb.mix", "urban.mix", "audio.bag"
		};

		public static string ContentDir(string filesRoot) => Path.Combine(filesRoot, "support", "Content", "ra2");

		/// <returns>(files copied, bytes copied, error or null)</returns>
		public static (int Files, long Bytes, string Error) Import(Context ctx, Android.Net.Uri tree, string filesRoot, Action<string> progress)
		{
			var files = 0;
			var bytes = 0L;
			try
			{
				var target = ContentDir(filesRoot);
				Directory.CreateDirectory(target);
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				void Walk(string docId, int depth)
				{
					if (depth > 6)
						return;

					var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, docId);
					var cols = new[]
					{
						DocumentsContract.Document.ColumnDocumentId,
						DocumentsContract.Document.ColumnDisplayName,
						DocumentsContract.Document.ColumnMimeType
					};

					var dirs = new List<string>();
					var wanted = new List<(string Id, string Name)>();
					using (var cursor = ctx.ContentResolver.Query(children, cols, null, null, null))
					{
						if (cursor == null)
							return;

						while (cursor.MoveToNext())
						{
							var id = cursor.GetString(0);
							var name = cursor.GetString(1) ?? "";
							if (cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir)
								dirs.Add(id);
							else if (Names.Contains(name) && !seen.Contains(name))
								wanted.Add((id, name));
						}
					}

					foreach (var (id, name) in wanted)
					{
						seen.Add(name);
						var dest = Path.Combine(target, name.ToLowerInvariant());
						progress($"Копіюю {name}… (скопійовано {files}, {bytes / 1048576} МБ)");

						var uri = DocumentsContract.BuildDocumentUriUsingTree(tree, id);
						using (var input = ctx.ContentResolver.OpenInputStream(uri))
						using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write))
						{
							input.CopyTo(output);
							bytes += output.Length;
						}

						files++;
					}

					foreach (var id in dirs)
						Walk(id, depth + 1);
				}

				progress("Шукаю файли у вибраній папці…");
				Walk(DocumentsContract.GetTreeDocumentId(tree), 0);
				return (files, bytes, null);
			}
			catch (Exception e)
			{
				return (files, bytes, e.Message);
			}
		}
	}
}
