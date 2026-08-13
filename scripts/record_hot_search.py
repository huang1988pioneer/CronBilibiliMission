#!/usr/bin/env python3
import json
import logging
import time
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

from requests import Session
from requests import exceptions as request_exceptions


HOT_SEARCH_URL = "https://s.search.bilibili.com/main/hotword"
HOT_SEARCH_LOG = Path("logs") / "hot_search.jsonl"
TAIPEI = ZoneInfo("Asia/Taipei")
USER_AGENT = (
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36"
)
REQUEST_TIMEOUT_SECONDS = 20
MAX_REQUEST_ATTEMPTS = 3
RETRY_STATUS_CODES = {429, 500, 502, 503, 504}
WORD_TYPE_LABELS = {
    4: "新",
    5: "熱",
    7: "直播中",
    9: "梗",
    11: "話題",
    12: "獨家",
}


def read_records(path=HOT_SEARCH_LOG):
    if not path.exists():
        return []

    records = []
    with path.open("r", encoding="utf-8") as file:
        for line in file:
            try:
                record = json.loads(line)
            except json.JSONDecodeError:
                logging.warning("Ignoring malformed hot-search record: %s", line.rstrip())
                continue
            if isinstance(record, dict):
                records.append(record)
    return records


def has_record_for_date(date, path=HOT_SEARCH_LOG):
    return any(record.get("date") == date for record in read_records(path))


def parse_hot_search(payload):
    if payload.get("code") != 0 or not isinstance(payload.get("list"), list):
        raise ValueError(f"Unexpected Bilibili hot-search response: {payload}")

    entries = []
    for fallback_position, item in enumerate(payload["list"], start=1):
        if not isinstance(item, dict):
            continue
        keyword = item.get("show_name") or item.get("keyword")
        if not isinstance(keyword, str) or not keyword.strip():
            continue

        position = item.get("pos")
        if not isinstance(position, int) or isinstance(position, bool):
            position = fallback_position
        word_type = item.get("word_type")
        entries.append(
            {
                "position": position,
                "keyword": keyword.strip(),
                "label": WORD_TYPE_LABELS.get(word_type, ""),
            }
        )

    if not entries:
        raise ValueError("Bilibili hot-search response contained no usable entries.")
    return entries


def fetch_hot_search(session=None, sleep=time.sleep):
    session = session or Session()
    headers = {
        "User-Agent": USER_AGENT,
        "Referer": "https://www.bilibili.com/",
    }
    last_error = None
    for attempt in range(1, MAX_REQUEST_ATTEMPTS + 1):
        try:
            response = session.get(
                HOT_SEARCH_URL,
                headers=headers,
                timeout=REQUEST_TIMEOUT_SECONDS,
            )
            if response.status_code in RETRY_STATUS_CODES:
                last_error = RuntimeError(f"HTTP {response.status_code}")
                if attempt < MAX_REQUEST_ATTEMPTS:
                    sleep(attempt)
                    continue
            response.raise_for_status()
            return parse_hot_search(response.json())
        except (request_exceptions.RequestException, ValueError) as error:
            last_error = error
            if attempt < MAX_REQUEST_ATTEMPTS:
                sleep(attempt)

    raise RuntimeError(f"Unable to fetch Bilibili hot search: {last_error}") from last_error


def record_daily_hot_search(now=None, path=HOT_SEARCH_LOG, session=None, sleep=time.sleep):
    now = now or datetime.now(TAIPEI)
    if now.tzinfo is None:
        now = now.replace(tzinfo=TAIPEI)
    else:
        now = now.astimezone(TAIPEI)
    date = now.date().isoformat()

    if has_record_for_date(date, path):
        logging.info("Hot search for %s is already recorded.", date)
        return False

    entries = fetch_hot_search(session=session, sleep=sleep)
    record = {
        "date": date,
        "captured_at_taipei": now.isoformat(timespec="seconds"),
        "entries": entries,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as file:
        file.write(json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n")
    logging.info("Recorded %s Bilibili hot-search entries for %s.", len(entries), date)
    return True


def main():
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s: %(message)s")
    record_daily_hot_search()


if __name__ == "__main__":
    main()
