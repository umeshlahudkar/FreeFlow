using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Platform.Mobile
{
    [CreateAssetMenu(fileName = "MobilePlatformProvider", menuName = "FreeFlow/Providers/Mobile Platform")]
    public class MobilePlatformProvider : PlatformServiceProvider
    {
        public override IPlatformService Create()
        {
            return new MobilePlatformService();
        }
    }
}
