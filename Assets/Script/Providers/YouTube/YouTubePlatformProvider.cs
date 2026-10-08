using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Platform.YouTube
{
    [CreateAssetMenu(fileName = "YouTubePlatformProvider", menuName = "FreeFlow/Providers/YouTube Platform")]
    public class YouTubePlatformProvider : PlatformServiceProvider
    {
        public override IPlatformService Create()
        {
            return new YouTubePlatformService();
        }
    }
}
