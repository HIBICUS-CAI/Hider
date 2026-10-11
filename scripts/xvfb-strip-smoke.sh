#!/bin/bash
# Linux strip capture end to end without a GNOME desktop (docs/manual-tests.md, "作業コンテナでの確認"):
# Xvfb + xcompmgr (per-pixel alpha needs a compositor) + a private session bus + tools/fakeportal, then Hider restores one
# mosaic area (240 px high, strips of 8 px) over a test image. Passes when the portal was called once per strip, every
# screenshot file was deleted (FR-53) and the area shows the image's pixelated colours.
#   scripts/xvfb-strip-smoke.sh [path/to/Hider or Hider.dll]
# Needs: Xvfb, xcompmgr, ImageMagick (convert, display, import), dbus-daemon.
set -euo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
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
dotnet "$work/portal/FakePortal.dll" "$work/shots" > "$work/portal.log" 2>&1 &
sleep 1
if [[ "$hider" == *.dll ]]; then run=(dotnet "$hider"); else run=("$hider"); fi
HOME="$work/home" "${run[@]}" --verbose > "$work/hider.log" 2>&1 &
for _ in $(seq 60); do
    [ "$(grep -c '^shot' "$work/portal.log" || true)" -ge 30 ] && break
    sleep 0.5
done
sleep 2
shots=$(grep -c '^shot' "$work/portal.log" || true)
left=$(ls "$work/shots" | wc -l)
import -window root "$work/after.png"
# Pixel (230, 200) lies in the 12 px cell starting at area-local (24, 48) = screen (224, 198): it should show that cell's
# average of the image (pyramid approximation: within a few levels).
rgb='%[fx:round(255*r)] %[fx:round(255*g)] %[fx:round(255*b)]\n'
read -r ir ig ib < <(convert "$work/after.png" -crop 1x1+230+200 -format "$rgb" info:)
read -r er eg eb < <(convert "$work/bg.png" -crop 12x12+224+198 -scale 1x1\! -format "$rgb" info:)
echo "portal calls: $shots (expected 30), files left: $left, area pixel: $ir $ig $ib, image cell average: $er $eg $eb"
grep -v '^$' "$work/hider.log" | tail -3
close() { local d=$(( $1 - $2 )); [ ${d#-} -le 6 ]; }
[ "$shots" -eq 30 ] && [ "$left" -eq 0 ] && ! grep -q "capture failed" "$work/hider.log"
close "$ir" "$er" && close "$ig" "$eg" && close "$ib" "$eb"
echo "xvfb strip smoke: passed"
