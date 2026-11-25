#!/usr/bin/env python3
import filecmp
import json
import logging
import time
from datetime import datetime
from itertools import count
from pathlib import Path
from tempfile import TemporaryDirectory
from zoneinfo import ZoneInfo

from requests import Session
from requests import exceptions as request_exceptions


API_ROOT = "https://api.bilibili.com"
NORMAL_RANKING_PATH = "/x/web-interface/ranking/v2"
PGC_WEB_RANKING_PATH = "/pgc/web/rank/list"
PGC_SEASON_RANKING_PATH = "/pgc/season/rank/web/list"
RANKING_LOG = Path("logs") / "ranking.jsonl"
MAX_RANKING_BYTES = 100 * 1024 * 1024
TAIPEI = ZoneInfo("Asia/Taipei")
USER_AGENT = (
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36"
)
REQUEST_TIMEOUT_SECONDS = 20
MAX_REQUEST_ATTEMPTS = 3
RETRY_STATUS_CODES = {429, 500, 502, 503, 504}

# Matches every category shown on Bilibili's web ranking page.
RANKING_CATEGORIES = (
    {"key": "all", "name": "全部", "api": "normal", "rid": 0},
    {"key": "anime", "name": "番劇", "api": "pgc_web", "season_type": 1},
    {"key": "guochuang", "name": "國創", "api": "pgc_season", "season_type": 4},
    {"key": "documentary", "name": "紀錄片", "api": "pgc_season", "season_type": 3},
    {"key": "movie", "name": "電影", "api": "pgc_season", "season_type": 2},
    {"key": "tv", "name": "電視劇", "api": "pgc_season", "season_type": 5},
    {"key": "variety", "name": "綜藝", "api": "pgc_season", "season_type": 7},
    {"key": "animation", "name": "動畫", "api": "normal", "rid": 1},
    {"key": "game", "name": "遊戲", "api": "normal", "rid": 4},
    {"key": "kichiku", "name": "鬼畜", "api": "normal", "rid": 119},
    {"key": "music", "name": "音樂", "api": "normal", "rid": 3},
    {"key": "dance", "name": "舞蹈", "api": "normal", "rid": 129},
    {"key": "cinephile", "name": "影視", "api": "normal", "rid": 181},
    {"key": "entertainment", "name": "娛樂", "api": "normal", "rid": 5},
    {"key": "knowledge", "name": "知識", "api": "normal", "rid": 36},
    {"key": "tech", "name": "科技數碼", "api": "normal", "rid": 188},
    {"key": "food", "name": "美食", "api": "normal", "rid": 211},
    {"key": "car", "name": "汽車", "api": "normal", "rid": 223},
    {"key": "fashion", "name": "時尚美妝", "api": "normal", "rid": 155},
    {"key": "sports", "name": "體育運動", "api": "normal", "rid": 234},
    {"key": "animal", "name": "動物", "api": "normal", "rid": 217},
)


def read_daily_record(path, date):
    history = []
    today = {"date": date, "categories": {}}
    if not path.exists():
        return history, today
    with path.open("rb") as file:
        for line in file:
            try:
                record = json.loads(line)
            except json.JSONDecodeError:
                logging.warning("Ignoring malformed ranking record: %s", line.rstrip())
                continue
            if isinstance(record, dict):
                if record.get("date") == date:
                    today = record
                else:
                    history.append(line)
    return history, today


def optional_int(value):
    return value if isinstance(value, int) and not isinstance(value, bool) else None


def optional_number(value):
    return value if isinstance(value, (int, float)) and not isinstance(value, bool) else None


def parse_normal_ranking(payload):
    data = payload.get("data")
    if payload.get("code") != 0 or not isinstance(data, dict) or not isinstance(data.get("list"), list):
        raise ValueError(f"Unexpected Bilibili ranking response: {payload}")

    entries = []
    for item in data["list"]:
        if not isinstance(item, dict):
            continue
        bvid = item.get("bvid")
        title = item.get("title")
        if not isinstance(bvid, str) or not bvid.strip() or not isinstance(title, str) or not title.strip():
            continue
        owner = item.get("owner") if isinstance(item.get("owner"), dict) else {}
        stat = item.get("stat") if isinstance(item.get("stat"), dict) else {}
        entries.append(
            {
                "position": len(entries) + 1,
                "content_id": bvid.strip(),
                "title": title.strip(),
                "uploader": owner.get("name") if isinstance(owner.get("name"), str) else "",
                "views": optional_int(stat.get("view")),
                "danmaku": optional_int(stat.get("danmaku")),
                "favorites": optional_int(stat.get("favorite")),
                "likes": optional_int(stat.get("like")),
                "coins": optional_int(stat.get("coin")),
                "shares": optional_int(stat.get("share")),
                "followers": None,
                "rating": None,
                "ranking_score": optional_int(item.get("score")),
                "progress": "",
                "duration_seconds": optional_int(item.get("duration")),
                "published_at_unix": optional_int(item.get("pubdate")),
                "cover_url": item.get("pic") if isinstance(item.get("pic"), str) else "",
                "url": f"https://www.bilibili.com/video/{bvid.strip()}",
            }
        )
    if not entries:
        raise ValueError("Bilibili ranking response contained no usable entries.")
    return entries


def parse_pgc_ranking(payload):
    container = payload.get("result") if isinstance(payload.get("result"), dict) else payload.get("data")
    if payload.get("code") != 0 or not isinstance(container, dict) or not isinstance(container.get("list"), list):
        raise ValueError(f"Unexpected Bilibili PGC ranking response: {payload}")

    entries = []
    for item in container["list"]:
        if not isinstance(item, dict) or not isinstance(item.get("title"), str) or not item["title"].strip():
            continue
        stat = item.get("stat") if isinstance(item.get("stat"), dict) else {}
        rating = item.get("rating") if isinstance(item.get("rating"), dict) else {}
        new_ep = item.get("new_ep") if isinstance(item.get("new_ep"), dict) else {}
        season_id = item.get("season_id")
        url = item.get("url") if isinstance(item.get("url"), str) else ""
        entries.append(
            {
                "position": len(entries) + 1,
                "content_id": str(season_id) if season_id is not None else url,
                "title": item["title"].strip(),
                "uploader": "",
                "views": optional_int(stat.get("view")),
                "danmaku": optional_int(stat.get("danmaku")),
                "favorites": None,
                "likes": None,
                "coins": None,
                "shares": None,
                "followers": optional_int(stat.get("follow")),
                "rating": optional_number(rating.get("score")),
                "ranking_score": None,
                "progress": (
                    new_ep.get("index_show")
                    if isinstance(new_ep.get("index_show"), str)
                    else item.get("desc") if isinstance(item.get("desc"), str) else ""
                ),
                "duration_seconds": None,
                "published_at_unix": None,
                "cover_url": item.get("cover") if isinstance(item.get("cover"), str) else "",
                "url": url,
            }
        )
    if not entries:
        raise ValueError("Bilibili PGC ranking response contained no usable entries.")
    return entries


def category_request(category):
    if category["api"] == "normal":
        return NORMAL_RANKING_PATH, {"rid": category["rid"], "type": "all"}
    path = PGC_WEB_RANKING_PATH if category["api"] == "pgc_web" else PGC_SEASON_RANKING_PATH
    return path, {"day": 3, "season_type": category["season_type"]}


def fetch_category(category, session, sleep=time.sleep):
    path, params = category_request(category)
    headers = {"User-Agent": USER_AGENT, "Referer": "https://www.bilibili.com/v/popular/rank/all/"}
    last_error = None
    for attempt in range(1, MAX_REQUEST_ATTEMPTS + 1):
        try:
            response = session.get(
                API_ROOT + path,
                params=params,
                headers=headers,
                timeout=REQUEST_TIMEOUT_SECONDS,
            )
            if response.status_code in RETRY_STATUS_CODES:
                last_error = RuntimeError(f"HTTP {response.status_code}")
                if attempt < MAX_REQUEST_ATTEMPTS:
                    sleep(attempt)
                    continue
            response.raise_for_status()
            payload = response.json()
            parser = parse_normal_ranking if category["api"] == "normal" else parse_pgc_ranking
            return parser(payload)
        except (request_exceptions.RequestException, ValueError) as error:
            last_error = error
            if attempt < MAX_REQUEST_ATTEMPTS:
                sleep(attempt)
    raise RuntimeError(f"Unable to fetch Bilibili ranking category {category['name']}: {last_error}") from last_error


def archive_history(staged, path, date):
    for number in count(1):
        prefix = date if number == 1 else f"{date}-{number}"
        archive = path.with_name(f"{prefix}-{path.name}")
        try:
            archive.hardlink_to(staged)
        except FileExistsError:
            # A retry may find the archive already saved before the active file was replaced.
            if not filecmp.cmp(archive, staged, shallow=False):
                continue
        logging.info("Archived previous rankings to %s.", archive)
        return


def write_daily_record(history, record, path):
    entry = (json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8")
    if len(entry) > MAX_RANKING_BYTES:
        raise ValueError("One daily ranking exceeds the file size limit.")
    separator = b"\n" if history and not history[-1].endswith(b"\n") else b""
    history_size = sum(map(len, history))
    rotate = history_size + len(separator) + len(entry) > MAX_RANKING_BYTES
    if history_size > MAX_RANKING_BYTES:
        raise ValueError("Ranking history already exceeds the archive size limit.")

    path.parent.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(dir=path.parent) as directory:
        active = Path(directory) / path.name
        with active.open("wb") as file:
            if not rotate:
                file.writelines(history)
                file.write(separator)
            file.write(entry)
        if rotate:
            staged = Path(directory) / "history"
            with staged.open("wb") as file:
                file.writelines(history)
            archive_history(staged, path, record["date"])
        active.replace(path)


def record_daily_ranking(now=None, path=RANKING_LOG, session=None, sleep=time.sleep):
    now = now or datetime.now(TAIPEI)
    now = now.replace(tzinfo=TAIPEI) if now.tzinfo is None else now.astimezone(TAIPEI)
    date = now.date().isoformat()
    history, record = read_daily_record(path, date)
    categories = record.get("categories") if isinstance(record.get("categories"), dict) else {}
    record["categories"] = categories

    expected_keys = {category["key"] for category in RANKING_CATEGORIES}
    if expected_keys.issubset(categories):
        logging.info("All ranking categories for %s are already recorded.", date)
        return False

    session = session or Session()
    successful = 0
    for category in RANKING_CATEGORIES:
        if category["key"] in categories:
            continue
        try:
            entries = fetch_category(category, session, sleep=sleep)
        except RuntimeError as error:
            logging.error("%s", error)
            continue
        categories[category["key"]] = {
            "name": category["name"],
            "entries": entries,
        }
        successful += 1

    if successful == 0 and not categories:
        return False
    record["captured_at_taipei"] = now.isoformat(timespec="seconds")
    record["complete"] = expected_keys.issubset(categories)
    record["category_count"] = len(categories)
    write_daily_record(history, record, path)
    logging.info("Recorded %s/%s Bilibili ranking categories for %s.", len(categories), len(expected_keys), date)
    return successful > 0


def main():
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s: %(message)s")
    record_daily_ranking()


if __name__ == "__main__":
    main()
