# FreeFlow — YouTube Playables: Required Changes

As of 2026-10-07 · Companion: [YOUTUBE_PLAYABLES_PLAN.md](YOUTUBE_PLAYABLES_PLAN.md) · Live doc: https://claude.ai/code/artifact/7dfe7cf2-55ab-46d5-b3df-c32fc5e67f24

## Summary

FreeFlow can ship on YouTube Playables as a Unity WebGL build, but every mobile-only service (AdMob, Firebase, file saves, native share, haptics) must be swapped for the YouTube Playables SDK in that build only. The puzzle core, levels and UI carry over; the work is in a platform layer, a WebGL build profile and certification fixes.

[YouTube Playables](https://developers.google.com/youtube/gaming/playables) are web games played inside the YouTube app and website, on phones and desktops. Access is early access: a studio applies through the Playables interest form, then uploads a ZIP build through the Developer Portal for certification.

The five changes that drive the effort:

1. **New build target:** WebGL, uncompressed, under 30 MiB to first interaction and 8,000 files total.
2. **SDK lifecycle:** call `firstFrameReady` and `gameReady`, and obey YouTube pause, resume and mute.
3. **Cloud save only:** progress goes through `saveData`/`loadData` (max 3 MiB); PlayerPrefs or file saves are not allowed.
4. **No off-platform monetization:** AdMob is removed. Only YouTube's own ad functions are permitted, and Google's requirements page also says monetization is not supported yet (see Platform requirements).
5. **Responsive, any-orientation UI:** playable at any aspect ratio (Google's examples run from 9:32 to 32:9) with touch and mouse; no share buttons or external links.

None of this touches the Android or iOS builds if it sits behind a `YOUTUBE_PLAYABLES` define and a separate build profile (last section).

## Platform requirements

Certification checks eight areas; the MUST rules below are the ones a FreeFlow port can fail. SHOULD items are listed where they cost little.

| Area | Rule | Level | Source |
| --- | --- | --- | --- |
| Integration | Load the Playables SDK before any game code | MUST | [Integration](https://developers.google.com/youtube/gaming/playables/certification/requirements_integration) |
| Integration | `firstFrameReady` when the loading screen shows; `gameReady` only when the menu is interactive | MUST | Integration |
| Integration | Save via `saveData` after material progress; no other save mechanism | MUST | Integration |
| Integration | Await `loadData` before the first `saveData`; old save formats must still load | MUST | Integration |
| Integration | Final save on pause is best-effort and capped at 64 KiB | SHOULD | Integration |
| Integration | Respect YouTube mute via `isAudioEnabled` / `onAudioEnabledChange`; no in-game master mute button | MUST / SHOULD | Integration |
| Integration | Stop loop, audio, input and rendering on `onPause`; do not use the Page Visibility API | MUST | Integration |
| Integration | Best score sent with `sendScore` matches the save | MUST if used | Integration |
| Stability | Bytes until `gameReady` under 30 MiB (target 15 MiB) | MUST | [Stability](https://developers.google.com/youtube/gaming/playables/certification/requirements_stability) |
| Stability | Total bundle under 250 MiB; each file under 30 MiB (target 512 KiB) | MUST | Stability |
| Stability | At most 8,000 files; relative paths; filenames use only letters, digits, `_ - .` | MUST | Stability |
| Stability | Save data under 3 MiB (target 500 KiB) | MUST | Stability |
| Stability | JS heap under 512 MB (iPhone crash limit); no reproducible crashes | MUST | Stability |
| Stability | Interactive in under 5 seconds | SHOULD | Stability |
| Stability | Runs in all YouTube browsers plus YouTube Android and iOS apps | MUST | Stability |
| Design | Playable at every aspect ratio (examples listed: 9:32, 9:16, 1:1, 16:9, 32:9), otherwise centred with pillarbox or letterbox; no orientation lock; state kept on resize | MUST | [Design](https://developers.google.com/youtube/gaming/playables/certification/requirements_design) |
| Design | Every interaction works with touch and with mouse; Esc closes dialogs | MUST / SHOULD | Design |
| Design | Text and graphics sharp at all resolutions and pixel densities | MUST | Design |
| Design | Tell the player when there is no more content | MUST | Design |
| Design | No share prompts, external links, extra user agreements, quit buttons, or icons identical to YouTube's close, mute or menu buttons placed near them | MUST NOT | Design |
| Design | Haptics optional, and must be toggleable if present | MUST if used | Design |
| Monetization | No off-platform ads or IAP. The page opens by saying monetization is not supported and in-game ads are prohibited, yet its rules say games MAY use YouTube-provided ad functions | MUST NOT / unclear | [Monetization](https://developers.google.com/youtube/gaming/playables/certification/requirements_monetization) |
| Privacy | No external network calls; no login or personal data; no code obfuscation | MUST NOT | [Privacy](https://developers.google.com/youtube/gaming/playables/certification/requirements_privacydata) |
| Localization | Support English; get language from `getLanguage`, never `navigator.language` | MUST | [i18n](https://developers.google.com/youtube/gaming/playables/certification/requirements_i18n_l10n) |
| Trust and safety | General audience 13+, not made for kids; all assets, fonts and music licensed | MUST | [Trust and safety](https://developers.google.com/youtube/gaming/playables/certification/requirements_trustsafety) |
| Accessibility | Aim for WCAG AA; add accurate accessibility tags | SHOULD | [Accessibility](https://developers.google.com/youtube/gaming/playables/certification/requirements_accessibility) |

The network rule is enforced by a Content Security Policy that only allows the game's own files, YouTube's game API and Google Fonts ([test suite guide](https://developers.google.com/youtube/gaming/playables/reference/test_suite_guide)).

On ads, the [Developer Portal](https://developers.google.com/youtube/gaming/playables/developer_portal) says its interstitial and rewarded settings "won't change the end-user ads experience for now". Whether YouTube ads actually serve today is therefore unconfirmed.

## Playables SDK

The SDK is a script tag in `index.html` that exposes a global `ytgame` object; Unity reaches it through a `.jslib` bridge. Google ships that bridge as the [Unity wrapper](https://developers.google.com/youtube/gaming/playables/samples/unity_wrapper) (`GoogleYTGameWrapper.unitypackage`: jslib, `YTGameWrapper.cs`, WebGL template), from the [web-game-samples](https://github.com/google/web-game-samples) repo. Inspected on 2026-10-07: the package holds `UnityYTGameSDKLib.jslib` and `YTGameWrapper.cs` (Apache 2.0). It wraps every call FreeFlow needs (`firstFrameReady`, `gameReady`, `saveData`/`loadData`, `isAudioEnabled`, `onAudioEnabledChange`, `onPause`, `onResume`, `IN_PLAYABLES_ENV`, `logError`/`logWarning`, `sendScore`, both ad calls) but not `getLanguage` or `openYTContent`. Callbacks reach C# through `SendMessage` to a GameObject that must be named `YTGameWrapper`, via a global `unityGameInstance` that the WebGL template must define. Three gaps to handle: the int returned by the save and interstitial calls is not the real result (the JS returns before the Promise settles); `loadData` has no failure path, so boot needs a timeout; and in the Editor the rewarded-ad call always reports a reward.

| API | What FreeFlow uses it for | Required |
| --- | --- | --- |
| `game.firstFrameReady()` | Boot/splash screen is drawn | Yes |
| `game.gameReady()` | Menu page is shown and clickable | Yes |
| `game.loadData()` → `Promise<string>` | Load the save JSON at boot, before anything saves | Yes |
| `game.saveData(string)` | Save after each solved level, daily solve, hint use, settings change; max 3 MiB | Yes |
| `system.isAudioEnabled()` / `onAudioEnabledChange(cb)` | Gate the AudioListener; YouTube mute always wins over in-game music/SFX toggles | Yes |
| `system.onPause(cb)` / `onResume(cb)` | Freeze timeScale, audio and input; flush the save on pause | Yes |
| `system.getLanguage()` | Pick UI language (BCP-47, e.g. `en-US`) | If localized |
| `ads.requestInterstitialAd()` | Replaces the AdMob interstitial between levels; no guarantee it shows | Optional |
| `ads.requestRewardedAd(rewardId)` → `Promise<bool>` | Replaces the AdMob rewarded ad (e.g. free hints) | Optional |
| `engagement.sendScore(score)` | Could send total levels solved; must match the save's best | Optional |
| `engagement.openYTContent(content)` | Open a YouTube video or Playable; the only allowed outbound link | Optional |
| `health.logError()` / `logWarning()` | Replaces Crashlytics for error counts (best-effort, rate-limited) | Recommended |
| `IN_PLAYABLES_ENV` | Lets the same WebGL build also run outside YouTube for local testing | Utility |

Two details shape the C# side. The SDK returns Promises, so C# gets results through jslib callbacks (`SendMessage` or function pointers), never synchronously. And a save written before `loadData` resolves is rejected, so boot must wait for the load before the first page that can save. Source: [SDK reference](https://developers.google.com/youtube/gaming/playables/reference/sdk).

## Unity WebGL build constraints

The WebGL player must be tuned for size and for YouTube's sandbox; these settings live only on the WebGL target, so Android and iOS settings are unaffected.

| Setting or behaviour | Playables build | Why |
| --- | --- | --- |
| Compression Format | Disabled (already set) | The Unity wrapper guide says Unity gzip/Brotli builds are not supported |
| Data Caching | Off (recommendation; currently On) | Not a stated rule. Unity's asset cache uses IndexedDB; whether that works in YouTube's sandbox is untested |
| WebGL template | Playables template (SDK script first in `<head>`) | SDK must load before any game code |
| Canvas sizing | Fill the frame, follow resize events | Any aspect ratio, no scrollbars |
| Managed stripping | Raise from Low (current) after testing | Smaller `.wasm`; must be tested, since higher stripping can remove reflected code |
| Texture compression | Choose after measuring the first build | `.data` holds every scene asset and counts toward the 30 MiB initial budget; format support differs between desktop and mobile browsers |
| Fonts | Only the TMP SDF assets the UI uses | Each of the five Teko SDF assets is 2.1 MB on disk |
| Audio | Compressed, music streamed or lazy-loaded | Size; browsers also block audio until the first tap |
| Max memory | Below 512 MB (currently 2048 MB) | The rule caps peak JavaScript heap at 512 MB; how Unity's wasm memory counts toward it is not stated, so treat 512 MB as a ceiling |
| Large content | Optional Addressables/AssetBundles from the game's own bundle | Keeps the initial download small; must be relative paths, no external hosts |
| Exception support | Explicitly thrown only (already set) | Smaller, faster wasm |
| Splash screen | Check whether one is shown | Time before interaction counts toward the under-5-second SHOULD |

Code that does not run in a browser must be excluded from the WebGL build:

- Background threads (`Thread`, `Task.Run` work), `System.Net`, sockets and raw `HttpClient`.
- `System.IO.File` saves and `Application.persistentDataPath` as a source of truth.
- `Application.Quit`, `Application.OpenURL`, `Handheld.Vibrate` and native plugins (`.aar`, `.framework`).
- Anything that reads `navigator.language` or the Page Visibility API through JS.

## Gap analysis: FreeFlow today vs Playables

Firebase stops the WebGL build compiling today; AdMob probably compiles but has no WebGL runtime and is not allowed; file saves break certification; the rest are UI and lifecycle fixes. The puzzle core, the 1,066 level assets, the page system and audio mixing port as they are. Surveyed on Unity 6000.3.8f1, Built-in pipeline, legacy Input Manager.

| System | Today | Playables change | Severity |
| --- | --- | --- | --- |
| Firebase Analytics + Crashlytics | `FirebaseManager.cs`, `AnalyticsManager.cs`; DLLs excluded from WebGL; started from StartScene init order | Exclude from WebGL build; route `LogLevelStart/Complete/HintUsed` to a no-op, errors to `health.logError` | Compile blocker |
| AdMob (Google Mobile Ads 11.5) | `Ads/ADManager.cs`: rewarded hint ad (`GameplayPage.cs:341`), interstitial every 3 levels and 180 s (`UIController.cs:737`) | YouTube implementation of the same calls: `requestRewardedAd` for hints, `requestInterstitialAd` with the same gating, built behind a switch and turned on only if Google confirms ads serve | Policy blocker (compile on WebGL unverified) |
| Save system | `ProfileManager.cs`: `SaveData.json` + `Settings.json` via `System.IO` in `persistentDataPath`, synchronous | Storage backend swapped to `loadData`/`saveData`; both files merged into one JSON string; load awaited in `Initialize(Action)` before MainScene | Certification blocker |
| Boot and lifecycle | `GameBootstrap` loads StartScene then MainScene; no ready signals, no pause handling | `firstFrameReady` on StartScene, `gameReady` when MainMenu page opens; `onPause`/`onResume` freeze `timeScale`, audio and input and flush the save | Certification blocker |
| Audio | `AudioManager.cs`: music auto-starts in `Start`; music and SFX sliders; flush on `OnApplicationQuit` | YouTube mute overrides everything via `isAudioEnabled`/`onAudioEnabledChange`; keep sliders (allowed); start music only after first input | Certification blocker |
| Share | `Share/ShareService.cs` (NativeShare, PNG card), buttons in `SettingPage.cs:217`, `LevelCompletePage.cs:327` | Hide both buttons in the Playables build; share prompts are not allowed | Certification blocker |
| Privacy policy row | `Row_Privacy` in Settings, no handler | Hide; external links are not allowed | Certification blocker |
| Haptics | `Haptics.cs`, already a no-op on WebGL | Hide the Vibration toggle so the setting doesn't lie | Minor |
| Developer page | Behind `#if !FINAL_BUILD`; `FINAL_BUILD` is not defined on any target, so the Developer row (including setting hints) shows in every current build, mobile too | Define `FINAL_BUILD` on the WebGL target for submission | Minor |
| Layout and orientation | Portrait 1080x1920 design; `CanvasScalerMatchSetter` covers 9:19.5 to 3:4; orientation fixed to Portrait | Pillarbox the portrait layout on wide frames (up to 32:9) and confirm 9:32; keep state on resize | Certification blocker |
| Input | Mouse API with touch-to-mouse in `GamePlayController.cs:190-206`; no keyboard | Works for mouse and touch; add Esc to close overlays via `PageManager`; ignore a second finger | Minor |
| Daily challenge | Fixed level per UTC date from device clock (`DailyChallengeSelector.DayIndex`) | No change needed; clock cheating is acceptable with no leaderboard | None |
| Hints economy | 3 at start; refilled only by the rewarded ad (also by the Developer page and by Reset all progress) | Decided: +1 hint per daily challenge solved, because the YouTube build otherwise needs a non-ad refill rule | Decided |
| Frame rate | `AppBootstrap.cs:32-33` sets `targetFrameRate = 60` | Use `-1` on WebGL so the browser's frame loop drives it | Minor |
| Localization | English only, hardcoded | Passes (English required); `getLanguage` only if languages are added later | None |
| End of content | No message says there is no more content: the last pack level shows "PACK COMPLETE", a finished pack list shows only checkmarks, and the main menu chip would read "Level 101" | Add an explicit end-of-content message | Certification blocker |

Download size is the other gap. Assets that ship today and are worth trimming for the 15 MiB target:

| Asset | Size on disk | Action |
| --- | --- | --- |
| `bg-music.wav` | 11.5 MB, imported as Vorbis at 100% quality, stereo, streaming | Lower quality or mono, or lazy-load after `gameReady` |
| Teko SDF font assets | 5 × 2.1 MB | Keep only the weights the UI uses |
| `bg_light_1080x1920.png` | 1.8 MB, used in MainScene | Compress or reduce resolution |
| `Resources/Levels` | 5.6 MB YAML for 1,066 levels | Everything in Resources ships in the initial download; measure in the built `.data` |
| Unused engine modules (terrain, cloth, vehicles, VR/XR, video, AI and others) | n/a | Disable for the WebGL build, or rely on engine stripping |

`__Sprites/screens` (11.1 MB) and `play_button_glow.png` (1.1 MB) are not referenced by any scene, prefab or asset, so Unity does not include them in a build. On-disk sizes overstate build size; the real number comes from the first WebGL build's `.data` and `.wasm`. Existing WebGL settings: compression is already Disabled (what the Unity wrapper guide asks for), data caching is On, managed stripping is Low, the template is Minimal and maximum memory is 2048 MB.

Not present, so nothing to do: IAP, push notifications, rate-us, login, consent/ATT, `Application.Quit`, `Application.OpenURL`, threads and networking.

## Keeping one project for all three stores

One codebase: the Playables work is done on a separate branch and merged back, and the Playables build is selected by build target plus a `YOUTUBE_PLAYABLES` scripting define, the same pattern the project already uses for `FINAL_BUILD`. These rules keep the mobile builds' behaviour unchanged:

1. **Add, don't edit, platform code.** New YouTube implementations sit next to the existing AdMob, Firebase and file-save code behind interfaces; mobile code paths keep their current classes.
2. **Define symbols per target.** `YOUTUBE_PLAYABLES` is set only on the WebGL target. Mobile-only SDK code is wrapped in `#if !YOUTUBE_PLAYABLES` (or `UNITY_ANDROID || UNITY_IOS`) so it is not compiled into WebGL.
3. **Plugin import settings.** Firebase and the platform-specific Google Mobile Ads DLLs are already excluded from WebGL. Four core Google Mobile Ads DLLs (`GoogleMobileAds`, `.Common`, `.Core`, `.Ump`) have no platform settings and likely reach WebGL, so exclude them explicitly rather than deleting packages.
4. **Separate scenes are a last resort.** Prefer one boot scene with a platform bootstrapper; a separate Playables scene only if a mobile-only component cannot be stripped.
5. **Save format stays shared.** The same `SaveData` JSON is written to disk on mobile and to `saveData` on YouTube, so one versioning scheme covers both.
6. **Build profiles, not manual switching.** A Unity Build Profile (or a menu build script) per store sets target, defines, template and compression, so nobody flips settings by hand before a mobile release.
7. **Mobile regression check.** Each Playables change is followed by an Android and iOS build plus a smoke test before merging.
