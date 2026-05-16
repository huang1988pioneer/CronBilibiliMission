# CronBilibiliMission

使用 GitHub Actions 執行 Bilibili 每日經驗任務。

## 需要設定的 GitHub Secrets

請到 GitHub Repo 的 `Settings` -> `Secrets and variables` -> `Actions` -> `New repository secret` 新增：

| Secret 名稱 | 說明 |
| --- | --- |
| `SESSDATA` | Bilibili Cookie 中的 `SESSDATA` |
| `bili_jct` | Bilibili Cookie 中的 `bili_jct` |
| `DedeUserID` | Bilibili Cookie 中的 `DedeUserID` |

## 每日經驗任務

目前會嘗試執行：

- 每日登入：檢查 Cookie 是否仍為登入狀態。
- 每日觀看影片：隨機取得一支熱門影片並上報觀看進度。
- 每日分享影片：呼叫影片分享 API。
- 每日獎勵查詢：執行前後查詢每日獎勵狀態並寫入紀錄。
- 帳號硬幣數：記錄目前帳號持有的硬幣數 `account_coins`。

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

## 執行方式

GitHub Actions 會在每天台北時間 02:05 到 19:05 每小時檢查一次，命中當天擲骰算出的時間才會執行每日經驗任務，並在後續確認點補查補跑。也可以到 `Actions` 頁面手動執行 `Bilibili Daily Experience Tasks`。

本機測試：

```bash
export SESSDATA="你的 SESSDATA"
export bili_jct="你的 bili_jct"
export DedeUserID="你的 DedeUserID"
python3 scripts/bilibili_sign.py
```

## 注意事項

- Cookie 屬於敏感資料，請只放在 GitHub Secrets，不要提交到程式碼。
- 若 Bilibili Cookie 過期，Action 會登入檢查失敗，需要重新更新 Secrets。
