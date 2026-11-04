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
	/// Launcher screen: unpacks the engine payload on first run, registers the managed
	/// entry point with the native bridge and hands over to the SDL activity.
	/// </summary>
	[Activity(Label = "OpenRA", MainLauncher = true, Exported = true, Theme = "@android:style/Theme.Material.NoActionBar")]
	public class MainActivity : Activity
	{
		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

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
