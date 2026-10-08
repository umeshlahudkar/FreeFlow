using UnityEngine;

namespace FreeFlow.Core.Services
{
    // A provider is an asset: it carries its own settings (ad unit IDs, say) and builds the service
    // at startup. ServiceConfig points at one provider asset per service. The concrete provider
    // classes live in their own assemblies (FreeFlow.Ads.AdMob, FreeFlow.Platform.Mobile, ...), so
    // FreeFlow.Core never references an SDK -- it only knows these base types.

    public abstract class AdServiceProvider : ScriptableObject
    {
        public abstract IAdService Create();
    }

    public abstract class AnalyticsServiceProvider : ScriptableObject
    {
        public abstract IAnalyticsService Create();
    }

    public abstract class SaveStorageProvider : ScriptableObject
    {
        public abstract ISaveStorage Create();
    }

    public abstract class PlatformServiceProvider : ScriptableObject
    {
        public abstract IPlatformService Create();
    }

    public abstract class ShareServiceProvider : ScriptableObject
    {
        public abstract IShareService Create();
    }

    public abstract class HapticsServiceProvider : ScriptableObject
    {
        public abstract IHapticsService Create();
    }
}
