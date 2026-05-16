# CronBilibiliMission

使用 GitHub Actions 執行 Bilibili 每日直播簽到。

## 需要設定的 GitHub Secrets

請到 GitHub Repo 的 `Settings` -> `Secrets and variables` -> `Actions` -> `New repository secret` 新增：

| Secret 名稱 | 說明 |
| --- | --- |
| `SESSDATA` | Bilibili Cookie 中的 `SESSDATA` |
| `bili_jct` | Bilibili Cookie 中的 `bili_jct` |
| `DedeUserID` | Bilibili Cookie 中的 `DedeUserID` |

## 自動簽到時間

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

例如週一擲到 1 就在 02:00 後簽到，擲到 6 就在 07:00 後簽到。每日擲骰結果與簽到結果會記錄在 `logs/`，並由 GitHub Actions 自動提交回 repo。

## 執行方式

GitHub Actions 會在每天台北時間 02:05 到 13:05 每小時檢查一次，命中當天擲骰算出的時間才會真正打卡簽到。也可以到 `Actions` 頁面手動執行 `Bilibili Daily Sign`。

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
