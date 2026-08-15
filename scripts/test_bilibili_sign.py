import unittest
import os
from datetime import datetime
from unittest import mock

import bilibili_sign


def login(level, coins):
    return {
        "account_coins": coins,
        "level_info": {"current_level": level},
    }


def reward(coin_experience):
    return {"data": {"coins": coin_experience}}


class FakeClient:
    def __init__(self):
        self.next_video = 1
        self.given = []

    def get_video_info(self, exclude_bvids=None):
        number = self.next_video
        self.next_video += 1
        return {
            "aid": number,
            "bvid": f"BV{number}",
            "title": f"Video {number}",
        }

    def give_coins(self, video, multiply):
        self.given.append((video["bvid"], multiply))
        return {
            "status": "coins_given",
            "message": "OK",
            "coins": multiply,
            "video": video,
        }


class CoinTaskTests(unittest.TestCase):
    def test_cookie_includes_buvid3_for_interaction_apis(self):
        with mock.patch.dict(
            os.environ,
            {
                "SESSDATA": "session",
                "BILI_JCT": "csrf",
                "DEDEUSERID": "123",
                "BUVID3": "device-id",
            },
            clear=True,
        ):
            auth = bilibili_sign.build_cookie()

        self.assertIn("buvid3=device-id", auth["cookie"])

    def test_daily_coin_experience_uses_current_api(self):
        client = bilibili_sign.BilibiliClient("SESSDATA=test", "test-csrf")
        client.request_json = mock.Mock(return_value={"code": 0, "data": 30})

        result = client.get_daily_coin_experience()

        self.assertEqual(30, result)
        client.request_json.assert_called_once_with(
            "https://api.bilibili.com/x/web-interface/coin/today/exp"
        )

    def test_level_6_is_never_eligible(self):
        plan = bilibili_sign.coin_task_plan(login(6, 999), reward(0))

        self.assertFalse(plan["eligible"])
        self.assertEqual("level_6", plan["reason"])

    def test_unknown_level_is_not_eligible(self):
        plan = bilibili_sign.coin_task_plan(login(None, 999), reward(0))

        self.assertFalse(plan["eligible"])
        self.assertEqual("unknown_level", plan["reason"])

    def test_balance_must_be_strictly_over_333(self):
        plan = bilibili_sign.coin_task_plan(login(5, 333), reward(0))

        self.assertFalse(plan["eligible"])
        self.assertEqual("balance_not_over_333", plan["reason"])

    def test_only_missing_daily_experience_is_planned(self):
        plan = bilibili_sign.coin_task_plan(login(5, 334), reward(20))

        self.assertTrue(plan["eligible"])
        self.assertEqual(3, plan["coins"])

    def test_realtime_experience_overrides_delayed_reward_value(self):
        plan = bilibili_sign.coin_task_plan(login(5, 500), reward(0), 40)

        self.assertTrue(plan["eligible"])
        self.assertEqual(1, plan["coins"])

    def test_five_coins_are_split_across_random_videos(self):
        client = FakeClient()
        plan = bilibili_sign.coin_task_plan(login(5, 500), reward(0))

        result = bilibili_sign.run_coin_task(client, plan)

        self.assertEqual("coins_given", result["status"])
        self.assertEqual(5, result["coins_spent"])
        self.assertEqual([2, 2, 1], [multiply for _, multiply in client.given])
        self.assertEqual(3, len({bvid for bvid, _ in client.given}))


class CoinBalanceNotificationTests(unittest.TestCase):
    def test_decreasing_balance_is_not_a_stagnant_streak(self):
        previous_records = [
            {"date": "2026-08-13", "account_coins": 364.7},
            {"date": "2026-08-14", "account_coins": 360.7},
        ]

        with mock.patch.object(
            bilibili_sign,
            "coin_records_before",
            return_value=previous_records,
        ):
            streak = bilibili_sign.stagnant_coin_balance_streak(
                "2026-08-15",
                356.7,
            )

        self.assertEqual(
            [{"date": "2026-08-15", "account_coins": 356.7}],
            [
                {
                    "date": record["date"],
                    "account_coins": record["account_coins"],
                }
                for record in streak
            ],
        )

    def test_alert_sent_earlier_on_same_date_is_not_sent_again(self):
        events = [
            {
                "date": "2026-08-15",
                "coin_balance_notification": {"status": "email_sent"},
            }
        ]

        with mock.patch.object(bilibili_sign, "read_event_log", return_value=events):
            already_sent = bilibili_sign.coin_balance_alert_already_sent(
                "2026-08-13",
                "2026-08-15",
            )

        self.assertTrue(already_sent)

    def test_second_account_email_lists_second_secret_names(self):
        streak = [
            {"date": "2026-08-13", "account_coins": 360.7},
            {"date": "2026-08-14", "account_coins": 360.7},
            {"date": "2026-08-15", "account_coins": 360.7},
        ]
        result = {"login": {"uname": "FENGTUINFO"}}

        with mock.patch.object(bilibili_sign, "ACCOUNT_NAME", "abuhg17"):
            body = bilibili_sign.build_coin_balance_email_body(
                streak,
                360.7,
                result,
                "2026-08-15",
            )

        self.assertIn("SESSDATA2", body)
        self.assertIn("BILI_JCT2", body)
        self.assertIn("DEDEUSERID2", body)
        self.assertIn("BUVID32", body)


class LevelBreakthroughSummaryTests(unittest.TestCase):
    def test_completed_levels_are_omitted(self):
        breakthroughs = {
            "lv3": {"days_at_15_exp_per_day": 0},
            "lv4": {"days_at_15_exp_per_day": 0},
            "lv5": {"days_at_15_exp_per_day": 12},
            "lv6": {"days_at_15_exp_per_day": 100},
        }

        pending = bilibili_sign.pending_level_breakthroughs(breakthroughs)

        self.assertEqual([5, 6], [level for level, _ in pending])

    def test_no_rows_remain_when_all_levels_are_completed(self):
        breakthroughs = {
            f"lv{level}": {"days_at_15_exp_per_day": 0}
            for level in (3, 4, 5, 6)
        }

        self.assertEqual([], bilibili_sign.pending_level_breakthroughs(breakthroughs))


class AccountBanStatusTests(unittest.TestCase):
    def test_estimates_release_time_and_remaining_days(self):
        status = bilibili_sign.estimate_account_ban_status(
            "2026-05-15T23:05:00+08:00",
            365,
            now=datetime(2026, 8, 14, 13, 30, tzinfo=bilibili_sign.TAIPEI),
        )

        self.assertTrue(status["active"])
        self.assertEqual("2027-05-15T23:05:00+08:00", status["estimated_release_at"])
        self.assertEqual(275, status["remaining_days"])

    def test_status_is_no_longer_active_after_release(self):
        status = bilibili_sign.estimate_account_ban_status(
            "2026-05-15T23:05:00+08:00",
            365,
            now=datetime(2027, 5, 15, 23, 5, tzinfo=bilibili_sign.TAIPEI),
        )

        self.assertFalse(status["active"])
        self.assertEqual(0, status["remaining_days"])


if __name__ == "__main__":
    unittest.main()
