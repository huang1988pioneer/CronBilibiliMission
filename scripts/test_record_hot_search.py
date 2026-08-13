import json
import tempfile
import unittest
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

import record_hot_search


class FakeResponse:
    status_code = 200

    def __init__(self, payload):
        self.payload = payload

    def raise_for_status(self):
        return None

    def json(self):
        return self.payload


class FakeSession:
    def __init__(self, payload):
        self.payload = payload
        self.calls = 0

    def get(self, *_args, **_kwargs):
        self.calls += 1
        return FakeResponse(self.payload)


def payload():
    return {
        "code": 0,
        "list": [
            {"pos": 1, "show_name": "第一名", "keyword": "ignored", "word_type": 4},
            {"pos": 2, "keyword": "第二名", "word_type": 8},
        ],
    }


class HotSearchRecordTests(unittest.TestCase):
    def test_records_full_list_with_taipei_date(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "hot_search.jsonl"
            session = FakeSession(payload())

            recorded = record_hot_search.record_daily_hot_search(
                now=datetime(2026, 8, 14, 8, 30, tzinfo=ZoneInfo("Asia/Taipei")),
                path=path,
                session=session,
                sleep=lambda _seconds: None,
            )

            self.assertTrue(recorded)
            record = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("2026-08-14", record["date"])
            self.assertEqual("第一名", record["entries"][0]["keyword"])
            self.assertEqual("新", record["entries"][0]["label"])
            self.assertEqual("", record["entries"][1]["label"])

    def test_same_day_does_not_fetch_or_append_again(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "hot_search.jsonl"
            path.write_text(
                json.dumps({"date": "2026-08-14", "entries": []}) + "\n",
                encoding="utf-8",
            )
            session = FakeSession(payload())

            recorded = record_hot_search.record_daily_hot_search(
                now=datetime(2026, 8, 14, 23, 59, tzinfo=ZoneInfo("Asia/Taipei")),
                path=path,
                session=session,
                sleep=lambda _seconds: None,
            )

            self.assertFalse(recorded)
            self.assertEqual(0, session.calls)
            self.assertEqual(1, len(path.read_text(encoding="utf-8").splitlines()))

    def test_utc_time_is_converted_to_taipei_date(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "hot_search.jsonl"

            record_hot_search.record_daily_hot_search(
                now=datetime(2026, 8, 13, 16, 30, tzinfo=ZoneInfo("UTC")),
                path=path,
                session=FakeSession(payload()),
                sleep=lambda _seconds: None,
            )

            record = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("2026-08-14", record["date"])

    def test_invalid_payload_is_rejected(self):
        with self.assertRaises(ValueError):
            record_hot_search.parse_hot_search({"code": -1, "list": []})


if __name__ == "__main__":
    unittest.main()
