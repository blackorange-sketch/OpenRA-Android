using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Android.Content;
using Android.Database;
using Android.Provider;

namespace OpenRAHost
{
	/// <summary>Result of an import run.</summary>
	sealed class ImportResult
	{
		public int Base, BaseTotal, Movies, MoviesTotal, Music, MusicTotal;
		public List<string> MissingBase = new List<string>();
		public string Error;
	}

	/// <summary>
	/// Copies the Dune 2000 files the d2k mod needs from a user-picked folder (Storage Access Framework)
	/// into support/Content/d2k/v3, which is where OpenRA looks for installed content. Files are matched by
	/// name, case-insensitively, anywhere below the chosen folder.
	/// </summary>
	static class ContentImporter
	{
		// Files OpenRA's d2k mod checks to decide the content is installed (mods/d2k-content/mod.yaml).
		static readonly string[] Required =
		{
			"BLOXBASE.R16", "BLOXBAT.R16", "BLOXBGBS.R16", "BLOXICE.R16",
			"BLOXTREE.R16", "BLOXWAST.R16", "SOUND.RS", "PALETTE.BIN", "DATA.R16"
		};

		public static string ContentDir(string filesRoot) => Path.Combine(filesRoot, "support", "Content", "d2k", "v3");

		public static bool IsInstalled(string filesRoot)
		{
			var dir = ContentDir(filesRoot);
			return Required.All(f => File.Exists(Path.Combine(dir, f)));
		}

		static List<(string Kind, string Dest)> LoadManifest(Context ctx)
		{
			var list = new List<(string, string)>();
			using var s = ctx.Assets.Open("content-manifest.txt");
			using var r = new StreamReader(s);
			string line;
			while ((line = r.ReadLine()) != null)
			{
				var parts = line.Split('\t');
				if (parts.Length == 2)
					list.Add((parts[0], parts[1]));
			}

			return list;
		}

		public static ImportResult Import(Context ctx, Android.Net.Uri tree, string filesRoot, Action<string> progress)
		{
			var result = new ImportResult();
			try
			{
				var manifest = LoadManifest(ctx);
				result.BaseTotal = manifest.Count(m => m.Kind == "base");
				result.MoviesTotal = manifest.Count(m => m.Kind == "movies");
				result.MusicTotal = manifest.Count(m => m.Kind == "music");

				progress("Шукаю файли у вибраній папці…");
				var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var found = 0;
				Walk(ctx, tree, DocumentsContract.GetTreeDocumentId(tree), index, ref found, progress, 0);

				var dir = ContentDir(filesRoot);
				var done = 0;
				foreach (var (kind, dest) in manifest)
				{
					done++;
					var name = Path.GetFileName(dest);
					if (!index.TryGetValue(name, out var docId))
					{
						if (kind == "base")
							result.MissingBase.Add(dest);
						continue;
					}

					if (done % 10 == 0)
						progress($"Копіюю {done} з {manifest.Count}…");

					var target = Path.Combine(dir, dest);
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					var docUri = DocumentsContract.BuildDocumentUriUsingTree(tree, docId);
					var tmp = target + ".part";
					using (var input = ctx.ContentResolver.OpenInputStream(docUri))
					using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write))
						input.CopyTo(output);

					if (File.Exists(target))
						File.Delete(target);
					File.Move(tmp, target);

					if (kind == "base") result.Base++;
					else if (kind == "movies") result.Movies++;
					else result.Music++;
				}
			}
			catch (Exception e)
			{
				result.Error = e.Message;
			}

			return result;
		}

		static void Walk(Context ctx, Android.Net.Uri tree, string docId, Dictionary<string, string> index,
			ref int found, Action<string> progress, int depth)
		{
			if (depth > 8)
				return;

			var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, docId);
			var cols = new[]
			{
				DocumentsContract.Document.ColumnDocumentId,
				DocumentsContract.Document.ColumnDisplayName,
				DocumentsContract.Document.ColumnMimeType
			};

			var subdirs = new List<string>();
			using (var cursor = ctx.ContentResolver.Query(children, cols, null, null, null))
			{
				if (cursor == null)
					return;

				while (cursor.MoveToNext())
				{
					var id = cursor.GetString(0);
					var name = cursor.GetString(1);
					var mime = cursor.GetString(2);
					if (mime == DocumentsContract.Document.MimeTypeDir)
						subdirs.Add(id);
					else if (name != null && !index.ContainsKey(name))
					{
						index[name] = id;
						found++;
					}
				}
			}

			progress($"Знайдено файлів: {found}");
			foreach (var sub in subdirs)
				Walk(ctx, tree, sub, index, ref found, progress, depth + 1);
		}
	}
}
