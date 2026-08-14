# Bilibili Cookie 讀取器

Avalonia 桌面工具：讀取瀏覽器匯出的 Netscape `cookies.txt`，取出本專案 GitHub Secrets / 本機環境變數需要的四個值。

| 工具顯示 | GitHub Secret | Cookie 原名 |
| --- | --- | --- |
| `SESSDATA` | `SESSDATA` | `SESSDATA` |
| `BILI_JCT` | `BILI_JCT` | `bili_jct` |
| `DEDEUSERID` | `DEDEUSERID` | `DedeUserID` |
| `BUVID3` | `BUVID3` | `buvid3` |

預設選取帳號 1 `huang1988pioneer`，也可切換至帳號 2 `abuhg17` 或帳號 3 `goldshoot0720`。每次切換帳號時 Cookie 檔案路徑保持空白；請拖放檔案或按「瀏覽」選擇。只採用 `.bilibili.com` 的 Cookie，略過 `huasheng.cn`、`biligame.com` 等站的同名欄位。`SESSDATA` 保持檔案中的原始值，不要 URL 解碼。

讀取後會顯示 Cookie 檔與 SESSDATA 工作階段的預定過期日（台北時間），並依剩餘天數用顏色提醒：14 天內請預定更新，3 天內或已過期會加強警告。

## 下載

[BilibiliCookieReader v1.5.1](https://github.com/huang1988pioneer/CronBilibiliMission/releases/tag/v1.5.1)（自包含，無需另外安裝 .NET）：

- [Windows x64](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.5.1/BilibiliCookieReader-v1.5.1-win-x64.zip)
- [macOS Apple Silicon](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.5.1/BilibiliCookieReader-v1.5.1-osx-arm64.zip)
- [macOS Intel](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.5.1/BilibiliCookieReader-v1.5.1-osx-x64.zip)
- [Linux x64](https://github.com/huang1988pioneer/CronBilibiliMission/releases/download/v1.5.1/BilibiliCookieReader-v1.5.1-linux-x64.zip)

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
2. 確認四個欄位都有值，必要時點「驗證登入」。
3. 在「更新 GitHub Actions Secrets」填 repo（預設 `huang1988pioneer/CronBilibiliMission`）與 GitHub 權杖，勾選確認後按「更新 GitHub Secrets」。
4. 或改複製後手動貼到 GitHub Repo `Settings` → `Secrets and variables` → `Actions`。

權杖需要能寫入該 repo 的 Actions Secrets。也可按「使用 gh 權杖」（需已 `gh auth login`）。工具會依選取帳號更新：

- 帳號 1：`SESSDATA`、`BILI_JCT`、`DEDEUSERID`、`BUVID3`
- 帳號 2：`SESSDATA2`、`BILI_JCT2`、`DEDEUSERID2`、`BUVID32`
- 帳號 3：`SESSDATA3`、`BILI_JCT3`、`DEDEUSERID3`、`BUVID33`

Cookie 是登入憑證，不要提交到 git。

## 建置 Release

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/win-x64
```

同樣可將 `-r` 換成 `osx-arm64`、`osx-x64`、`linux-x64`。
