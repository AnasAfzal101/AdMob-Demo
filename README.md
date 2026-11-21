# AdMob-Demo

**AdMob integration demo: banner, interstitial, rewarded.**

A tiny Unity tap game that shows Google AdMob ads working the right way on Android.
Tap the button to score +1. The ads are the point, not the game.

<!-- Add the demo recording here, e.g. ![Demo](docs/demo.gif) -->

## The ads

| Ad | How it works |
|---|---|
| **Banner** | Adaptive banner anchored to the bottom of the screen |
| **Interstitial** | Every 10 taps, after a 1-second "Ad break" screen so fast tappers don't click it by accident |
| **Rewarded** | "Watch ad for +50 points". The points are added **only after the ad is watched and closed**. |

Also handled:

- Ads preload, so they show instantly.
- Failed loads retry with growing delays.
- Every SDK callback runs on Unity's main thread.
- Ad unit IDs are set in the Inspector, with no code changes needed.

## Built with

- Unity 6000.6.8f1, Android (IL2CPP, ARM64, min API 24)
- Google Mobile Ads Unity plugin 11.7.0
- Google's **test** App ID and test ad unit IDs

## Try it

1. Open the project in Unity 6000.6.8f1 and switch the platform to Android.
2. Open `Assets/Scenes/Main.unity` and press Play to see the Editor's placeholder ads.
3. Run **AdMob Demo → 3. Build APK**, install the APK on an Android phone, and see Google's test ads.

## Key scripts

| Script | Job |
|---|---|
| `Assets/Scripts/AdsManager.cs` | Banner, interstitial and rewarded ads |
| `Assets/Scripts/TapGame.cs` | The game: +1 per tap |
| `Assets/Scripts/GameAudio.cs` | Tap sounds with a musical combo |
| `Assets/Scripts/Editor/AdMobDemoSetup.cs` | One-click scene setup, Android settings and APK build |

---

Made by **Anas Afzal**, Unity developer specialising in AdMob and mobile ads integration.
