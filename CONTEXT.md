# Context

Glossary for CronBilibiliMission. No implementation details.

## Cookie Export
A Netscape `cookies.txt` (or equivalent JSON / Cookie header) exported from a logged-in browser.

## Login Cookie
The three Bilibili values the daily Action needs: `SESSDATA`, `BILI_JCT` (`bili_jct`), `DEDEUSERID` (`DedeUserID`).

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
A GitHub Actions repository secret. This repo's names are `SESSDATA`, `BILI_JCT`, `DEDEUSERID`.

## Secret Publish
Writing the three Action Secrets to a GitHub repo. Overwrites the previous values; GitHub never returns the old ones.
