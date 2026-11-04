package org.openra.android;

import org.libsdl.app.SDLActivity;

/**
 * Hosts the SDL surface. The game loop itself runs in managed code: SDL calls
 * SDL_main() in libopenramain.so, which forwards to the .NET host.
 */
public class OpenRAActivity extends SDLActivity {
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
