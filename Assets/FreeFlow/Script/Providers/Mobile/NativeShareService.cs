using FreeFlow.Core.Services;

namespace FreeFlow.Platform.Mobile
{
    /// <summary>The Android/iOS share sheet, through the NativeShare plugin. The Editor has no share
    /// sheet; ShareService shows its own stand-in there and never reaches this.</summary>
    public sealed class NativeShareService : IShareService
    {
        public bool IsAvailable { get { return true; } }

        public void Share(string subject, string text, string filePath)
        {
            NativeShare share = new NativeShare().SetSubject(subject).SetText(text);
            if (!string.IsNullOrEmpty(filePath)) { share.AddFile(filePath); }
            share.Share();
        }
    }
}
