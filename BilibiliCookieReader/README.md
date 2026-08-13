# Bilibili Cookie 讀取器

Avalonia 桌面工具：讀取瀏覽器匯出的 Netscape `cookies.txt`，取出本專案 GitHub Secrets / 本機環境變數需要的三個值。

| 工具顯示 | GitHub Secret | Cookie 原名 |
| --- | --- | --- |
| `SESSDATA` | `SESSDATA` | `SESSDATA` |
| `BILI_JCT` | `bili_jct` | `bili_jct` |
| `DEDEUSERID` | `DedeUserID` | `DedeUserID` |

啟動時若在「文件」資料夾找到 `abuhg17_cookies.txt`（或 `cookies.txt`）會自動讀取。只採用 `.bilibili.com` 的 Cookie，略過 `huasheng.cn`、`biligame.com` 等站的同名欄位。`SESSDATA` 保持檔案中的原始值，不要 URL 解碼。

## 下載

[BilibiliCookieReader v1.0.0](https://github.com/huang1988pioneer/CronBilibiliMission/releases/tag/v1.0.0)（自包含，無需另外安裝 .NET）：

- [Windows x64](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.0.0/BilibiliCookieReader-v1.0.0-win-x64.zip)
- [macOS Apple Silicon](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.0.0/BilibiliCookieReader-v1.0.0-osx-arm64.zip)
- [macOS Intel](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.0.0/BilibiliCookieReader-v1.0.0-osx-x64.zip)
- [Linux x64](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.0.0/BilibiliCookieReader-v1.0.0-linux-x64.zip)

解壓後執行 `BilibiliCookieReader.exe`（Windows）或 `BilibiliCookieReader`（macOS / Linux）。

## 需求

- 預編譯包：無需另外安裝 .NET
- 從原始碼執行：[.NET 10 SDK](https://dotnet.microsoft.com/download)

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

## 建置 Release

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/win-x64
```

同樣可將 `-r` 換成 `osx-arm64`、`osx-x64`、`linux-x64`。
