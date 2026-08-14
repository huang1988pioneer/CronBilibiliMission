# CronBilibiliMission

Assumptions inferred from the shipped tool and README; not from a live interview.

## Product
Desktop helper that reads a Netscape `cookies.txt`, extracts Bilibili `SESSDATA` / `BILI_JCT` / `DEDEUSERID` / `BUVID3`, reminds the user when the session will expire, and can write those values to GitHub Actions secrets.

## Audience
The repo owner, at a desk, refreshing expired Bilibili cookies for a daily GitHub Action.

## Job
Get four secret values from a cookie export into GitHub Secrets without pasting them through a browser settings page.

## Constraints
- Cookie values are credentials; never commit them.
- Keep SESSDATA wire-encoded.
- Prefer `.bilibili.com` over other hosts.
- Light desktop window, Avalonia, Traditional Chinese UI.
