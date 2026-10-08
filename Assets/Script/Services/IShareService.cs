namespace FreeFlow.Core.Services
{
    /// <summary>The platform's share sheet. What is shared, and its wording, is decided by
    /// ShareService; a provider only hands it to the system.</summary>
    public interface IShareService
    {
        /// <summary>Whether this platform can share at all. UI that offers sharing should hide
        /// itself when this is false.</summary>
        bool IsAvailable { get; }

        /// <summary>Opens the share sheet. <paramref name="filePath"/> may be null for text-only
        /// shares.</summary>
        void Share(string subject, string text, string filePath);
    }
}
