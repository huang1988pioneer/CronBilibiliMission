#!/usr/bin/env python3
import json
import os
import random
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo


USER_AGENT = (
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36"
)
TAIPEI = ZoneInfo("Asia/Taipei")
LOG_DIR = Path("logs")
PLAN_FILE = LOG_DIR / "daily_plan.json"
EVENT_LOG = LOG_DIR / "bilibili_sign.jsonl"


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
        cookie=cookie,
    )

    code = response.get("code")
    message = response.get("message") or response.get("msg") or ""

    if code == 0:
        data = response.get("data") or {}
        text = data.get("text") or data.get("specialText") or "Sign completed"
        print(text)
        return {"status": "signed", "message": text, "response": response}

    unavailable_messages = ("活动已下线", "无法使用", "activity offline", "unavailable")
    if any(token in message for token in unavailable_messages):
        print(f"Sign unavailable: {message}")
        return {"status": "sign_unavailable", "message": message, "response": response}

    already_signed_messages = ("already", "重复", "signed", "已经签到", "已签到")
    if any(token in message for token in already_signed_messages):
        print(f"Already signed: {message}")
        return {"status": "already_signed", "message": message, "response": response}

    raise RuntimeError(f"Sign failed: {response}")


def append_event(event):
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    event["created_at_taipei"] = datetime.now(TAIPEI).isoformat(timespec="seconds")
    with EVENT_LOG.open("a", encoding="utf-8") as file:
        file.write(json.dumps(event, ensure_ascii=False, sort_keys=True) + "\n")


def read_plan():
    if not PLAN_FILE.exists():
        return {}

    with PLAN_FILE.open("r", encoding="utf-8") as file:
        return json.load(file)


def write_plan(plan):
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    with PLAN_FILE.open("w", encoding="utf-8") as file:
        json.dump(plan, file, ensure_ascii=False, indent=2, sort_keys=True)
        file.write("\n")


def today_plan(now):
    today = now.date().isoformat()
    plan = read_plan()
    if plan.get("date") == today:
        return plan

    weekday = now.isoweekday()
    dice = random.SystemRandom().randint(1, 6)
    plan = {
        "date": today,
        "timezone": "Asia/Taipei",
        "weekday": weekday,
        "dice": dice,
        "target_hour_24h": weekday + dice,
        "signed": False,
    }
    write_plan(plan)
    append_event(
        {
            "event": "plan_created",
            "date": today,
            "weekday": weekday,
            "dice": dice,
            "target_hour_24h": plan["target_hour_24h"],
        }
    )
    return plan


def run_scheduled():
    now = datetime.now(TAIPEI)
    plan = today_plan(now)
    date = now.date().isoformat()
    current_hour = now.hour
    target_hour = int(plan["target_hour_24h"])

    if plan.get("signed"):
        print(f"{date} already signed. Target hour was {target_hour}:00 Taipei.")
        append_event({"event": "skip", "reason": "already_signed", "date": date})
        return

    if current_hour < target_hour:
        print(f"Waiting. Now {current_hour}:00 Taipei, target is {target_hour}:00.")
        append_event(
            {
                "event": "skip",
                "reason": "before_target_hour",
                "date": date,
                "current_hour_24h": current_hour,
                "target_hour_24h": target_hour,
            }
        )
        return

    auth = build_cookie()
    check_login(auth["cookie"])
    result = live_sign(auth["cookie"], auth["csrf"])
    plan["signed"] = True
    plan["signed_at_taipei"] = now.isoformat(timespec="seconds")
    plan["sign_status"] = result["status"]
    plan["sign_message"] = result["message"]
    write_plan(plan)
    append_event(
        {
            "event": "sign",
            "date": date,
            "weekday": plan["weekday"],
            "dice": plan["dice"],
            "target_hour_24h": target_hour,
            "status": result["status"],
            "message": result["message"],
        }
    )


def main():
    if "--scheduled" in sys.argv:
        run_scheduled()
        return

    auth = build_cookie()
    check_login(auth["cookie"])
    result = live_sign(auth["cookie"], auth["csrf"])
    append_event({"event": "manual_sign", "status": result["status"], "message": result["message"]})


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(error, file=sys.stderr)
        sys.exit(1)
