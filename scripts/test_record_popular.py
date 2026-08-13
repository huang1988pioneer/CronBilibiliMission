import json
import tempfile
import unittest
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

import record_popular


def video(number):
    return {
        "bvid": f"BV{number}",
        "title": f"熱門影片 {number}",
        "owner": {"name": f"UP {number}"},
        "stat": {"view": 100 * number, "danmaku": 10 * number, "favorite": 5, "like": 20},
        "duration": 180,
        "pubdate": 1786636800,
        "rcmd_reason": {"content": "熱門理由"},
        "pic": f"https://i.example/{number}.jpg",
    }


class FakeResponse:
    status_code = 200

    def __init__(self, payload):
        self.payload = payload

    def raise_for_status(self):
        return None

    def json(self):
        return self.payload


class FakeSession:
    def __init__(self, pages):
        self.pages = pages
        self.calls = []

    def get(self, _url, **kwargs):
        page_number = kwargs["params"]["pn"]
        self.calls.append(page_number)
        return FakeResponse(self.pages[page_number - 1])


def page(items, no_more):
    return {"code": 0, "data": {"list": items, "no_more": no_more}}


class PopularRecordTests(unittest.TestCase):
    def test_records_all_pages_and_fields(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "popular.jsonl"
            session = FakeSession([page([video(1), video(2)], False), page([video(3)], True)])

            recorded = record_popular.record_daily_popular(
                now=datetime(2026, 8, 14, 9, 0, tzinfo=ZoneInfo("Asia/Taipei")),
                path=path,
                session=session,
                sleep=lambda _seconds: None,
            )

            self.assertTrue(recorded)
            record = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("2026-08-14", record["date"])
            self.assertEqual([1, 2], session.calls)
            self.assertEqual(3, len(record["entries"]))
            self.assertEqual("熱門影片 1", record["entries"][0]["title"])
            self.assertEqual("UP 1", record["entries"][0]["uploader"])
            self.assertEqual(100, record["entries"][0]["views"])
            self.assertEqual("https://www.bilibili.com/video/BV1", record["entries"][0]["url"])

    def test_duplicate_video_across_pages_is_recorded_once(self):
        session = FakeSession([page([video(1)], False), page([video(1), video(2)], True)])

        entries = record_popular.fetch_popular(session=session, sleep=lambda _seconds: None)

        self.assertEqual(["BV1", "BV2"], [entry["bvid"] for entry in entries])
        self.assertEqual([1, 2], [entry["position"] for entry in entries])

    def test_same_day_does_not_fetch_or_append_again(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "popular.jsonl"
            path.write_text(json.dumps({"date": "2026-08-14", "entries": []}) + "\n", encoding="utf-8")
            session = FakeSession([])

            recorded = record_popular.record_daily_popular(
                now=datetime(2026, 8, 14, 23, 0, tzinfo=ZoneInfo("Asia/Taipei")),
                path=path,
                session=session,
                sleep=lambda _seconds: None,
            )

            self.assertFalse(recorded)
            self.assertEqual([], session.calls)
            self.assertEqual(1, len(path.read_text(encoding="utf-8").splitlines()))

    def test_invalid_payload_is_rejected(self):
        with self.assertRaises(ValueError):
            record_popular.parse_popular_page({"code": -1, "data": {}})


if __name__ == "__main__":
    unittest.main()
