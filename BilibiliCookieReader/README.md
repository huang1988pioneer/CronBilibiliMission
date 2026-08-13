# Bilibili Cookie 讀取器

Avalonia 桌面工具：讀取瀏覽器匯出的 Netscape `cookies.txt`，取出本專案 GitHub Secrets / 本機環境變數需要的三個值。

| 工具顯示 | GitHub Secret | Cookie 原名 |
| --- | --- | --- |
| `SESSDATA` | `SESSDATA` | `SESSDATA` |
| `BILI_JCT` | `bili_jct` | `bili_jct` |
| `DEDEUSERID` | `DedeUserID` | `DedeUserID` |

啟動時若在「文件」資料夾找到 `abuhg17_cookies.txt`（或 `cookies.txt`）會自動讀取。只採用 `.bilibili.com` 的 Cookie，略過 `huasheng.cn`、`biligame.com` 等站的同名欄位。`SESSDATA` 保持檔案中的原始值，不要 URL 解碼。

## 需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## 執行

```bash
cd BilibiliCookieReader
dotnet run
```

Windows 也可雙擊 `run.bat`。

## 操作

1. 瀏覽或拖放 `.txt`（Ctrl+O）。
2. 確認三個欄位都有值，必要時點「驗證登入」。
3. 依需求複製：
   - **複製環境變數**：`SESSDATA` / `BILI_JCT` / `DEDEUSERID`
   - **複製 GitHub Secrets**：`SESSDATA` / `bili_jct` / `DedeUserID`
   - PowerShell / bash export / Cookie 字串
4. 把值貼到 GitHub Repo `Settings` → `Secrets and variables` → `Actions`。

Cookie 是登入憑證，不要提交到 git。
