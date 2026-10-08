using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Platform.Mobile
{
    [CreateAssetMenu(fileName = "MobileShareProvider", menuName = "FreeFlow/Providers/Mobile Share")]
    public class MobileShareProvider : ShareServiceProvider
    {
        public override IShareService Create()
        {
            return new NativeShareService();
        }
    }
}
