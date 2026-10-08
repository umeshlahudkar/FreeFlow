using FreeFlow.Core.Services;
using UnityEngine;

namespace FreeFlow.Platform.YouTube
{
    [CreateAssetMenu(fileName = "YouTubeCloudSaveProvider", menuName = "FreeFlow/Providers/YouTube Cloud Save")]
    public class YouTubeCloudSaveProvider : SaveStorageProvider
    {
        public override ISaveStorage Create()
        {
            return new YouTubeCloudSaveStorage();
        }
    }
}
