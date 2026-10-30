// Store builds only: kept out of the YouTube Playables (web) build.
#if !YOUTUBE_PLAYABLES

using System;
using FreeFlow.Core.Services;
using FreeFlow.Util;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections;
using Unity.Notifications.Android;
#endif

namespace FreeFlow.Platform.Mobile
{
    /// <summary>
    /// Android's own notification system, through Unity's Mobile Notifications package.
    ///
    /// Everything here is gated to a real device. The Editor compiles the empty halves and reports
    /// <see cref="IsSupported"/> false, so pressing Play never queues anything and never asks for a
    /// permission the Editor cannot grant. The decisions about WHEN to nag live in
    /// ReminderScheduler, which is platform-neutral and can be exercised in the Editor; only the
    /// posting is device-only.
    ///
    /// Scheduling is deliberately INEXACT. Unity's notification settings can add
    /// SCHEDULE_EXACT_ALARM / USE_EXACT_ALARM to the manifest -- do not let them. Google Play
    /// prohibits declaring those unless exact timing is a core feature of the app, and "remind me
    /// to play a puzzle" is not one. Doze may therefore delay a reminder; for a nudge, nobody
    /// notices.
    /// </summary>
    public sealed class MobileNotificationService : INotificationService
    {
        /// <summary>One channel for every reminder this game sends. Channel settings are immutable
        /// once created, so splitting daily and win-back apart later means creating a SECOND
        /// channel rather than editing this one.</summary>
        public const string ChannelId = "freeflow_reminders";

        private const string ChannelName = "Reminders";
        // Shown in Android's own notification settings, where the player can switch this
        // category off. Says nothing about unfinished levels: the game saves none, so that would
        // be the same false promise the reminder text itself is careful to avoid.
        private const string ChannelDescription =
            "Reminders about the daily puzzle and your next game.";

        public void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Importance.Default: a sound, but no heads-up banner over whatever they are doing.
            // High would be a pop-over, which is what a reminder to play a puzzle game has not
            // earned. Low would be silent, which defeats the point of a reminder.
            AndroidNotificationChannel channel = new AndroidNotificationChannel(
                ChannelId, ChannelName, ChannelDescription, Importance.Default);

            AndroidNotificationCenter.RegisterNotificationChannel(channel);
#endif
        }

        public bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public bool HasPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return AndroidNotificationCenter.UserPermissionToPost == PermissionStatus.Allowed;
#else
                return false;
#endif
            }
        }

        public void RequestPermission(Action<bool> onGranted)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PermissionStatus status = AndroidNotificationCenter.UserPermissionToPost;

            // DeniedDontAskAgain and NotificationsBlockedForApp both mean the OS will no longer
            // show the prompt: asking again does nothing at all, silently. Only the system
            // settings screen can change it from there.
            bool canAsk = status == PermissionStatus.NotRequested || status == PermissionStatus.Denied;

            if (!canAsk)
            {
                onGranted?.Invoke(HasPermission);
                return;
            }

            PlatformRunner.Instance.StartCoroutine(RequestPermissionRoutine(onGranted));
#else
            onGranted?.Invoke(false);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static IEnumerator RequestPermissionRoutine(Action<bool> onGranted)
        {
            PermissionRequest request = new PermissionRequest();

            while (request.Status == PermissionStatus.RequestPending)
            {
                yield return null;
            }

            onGranted?.Invoke(request.Status == PermissionStatus.Allowed);
        }
#endif

        public void Schedule(int id, string title, string body, DateTime fireAtDeviceLocal)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Scheduling without permission is allowed and silently posts nothing, which is a
            // waste of an alarm the OS has to keep -- so don't.
            if (!HasPermission) { return; }

            AndroidNotification notification = new AndroidNotification
            {
                Title = title,
                Text = body,
                FireTime = fireAtDeviceLocal,

                // Tapping it clears it. Without this a reminder the player has already acted on
                // sits in the tray until they swipe it away themselves.
                ShouldAutoCancel = true,
            };

            // SmallIcon is left unset so the package falls back to the app icon. A proper
            // monochrome status-bar icon is an art task: it must be configured under
            // Project Settings > Mobile Notifications before a name here resolves to anything,
            // and an unresolved name shows as a blank square.

            AndroidNotificationCenter.SendNotificationWithExplicitID(notification, ChannelId, id);
#endif
        }

        public void CancelAll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Both halves: the ones still pending AND any already sitting in the tray. The player
            // is looking at the game right now, so every reminder about coming back to it is
            // wrong, whether it has fired yet or not.
            AndroidNotificationCenter.CancelAllNotifications();
#endif
        }
    }
}

#endif
