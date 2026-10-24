#!/usr/bin/env bash
# Native arm64 launch harness for the modded Valheim spike (issue #34).
# Launches the game the way BepInEx 5.4.23.6's run_bepinex.sh does and collects the evidence.
# Reads the Steam install, never writes to it, and never runs anything it did not find in place.

set -u

usage() {
    cat <<'EOF'
Usage: arm64-spike.sh --game <Valheim folder> --profile <profile folder> [options]

  --game DIR             Valheim install folder (the one holding valheim.app), or the .app itself
  --profile DIR          profile folder: libdoorstop.dylib and BepInEx/ at its root
  --label NAME           name for this run, used in the result folder (default: run)
  --out DIR              where the result folder is created (default: current directory)
  --archpreference LIST  ARCHPREFERENCE for arch (default: arm64,x86_64; use x86_64 for a Rosetta control run)
  --insert-absolute      put the absolute library path in DYLD_INSERT_LIBRARIES instead of the bare name
  --steam-bin DIR        folder with steamloader.dylib / gameoverlayrenderer.dylib
  --no-overlay           do not add the Steam overlay libraries
  --no-steam-start       do not ask Steam to start
  --timeout SECONDS      terminate the game after this long (default: wait until it exits)
  --dry-run              run the checks and print the launch command, but do not start the game
  -h, --help             this text
EOF
}

GAME_IN=""
PROFILE_IN=""
LABEL="run"
OUT_PARENT="."
ARCHPREF="arm64,x86_64"
INSERT_ABSOLUTE=0
STEAM_BIN="${HOME}/Library/Application Support/Steam/Steam.AppBundle/Steam/Contents/MacOS"
NO_OVERLAY=0
NO_STEAM_START=0
TIMEOUT=0
DRY_RUN=0
STEAM_APP_ID="892970"

while [ $# -gt 0 ]; do
    case "$1" in
        --game) GAME_IN="${2:-}"; shift 2 ;;
        --profile) PROFILE_IN="${2:-}"; shift 2 ;;
        --label) LABEL="${2:-}"; shift 2 ;;
        --out) OUT_PARENT="${2:-}"; shift 2 ;;
        --archpreference) ARCHPREF="${2:-}"; shift 2 ;;
        --insert-absolute) INSERT_ABSOLUTE=1; shift ;;
        --steam-bin) STEAM_BIN="${2:-}"; shift 2 ;;
        --no-overlay) NO_OVERLAY=1; shift ;;
        --no-steam-start) NO_STEAM_START=1; shift ;;
        --timeout) TIMEOUT="${2:-0}"; shift 2 ;;
        --dry-run) DRY_RUN=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

[ -n "$GAME_IN" ] && [ -n "$PROFILE_IN" ] || { usage >&2; exit 2; }
case "$TIMEOUT" in ''|*[!0-9]*) echo "--timeout needs a number of seconds" >&2; exit 2 ;; esac

have() { command -v "$1" >/dev/null 2>&1; }
abs_dir() { ( cd "$1" 2>/dev/null && pwd -P ); }

if [ "$(uname -s)" != "Darwin" ] && [ "$DRY_RUN" -ne 1 ]; then
    echo "This harness launches a macOS game; run it on the Mac (or use --dry-run to only check paths)." >&2
    exit 2
fi

GAME_DIR="$(abs_dir "$GAME_IN")" || true
PROFILE_DIR="$(abs_dir "$PROFILE_IN")" || true
[ -n "$GAME_DIR" ] || { echo "Game folder not found: $GAME_IN" >&2; exit 2; }
[ -n "$PROFILE_DIR" ] || { echo "Profile folder not found: $PROFILE_IN" >&2; exit 2; }

case "$GAME_DIR" in
    *.app) GAME_APP="$GAME_DIR"; GAME_DIR="$(dirname "$GAME_APP")" ;;
    *)
        GAME_APP=""
        for name in valheim.app Valheim.app; do
            [ -d "$GAME_DIR/$name" ] && { GAME_APP="$GAME_DIR/$name"; break; }
        done
        ;;
esac
[ -n "$GAME_APP" ] && [ -d "$GAME_APP/Contents/MacOS" ] || { echo "No valheim.app found in the game folder." >&2; exit 2; }

bundle_executable() {
    local name=""
    if have defaults; then
        name="$(defaults read "$GAME_APP/Contents/Info" CFBundleExecutable 2>/dev/null)"
    fi
    if [ -z "$name" ] && [ -x /usr/libexec/PlistBuddy ]; then
        name="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$GAME_APP/Contents/Info.plist" 2>/dev/null)"
    fi
    if [ -z "$name" ]; then
        local only
        only="$(ls -1 "$GAME_APP/Contents/MacOS")"
        [ "$(printf '%s\n' "$only" | wc -l | tr -d ' ')" = "1" ] && name="$only"
    fi
    [ -n "$name" ] && [ -f "$GAME_APP/Contents/MacOS/$name" ] && printf '%s' "$GAME_APP/Contents/MacOS/$name"
}

EXE="$(bundle_executable)" || true
[ -n "$EXE" ] || { echo "Could not resolve the game executable inside the .app." >&2; exit 2; }

TS="$(date +%Y%m%d-%H%M%S)"
SAFE_LABEL="$(printf '%s' "$LABEL" | tr -c 'A-Za-z0-9._-' '_')"
mkdir -p "$OUT_PARENT" || exit 2
OUT="$(abs_dir "$OUT_PARENT")/arm64-spike-${SAFE_LABEL}-${TS}"
mkdir -p "$OUT" || exit 2

WORK="$(mktemp -d "${TMPDIR:-/tmp}/arm64-spike.XXXXXX")" || exit 2
mkdir -p "$WORK/raw"
trap 'rm -rf "$WORK"' EXIT
RAW="$WORK/raw/summary.txt"
: > "$RAW"
STAMP="$WORK/stamp"
: > "$STAMP"

note() { printf '%s\n' "$*" >> "$RAW"; }
say() { printf '%s\n' "$*"; note "$*"; }
sec() { say ""; say "== $* =="; }
cap() { note "\$ $*"; "$@" >> "$RAW" 2>&1; note "[exit $?]"; }

esc() { printf '%s' "$1" | sed 's/[][\.*^$/]/\\&/g'; }
REDACT="$WORK/redact.sed"
: > "$REDACT"
redact_add() { [ -n "$1" ] && printf 's/%s/%s/g\n' "$(esc "$1")" "$2" >> "$REDACT"; return 0; }
redact_add "$GAME_DIR" '<GAME>'
redact_add "$PROFILE_DIR" '<PROFILE>'
redact_add "$OUT" '<OUT>'
redact_add "$GAME_IN" '<GAME>'
redact_add "$PROFILE_IN" '<PROFILE>'
redact_add "${TMPDIR:-/tmp}" '<TMP>'
redact_add "$HOME" '~'
redact_file() { LC_ALL=C sed -f "$REDACT" "$1" > "$2"; }
redact_stream() { LC_ALL=C sed -f "$REDACT"; }

LIB_NAME="libdoorstop.dylib"
LIB_PATH="$PROFILE_DIR/$LIB_NAME"
PRELOADER="$PROFILE_DIR/BepInEx/core/BepInEx.Preloader.dll"

sec "Machine"
say "date: $(date '+%Y-%m-%d %H:%M:%S %z')"
if have sw_vers; then cap sw_vers; else say "sw_vers: not available"; fi
say "uname -m: $(uname -m)"
have sysctl && say "cpu: $(sysctl -n machdep.cpu.brand_string 2>/dev/null || echo unknown)"
TRANSLATED="$(sysctl -n sysctl.proc_translated 2>/dev/null || echo n/a)"
say "this shell translated by Rosetta (sysctl.proc_translated): $TRANSLATED"
[ "$TRANSLATED" = "1" ] && say "WARNING: the terminal itself runs under Rosetta; children inherit x86_64 unless ARCHPREFERENCE wins."

if have arch; then
    if arch -x86_64 /usr/bin/true >/dev/null 2>&1; then say "Rosetta (arch -x86_64 /usr/bin/true): works"; else say "Rosetta (arch -x86_64 /usr/bin/true): NOT working"; fi
    if arch -e SPIKE_PROBE=1 /usr/bin/true >/dev/null 2>&1; then say "arch -e VAR=value: supported"; else say "arch -e VAR=value: NOT supported on this macOS"; fi
else
    say "arch: not available"
fi
have pkgutil && say "Rosetta package receipt: $(pkgutil --pkg-info com.apple.pkg.RosettaUpdateAuto >/dev/null 2>&1 && echo present || echo absent)"
say "Rosetta runtime file: $([ -e /Library/Apple/usr/libexec/oah/libRosettaRuntime ] && echo present || echo absent)"

sec "Game"
say "executable: $EXE"
if have lipo; then say "lipo -archs (game): $(lipo -archs "$EXE" 2>&1)"; fi
if have file; then say "file (game): $(file -b "$EXE")"; fi
if have codesign; then
    note "\$ codesign -d --entitlements :- <app>"
    codesign -d --entitlements :- "$GAME_APP" >> "$RAW" 2>&1
    note "\$ codesign -dv <app>"
    codesign -dv "$GAME_APP" >> "$RAW" 2>&1
fi

sec "Profile"
say "libdoorstop.dylib: $([ -f "$LIB_PATH" ] && echo present || echo MISSING)"
say "BepInEx.Preloader.dll: $([ -f "$PRELOADER" ] && echo present || echo MISSING)"
if [ ! -f "$LIB_PATH" ] && [ -f "$PROFILE_DIR/doorstop_libs/libdoorstop_x64.dylib" ]; then
    say "NOTE: only the old doorstop_libs/libdoorstop_x64.dylib layout is present; this harness needs libdoorstop.dylib at the profile root."
fi
if [ -f "$LIB_PATH" ] && have lipo; then say "lipo -archs (libdoorstop.dylib): $(lipo -archs "$LIB_PATH" 2>&1)"; fi
for f in .doorstop_version changelog.txt; do
    [ -f "$PROFILE_DIR/$f" ] && { say "$f:"; head -n 3 "$PROFILE_DIR/$f" >> "$RAW"; head -n 3 "$PROFILE_DIR/$f"; }
done
if have xattr; then
    quarantined="$(xattr -r "$PROFILE_DIR" 2>/dev/null | grep -c 'com.apple.quarantine')"
    say "files with com.apple.quarantine under the profile: ${quarantined:-0}"
fi
if have codesign && [ -f "$LIB_PATH" ]; then
    note "\$ codesign -v libdoorstop.dylib"
    codesign -v "$LIB_PATH" >> "$RAW" 2>&1
    note "[exit $?]"
fi

HASHES="$WORK/raw/file-hashes.txt"
hash_cmd() { if have shasum; then shasum -a 256 "$@"; elif have sha256sum; then sha256sum "$@"; else echo "no sha256 tool"; fi; }
(
    cd "$PROFILE_DIR" || exit 0
    for f in libdoorstop.dylib BepInEx/core/BepInEx.dll BepInEx/core/BepInEx.Preloader.dll BepInEx/core/0Harmony.dll \
             BepInEx/core/MonoMod.RuntimeDetour.dll BepInEx/core/HarmonyXInterop.dll; do
        [ -f "$f" ] && hash_cmd "$f"
    done
) > "$HASHES" 2>&1

PLUGINS="$WORK/raw/profile-plugins.txt"
{
    for d in plugins patchers monomod; do
        [ -d "$PROFILE_DIR/BepInEx/$d" ] || continue
        echo "# BepInEx/$d"
        ls -1 "$PROFILE_DIR/BepInEx/$d"
    done
} > "$PLUGINS" 2>&1

NATIVE="$WORK/raw/native-libs.txt"
{
    echo "# architectures of every native library under the profile and the game bundle"
    if have lipo; then
        find "$PROFILE_DIR" "$GAME_APP" -type f \( -name '*.dylib' -o -name '*.so' -o -name '*.bundle' \) 2>/dev/null |
        while IFS= read -r lib; do
            archs="$(lipo -archs "$lib" 2>/dev/null)" || continue
            case " $archs " in
                *" arm64 "*|*" arm64e "*) tag="" ;;
                *) tag="  <-- NO arm64 SLICE" ;;
            esac
            printf '%s: %s%s\n' "$lib" "$archs" "$tag"
        done
    else
        echo "lipo not available"
    fi
} > "$NATIVE" 2>&1
say "native libraries without an arm64 slice: $(grep -c 'NO arm64 SLICE' "$NATIVE")"

INSERT_LIBS="$LIB_NAME"
[ "$INSERT_ABSOLUTE" -eq 1 ] && INSERT_LIBS="$LIB_PATH"
INHERITED="${DYLD_INSERT_LIBRARIES:-${STEAM_DYLD_INSERT_LIBRARIES:-}}"
OVERLAY=""
if [ -z "$INHERITED" ] && [ "$NO_OVERLAY" -ne 1 ]; then
    for name in steamloader.dylib gameoverlayrenderer.dylib; do
        [ -f "$STEAM_BIN/$name" ] && OVERLAY="${OVERLAY:+$OVERLAY:}$STEAM_BIN/$name"
    done
fi
EXTRA="${INHERITED:-$OVERLAY}"
[ -n "$EXTRA" ] && INSERT_LIBS="$INSERT_LIBS:$EXTRA"
DYLD_LIB="$PROFILE_DIR/:${DYLD_LIBRARY_PATH:-}"

sec "Launch plan"
say "ARCHPREFERENCE=$ARCHPREF"
say "DYLD_LIBRARY_PATH=$DYLD_LIB"
say "DYLD_INSERT_LIBRARIES=$INSERT_LIBS"
say "inherited insert libraries: ${INHERITED:-none}; overlay libraries added by the harness: ${OVERLAY:-none}"
say "DOORSTOP_TARGET_ASSEMBLY=$PRELOADER"
say "SteamAppId=SteamGameId=SteamOverlayGameId=$STEAM_APP_ID, SteamEnv=1, SteamClientLaunch=1"
say "working directory: $GAME_DIR"
say "command: arch -e DYLD_LIBRARY_PATH=... -e DYLD_INSERT_LIBRARIES=... $EXE"

MISSING=0
[ -f "$LIB_PATH" ] || MISSING=1
[ -f "$PRELOADER" ] || MISSING=1

if [ "$DRY_RUN" -eq 1 ] || [ "$MISSING" -eq 1 ]; then
    [ "$MISSING" -eq 1 ] && say "NOT LAUNCHING: libdoorstop.dylib or BepInEx.Preloader.dll is missing from the profile."
    [ "$DRY_RUN" -eq 1 ] && say "Dry run: the game was not started."
    RC="not started"
else
    if [ "$NO_STEAM_START" -ne 1 ] && have open; then
        open -g -b com.valvesoftware.steam >/dev/null 2>&1
        say "asked Steam to start; waiting 5 seconds"
        sleep 5
    fi

    GAME_OUT="$WORK/raw/doorstop-output.txt"
    cd "$GAME_DIR" || exit 2
    (
        export DOORSTOP_ENABLED=1
        export DOORSTOP_TARGET_ASSEMBLY="$PRELOADER"
        export DOORSTOP_BOOT_CONFIG_OVERRIDE=""
        export DOORSTOP_IGNORE_DISABLED_ENV=0
        export DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE=""
        export DOORSTOP_MONO_DEBUG_ENABLED=0
        export DOORSTOP_MONO_DEBUG_ADDRESS="127.0.0.1:10000"
        export DOORSTOP_MONO_DEBUG_SUSPEND=0
        export DOORSTOP_CLR_RUNTIME_CORECLR_PATH=".dylib"
        export DOORSTOP_CLR_CORLIB_DIR=""
        export SteamAppId="$STEAM_APP_ID"
        export SteamGameId="$STEAM_APP_ID"
        export SteamOverlayGameId="$STEAM_APP_ID"
        export SteamEnv=1
        export SteamClientLaunch=1
        export ARCHPREFERENCE="$ARCHPREF"
        exec arch -e "DYLD_LIBRARY_PATH=$DYLD_LIB" -e "DYLD_INSERT_LIBRARIES=$INSERT_LIBS" "$EXE"
    ) > "$GAME_OUT" 2>&1 &
    PID=$!
    say "started, pid $PID; play until the main menu or a world, then quit the game (Ctrl-C here stops waiting and collects what is there)"

    INTERRUPTED=0
    trap 'INTERRUPTED=1' INT

    sample() {
        local f="$WORK/raw/process-$1.txt" flags
        {
            echo "# sampled $1 after about ${2}s"
            echo "# ps"
            ps -p "$PID" -o pid,ppid,flags,stat,etime,comm 2>&1
            echo "# ps command"
            ps -p "$PID" -o command= 2>&1
            echo "# vmmap header"
            if have vmmap; then vmmap -summary "$PID" 2>&1 | head -n 40; else echo "vmmap not available"; fi
            echo "# mapped libraries of interest"
            if have lsof; then
                lsof -p "$PID" 2>/dev/null | grep -Ei 'rosetta|/oah/|doorstop|libmono|libsteam|gameoverlay|steamloader|BepInEx' |
                    sed 's/^[^/]*\//\//' | sort -u
            else
                echo "lsof not available"
            fi
        } > "$f" 2>&1
        flags="$(ps -p "$PID" -o flags= 2>/dev/null | tr -d ' ')"
        {
            echo "# verdict signals ($1)"
            case "$flags" in
                ''|*[!0-9a-fA-F]*) echo "p_flag: unreadable (${flags:-empty})" ;;
                *) if [ $(( 0x$flags & 0x20000 )) -ne 0 ]; then echo "p_flag $flags: P_TRANSLATED set (running under Rosetta)"; else echo "p_flag $flags: P_TRANSLATED clear (native)"; fi ;;
            esac
            grep -i 'code type' "$f" | sed 's/^/vmmap: /' || true
            if grep -qiE 'rosetta|/oah/' "$f"; then echo "lsof: Rosetta runtime is mapped"; else echo "lsof: no Rosetta runtime mapped"; fi
        } >> "$f"
        grep -A4 '^# verdict' "$f" | while IFS= read -r line; do say "[$1] $line"; done
    }

    elapsed=0
    while kill -0 "$PID" 2>/dev/null; do
        sleep 1
        elapsed=$((elapsed + 1))
        [ "$elapsed" -eq 3 ] && sample early 3
        [ "$elapsed" -eq 45 ] && sample late 45
        if [ "$INTERRUPTED" -eq 1 ]; then say "interrupted; the game is still running"; break; fi
        if [ "$TIMEOUT" -gt 0 ] && [ "$elapsed" -ge "$TIMEOUT" ]; then
            say "timeout reached; terminating the game"
            kill -TERM "$PID" 2>/dev/null
            break
        fi
    done
    if kill -0 "$PID" 2>/dev/null; then
        RC="still running or terminated by the harness"
    else
        wait "$PID"; RC="exit status $?"
    fi
    [ -f "$WORK/raw/process-early.txt" ] || { [ "$elapsed" -lt 3 ] && say "the game exited within 3 seconds, no process sample was possible"; }
    say "game finished after about ${elapsed}s: $RC"
fi

sec "Collecting"
collect() {
    local src="$1" name="$2"
    if [ -f "$src" ] && [ "$src" -nt "$STAMP" ]; then
        redact_file "$src" "$OUT/$name"
        say "collected $name"
    else
        say "not produced by this run: $name"
    fi
}

BEPINEX_LOG="$PROFILE_DIR/BepInEx/LogOutput.log"
[ -f "$BEPINEX_LOG" ] || BEPINEX_LOG="$GAME_DIR/BepInEx/LogOutput.log"
collect "$BEPINEX_LOG" "LogOutput.log"
collect "$WORK/raw/doorstop-output.txt" "doorstop-output.txt"
for p in "$HOME/Library/Logs/IronGate/Valheim/Player.log" "$HOME/Library/Logs/Unity/Player.log"; do
    if [ -f "$p" ] && [ "$p" -nt "$STAMP" ]; then collect "$p" "Player.log"; break; fi
done
for f in "$WORK/raw/process-early.txt" "$WORK/raw/process-late.txt"; do
    [ -f "$f" ] && collect "$f" "$(basename "$f")"
done

mkdir -p "$OUT/crash-reports"
for d in "$HOME/Library/Logs/DiagnosticReports" /Library/Logs/DiagnosticReports; do
    [ -d "$d" ] || continue
    for f in "$d"/*alheim*; do
        [ -f "$f" ] && [ "$f" -nt "$STAMP" ] && redact_file "$f" "$OUT/crash-reports/$(basename "$f")"
    done
done
rmdir "$OUT/crash-reports" 2>/dev/null || say "collected crash reports: $(ls -1 "$OUT/crash-reports" | wc -l | tr -d ' ')"

redact_file "$HASHES" "$OUT/file-hashes.txt"
redact_file "$PLUGINS" "$OUT/profile-plugins.txt"
redact_file "$NATIVE" "$OUT/native-libs.txt"

WRITES="$WORK/raw/game-folder-writes.txt"
find "$GAME_DIR" -newer "$STAMP" 2>/dev/null | head -n 200 > "$WRITES"
redact_file "$WRITES" "$OUT/game-folder-writes.txt"
say "files in the game folder modified during this run: $(wc -l < "$WRITES" | tr -d ' ') (see game-folder-writes.txt)"

say ""
say "result folder: $OUT"
redact_file "$RAW" "$OUT/summary.txt"
echo "Review $OUT before sharing: paths are redacted, but check the logs for anything else personal."
