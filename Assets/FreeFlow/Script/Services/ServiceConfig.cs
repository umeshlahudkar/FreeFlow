using UnityEngine;

namespace FreeFlow.Core.Services
{
    /// <summary>
    /// One build's choice of providers, plus the game rules that go with them. There is one of these
    /// per build define, under Resources/Services; <see cref="Current"/> is the one this build uses.
    ///
    /// Each manager builds its own service from here once (ADManager its ad service, ProfileManager
    /// its save storage, ...) and keeps the reference. A slot left empty gets that service's "nothing
    /// here" default -- no ads, no analytics, no share sheet -- except saving, which defaults to files
    /// on the device.
    ///
    /// A new platform adds its own define, its own config asset under Resources/Services, and one
    /// line in <see cref="ConfigResourcePath"/> -- nothing else in the game changes.
    /// </summary>
    [CreateAssetMenu(fileName = "ServiceConfig", menuName = "FreeFlow/Service Config")]
    public class ServiceConfig : ScriptableObject
    {
        private const string ConfigResourcePath =
#if YOUTUBE_PLAYABLES
            "Services/ServiceConfig_YouTube";
#else
            "Services/ServiceConfig_Mobile";
#endif

        [Header("Providers (empty = none)")]
        [SerializeField] private AdServiceProvider ads;
        [SerializeField] private AnalyticsServiceProvider analytics;
        [Tooltip("Empty = files on the device.")]
        [SerializeField] private SaveStorageProvider saveStorage;
        [SerializeField] private PlatformServiceProvider platform;
        [SerializeField] private ShareServiceProvider share;
        [SerializeField] private HapticsServiceProvider haptics;
        [Tooltip("Empty = no reminders are ever posted.")]
        [SerializeField] private NotificationServiceProvider notifications;

        // Both conditions must hold before an interstitial is due -- see ADManager. Here rather than
        // on an ad provider because they are the game's rules, not a network's: a different network
        // shows ads at the same moments.
        [Header("Interstitial ad rules")]
        [Tooltip("How many levels the player must complete before another interstitial is due.")]
        [SerializeField] private int levelsBetweenInterstitials = 3;
        [Tooltip("Minimum real-world seconds that must pass between two interstitials.")]
        [SerializeField] private float minSecondsBetweenInterstitials = 120f;

        // A build with no rewarded ad (YouTube v1) has no other way to earn hints, so it pays some
        // for the daily challenge instead. 0 = none, as on Android and iOS.
        // The game's reminder rules, not a platform's -- a different notification backend would
        // nag at the same moments. Hours are on the PLAYER'S clock.
        [Header("Reminders")]
        [Tooltip("Hour of the day a daily-challenge reminder is aimed at, 0-23. The puzzle "
            + "unlocks at 00:00 local, but nobody wants to hear about it then.")]
        [Range(0, 23)]
        [SerializeField] private int reminderHour = 12;

        [Tooltip("Minutes past Reminder Hour.")]
        [Range(0, 59)]
        [SerializeField] private int reminderMinute = 30;

        [Tooltip("How long after the player leaves a win-back reminder is aimed at. 0 = none.")]
        [SerializeField] private float winBackHours = 6f;

        [Header("Hints")]
        [Tooltip("Hints granted the first time each day's daily challenge is solved. 0 = none.")]
        [SerializeField] private int dailyFirstSolveHints = 0;

        private static ServiceConfig current;

        /// <summary>This build's config. If the asset is missing, an empty one: every service then
        /// gets its default.</summary>
        public static ServiceConfig Current
        {
            get
            {
                if (current == null)
                {
                    current = Resources.Load<ServiceConfig>(ConfigResourcePath);
                    if (current == null)
                    {
                        Debug.LogWarning("ServiceConfig: none at Resources/" + ConfigResourcePath
                            + " -- every service falls back to its default.");
                        current = CreateInstance<ServiceConfig>();
                    }
                }
                return current;
            }
        }

        public int LevelsBetweenInterstitials { get { return levelsBetweenInterstitials; } }
        public float MinSecondsBetweenInterstitials { get { return minSecondsBetweenInterstitials; } }
        public int DailyFirstSolveHints { get { return dailyFirstSolveHints; } }

        /// <summary>The time of day a daily reminder aims for, on the player's own clock.</summary>
        public System.TimeSpan ReminderTimeOfDay
        {
            get { return new System.TimeSpan(reminderHour, reminderMinute, 0); }
        }

        public float WinBackHours { get { return winBackHours; } }

        public IAdService CreateAdService() { return ads != null ? ads.Create() : new NullAdService(); }
        public IAnalyticsService CreateAnalyticsService() { return analytics != null ? analytics.Create() : new NullAnalyticsService(); }
        public ISaveStorage CreateSaveStorage() { return saveStorage != null ? saveStorage.Create() : new FileSaveStorage(); }
        public IPlatformService CreatePlatformService() { return platform != null ? platform.Create() : new NullPlatformService(); }
        public IShareService CreateShareService() { return share != null ? share.Create() : new NullShareService(); }
        public IHapticsService CreateHapticsService() { return haptics != null ? haptics.Create() : new NullHapticsService(); }
        public INotificationService CreateNotificationService() { return notifications != null ? notifications.Create() : new NullNotificationService(); }

        // Forgets the loaded config before each play session, so a project with domain reload turned
        // off never carries one session's config into the next.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            current = null;
        }
    }
}
