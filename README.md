# CronBilibiliMission

使用 GitHub Actions 執行 Bilibili 每日直播簽到。

## 需要設定的 GitHub Secrets

請到 GitHub Repo 的 `Settings` -> `Secrets and variables` -> `Actions` -> `New repository secret` 新增：

| Secret 名稱 | 說明 |
| --- | --- |
| `SESSDATA` | Bilibili Cookie 中的 `SESSDATA` |
| `bili_jct` | Bilibili Cookie 中的 `bili_jct` |
| `DedeUserID` | Bilibili Cookie 中的 `DedeUserID` |

## 執行方式

GitHub Actions 會在每天台北時間 08:10 左右自動執行，也可以到 `Actions` 頁面手動執行 `Bilibili Daily Sign`。

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
