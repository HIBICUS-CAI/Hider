#!/usr/bin/env bash
# Linux strip capture end to end without a GNOME desktop (docs/manual-tests.md, "作業コンテナでの確認"):
# Xvfb + xcompmgr (per-pixel alpha needs a compositor) + a private session bus + tools/fakeportal, then Hider restores one
# mosaic area (240 px high, strips of 8 px) over a test image. Passes when every strip was grabbed, no screenshot file
# was left (FR-53) and the area shows the image's pixelated colours; then Hider is sent SIGTERM and must exit with 0,
# having written areas.json again (FR-06, FR-61).
#
#   scripts/xvfb-strip-smoke.sh [--screencast] [--deny] [path/to/Hider or Hider.dll]
#
# Default: stage 1 (Screenshot portal). --screencast: stage 2 — a private PipeWire + WirePlumber, the X screen as a
# PipeWire video node (GStreamer ximagesrc ! pipewiresink), the fake portal's ScreenCast and libhidercap (built here and
# found through LD_LIBRARY_PATH); passes only when every strip came through ScreenCast and a restore token was stored.
# --deny: the portal refuses (as when the user cancels its dialog): passes when the refusal is logged, nothing was shot,
# and the area fell back to a generated pattern that areas.json keeps (FR-52); with --screencast both portals refuse.
# Needs: Xvfb, xcompmgr, ImageMagick (convert, display, import), dbus-daemon, python3; for --screencast also pipewire,
# wireplumber, gstreamer1.0-pipewire, gstreamer1.0-plugins-good, gstreamer1.0-tools, libpipewire-0.3-dev, make.
set -euo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
screencast=0
deny=0
while [ $# -gt 0 ]; do
    case "$1" in
        --screencast) screencast=1 ;;
        --deny) deny=1 ;;
        *) break ;;
    esac
    shift
done
hider=${1:-$repo/src/Hider/bin/Release/net10.0/Hider.dll}
work=$(mktemp -d)
# The area: 240 rows in strips of 8 (areas.json below). Stage 1 also takes one screenshot before the first strip
# (PortalScreenshotGrabber.PrepareAsync); stage 2 opens its session there instead. Refused, only that first request is
# made.
area_height=240
strip_px=8
strips=$((area_height / strip_px))
if [ $deny = 1 ]; then
    expected_grabs=1
elif [ $screencast = 1 ]; then
    expected_grabs=$strips
else
    expected_grabs=$((strips + 1))
fi
areas_json="$work/home/.hider/areas.json"
# Every step may fail (a process already gone): the temporary directory goes first and the trap never stops early.
cleanup() {
    rm -rf "$work"
    jobs -p | xargs -r kill 2>/dev/null || true
    if [ -n "${bus_pid:-}" ]; then kill "$bus_pid" 2>/dev/null || true; fi
}
trap cleanup EXIT

# Waits poll (every 0.1 s, at most the given number of times) instead of sleeping for a fixed time.
wait_for() {
    local what=$1 tries=$2
    shift 2
    for _ in $(seq "$tries"); do
        if "$@"; then return 0; fi
        sleep 0.1
    done
    echo "$what"
    return 1
}
quiet() { "$@" >/dev/null 2>&1; }
not() { ! "$@"; }

dotnet build "$repo/tools/fakeportal" -c Release -o "$work/portal" >/dev/null
mkdir -p "$work/home/.hider" "$work/shots"
cat > "$areas_json" <<JSON
{ "version": 1, "areas": [ { "id": "0f3b8d2e", "x": 200, "y": 150, "width": 400, "height": $area_height, "opacity": 1.0, "pattern": "mosaic",
  "mosaic": { "method": "pixelate", "common": { "intervalMs": 300000, "stripPx": $strip_px }, "params": { "pixelate": { "block": 12 } } } } ] }
JSON
convert -size 1280x720 gradient:red-blue \( -size 160x90 pattern:checkerboard -scale 800% \) -compose multiply -composite "$work/bg.png"

# A free display number from Xvfb itself, written once it accepts connections.
Xvfb -displayfd 9 -screen 0 1280x720x24 9>"$work/display" >/dev/null 2>&1 &
wait_for "Xvfb did not start" 50 [ -s "$work/display" ]
DISPLAY=":$(tr -d '[:space:]' < "$work/display")"
export DISPLAY
xcompmgr >/dev/null 2>&1 &
display -geometry +0+0 -backdrop "$work/bg.png" >/dev/null 2>&1 &
read -r DBUS_SESSION_BUS_ADDRESS bus_pid < <(dbus-daemon --session --fork --print-address=1 --print-pid=1 | paste -sd' ')
export DBUS_SESSION_BUS_ADDRESS

portal_args=("$work/shots" "$( [ $deny = 1 ] && echo deny || echo ok )")
if [ $screencast = 1 ]; then
    make -s -C "$repo/native/hidercap" >/dev/null
    rid=$( [ "$(uname -m)" = aarch64 ] && echo linux-arm64 || echo linux-x64 )
    export LD_LIBRARY_PATH="$repo/native/hidercap/out/$rid${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    export XDG_RUNTIME_DIR="$work/run"
    mkdir -m 700 "$XDG_RUNTIME_DIR"
    pipewire > "$work/pipewire.log" 2>&1 &
    wait_for "PipeWire did not start" 100 quiet pw-cli info 0 || { cat "$work/pipewire.log"; exit 1; }
    wireplumber > "$work/wireplumber.log" 2>&1 &
    # The session manager links the streams; it is ready once it has connected as a client.
    wireplumber_ready() { pw-cli ls Client 2>/dev/null | grep -q 'application.name = "WirePlumber"'; }
    wait_for "WirePlumber did not connect" 100 wireplumber_ready || { cat "$work/wireplumber.log"; exit 1; }
    gst-launch-1.0 -q ximagesrc use-damage=false ! video/x-raw,framerate=30/1 ! videoconvert ! video/x-raw,format=BGRx \
        ! pipewiresink mode=provide stream-properties="p,media.class=Video/Source,node.name=hider-smoke-screen" > "$work/gst.log" 2>&1 &
    node=""
    screen_node() {
        node=$(pw-cli ls Node 2>/dev/null | awk '/^\tid /{id=$2} /node.name = "hider-smoke-screen"/{gsub(",","",id); print id}' | head -1)
        [ -n "$node" ]
    }
    wait_for "no PipeWire node for the X screen" 100 screen_node || { cat "$work/gst.log"; exit 1; }
    portal_args+=("$node")
    export FAKEPORTAL_WIDTH=1280 FAKEPORTAL_HEIGHT=720
fi
dotnet "$work/portal/FakePortal.dll" "${portal_args[@]}" > "$work/portal.log" 2>&1 &
wait_for "the fake portal did not start" 100 grep -q '^fake portal ready$' "$work/portal.log" || { cat "$work/portal.log"; exit 1; }

if [[ "$hider" == *.dll ]]; then run=(dotnet "$hider"); else run=("$hider"); fi
HOME="$work/home" "${run[@]}" --verbose > "$work/hider.log" 2>&1 &
hider_pid=$!
grabbed() { echo $(( $(grep -c '^shot' "$work/portal.log" || true) + $(grep -c 'screencast: frame' "$work/hider.log" || true) )); }
for _ in $(seq 60); do
    [ "$(grabbed)" -ge "$expected_grabs" ] && break
    sleep 0.5
done
if [ $deny = 1 ]; then
    wait_for "no fallback after the refusal" 100 grep -q 'capture failed (Denied)' "$work/hider.log" || true
fi
sleep 2
shots=$(grep -c '^shot' "$work/portal.log" || true)
refused_shots=$(grep -c '^shot .* code=1$' "$work/portal.log" || true)
frames=$(grep -c 'screencast: frame' "$work/hider.log" || true)
left=$(find "$work/shots" -type f | wc -l)
import -window root "$work/after.png"
# Pixel (230, 200) lies in the 12 px cell starting at area-local (24, 48) = screen (224, 198): it should show that cell's
# average of the image (pyramid approximation: within a few levels).
rgb='%[fx:round(255*r)] %[fx:round(255*g)] %[fx:round(255*b)]\n'
read -r ir ig ib < <(convert "$work/after.png" -crop 1x1+230+200 -format "$rgb" info:)
read -r er eg eb < <(convert "$work/bg.png" -crop 12x12+224+198 -scale 1x1\! -format "$rgb" info:)

# SIGTERM (a session ending, systemd stopping the user's services): Hider saves and exits with 0. areas.json gets an
# mtime of 0 first, so only a new write gives it another one.
saves_before=$(grep -c 'saved 1 area(s)' "$work/hider.log" || true)
touch -d @0 "$areas_json"
kill -TERM "$hider_pid"
# Ended: gone, or a zombie waiting for the wait below.
hider_ended() { [ ! -e "/proc/$hider_pid" ] || [ "$(sed 's/.*) //' "/proc/$hider_pid/stat" | cut -d ' ' -f 1)" = Z ]; }
wait_for "Hider still runs 15 s after SIGTERM" 150 hider_ended || kill -KILL "$hider_pid" 2>/dev/null || true
hider_code=0
wait "$hider_pid" || hider_code=$?
saves_after=$(grep -c 'saved 1 area(s)' "$work/hider.log" || true)

echo "screenshots: $shots ($refused_shots refused), screencast frames: $frames ($strips strips; stage 1 adds one screenshot before them), files left: $left, area pixel: $ir $ig $ib, image cell average: $er $eg $eb, exit code after SIGTERM: $hider_code"
grep -v '^$' "$work/hider.log" | grep -v 'screencast: frame' | tail -6
# Every check fails the script on its own (set -e does not stop on a failed command inside an && list or after "!").
failures=0
check() {
    local name=$1
    shift
    if ! "$@"; then
        echo "FAIL: $name"
        failures=$((failures + 1))
    fi
}
close() { local d=$(($1 - $2)); [ "${d#-}" -le 6 ]; }
# Two "r g b" triples, each channel within 6.
close3() {
    local a b
    read -r -a a <<< "$1"
    read -r -a b <<< "$2"
    close "${a[0]}" "${b[0]}" && close "${a[1]}" "${b[1]}" && close "${a[2]}" "${b[2]}"
}
# areas.json after the exit: the one area, where it was, with the given pattern (the mosaic settings kept).
saved_area() {
    python3 -c '
import json, sys
state = json.load(open(sys.argv[1], encoding="utf-8"))
(area,) = state["areas"]
place = (area["x"], area["y"], area["width"], area["height"])
ok = state["version"] == 1 and area["id"] == "0f3b8d2e" and place == (200, 150, 400, 240)
sys.exit(0 if ok and area["pattern"] == sys.argv[2] and area["mosaic"]["method"] == "pixelate" else 1)
' "$areas_json" "$1"
}
check "no screenshot file left (FR-53)" [ "$left" -eq 0 ]
# Avalonia asks for the Settings portal's version (its theme); everything of the capture portals must be offered.
check "the capture portals answered every call" not grep -Eq '^unknown method .*portal\.(Screenshot|ScreenCast|Request|Session)\b' "$work/portal.log"
if [ $deny = 1 ]; then
    check "one screenshot request" [ "$shots" -eq 1 ]
    check "the screenshot refused" [ "$refused_shots" -eq 1 ]
    check "no frame taken" [ "$frames" -eq 0 ]
    check "the refusal logged" grep -q 'strip capture denied: .*cancelled by the user' "$work/hider.log"
    check "fell back to a generated pattern (FR-52)" grep -q 'capture failed (Denied) on .*, switching to a generated pattern' "$work/hider.log"
    check "the area no longer shows the image" not close3 "$ir $ig $ib" "$er $eg $eb"
    if [ $screencast = 1 ]; then
        check "ScreenCast asked first" grep -q '^select sources' "$work/portal.log"
        check "no restore token from a refused ScreenCast" [ ! -e "$work/home/.hider/restore_token" ]
    fi
else
    check "all $strips strips grabbed" [ $((shots + frames)) -eq "$expected_grabs" ]
    check "no failed capture in the log" not grep -q "capture failed" "$work/hider.log"
    if [ $screencast = 1 ]; then
        check "every strip through ScreenCast" [ "$frames" -eq "$strips" ]
        check "restore token stored" [ -s "$work/home/.hider/restore_token" ]
    fi
    check "red within 6 of the cell average" close "$ir" "$er"
    check "green within 6 of the cell average" close "$ig" "$eg"
    check "blue within 6 of the cell average" close "$ib" "$eb"
fi
check "exit code 0 after SIGTERM (FR-06)" [ "$hider_code" -eq 0 ]
check "areas.json written again on SIGTERM" [ "$(stat -c %Y "$areas_json")" != 0 ]
check "saved once more on SIGTERM" [ "$saves_after" -eq $((saves_before + 1)) ]
check "areas.json holds the area" saved_area "$( [ $deny = 1 ] && echo pattern || echo mosaic )"
check "no unhandled exception" not grep -q 'Unhandled' "$work/hider.log"
if [ $failures -ne 0 ]; then
    echo "--- portal.log (without the shots)"
    grep -v '^shot' "$work/portal.log" | tail -20 || true
    exit 1
fi
echo "xvfb strip smoke$( [ $screencast = 1 ] && echo ' (screencast)')$( [ $deny = 1 ] && echo ' (denied)'): passed"
