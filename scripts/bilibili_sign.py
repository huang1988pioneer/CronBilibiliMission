#!/usr/bin/env python3
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request


USER_AGENT = (
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36"
)


def env_value(*names):
    for name in names:
        value = os.environ.get(name)
        if value:
            return value
    return ""


def build_cookie():
    sessdata = env_value("SESSDATA")
    bili_jct = env_value("bili_jct", "BILI_JCT")
    dede_user_id = env_value("DedeUserID", "DEDEUSERID")

    missing = []
    if not sessdata:
        missing.append("SESSDATA")
    if not bili_jct:
        missing.append("bili_jct")
    if not dede_user_id:
        missing.append("DedeUserID")

    if missing:
        raise RuntimeError(f"Missing required environment variables: {', '.join(missing)}")

    return {
        "cookie": (
            f"SESSDATA={sessdata}; "
            f"bili_jct={bili_jct}; "
            f"DedeUserID={dede_user_id}"
        ),
        "csrf": bili_jct,
    }


def request_json(url, method="GET", data=None, cookie=""):
    headers = {
        "User-Agent": USER_AGENT,
        "Referer": "https://live.bilibili.com/",
        "Cookie": cookie,
    }

    encoded_data = None
    if data is not None:
        encoded_data = urllib.parse.urlencode(data).encode("utf-8")
        headers["Content-Type"] = "application/x-www-form-urlencoded"

    request = urllib.request.Request(
        url,
        data=encoded_data,
        headers=headers,
        method=method,
    )

    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            payload = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        payload = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"HTTP {error.code}: {payload}") from error
    except urllib.error.URLError as error:
        raise RuntimeError(f"Request failed: {error}") from error

    try:
        return json.loads(payload)
    except json.JSONDecodeError as error:
        raise RuntimeError(f"Invalid JSON response: {payload[:500]}") from error


def check_login(cookie):
    response = request_json(
        "https://api.bilibili.com/x/web-interface/nav",
        cookie=cookie,
    )
    if response.get("code") != 0:
        raise RuntimeError(f"Login check failed: {response}")

    data = response.get("data") or {}
    if not data.get("isLogin"):
        raise RuntimeError("Login check failed: cookie is not logged in")

    uname = data.get("uname") or "Bilibili user"
    mid = data.get("mid") or "unknown"
    print(f"Logged in as {uname} ({mid})")


def live_sign(cookie, csrf):
    response = request_json(
        "https://api.live.bilibili.com/xlive/web-ucenter/v1/sign/DoSign",
        method="POST",
        data={"csrf": csrf, "csrf_token": csrf},
        cookie=cookie,
    )

    code = response.get("code")
    message = response.get("message") or response.get("msg") or ""

    if code == 0:
        data = response.get("data") or {}
        text = data.get("text") or data.get("specialText") or "Sign completed"
        print(text)
        return

    already_signed_messages = ("already", "已", "重复", "signed")
    if any(token in message for token in already_signed_messages):
        print(f"Already signed: {message}")
        return

    raise RuntimeError(f"Sign failed: {response}")


def main():
    auth = build_cookie()
    check_login(auth["cookie"])
    live_sign(auth["cookie"], auth["csrf"])


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(error, file=sys.stderr)
        sys.exit(1)
