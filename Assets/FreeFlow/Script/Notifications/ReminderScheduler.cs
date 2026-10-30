using System;
using FreeFlow.Core.Services;
using FreeFlow.GamePlay;

/// <summary>
/// The game's two reminders: "your board is waiting" a few hours after the player leaves, and
/// "today's puzzle is ready" in the evening. Posting them is this scheduler's
/// <see cref="INotificationService"/>, built from the build's ServiceConfig.
///
/// The whole thing runs on one idea: nothing of ours is running when a reminder appears, so it
/// cannot check whether the player already came back. Instead opening the game cancels everything
/// pending, and leaving queues a fresh pair worked out from the state right then. Nothing is
/// carried between sessions and nothing can go stale, because the only thing that survives a
/// session is the alarm the OS is holding.
///
/// That is also why RepeatInterval is not used. A repeating alarm belongs to the OS and would
/// cheerfully announce the daily to someone who solved it at lunchtime.
/// </summary>
public static class ReminderScheduler
{
    private const int WinBackId = 9001;
    private const int DailyId = 9002;

    // Boards finished before the permission is asked for. Two is early enough to still reach the
    // players the win-back reminder exists for -- the ones who leave and do not come back -- and
    // late enough that they have finished something and know what the game is. It is also the last
    // completion before an interstitial can appear: ADManager gates those at every third, so
    // asking here cannot put Android's dialog on top of an ad.
    private const int AskPermissionAfterLevels = 2;

    // Nobody wants a puzzle game at 3am. A six-hour win-back started at 11pm lands at 5am without
    // this, which is how an app gets its notifications turned off for good.
    private const int QuietStartHour = 22;
    private const int QuietEndHour = 9;

    // Two reminders minutes apart read as a malfunction, so the daily one is dropped when the
    // win-back has already landed near it.
    private const double MinHoursApart = 2.0;

    private struct Message
    {
        public readonly string Title;
        public readonly string Body;

        public Message(string title, string body)
        {
            Title = title;
            Body = body;
        }
    }

    // The only player-facing strings this feature has -- the place to start if it is ever
    // localised. One is picked at random per reminder so a player who sees these every day is not
    // reading the same sentence each time.
    //
    // Every line in both lists has to be true whenever it happens to fire, because nothing checks
    // it at that moment. That rules out more than it sounds like: no time of day (the win-back
    // lands six hours after they left, which is any hour at all), and nothing about resuming or
    // finishing -- the game saves no in-progress board, so "pick up where you left off" would send
    // them to a main menu with nothing to pick up.
    private static readonly Message[] DailyMessages =
    {
        new Message("Today's puzzle is ready", "A new daily challenge is waiting."),
        new Message("A new board is up", "Today's daily challenge is waiting."),
        new Message("Today's daily is waiting", "One board, once a day."),
        new Message("Fresh puzzle, fresh day", "Today's daily challenge is ready."),
        new Message("Your daily is up", "A new board is waiting for you."),
    };

    private static readonly Message[] WinBackMessages =
    {
        new Message("Ready for another?", "Your next puzzle is one tap away."),
        new Message("Got a few minutes?", "A new board is waiting."),
        new Message("Fancy a puzzle?", "Pick a board and connect the dots."),
        new Message("Back for more?", "There is always another puzzle."),
        new Message("One more?", "Your next board is ready when you are."),
    };

    private static int levelsCompletedThisRun;

    private static INotificationService service;

    public static INotificationService Service
    {
        get
        {
            if (service == null) { service = ServiceConfig.Current.CreateNotificationService(); }
            return service;
        }
    }

    /// <summary>Registers the channel and clears whatever a previous session left queued -- the
    /// player is looking at the game, so all of it is already wrong.
    ///
    /// Synchronous, and independent of every other service: nothing here waits on an SDK, a host
    /// or a network. ServicesInitializer calls it on its own line for that reason.</summary>
    public static void Initialize()
    {
        Service.Initialize();
        Service.CancelAll();
    }

    /// <summary>They came back. Everything queued was about getting them to.</summary>
    public static void OnAppResumed()
    {
        Service.CancelAll();
    }

    /// <summary>
    /// A board was finished -- any board, Classic, Advanced or the daily. The permission ask rides
    /// on this rather than on a screen of its own, because the moment just after a win is the one
    /// moment the player is plainly enjoying the game.
    ///
    /// Counted per run rather than saved. A player who quits after one level is asked at their
    /// next second completion instead, and Android stops showing the dialog after about two
    /// refusals anyway -- past that this call is a silent no-op, so there is nothing for a save
    /// field to protect against.
    /// </summary>
    public static void OnLevelCompleted()
    {
        levelsCompletedThisRun++;

        // Exactly, not at-least: asking once per run is the whole point.
        if (levelsCompletedThisRun != AskPermissionAfterLevels) { return; }

        // Nothing to ask on the web build, and nothing to ask a player who already has it --
        // which is most of the existing ones, since Android pre-grants the permission to apps
        // already installed when the device upgrades to 13.
        if (!Service.IsSupported || Service.HasPermission) { return; }

        // No callback: OnAppPaused re-reads HasPermission every time, so there is nothing to do
        // with the answer either way.
        Service.RequestPermission(null);
    }

    /// <summary>They left. The only moment the game knows enough to decide what is worth
    /// saying.</summary>
    public static void OnAppPaused()
    {
        Service.CancelAll();

        if (!Service.IsSupported || !Service.HasPermission) { return; }

        // The DEVICE clock, not TimeManager.Now: the alarm fires against the device's own notion
        // of the time, so a moment worked out from the game clock would be wrong by however far
        // that clock has drifted since startup.
        DateTime now = DateTime.Now;
        ServiceConfig config = ServiceConfig.Current;

        DateTime winBack = DateTime.MinValue;

        if (config.WinBackHours > 0f)
        {
            Message message = PickFrom(WinBackMessages);
            winBack = PushOutOfQuietHours(now.AddHours(config.WinBackHours));
            Service.Schedule(WinBackId, message.Title, message.Body, winBack);
        }

        DateTime daily = PushOutOfQuietHours(NextDailyReminder(now, config.ReminderTimeOfDay));

        // Quiet hours can push the win-back right on top of the daily one -- an evening session
        // ending at 10pm pushes both to 9am tomorrow.
        bool clashes = winBack != DateTime.MinValue
            && Math.Abs((daily - winBack).TotalHours) < MinHoursApart;

        if (!clashes)
        {
            Message message = PickFrom(DailyMessages);
            Service.Schedule(DailyId, message.Title, message.Body, daily);
        }
    }

    /// <summary>
    /// When to say the daily is waiting. Today's slot only counts if it is still ahead AND today's
    /// puzzle is unsolved -- there is nothing to come back for otherwise. Tomorrow's is unsolved by
    /// definition, since it does not exist yet.
    /// </summary>
    private static DateTime NextDailyReminder(DateTime deviceNow, TimeSpan timeOfDay)
    {
        DateTime todayAt = deviceNow.Date.Add(timeOfDay);

        if (todayAt > deviceNow && !DailySolved(deviceNow)) { return todayAt; }

        return todayAt.AddDays(1);
    }

    private static bool DailySolved(DateTime deviceNow)
    {
        if (ProfileManager.Instance == null) { return false; }

        // Off the device clock, to stay in the same frame as everything else here.
        int todayIndex = DailyChallengeSelector.DayIndex(deviceNow);
        return ProfileManager.Instance.LoadProgress().IsDayCompleted(todayIndex);
    }

    /// <summary>One of the lines for this kind of reminder, at random. Plain random, so the same
    /// one can come up twice running -- with five to choose from that is a one-in-five chance,
    /// which is a different problem from always reading the same sentence.</summary>
    private static Message PickFrom(Message[] messages)
    {
        return messages[UnityEngine.Random.Range(0, messages.Length)];
    }

    /// <summary>Moves a reminder out of the small hours: anything landing between 22:00 and 09:00
    /// is pushed to the 09:00 that follows it.</summary>
    private static DateTime PushOutOfQuietHours(DateTime at)
    {
        if (at.Hour >= QuietEndHour && at.Hour < QuietStartHour) { return at; }

        DateTime morning = at.Date.AddHours(QuietEndHour);
        return morning > at ? morning : morning.AddDays(1);
    }
}
