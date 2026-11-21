using System;
using System.Collections;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// All the ads for the AdMob demo, using Google's TEST ad unit IDs:
///   1. Banner        - anchored adaptive banner at the bottom of the screen
///   2. Interstitial  - full-screen ad every 10 taps, after a short "Ad break" screen
///   3. Rewarded      - "Watch ad for +50 points". The points are added only after the
///                      player has earned the reward AND the ad has closed.
///
/// Every SDK callback is moved to Unity's main thread with MobileAdsEventExecutor.ExecuteInUpdate,
/// because Unity objects (score, UI, sounds) may only be touched there.
/// </summary>
public class AdsManager : MonoBehaviour
{
    // Ad unit IDs that start with this publisher ID are Google's test ads: safe to tap.
    const string GoogleTestPublisher = "ca-app-pub-3940256099942544/";

    public static AdsManager Instance { get; private set; }

    /// <summary>True once the SDK has started.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>Fires once, when the SDK is ready.</summary>
    public event Action Initialized;

    [Header("Ad unit IDs (Android) - Google's test IDs")]
    [SerializeField] string bannerId = "ca-app-pub-3940256099942544/9214589741";
    [SerializeField] string interstitialId = "ca-app-pub-3940256099942544/1033173712";
    [SerializeField] string rewardedId = "ca-app-pub-3940256099942544/5224354917";

    [Header("Rules")]
    [SerializeField] int interstitialEveryTaps = 10;
    [SerializeField] int rewardPoints = 50;
    [Tooltip("Seconds the 'Ad break' screen shows before the interstitial, so the player stops tapping.")]
    [SerializeField] float adBreakSeconds = 1f;

    [Header("Scene references")]
    [SerializeField] Button rewardedButton;
    [SerializeField] GameObject adBreakOverlay;

    BannerView banner;
    InterstitialAd interstitial;
    RewardedAd rewarded;

    bool rewardEarned;      // Google says the player watched enough to earn the reward
    bool rewardedOnScreen;  // the rewarded ad is showing right now
    bool adBreakRunning;
    bool quitting;          // the app (or Editor Play mode) is shutting down
    int bannerRetries, interstitialRetries, rewardedRetries;
    TapGame game;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        SetRewardedButton(false);
        if (rewardedButton != null) rewardedButton.onClick.AddListener(ShowRewarded);
        SetAdBreak(false);

        game = TapGame.Instance;
        if (game != null) game.Tapped += OnTapped;
        else Debug.LogWarning("[AdsManager] No TapGame in the scene, so no interstitials.");

        WarnIfRealAdUnits();

        Debug.Log("[AdsManager] Starting the Google Mobile Ads SDK...");
        MobileAds.Initialize(status => OnMainThread(() => OnInitialized(status)));
    }

    void OnInitialized(InitializationStatus status)
    {
        if (status == null)
        {
            Debug.LogError("[AdsManager] Google Mobile Ads failed to initialize.");
            return;
        }

        IsInitialized = true;
        Debug.Log("[AdsManager] Google Mobile Ads SDK is ready.");
        foreach (var adapter in status.getAdapterStatusMap())
            Debug.Log($"[AdsManager]   {adapter.Key}: {adapter.Value.InitializationState} ({adapter.Value.Description})");
        Initialized?.Invoke();

        LoadBanner();
        LoadInterstitial();
        LoadRewarded();
    }

    // Unity calls this before OnDestroy when the app quits or you stop Play mode in the Editor.
    void OnApplicationQuit() => quitting = true;

    void OnDestroy()
    {
        if (game != null) game.Tapped -= OnTapped;
        if (rewardedButton != null) rewardedButton.onClick.RemoveListener(ShowRewarded);

        // When everything is shutting down anyway, leave the ads alone. In the Editor, destroying them
        // here makes the plugin's stand-in ads create a new "AdBehaviour" object in the middle of the
        // scene closing (Unity's "Some objects were not cleaned up" error). On a phone, the OS cleans up.
        if (!quitting)
        {
            banner?.Destroy();
            interstitial?.Destroy();
            rewarded?.Destroy();
        }
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ 1. banner

    void LoadBanner()
    {
        if (banner == null)
        {
            // Adaptive banner: full screen width, height picked by Google for this device.
            AdSize size = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
            banner = new BannerView(bannerId, size, AdPosition.Bottom);

            banner.OnBannerAdLoaded += () => OnMainThread(() =>
            {
                bannerRetries = 0;
                Debug.Log("[AdsManager] Banner loaded.");
            });
            banner.OnBannerAdLoadFailed += error => OnMainThread(() =>
            {
                Debug.LogWarning("[AdsManager] Banner failed to load: " + error.GetMessage());
                RetryLater(ref bannerRetries, LoadBanner);
            });
        }

        banner.LoadAd(new AdRequest()); // shows by itself once loaded
    }

    // ------------------------------------------------------------------ 2. interstitial

    void LoadInterstitial()
    {
        interstitial?.Destroy();
        interstitial = null;

        InterstitialAd.Load(interstitialId, new AdRequest(), (ad, error) => OnMainThread(() =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning("[AdsManager] Interstitial failed to load: " + error?.GetMessage());
                RetryLater(ref interstitialRetries, LoadInterstitial);
                return;
            }

            interstitialRetries = 0;
            interstitial = ad;
            ad.OnAdFullScreenContentClosed += () => OnMainThread(OnInterstitialFinished);
            ad.OnAdFullScreenContentFailed += showError => OnMainThread(() =>
            {
                Debug.LogWarning("[AdsManager] Interstitial failed to show: " + showError.GetMessage());
                OnInterstitialFinished();
            });
            Debug.Log("[AdsManager] Interstitial loaded.");
        }));
    }

    void OnTapped(int taps)
    {
        if (interstitialEveryTaps <= 0 || taps % interstitialEveryTaps != 0 || adBreakRunning) return;

        if (interstitial == null || !interstitial.CanShowAd())
        {
            Debug.Log($"[AdsManager] Tap {taps}: interstitial not loaded yet, skipping this one.");
            return;
        }
        StartCoroutine(AdBreakThenInterstitial());
    }

    IEnumerator AdBreakThenInterstitial()
    {
        // A clear "Ad break" screen first. It blocks taps, so a fast-tapping player
        // doesn't accidentally tap the ad the moment it appears.
        adBreakRunning = true;
        SetAdBreak(true);
        yield return new WaitForSecondsRealtime(adBreakSeconds);

        if (interstitial != null && interstitial.CanShowAd())
            interstitial.Show();
        else
            OnInterstitialFinished();
    }

    void OnInterstitialFinished()
    {
        adBreakRunning = false;
        SetAdBreak(false);
        LoadInterstitial(); // get the next one ready
    }

    // ------------------------------------------------------------------ 3. rewarded

    void LoadRewarded()
    {
        rewarded?.Destroy();
        rewarded = null;
        SetRewardedButton(false); // greyed out until an ad is ready

        RewardedAd.Load(rewardedId, new AdRequest(), (ad, error) => OnMainThread(() =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning("[AdsManager] Rewarded ad failed to load: " + error?.GetMessage());
                RetryLater(ref rewardedRetries, LoadRewarded);
                return;
            }

            rewardedRetries = 0;
            rewarded = ad;
            ad.OnAdFullScreenContentClosed += () => OnMainThread(OnRewardedFinished);
            ad.OnAdFullScreenContentFailed += showError => OnMainThread(() =>
            {
                Debug.LogWarning("[AdsManager] Rewarded ad failed to show: " + showError.GetMessage());
                rewardEarned = false;
                OnRewardedFinished();
            });
            SetRewardedButton(true);
            Debug.Log("[AdsManager] Rewarded ad loaded.");
        }));
    }

    /// <summary>Called by the "Watch ad for +50 points" button.</summary>
    public void ShowRewarded()
    {
        if (rewarded == null || !rewarded.CanShowAd())
        {
            Debug.Log("[AdsManager] Rewarded ad not ready yet.");
            return;
        }

        if (GameAudio.Instance != null) GameAudio.Instance.PlayClick();
        SetRewardedButton(false); // no double taps
        rewardEarned = false;
        rewardedOnScreen = true;

        rewarded.Show(reward => OnMainThread(() =>
        {
            // The player has earned the reward. Don't give it yet: wait until the ad has
            // closed, so the points land when the player is back in the game.
            rewardEarned = true;
            if (!rewardedOnScreen) GiveReward(); // rare: the close event arrived first
        }));
    }

    void OnRewardedFinished()
    {
        rewardedOnScreen = false;
        if (rewardEarned) GiveReward(); // closed early = no reward earned = no points
        LoadRewarded();                 // get the next one ready
    }

    void GiveReward()
    {
        rewardEarned = false;
        if (TapGame.Instance != null) TapGame.Instance.AddPoints(rewardPoints);
        if (GameAudio.Instance != null) GameAudio.Instance.PlayReward();
        Debug.Log($"[AdsManager] Reward given: +{rewardPoints} points.");
    }

    // ------------------------------------------------------------------ helpers

    static void OnMainThread(Action action) => MobileAdsEventExecutor.ExecuteInUpdate(action);

    void RetryLater(ref int retries, Action load)
    {
        // Wait longer after each failure (5 s, 10 s, 20 s, 40 s, then every 60 s): never spam requests.
        retries++;
        float delay = Mathf.Min(60f, 2.5f * Mathf.Pow(2f, retries));
        Debug.Log($"[AdsManager] Retrying in {delay:0} s.");
        StartCoroutine(After(delay, load));
    }

    static IEnumerator After(float seconds, Action action)
    {
        yield return new WaitForSecondsRealtime(seconds);
        action();
    }

    void SetRewardedButton(bool ready)
    {
        if (rewardedButton != null) rewardedButton.interactable = ready;
    }

    void SetAdBreak(bool on)
    {
        if (adBreakOverlay != null) adBreakOverlay.SetActive(on);
    }

    void WarnIfRealAdUnits()
    {
        foreach (string id in new[] { bannerId, interstitialId, rewardedId })
        {
            if (!id.StartsWith(GoogleTestPublisher, StringComparison.Ordinal))
                Debug.LogWarning("[AdsManager] REAL ad unit in use: " + id +
                                 ". Never tap your own real ads: Google can suspend the account.");
        }
    }
}
