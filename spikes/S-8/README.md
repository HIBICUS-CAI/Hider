# S-8: linux-arm64 実機で S-3 を再測定

設計書 §10 S-8。合否: CPU パスでも 100 ms 内に収まる。機材が用意でき次第。

## 手順（想定機材は要確認: Raspberry Pi 5 / Snapdragon X 系 Linux 等）

```sh
dotnet run --project spikes/S-3 -c Release               # CPU
dotnet run --project spikes/S-3 -c Release -- --window   # GLX（または Software）
LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project spikes/S-3 -c Release -- --window   # Software を強制
```

## 返してほしい情報

- 3 回分の出力、機材名、`glxinfo -B | head -20`
