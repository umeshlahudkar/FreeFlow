using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Platform.Mobile
{
    [CreateAssetMenu(fileName = "MobileHapticsProvider", menuName = "FreeFlow/Providers/Mobile Haptics")]
    public class MobileHapticsProvider : HapticsServiceProvider
    {
        public override IHapticsService Create()
        {
            return new MobileHapticsService();
        }
    }
}
