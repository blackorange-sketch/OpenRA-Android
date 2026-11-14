#!/usr/bin/env bash
# Builds the native libraries OpenRA needs for Android arm64-v8a.
#
# Output (android/OpenRA.Android/lib/arm64-v8a):
#   libSDL2.so        <- SDL2         (DllImport "SDL2")
#   libsoft_oal.so    <- openal-soft  (DllImport "soft_oal")
#   libfreetype6.so   <- FreeType     (DllImport "freetype6")
#   liblua51.so       <- Lua 5.1      (DllImport "lua51")
#   libopenramain.so  <- native/src/openramain.c (SDL_main bridge)
# Also copies SDL's Java sources to native-build/sdl-java for build-java.sh.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NDK="${ANDROID_NDK_HOME:-${ANDROID_NDK_LATEST_HOME:-}}"
[ -d "$NDK" ] || { echo "ANDROID_NDK_HOME is not set or not a directory" >&2; exit 1; }

API=26
ABI=arm64-v8a
SDL_VER=2.32.8
OAL_VER=1.24.3
FT_VER=2.13.3
LUA_VER=5.1.5

WORK="$ROOT/native-build"
SRC="$WORK/src"
OUT="$ROOT/android/OpenRA.Android/lib/$ABI"
TOOLCHAIN="$NDK/build/cmake/android.toolchain.cmake"
BIN="$NDK/toolchains/llvm/prebuilt/linux-x86_64/bin"
CC="$BIN/aarch64-linux-android${API}-clang"

mkdir -p "$SRC" "$OUT"
JOBS="$(nproc 2>/dev/null || echo 2)"

fetch() { # url dest-dir
	local url="$1" dest="$2"
	if [ -d "$dest" ]; then return; fi
	mkdir -p "$dest"
	echo "==> Downloading $url"
	local tmp; tmp="$(mktemp)"
	local ok=0
	for attempt in 1 2 3 4 5; do
		if curl -fsSL --retry 3 --retry-all-errors --retry-delay 3 -o "$tmp" "$url" && tar -tzf "$tmp" >/dev/null 2>&1; then ok=1; break; fi
		echo "download failed (attempt $attempt), retrying" >&2; sleep 5
	done
	[ "$ok" = 1 ] || { rm -rf "$dest" "$tmp"; echo "could not download $url" >&2; return 1; }
	tar -xzf "$tmp" --strip-components=1 -C "$dest"
	rm -f "$tmp"
}

cmake_build() { # srcdir builddir extra-args...
	local s="$1" b="$2"; shift 2
	cmake -S "$s" -B "$b" -G Ninja \
		-DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" \
		-DANDROID_ABI="$ABI" -DANDROID_PLATFORM="android-$API" -DANDROID_STL=c++_static \
		-DCMAKE_BUILD_TYPE=Release "$@"
	cmake --build "$b" -j "$JOBS"
}

# ---- SDL2 ----
fetch "https://github.com/libsdl-org/SDL/releases/download/release-$SDL_VER/SDL2-$SDL_VER.tar.gz" "$SRC/sdl2"
cmake_build "$SRC/sdl2" "$WORK/build-sdl2" -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST=OFF -DSDL_TESTS=OFF
cp -L "$WORK/build-sdl2/libSDL2.so" "$OUT/libSDL2.so"

# SDL's Java side (SDLActivity and friends) is compiled by build-java.sh.
rm -rf "$WORK/sdl-java"; mkdir -p "$WORK/sdl-java"
cp -r "$SRC/sdl2/android-project/app/src/main/java/." "$WORK/sdl-java/"

# ---- OpenAL Soft ----
fetch "https://github.com/kcat/openal-soft/archive/refs/tags/$OAL_VER.tar.gz" "$SRC/openal"
cmake_build "$SRC/openal" "$WORK/build-openal" -DLIBTYPE=SHARED \
	"-DCMAKE_CXX_FLAGS=-include cstdlib" \
	-DALSOFT_UTILS=OFF -DALSOFT_EXAMPLES=OFF -DALSOFT_TESTS=OFF -DALSOFT_INSTALL=OFF
cp -L "$WORK/build-openal/libopenal.so" "$OUT/libsoft_oal.so"

# ---- FreeType ----
fetch "https://github.com/freetype/freetype/archive/refs/tags/VER-${FT_VER//./-}.tar.gz" "$SRC/freetype"
cmake_build "$SRC/freetype" "$WORK/build-freetype" -DBUILD_SHARED_LIBS=ON \
	-DFT_DISABLE_ZLIB=ON -DFT_DISABLE_BZIP2=ON -DFT_DISABLE_PNG=ON -DFT_DISABLE_HARFBUZZ=ON -DFT_DISABLE_BROTLI=ON
FT_SO="$(find "$WORK/build-freetype" -maxdepth 1 -name 'libfreetype.so*' -type f | head -n1)"
[ -n "$FT_SO" ] || { echo "libfreetype.so not found" >&2; exit 1; }
cp -L "$FT_SO" "$OUT/libfreetype6.so"

# ---- Lua 5.1 (plain C, built directly) ----
fetch "https://www.lua.org/ftp/lua-$LUA_VER.tar.gz" "$SRC/lua"
mapfile -t LUA_SOURCES < <(find "$SRC/lua/src" -name '*.c' ! -name 'lua.c' ! -name 'luac.c' ! -name 'print.c' | sort)
"$CC" -shared -fPIC -O2 -DLUA_USE_POSIX -o "$OUT/liblua51.so" "${LUA_SOURCES[@]}" -lm

# ---- SDL_main bridge ----
"$CC" -shared -fPIC -O2 -o "$OUT/libopenramain.so" "$ROOT/native/src/openramain.c" -llog

echo "==> Built libraries:"
for f in "$OUT"/*.so; do
	echo "--- $(basename "$f")"
	file "$f" | sed 's/^/    /'
	"$BIN/llvm-readelf" -d "$f" | grep -E 'NEEDED|SONAME' | sed 's/^/    /' || true
done
