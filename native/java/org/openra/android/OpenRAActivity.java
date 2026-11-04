package org.openra.android;

import android.graphics.Point;
import android.os.Build;
import android.os.Bundle;
import android.view.Display;
import android.view.View;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import java.io.File;
import java.io.FileWriter;
import org.libsdl.app.SDLActivity;

/**
 * Hosts the SDL surface. The game loop itself runs in managed code: SDL calls
 * SDL_main() in libopenramain.so, which forwards to the .NET host.
 */
public class OpenRAActivity extends SDLActivity {
	@Override
	protected void onCreate(Bundle savedInstanceState) {
		// Draw under the camera cut-out and system bars instead of leaving black strips.
		if (Build.VERSION.SDK_INT >= 28) {
			WindowManager.LayoutParams lp = getWindow().getAttributes();
			// 3 = LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS (API 30), 1 = SHORT_EDGES (API 28)
			lp.layoutInDisplayCutoutMode = Build.VERSION.SDK_INT >= 30 ? 3 : 1;
			getWindow().setAttributes(lp);
		}

		// 6 = SCREEN_ORIENTATION_SENSOR_LANDSCAPE
		setRequestedOrientation(6);
		super.onCreate(savedInstanceState);
	}

	@Override
	public void setRequestedOrientation(int requestedOrientation) {
		// SDL picks portrait/landscape from the window size at creation time, which on a phone whose
		// desktop is portrait flips the game upright. This game is landscape only.
		super.setRequestedOrientation(6);
	}

	@Override
	public void onWindowFocusChanged(boolean hasFocus) {
		super.onWindowFocusChanged(hasFocus);
		if (hasFocus) {
			goImmersive();
			// Insets settle a moment after focus is gained.
			getWindow().getDecorView().postDelayed(new Runnable() {
				public void run() {
					logWindow();
				}
			}, 700);
		}
	}

	// Hide the status/navigation bars and draw under the camera cut-out so the surface fills the screen.
	// Without this the view is smaller than the display and SDL's surface is stretched into it,
	// which also skews where touches land.
	private void goImmersive() {
		try {
			Window w = getWindow();
			if (Build.VERSION.SDK_INT >= 30) {
				w.setDecorFitsSystemWindows(false);
				WindowInsetsController c = w.getInsetsController();
				if (c != null) {
					c.hide(WindowInsets.Type.systemBars());
					c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
				}
			} else {
				w.getDecorView().setSystemUiVisibility(
					View.SYSTEM_UI_FLAG_LAYOUT_STABLE | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
					| View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
					| View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
			}
		} catch (Throwable t) {
			// Cosmetic only.
		}
	}

	private void logWindow() {
		try {
			View decor = getWindow().getDecorView();
			Display d = getWindowManager().getDefaultDisplay();
			Point real = new Point();
			d.getRealSize(real);
			String insets = "n/a";
			if (Build.VERSION.SDK_INT >= 30 && decor.getRootWindowInsets() != null) {
				WindowInsets wi = decor.getRootWindowInsets();
				insets = "systemBars=" + wi.getInsets(WindowInsets.Type.systemBars())
					+ " cutout=" + wi.getInsets(WindowInsets.Type.displayCutout());
			}
			File dir = new File(getFilesDir(), "support/Logs");
			dir.mkdirs();
			FileWriter fw = new FileWriter(new File(dir, "window.log"), true);
			fw.write("decor=" + decor.getWidth() + "x" + decor.getHeight()
				+ " display=" + real.x + "x" + real.y + " " + insets + "\n");
			fw.close();
		} catch (Throwable t) {
			// Diagnostics only.
		}
	}

	@Override
	protected String[] getLibraries() {
		// Order matters: dependencies first. The names match what OpenRA's
		// native bindings ask for (SDL2, soft_oal, freetype6, lua51).
		return new String[] { "SDL2", "soft_oal", "freetype6", "lua51", "openramain" };
	}

	@Override
	protected String getMainSharedObject() {
		return getContext().getApplicationInfo().nativeLibraryDir + "/libopenramain.so";
	}

	@Override
	protected String getMainFunction() {
		return "SDL_main";
	}
}
