# Context

Glossary for CronBilibiliMission. No implementation details.

## Cookie Export
A Netscape `cookies.txt` (or equivalent JSON / Cookie header) exported from a logged-in browser.

## Login Cookie
The four Bilibili values used by the daily Action: `SESSDATA`, `BILI_JCT` (`bili_jct`), `DEDEUSERID` (`DedeUserID`), `BUVID3` (`buvid3`).

## Wire Value
The Login Cookie string exactly as the export wrote it. `SESSDATA` often contains `%2C`; decoding it breaks login.

## Host Preference
When the same Login Cookie name appears on several hosts, `.bilibili.com` wins. `huasheng.cn` and `biligame.com` are ignored if a Bilibili host exists.

## Cookie Expiry
The Netscape column expiry on the cookie line.

## Session Expiry
The unix timestamp embedded in `SESSDATA` (the second comma-separated field).

## Effective Expiry
The earlier of Cookie Expiry and Session Expiry. This is the date we remind the user about.

## Action Secret
A GitHub Actions repository secret. This repo's account-1 names are `SESSDATA`, `BILI_JCT`, `DEDEUSERID`, `BUVID3`.

## Secret Publish
Writing the four Action Secrets to a GitHub repo. Overwrites the previous values; GitHub never returns the old ones.
