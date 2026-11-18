using System;
using System.Collections.Generic;
using System.Reflection;
using AC;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RTLC.TheChildrenOfClay.RussianTranslate;

/// <summary>
/// Patches TextMeshPro fallbacks and Adventure Creator MenuElement fonts so all Russian Cyrillic glyphs render cleanly,
/// and translates unlinked static UI labels inside Unity UI canvases.
/// </summary>
internal static class FontPatcher
{
    private static Font _typewriterFont;

    private static readonly FieldInfo PlayerMenusListField = AccessTools.Field(typeof(PlayerMenus), "menus");
    private static readonly HashSet<int> PatchedFontAssetIds = new();
    private static readonly Dictionary<int, Font> OriginalMenuElementFonts = new();
    private static readonly Dictionary<int, string> OriginalTmpTexts = new();
    private static readonly Dictionary<int, string> OriginalUiTexts = new();
    private static readonly HashSet<int> TranslatedComponentIds = new();

    public static List<Menu> GetInitializedMenus()
    {
        if (KickStarter.playerMenus == null)
        {
            return null;
        }
        if (PlayerMenusListField?.GetValue(KickStarter.playerMenus) is not List<Menu> baseMenus || baseMenus.Count == 0)
        {
            return null;
        }
        return PlayerMenus.GetMenus(includeDuplicatesAndCustom: true);
    }

    public static void EnsureFontsReady()
    {
        if (_typewriterFont == null)
        {
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (string.Equals(f.name, "JMH Typewriter", StringComparison.Ordinal))
                {
                    _typewriterFont = f;
                    break;
                }
            }
        }

        TMP_FontAsset jmhDynamic = null;
        TMP_FontAsset notoSerifDynamic = null;
        var staticFonts = new List<TMP_FontAsset>();

        foreach (TMP_FontAsset asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (asset == null)
            {
                continue;
            }
            if (string.Equals(asset.name, "JMH Typewriter SDF", StringComparison.Ordinal))
            {
                jmhDynamic = asset;
            }
            else if (string.Equals(asset.name, "NotoSerifJP-Regular SDF", StringComparison.Ordinal))
            {
                notoSerifDynamic = asset;
            }
            else if ((string.Equals(asset.name, "HTOWERT SDF", StringComparison.Ordinal) ||
                      string.Equals(asset.name, "JMH Typewriter", StringComparison.Ordinal)) &&
                     !PatchedFontAssetIds.Contains(asset.GetInstanceID()))
            {
                staticFonts.Add(asset);
            }
        }

        if (staticFonts.Count > 0 && (jmhDynamic != null || notoSerifDynamic != null))
        {
            foreach (TMP_FontAsset staticFont in staticFonts)
            {
                staticFont.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
                bool isSerif = staticFont.name.StartsWith("HTOWERT", StringComparison.Ordinal);
                TMP_FontAsset primaryFallback = isSerif ? (notoSerifDynamic ?? jmhDynamic) : (jmhDynamic ?? notoSerifDynamic);
                TMP_FontAsset secondaryFallback = isSerif ? jmhDynamic : notoSerifDynamic;

                if (primaryFallback != null && !staticFont.fallbackFontAssetTable.Contains(primaryFallback))
                {
                    staticFont.fallbackFontAssetTable.Add(primaryFallback);
                }
                if (secondaryFallback != null && !staticFont.fallbackFontAssetTable.Contains(secondaryFallback))
                {
                    staticFont.fallbackFontAssetTable.Add(secondaryFallback);
                }

                PatchedFontAssetIds.Add(staticFont.GetInstanceID());
                Plugin.Log.LogInfo($"Patched TMP_FontAsset fallback on '{staticFont.name}' (primary='{primaryFallback?.name}').");
            }
        }
    }

    public static void ApplyForCurrentLanguage()
    {
        EnsureFontsReady();

        bool russian = TranslationManager.IsRussianActive;
        List<Menu> menus = GetInitializedMenus();
        if (menus == null)
        {
            return;
        }

        foreach (Menu menu in menus)
        {
            ApplyToMenu(menu, russian);
        }
    }

    public static void ApplyToMenu(Menu menu, bool russian)
    {
        if (menu == null)
        {
            return;
        }

        EnsureFontsReady();

        bool changedFont = false;
        if (menu.elements != null)
        {
            foreach (MenuElement element in menu.elements)
            {
                if (element == null)
                {
                    continue;
                }

                int eid = element.GetInstanceID();
                if (!OriginalMenuElementFonts.ContainsKey(eid))
                {
                    OriginalMenuElementFonts[eid] = element.font;
                }

                Font orig = OriginalMenuElementFonts[eid];
                if (russian && _typewriterFont != null && orig != null &&
                    (orig.name == "HTOWERT" || orig.name == "Chunkfive" || orig.name == "PerfectDOSVGA437"))
                {
                    if (element.font != _typewriterFont)
                    {
                        element.font = _typewriterFont;
                        changedFont = true;
                    }
                }
                else if (element.font != orig)
                {
                    element.font = orig;
                    changedFont = true;
                }
            }
        }

        if (menu.RuntimeCanvas != null)
        {
            ApplyCanvasTextFallbacks(menu.RuntimeCanvas.gameObject, russian);
        }

        if (changedFont)
        {
            menu.Recalculate();
        }
    }

    private static void ApplyCanvasTextFallbacks(GameObject canvasGo, bool russian)
    {
        if (canvasGo == null)
        {
            return;
        }

        foreach (TMP_Text tmp in canvasGo.GetComponentsInChildren<TMP_Text>(includeInactive: true))
        {
            if (tmp != null && !ShouldSkipComponent(tmp.gameObject))
            {
                ApplyFallbackToComponent(tmp.GetInstanceID(), () => tmp.text, v => tmp.text = v, OriginalTmpTexts, russian);
            }
        }

        foreach (Text uiText in canvasGo.GetComponentsInChildren<Text>(includeInactive: true))
        {
            if (uiText != null && !ShouldSkipComponent(uiText.gameObject))
            {
                ApplyFallbackToComponent(uiText.GetInstanceID(), () => uiText.text, v => uiText.text = v, OriginalUiTexts, russian);
            }
        }
    }

    private static void ApplyFallbackToComponent(
        int instanceId,
        Func<string> getter,
        Action<string> setter,
        Dictionary<int, string> cache,
        bool russian)
    {
        string current = getter() ?? string.Empty;
        if (!cache.TryGetValue(instanceId, out string orig))
        {
            if (ShouldIgnoreTextValue(current) || !TranslationManager.TryTranslateUnlinkedText(current, out _))
            {
                return;
            }
            orig = current;
            cache[instanceId] = orig;
        }

        if (russian && TranslationManager.TryTranslateUnlinkedText(orig, out string ru))
        {
            if (!string.Equals(current, ru, StringComparison.Ordinal))
            {
                setter(ru);
            }
            TranslatedComponentIds.Add(instanceId);
        }
        else if (!russian && TranslatedComponentIds.Remove(instanceId) && !string.Equals(current, orig, StringComparison.Ordinal))
        {
            setter(orig);
        }
    }

    private static bool ShouldSkipComponent(GameObject go)
    {
        if (go.GetComponentInParent<TMP_InputField>(includeInactive: true) != null)
        {
            return true;
        }
        return false;
    }

    private static bool ShouldIgnoreTextValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        if (text.Contains("[var:", StringComparison.Ordinal) ||
            text.StartsWith("Language", StringComparison.Ordinal) ||
            text.StartsWith("Язык", StringComparison.Ordinal) ||
            text.EndsWith("fps", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }
}
