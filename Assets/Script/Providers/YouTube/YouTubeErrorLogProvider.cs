using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Platform.YouTube
{
    [CreateAssetMenu(fileName = "YouTubeErrorLogProvider", menuName = "FreeFlow/Providers/YouTube Error Log")]
    public class YouTubeErrorLogProvider : AnalyticsServiceProvider
    {
        public override IAnalyticsService Create()
        {
            return new YouTubeErrorLogService();
        }
    }
}
