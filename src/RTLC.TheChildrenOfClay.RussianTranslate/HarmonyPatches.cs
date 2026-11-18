using AC;
using HarmonyLib;

namespace RTLC.TheChildrenOfClay.RussianTranslate;

/// <summary>
/// Harmony patches into Adventure Creator initialization, translation lookup, and keyword matching.
/// </summary>
[HarmonyPatch]
internal static class HarmonyPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(RuntimeLanguages), nameof(RuntimeLanguages.OnInitPersistentEngine))]
    private static void RuntimeLanguages_OnInitPersistentEngine_Postfix()
    {
        TranslationManager.InjectRussianLanguage();
        FontPatcher.EnsureFontsReady();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RuntimeVariables), nameof(RuntimeVariables.TransferFromManager))]
    private static void RuntimeVariables_TransferFromManager_Postfix()
    {
        TranslationManager.RefreshGlobalVarTranslations();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerMenus), nameof(PlayerMenus.OnInitPersistentEngine))]
    private static void PlayerMenus_OnInitPersistentEngine_Postfix()
    {
        FontPatcher.ApplyForCurrentLanguage();
        TextureLocalizer.ApplyForCurrentLanguage();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Options), nameof(Options.OnInitPersistentEngine))]
    private static void Options_OnInitPersistentEngine_Prefix()
    {
        // Inject before Options.LoadPrefs() validates saved language index so index 8 is preserved across launches.
        TranslationManager.InjectRussianLanguage();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Options), nameof(Options.OnInitPersistentEngine))]
    private static void Options_OnInitPersistentEngine_Postfix()
    {
        Plugin plugin = Plugin.Instance;
        if (plugin != null &&
            plugin.EnabledConfig.Value &&
            plugin.AutoSelectRussianOnFirstRunConfig.Value &&
            !plugin.FirstRunCompletedConfig.Value &&
            TranslationManager.RussianLanguageIndex > 0)
        {
            plugin.FirstRunCompletedConfig.Value = true;
            plugin.Config.Save();

            if (Options.GetLanguage() != TranslationManager.RussianLanguageIndex)
            {
                Options.SetLanguage(TranslationManager.RussianLanguageIndex);
                Plugin.Log.LogInfo($"First run: automatically selected '{TranslationManager.RussianLanguageName}' (index {TranslationManager.RussianLanguageIndex}).");
            }
        }

        Plugin.Log.LogInfo($"Active AC language: index={Options.GetLanguage()}, name='{Options.GetLanguageName()}'.");
        FontPatcher.ApplyForCurrentLanguage();
        TextureLocalizer.ApplyForCurrentLanguage();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RuntimeLanguages), nameof(RuntimeLanguages.GetTranslation), typeof(string), typeof(int), typeof(int))]
    private static bool RuntimeLanguages_GetTranslation3_Prefix(string originalText, int _lineID, int language, ref string __result)
    {
        if (Plugin.Instance == null || !Plugin.Instance.EnabledConfig.Value)
        {
            return true;
        }
        if (language != TranslationManager.RussianLanguageIndex || TranslationManager.RussianLanguageIndex <= 0)
        {
            return true;
        }

        if (TranslationManager.TryTranslate(originalText, _lineID, out string translated))
        {
            __result = translated;
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RuntimeLanguages), nameof(RuntimeLanguages.GetTranslation), typeof(string), typeof(int), typeof(int), typeof(AC_TextType))]
    private static bool RuntimeLanguages_GetTranslation4_Prefix(string originalText, int _lineID, int language, AC_TextType textType, ref string __result)
    {
        if (Plugin.Instance == null || !Plugin.Instance.EnabledConfig.Value)
        {
            return true;
        }
        if (language != TranslationManager.RussianLanguageIndex || TranslationManager.RussianLanguageIndex <= 0)
        {
            return true;
        }

        if ((KickStarter.speechManager == null || KickStarter.speechManager.IsTextTypeTranslatable(textType)) &&
            TranslationManager.TryTranslate(originalText, _lineID, out string translated))
        {
            __result = translated;
            return false;
        }

        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActionVarSet), nameof(ActionVarSet.Run))]
    private static void ActionVarSet_Run_Postfix(ActionVarSet __instance)
    {
        if (!TranslationManager.IsRussianActive)
        {
            return;
        }

        // Global variable ID 0 is SearchWord set from ResearchNew -> Input before CheckThroughAllKeywords runs.
        if (__instance.location == VariableLocation.Global && __instance.variableID == 0)
        {
            GVar searchVar = GlobalVariables.GetVariable(0);
            if (searchVar == null)
            {
                return;
            }

            string current = searchVar.TextValue;
            if (TranslationManager.TryCanonicalizeKeyword(current, TranslationManager.RussianLanguageIndex, out string canonical))
            {
                if (!string.Equals(current, canonical, System.StringComparison.Ordinal))
                {
                    GlobalVariables.SetStringValue(0, canonical);
                    Plugin.Log.LogDebug($"Normalized encyclopedia keyword '{current}' -> '{canonical}'.");
                }
            }
        }
    }
}
