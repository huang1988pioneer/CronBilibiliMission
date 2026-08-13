# Bilibili Cookie 讀取器

Avalonia 桌面工具：讀取瀏覽器匯出的 Netscape `cookies.txt`，取出本專案 GitHub Secrets / 本機環境變數需要的三個值。

| 工具顯示 | GitHub Secret | Cookie 原名 |
| --- | --- | --- |
| `SESSDATA` | `SESSDATA` | `SESSDATA` |
| `BILI_JCT` | `BILI_JCT` | `bili_jct` |
| `DEDEUSERID` | `DEDEUSERID` | `DedeUserID` |

啟動時若在「文件」資料夾找到 `abuhg17_cookies.txt`（或 `cookies.txt`）會自動讀取。只採用 `.bilibili.com` 的 Cookie，略過 `huasheng.cn`、`biligame.com` 等站的同名欄位。`SESSDATA` 保持檔案中的原始值，不要 URL 解碼。

讀取後會顯示 Cookie 檔與 SESSDATA 工作階段的預定過期日（台北時間），並依剩餘天數用顏色提醒：14 天內請預定更新，3 天內或已過期會加強警告。

## 下載

[BilibiliCookieReader v1.3.0](https://github.com/huang1988pioneer/CronBilibiliMission/releases/tag/v1.3.0)（自包含，無需另外安裝 .NET）：

- [Windows x64](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.3.0/BilibiliCookieReader-v1.3.0-win-x64.zip)
- [macOS Apple Silicon](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.3.0/BilibiliCookieReader-v1.3.0-osx-arm64.zip)
- [macOS Intel](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.3.0/BilibiliCookieReader-v1.3.0-osx-x64.zip)
- [Linux x64](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.3.0/BilibiliCookieReader-v1.3.0-linux-x64.zip)

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
3. 在「更新 GitHub Actions Secrets」填 repo（預設 `huang1988pioneer/CronBilibiliMission`）與 GitHub 權杖，勾選確認後按「更新 GitHub Secrets」。
4. 或改複製後手動貼到 GitHub Repo `Settings` → `Secrets and variables` → `Actions`。

權杖需要能寫入該 repo 的 Actions Secrets。也可按「使用 gh 權杖」（需已 `gh auth login`）。工具會更新：

- `SESSDATA`
- `BILI_JCT`
- `DEDEUSERID`

Cookie 是登入憑證，不要提交到 git。

## 建置 Release

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/win-x64
```

同樣可將 `-r` 換成 `osx-arm64`、`osx-x64`、`linux-x64`。
