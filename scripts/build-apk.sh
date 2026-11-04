#!/usr/bin/env bash
# Publishes the signed APK. Tries the prebuilt-jar packaging first and falls back to
# letting .NET compile the Java sources, so one CI run can tell us which one works.
# Needs KEYSTORE (path) in the environment.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/android/OpenRA.Android"
: "${KEYSTORE:?KEYSTORE not set}"

for mode in jar source; do
	echo "==> Java packaging mode: $mode"
	rm -rf "$PROJ/obj" "$PROJ/bin"
	if dotnet publish "$PROJ/OpenRA.Android.csproj" -c Release -p:JavaMode="$mode" \
		-p:InstallAndroidDependencies=true -p:AcceptAndroidSDKLicenses=True \
		-p:AndroidKeyStore=true -p:AndroidSigningKeyStore="$KEYSTORE" \
		-p:AndroidSigningStorePass=openra-dev -p:AndroidSigningKeyAlias=openra -p:AndroidSigningKeyPass=openra-dev \
		2>&1 | tee "$ROOT/apk-build-$mode.log"; then
		APK="$(find "$PROJ/bin" -name '*-Signed.apk' | head -n 1)"
		if [ -n "$APK" ]; then
			mkdir -p "$ROOT/dist" && cp "$APK" "$ROOT/dist/OpenRA-Android.apk"
			echo "==> APK built with mode '$mode': $APK"
			unzip -l "$ROOT/dist/OpenRA-Android.apk" | grep -E 'lib/|assets/|classes' || true
			exit 0
		fi
		echo "Build succeeded but no *-Signed.apk found" >&2
	fi
done

echo "Both Java packaging modes failed" >&2
exit 1
