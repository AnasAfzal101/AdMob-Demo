using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole game: tap the button, get +1.
/// It is tiny on purpose. The ads are the star of this demo, not the game.
///
/// The ads code (next step) only needs three things from here:
///   TapGame.Instance       - a reference to this script
///   Tapped (event)         - fires after every tap, so an interstitial can show every 10 taps
///   AddPoints(int amount)  - call it from the rewarded ad callback to give +50
/// </summary>
public class TapGame : MonoBehaviour
{
    public static TapGame Instance { get; private set; }

    /// <summary>Fires after every tap. The int is how many taps happened this session.</summary>
    public event Action<int> Tapped;

    [Header("Scene references")]
    [SerializeField] Button tapButton;
    [SerializeField] TMP_Text scoreText;
    [Tooltip("The 'Tap the button' hint. Hidden after the first tap.")]
    [SerializeField] GameObject hint;

    [Header("Rules")]
    [SerializeField] int pointsPerTap = 1;

    [Header("Feel")]
    [SerializeField] float scorePunch = 1.18f;   // score text grows, then settles
    [SerializeField] float buttonPunch = 0.92f;  // button squashes, then settles
    [SerializeField] float punchTime = 0.12f;

    public int Score { get; private set; }
    public int TapCount { get; private set; }

    Coroutine scoreAnim;
    Coroutine buttonAnim;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("TapGame: there is more than one TapGame in the scene. Keeping the first one.");
            enabled = false;
            return;
        }
        Instance = this;
        Application.targetFrameRate = 60; // mobile defaults to 30 fps; 60 feels much smoother
    }

    void OnEnable()
    {
        if (tapButton != null) tapButton.onClick.AddListener(OnTap);
    }

    void OnDisable()
    {
        if (tapButton != null) tapButton.onClick.RemoveListener(OnTap);
    }

    void Start()
    {
        if (tapButton == null || scoreText == null)
            Debug.LogError("TapGame: assign Tap Button and Score Text in the Inspector.", this);
        RefreshScore();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnTap()
    {
        TapCount++;
        AddPoints(pointsPerTap);

        if (hint != null && hint.activeSelf) hint.SetActive(false);
        Punch(tapButton.transform, buttonPunch, ref buttonAnim);

        Tapped?.Invoke(TapCount);
    }

    /// <summary>Adds points to the score. Use this for rewards, e.g. AddPoints(50).</summary>
    public void AddPoints(int amount)
    {
        if (amount <= 0) return;
        Score += amount;
        RefreshScore();
        if (scoreText != null) Punch(scoreText.transform, scorePunch, ref scoreAnim);
    }

    void RefreshScore()
    {
        if (scoreText != null) scoreText.text = Score.ToString();
    }

    // ---- tiny code-only animation (no Animator needed) ----

    void Punch(Transform target, float peak, ref Coroutine running)
    {
        if (running != null) StopCoroutine(running);
        target.localScale = Vector3.one;
        running = StartCoroutine(PunchRoutine(target, peak));
    }

    IEnumerator PunchRoutine(Transform target, float peak)
    {
        // Unscaled time keeps it working even if an ad pauses the game (timeScale = 0).
        for (float t = 0f; t < punchTime; t += Time.unscaledDeltaTime)
        {
            float k = 1f - t / punchTime;          // 1 -> 0
            float s = 1f + (peak - 1f) * k * k;    // ease out
            target.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        target.localScale = Vector3.one;
    }
}
