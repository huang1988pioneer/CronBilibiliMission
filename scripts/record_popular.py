#!/usr/bin/env python3
import json
import logging
import time
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

from requests import Session
from requests import exceptions as request_exceptions


POPULAR_URL = "https://api.bilibili.com/x/web-interface/popular"
POPULAR_LOG = Path("logs") / "popular.jsonl"
TAIPEI = ZoneInfo("Asia/Taipei")
USER_AGENT = (
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36"
)
REQUEST_TIMEOUT_SECONDS = 20
MAX_REQUEST_ATTEMPTS = 3
RETRY_STATUS_CODES = {429, 500, 502, 503, 504}
PAGE_SIZE = 50
MAX_PAGES = 20


def read_records(path=POPULAR_LOG):
    if not path.exists():
        return []

    records = []
    with path.open("r", encoding="utf-8") as file:
        for line in file:
            try:
                record = json.loads(line)
            except json.JSONDecodeError:
                logging.warning("Ignoring malformed popular-video record: %s", line.rstrip())
                continue
            if isinstance(record, dict):
                records.append(record)
    return records


def has_record_for_date(date, path=POPULAR_LOG):
    return any(record.get("date") == date for record in read_records(path))


def optional_int(value):
    return value if isinstance(value, int) and not isinstance(value, bool) else None


def parse_popular_page(payload, starting_position=1):
    data = payload.get("data")
    if payload.get("code") != 0 or not isinstance(data, dict) or not isinstance(data.get("list"), list):
        raise ValueError(f"Unexpected Bilibili popular response: {payload}")

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
        reason = item.get("rcmd_reason") if isinstance(item.get("rcmd_reason"), dict) else {}
        entries.append(
            {
                "position": starting_position + len(entries),
                "bvid": bvid.strip(),
                "title": title.strip(),
                "uploader": owner.get("name") if isinstance(owner.get("name"), str) else "",
                "views": optional_int(stat.get("view")),
                "danmaku": optional_int(stat.get("danmaku")),
                "favorites": optional_int(stat.get("favorite")),
                "likes": optional_int(stat.get("like")),
                "duration_seconds": optional_int(item.get("duration")),
                "published_at_unix": optional_int(item.get("pubdate")),
                "reason": reason.get("content") if isinstance(reason.get("content"), str) else "",
                "cover_url": item.get("pic") if isinstance(item.get("pic"), str) else "",
                "url": f"https://www.bilibili.com/video/{bvid.strip()}",
            }
        )

    return entries, data.get("no_more") is True


def request_page(session, page_number, sleep=time.sleep):
    headers = {
        "User-Agent": USER_AGENT,
        "Referer": "https://www.bilibili.com/v/popular/all/",
    }
    last_error = None
    for attempt in range(1, MAX_REQUEST_ATTEMPTS + 1):
        try:
            response = session.get(
                POPULAR_URL,
                params={"pn": page_number, "ps": PAGE_SIZE},
                headers=headers,
                timeout=REQUEST_TIMEOUT_SECONDS,
            )
            if response.status_code in RETRY_STATUS_CODES:
                last_error = RuntimeError(f"HTTP {response.status_code}")
                if attempt < MAX_REQUEST_ATTEMPTS:
                    sleep(attempt)
                    continue
            response.raise_for_status()
            return response.json()
        except (request_exceptions.RequestException, ValueError) as error:
            last_error = error
            if attempt < MAX_REQUEST_ATTEMPTS:
                sleep(attempt)

    raise RuntimeError(f"Unable to fetch Bilibili popular page {page_number}: {last_error}") from last_error


def fetch_popular(session=None, sleep=time.sleep):
    session = session or Session()
    entries = []
    seen_bvids = set()
    for page_number in range(1, MAX_PAGES + 1):
        page_entries, no_more = parse_popular_page(
            request_page(session, page_number, sleep=sleep),
            starting_position=len(entries) + 1,
        )
        new_entries = [entry for entry in page_entries if entry["bvid"] not in seen_bvids]
        entries.extend(new_entries)
        seen_bvids.update(entry["bvid"] for entry in new_entries)
        if no_more or not page_entries:
            break

    if not entries:
        raise ValueError("Bilibili popular response contained no usable entries.")
    for position, entry in enumerate(entries, start=1):
        entry["position"] = position
    return entries


def record_daily_popular(now=None, path=POPULAR_LOG, session=None, sleep=time.sleep):
    now = now or datetime.now(TAIPEI)
    if now.tzinfo is None:
        now = now.replace(tzinfo=TAIPEI)
    else:
        now = now.astimezone(TAIPEI)
    date = now.date().isoformat()

    if has_record_for_date(date, path):
        logging.info("Popular videos for %s are already recorded.", date)
        return False

    entries = fetch_popular(session=session, sleep=sleep)
    record = {
        "date": date,
        "captured_at_taipei": now.isoformat(timespec="seconds"),
        "entries": entries,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as file:
        file.write(json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n")
    logging.info("Recorded %s Bilibili popular videos for %s.", len(entries), date)
    return True


def main():
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s: %(message)s")
    record_daily_popular()


if __name__ == "__main__":
    main()
