using System.IO;
using UnityEngine;
using FreeFlow.Util;

namespace FreeFlow.Share
{
    /// <summary>
    /// The one place the game reaches the platform's share sheet, and the one place the wording it
    /// sends is written.
    ///
    /// There are two shares and they are deliberately not the same call with different strings.
    /// <see cref="ShareGame"/> promotes the game and is available any time; <see cref="ShareResult"/>
    /// posts something the player just did, and carries a rendered card image (see
    /// <see cref="ShareCard"/>) because a picture of a finished level is what actually earns a tap
    /// from someone scrolling past it.
    ///
    /// Both end with the store link. A share that shows off the game without telling the reader
    /// where to get it is a share that does nothing.
    ///
    /// Everything here is written as prose, in sentence case. The screens' own wording is all-caps
    /// because it labels a UI; a message somebody receives is read as a sentence, and the same caps
    /// there read as shouting -- which is why the result carries UIController.LevelPhrase ("Level
    /// 24") rather than the header it shows on screen ("LEVEL 24").
    /// </summary>
    public class ShareService : Singleton<ShareService>
    {
        [Header("Identity (both shares)")]
        // The game's name comes from Player Settings (Edit > Project Settings > Player > Product
        // Name) rather than being typed again here: it is already the name on the store listing,
        // the home screen icon and the window title, and a second copy is a second thing to
        // remember to rename. Leave productNameOverride empty unless the share needs to say
        // something the build itself is not called.
        //
        // Used by BOTH shares -- it is the name in the Settings share's sentence, the name in the
        // result's sentence, AND the brand line printed across the top of the result card.
        [SerializeField] private string productNameOverride = "";

        // The game share's sentence only ("I'm playing X -- a fun path puzzle game!"). A result
        // has its own thing to say and does not need the pitch.
        [SerializeField] private string tagline = "a fun path puzzle game";

        [Header("Store links (both shares)")]
        // PLACEHOLDERS -- replace with the real listings before shipping. Not derived from
        // Application.identifier on purpose: the bundle id is still Unity's default
        // (com.DefaultCompany.2DProject), so deriving a link would produce a confident-looking URL
        // that 404s.
        [SerializeField] private string androidStoreUrl = "https://play.google.com/store/apps/details?id=com.yourcompany.pathza";
        [SerializeField] private string iosStoreUrl = "https://apps.apple.com/app/id0000000000";

        [Header("Result share (Level Complete only)")]
        [SerializeField] private ShareCard shareCard;

        // The line that asks for a reply. Deliberately ONE field driving two places: it is the
        // last line of the shared text AND the line across the bottom of the card, and a reader
        // who sees both should not be challenged twice in two different wordings.
        [SerializeField] private string resultChallenge = "Can you beat me?";

        // OPTIONAL, and empty by default: a short link to PRINT ON THE CARD, if there ever is one
        // ("pathza.game", a bit.ly, whatever). It cannot be the store URL -- a full
        // play.google.com/store/apps/details?id=... wraps onto two lines at any readable size and
        // reads as a bug rather than an invitation -- and it is not needed for the share to work,
        // because the real tappable link always travels in the share TEXT beside the image.
        //
        // Left empty the card simply ends on the challenge line, which is the honest state: better
        // to print nothing than a short link that has not been registered and goes nowhere.
        [SerializeField] private string cardLinkText = "";

#if UNITY_EDITOR
        // What the on-screen notice says when Share is tapped in the Editor, where no share sheet
        // exists. Editor-only, so it cannot be mistaken for something a player will ever read.
        [Header("Editor")]
        [SerializeField] private string editorNoticeMessage = "Sharing works on device only";
#endif

        // The card is written here rather than kept in memory because the share sheet hands the
        // receiving app a file path, and the OS reads that file after this method has returned.
        // One fixed name, overwritten each time: the cache directory is not somewhere to
        // accumulate one PNG per level ever completed.
        private const string CardFileName = "pathza_result.png";

        /// <summary>What the game calls itself -- Player Settings' Product Name, unless something
        /// here deliberately overrides it.</summary>
        public string GameName
        {
            get
            {
                return string.IsNullOrEmpty(productNameOverride)
                    ? Application.productName
                    : productNameOverride;
            }
        }

        /// <summary>The listing for the platform the player is actually on. Falls back to the
        /// Android link everywhere else (the Editor included) so a test share still carries
        /// something openable rather than an empty line.</summary>
        public string StoreUrl
        {
            get
            {
#if UNITY_IOS
                return iosStoreUrl;
#else
                return androidStoreUrl;
#endif
            }
        }

        /// <summary>Promotes the game itself -- the Settings screen's Share row. No image: there is
        /// no moment being shared, and a generic logo card would add nothing the store link does
        /// not already carry.</summary>
        public void ShareGame()
        {
            // "I'm playing Pathza: Connect - a fun path puzzle game! Try it out."
            string text = "I'm playing " + GameName + " — " + tagline + "! Try it out."
                        + "\n\n" + StoreUrl;

            Send(text, GameName, null);
        }

        /// <summary>Posts a finished level: a rendered card plus the same result in text, since
        /// most apps show the text beside the image and some (SMS, plain email) show only the text.
        /// The share still goes out if the card cannot be rendered -- a missing image is worth
        /// losing, a share the player asked for is not.</summary>
        public void ShareResult(ShareResultData data)
        {
            string cardPath = WriteCard(data);
            Send(BuildResultText(data), GameName, cardPath);
        }

        /// <summary>The text beside the card. Opens with what the player did, in one sentence,
        /// because that is the line a messaging app previews and the only line some readers get;
        /// the stats back it up, and the link is what makes the post worth anything to the game.
        /// </summary>
        private string BuildResultText(ShareResultData data)
        {
            // "I completed Level 24 in Pathza: Connect!" / "...today's Daily Challenge in..."
            string headline = "\U0001F389 I completed " + data.Phrase + " in " + GameName + "!";

            string stats = "Moves: " + data.Moves
                         + "  ·  Time: " + ShareCard.FormatTime(data.Seconds)
                         + "  ·  Hints: " + data.Hints;

            return headline + "\n\n" + stats + "\n\n" + resultChallenge + "\n" + StoreUrl;
        }

        /// <summary>The card's closing lines: the challenge, and -- only if one is set -- where
        /// to get the game. Both are one text object on the card, so the link is shrunk with TMP's
        /// own rich text rather than by giving it a second object: the challenge is the line meant
        /// to be read across a feed, the link is a footnote under it.</summary>
        private string CardFooter
        {
            get
            {
                return string.IsNullOrEmpty(cardLinkText)
                    ? resultChallenge
                    : resultChallenge + "\n<size=55%>" + cardLinkText + "</size>";
            }
        }

        /// <summary>Renders the card and writes it as a PNG, returning the path or null if
        /// anything went wrong. Never throws at the caller: every failure here costs an image, not
        /// the share.</summary>
        private string WriteCard(ShareResultData data)
        {
            if (shareCard == null)
            {
                Debug.LogWarning("ShareService: no ShareCard assigned -- sharing the result as text only.");
                return null;
            }

            Texture2D texture = shareCard.Render(data, GameName, CardFooter);
            if (texture == null) { return null; }

            try
            {
                string path = Path.Combine(Application.temporaryCachePath, CardFileName);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return path;
            }
            catch (IOException e)
            {
                Debug.LogWarning("ShareService: could not write the result card (" + e.Message
                    + ") -- sharing as text only.");
                return null;
            }
            finally
            {
                // The caller owns the texture (see ShareCard.Render) and nothing else refers to it
                // once the PNG bytes are out.
                Destroy(texture);
            }
        }

        /// <summary>Hands the share to the platform.
        ///
        /// There is no share sheet in the Editor, so a tap there would otherwise do nothing at all
        /// and look like a broken button. Instead it says so on screen -- through the same
        /// WarningNotifier an empty hint balance uses -- and logs exactly what WOULD have gone out,
        /// card path included, so the text and the image can both be checked without a build.
        /// </summary>
        private void Send(string text, string subject, string filePath)
        {
#if UNITY_EDITOR
            Debug.Log("[ShareService] Share sheet (Editor stand-in)\nsubject: " + subject
                + "\ntext:\n" + text
                + "\nimage: " + (string.IsNullOrEmpty(filePath) ? "(none)" : filePath));

            if (UI.UIController.Instance != null)
            {
                UI.UIController.Instance.ShowWarning(editorNoticeMessage);
            }
#else
            NativeShare share = new NativeShare().SetSubject(subject).SetText(text);
            if (!string.IsNullOrEmpty(filePath)) { share.AddFile(filePath); }
            share.Share();
#endif
        }
    }
}
