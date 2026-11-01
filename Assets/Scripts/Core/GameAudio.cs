using UnityEngine;

// Background music and the victory fanfare. Creates itself when the game starts and survives scene
// loads, so the music never restarts between screens. It also holds the AudioListener, because the
// scenes' cameras have none. The music dips while the fanfare plays.
public class GameAudio : MonoBehaviour
{
    public const string MusicClip = "Audio/SanChoi_BGM_Loop";
    public const string VictoryClip = "Audio/emand_edroff-victory-bell-success-fanfare-576275";

    public const float MusicVolume = 0.5f;
    public const float DuckedVolume = 0.15f;
    const float VictoryVolume = 1f;
    const float FadeSeconds = 0.5f;  // from full volume to silence

    AudioSource _music;
    AudioSource _victory;

    public static GameAudio Instance { get; private set; }

    public bool IsMusicPlaying => _music.isPlaying;
    public bool IsVictoryPlaying => _victory.isPlaying;

    // Domain reload is disabled in this project, so statics survive between play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject(nameof(GameAudio), typeof(AudioListener));
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<GameAudio>();
    }

    public static void PlayVictory()
    {
        if (Instance != null) Instance._victory.Play();
    }

    public static void StopVictory()
    {
        if (Instance != null) Instance._victory.Stop();
    }

    public static float NextMusicVolume(float current, bool victoryPlaying, float deltaTime) =>
        Mathf.MoveTowards(current, victoryPlaying ? DuckedVolume : MusicVolume, deltaTime / FadeSeconds);

    void Awake()
    {
        _music = AddSource(MusicClip, MusicVolume);
        _music.loop = true;
        _music.Play();

        _victory = AddSource(VictoryClip, VictoryVolume);
    }

    void Update()
    {
        _music.volume = NextMusicVolume(_music.volume, _victory.isPlaying, Time.unscaledDeltaTime);
    }

    AudioSource AddSource(string clip, float volume)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.clip = Resources.Load<AudioClip>(clip);
        source.volume = volume;
        source.playOnAwake = false;
        return source;
    }
}
