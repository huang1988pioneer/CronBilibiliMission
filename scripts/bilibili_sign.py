#!/usr/bin/env python3
import argparse
import json
import logging
import math
import os
import random
import sys
import time
import urllib.parse
from datetime import datetime, timedelta
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
SUMMARY_FILE = LOG_DIR / "daily_summary.md"
ACCOUNT_NAME = "huang1988pioneer"
TASK_NAME = "daily_experience"
EVENT_LOG_RETENTION_DAYS = 30
CONFIRM_AFTER_HOURS = (1, 3, 6)
WEEKDAY_NAMES = {
    1: "週一",
    2: "週二",
    3: "週三",
    4: "週四",
    5: "週五",
    6: "週六",
    7: "週日",
}
REQUEST_TIMEOUT_SECONDS = 20
MAX_REQUEST_ATTEMPTS = 5
RETRY_STATUS_CODES = {429, 500, 502, 503, 504}
BASE_DAILY_EXPERIENCE = 15
COIN_SPEND_BALANCE_THRESHOLD = 333
MAX_DAILY_COIN_EXPERIENCE = 50
EXPERIENCE_PER_COIN = 10
MAX_COINS_PER_VIDEO = 2
LEVEL_EXP_THRESHOLDS = {
    3: 1500,
    4: 4500,
    5: 10800,
    6: 28800,
}
ACCOUNT_BAN_WINDOWS = {
    "huang1988pioneer": {
        "started_at": "2026-05-15T23:05:00+08:00",
        "duration_days": 365,
        "source": "Bilibili 帳號違規處理通知",
    },
}
DEFAULT_SMTP_HOST = "smtp.gmail.com"
DEFAULT_SMTP_PORT = 587
DEFAULT_EMAIL_NOTIFY_TO = (
    "goldshoot0720@gmail.com,"
    "huang1988pioneer@gmail.com,"
    "chbondg@hotmail.com"
)
RESEND_RECIPIENTS = (
    ("RESEND_API_KEY", "goldshoot0720@gmail.com"),
    ("RESEND_API_KEY3", "huang1988pioneer@gmail.com"),
    ("RESEND_API_KEY2", "chbondg@hotmail.com"),
)
DEFAULT_RESEND_FROM = "Bilibili Monitor <onboarding@resend.dev>"
RESEND_EMAILS_URL = "https://api.resend.com/emails"


def configure_account(account_name):
    global ACCOUNT_NAME, LOG_DIR, PLAN_FILE, EVENT_LOG, RUNTIME_LOG, SUMMARY_FILE
    safe_name = "".join(
        character for character in account_name.strip()
        if character.isalnum() or character in {"-", "_"}
    )
    if not safe_name:
        raise ValueError("Account name must contain letters or numbers.")

    ACCOUNT_NAME = safe_name
    LOG_DIR = Path("logs") / safe_name
    PLAN_FILE = LOG_DIR / "daily_experience_plan.json"
    EVENT_LOG = LOG_DIR / "bilibili_experience.jsonl"
    RUNTIME_LOG = LOG_DIR / "bilibili_experience.log"
    SUMMARY_FILE = LOG_DIR / "daily_summary.md"


def estimate_account_ban_status(started_at, duration_days, now=None):
    started = datetime.fromisoformat(started_at)
    if started.tzinfo is None:
        started = started.replace(tzinfo=TAIPEI)
    else:
        started = started.astimezone(TAIPEI)

    duration_days = int(duration_days)
    if duration_days <= 0:
        raise ValueError("Ban duration must be greater than zero.")

    now = (now or datetime.now(TAIPEI)).astimezone(TAIPEI)
    estimated_release = started + timedelta(days=duration_days)
    remaining_seconds = max(0, (estimated_release - now).total_seconds())
    return {
        "active": now < estimated_release,
        "started_at": started.isoformat(timespec="seconds"),
        "duration_days": duration_days,
        "estimated_release_at": estimated_release.isoformat(timespec="seconds"),
        "remaining_days": math.ceil(remaining_seconds / 86400),
    }


def configured_account_ban_status(now=None):
    config = ACCOUNT_BAN_WINDOWS.get(ACCOUNT_NAME)
    if not config:
        return None
    status = estimate_account_ban_status(
        config["started_at"],
        config["duration_days"],
        now=now,
    )
    status["source"] = config.get("source") or "人工設定"
    return status


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


def env_bool(name, default=False):
    value = os.environ.get(name)
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


def build_cookie():
    sessdata = env_value("SESSDATA")
    bili_jct = env_value("bili_jct", "BILI_JCT")
    dede_user_id = env_value("DedeUserID", "DEDEUSERID")
    buvid3 = env_value("buvid3", "BUVID3")

    missing = []
    if not sessdata:
        missing.append("SESSDATA")
    if not bili_jct:
        missing.append("bili_jct")
    if not dede_user_id:
        missing.append("DedeUserID")
    if missing:
        raise LoginExpiredError(f"Missing required environment variables: {', '.join(missing)}")

    cookie_parts = [
        f"SESSDATA={sessdata}",
        f"bili_jct={bili_jct}",
        f"DedeUserID={dede_user_id}",
    ]
    if buvid3:
        cookie_parts.append(f"buvid3={buvid3}")
    else:
        logging.warning("BUVID3 is missing; interaction APIs may be rejected by Bilibili risk control.")

    return {
        "cookie": "; ".join(cookie_parts),
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
                "exp to next: %s, days at %s exp/day: %s"
            ),
            uname,
            mid,
            account_coins,
            level_info.get("current_level"),
            level_info.get("current_exp"),
            level_info.get("exp_to_next_level"),
            BASE_DAILY_EXPERIENCE,
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

    def get_daily_coin_experience(self):
        response = self.request_json(
            "https://api.bilibili.com/x/web-interface/coin/today/exp"
        )
        if response.get("code") != 0:
            return None
        return parse_int(response.get("data"))

    def get_video_info(self, exclude_bvids=None):
        popular = self.request_json("https://api.bilibili.com/x/web-interface/popular?ps=20&pn=1")
        if popular.get("code") != 0:
            raise ApiResponseError(f"Failed to get popular videos: {popular}")

        videos = (popular.get("data") or {}).get("list") or []
        excluded = set(exclude_bvids or [])
        candidates = [
            video for video in videos
            if video.get("aid") and video.get("bvid") and video.get("bvid") not in excluded
        ]
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

    def give_coins(self, video, multiply):
        response = self.request_json(
            "https://api.bilibili.com/x/web-interface/coin/add",
            method="POST",
            data={
                "aid": video["aid"],
                "multiply": multiply,
                "select_like": 0,
                "csrf": self.csrf,
            },
            referer=f"https://www.bilibili.com/video/{video['bvid']}/",
        )
        result = response_status(response, "coins_given", "coin_failed")
        result["coins"] = multiply if response.get("code") == 0 else 0
        result["video"] = video
        return result


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
    level_breakthrough_dates_at_15_exp_per_day = {}

    if current_exp is not None and next_exp is not None:
        exp_to_next_level = max(0, next_exp - current_exp)
        days_to_next_level_at_15_exp_per_day = ceil_div(exp_to_next_level, BASE_DAILY_EXPERIENCE)

    if current_exp is not None:
        level_breakthrough_dates_at_15_exp_per_day = estimate_level_breakthrough_dates(current_exp)

    return {
        "current_level": current_level,
        "current_exp": current_exp,
        "next_level_exp": next_exp,
        "exp_to_next_level": exp_to_next_level,
        "days_to_next_level_at_15_exp_per_day": days_to_next_level_at_15_exp_per_day,
        "level_breakthrough_dates_at_15_exp_per_day": level_breakthrough_dates_at_15_exp_per_day,
    }


def estimate_level_breakthrough_dates(current_exp, today=None):
    today = today or datetime.now(TAIPEI).date()
    dates = {}

    for level, required_exp in LEVEL_EXP_THRESHOLDS.items():
        exp_remaining = max(0, required_exp - current_exp)
        days_remaining = ceil_div(exp_remaining, BASE_DAILY_EXPERIENCE)
        dates[f"lv{level}"] = {
            "required_exp": required_exp,
            "exp_remaining": exp_remaining,
            "days_at_15_exp_per_day": days_remaining,
            "estimated_date": (today + timedelta(days=days_remaining)).isoformat(),
        }

    return dates


def pending_level_breakthroughs(breakthroughs):
    pending = []
    for level in (3, 4, 5, 6):
        item = breakthroughs.get(f"lv{level}") or {}
        days_remaining = parse_int(item.get("days_at_15_exp_per_day"))
        if days_remaining is not None and days_remaining > 0:
            pending.append((level, item))
    return pending


def ceil_div(value, divisor):
    return -(-value // divisor)


def parse_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def parse_float(value):
    try:
        return float(value)
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


def daily_coin_experience(reward):
    value = parse_int(reward_data(reward).get("coins"))
    return max(0, value or 0)


def coin_task_plan(login, reward, realtime_coin_experience=None):
    level = parse_int((login.get("level_info") or {}).get("current_level"))
    balance = parse_float(login.get("account_coins"))
    earned = (
        daily_coin_experience(reward)
        if realtime_coin_experience is None
        else max(0, realtime_coin_experience)
    )

    if level is None:
        return {"eligible": False, "reason": "unknown_level", "earned_experience": earned, "coins": 0}
    if level >= 6:
        return {"eligible": False, "reason": "level_6", "earned_experience": earned, "coins": 0}
    if balance is None or balance <= COIN_SPEND_BALANCE_THRESHOLD:
        return {"eligible": False, "reason": "balance_not_over_333", "earned_experience": earned, "coins": 0}
    if earned >= MAX_DAILY_COIN_EXPERIENCE:
        return {"eligible": False, "reason": "daily_50_exp_complete", "earned_experience": earned, "coins": 0}

    missing_experience = MAX_DAILY_COIN_EXPERIENCE - earned
    return {
        "eligible": True,
        "reason": "eligible",
        "earned_experience": earned,
        "coins": ceil_div(missing_experience, EXPERIENCE_PER_COIN),
    }


def run_coin_task(client, plan, dry_run=False):
    if not plan["eligible"]:
        return {
            "status": "coin_skipped",
            "reason": plan["reason"],
            "coins_spent": 0,
            "experience_before": plan["earned_experience"],
            "target_experience": MAX_DAILY_COIN_EXPERIENCE,
            "videos": [],
        }

    if dry_run:
        return {
            "status": "coin_skipped",
            "reason": "dry_run",
            "coins_spent": 0,
            "coins_planned": plan["coins"],
            "experience_before": plan["earned_experience"],
            "target_experience": MAX_DAILY_COIN_EXPERIENCE,
            "videos": [],
        }

    coins_remaining = plan["coins"]
    coins_spent = 0
    videos = []
    used_bvids = set()
    while coins_remaining > 0:
        video = client.get_video_info(exclude_bvids=used_bvids)
        used_bvids.add(video["bvid"])
        multiply = min(MAX_COINS_PER_VIDEO, coins_remaining)
        result = client.give_coins(video, multiply)
        videos.append(
            {
                "aid": video["aid"],
                "bvid": video["bvid"],
                "title": video["title"],
                "coins": result["coins"],
                "status": result["status"],
                "message": result["message"],
            }
        )
        if result["status"] != "coins_given":
            logging.warning("Coin task failed for %s: %s", video["bvid"], result["message"])
            break
        coins_spent += multiply
        coins_remaining -= multiply
        logging.info("Gave %s coin(s) to %s.", multiply, video["bvid"])

    return {
        "status": "coins_given" if coins_remaining == 0 else "coin_incomplete",
        "reason": "completed" if coins_remaining == 0 else "api_failed",
        "coins_spent": coins_spent,
        "experience_before": plan["earned_experience"],
        "target_experience": MAX_DAILY_COIN_EXPERIENCE,
        "videos": videos,
    }


def run_experience_tasks(client, mode="full", dry_run=False):
    login = client.check_login()
    before_reward = client.get_daily_reward()
    before_missing = missing_experience_tasks(before_reward)
    realtime_coin_experience = client.get_daily_coin_experience()
    coin_plan = coin_task_plan(login, before_reward, realtime_coin_experience)

    if mode == "confirm" and not before_missing and not coin_plan["eligible"]:
        logging.info("Daily experience tasks already complete.")
        return {
            "login": login,
            "video": None,
            "watch": {"status": "watch_skipped", "message": "already complete"},
            "share": {"status": "share_skipped", "message": "already complete"},
            "coin_task": run_coin_task(client, coin_plan, dry_run=dry_run),
            "reward_before": before_reward,
            "reward_after": before_reward,
            "missing_before": before_missing,
            "missing_after": [],
            "complete": True,
        }

    video = client.get_video_info()
    logging.info("Selected video: %s %s", video["bvid"], video["title"])

    if dry_run:
        logging.info("Dry run enabled; skipping watch, share, and coin API calls.")
        return {
            "login": login,
            "video": video,
            "watch": {"status": "watch_skipped", "message": "dry run"},
            "share": {"status": "share_skipped", "message": "dry run"},
            "coin_task": run_coin_task(client, coin_plan, dry_run=True),
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

    coin_task = run_coin_task(client, coin_plan)

    after_reward = client.get_daily_reward()
    after_missing = missing_experience_tasks(after_reward)
    return {
        "login": login,
        "video": video,
        "watch": watch,
        "share": share,
        "coin_task": coin_task,
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
    prune_event_log()


def event_record_date(event):
    event_date = event.get("date")
    if event_date:
        try:
            return datetime.fromisoformat(event_date).date()
        except ValueError:
            pass

    created_at = event.get("created_at_taipei")
    if created_at:
        try:
            return datetime.fromisoformat(created_at).astimezone(TAIPEI).date()
        except ValueError:
            pass

    return None


def prune_event_log(now=None):
    if not EVENT_LOG.exists():
        return

    today = (now or datetime.now(TAIPEI)).date()
    cutoff_date = today - timedelta(days=EVENT_LOG_RETENTION_DAYS - 1)
    retained_lines = []
    pruned_count = 0

    with EVENT_LOG.open("r", encoding="utf-8") as file:
        for line in file:
            try:
                event = json.loads(line)
            except json.JSONDecodeError:
                retained_lines.append(line)
                continue

            record_date = event_record_date(event)
            if record_date is None or record_date >= cutoff_date:
                retained_lines.append(json.dumps(event, ensure_ascii=False, sort_keys=True) + "\n")
            else:
                pruned_count += 1

    if pruned_count == 0:
        return

    temp_log = EVENT_LOG.with_suffix(EVENT_LOG.suffix + ".tmp")
    with temp_log.open("w", encoding="utf-8") as file:
        file.writelines(retained_lines)
    temp_log.replace(EVENT_LOG)
    logging.info(
        "Pruned %s event log records older than %s.",
        pruned_count,
        cutoff_date.isoformat(),
    )


def latest_recorded_level():
    for event in reversed(read_event_log()):
        level_info = event.get("level_info") or {}
        current_level = parse_int(level_info.get("current_level"))
        if current_level is not None:
            return current_level

    return None


def read_event_log():
    if not EVENT_LOG.exists():
        return []

    events = []
    with EVENT_LOG.open("r", encoding="utf-8") as file:
        for line in file:
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                continue

    return events


def latest_coin_record_before(date):
    coin_records = coin_records_before(date)
    if not coin_records:
        return None

    return coin_records[-1]


def coin_records_before(date):
    return [
        record
        for record in coin_records_by_date()
        if record["date"] < date
    ]


def coin_records_by_date():
    latest_by_date = {}

    for event in read_event_log():
        event_date = event.get("date")
        coins = parse_float(event.get("account_coins"))
        if event_date is None or coins is None:
            continue
        latest_by_date[event_date] = {
            "date": event_date,
            "account_coins": coins,
            "created_at_taipei": event.get("created_at_taipei"),
        }

    return [
        latest_by_date[date]
        for date in sorted(latest_by_date)
    ]


def stagnant_coin_balance_streak(date, current_coins):
    if current_coins is None:
        return []

    records = coin_records_before(date)
    records.append(
        {
            "date": date,
            "account_coins": current_coins,
            "created_at_taipei": datetime.now(TAIPEI).isoformat(timespec="seconds"),
        }
    )

    if len(records) < 2:
        return records

    streak = [records[-1]]
    for record in reversed(records[:-1]):
        next_record = streak[0]
        if next_record["account_coins"] > record["account_coins"]:
            break
        streak.insert(0, record)

    return streak


def coin_balance_alert_already_sent(start_date, current_date):
    for event in read_event_log():
        event_date = event.get("date")
        if event_date is None or event_date < start_date or event_date >= current_date:
            continue

        notification = event.get("coin_balance_notification") or {}
        if notification.get("status") == "email_sent":
            return True

    return False


def email_notification_configured():
    return bool(resend_recipient_configs() or (env_value("SMTP_USERNAME") and env_value("SMTP_PASSWORD")))


def email_config():
    username = env_value("SMTP_USERNAME")
    resend_recipients = resend_recipient_configs()
    provider = "resend" if resend_recipients else "smtp"
    return {
        "provider": provider,
        "resend_recipients": resend_recipients,
        "resend_from": env_value("RESEND_FROM") or DEFAULT_RESEND_FROM,
        "host": env_value("SMTP_HOST") or DEFAULT_SMTP_HOST,
        "port": parse_int(env_value("SMTP_PORT")) or DEFAULT_SMTP_PORT,
        "username": username,
        "password": env_value("SMTP_PASSWORD"),
        "sender": env_value("SMTP_FROM") or username,
        "recipient": env_value("EMAIL_NOTIFY_TO") or DEFAULT_EMAIL_NOTIFY_TO,
        "starttls": env_bool("SMTP_STARTTLS", default=True),
    }


def resend_recipient_configs():
    return [
        {"api_key_name": api_key_name, "api_key": env_value(api_key_name), "recipient": recipient}
        for api_key_name, recipient in RESEND_RECIPIENTS
        if env_value(api_key_name)
    ]


def notify_level_upgrade(previous_level, result):
    level_info = result["login"].get("level_info") or {}
    current_level = parse_int(level_info.get("current_level"))

    if previous_level is None or current_level is None or current_level <= previous_level:
        return {"status": "email_skipped", "reason": "no_level_upgrade"}

    if not email_notification_configured():
        logging.info(
            "Level upgraded from Lv%s to Lv%s, but email notification is not configured.",
            previous_level,
            current_level,
        )
        return {"status": "email_skipped", "reason": "email_not_configured"}

    config = email_config()

    subject = f"Bilibili level upgraded: Lv{previous_level} -> Lv{current_level}"
    body = build_level_upgrade_email_body(previous_level, current_level, result)
    try:
        send_email(config, subject, body)
    except (OSError, RequestFailedError, ApiResponseError) as error:
        logging.error("Level upgrade email failed: %s", error)
        return {
            "status": "email_failed",
            "previous_level": previous_level,
            "current_level": current_level,
            "message": str(error),
        }

    logging.info("Level upgrade email sent to %s.", config["recipient"])
    return {
        "status": "email_sent",
        "previous_level": previous_level,
        "current_level": current_level,
        "recipient": config["recipient"],
    }


def notify_coin_balance_issue(result, date):
    current_coins = parse_float(result["login"].get("account_coins"))
    if current_coins is None:
        return {"status": "email_skipped", "reason": "insufficient_coin_history"}

    streak = stagnant_coin_balance_streak(date, current_coins)
    if len(streak) < 3:
        return {
            "status": "email_skipped",
            "reason": "stagnant_coin_streak_below_threshold",
            "streak_days": len(streak),
        }

    if coin_balance_alert_already_sent(streak[0]["date"], date):
        return {
            "status": "email_skipped",
            "reason": "coin_balance_alert_already_sent",
            "streak_days": len(streak),
            "streak": streak,
        }

    previous_coin_record = streak[-2]
    previous_coins = previous_coin_record["account_coins"]

    if not email_notification_configured():
        logging.info(
            "Coin balance did not increase for %s recorded days, but email notification is not configured.",
            len(streak),
        )
        return {
            "status": "email_skipped",
            "reason": "email_not_configured",
            "streak_days": len(streak),
            "streak": streak,
            "previous_date": previous_coin_record["date"],
            "previous_coins": previous_coins,
            "current_date": date,
            "current_coins": current_coins,
        }

    config = email_config()
    subject = "Bilibili coin balance has not increased for 3 days"
    body = build_coin_balance_email_body(streak, current_coins, result, date)
    try:
        send_email(config, subject, body)
    except (OSError, RequestFailedError, ApiResponseError) as error:
        logging.error("Coin balance email failed: %s", error)
        return {
            "status": "email_failed",
            "message": str(error),
            "streak_days": len(streak),
            "streak": streak,
            "previous_date": previous_coin_record["date"],
            "previous_coins": previous_coins,
            "current_date": date,
            "current_coins": current_coins,
        }

    logging.info("Coin balance email sent to %s.", config["recipient"])
    return {
        "status": "email_sent",
        "recipient": config["recipient"],
        "streak_days": len(streak),
        "streak": streak,
        "previous_date": previous_coin_record["date"],
        "previous_coins": previous_coins,
        "current_date": date,
        "current_coins": current_coins,
    }


def build_level_upgrade_email_body(previous_level, current_level, result):
    level_info = result["login"].get("level_info") or {}
    lines = [
        f"Bilibili account {result['login'].get('uname')} upgraded from Lv{previous_level} to Lv{current_level}.",
        "",
        f"Current exp: {level_info.get('current_exp')}",
        f"Next level exp: {level_info.get('next_level_exp')}",
        f"Exp to next level: {level_info.get('exp_to_next_level')}",
        f"Days to next level at {BASE_DAILY_EXPERIENCE} exp/day: {level_info.get('days_to_next_level_at_15_exp_per_day')}",
        f"Account coins: {result['login'].get('account_coins')}",
        f"Checked at: {datetime.now(TAIPEI).isoformat(timespec='seconds')}",
    ]
    return "\n".join(lines)


def build_coin_balance_email_body(streak, current_coins, result, date):
    first_record = streak[0]
    previous_record = streak[-2]
    streak_lines = [
        f"- {record['date']}: {record['account_coins']}"
        for record in streak
    ]
    lines = [
        f"Bilibili account {result['login'].get('uname')} coin balance has not increased for {len(streak)} recorded days.",
        "",
        "Automatic coin spending runs only below Lv.6 when the balance is over 333 coins.",
        "If the balance did not change, inspect the recorded coin task status.",
        "Please check whether the Bilibili cookie secrets need to be refreshed.",
        "",
        f"First stagnant date: {first_record['date']}",
        f"Previous date: {previous_record['date']}",
        f"Previous coins: {previous_record['account_coins']}",
        f"Current date: {date}",
        f"Current coins: {current_coins}",
        "",
        "Recent coin records:",
        *streak_lines,
        "",
        "Secrets to check:",
        "SESSDATA",
        "bili_jct / BILI_JCT",
        "DedeUserID / DEDEUSERID",
        "buvid3 / BUVID3（建議；投幣與分享風控識別）",
        "",
        f"Checked at: {datetime.now(TAIPEI).isoformat(timespec='seconds')}",
    ]
    return "\n".join(lines)


def send_email(config, subject, body):
    if config["provider"] == "resend":
        send_resend_email(config, subject, body)
        return

    send_smtp_email(config, subject, body)


def send_resend_email(config, subject, body):
    results = []
    failures = []

    for recipient_config in config["resend_recipients"]:
        payload = {
            "from": config["resend_from"],
            "to": [recipient_config["recipient"]],
            "subject": subject,
            "text": body,
        }
        response = Session().post(
            RESEND_EMAILS_URL,
            headers={
                "Authorization": f"Bearer {recipient_config['api_key']}",
                "Content-Type": "application/json",
            },
            data=json.dumps(payload),
            timeout=REQUEST_TIMEOUT_SECONDS,
        )
        result = {
            "api_key_name": recipient_config["api_key_name"],
            "recipient": recipient_config["recipient"],
            "status_code": response.status_code,
        }
        if response.status_code >= 400:
            result["message"] = response.text[:500]
            failures.append(result)
        else:
            try:
                result["response"] = response.json()
            except ValueError:
                result["response"] = response.text[:500]
        results.append(result)

    config["send_results"] = results
    if failures:
        raise ApiResponseError(f"Resend email failed for {len(failures)} recipient(s): {failures}")


def send_smtp_email(config, subject, body):
    import smtplib
    from email.message import EmailMessage

    message = EmailMessage()
    message["From"] = config["sender"]
    message["To"] = config["recipient"]
    message["Subject"] = subject
    message.set_content(body)

    with smtplib.SMTP(config["host"], config["port"], timeout=REQUEST_TIMEOUT_SECONDS) as smtp:
        if config["starttls"]:
            smtp.starttls()
        if config["username"] and config["password"]:
            smtp.login(config["username"], config["password"])
        smtp.send_message(message)


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
    previous_level = latest_recorded_level()
    result = run_experience_tasks(
        client,
        mode="confirm" if run_type == "confirm" else "full",
        dry_run=dry_run,
    )
    level_upgrade_notification = (
        {"status": "email_skipped", "reason": "dry_run"}
        if dry_run
        else notify_level_upgrade(previous_level, result)
    )
    coin_balance_notification = (
        {"status": "email_skipped", "reason": "dry_run"}
        if dry_run
        else notify_coin_balance_issue(result, date)
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
    plan["level_upgrade_notification"] = level_upgrade_notification
    plan["coin_balance_notification"] = coin_balance_notification
    plan["watch_status"] = result["watch"]["status"]
    plan["share_status"] = result["share"]["status"]
    plan["coin_task"] = result["coin_task"]
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
            "level_upgrade_notification": level_upgrade_notification,
            "coin_balance_notification": coin_balance_notification,
            "watch_status": result["watch"]["status"],
            "share_status": result["share"]["status"],
            "coin_task": result["coin_task"],
            "video": result["video"],
            "missing_before": result["missing_before"],
            "missing_after": result["missing_after"],
            "complete": result["complete"],
            "reward_after": result["reward_after"],
        }
    )


def events_for_date(date):
    return [event for event in read_event_log() if event.get("date") == date]


def plan_for_date(date):
    plan = read_plan()
    if plan.get("date") == date and plan.get("task") == TASK_NAME:
        return plan
    return {}


def task_runs_for_date(date):
    return [
        event
        for event in events_for_date(date)
        if event.get("event") in {"experience_tasks", "manual_experience_tasks"}
    ]


def bool_mark(value):
    return "✓" if value else "✗"


def escape_markdown_cell(value):
    return str(value).replace("|", "\\|").replace("\n", " ")


def format_video(video):
    if not video:
        return "—"
    bvid = video.get("bvid") or "unknown"
    title = video.get("title") or ""
    if title:
        return f"`{escape_markdown_cell(bvid)}` {escape_markdown_cell(title)}"
    return f"`{escape_markdown_cell(bvid)}`"


def format_run_type(event):
    run_type = event.get("run_type")
    if run_type == "initial":
        return "首次"
    if run_type == "confirm":
        hours = event.get("confirm_after_hours")
        if hours is not None:
            return f"+{hours}h 補查"
        return "補查"
    if event.get("event") == "manual_experience_tasks":
        return "手動"
    return run_type or "—"


def reward_flags(reward):
    data = reward_data(reward or {})
    return {
        "login": bool(data.get("login")),
        "watch": bool(data.get("watch")),
        "share": bool(data.get("share")),
    }


def overall_status_label(plan, runs):
    if plan.get("completed") or any(run.get("complete") for run in runs):
        confirmations = {int(hour) for hour in plan.get("confirmations_done", [])}
        if plan.get("initial_done") and all(hour in confirmations for hour in CONFIRM_AFTER_HOURS):
            return "已完成（含 1h / 3h / 6h 補查）"
        if plan.get("completed") or (runs and runs[-1].get("complete")):
            return "已完成每日經驗（補查未全部跑完）"
        return "已完成"
    if runs:
        return "進行中 / 尚未全部完成"
    if plan:
        if not plan.get("initial_done"):
            return "等待首次執行"
        return "進行中"
    return "尚無今日紀錄"


def build_daily_summary_markdown(date=None):
    now = datetime.now(TAIPEI)
    date = date or now.date().isoformat()
    plan = plan_for_date(date)
    runs = task_runs_for_date(date)
    latest = runs[-1] if runs else {}
    level_info = plan.get("level_info") or latest.get("level_info") or {}
    reward = plan.get("reward_after") or latest.get("reward_after") or {}
    flags = reward_flags(reward)
    weekday = plan.get("weekday") or latest.get("weekday")
    if weekday is None:
        try:
            weekday = datetime.fromisoformat(date).isoweekday()
        except ValueError:
            weekday = None
    weekday_name = WEEKDAY_NAMES.get(weekday, "—")
    dice = plan.get("dice", latest.get("dice", "—"))
    target_hour = plan.get("target_hour_24h", latest.get("target_hour_24h", "—"))
    coins = plan.get("account_coins", latest.get("account_coins"))
    confirmations = sorted(int(hour) for hour in plan.get("confirmations_done", []))
    missing_after = plan.get("missing_after")
    if missing_after is None:
        missing_after = latest.get("missing_after") or []

    lines = [
        f"# Bilibili 每日經驗匯總 — {ACCOUNT_NAME} — {date}（{weekday_name}）",
        "",
        f"**總結：{overall_status_label(plan, runs)}**",
        "",
        "## 排程",
        "",
        "| 項目 | 結果 |",
        "| --- | --- |",
        f"| 擲骰 | **{dice}** |",
        f"| 目標時段 | **{target_hour}:00** 後 |",
        f"| 首次執行 | {plan.get('initial_done_at_taipei') or ('已完成' if plan.get('initial_done') else '尚未')} |",
        f"| 補查 1h / 3h / 6h | {' / '.join('✓' if hour in confirmations else '—' for hour in CONFIRM_AFTER_HOURS)} |",
        "",
        "## 任務進度",
        "",
    ]

    if runs:
        lines.extend(
            [
                "| 時間 | 類型 | 完成 | 登入 | 觀看 | 分享 | 投幣 | 影片 |",
                "| --- | --- | --- | --- | --- | --- | --- | --- |",
            ]
        )
        for run in runs:
            run_flags = reward_flags(run.get("reward_after"))
            lines.append(
                "| {time} | {run_type} | {complete} | {login} | {watch} | {share} | {coin} | {video} |".format(
                    time=run.get("created_at_taipei") or "—",
                    run_type=format_run_type(run),
                    complete=bool_mark(run.get("complete")),
                    login=run.get("login_status") or ("✓" if run_flags["login"] else "—"),
                    watch=run.get("watch_status") or "—",
                    share=run.get("share_status") or "—",
                    coin=(run.get("coin_task") or {}).get("status") or "—",
                    video=format_video(run.get("video")),
                )
            )
        lines.append("")
    else:
        lines.extend(["尚無任務執行紀錄。", ""])

    lines.extend(
        [
            "**獎勵狀態（最新）**",
            "",
            f"- 登入：{bool_mark(flags['login'])}",
            f"- 觀看：{bool_mark(flags['watch'])}",
            f"- 分享：{bool_mark(flags['share'])}",
            f"- 缺少任務：{', '.join(missing_after) if missing_after else '無'}",
            "",
            "## 帳號狀態",
            "",
            "| 項目 | 數值 |",
            "| --- | --- |",
            f"| 等級 | **Lv.{level_info.get('current_level', '—')}** |",
            f"| 經驗 | **{level_info.get('current_exp', '—')}** / {level_info.get('next_level_exp', '—')}（還差 **{level_info.get('exp_to_next_level', '—')}**） |",
            f"| 預估升下一級 | 約 **{level_info.get('days_to_next_level_at_15_exp_per_day', '—')}** 天（每日 {BASE_DAILY_EXPERIENCE} exp） |",
            f"| 硬幣 | **{coins if coins is not None else '—'}** |",
            f"| 登入狀態 | {plan.get('login_status') or latest.get('login_status') or '—'} |",
            "",
        ]
    )

    ban_status = configured_account_ban_status(now=now)
    if ban_status and ban_status["active"]:
        ban_started = datetime.fromisoformat(ban_status["started_at"]).strftime("%Y-%m-%d %H:%M")
        estimated_release = datetime.fromisoformat(
            ban_status["estimated_release_at"]
        ).strftime("%Y-%m-%d %H:%M")
        lines.extend(
            [
                "## 帳號封禁狀態",
                "",
                "> ⚠️ 帳號目前處於封禁中；分享、投幣、投稿等社區功能可能無法使用。",
                "",
                "| 項目 | 資訊 |",
                "| --- | --- |",
                "| 狀態 | **帳號已封禁** |",
                f"| 封禁開始 | {ban_started}（台北時間） |",
                f"| 封禁期限 | {ban_status['duration_days']} 天 |",
                f"| 預估解禁時間 | **{estimated_release}（台北時間）** |",
                f"| 剩餘時間 | 約 **{ban_status['remaining_days']} 天** |",
                f"| 推估依據 | {ban_status['source']} |",
                "",
            ]
        )

    breakthroughs = level_info.get("level_breakthrough_dates_at_15_exp_per_day") or {}
    pending_breakthroughs = pending_level_breakthroughs(breakthroughs)
    if pending_breakthroughs:
        lines.extend(
            [
                "升等預估（每日 15 exp）：",
                "",
                "| 目標 | 預估日期 | 剩餘天數 |",
                "| --- | --- | ---: |",
            ]
        )
        for level, item in pending_breakthroughs:
            lines.append(
                f"| Lv.{level} | {item.get('estimated_date', '—')} | {item.get('days_at_15_exp_per_day', '—')} |"
            )
        lines.append("")

    notes = []
    failed_shares = [
        run for run in runs if str(run.get("share_status") or "").endswith("failed")
    ]
    if failed_shares:
        notes.append("分享曾失敗，後續補查有機會補上。")
    if plan.get("completed") and all(hour in confirmations for hour in CONFIRM_AFTER_HOURS):
        notes.append("今日全流程已結束。")
    elif plan and not plan.get("initial_done"):
        notes.append("尚未到首次執行時段，或首次任務尚未跑完。")
    if notes:
        lines.extend(["## 備註", ""])
        for index, note in enumerate(notes, start=1):
            lines.append(f"{index}. {note}")
        lines.append("")

    lines.extend(
        [
            "---",
            f"_產生時間（台北）：{now.isoformat(timespec='seconds')}_",
            "",
        ]
    )
    return "\n".join(lines)


def write_github_step_summary(markdown):
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary_path:
        return False

    path = Path(summary_path)
    with path.open("a", encoding="utf-8") as file:
        file.write(markdown)
        if not markdown.endswith("\n"):
            file.write("\n")
    return True


def emit_daily_summary(date=None):
    markdown = build_daily_summary_markdown(date=date)
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    SUMMARY_FILE.write_text(markdown, encoding="utf-8")
    if write_github_step_summary(markdown):
        logging.info("Daily summary written to %s and GitHub Step Summary.", SUMMARY_FILE)
    else:
        logging.info("Daily summary written to %s.", SUMMARY_FILE)
    return markdown


def parse_args():
    parser = argparse.ArgumentParser(description="Run Bilibili daily experience tasks.")
    parser.add_argument("--scheduled", action="store_true", help="Use the hourly scheduled run window.")
    parser.add_argument("--dry-run", action="store_true", help="Check login/reward/video without posting watch/share actions.")
    parser.add_argument("--debug", action="store_true", help="Enable verbose runtime logging.")
    parser.add_argument(
        "--account",
        default="huang1988pioneer",
        help="Account label used to isolate logs and summaries.",
    )
    parser.add_argument(
        "--summary-only",
        action="store_true",
        help="Only generate today's daily summary from existing logs.",
    )
    return parser.parse_args()


def main():
    args = parse_args()
    configure_account(args.account)
    setup_logging(debug=args.debug)
    if args.summary_only:
        emit_daily_summary()
        return

    try:
        if args.scheduled:
            run_scheduled(dry_run=args.dry_run)
            return

        auth = build_cookie()
        client = BilibiliClient(auth["cookie"], auth["csrf"])
        now = datetime.now(TAIPEI)
        date = now.date().isoformat()
        previous_level = latest_recorded_level()
        result = run_experience_tasks(client, dry_run=args.dry_run)
        level_upgrade_notification = (
            {"status": "email_skipped", "reason": "dry_run"}
            if args.dry_run
            else notify_level_upgrade(previous_level, result)
        )
        coin_balance_notification = (
            {"status": "email_skipped", "reason": "dry_run"}
            if args.dry_run
            else notify_coin_balance_issue(result, date)
        )
        append_event(
            {
                "event": "manual_experience_tasks",
                "dry_run": args.dry_run,
                "date": date,
                "login_status": result["login"]["status"],
                "account_coins": result["login"].get("account_coins"),
                "level_info": result["login"].get("level_info"),
                "level_upgrade_notification": level_upgrade_notification,
                "coin_balance_notification": coin_balance_notification,
                "watch_status": result["watch"]["status"],
                "share_status": result["share"]["status"],
                "coin_task": result["coin_task"],
                "video": result["video"],
                "missing_before": result["missing_before"],
                "missing_after": result["missing_after"],
                "complete": result["complete"],
                "reward_after": result["reward_after"],
            }
        )
    finally:
        try:
            emit_daily_summary()
        except Exception as error:
            logging.exception("Failed to emit daily summary: %s", error)


if __name__ == "__main__":
    try:
        main()
    except BilibiliError as error:
        logging.error("%s", error)
        sys.exit(1)
    except Exception as error:
        logging.exception("Unexpected failure: %s", error)
        sys.exit(1)
