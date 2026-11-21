using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-click setup for the AdMob-Demo tap game.
/// Adds an "AdMob Demo" menu to the Unity menu bar:
///   1. Build Main Scene      - imports sprites + sounds, builds the UI, adds TapGame, GameAudio
///                              and AdsManager, saves Assets/Scenes/Main.unity
///   2. Apply Android Settings - package name, portrait, IL2CPP + ARM64, minimum API level
///   3. Build APK              - builds Builds/AdMob-Demo.apk, no file dialog needed
///   4. Build APK + Install on Phone (USB) - same, then installs and opens it on a USB-connected phone
/// Editor-only: this file must live inside a folder named "Editor". It is not included in builds.
/// Step 3 version: also builds the "Ad break" screen and wires AdsManager to the rewarded button.
/// Needs the Google Mobile Ads plugin (because of AdsManager).
/// </summary>
public static class AdMobDemoSetup
{
    const string ArtFolder = "Assets/Art";
    const string AudioFolder = "Assets/Audio";
    static readonly string[] TapSoundFiles = { "tap_pop_1.wav", "tap_pop_2.wav", "tap_pop_3.wav" };
    const string SceneFolder = "Assets/Scenes";
    const string ScenePath = "Assets/Scenes/Main.unity";

    // Reference resolution: portrait 1080 x 1920. The bottom ~230 px stay empty for the banner ad.
    static readonly Vector2 ReferenceResolution = new Vector2(1080, 1920);
    static readonly Color32 CameraColor = new Color32(22, 16, 58, 255);

    // ------------------------------------------------------------------ menu 1
    [MenuItem("AdMob Demo/1. Build Main Scene", false, 1)]
    public static void BuildMainScene()
    {
        if (Resources.Load<TMP_Settings>("TMP Settings") == null)
        {
            EditorUtility.DisplayDialog("TextMeshPro is not set up yet",
                "Import the TMP Essential Resources first:\n\nWindow > TextMeshPro > Import TMP Essential Resources\n\n" +
                "Click 'Import' in that window, then run 'AdMob Demo > 1. Build Main Scene' again.", "OK");
            EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Rebuild Main scene?",
                ScenePath + " already exists. Replace it with a fresh copy?", "Replace", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // ---- sprites (also fixes their import settings)
        Sprite background = ImportSprite("background.png", Vector4.zero);
        Sprite tap = ImportSprite("tap_button.png", Vector4.zero);
        Sprite tapPressed = ImportSprite("tap_button_pressed.png", Vector4.zero);
        Sprite pill = ImportSprite("pill_green.png", new Vector4(64, 76, 64, 64)); // 9-slice: left, bottom, right, top
        Sprite panel = ImportSprite("panel.png", new Vector4(56, 56, 56, 56));
        Sprite play = ImportSprite("icon_play.png", Vector4.zero);
        ImportSprite("app_icon.png", Vector4.zero);
        if (background == null || tap == null || tapPressed == null || pill == null || panel == null || play == null)
            return; // ImportSprite already showed the error

        // ---- sounds (also fixes their import settings)
        var tapSounds = new AudioClip[TapSoundFiles.Length];
        for (int i = 0; i < TapSoundFiles.Length; i++) tapSounds[i] = ImportAudio(TapSoundFiles[i]);
        AudioClip rewardSound = ImportAudio("reward_coin.wav");
        AudioClip clickSound = ImportAudio("ui_click.wav");
        if (Array.IndexOf(tapSounds, null) >= 0 || rewardSound == null || clickSound == null)
            return; // ImportAudio already showed the error

        // ---- fresh scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera (only clears the screen; the UI draws on top of it)
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = CameraColor;
        camGO.AddComponent<AudioListener>();

        // Canvas
        var canvasGO = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.layer = LayerMask.NameToLayer("UI");
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        Transform canvas = canvasGO.transform;

        // EventSystem (works with both the old Input Manager and the new Input System)
        var esGO = new GameObject("EventSystem", typeof(EventSystem));
        AddInputModule(esGO);

        // Background
        var bg = CreateUI("Background", canvas);
        Stretch(bg);
        var bgImage = bg.gameObject.AddComponent<Image>();
        bgImage.sprite = background;
        bgImage.raycastTarget = false;

        // Score card (top)
        var card = CreateUI("ScoreCard", canvas);
        Place(card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(760f, 330f));
        var cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = panel;
        cardImage.type = Image.Type.Sliced;
        cardImage.raycastTarget = false;

        var scoreLabel = CreateText("ScoreLabel", card, "SCORE", 52f, new Color(1f, 1f, 1f, 0.7f));
        scoreLabel.characterSpacing = 12f;
        Place(scoreLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(700f, 70f));

        var scoreText = CreateText("ScoreText", card, "0", 180f, Color.white);
        scoreText.enableAutoSizing = true;
        scoreText.fontSizeMin = 80f;
        scoreText.fontSizeMax = 180f;
        Place(scoreText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -36f), new Vector2(720f, 210f));

        // Tap button (centre)
        var tapRT = CreateUI("TapButton", canvas);
        Place(tapRT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(560f, 560f));
        var tapImage = tapRT.gameObject.AddComponent<Image>();
        tapImage.sprite = tap;
        tapImage.preserveAspect = true;
        var tapButton = tapRT.gameObject.AddComponent<Button>();
        tapButton.targetGraphic = tapImage;
        tapButton.transition = Selectable.Transition.SpriteSwap;
        var states = tapButton.spriteState;
        states.pressedSprite = tapPressed;
        tapButton.spriteState = states;
        tapButton.navigation = new Navigation { mode = Navigation.Mode.None };

        // Hint (under the button)
        var hint = CreateText("Hint", canvas, "Tap the button to score +1", 44f, new Color(1f, 1f, 1f, 0.75f));
        Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -320f), new Vector2(900f, 80f));

        // Rewarded ad button (bottom, above the banner area).
        // Starts NOT interactable: AdsManager turns it on when a rewarded ad has loaded.
        var rewardRT = CreateUI("RewardedAdButton", canvas);
        Place(rewardRT, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 290f), new Vector2(800f, 150f));
        var rewardImage = rewardRT.gameObject.AddComponent<Image>();
        rewardImage.sprite = pill;
        rewardImage.type = Image.Type.Sliced;
        var rewardButton = rewardRT.gameObject.AddComponent<Button>();
        rewardButton.targetGraphic = rewardImage;
        var colors = rewardButton.colors;
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
        rewardButton.colors = colors;
        rewardButton.navigation = new Navigation { mode = Navigation.Mode.None };
        rewardButton.interactable = false;

        var icon = CreateUI("Icon", rewardRT);
        Place(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(36f, 5f), new Vector2(84f, 84f));
        var iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.sprite = play;
        iconImage.raycastTarget = false;

        var rewardLabel = CreateText("Label", rewardRT, "Watch ad for +50 points", 48f, Color.white);
        rewardLabel.enableAutoSizing = true;
        rewardLabel.fontSizeMin = 32f;
        rewardLabel.fontSizeMax = 48f;
        var labelRT = rewardLabel.rectTransform;
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(130f, 10f);
        labelRT.offsetMax = new Vector2(-30f, 0f);

        // "Ad break" screen shown for a moment before each interstitial.
        // Created last so it sits on top of everything; its dark image blocks taps while it's up.
        var adBreak = CreateUI("AdBreakOverlay", canvas);
        Stretch(adBreak);
        var adBreakImage = adBreak.gameObject.AddComponent<Image>();
        adBreakImage.color = new Color(0.04f, 0.03f, 0.12f, 0.82f);
        adBreakImage.raycastTarget = true;
        var adBreakTitle = CreateText("Title", adBreak, "Ad break", 110f, Color.white);
        Place(adBreakTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(900f, 150f));
        var adBreakText = CreateText("Subtitle", adBreak, "The ad starts in a moment", 46f, new Color(1f, 1f, 1f, 0.75f));
        Place(adBreakText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(900f, 70f));
        adBreak.gameObject.SetActive(false);

        // Game logic
        var gameGO = new GameObject("Game");
        var game = gameGO.AddComponent<TapGame>();
        var so = new SerializedObject(game);
        so.FindProperty("tapButton").objectReferenceValue = tapButton;
        so.FindProperty("scoreText").objectReferenceValue = scoreText;
        so.FindProperty("hint").objectReferenceValue = hint.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Sounds
        var audio = gameGO.AddComponent<GameAudio>();
        var audioSO = new SerializedObject(audio);
        var tapList = audioSO.FindProperty("tapClips");
        tapList.arraySize = tapSounds.Length;
        for (int i = 0; i < tapSounds.Length; i++)
            tapList.GetArrayElementAtIndex(i).objectReferenceValue = tapSounds[i];
        audioSO.FindProperty("rewardClip").objectReferenceValue = rewardSound;
        audioSO.FindProperty("clickClip").objectReferenceValue = clickSound;
        audioSO.ApplyModifiedPropertiesWithoutUndo();

        // Ads: banner, interstitial every 10 taps, rewarded +50
        var ads = gameGO.AddComponent<AdsManager>();
        var adsSO = new SerializedObject(ads);
        adsSO.FindProperty("rewardedButton").objectReferenceValue = rewardButton;
        adsSO.FindProperty("adBreakOverlay").objectReferenceValue = adBreak.gameObject;
        adsSO.ApplyModifiedPropertiesWithoutUndo();

        // ---- save + make it the only scene in the build
        if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Selection.activeGameObject = gameGO;
        Debug.Log("AdMob Demo: Main scene built and saved to " + ScenePath + ". Press Play and tap!");
        EditorUtility.DisplayDialog("Main scene ready",
            "Saved to " + ScenePath + " and set as the only scene in the build.\n\n" +
            "Set the Game view to 1080x1920 (portrait), press Play and tap the button.\n" +
            "You should see a placeholder banner at the bottom, an 'Ad break' + interstitial every 10 taps, " +
            "and the green button lights up when the rewarded ad is ready.", "Nice");
    }

    // ------------------------------------------------------------------ menu 2
    [MenuItem("AdMob Demo/2. Apply Android Settings", false, 2)]
    public static void ApplyAndroidSettings()
    {
        PlayerSettings.companyName = "Anas Afzal";
        PlayerSettings.productName = "AdMob Demo";
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.anasafzal.admobdemo");
        PlayerSettings.Android.bundleVersionCode = 1;

        // Portrait only
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

        // 64-bit: required by Google Play, and some new phones can no longer run 32-bit apps.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // Android 7.0 (API 24) minimum keeps the Google Mobile Ads SDK happy. Never lowers a higher value.
        const int minApi = 24;
        if ((int)PlayerSettings.Android.minSdkVersion < minApi)
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)minApi;

        AssetDatabase.SaveAssets();
        Debug.Log("AdMob Demo: Android Player Settings applied.");
        EditorUtility.DisplayDialog("Android settings applied",
            "Company: Anas Afzal\nProduct: AdMob Demo\nPackage: com.anasafzal.admobdemo\n" +
            "Orientation: Portrait\nScripting: IL2CPP, ARM64\nMinimum API: " + (int)PlayerSettings.Android.minSdkVersion +
            "\n\nStill to do by hand: set the Default Icon to Assets/Art/app_icon.png (see Guide.md).", "OK");
    }

    // ------------------------------------------------------------------ menu 3 + 4
    const string ApkRelativePath = "Builds/AdMob-Demo.apk";

    [MenuItem("AdMob Demo/3. Build APK", false, 20)]
    public static void BuildApk() => BuildAndroid(false);

    [MenuItem("AdMob Demo/4. Build APK + Install on Phone (USB)", false, 21)]
    public static void BuildApkAndRun() => BuildAndroid(true);

    static void BuildAndroid(bool installOnPhone)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            EditorUtility.DisplayDialog("Switch to Android first",
                "The active platform isn't Android yet.\n\nFile > Build Profiles > Android > Switch Platform, " +
                "then run this again.", "OK");
            return;
        }

        var scenes = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) scenes.Add(s.path);
        if (scenes.Count == 0)
        {
            EditorUtility.DisplayDialog("No scene in the build",
                "Run 'AdMob Demo > 1. Build Main Scene' first. It adds Main.unity to the build.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // A plain APK you can install directly (not a Play Store bundle, not an exported project).
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string apkPath = Path.Combine(projectRoot, ApkRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(apkPath));

        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = apkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = installOnPhone ? BuildOptions.AutoRunPlayer : BuildOptions.None
        };

        Debug.Log("AdMob Demo: building " + apkPath + " ... (the first IL2CPP build can take 10+ minutes)");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            string size = (summary.totalSize / (1024f * 1024f)).ToString("0.0");
            string time = summary.totalTime.ToString(@"m\:ss");
            Debug.Log($"AdMob Demo: build succeeded: {apkPath} ({size} MB, {time})");
            string message = $"Saved to:\n{ApkRelativePath}  ({size} MB, built in {time})\n\n" +
                (installOnPhone
                    ? "Unity also installed it on your phone. Look for 'AdMob Demo' there."
                    : "Copy it to your phone (USB, Google Drive or Telegram) and open it to install.");
            if (EditorUtility.DisplayDialog("APK ready", message, "Show in folder", "OK"))
                EditorUtility.RevealInFinder(apkPath);
            return;
        }

        if (summary.result == BuildResult.Cancelled)
        {
            Debug.LogWarning("AdMob Demo: build cancelled.");
            return;
        }

        // Failed: show the first few errors so you don't have to dig through the Console.
        var errors = new StringBuilder();
        int shown = 0;
        foreach (var step in report.steps)
        {
            foreach (var msg in step.messages)
            {
                if (shown >= 3 || (msg.type != LogType.Error && msg.type != LogType.Exception)) continue;
                string line = msg.content.Split('\n')[0];
                if (line.Length > 220) line = line.Substring(0, 220) + "...";
                errors.AppendLine("- " + line);
                shown++;
            }
        }
        if (shown == 0) errors.AppendLine("- (no details in the build report; see the red errors in the Console)");

        Debug.LogError($"AdMob Demo: build FAILED with {summary.totalErrors} error(s). See the Console above.");
        EditorUtility.DisplayDialog("Build failed",
            $"{summary.totalErrors} error(s). The first ones:\n\n{errors}\n" +
            "The full list is in the Console. Send the FIRST red error to get it fixed.", "OK");
    }

    // ------------------------------------------------------------------ helpers
    static Sprite ImportSprite(string fileName, Vector4 border)
    {
        string path = ArtFolder + "/" + fileName;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Missing art", "Could not find " + path +
                "\n\nCopy the Art folder from the kit into Assets/ and try again.", "OK");
            return null;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; // needed for clean 9-slicing
        settings.spriteBorder = border;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static AudioClip ImportAudio(string fileName)
    {
        string path = AudioFolder + "/" + fileName;
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Missing sound", "Could not find " + path +
                "\n\nCopy the Audio folder from the kit into Assets/ and try again.", "OK");
            return null;
        }

        // Short sound effects: keep them uncompressed in memory so they play instantly.
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static void AddInputModule(GameObject eventSystem)
    {
#if ENABLE_INPUT_SYSTEM
        // New Input System (default in Unity 6). Looked up by name so this file compiles without the package.
        Type newModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (newModule != null)
        {
            eventSystem.AddComponent(newModule);
            return;
        }
#endif
        eventSystem.AddComponent<StandaloneInputModule>();
    }

    static RectTransform CreateUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color)
    {
        var rt = CreateUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
