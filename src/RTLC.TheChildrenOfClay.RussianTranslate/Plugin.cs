using System;
using System.IO;
using System.Linq;
using AC;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RTLC.TheChildrenOfClay.RussianTranslate;

/// <summary>
/// BepInEx entrypoint for The Children of Clay Russian localization plugin.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "rtlc.thechildrenofclay.russiantranslate";
    public const string PluginName = "The Children of Clay Russian Translate";
    public const string PluginVersion = "1.0.0";

    internal static Plugin Instance { get; private set; }
    internal static ManualLogSource Log { get; private set; }

    internal ConfigEntry<bool> EnabledConfig { get; private set; }
    internal ConfigEntry<bool> ReplaceTexturesConfig { get; private set; }
    internal ConfigEntry<bool> AutoSelectRussianOnFirstRunConfig { get; private set; }
    internal ConfigEntry<bool> FirstRunCompletedConfig { get; private set; }

    private Harmony _harmony;
    private AssetBundle _assetBundle;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        EnabledConfig = Config.Bind("General", "Enabled", true, "Enable Russian localization plugin.");
        ReplaceTexturesConfig = Config.Bind("General", "ReplaceTextures", true, "Replace English credits/title/Making-Of textures with Russian versions.");
        AutoSelectRussianOnFirstRunConfig = Config.Bind("General", "AutoSelectRussianOnFirstRun", true, "Automatically select Russian language on first launch.");
        FirstRunCompletedConfig = Config.Bind("Internal", "FirstRunCompleted", false, "Tracks whether initial Russian auto-selection already ran.");

        string pluginDir = Path.GetDirectoryName(Info.Location) ?? BepInEx.Paths.PluginPath;
        LoadLocalizedResources(pluginDir);

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(HarmonyPatches));

        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        EventManager.OnMenuTurnOn = (EventManager.Delegate_OnMenuTurnOn)Delegate.Combine(
            EventManager.OnMenuTurnOn,
            new EventManager.Delegate_OnMenuTurnOn(OnMenuTurnOn));
        EventManager.OnChangeLanguage += OnChangeLanguage;

        Log.LogInfo($"{PluginName} v{PluginVersion} initialized (Enabled={EnabledConfig.Value}, ReplaceTextures={ReplaceTexturesConfig.Value}).");
    }

    private void LoadLocalizedResources(string pluginDir)
    {
        string bundlePath = Path.Combine(pluginDir, "rtlc.bundle");
        if (File.Exists(bundlePath))
        {
            _assetBundle = AssetBundle.LoadFromFile(bundlePath);
            if (_assetBundle != null)
            {
                TextAsset ruAsset = _assetBundle.LoadAsset<TextAsset>("ru")
                                    ?? _assetBundle.LoadAllAssets<TextAsset>().FirstOrDefault();
                if (ruAsset != null)
                {
                    TranslationManager.LoadFromJson(ruAsset.text, "rtlc.bundle:ru.json");
                }
                else
                {
                    Log.LogError($"TextAsset 'ru' not found inside {bundlePath}.");
                }

                TextureLocalizer.LoadFromBundle(_assetBundle);
                return;
            }

            Log.LogError($"Failed to load AssetBundle: {bundlePath}. Attempting directory fallback.");
        }

        TranslationManager.Load(Path.Combine(pluginDir, "Translations", "ru.json"));
        TextureLocalizer.LoadFromDirectory(Path.Combine(pluginDir, "Textures"));
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        EventManager.OnMenuTurnOn = (EventManager.Delegate_OnMenuTurnOn)Delegate.Remove(
            EventManager.OnMenuTurnOn,
            new EventManager.Delegate_OnMenuTurnOn(OnMenuTurnOn));
        EventManager.OnChangeLanguage -= OnChangeLanguage;
        _harmony?.UnpatchSelf();

        if (_assetBundle != null)
        {
            _assetBundle.Unload(false);
            _assetBundle = null;
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (KickStarter.runtimeLanguages != null)
        {
            TranslationManager.InjectRussianLanguage();
        }
        FontPatcher.ApplyForCurrentLanguage();
        TextureLocalizer.ApplyForCurrentLanguage();
    }

    private static void OnMenuTurnOn(Menu menu, bool isInstant)
    {
        FontPatcher.ApplyToMenu(menu, TranslationManager.IsRussianActive);
        TextureLocalizer.ApplyForCurrentLanguage();
    }

    private static void OnChangeLanguage(int languageIndex)
    {
        FontPatcher.ApplyForCurrentLanguage();
        TextureLocalizer.ApplyForCurrentLanguage();
    }
}
