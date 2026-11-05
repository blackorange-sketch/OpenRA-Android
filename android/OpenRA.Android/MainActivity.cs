using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace OpenRAHost
{
	/// <summary>
	/// Launcher: shows whether the game files are present, imports them from a folder the player picks,
	/// unpacks the engine payload and starts the SDL activity.
	/// </summary>
	[Activity(Label = "OpenRA", MainLauncher = true, Exported = true,
		ScreenOrientation = ScreenOrientation.SensorLandscape,
		Theme = "@android:style/Theme.Material.NoActionBar")]
	public class MainActivity : Activity
	{
		const int PickFolder = 1001;

		static readonly Color Bg = Color.ParseColor("#14110C");
		static readonly Color Card = Color.ParseColor("#231C11");
		static readonly Color Gold = Color.ParseColor("#E0A93B");
		static readonly Color Text = Color.ParseColor("#EFE3C4");
		static readonly Color Dim = Color.ParseColor("#A89B7C");
		static readonly Color Good = Color.ParseColor("#7DBB5A");
		static readonly Color Bad = Color.ParseColor("#E06A4A");

		string engineRoot;
		bool engineReady;
		string engineError;
		bool busy;
		string busyText = "";
		string lastImportNote;

		// Which game the launcher shows and starts (OpenRA mod id).
		string SelectedGame
		{
			get => GetSharedPreferences("openra", FileCreationMode.Private).GetString("game", "d2k");
			set => GetSharedPreferences("openra", FileCreationMode.Private).Edit().PutString("game", value).Apply();
		}

		string VoiceFolder
		{
			get => GetSharedPreferences("openra", FileCreationMode.Private).GetString("voice", "GAMESFX");
			set => GetSharedPreferences("openra", FileCreationMode.Private).Edit().PutString("voice", value).Apply();
		}

		int TouchDy
		{
			get => GetSharedPreferences("openra", FileCreationMode.Private).GetInt("touch_dy", 30);
			set => GetSharedPreferences("openra", FileCreationMode.Private).Edit().PutInt("touch_dy", Math.Clamp(value, -60, 150)).Apply();
		}

		// "ru" or "en": which text set is copied over the mod files before the game starts.
		string TextLanguage
		{
			get => GetSharedPreferences("openra", FileCreationMode.Private).GetString("lang", "ru");
			set => GetSharedPreferences("openra", FileCreationMode.Private).Edit().PutString("lang", value).Apply();
		}

		static void CopyTree(string from, string to)
		{
			if (!System.IO.Directory.Exists(from))
				return;

			foreach (var file in System.IO.Directory.GetFiles(from, "*", System.IO.SearchOption.AllDirectories))
			{
				var dest = System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, file));
				System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
				System.IO.File.Copy(file, dest, true);
			}
		}

		void ApplyTextLanguage()
		{
			try
			{
				CopyTree(System.IO.Path.Combine(engineRoot, "translations", TextLanguage), engineRoot);
			}
			catch (Exception e)
			{
				Android.Util.Log.Warn("OpenRA", "Could not apply text language: " + e.Message);
			}
		}

		// 0 = no cap (follows the display refresh rate).
		int FpsCap
		{
			get => GetSharedPreferences("openra", FileCreationMode.Private).GetInt("fps_cap", 60);
			set => GetSharedPreferences("openra", FileCreationMode.Private).Edit().PutInt("fps_cap", value).Apply();
		}

		bool ShowFps
		{
			get => GetSharedPreferences("openra", FileCreationMode.Private).GetBoolean("show_fps", false);
			set => GetSharedPreferences("openra", FileCreationMode.Private).Edit().PutBoolean("show_fps", value).Apply();
		}

		string FilesRoot => FilesDir.AbsolutePath;

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			var report = DiagnosticsReport.Build(FilesRoot);
			if (report != null)
				ShowReport(report, true);
			else
				BuildMain();

			StartEnginePrepare();
		}

		int Dp(float v) => (int)(v * Resources.DisplayMetrics.Density + 0.5f);

		static GradientDrawable Round(Color fill, int radius, Color? stroke = null, int strokeWidth = 0)
		{
			var d = new GradientDrawable();
			d.SetColor(fill);
			d.SetCornerRadius(radius);
			if (stroke.HasValue)
				d.SetStroke(strokeWidth, stroke.Value);
			return d;
		}

		Button MakeButton(string text, bool primary, bool enabled, Action onClick)
		{
			var b = new Button(this) { Text = text, Enabled = enabled, TextSize = 18, Gravity = GravityFlags.Center };
			b.SetAllCaps(false);
			b.SetTypeface(null, TypefaceStyle.Bold);
			b.SetTextColor(primary ? Color.ParseColor("#1B1407") : Text);
			var fill = primary ? Gold : Color.ParseColor("#3A2F1B");
			if (!enabled)
				fill = Color.ParseColor("#2A241A");
			b.Background = Round(fill, Dp(14));
			b.Alpha = enabled ? 1f : 0.5f;
			b.SetMinimumHeight(Dp(60));
			b.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
			b.Click += (_, _) => onClick();
			return b;
		}

		TextView MakeText(string text, float size, Color color, bool bold = false)
		{
			var t = new TextView(this) { Text = text, TextSize = size };
			t.SetTextColor(color);
			if (bold)
				t.SetTypeface(null, TypefaceStyle.Bold);
			return t;
		}

		void BuildMain()
		{
			var game = Games.ById(SelectedGame);
			var isDune = game.Id == "d2k";
			var contentOk = Games.IsInstalled(game, FilesRoot);

			var root = new LinearLayout(this) { Orientation = Orientation.Horizontal };
			root.SetBackgroundColor(Bg);
			root.SetPadding(Dp(28), Dp(20), Dp(28), Dp(20));
			root.SetGravity(GravityFlags.CenterVertical);

			// Left: title
			var left = new LinearLayout(this) { Orientation = Orientation.Vertical };
			left.SetGravity(GravityFlags.CenterVertical);
			left.AddView(MakeText(game.Line1, 34, Gold, true));
			left.AddView(MakeText(game.Line2, 34, Gold, true));
			var sub = MakeText("OpenRA для Android", 15, Dim);
			sub.SetPadding(0, Dp(4), 0, Dp(10));
			left.AddView(sub);

			foreach (var g in Games.All)
			{
				var gid = g.Id;
				var pick = MakeButton(g.Name, gid == game.Id, true, () =>
				{
					SelectedGame = gid;
					BuildMain();
				});
				pick.TextSize = 15;
				pick.SetMinimumHeight(Dp(44));
				left.AddView(pick, ButtonParams(Dp(6)));
			}

			var leftScroll = new ScrollView(this) { FillViewport = true };
			leftScroll.AddView(left);
			root.AddView(leftScroll, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 0.8f));

			// Right: card
			var scroll = new ScrollView(this) { FillViewport = true };
			var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
			card.Background = Round(Card, Dp(20), Color.ParseColor("#4A3A1C"), Dp(1));
			card.SetPadding(Dp(22), Dp(18), Dp(22), Dp(18));
			scroll.AddView(card);

			card.AddView(MakeText("Файли гри", 14, Dim));
			var status = MakeText(
				isDune
					? (contentOk ? "✔ Файли Dune 2000 імпортовано" : "✖ Файли гри ще не додані")
					: (contentOk ? "✔ Файли гри встановлені" : "ℹ Файли ще не встановлені"),
				20, contentOk ? Good : (isDune ? Bad : Dim), true);
			status.SetPadding(0, Dp(4), 0, Dp(4));
			card.AddView(status);

			var hint = MakeText(
				isDune
					? (contentOk
						? "Можна грати. Щоб оновити файли, імпортуйте папку ще раз."
						: "Натисніть «Імпортувати» і виберіть папку з вашою грою Dune 2000 (там, де лежать BLOXBASE.R16, SOUND.RS, папки GAMESFX, Movies, Music).")
					: (contentOk
						? "Можна грати."
						: "Натисніть «Грати». При першому запуску гра запропонує встановити вміст: виберіть швидке встановлення, безкоштовна версія завантажиться з інтернету (потрібен інтернет)."),
				14, Dim);
			hint.SetPadding(0, 0, 0, Dp(10));
			card.AddView(hint);

			if (lastImportNote != null)
			{
				var note = MakeText(lastImportNote, 13, Text);
				note.SetPadding(0, 0, 0, Dp(10));
				card.AddView(note);
			}

			var engine = MakeText(
				engineError != null ? "Рушій: помилка підготовки" :
				engineReady ? "Рушій: готовий" : "Рушій: розпаковується…",
				13, engineError != null ? Bad : Dim);
			engine.SetPadding(0, 0, 0, Dp(12));
			card.AddView(engine);

			if (busy)
			{
				var bar = new ProgressBar(this) { Indeterminate = true };
				card.AddView(bar);
				var bt = MakeText(busyText, 14, Text);
				bt.SetPadding(0, Dp(6), 0, Dp(6));
				card.AddView(bt);
			}
			else
			{
				var play = MakeButton("▶  Грати", true, (contentOk || !isDune) && engineReady, () => Launch(null));
				card.AddView(play, ButtonParams(0));

				if (isDune)
				{
					var import = MakeButton(contentOk ? "Імпортувати знову" : "Імпортувати файли гри", !contentOk,
						true, StartPicker);
					card.AddView(import, ButtonParams(Dp(10)));
				}
				else
				{
					card.AddView(MakeButton("Імпортувати файли гри (відео, музика)", true, true, StartPicker),
						ButtonParams(Dp(10)));
					card.AddView(MakeText(
						"Відкриється вибір папки (як для Dune 2000): виберіть папку зі Steam/Origin/Remastered-версією або дисками. Файли скопіюються, гра сама розпакує відео й музику та запуститься.",
						12, Dim));
					card.AddView(MakeButton("Завантажити безкоштовні файли з інтернету", false, engineReady, () => Launch(game.Id + "-content")),
						ButtonParams(Dp(10)));
					if (SourceImporter.HasImport(FilesRoot))
						card.AddView(MakeButton("Видалити скопійовані файли імпорту (звільнити місце)", false, true, () =>
						{
							SourceImporter.Clear(FilesRoot);
							lastImportNote = "Файли імпорту видалено.";
							BuildMain();
						}), ButtonParams(Dp(10)));
				}

				var calib = new LinearLayout(this) { Orientation = Orientation.Horizontal };
				calib.SetGravity(GravityFlags.CenterVertical);
				var calibText = MakeText("Тап вище на " + TouchDy + " px", 14, Text);
				calib.AddView(calibText, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
				calib.AddView(MakeButton("−", false, true, () => { TouchDy -= 10; BuildMain(); }),
					new LinearLayout.LayoutParams(Dp(64), ViewGroup.LayoutParams.WrapContent));
				calib.AddView(MakeButton("+", false, true, () => { TouchDy += 10; BuildMain(); }),
					new LinearLayout.LayoutParams(Dp(64), ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(8) });
				card.AddView(calib, ButtonParams(Dp(10)));

				if (isDune)
				{
					var russian = VoiceFolder == "GAMESFXRus";
					card.AddView(MakeButton("Озвучка: " + (russian ? "російська" : "оригінальна (англ.)") + "  — змінити",
						false, true, () => { VoiceFolder = russian ? "GAMESFX" : "GAMESFXRus"; BuildMain(); }),
						ButtonParams(Dp(10)));
					card.AddView(MakeText("Озвучка застосовується при наступному імпорті файлів.", 12, Dim));
				}

				var lang = TextLanguage;
				card.AddView(MakeButton("Мова тексту: " + (lang == "ru" ? "російська" : "English") + "  — змінити",
					false, true, () => { TextLanguage = lang == "ru" ? "en" : "ru"; BuildMain(); }),
					ButtonParams(Dp(10)));
				card.AddView(MakeText("Переклад: місії, меню, назви юнітів. Решта лишається англійською.", 12, Dim));

				var cap = FpsCap;
				card.AddView(MakeButton("Ліміт кадрів: " + (cap == 0 ? "без ліміту" : cap + " FPS") + "  — змінити",
					false, true, () => { FpsCap = cap == 60 ? 30 : cap == 30 ? 0 : 60; BuildMain(); }),
					ButtonParams(Dp(10)));
				var fps = ShowFps;
				card.AddView(MakeButton("Лічильник FPS у грі: " + (fps ? "увімкнено" : "вимкнено") + "  — змінити",
					false, true, () => { ShowFps = !fps; BuildMain(); }),
					ButtonParams(Dp(10)));
				card.AddView(MakeText("Нижчий ліміт менше гріє телефон і стабілізує кадри.", 12, Dim));

				var diag = MakeButton("Діагностика", false, true,
					() => ShowReport(engineError ?? DiagnosticsReport.BuildAlways(FilesRoot), false));
				card.AddView(diag, ButtonParams(Dp(10)));
			}

			var cardParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1.2f);
			root.AddView(scroll, cardParams);
			SetContentView(root);
		}

		LinearLayout.LayoutParams ButtonParams(int topMargin)
		{
			var p = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
			p.TopMargin = topMargin;
			return p;
		}

		void ShowReport(string report, bool fromCrash)
		{
			var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
			layout.SetBackgroundColor(Bg);
			layout.SetPadding(Dp(20), Dp(14), Dp(20), Dp(14));

			var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
			row.AddView(MakeButton("Копіювати", true, true, () =>
			{
				var clipboard = (ClipboardManager)GetSystemService(ClipboardService);
				clipboard.PrimaryClip = ClipData.NewPlainText("OpenRA log", report);
				Toast.MakeText(this, "Скопійовано", ToastLength.Short).Show();
			}), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
			var gap = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Dp(10) };
			row.AddView(MakeButton("Назад", false, true, () =>
			{
				if (fromCrash)
					DiagnosticsReport.Clear(FilesRoot);
				BuildMain();
			}), gap);
			layout.AddView(row);

			var text = new TextView(this) { Text = report, TextSize = 11 };
			text.SetTextColor(Text);
			text.SetTextIsSelectable(true);
			text.SetPadding(0, Dp(10), 0, 0);
			var scroll = new ScrollView(this);
			scroll.AddView(text);
			layout.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
			SetContentView(layout);
		}

		void StartEnginePrepare()
		{
			Task.Run(() =>
			{
				try
				{
					engineRoot = PayloadInstaller.Install(this);
					engineReady = true;
				}
				catch (Exception e)
				{
					engineError = "Не вдалося розпакувати рушій:\n\n" + e;
				}

				RunOnUiThread(() =>
				{
					if (!busy)
						BuildMain();
				});
			});
		}

		void StartPicker()
		{
			var intent = new Intent(Intent.ActionOpenDocumentTree);
			intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
			try
			{
				StartActivityForResult(intent, PickFolder);
			}
			catch (Exception e)
			{
				Toast.MakeText(this, "Не вдалося відкрити вибір папки: " + e.Message, ToastLength.Long).Show();
			}
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			base.OnActivityResult(requestCode, resultCode, data);
			if (requestCode != PickFolder || resultCode != Result.Ok || data?.Data == null)
				return;

			var tree = data.Data;
			busy = true;
			busyText = "Починаю…";
			BuildMain();

			Task.Run(() =>
			{
				if (SelectedGame != "d2k")
				{
					var (files, bytes, error) = SourceImporter.Import(this, tree, FilesRoot, SelectedGame, msg =>
					{
						busyText = msg;
						RunOnUiThread(() => BuildMain());
					});

					lastImportNote = error != null
						? "Помилка імпорту: " + error
						: files == 0
							? "У вибраній папці не знайдено потрібних файлів. Виберіть папку встановленої гри або диска."
							: $"Скопійовано файлів: {files} ({bytes / 1048576} МБ).";
					busy = false;

					if (error == null && files > 0)
					{
						// The engine installer picks the imported folder up on its own and goes into the game when done.
						try
						{
							System.IO.File.WriteAllText(System.IO.Path.Combine(SourceImporter.ImportRoot(FilesRoot), "AUTO"), "1");
						}
						catch
						{
							// the manual button still works
						}

						var gameId = SelectedGame;
						RunOnUiThread(() => Launch(gameId + "-content"));
						return;
					}

					RunOnUiThread(BuildMain);
					return;
				}

				var result = ContentImporter.Import(this, tree, FilesRoot, VoiceFolder, msg =>
				{
					busyText = msg;
					RunOnUiThread(() => BuildMain());
				});

				lastImportNote = Summarize(result);
				busy = false;
				RunOnUiThread(BuildMain);
			});
		}

		static string Summarize(ImportResult r)
		{
			if (r.Error != null)
				return "Помилка імпорту: " + r.Error;

			var s = $"Скопійовано: основні файли {r.Base}/{r.BaseTotal}, відео {r.Movies}/{r.MoviesTotal}, музика {r.Music}/{r.MusicTotal}.";
			if (r.MissingBase.Count > 0)
				s += "\nНе знайдено: " + string.Join(", ", r.MissingBase.Take(6).Select(System.IO.Path.GetFileName))
					+ (r.MissingBase.Count > 6 ? $" … (+{r.MissingBase.Count - 6})" : "");
			return s;
		}

		/// <summary>
		/// Menus need ~600 logical pixels of height; the in-game sidebar is ~490 tall, so a running game can use a
		/// larger scale (bigger buttons and icons) than the menus.
		/// </summary>
		string ComputeUiScale(bool inGame)
		{
			var m = new DisplayMetrics();
			WindowManager.DefaultDisplay.GetRealMetrics(m);
			var shortSide = Math.Min(m.WidthPixels, m.HeightPixels);
			var scale = inGame
				? Math.Clamp(Math.Floor(shortSide / 492.0 * 20) / 20, 1.0, 3.0)
				: Math.Clamp(Math.Floor(shortSide / 600.0 * 4) / 4, 1.0, 3.0);
			return scale.ToString("0.##", CultureInfo.InvariantCulture);
		}

		void Launch() => Launch(null);

		void Launch(string modOverride)
		{
			if (!engineReady)
				return;

			// A crash file from an earlier run must not be reported as the result of this one.
			DiagnosticsReport.Clear(FilesRoot);
			ApplyTextLanguage();
			OpenRAEntry.Configure(modOverride ?? SelectedGame, engineRoot, FilesRoot, ComputeUiScale(false), ComputeUiScale(true), TouchDy, FpsCap, ShowFps);

			var intent = new Intent();
			intent.SetClassName(this, "org.openra.android.OpenRAActivity");
			StartActivity(intent);
			Finish();
		}
	}
}
