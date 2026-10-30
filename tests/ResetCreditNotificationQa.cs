using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
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
        ExpiryReminderStartsAtTwentyFourHours();
        ExpiryReminderSkipsInvalidObservations();
        ExpiryReminderSupportsUnknownAvailableCount();
        ExpiryReminderSurvivesRestart();
        ExpiryReminderGroupsBatches();
        ExpiryReminderTracksBatchCountIncreases();
        ExpiryReminderIgnoresChangingDetails();
        ExpiryReminderSupportsLegacyState();

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

    private static void ExpiryReminderStartsAtTwentyFourHours()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            DateTime expiryUtc = StartUtc.AddDays(1);
            RateLimitResetCreditSnapshot credit =
                Credit(StartUtc.AddDays(-2), expiryUtc, "Existing");
            Check(tracker.ObserveExpiry(
                    Snapshot(1, StartUtc.AddTicks(-1), credit),
                    StartUtc.AddTicks(-1)) == null,
                "a card with more than 24 hours remaining does not notify");

            ResetCreditExpiryNotification notification = tracker.ObserveExpiry(
                Snapshot(1, StartUtc, credit), StartUtc);
            Check(notification != null && notification.ExpiringCreditCount == 1 &&
                    notification.EarliestExpiryUtc == expiryUtc,
                "a card notifies exactly at the 24-hour boundary");
            Check(tracker.ObserveExpiry(
                    Snapshot(1, StartUtc.AddMinutes(1), credit),
                    StartUtc.AddMinutes(1)) == null,
                "refreshing inside the reminder window does not repeat it");
        });
    }

    private static void ExpiryReminderSkipsInvalidObservations()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            DateTime expiryUtc = StartUtc.AddHours(23);
            RateLimitResetCreditSnapshot valid =
                Credit(StartUtc.AddDays(-2), expiryUtc, "Existing");
            Check(tracker.ObserveExpiry(null, StartUtc) == null,
                "a missing snapshot does not produce an expiry reminder");
            UsageSnapshot stale = Snapshot(1, StartUtc, valid);
            stale.IsStale = true;
            Check(tracker.ObserveExpiry(stale, StartUtc) == null,
                "cached or stale data does not produce an expiry reminder");
            Check(tracker.ObserveExpiry(Snapshot(0, StartUtc, valid), StartUtc) == null,
                "zero available cards suppress inconsistent expiry details");

            UsageSnapshot missingDetails = Snapshot(1, StartUtc);
            missingDetails.ResetCredits = null;
            Check(tracker.ObserveExpiry(missingDetails, StartUtc) == null,
                "missing card details do not produce an expiry reminder");
            Check(tracker.ObserveExpiry(Snapshot(1, StartUtc,
                    new RateLimitResetCreditSnapshot
                    {
                        GrantedAtUtc = StartUtc.AddDays(-1),
                        ExpiresAtUtc = null
                    }), StartUtc) == null,
                "a missing expiry date is not guessed");
            DateTime recentUtc = DateTime.UtcNow.AddHours(-1);
            Check(tracker.ObserveExpiry(Snapshot(1, recentUtc,
                    new RateLimitResetCreditSnapshot
                    {
                        ExpiresAtUtc = default(DateTime)
                    }), recentUtc) == null,
                "an uninitialized expiry timestamp is not replaced with the current time");
            Check(tracker.ObserveExpiry(Snapshot(1, StartUtc,
                    Credit(StartUtc.AddDays(-2), StartUtc.AddTicks(-1), "Expired")),
                    StartUtc) == null,
                "an already expired card does not notify");
            Check(tracker.ObserveExpiry(Snapshot(1, StartUtc,
                    Credit(StartUtc.AddDays(-2), StartUtc, "Expires now")),
                    StartUtc) == null,
                "a card at its exact expiry instant does not notify");

            ResetCreditExpiryNotification notification = tracker.ObserveExpiry(
                Snapshot(1, StartUtc, valid), StartUtc);
            Check(notification != null && notification.ExpiringCreditCount == 1,
                "ignored observations do not consume the future valid reminder");
        });
    }

    private static void ExpiryReminderSupportsUnknownAvailableCount()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditExpiryNotification notification =
                new ResetCreditNotificationTracker(path).ObserveExpiry(
                    Snapshot(null, StartUtc, new RateLimitResetCreditSnapshot
                    {
                        GrantedAtUtc = null,
                        ExpiresAtUtc = StartUtc.AddHours(8),
                        Title = "Available card"
                    }), StartUtc);
            Check(notification != null && notification.ExpiringCreditCount == 1,
                "available card details can notify without count or grant time");
        });
    }

    private static void ExpiryReminderSurvivesRestart()
    {
        WithStatePath(delegate(string path)
        {
            UsageSnapshot snapshot = Snapshot(1, StartUtc,
                Credit(StartUtc.AddDays(-2), StartUtc.AddHours(12), "Existing"));
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            ResetCreditExpiryNotification notification =
                tracker.ObserveExpiry(snapshot, StartUtc);
            Check(notification != null && notification.ExpiringCreditCount == 1,
                "first startup inside the 24-hour window reminds immediately");
            Check(tracker.Observe(snapshot) == null,
                "an expiry reminder retains the silent initial grant baseline");
            Check(new ResetCreditNotificationTracker(path).ObserveExpiry(
                    snapshot, StartUtc.AddMinutes(5)) == null,
                "the expiry reminder is not repeated after restarting");
        });
    }

    private static void ExpiryReminderGroupsBatches()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            RateLimitResetCreditSnapshot first =
                Credit(StartUtc.AddDays(-2), StartUtc.AddHours(2), "First");
            RateLimitResetCreditSnapshot second =
                Credit(StartUtc.AddDays(-2), StartUtc.AddHours(2), "Second");
            RateLimitResetCreditSnapshot later =
                Credit(StartUtc.AddDays(-1), StartUtc.AddHours(23), "Later");
            RateLimitResetCreditSnapshot future =
                Credit(StartUtc.AddDays(-1), StartUtc.AddHours(25), "Future");
            ResetCreditExpiryNotification notification = tracker.ObserveExpiry(
                Snapshot(4, StartUtc, first, second, later, future), StartUtc);
            Check(notification != null && notification.ExpiringCreditCount == 3 &&
                    notification.EarliestExpiryUtc == StartUtc.AddHours(2),
                "one reminder groups due cards and reports their earliest expiry");
            Check(tracker.ObserveExpiry(
                    Snapshot(4, StartUtc.AddMinutes(1), future, later, second, first),
                    StartUtc.AddMinutes(1)) == null,
                "reordering expiry batches does not repeat reminders");
            notification = tracker.ObserveExpiry(
                Snapshot(2, StartUtc.AddHours(2), later, future),
                StartUtc.AddHours(2));
            Check(notification != null && notification.ExpiringCreditCount == 1 &&
                    notification.EarliestExpiryUtc == StartUtc.AddHours(25),
                "a later batch notifies when it enters its own 24-hour window");
        });
    }

    private static void ExpiryReminderTracksBatchCountIncreases()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            RateLimitResetCreditSnapshot first =
                Credit(StartUtc.AddDays(-2), StartUtc.AddHours(10), "First");
            RateLimitResetCreditSnapshot second =
                Credit(StartUtc.AddDays(-1), StartUtc.AddHours(10), "Second");
            RateLimitResetCreditSnapshot third =
                Credit(StartUtc.AddHours(-1), StartUtc.AddHours(10), "Third");
            tracker.ObserveExpiry(Snapshot(2, StartUtc, first, second), StartUtc);
            ResetCreditExpiryNotification notification =
                new ResetCreditNotificationTracker(path).ObserveExpiry(
                    Snapshot(3, StartUtc.AddMinutes(1), first, second, third),
                    StartUtc.AddMinutes(1));
            Check(notification != null && notification.ExpiringCreditCount == 1,
                "a persisted expiry batch reminds only its newly added card");
            tracker = new ResetCreditNotificationTracker(path);
            Check(tracker.ObserveExpiry(
                    Snapshot(1, StartUtc.AddMinutes(2), third),
                    StartUtc.AddMinutes(2)) == null,
                "using cards does not repeat the batch reminder");
            Check(tracker.ObserveExpiry(
                    Snapshot(3, StartUtc.AddMinutes(3), first, second, third),
                    StartUtc.AddMinutes(3)) == null,
                "restored batch counts do not exceed the remembered high-water mark");
        });
    }

    private static void ExpiryReminderIgnoresChangingDetails()
    {
        WithStatePath(delegate(string path)
        {
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            DateTime expiryUtc = StartUtc.AddHours(20);
            tracker.ObserveExpiry(Snapshot(1, StartUtc,
                new RateLimitResetCreditSnapshot
                {
                    ExpiresAtUtc = expiryUtc,
                    Title = "Original title"
                }), StartUtc);
            Check(tracker.ObserveExpiry(Snapshot(1, StartUtc.AddMinutes(1)),
                    StartUtc.AddMinutes(1)) == null,
                "temporarily missing expiry details do not create a reminder");
            RateLimitResetCreditSnapshot restored =
                Credit(StartUtc.AddDays(-2), expiryUtc, "Changed title");
            restored.Description = "Later supplied details";
            Check(tracker.ObserveExpiry(
                    Snapshot(1, StartUtc.AddMinutes(2), restored),
                    StartUtc.AddMinutes(2)) == null,
                "restored details and title or grant-time changes do not notify again");
            Check(new ResetCreditNotificationTracker(path).ObserveExpiry(
                    Snapshot(1, StartUtc.AddMinutes(3), restored),
                    StartUtc.AddMinutes(3)) == null,
                "restored detail deduplication survives restarting");
        });
    }

    private static void ExpiryReminderSupportsLegacyState()
    {
        WithStatePath(delegate(string path)
        {
            Dictionary<string, object> legacy = new Dictionary<string, object>();
            legacy["Initialized"] = true;
            legacy["AvailableCount"] = 2;
            legacy["LastObservedAtUtc"] = StartUtc.AddMinutes(-1);
            legacy["LatestGrantedAtUtc"] = StartUtc.AddDays(-2);
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(legacy));
            ResetCreditNotificationTracker tracker =
                new ResetCreditNotificationTracker(path);
            RateLimitResetCreditSnapshot credit =
                Credit(StartUtc.AddDays(-2), StartUtc.AddHours(6), "Existing");
            ResetCreditExpiryNotification notification = tracker.ObserveExpiry(
                Snapshot(2, StartUtc, credit, credit), StartUtc);
            Check(notification != null && notification.ExpiringCreditCount == 2,
                "a pre-2.0.2 state file supports the first expiry reminder");
            ResetCreditGrantNotification grant = tracker.Observe(
                Snapshot(3, StartUtc.AddMinutes(1), credit, credit, credit));
            Check(grant != null && grant.NewCreditCount == 1,
                "adding expiry state preserves the previous grant baseline");
            Check(new ResetCreditNotificationTracker(path).ObserveExpiry(
                    Snapshot(2, StartUtc.AddMinutes(2), credit, credit),
                    StartUtc.AddMinutes(2)) == null,
                "upgraded legacy state persists expiry deduplication");
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
