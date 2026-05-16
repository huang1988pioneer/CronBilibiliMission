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
EVENT_LOG = LOG_DIR / "bilibili_experience.jsonl"
TASK_NAME = "daily_experience"


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


def request_json(url, method="GET", data=None, cookie="", referer="https://www.bilibili.com/"):
    headers = {
        "User-Agent": USER_AGENT,
        "Referer": referer,
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
    return {"status": "logged_in", "uname": uname, "mid": mid}


def response_status(response, success_status, failed_status):
    code = response.get("code")
    message = response.get("message") or response.get("msg") or ""
    if code == 0:
        return {"status": success_status, "message": message or "OK", "response": response}
    return {"status": failed_status, "message": message or f"code={code}", "response": response}


def get_daily_reward(cookie):
    response = request_json(
        "https://api.bilibili.com/x/member/web/exp/reward",
        cookie=cookie,
        referer="https://account.bilibili.com/account/home",
    )
    if response.get("code") != 0:
        return response_status(response, "reward_checked", "reward_check_failed")
    return {"status": "reward_checked", "message": "OK", "data": response.get("data") or {}}


def get_video_info(cookie):
    popular = request_json(
        "https://api.bilibili.com/x/web-interface/popular?ps=20&pn=1",
        cookie=cookie,
    )
    if popular.get("code") != 0:
        raise RuntimeError(f"Failed to get popular videos: {popular}")

    videos = (popular.get("data") or {}).get("list") or []
    candidates = [video for video in videos if video.get("aid") and video.get("bvid")]
    if not candidates:
        raise RuntimeError("No playable video found from popular list")

    video = random.SystemRandom().choice(candidates)
    if not video.get("cid"):
        detail = request_json(
            "https://api.bilibili.com/x/web-interface/view?"
            + urllib.parse.urlencode({"bvid": video["bvid"]}),
            cookie=cookie,
        )
        if detail.get("code") == 0:
            video.update(detail.get("data") or {})

    if not video.get("cid"):
        raise RuntimeError(f"Video is missing cid: {video.get('bvid')}")

    return {
        "aid": video["aid"],
        "bvid": video["bvid"],
        "cid": video["cid"],
        "title": video.get("title") or "",
    }


def watch_video(cookie, csrf, video):
    data = {
        "aid": video["aid"],
        "bvid": video["bvid"],
        "cid": video["cid"],
        "played_time": 60,
        "realtime": 60,
        "start_ts": int(datetime.now(TAIPEI).timestamp()) - 60,
        "type": 3,
        "dt": 2,
        "play_type": 1,
        "csrf": csrf,
    }
    response = request_json(
        "https://api.bilibili.com/x/click-interface/web/heartbeat",
        method="POST",
        data=data,
        cookie=cookie,
        referer=f"https://www.bilibili.com/video/{video['bvid']}/",
    )
    return response_status(response, "watch_reported", "watch_failed")


def share_video(cookie, csrf, video):
    response = request_json(
        "https://api.bilibili.com/x/web-interface/share/add",
        method="POST",
        data={"aid": video["aid"], "csrf": csrf},
        cookie=cookie,
        referer=f"https://www.bilibili.com/video/{video['bvid']}/",
    )
    return response_status(response, "share_reported", "share_failed")


def run_experience_tasks(cookie, csrf):
    login = check_login(cookie)
    before_reward = get_daily_reward(cookie)
    video = get_video_info(cookie)
    print(f"Selected video: {video['bvid']} {video['title']}")

    watch = watch_video(cookie, csrf, video)
    print(f"Watch task: {watch['status']} {watch['message']}")

    share = share_video(cookie, csrf, video)
    print(f"Share task: {share['status']} {share['message']}")

    after_reward = get_daily_reward(cookie)
    return {
        "login": login,
        "video": video,
        "watch": watch,
        "share": share,
        "reward_before": before_reward,
        "reward_after": after_reward,
    }


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
    if plan.get("date") == today and plan.get("task") == TASK_NAME:
        return plan

    weekday = now.isoweekday()
    dice = random.SystemRandom().randint(1, 6)
    plan = {
        "date": today,
        "task": TASK_NAME,
        "timezone": "Asia/Taipei",
        "weekday": weekday,
        "dice": dice,
        "target_hour_24h": weekday + dice,
        "completed": False,
    }
    write_plan(plan)
    append_event(
        {
            "event": "experience_plan_created",
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

    if plan.get("completed"):
        print(f"{date} experience tasks already completed. Target hour was {target_hour}:00 Taipei.")
        append_event({"event": "skip", "reason": "already_completed", "date": date})
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
    result = run_experience_tasks(auth["cookie"], auth["csrf"])
    plan["completed"] = True
    plan["completed_at_taipei"] = now.isoformat(timespec="seconds")
    plan["login_status"] = result["login"]["status"]
    plan["watch_status"] = result["watch"]["status"]
    plan["share_status"] = result["share"]["status"]
    plan["video"] = result["video"]
    plan["reward_after"] = result["reward_after"]
    write_plan(plan)
    append_event(
        {
            "event": "experience_tasks",
            "date": date,
            "weekday": plan["weekday"],
            "dice": plan["dice"],
            "target_hour_24h": target_hour,
            "login_status": result["login"]["status"],
            "watch_status": result["watch"]["status"],
            "share_status": result["share"]["status"],
            "video": result["video"],
            "reward_after": result["reward_after"],
        }
    )


def main():
    if "--scheduled" in sys.argv:
        run_scheduled()
        return

    auth = build_cookie()
    result = run_experience_tasks(auth["cookie"], auth["csrf"])
    append_event(
        {
            "event": "manual_experience_tasks",
            "login_status": result["login"]["status"],
            "watch_status": result["watch"]["status"],
            "share_status": result["share"]["status"],
            "video": result["video"],
            "reward_after": result["reward_after"],
        }
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(error, file=sys.stderr)
        sys.exit(1)
