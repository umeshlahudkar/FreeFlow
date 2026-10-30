// Store builds only: kept out of the YouTube Playables (web) build.
#if !YOUTUBE_PLAYABLES

using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Platform.Mobile
{
    [CreateAssetMenu(fileName = "MobileNotificationProvider", menuName = "FreeFlow/Providers/Mobile Notifications")]
    public class MobileNotificationProvider : NotificationServiceProvider
    {
        public override INotificationService Create()
        {
            return new MobileNotificationService();
        }
    }
}

#endif
