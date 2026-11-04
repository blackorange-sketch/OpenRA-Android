package org.openra.android;

import android.os.Build;
import android.os.Bundle;
import android.view.WindowManager;
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

		super.onCreate(savedInstanceState);
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
