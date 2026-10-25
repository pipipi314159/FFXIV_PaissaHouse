# PaissaHouse (TW)

[PaissaHouse](https://github.com/zhudotexe/FFXIV_PaissaHouse) 的 FFXIV 台服（繁中）移植版。

原版會把巡房資料上傳到 PaissaDB，並透過雲端推播其他伺服器的空地通知。PaissaDB 不收台服資料，所以這個版本**拿掉所有雲端連線**，改成把資料存在自己電腦上，並提供遊戲內清單視窗與 CSV 匯出。

## 版本資訊

| 項目 | 內容 |
|---|---|
| 基於 | 原版 v1.4.8.0，commit `b973a5eeb136a2de99c0a77c5c7b9d5e7e180475`（2025-08-11） |
| Dalamud API | 13（`Dalamud.NET.Sdk/13.0.0`） |
| 分支 | `tw-api13` |

## 功能

- **巡房紀錄**：在以太之光或渡船選「前往住宅區」翻看各區清單時，自動記錄每塊空地的區號、大小、價格、購買方式（先到先得／抽籤）與限制（部隊／個人）。
- **抽籤紀錄**：查看空地門口的看板時，記錄抽籤狀態、參加人數與階段結束時間。
- **聊天欄摘要**：巡完一個住宅區的全部區數後（台服目前 18 區，可在設定調整），在聊天欄列出空地清單（輸出格式與頻道可在設定中調整）。
- **`/phouse` 空地清單視窗**：依伺服器、住宅區、大小篩選，可只看「目前可購買」，依地址／價格／抽籤人數排序。
- **CSV 匯出**：巡完整區後自動匯出（可在設定關閉），或在清單視窗手動匯出目前篩選結果。CSV 為 UTF-8（含 BOM），可直接用 Excel 開啟。

## 與原版的差異（程式修改）

- 移除 `Paissa/Client.cs`、`Events.cs`、`Schema.cs`（PaissaDB HTTP 上傳、WebSocket 推播、角色登入回報），以及 `DebounceThrottle`、`WebSocketSharp.Standard` 兩個 NuGet 套件。
- 新增 `Paissa/LocalStore.cs`（本機儲存與 CSV）與 `HouseListWindow.cs`（清單視窗）。
- `WardObserver`：自動偵測台服封包中 LandIdent 的位置（偏移量會寫入 log），讀不到合理資料時略過不記錄；每區區數改為設定值，預設 18。
- `LotteryObserver`：抽籤看板特徵碼先試 7.3 之後的版本，找不到再退回 7.3 之前的版本。
- 設定介面移除需要雲端的「Notifications: Homeworld / Datacenter / All」與各住宅區通知分頁，新增「巡完整區後自動匯出 CSV」。
- `/phouse` 改為開啟本機清單視窗（原為 PaissaDB 網站連結）。
- manifest 名稱加上 `(TW)`；`InternalName` 維持 `autoSweep`，原有設定不會遺失。

## 資料存放位置

```
%APPDATA%\FFXIVSimpleLauncher\Dalamud\Config\pluginConfigs\autoSweep\
  housing_local.json   本機資料
  exports\             匯出的 CSV
```

- **不會**記錄其他玩家的房主名稱。
- 清單視窗的「清除全部資料」要按住 Ctrl 再點，不會刪除已匯出的 CSV。
- 資料只反映你自己看過的時間點；超過 24 小時的資料在清單中會以灰色顯示。

## 編譯

```bat
set DALAMUD_HOME=%APPDATA%\FFXIVSimpleLauncher\Dalamud\Injector
dotnet build AutoSweep.csproj -c Release
```

輸出：`bin\Release\autoSweep.dll`

## 原作者與授權

原作者 [zhudotexe (Andrew Zhu)](https://github.com/zhudotexe)，MIT License（見 `LICENSE`）。

這是非官方移植版，**使用上的問題請回報到本 repo，不要回報給原作者或 PaissaDB。**
