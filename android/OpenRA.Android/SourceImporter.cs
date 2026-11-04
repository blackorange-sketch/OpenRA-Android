using System;
using System.Collections.Generic;
using System.IO;
using Android.Content;
using Android.Provider;

namespace OpenRAHost
{
	/// <summary>
	/// For Red Alert / Tiberian Dawn / Tiberian Sun: copies the files OpenRA's own content installers read
	/// (MIX archives, disc .cab/.hdr, ...) from a folder the player picks into support/Import/&lt;mod&gt;.
	/// The engine (patched) treats that folder as an extra content source, so its installer can then
	/// extract music, videos and game data from it. Sub-folders are kept, because installers look for
	/// files such as INSTALL/REDALERT.MIX.
	/// </summary>
	static class SourceImporter
	{
		public static string ImportRoot(string filesRoot) => Path.Combine(filesRoot, "support", "Import");

		public static string ImportDir(string filesRoot, string mod) => Path.Combine(ImportRoot(filesRoot), mod);

		public static bool HasImport(string filesRoot) =>
			Directory.Exists(ImportRoot(filesRoot)) && Directory.GetFiles(ImportRoot(filesRoot), "*", SearchOption.AllDirectories).Length > 0;

		public static void Clear(string filesRoot)
		{
			try
			{
				if (Directory.Exists(ImportRoot(filesRoot)))
					Directory.Delete(ImportRoot(filesRoot), true);
			}
			catch
			{
				// best effort
			}
		}

		static HashSet<string> LoadNames(Context ctx)
		{
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			using var s = ctx.Assets.Open("source-names.txt");
			using var r = new StreamReader(s);
			string line;
			while ((line = r.ReadLine()) != null)
				if (line.Trim().Length > 0)
					names.Add(line.Trim());

			return names;
		}

		/// <returns>(files copied, bytes copied, error or null)</returns>
		public static (int Files, long Bytes, string Error) Import(Context ctx, Android.Net.Uri tree, string filesRoot, string mod, Action<string> progress)
		{
			var files = 0;
			var bytes = 0L;
			try
			{
				var names = LoadNames(ctx);
				var target = ImportDir(filesRoot, mod);
				if (Directory.Exists(target))
					Directory.Delete(target, true);
				Directory.CreateDirectory(target);

				void Walk(string docId, string rel, int depth)
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

					var dirs = new List<(string Id, string Name)>();
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
								dirs.Add((id, name));
							else if (names.Contains(name))
								wanted.Add((id, name));
						}
					}

					foreach (var (id, name) in wanted)
					{
						var dest = Path.Combine(target, rel, name);
						Directory.CreateDirectory(Path.GetDirectoryName(dest));
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

					foreach (var (id, name) in dirs)
						Walk(id, Path.Combine(rel, name), depth + 1);
				}

				progress("Шукаю файли у вибраній папці…");
				Walk(DocumentsContract.GetTreeDocumentId(tree), "", 0);
				return (files, bytes, null);
			}
			catch (Exception e)
			{
				return (files, bytes, e.Message);
			}
		}
	}
}
