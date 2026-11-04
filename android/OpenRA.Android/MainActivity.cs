using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace OpenRAHost
{
	/// <summary>
	/// Launcher screen: shows the previous run's error if there was one, otherwise unpacks the
	/// engine payload on first run, registers the managed entry point with the native bridge
	/// and hands over to the SDL activity.
	/// </summary>
	[Activity(Label = "OpenRA", MainLauncher = true, Exported = true, Theme = "@android:style/Theme.Material.NoActionBar")]
	public class MainActivity : Activity
	{
		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			var report = DiagnosticsReport.Build(FilesDir.AbsolutePath);
			if (report != null)
				ShowReport(report);
			else
				Prepare();
		}

		void ShowReport(string report)
		{
			var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
			layout.SetPadding(24, 24, 24, 24);

			var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };

			var copy = new Button(this) { Text = "Copy" };
			copy.Click += (_, _) =>
			{
				var clipboard = (ClipboardManager)GetSystemService(ClipboardService);
				clipboard.PrimaryClip = ClipData.NewPlainText("OpenRA log", report);
				Toast.MakeText(this, "Copied", ToastLength.Short).Show();
			};

			var retry = new Button(this) { Text = "Run again" };
			retry.Click += (_, _) =>
			{
				DiagnosticsReport.Clear(FilesDir.AbsolutePath);
				Prepare();
			};

			buttons.AddView(copy);
			buttons.AddView(retry);

			var text = new TextView(this) { Text = report, TextSize = 11 };
			text.SetTextIsSelectable(true);
			var scroll = new ScrollView(this);
			scroll.AddView(text);

			layout.AddView(buttons);
			layout.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
			SetContentView(layout);
		}

		void Prepare()
		{
			var status = new TextView(this) { Text = "Preparing OpenRA...", Gravity = GravityFlags.Center, TextSize = 18 };
			SetContentView(status);

			Task.Run(() =>
			{
				try
				{
					var engineRoot = PayloadInstaller.Install(this);
					RunOnUiThread(() => Launch(engineRoot));
				}
				catch (Exception e)
				{
					RunOnUiThread(() => status.Text = "Failed to prepare game files:\n\n" + e);
				}
			});
		}

		void Launch(string engineRoot)
		{
			OpenRAEntry.Configure(engineRoot, FilesDir.AbsolutePath);

			var intent = new Intent();
			intent.SetClassName(this, "org.openra.android.OpenRAActivity");
			StartActivity(intent);
			Finish();
		}
	}
}
