using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using AC;
using HarmonyLib;

namespace RTLC.TheChildrenOfClay.RussianTranslate;

/// <summary>
/// Manages loading ru.json, registering the Russian language in Adventure Creator,
/// translating lines by ID/fallback text, and resolving Cyrillic/Latin encyclopedia keywords.
/// </summary>
internal static class TranslationManager
{
    public const string DefaultRussianLanguageName = "Русский";

    private static readonly Dictionary<int, string> LinesById = new();
    private static readonly Dictionary<string, string> LinesByExactText = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> LinesByNormalizedText = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> KeywordLookup = new(StringComparer.Ordinal);

    private static readonly FieldInfo SpeechLinesDictField = AccessTools.Field(typeof(RuntimeLanguages), "speechLinesDictionary");
    private static readonly MethodInfo SpeechLinesDictGetter = AccessTools.PropertyGetter(typeof(RuntimeLanguages), "SpeechLinesDictionary");
    private static bool _loggedInjection;

    public static string RussianLanguageName { get; private set; } = DefaultRussianLanguageName;
    public static int RussianLanguageIndex { get; private set; } = -1;
    public static int IdLineCount => LinesById.Count;
    public static int TextFallbackCount => LinesByExactText.Count;
    public static int KeywordAliasCount => KeywordLookup.Count;

    public static bool IsRussianActive =>
        Plugin.Instance != null &&
        Plugin.Instance.EnabledConfig.Value &&
        RussianLanguageIndex > 0 &&
        Options.GetLanguage() == RussianLanguageIndex;

    public static void Load(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            LinesById.Clear();
            LinesByExactText.Clear();
            LinesByNormalizedText.Clear();
            KeywordLookup.Clear();
            Plugin.Log.LogError($"Translation file not found: {jsonPath}");
            return;
        }

        LoadFromJson(File.ReadAllText(jsonPath, Encoding.UTF8), jsonPath);
    }

    public static void LoadFromJson(string rawJson, string sourceName = "rtlc.bundle:ru.json")
    {
        LinesById.Clear();
        LinesByExactText.Clear();
        LinesByNormalizedText.Clear();
        KeywordLookup.Clear();

        if (string.IsNullOrEmpty(rawJson))
        {
            Plugin.Log.LogError($"Translation JSON is empty ({sourceName}).");
            return;
        }

        if (SimpleJson.Parse(rawJson.TrimStart('\uFEFF')) is not Dictionary<string, object> root)
        {
            Plugin.Log.LogError($"Failed to parse translation JSON root object from {sourceName}.");
            return;
        }

        if (root.TryGetValue("languageName", out object langObj) && langObj is string langStr && !string.IsNullOrWhiteSpace(langStr))
        {
            RussianLanguageName = langStr;
        }

        if (root.TryGetValue("linesById", out object idMapObj) && idMapObj is Dictionary<string, object> idMap)
        {
            foreach (var kv in idMap)
            {
                if (int.TryParse(kv.Key, out int lineId) && kv.Value is string text)
                {
                    LinesById[lineId] = text;
                }
            }
        }

        if (root.TryGetValue("linesByText", out object textMapObj) && textMapObj is Dictionary<string, object> textMap)
        {
            foreach (var kv in textMap)
            {
                if (kv.Value is string translated)
                {
                    LinesByExactText[kv.Key] = translated;
                    string norm = NormalizeText(kv.Key);
                    if (!string.IsNullOrEmpty(norm))
                    {
                        LinesByNormalizedText[norm] = translated;
                    }
                }
            }
        }

        if (root.TryGetValue("keywordAliases", out object kwObj) && kwObj is Dictionary<string, object> kwMap)
        {
            foreach (var kv in kwMap)
            {
                if (!int.TryParse(kv.Key, out int varId) || kv.Value is not List<object> aliases)
                {
                    continue;
                }
                foreach (object item in aliases)
                {
                    if (item is string alias)
                    {
                        string norm = NormalizeKeyword(alias);
                        if (!string.IsNullOrEmpty(norm))
                        {
                            KeywordLookup[norm] = varId;
                        }
                    }
                }
            }
        }

        Plugin.Log.LogInfo($"Loaded {sourceName}: {LinesById.Count} ID lines, {LinesByExactText.Count} text fallbacks, {KeywordLookup.Count} keyword aliases.");
    }

    public static void InjectRussianLanguage()
    {
        if (Plugin.Instance != null && !Plugin.Instance.EnabledConfig.Value)
        {
            return;
        }

        SpeechManager speechManager = KickStarter.speechManager;
        RuntimeLanguages runtimeLanguages = KickStarter.runtimeLanguages;

        if (speechManager != null)
        {
            RussianLanguageIndex = EnsureLanguageEntry(speechManager.Languages);
            if (speechManager.lines != null)
            {
                foreach (SpeechLine line in speechManager.lines)
                {
                    ApplyLineTranslation(line, RussianLanguageIndex);
                }
            }
        }

        if (runtimeLanguages != null)
        {
            int rtIndex = EnsureLanguageEntry(runtimeLanguages.Languages);
            if (rtIndex > 0)
            {
                RussianLanguageIndex = rtIndex;
            }

            if (RussianLanguageIndex > 0)
            {
                var dict = SpeechLinesDictGetter?.Invoke(runtimeLanguages, null) as Dictionary<int, SpeechLine>
                           ?? SpeechLinesDictField?.GetValue(runtimeLanguages) as Dictionary<int, SpeechLine>;

                if (dict != null)
                {
                    int populated = 0;
                    int missingCount = 0;
                    foreach (var kv in dict)
                    {
                        if (ApplyLineTranslation(kv.Value, RussianLanguageIndex))
                        {
                            populated++;
                        }
                        else
                        {
                            missingCount++;
                        }
                    }

                    if (!_loggedInjection)
                    {
                        _loggedInjection = true;
                        Plugin.Log.LogInfo($"Registered '{RussianLanguageName}' at language index {RussianLanguageIndex} ({populated}/{dict.Count} lines populated, missing={missingCount}).");
                    }
                }
            }
        }

        RefreshGlobalVarTranslations();
    }

    public static void RefreshGlobalVarTranslations()
    {
        if (KickStarter.variablesManager?.vars != null)
        {
            foreach (GVar v in KickStarter.variablesManager.vars)
            {
                v?.CreateRuntimeTranslations();
            }
        }
        if (KickStarter.runtimeVariables?.globalVars != null)
        {
            foreach (GVar v in KickStarter.runtimeVariables.globalVars)
            {
                v?.CreateRuntimeTranslations();
            }
        }
    }

    public static bool TryTranslate(string originalText, int lineId, out string translated)
    {
        if (lineId >= 0 && LinesById.TryGetValue(lineId, out translated))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(originalText))
        {
            if (LinesByExactText.TryGetValue(originalText, out translated))
            {
                return true;
            }

            string norm = NormalizeText(originalText);
            if (!string.IsNullOrEmpty(norm) && LinesByNormalizedText.TryGetValue(norm, out translated))
            {
                return true;
            }
        }

        translated = null;
        return false;
    }

    public static bool TryTranslateUnlinkedText(string originalText, out string translated)
    {
        return TryTranslate(originalText, -1, out translated);
    }

    public static bool TryCanonicalizeKeyword(string rawInput, int languageIndex, out string canonicalValue)
    {
        canonicalValue = null;
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return false;
        }

        string norm = NormalizeKeyword(rawInput);
        if (string.IsNullOrEmpty(norm) || !KeywordLookup.TryGetValue(norm, out int targetVarId))
        {
            return false;
        }

        GVar targetVar = GlobalVariables.GetVariable(targetVarId);
        if (targetVar == null)
        {
            return false;
        }

        // ActionVarCheck compares SearchWord against _compareVar.GetValue(Options.GetLanguage()).
        canonicalValue = targetVar.GetValue(languageIndex);
        return !string.IsNullOrEmpty(canonicalValue);
    }

    private static int EnsureLanguageEntry(List<Language> languages)
    {
        if (languages == null)
        {
            return -1;
        }

        for (int i = 0; i < languages.Count; i++)
        {
            if (string.Equals(languages[i].name, RussianLanguageName, StringComparison.Ordinal))
            {
                languages[i].isDisabled = false;
                return i;
            }
        }

        languages.Add(new Language(RussianLanguageName, _isRightToLeft: false));
        return languages.Count - 1;
    }

    private static bool ApplyLineTranslation(SpeechLine line, int languageIndex)
    {
        if (line == null || languageIndex <= 0)
        {
            return false;
        }

        line.translationText ??= new List<string>();
        int targetSlot = languageIndex - 1;
        while (line.translationText.Count <= targetSlot)
        {
            line.translationText.Add(string.Empty);
        }

        if (TryTranslate(line.text, line.lineID, out string ru))
        {
            line.translationText[targetSlot] = ru;
            return true;
        }

        return false;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        bool prevSpace = false;
        foreach (char rawChar in text.Replace("\r\n", "\n").Replace('\r', '\n').Trim())
        {
            if (rawChar == '\n')
            {
                if (prevSpace && sb.Length > 0 && sb[sb.Length - 1] == ' ')
                {
                    sb.Length--;
                }
                sb.Append('\n');
                prevSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(rawChar))
            {
                if (!prevSpace && sb.Length > 0 && sb[sb.Length - 1] != '\n')
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }

            sb.Append(rawChar);
            prevSpace = false;
        }

        return sb.ToString();
    }

    private static string NormalizeKeyword(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        foreach (char c in input.Trim().ToLowerInvariant())
        {
            if (c == 'ё')
            {
                sb.Append('е');
            }
            else if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
