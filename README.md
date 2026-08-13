# CronBilibiliMission

使用 GitHub Actions 執行 Bilibili 每日經驗任務。

## 需要設定的 GitHub Secrets

請到 GitHub Repo 的 `Settings` -> `Secrets and variables` -> `Actions` -> `New repository secret` 新增：

| Secret 名稱 | 說明 |
| --- | --- |
| `SESSDATA` / `BILI_JCT` / `DEDEUSERID` | 帳號 1：`huang1988pioneer` |
| `SESSDATA2` / `BILI_JCT2` / `DEDEUSERID2` | 帳號 2：`abuhg17` |
| `SESSDATA3` / `BILI_JCT3` / `DEDEUSERID3` | 帳號 3：`goldshoot0720` |

## 每日經驗任務

目前會嘗試執行：

- 每日登入：檢查 Cookie 是否仍為登入狀態。
- 每日觀看影片：隨機取得一支熱門影片並上報觀看進度。
- 每日分享影片：呼叫影片分享 API。
- 每日獎勵查詢：執行前後查詢每日獎勵狀態並寫入紀錄。
- 帳號硬幣數：記錄目前帳號持有的硬幣數 `account_coins`。
- 自動投幣：帳號未達 Lv.6 且硬幣餘額大於 333 時，隨機選片投幣，每日最多取得 50 投幣經驗；Lv.6 不執行。
- 每日匯總：依各帳號的當日 plan / 事件紀錄產生 `logs/<帳號>/daily_summary.md`，並寫入 GitHub Actions 的 Job Summary。

## 自動執行時間

排程以台北時間與 24 小時制計算。每天第一次執行時會隨機擲 1 到 6 點，並加上星期數：

| 星期 | 星期數 | 簽到範圍 |
| --- | ---: | --- |
| 週一 | 1 | 02:00-07:59 |
| 週二 | 2 | 03:00-08:59 |
| 週三 | 3 | 04:00-09:59 |
| 週四 | 4 | 05:00-10:59 |
| 週五 | 5 | 06:00-11:59 |
| 週六 | 6 | 07:00-12:59 |
| 週日 | 7 | 08:00-13:59 |

例如週一擲到 1 就在 02:00 後執行，擲到 6 就在 07:00 後執行。任務執行後，當天還會在目標時間後 1、3、6 小時重新確認每日登入、觀看、分享狀態；如果仍未完成，會補跑缺少的任務。每日擲骰結果、補跑檢查與任務結果會記錄在 `logs/`，並由 GitHub Actions 自動提交回 repo。

## 每日匯總

每次 Action 執行（含 skip、首次任務、補查、手動）結束後都會產生每日匯總：

| 輸出 | 說明 |
| --- | --- |
| GitHub Job Summary | 在 Actions 執行結果頁可直接閱讀 |
| `logs/<帳號>/daily_summary.md` | 寫入 repo，隨各帳號的 `logs/` 紀錄一併 commit |

也可本機只重算匯總：

```bash
python3 scripts/bilibili_sign.py --summary-only
```

## 自動執行

GitHub Actions 會在每天台北時間 02:05 到 19:05 每小時檢查一次，命中當天擲骰算出的時間才會執行每日經驗任務，並在後續確認點補查補跑。

工作流程每天也會擷取一次 Bilibili 熱搜完整榜單，依台北日期寫入 `logs/hot_search.jsonl`。同一天重跑不會產生重複紀錄；熱搜暫時無法取得時不會阻斷三個帳號的每日經驗任務，下一個排程時段會再次嘗試。

綜合熱門影片也會每天完整分頁擷取一次，將標題、UP 主、播放量、彈幕、收藏、按讚、熱門理由、封面與影片連結寫入 `logs/popular.jsonl`。同一天只保留一份快照；熱門 API 暫時失敗時會在下一個排程時段再次嘗試。

排行榜會每天擷取「全部」及頁面上的 20 個分類完整清單，依分類分組寫入 `logs/ranking.jsonl`。一般影片保存名次、標題、UP 主、播放量、彈幕、收藏、按讚、投幣、分享、綜合分數、封面與連結；番劇、國創、紀錄片、電影、電視劇、綜藝另保存評分、追蹤數與更新進度。紀錄依台北日期去重；個別分類暫時失敗時保留已成功的資料，下一個排程時段只補抓缺少的分類。

## 使用者手動執行

使用者可以在 GitHub 網頁手動執行：

1. 打開 Repo 的 `Actions` 頁面。
2. 點左側 `Bilibili Daily Experience Tasks`。
3. 點右上 `Run workflow`。
4. Branch 保持 `main`。
5. 點綠色 `Run workflow`。

手動執行會依序執行三個帳號的每日經驗任務，並把結果寫入各自的 `logs/<帳號>/bilibili_experience.jsonl`，再由 GitHub Actions 提交回 repo；不會套用自動排程的擲骰時間限制。

## 從 cookies.txt 取出 Secrets

桌面工具（Avalonia）可分別選擇 `huang1988pioneer`、`abuhg17`、`goldshoot0720` 三個帳號，讀取各自 Netscape 格式的 `cookies.txt`，並直接上傳帳號對應的 GitHub Actions Secrets。

下載 [BilibiliCookieReader v1.4.0](https://github.com/huang1988pioneer/CronBilibiliMission/releases/tag/v1.4.0)（自包含，無需另外安裝 .NET）：

- [Windows x64](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.4.0/BilibiliCookieReader-v1.4.0-win-x64.zip)
- [macOS Apple Silicon](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.4.0/BilibiliCookieReader-v1.4.0-osx-arm64.zip)
- [macOS Intel](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.4.0/BilibiliCookieReader-v1.4.0-osx-x64.zip)
- [Linux x64](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.4.0/BilibiliCookieReader-v1.4.0-linux-x64.zip)

讀到三個值後，工具會提醒 Cookie 檔與 SESSDATA 工作階段的預定過期日（台北時間）。也可依帳號直接更新這個 repo 對應的 GitHub Actions Secrets。需要有 Secrets 寫入權限的 GitHub 權杖，或本機已 `gh auth login`。

Avalonia 工具啟動後會在背景立即檢查熱搜，之後每小時檢查一次，依台北日期每天寫入一筆至本機 `%LocalAppData%\BilibiliCookieReader\hot_search.jsonl`。此功能不需要 Cookie；程式必須保持執行，關閉後背景檢查即停止。

排行榜背景服務同時擷取「全部＋20 分類」，依台北日期寫入本機 `%LocalAppData%\BilibiliCookieReader\ranking.jsonl`。若個別分類暫時失敗，已成功分類會先保存，下一個小時只補抓缺少分類；介面會顯示完成分類數。排行榜同樣不需要 Cookie，關閉程式後停止。

本機開發：

```bash
cd BilibiliCookieReader
dotnet run
```

只採用 `.bilibili.com` 的 Cookie。Cookie 屬於敏感資料，請只貼到 GitHub Secrets，不要提交到程式碼。

## 本機測試

本機測試：

```bash
python3 -m pip install -r requirements.txt
export SESSDATA="你的 SESSDATA"
export bili_jct="你的 bili_jct"
export DedeUserID="你的 DedeUserID"
python3 scripts/bilibili_sign.py
```

常用參數：

- `--scheduled`：套用台北時間排程與補查規則。
- `--dry-run`：只檢查登入、獎勵狀態與影片資料，不送出觀看或分享請求。
- `--debug`：輸出更詳細的執行紀錄。
- `--summary-only`：只依現有 `logs/` 產生每日匯總，不呼叫 Bilibili API。

## 注意事項

- Cookie 屬於敏感資料，請只放在 GitHub Secrets，不要提交到程式碼。
- 若 Bilibili Cookie 過期，Action 會登入檢查失敗，需要重新更新 Secrets。
