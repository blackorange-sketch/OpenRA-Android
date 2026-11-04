#!/usr/bin/env bash
# Compiles SDL's Java sources plus our activity into a jar that the .NET
# Android project embeds (AndroidJavaLibrary). Run build-native.sh first.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
[ -d "$SDK/platforms" ] || { echo "ANDROID_HOME is not set or has no platforms/" >&2; exit 1; }

ANDROID_JAR="$(ls -d "$SDK"/platforms/android-*/android.jar | sort -t- -k2 -n | tail -n1)"
echo "==> Compiling against $ANDROID_JAR"

JAVA_SRC="$ROOT/native-build/sdl-java"
[ -d "$JAVA_SRC/org/libsdl/app" ] || { echo "SDL Java sources missing; run build-native.sh first" >&2; exit 1; }

CLASSES="$ROOT/native-build/java-classes"
rm -rf "$CLASSES"; mkdir -p "$CLASSES"

mapfile -t FILES < <(find "$JAVA_SRC" "$ROOT/android/OpenRA.Android/java" -name '*.java' | sort)
javac -nowarn -source 8 -target 8 -classpath "$ANDROID_JAR" -d "$CLASSES" "${FILES[@]}"

JAR="$ROOT/android/OpenRA.Android/libs/openra-java.jar"
mkdir -p "$(dirname "$JAR")"
jar cf "$JAR" -C "$CLASSES" .
echo "==> Wrote $JAR"
unzip -l "$JAR" | tail -n 5
