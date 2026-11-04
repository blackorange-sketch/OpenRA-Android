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
	[Activity(Label = "Dune 2000", MainLauncher = true, Exported = true,
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
			var contentOk = ContentImporter.IsInstalled(FilesRoot);

			var root = new LinearLayout(this) { Orientation = Orientation.Horizontal };
			root.SetBackgroundColor(Bg);
			root.SetPadding(Dp(28), Dp(20), Dp(28), Dp(20));
			root.SetGravity(GravityFlags.CenterVertical);

			// Left: title
			var left = new LinearLayout(this) { Orientation = Orientation.Vertical };
			left.SetGravity(GravityFlags.CenterVertical);
			left.AddView(MakeText("DUNE", 44, Gold, true));
			left.AddView(MakeText("2000", 44, Gold, true));
			var sub = MakeText("OpenRA для Android", 16, Dim);
			sub.SetPadding(0, Dp(8), 0, 0);
			left.AddView(sub);
			root.AddView(left, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 0.8f));

			// Right: card
			var scroll = new ScrollView(this) { FillViewport = true };
			var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
			card.Background = Round(Card, Dp(20), Color.ParseColor("#4A3A1C"), Dp(1));
			card.SetPadding(Dp(22), Dp(18), Dp(22), Dp(18));
			scroll.AddView(card);

			card.AddView(MakeText("Файли гри", 14, Dim));
			var status = MakeText(
				contentOk ? "✔ Файли Dune 2000 імпортовано" : "✖ Файли гри ще не додані",
				20, contentOk ? Good : Bad, true);
			status.SetPadding(0, Dp(4), 0, Dp(4));
			card.AddView(status);

			var hint = MakeText(
				contentOk
					? "Можна грати. Щоб оновити файли, імпортуйте папку ще раз."
					: "Натисніть «Імпортувати» і виберіть папку з вашою грою Dune 2000 (там, де лежать BLOXBASE.R16, SOUND.RS, папки GAMESFX, Movies, Music).",
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
				var play = MakeButton("▶  Грати", true, contentOk && engineReady, Launch);
				card.AddView(play, ButtonParams(0));

				var import = MakeButton(contentOk ? "Імпортувати знову" : "Імпортувати файли гри", !contentOk,
					true, StartPicker);
				card.AddView(import, ButtonParams(Dp(10)));

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
				var result = ContentImporter.Import(this, tree, FilesRoot, msg =>
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

		string ComputeUiScale()
		{
			// OpenRA (patched) needs at least 600 logical pixels of height, so the UI scale is the largest
			// quarter-step that still satisfies that on this screen.
			var m = new DisplayMetrics();
			WindowManager.DefaultDisplay.GetRealMetrics(m);
			var shortSide = Math.Min(m.WidthPixels, m.HeightPixels);
			var scale = Math.Clamp(Math.Floor(shortSide / 600.0 * 4) / 4, 1.0, 3.0);
			return scale.ToString("0.##", CultureInfo.InvariantCulture);
		}

		void Launch()
		{
			if (!engineReady)
				return;

			// A crash file from an earlier run must not be reported as the result of this one.
			DiagnosticsReport.Clear(FilesRoot);
			OpenRAEntry.Configure(engineRoot, FilesRoot, ComputeUiScale());

			var intent = new Intent();
			intent.SetClassName(this, "org.openra.android.OpenRAActivity");
			StartActivity(intent);
			Finish();
		}
	}
}
