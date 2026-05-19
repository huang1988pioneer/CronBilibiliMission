#!/usr/bin/env python3
import argparse
import json
import logging
import os
import random
import sys
import time
import urllib.parse
from datetime import datetime
from logging.handlers import RotatingFileHandler
from pathlib import Path
from zoneinfo import ZoneInfo

from requests import Session
from requests import exceptions as request_exceptions


USER_AGENT = (
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36"
)
TAIPEI = ZoneInfo("Asia/Taipei")
LOG_DIR = Path("logs")
PLAN_FILE = LOG_DIR / "daily_experience_plan.json"
EVENT_LOG = LOG_DIR / "bilibili_experience.jsonl"
RUNTIME_LOG = LOG_DIR / "bilibili_experience.log"
TASK_NAME = "daily_experience"
CONFIRM_AFTER_HOURS = (1, 3, 6)
REQUEST_TIMEOUT_SECONDS = 20
MAX_REQUEST_ATTEMPTS = 5
RETRY_STATUS_CODES = {429, 500, 502, 503, 504}


class BilibiliError(RuntimeError):
    """Base error for predictable Bilibili task failures."""


class LoginExpiredError(BilibiliError):
    """Raised when cookie credentials are missing or expired."""


class RequestFailedError(BilibiliError):
    """Raised when an HTTP request cannot be completed."""


class ApiResponseError(BilibiliError):
    """Raised when an API response is syntactically valid but unsuccessful."""


def setup_logging(debug=False):
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    logger = logging.getLogger()
    logger.setLevel(logging.DEBUG if debug else logging.INFO)
    logger.handlers.clear()

    formatter = logging.Formatter(
        "%(asctime)s %(levelname)s %(name)s: %(message)s",
        datefmt="%Y-%m-%dT%H:%M:%S%z",
    )

    stream_handler = logging.StreamHandler()
    stream_handler.setLevel(logging.DEBUG if debug else logging.INFO)
    stream_handler.setFormatter(formatter)

    file_handler = RotatingFileHandler(
        RUNTIME_LOG,
        maxBytes=512 * 1024,
        backupCount=3,
        encoding="utf-8",
    )
    file_handler.setLevel(logging.DEBUG)
    file_handler.setFormatter(formatter)

    logger.addHandler(stream_handler)
    logger.addHandler(file_handler)


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
        raise LoginExpiredError(f"Missing required environment variables: {', '.join(missing)}")

    return {
        "cookie": (
            f"SESSDATA={sessdata}; "
            f"bili_jct={bili_jct}; "
            f"DedeUserID={dede_user_id}"
        ),
        "csrf": bili_jct,
    }


class BilibiliClient:
    def __init__(
        self,
        cookie,
        csrf,
        timeout=REQUEST_TIMEOUT_SECONDS,
        max_attempts=MAX_REQUEST_ATTEMPTS,
    ):
        self.cookie = cookie
        self.csrf = csrf
        self.timeout = timeout
        self.max_attempts = max_attempts
        self.session = Session()
        self.session.headers.update(
            {
                "User-Agent": USER_AGENT,
                "Referer": "https://www.bilibili.com/",
                "Cookie": cookie,
            }
        )

    def request_json(self, url, method="GET", data=None, referer="https://www.bilibili.com/"):
        headers = {"Referer": referer}
        last_error = None

        for attempt in range(1, self.max_attempts + 1):
            try:
                response = self.session.request(
                    method,
                    url,
                    data=data,
                    headers=headers,
                    timeout=self.timeout,
                )
                if response.status_code in RETRY_STATUS_CODES:
                    last_error = RequestFailedError(f"HTTP {response.status_code}: {response.text[:500]}")
                    if attempt == self.max_attempts:
                        raise last_error
                    self._sleep_before_retry(attempt, f"HTTP {response.status_code}")
                    continue

                response.raise_for_status()
                payload = response.text
                break
            except (request_exceptions.Timeout, request_exceptions.ConnectionError) as error:
                last_error = error
                if attempt == self.max_attempts:
                    raise RequestFailedError(f"Request failed after retries: {error}") from error
                self._sleep_before_retry(attempt, str(error))
            except request_exceptions.HTTPError as error:
                status_code = error.response.status_code if error.response is not None else "unknown"
                payload = error.response.text[:500] if error.response is not None else ""
                raise RequestFailedError(f"HTTP {status_code}: {payload}") from error
        else:
            raise RequestFailedError(f"Request failed: {last_error}")

        try:
            result = json.loads(payload)
        except json.JSONDecodeError as error:
            raise RequestFailedError(f"Invalid JSON response: {payload[:500]}") from error

        if result.get("code") == -101:
            raise LoginExpiredError(f"Cookie expired or not logged in: {result}")
        return result

    def _sleep_before_retry(self, attempt, reason):
        wait_seconds = min(30, (2 ** (attempt - 1)) + random.SystemRandom().uniform(0, 1.5))
        logging.warning("Transient request failure (%s), retrying in %.1fs", reason, wait_seconds)
        time.sleep(wait_seconds)

    def check_login(self):
        response = self.request_json("https://api.bilibili.com/x/web-interface/nav")
        if response.get("code") != 0:
            raise ApiResponseError(f"Login check failed: {response}")

        data = response.get("data") or {}
        if not data.get("isLogin"):
            raise LoginExpiredError("Login check failed: cookie is not logged in")

        uname = data.get("uname") or "Bilibili user"
        mid = data.get("mid") or "unknown"
        account_coins = data.get("money")
        level_info = summarize_level_info(data.get("level_info") or {})
        logging.info(
            (
                "Logged in as %s (%s), account coins: %s, level: Lv%s, exp: %s, "
                "exp to next: %s, days at 15 exp/day: %s"
            ),
            uname,
            mid,
            account_coins,
            level_info.get("current_level"),
            level_info.get("current_exp"),
            level_info.get("exp_to_next_level"),
            level_info.get("days_to_next_level_at_15_exp_per_day"),
        )
        return {
            "status": "logged_in",
            "uname": uname,
            "mid": mid,
            "account_coins": account_coins,
            "level_info": level_info,
        }

    def get_daily_reward(self):
        response = self.request_json(
            "https://api.bilibili.com/x/member/web/exp/reward",
            referer="https://account.bilibili.com/account/home",
        )
        if response.get("code") != 0:
            return response_status(response, "reward_checked", "reward_check_failed")
        return {"status": "reward_checked", "message": "OK", "data": response.get("data") or {}}

    def get_video_info(self):
        popular = self.request_json("https://api.bilibili.com/x/web-interface/popular?ps=20&pn=1")
        if popular.get("code") != 0:
            raise ApiResponseError(f"Failed to get popular videos: {popular}")

        videos = (popular.get("data") or {}).get("list") or []
        candidates = [video for video in videos if video.get("aid") and video.get("bvid")]
        if not candidates:
            raise ApiResponseError("No playable video found from popular list")

        video = random.SystemRandom().choice(candidates)
        if not video.get("cid"):
            detail = self.request_json(
                "https://api.bilibili.com/x/web-interface/view?"
                + urllib.parse.urlencode({"bvid": video["bvid"]})
            )
            if detail.get("code") == 0:
                video.update(detail.get("data") or {})

        if not video.get("cid"):
            raise ApiResponseError(f"Video is missing cid: {video.get('bvid')}")

        return {
            "aid": video["aid"],
            "bvid": video["bvid"],
            "cid": video["cid"],
            "title": video.get("title") or "",
        }

    def watch_video(self, video):
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
            "csrf": self.csrf,
        }
        response = self.request_json(
            "https://api.bilibili.com/x/click-interface/web/heartbeat",
            method="POST",
            data=data,
            referer=f"https://www.bilibili.com/video/{video['bvid']}/",
        )
        return response_status(response, "watch_reported", "watch_failed")

    def share_video(self, video):
        response = self.request_json(
            "https://api.bilibili.com/x/web-interface/share/add",
            method="POST",
            data={"aid": video["aid"], "csrf": self.csrf},
            referer=f"https://www.bilibili.com/video/{video['bvid']}/",
        )
        return response_status(response, "share_reported", "share_failed")


def response_status(response, success_status, failed_status):
    code = response.get("code")
    message = response.get("message") or response.get("msg") or ""
    if code == 0:
        return {"status": success_status, "message": message or "OK", "response": response}
    return {"status": failed_status, "message": message or f"code={code}", "response": response}


def summarize_level_info(level_info):
    current_level = level_info.get("current_level")
    current_exp = parse_int(level_info.get("current_exp"))
    next_exp = parse_int(level_info.get("next_exp"))
    exp_to_next_level = None
    days_to_next_level_at_15_exp_per_day = None

    if current_exp is not None and next_exp is not None:
        exp_to_next_level = max(0, next_exp - current_exp)
        days_to_next_level_at_15_exp_per_day = ceil_div(exp_to_next_level, 15)

    return {
        "current_level": current_level,
        "current_exp": current_exp,
        "next_level_exp": next_exp,
        "exp_to_next_level": exp_to_next_level,
        "days_to_next_level_at_15_exp_per_day": days_to_next_level_at_15_exp_per_day,
    }


def ceil_div(value, divisor):
    return -(-value // divisor)


def parse_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def reward_data(reward):
    return reward.get("data") or {}


def reward_complete(reward):
    data = reward_data(reward)
    return bool(data.get("login") and data.get("watch") and data.get("share"))


def missing_experience_tasks(reward):
    data = reward_data(reward)
    missing = []
    for task in ("login", "watch", "share"):
        if not data.get(task):
            missing.append(task)
    return missing


def run_experience_tasks(client, mode="full", dry_run=False):
    login = client.check_login()
    before_reward = client.get_daily_reward()
    before_missing = missing_experience_tasks(before_reward)

    if mode == "confirm" and not before_missing:
        logging.info("Daily experience tasks already complete.")
        return {
            "login": login,
            "video": None,
            "watch": {"status": "watch_skipped", "message": "already complete"},
            "share": {"status": "share_skipped", "message": "already complete"},
            "reward_before": before_reward,
            "reward_after": before_reward,
            "missing_before": before_missing,
            "missing_after": [],
            "complete": True,
        }

    video = client.get_video_info()
    logging.info("Selected video: %s %s", video["bvid"], video["title"])

    if dry_run:
        logging.info("Dry run enabled; skipping watch and share API calls.")
        return {
            "login": login,
            "video": video,
            "watch": {"status": "watch_skipped", "message": "dry run"},
            "share": {"status": "share_skipped", "message": "dry run"},
            "reward_before": before_reward,
            "reward_after": before_reward,
            "missing_before": before_missing,
            "missing_after": before_missing,
            "complete": reward_complete(before_reward),
        }

    if mode == "full" or "watch" in before_missing:
        watch = client.watch_video(video)
        logging.info("Watch task: %s %s", watch["status"], watch["message"])
    else:
        watch = {"status": "watch_skipped", "message": "already complete"}

    if mode == "full" or "share" in before_missing:
        share = client.share_video(video)
        logging.info("Share task: %s %s", share["status"], share["message"])
    else:
        share = {"status": "share_skipped", "message": "already complete"}

    after_reward = client.get_daily_reward()
    after_missing = missing_experience_tasks(after_reward)
    return {
        "login": login,
        "video": video,
        "watch": watch,
        "share": share,
        "reward_before": before_reward,
        "reward_after": after_reward,
        "missing_before": before_missing,
        "missing_after": after_missing,
        "complete": reward_complete(after_reward),
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
        "initial_done": False,
        "confirmations_done": [],
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


def run_scheduled(dry_run=False):
    now = datetime.now(TAIPEI)
    plan = today_plan(now)
    date = now.date().isoformat()
    current_hour = now.hour
    target_hour = int(plan["target_hour_24h"])

    if current_hour < target_hour:
        logging.info("Waiting. Now %s:00 Taipei, target is %s:00.", current_hour, target_hour)
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

    run_type = None
    confirm_after_hours = None
    confirmations_done = {int(hour) for hour in plan.get("confirmations_done", [])}
    if not plan.get("initial_done"):
        run_type = "initial"
    else:
        for offset in CONFIRM_AFTER_HOURS:
            if current_hour >= target_hour + offset and offset not in confirmations_done:
                run_type = "confirm"
                confirm_after_hours = offset
                break

    if run_type is None:
        logging.info("No due experience check. Now %s:00 Taipei, target was %s:00.", current_hour, target_hour)
        append_event(
            {
                "event": "skip",
                "reason": "no_due_check",
                "date": date,
                "current_hour_24h": current_hour,
                "target_hour_24h": target_hour,
                "confirmations_done": sorted(confirmations_done),
            }
        )
        return

    auth = build_cookie()
    client = BilibiliClient(auth["cookie"], auth["csrf"])
    result = run_experience_tasks(
        client,
        mode="confirm" if run_type == "confirm" else "full",
        dry_run=dry_run,
    )
    if run_type == "initial":
        plan["initial_done"] = True
        plan["initial_done_at_taipei"] = now.isoformat(timespec="seconds")
    else:
        confirmations_done.add(confirm_after_hours)
        plan["confirmations_done"] = sorted(confirmations_done)
        plan[f"confirmed_after_{confirm_after_hours}h_at_taipei"] = now.isoformat(timespec="seconds")

    plan["completed"] = result["complete"]
    plan["login_status"] = result["login"]["status"]
    plan["account_coins"] = result["login"].get("account_coins")
    plan["level_info"] = result["login"].get("level_info")
    plan["watch_status"] = result["watch"]["status"]
    plan["share_status"] = result["share"]["status"]
    if result["video"] is not None:
        plan["video"] = result["video"]
    plan["missing_after"] = result["missing_after"]
    plan["reward_after"] = result["reward_after"]
    write_plan(plan)
    append_event(
        {
            "event": "experience_tasks",
            "dry_run": dry_run,
            "run_type": run_type,
            "confirm_after_hours": confirm_after_hours,
            "date": date,
            "weekday": plan["weekday"],
            "dice": plan["dice"],
            "target_hour_24h": target_hour,
            "login_status": result["login"]["status"],
            "account_coins": result["login"].get("account_coins"),
            "level_info": result["login"].get("level_info"),
            "watch_status": result["watch"]["status"],
            "share_status": result["share"]["status"],
            "video": result["video"],
            "missing_before": result["missing_before"],
            "missing_after": result["missing_after"],
            "complete": result["complete"],
            "reward_after": result["reward_after"],
        }
    )


def parse_args():
    parser = argparse.ArgumentParser(description="Run Bilibili daily experience tasks.")
    parser.add_argument("--scheduled", action="store_true", help="Use the hourly scheduled run window.")
    parser.add_argument("--dry-run", action="store_true", help="Check login/reward/video without posting watch/share actions.")
    parser.add_argument("--debug", action="store_true", help="Enable verbose runtime logging.")
    return parser.parse_args()


def main():
    args = parse_args()
    setup_logging(debug=args.debug)
    if args.scheduled:
        run_scheduled(dry_run=args.dry_run)
        return

    auth = build_cookie()
    client = BilibiliClient(auth["cookie"], auth["csrf"])
    result = run_experience_tasks(client, dry_run=args.dry_run)
    now = datetime.now(TAIPEI)
    append_event(
        {
            "event": "manual_experience_tasks",
            "dry_run": args.dry_run,
            "date": now.date().isoformat(),
            "login_status": result["login"]["status"],
            "account_coins": result["login"].get("account_coins"),
            "level_info": result["login"].get("level_info"),
            "watch_status": result["watch"]["status"],
            "share_status": result["share"]["status"],
            "video": result["video"],
            "missing_before": result["missing_before"],
            "missing_after": result["missing_after"],
            "complete": result["complete"],
            "reward_after": result["reward_after"],
        }
    )


if __name__ == "__main__":
    try:
        main()
    except BilibiliError as error:
        logging.error("%s", error)
        sys.exit(1)
    except Exception as error:
        logging.exception("Unexpected failure: %s", error)
        sys.exit(1)
