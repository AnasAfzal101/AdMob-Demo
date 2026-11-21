using UnityEngine;

/// <summary>
/// Fun, satisfying sounds for the tap game.
///
/// Every tap plays a short "bloop". Tap fast and the bloops climb a happy musical scale,
/// then come back down, like a little melody. Pause for a moment and it starts again
/// from the bottom.
///
/// Also has PlayReward() (coin chime for the +50 reward) and PlayClick() (UI buttons),
/// which the ads step uses.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Header("Clips")]
    [SerializeField] AudioClip[] tapClips;
    [SerializeField] AudioClip rewardClip;
    [SerializeField] AudioClip clickClip;

    [Header("Mix")]
    [SerializeField, Range(0f, 1f)] float volume = 0.8f;

    [Header("Combo")]
    [Tooltip("Taps closer together than this many seconds climb the scale.")]
    [SerializeField] float comboWindow = 0.35f;

    // Major pentatonic scale in semitones: any two notes sound nice together.
    static readonly int[] Scale = { 0, 2, 4, 7, 9, 12 };

    const int VoiceCount = 6; // lets quick taps overlap instead of cutting each other off
    AudioSource[] voices;
    int nextVoice;
    int comboCount;
    float lastTapTime = -10f;
    int lastClip = -1;
    TapGame game;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }
        Instance = this;

        voices = new AudioSource[VoiceCount];
        for (int i = 0; i < VoiceCount; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // plain 2D sound
            voices[i] = source;
        }
    }

    void Start()
    {
        game = TapGame.Instance;
        if (game != null) game.Tapped += OnTapped;
        else Debug.LogWarning("GameAudio: no TapGame in the scene, so tap sounds are off.");
    }

    void OnDestroy()
    {
        if (game != null) game.Tapped -= OnTapped;
        if (Instance == this) Instance = null;
    }

    void OnTapped(int taps)
    {
        float now = Time.unscaledTime;
        comboCount = (now - lastTapTime <= comboWindow) ? comboCount + 1 : 0;
        lastTapTime = now;

        // Climb up the scale, then back down: 0 1 2 3 4 5 4 3 2 1 0 1 ...
        int top = Scale.Length - 1;
        int step = comboCount % (2 * top);
        int note = step <= top ? step : 2 * top - step;

        float pitch = Mathf.Pow(2f, Scale[note] / 12f) * Random.Range(0.995f, 1.005f);
        Play(NextTapClip(), pitch, volume);
    }

    /// <summary>Coin chime. Call it when the player gets the +50 reward.</summary>
    public void PlayReward() => Play(rewardClip, 1f, volume);

    /// <summary>Soft click for UI buttons.</summary>
    public void PlayClick() => Play(clickClip, 1f, volume);

    AudioClip NextTapClip()
    {
        if (tapClips == null || tapClips.Length == 0) return null;
        if (tapClips.Length == 1) return tapClips[0];

        int i;
        do { i = Random.Range(0, tapClips.Length); } while (i == lastClip); // never the same one twice in a row
        lastClip = i;
        return tapClips[i];
    }

    void Play(AudioClip clip, float pitch, float vol)
    {
        if (clip == null) return;
        var source = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        source.Stop();
        source.clip = clip;
        source.pitch = pitch;
        source.volume = vol;
        source.Play();
    }
}
