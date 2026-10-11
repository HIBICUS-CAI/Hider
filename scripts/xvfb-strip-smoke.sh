#!/bin/bash
# Linux strip capture end to end without a GNOME desktop (docs/manual-tests.md, "作業コンテナでの確認"):
# Xvfb + xcompmgr (per-pixel alpha needs a compositor) + a private session bus + tools/fakeportal, then Hider restores one
# mosaic area (240 px high, strips of 8 px) over a test image. Passes when all 30 strips were grabbed, no screenshot file
# was left (FR-53) and the area shows the image's pixelated colours.
#
#   scripts/xvfb-strip-smoke.sh [--screencast] [path/to/Hider or Hider.dll]
#
# Default: stage 1 (Screenshot portal). --screencast: stage 2 — a private PipeWire + WirePlumber, the X screen as a
# PipeWire video node (GStreamer ximagesrc ! pipewiresink), the fake portal's ScreenCast and libhidercap (built here and
# found through LD_LIBRARY_PATH); passes only when every strip came through ScreenCast and a restore token was stored.
# Needs: Xvfb, xcompmgr, ImageMagick (convert, display, import), dbus-daemon; for --screencast also pipewire,
# wireplumber, gstreamer1.0-pipewire, gstreamer1.0-plugins-good, gstreamer1.0-tools, libpipewire-0.3-dev, make.
set -euo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
screencast=0
if [ "${1:-}" = "--screencast" ]; then
    screencast=1
    shift
fi
hider=${1:-$repo/src/Hider/bin/Release/net10.0/Hider.dll}
work=$(mktemp -d)
display_no=:57
cleanup() { kill $(jobs -p) 2>/dev/null || true; [ -n "${bus_pid:-}" ] && kill "$bus_pid" 2>/dev/null; rm -rf "$work"; }
trap cleanup EXIT

dotnet build "$repo/tools/fakeportal" -c Release -o "$work/portal" >/dev/null
mkdir -p "$work/home/.hider" "$work/shots"
cat > "$work/home/.hider/areas.json" <<JSON
{ "version": 1, "areas": [ { "id": "0f3b8d2e", "x": 200, "y": 150, "width": 400, "height": 240, "opacity": 1.0, "pattern": "mosaic",
  "mosaic": { "method": "pixelate", "common": { "intervalMs": 300000, "stripPx": 8 }, "params": { "pixelate": { "block": 12 } } } } ] }
JSON
convert -size 1280x720 gradient:red-blue \( -size 160x90 pattern:checkerboard -scale 800% \) -compose multiply -composite "$work/bg.png"

Xvfb $display_no -screen 0 1280x720x24 >/dev/null 2>&1 &
sleep 1
export DISPLAY=$display_no
xcompmgr >/dev/null 2>&1 &
display -geometry +0+0 -backdrop "$work/bg.png" >/dev/null 2>&1 &
read -r DBUS_SESSION_BUS_ADDRESS bus_pid < <(dbus-daemon --session --fork --print-address=1 --print-pid=1 | paste -sd' ')
export DBUS_SESSION_BUS_ADDRESS

portal_args=("$work/shots" ok)
if [ $screencast = 1 ]; then
    make -s -C "$repo/native/hidercap" >/dev/null
    rid=$( [ "$(uname -m)" = aarch64 ] && echo linux-arm64 || echo linux-x64 )
    export LD_LIBRARY_PATH="$repo/native/hidercap/out/$rid${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    export XDG_RUNTIME_DIR="$work/run"
    mkdir -m 700 "$XDG_RUNTIME_DIR"
    pipewire > "$work/pipewire.log" 2>&1 &
    sleep 1
    wireplumber > "$work/wireplumber.log" 2>&1 &
    sleep 2
    gst-launch-1.0 -q ximagesrc use-damage=false ! video/x-raw,framerate=30/1 ! videoconvert ! video/x-raw,format=BGRx \
        ! pipewiresink mode=provide stream-properties="p,media.class=Video/Source,node.name=hider-smoke-screen" > "$work/gst.log" 2>&1 &
    node=""
    for _ in $(seq 20); do
        node=$(pw-cli ls Node 2>/dev/null | awk '/^\tid /{id=$2} /node.name = "hider-smoke-screen"/{gsub(",","",id); print id}' | head -1)
        [ -n "$node" ] && break
        sleep 0.5
    done
    [ -n "$node" ] || { echo "no PipeWire node for the X screen"; cat "$work/gst.log"; exit 1; }
    portal_args+=("$node")
    export FAKEPORTAL_WIDTH=1280 FAKEPORTAL_HEIGHT=720
fi
dotnet "$work/portal/FakePortal.dll" "${portal_args[@]}" > "$work/portal.log" 2>&1 &
sleep 1

if [[ "$hider" == *.dll ]]; then run=(dotnet "$hider"); else run=("$hider"); fi
HOME="$work/home" "${run[@]}" --verbose > "$work/hider.log" 2>&1 &
grabbed() { echo $(( $(grep -c '^shot' "$work/portal.log" || true) + $(grep -c 'screencast: frame' "$work/hider.log" || true) )); }
for _ in $(seq 60); do
    [ "$(grabbed)" -ge $((screencast == 1 ? 30 : 31)) ] && break
    sleep 0.5
done
sleep 2
shots=$(grep -c '^shot' "$work/portal.log" || true)
frames=$(grep -c 'screencast: frame' "$work/hider.log" || true)
left=$(ls "$work/shots" | wc -l)
import -window root "$work/after.png"
# Pixel (230, 200) lies in the 12 px cell starting at area-local (24, 48) = screen (224, 198): it should show that cell's
# average of the image (pyramid approximation: within a few levels).
rgb='%[fx:round(255*r)] %[fx:round(255*g)] %[fx:round(255*b)]\n'
read -r ir ig ib < <(convert "$work/after.png" -crop 1x1+230+200 -format "$rgb" info:)
read -r er eg eb < <(convert "$work/bg.png" -crop 12x12+224+198 -scale 1x1\! -format "$rgb" info:)
echo "screenshots: $shots, screencast frames: $frames (30 strips; stage 1 adds one screenshot before them), files left: $left, area pixel: $ir $ig $ib, image cell average: $er $eg $eb"
grep -v '^$' "$work/hider.log" | grep -v 'screencast: frame' | tail -4
# Every check fails the script on its own (set -e does not stop on a failed command inside an && list or after "!").
failures=0
check() { if ! eval "$2"; then echo "FAIL: $1"; failures=$((failures + 1)); fi; }
close() { local d=$(( $1 - $2 )); [ ${d#-} -le 6 ]; }
# Stage 1 takes one extra screenshot before the first strip (PortalScreenshotGrabber.PrepareAsync: a permission dialog must
# not wait with a strip transparent); stage 2 opens its session there instead.
check "all 30 strips grabbed" '[ $((shots + frames)) -eq $((screencast == 1 ? 30 : 31)) ]'
check "no screenshot file left (FR-53)" '[ "$left" -eq 0 ]'
check "no failed capture in the log" '! grep -q "capture failed" "$work/hider.log"'
if [ $screencast = 1 ]; then
    check "every strip through ScreenCast" '[ "$frames" -eq 30 ]'
    check "restore token stored" '[ -s "$work/home/.hider/restore_token" ]'
fi
check "red within 6 of the cell average" 'close "$ir" "$er"'
check "green within 6 of the cell average" 'close "$ig" "$eg"'
check "blue within 6 of the cell average" 'close "$ib" "$eb"'
[ $failures -eq 0 ] || exit 1
echo "xvfb strip smoke$( [ $screencast = 1 ] && echo ' (screencast)'): passed"
