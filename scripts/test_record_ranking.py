import json
import tempfile
import unittest
from datetime import datetime
from pathlib import Path
from unittest.mock import patch
from zoneinfo import ZoneInfo

import record_ranking


def normal_payload(number=1):
    return {"code": 0, "data": {"list": [{
        "bvid": f"BV{number}", "title": f"影片 {number}", "owner": {"name": "UP"},
        "stat": {"view": 1000, "danmaku": 100, "favorite": 50, "like": 200, "coin": 80, "share": 20},
        "score": 9999, "duration": 180, "pubdate": 1786636800, "pic": "https://i.example/1.jpg",
    }]}}


def pgc_payload(title="番劇 1"):
    return {"code": 0, "result": {"list": [{
        "season_id": 123, "title": title, "url": "https://www.bilibili.com/bangumi/play/ss123",
        "cover": "https://i.example/pgc.jpg", "new_ep": {"index_show": "更新至第 3 話"},
        "rating": {"score": 9.8}, "stat": {"view": 2000, "danmaku": 200, "follow": 300},
    }]}}


class FakeResponse:
    status_code = 200
    def __init__(self, payload): self.payload = payload
    def raise_for_status(self): return None
    def json(self): return self.payload


class FakeSession:
    def __init__(self, failing_keys=None):
        self.failing_keys = set(failing_keys or [])
        self.calls = []

    def get(self, url, **kwargs):
        params = kwargs["params"]
        key = (url, tuple(sorted(params.items())))
        self.calls.append(key)
        if any(value in self.failing_keys for value in params.values()):
            raise record_ranking.request_exceptions.ConnectionError("temporary")
        if "/pgc/" in url:
            return FakeResponse(pgc_payload())
        return FakeResponse(normal_payload(params["rid"] + 1))


class RankingRecordTests(unittest.TestCase):
    def test_category_catalog_matches_every_visible_tab(self):
        self.assertEqual(21, len(record_ranking.RANKING_CATEGORIES))
        self.assertEqual(
            ["全部", "番劇", "國創", "紀錄片", "電影", "電視劇", "綜藝", "動畫", "遊戲", "鬼畜", "音樂", "舞蹈", "影視", "娛樂", "知識", "科技數碼", "美食", "汽車", "時尚美妝", "體育運動", "動物"],
            [category["name"] for category in record_ranking.RANKING_CATEGORIES],
        )

    def test_records_all_categories_grouped_in_one_daily_snapshot(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ranking.jsonl"
            recorded = record_ranking.record_daily_ranking(
                now=datetime(2026, 8, 14, 10, tzinfo=ZoneInfo("Asia/Taipei")),
                path=path, session=FakeSession(), sleep=lambda _seconds: None,
            )
            record = json.loads(path.read_text(encoding="utf-8"))
            self.assertTrue(recorded)
            self.assertTrue(record["complete"])
            self.assertEqual(21, record["category_count"])
            self.assertEqual("全部", record["categories"]["all"]["name"])
            self.assertEqual("番劇 1", record["categories"]["anime"]["entries"][0]["title"])
            self.assertEqual("BV1", record["categories"]["all"]["entries"][0]["content_id"])

    def test_partial_snapshot_is_completed_without_refetching_successes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ranking.jsonl"
            date = "2026-08-14"
            existing = {"date": date, "complete": False, "category_count": 1, "categories": {"all": {"name": "全部", "entries": []}}}
            path.write_text(json.dumps(existing) + "\n", encoding="utf-8")
            session = FakeSession()
            record_ranking.record_daily_ranking(
                now=datetime(2026, 8, 14, 11, tzinfo=ZoneInfo("Asia/Taipei")),
                path=path, session=session, sleep=lambda _seconds: None,
            )
            record = json.loads(path.read_text(encoding="utf-8"))
            self.assertTrue(record["complete"])
            self.assertEqual(20, len(session.calls))
            self.assertEqual(1, len(path.read_text(encoding="utf-8").splitlines()))

    def test_complete_same_day_snapshot_does_not_fetch_again(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ranking.jsonl"
            categories = {category["key"]: {"name": category["name"], "entries": []} for category in record_ranking.RANKING_CATEGORIES}
            path.write_text(json.dumps({"date": "2026-08-14", "categories": categories}) + "\n", encoding="utf-8")
            session = FakeSession()
            recorded = record_ranking.record_daily_ranking(
                now=datetime(2026, 8, 14, 23, tzinfo=ZoneInfo("Asia/Taipei")),
                path=path, session=session, sleep=lambda _seconds: None,
            )
            self.assertFalse(recorded)
            self.assertEqual([], session.calls)

    def test_rotation_uses_utf8_bytes_and_allows_an_exact_fit(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ranking.jsonl"
            history = '{"date":"2026-08-13","title":"影片"}'.encode("utf-8")
            record = {"date": "2026-08-14", "title": "動畫"}
            entry = (json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8")
            combined = history + b"\n" + entry
            archive = path.with_name("2026-08-14-ranking.jsonl")
            with patch.object(record_ranking, "MAX_RANKING_BYTES", len(combined)):
                record_ranking.write_daily_record([history], record, path)
            self.assertEqual(combined, path.read_bytes())
            self.assertFalse(archive.exists())
            with patch.object(record_ranking, "MAX_RANKING_BYTES", len(combined) - 1):
                record_ranking.write_daily_record([history], record, path)
            self.assertEqual(history, archive.read_bytes())
            self.assertEqual(entry, path.read_bytes())

    def test_rotation_during_retry_keeps_only_one_snapshot_for_today(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ranking.jsonl"
            history = json.dumps({"date": "2026-08-13", "padding": "x" * 20000}).encode() + b"\n"
            partial = {"date": "2026-08-14", "categories": {"all": {"name": "全部", "entries": []}}}
            path.write_bytes(history + json.dumps(partial).encode() + b"\n")
            now = datetime(2026, 8, 13, 18, tzinfo=ZoneInfo("UTC"))
            session = FakeSession()
            with patch.object(record_ranking, "MAX_RANKING_BYTES", path.stat().st_size):
                self.assertTrue(record_ranking.record_daily_ranking(now, path, session))
                self.assertFalse(record_ranking.record_daily_ranking(now, path, session))
            self.assertEqual(20, len(session.calls))
            self.assertEqual(history, path.with_name("2026-08-14-ranking.jsonl").read_bytes())
            record = json.loads(path.read_bytes())
            self.assertTrue(record["complete"])
            self.assertEqual(partial["categories"]["all"], record["categories"]["all"])

    def test_existing_archive_is_preserved_and_retry_reuses_matching_archive(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ranking.jsonl"
            archive = path.with_name("2026-08-14-ranking.jsonl")
            archive.write_bytes(b'{"date":"2026-08-12"}\n')
            history = b'{"date":"2026-08-13"}\n'
            record = {"date": "2026-08-14"}
            with patch.object(record_ranking, "MAX_RANKING_BYTES", 30):
                for _ in range(2):
                    record_ranking.write_daily_record([history], record, path)
            self.assertEqual(b'{"date":"2026-08-12"}\n', archive.read_bytes())
            self.assertEqual(history, path.with_name("2026-08-14-2-ranking.jsonl").read_bytes())
            self.assertEqual(3, len(list(path.parent.iterdir())))

    def test_oversized_single_snapshot_leaves_the_old_log_intact(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ranking.jsonl"
            history = b'{"date":"2026-08-13"}\n'
            path.write_bytes(history)
            with patch.object(record_ranking, "MAX_RANKING_BYTES", len(history)):
                with self.assertRaises(ValueError):
                    record_ranking.write_daily_record([history], {"date": "2026-08-14", "title": "動畫"}, path)
            self.assertEqual(history, path.read_bytes())
            self.assertEqual([path], list(path.parent.iterdir()))

    def test_pgc_parser_keeps_rating_progress_and_followers(self):
        entry = record_ranking.parse_pgc_ranking(pgc_payload())[0]
        self.assertEqual(9.8, entry["rating"])
        self.assertEqual("更新至第 3 話", entry["progress"])
        self.assertEqual(300, entry["followers"])

    def test_invalid_payload_is_rejected(self):
        with self.assertRaises(ValueError):
            record_ranking.parse_normal_ranking({"code": -1})


if __name__ == "__main__":
    unittest.main()
