using System;
using System.Collections.Generic;
using System.IO;
using UsagePeek;

internal static class ResetCreditNotificationQa
{
    private static int failures;
    private static readonly DateTime StartUtc =
        new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

    private static void Main()
    {
        FirstObservationOnlyEstablishesBaseline();
        CountIncreaseNotifiesExactlyOnce();
        SameCountNewGrantNotifies();
        OldDetailsAppearingLaterDoNotNotify();
        DecreaseDoesNotNotify();
        StateSurvivesRestart();

        if (failures > 0)
        {
            Console.Error.WriteLine(
                "Reset credit notification QA failed: " + failures);
            Environment.Exit(1);
        }

        Console.WriteLine("Reset credit notification QA passed.");
    }

    private static void FirstObservationOnlyEstablishesBaseline()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            ResetCreditGrantNotification notification = tracker.Observe(
                Snapshot(1, StartUtc,
                    Credit(StartUtc.AddHours(-1), StartUtc.AddDays(1), "Existing")));
            Check(notification == null,
                "first observation establishes a silent baseline");
        });
    }

    private static void CountIncreaseNotifiesExactlyOnce()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            tracker.Observe(Snapshot(0, StartUtc));

            UsageSnapshot granted = Snapshot(1, StartUtc.AddMinutes(5),
                Credit(StartUtc.AddMinutes(2), StartUtc.AddDays(1), "New"));
            ResetCreditGrantNotification notification = tracker.Observe(granted);
            Check(notification != null && notification.NewCreditCount == 1 &&
                notification.AvailableCount == 1,
                "an increased available count notifies once");
            Check(tracker.Observe(granted) == null,
                "the same grant is not notified twice");
        });
    }

    private static void SameCountNewGrantNotifies()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            tracker.Observe(Snapshot(1, StartUtc,
                Credit(StartUtc.AddHours(-2), StartUtc.AddMinutes(1), "Old")));

            ResetCreditGrantNotification notification = tracker.Observe(
                Snapshot(1, StartUtc.AddMinutes(10),
                    Credit(StartUtc.AddMinutes(4), StartUtc.AddDays(2), "Replacement")));
            Check(notification != null && notification.NewCreditCount == 1,
                "a newly granted replacement card notifies at the same count");
        });
    }

    private static void OldDetailsAppearingLaterDoNotNotify()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            tracker.Observe(Snapshot(1, StartUtc));

            ResetCreditGrantNotification notification = tracker.Observe(
                Snapshot(1, StartUtc.AddMinutes(5),
                    Credit(StartUtc.AddHours(-3), StartUtc.AddDays(1), "Existing")));
            Check(notification == null,
                "late-arriving details for an old card do not notify");
        });
    }

    private static void DecreaseDoesNotNotify()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            tracker.Observe(Snapshot(1, StartUtc,
                Credit(StartUtc.AddHours(-1), StartUtc.AddHours(1), "Existing")));
            Check(tracker.Observe(Snapshot(0, StartUtc.AddMinutes(5))) == null,
                "using or expiring a reset card does not notify");
        });
    }

    private static void StateSurvivesRestart()
    {
        WithStatePath(delegate(string path)
        {
            new ResetCreditNotificationTracker(path).Observe(
                Snapshot(0, StartUtc));

            UsageSnapshot granted = Snapshot(1, StartUtc.AddMinutes(5),
                Credit(StartUtc.AddMinutes(1), StartUtc.AddDays(1), "New"));
            ResetCreditGrantNotification notification =
                new ResetCreditNotificationTracker(path).Observe(granted);
            Check(notification != null,
                "persisted baseline detects a grant after restart");
            Check(new ResetCreditNotificationTracker(path).Observe(granted) == null,
                "persisted grant is not repeated after restart");
        });
    }

    private static UsageSnapshot Snapshot(
        int? available,
        DateTime fetchedAtUtc,
        params RateLimitResetCreditSnapshot[] credits)
    {
        return new UsageSnapshot
        {
            ProviderId = "chatgpt-codex",
            RateLimitResetCreditsAvailable = available,
            ResetCredits = new List<RateLimitResetCreditSnapshot>(credits),
            FetchedAtUtc = fetchedAtUtc
        };
    }

    private static RateLimitResetCreditSnapshot Credit(
        DateTime grantedAtUtc,
        DateTime expiresAtUtc,
        string title)
    {
        return new RateLimitResetCreditSnapshot
        {
            GrantedAtUtc = grantedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            Title = title
        };
    }

    private static void WithStatePath(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(),
            "UsagePeek-reset-credit-qa-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            action(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + message);
            return;
        }

        failures++;
        Console.Error.WriteLine("FAIL " + message);
    }
}
