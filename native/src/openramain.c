// Tiny native bridge between SDL's Android activity and the managed OpenRA host.
//
// SDLActivity starts a thread, dlopens this library and calls SDL_main() on it.
// The managed host registers a function pointer (OpenRA_SetMain) before the
// activity starts; SDL_main simply forwards to it, so the .NET code runs on the
// thread SDL created for the game loop.

#include <android/log.h>
#include <stddef.h>

#define TAG "OpenRA"
#define EXPORT __attribute__((visibility("default")))

typedef int (*managed_main_fn)(int, char **);

static managed_main_fn g_main = NULL;

EXPORT void OpenRA_SetMain(managed_main_fn fn)
{
	g_main = fn;
}

EXPORT int SDL_main(int argc, char **argv)
{
	if (g_main == NULL)
	{
		__android_log_print(ANDROID_LOG_ERROR, TAG, "SDL_main called before the managed entry point was registered");
		return -1;
	}

	__android_log_print(ANDROID_LOG_INFO, TAG, "Entering managed OpenRA main");
	int status = g_main(argc, argv);
	__android_log_print(ANDROID_LOG_INFO, TAG, "Managed OpenRA main returned %d", status);
	return status;
}
