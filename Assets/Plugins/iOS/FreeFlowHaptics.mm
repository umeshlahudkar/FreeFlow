// iOS side of Haptics.cs. UIFeedbackGenerator is the system's own haptic vocabulary -- the same
// taps the OS uses for switches, pickers and alerts -- so the game feels like the phone rather than
// like a motor being driven by hand.
//
// The type argument matches FreeFlow.Enums.HapticType exactly:
//   0 Selection   1 Light   2 Medium   3 Success   4 Warning
// Keep the two in step if that enum ever changes.

#import <UIKit/UIKit.h>

// Generators are kept alive between taps on purpose. Creating one per call means the Taptic Engine
// is cold every time, which costs a few tens of milliseconds before the tap is felt -- long enough
// to arrive after the touch that caused it.
static UISelectionFeedbackGenerator *selectionGenerator = nil;
static UIImpactFeedbackGenerator *lightGenerator = nil;
static UIImpactFeedbackGenerator *mediumGenerator = nil;
static UINotificationFeedbackGenerator *notificationGenerator = nil;

extern "C" void _freeflowHaptic(int type)
{
    // Haptics arrived with iOS 10 and the Taptic Engine. Older devices get nothing rather than a
    // fallback buzz: there is no light tap to fall back TO, only the full-strength vibration, which
    // is the thing this whole file exists to avoid.
    if (@available(iOS 10.0, *))
    {
        switch (type)
        {
            case 1:
                if (lightGenerator == nil)
                {
                    lightGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
                }
                [lightGenerator prepare];
                [lightGenerator impactOccurred];
                break;

            case 2:
                if (mediumGenerator == nil)
                {
                    mediumGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                }
                [mediumGenerator prepare];
                [mediumGenerator impactOccurred];
                break;

            case 3:
                if (notificationGenerator == nil)
                {
                    notificationGenerator = [[UINotificationFeedbackGenerator alloc] init];
                }
                [notificationGenerator prepare];
                [notificationGenerator notificationOccurred:UINotificationFeedbackTypeSuccess];
                break;

            case 4:
                if (notificationGenerator == nil)
                {
                    notificationGenerator = [[UINotificationFeedbackGenerator alloc] init];
                }
                [notificationGenerator prepare];
                [notificationGenerator notificationOccurred:UINotificationFeedbackTypeWarning];
                break;

            default:
                if (selectionGenerator == nil)
                {
                    selectionGenerator = [[UISelectionFeedbackGenerator alloc] init];
                }
                [selectionGenerator prepare];
                [selectionGenerator selectionChanged];
                break;
        }
    }
}
