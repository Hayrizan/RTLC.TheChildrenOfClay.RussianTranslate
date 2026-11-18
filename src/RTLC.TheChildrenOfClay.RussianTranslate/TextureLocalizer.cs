using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AC;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTLC.TheChildrenOfClay.RussianTranslate;

/// <summary>
/// Loads localized PNG textures and swaps SpriteRenderers, UI Images, AC MenuGraphics, and AC Document pages
/// using FullRect sprites so Tight-mesh English sprites never clip Russian text.
/// </summary>
internal static class TextureLocalizer
{
    private static readonly Dictionary<string, Texture2D> RuTexturesByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Sprite> RuSpritesByOrigId = new();

    private static readonly Dictionary<int, Sprite> OriginalRendererSprites = new();
    private static readonly Dictionary<int, Sprite> OriginalImageSprites = new();
    private static readonly Dictionary<int, Texture> OriginalMenuGraphicTextures = new();
    private static readonly Dictionary<JournalPage, Texture2D> OriginalPageTextures = new();
    private static readonly Dictionary<Document, Texture2D> OriginalDocumentTextures = new();

    private static readonly FieldInfo CollectedDocsField = AccessTools.Field(typeof(RuntimeDocuments), "collectedDocuments");

    public static int LoadedTextureCount => RuTexturesByName.Count;

    public static void LoadFromDirectory(string texturesDir)
    {
        RuTexturesByName.Clear();
        RuSpritesByOrigId.Clear();

        if (!Directory.Exists(texturesDir))
        {
            Plugin.Log.LogWarning($"Textures directory not found: {texturesDir}");
            return;
        }

        foreach (string file in Directory.GetFiles(texturesDir, "*.png", SearchOption.TopDirectoryOnly))
        {
            string baseName = Path.GetFileNameWithoutExtension(file);
            byte[] bytes = File.ReadAllBytes(file);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = baseName + "_RU",
                hideFlags = HideFlags.DontSave
            };

            if (ImageConversion.LoadImage(tex, bytes, markNonReadable: false))
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                RuTexturesByName[baseName] = tex;
            }
            else
            {
                UnityEngine.Object.Destroy(tex);
                Plugin.Log.LogWarning($"Failed to decode localized PNG texture: {file}");
            }
        }

        Plugin.Log.LogInfo($"Loaded {RuTexturesByName.Count} localized texture(s) from directory.");
    }

    public static void LoadFromBundle(AssetBundle bundle)
    {
        RuTexturesByName.Clear();
        RuSpritesByOrigId.Clear();

        if (bundle == null)
        {
            Plugin.Log.LogError("Cannot load localized textures: AssetBundle is null.");
            return;
        }

        Texture2D[] textures = bundle.LoadAllAssets<Texture2D>();
        if (textures != null)
        {
            foreach (Texture2D tex in textures)
            {
                if (tex == null || string.IsNullOrEmpty(tex.name))
                {
                    continue;
                }

                string baseName = tex.name.EndsWith("_RU", StringComparison.OrdinalIgnoreCase)
                    ? tex.name.Substring(0, tex.name.Length - 3)
                    : tex.name;

                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.hideFlags = HideFlags.DontSave;
                RuTexturesByName[baseName] = tex;
            }
        }

        Plugin.Log.LogInfo($"Loaded {RuTexturesByName.Count} localized texture(s) from AssetBundle.");
    }

    public static void ApplyForCurrentLanguage()
    {
        if (RuTexturesByName.Count == 0)
        {
            return;
        }

        bool applyRussian =
            TranslationManager.IsRussianActive &&
            Plugin.Instance != null &&
            Plugin.Instance.ReplaceTexturesConfig.Value;

        int srCount = ApplyToSpriteRenderers(applyRussian);
        int imgCount = ApplyToUiImages(applyRussian);
        int mgCount = ApplyToMenuGraphics(applyRussian);
        int docCount = ApplyToDocuments(applyRussian);

        if (srCount + imgCount + mgCount + docCount > 0)
        {
            Plugin.Log.LogInfo(
                $"TextureLocalizer applied (russian={applyRussian}): SpriteRenderers={srCount}, UI.Images={imgCount}, MenuGraphics={mgCount}, DocumentTextures={docCount}.");
        }
    }

    private static int ApplyToSpriteRenderers(bool applyRussian) =>
        ApplyToSpriteComponents<SpriteRenderer>(
            OriginalRendererSprites,
            sr => sr.sprite,
            (sr, sp) => sr.sprite = sp,
            applyRussian);

    private static int ApplyToUiImages(bool applyRussian) =>
        ApplyToSpriteComponents<Image>(
            OriginalImageSprites,
            img => img.sprite,
            (img, sp) => img.sprite = sp,
            applyRussian);

    private static int ApplyToSpriteComponents<T>(
        Dictionary<int, Sprite> originalCache,
        Func<T, Sprite> getter,
        Action<T, Sprite> setter,
        bool applyRussian) where T : Component
    {
        int count = 0;
        foreach (T comp in Resources.FindObjectsOfTypeAll<T>())
        {
            if (comp == null || !comp.gameObject.scene.IsValid())
            {
                continue;
            }

            int cid = comp.GetInstanceID();
            if (!originalCache.TryGetValue(cid, out Sprite origSprite))
            {
                Sprite current = getter(comp);
                if (current == null || current.texture == null)
                {
                    continue;
                }
                origSprite = current;
                originalCache[cid] = origSprite;
            }

            if (origSprite == null || origSprite.texture == null ||
                !TryGetRussianTexture(origSprite.texture.name, out Texture2D ruTex))
            {
                continue;
            }

            Sprite desired = applyRussian ? GetOrCreateFullRectSprite(origSprite, ruTex) : origSprite;
            if (getter(comp) != desired)
            {
                setter(comp, desired);
                count++;
            }
        }
        return count;
    }

    private static int ApplyToMenuGraphics(bool applyRussian)
    {
        List<Menu> menus = FontPatcher.GetInitializedMenus();
        if (menus == null)
        {
            return 0;
        }

        int count = 0;
        foreach (Menu menu in menus)
        {
            if (menu?.elements == null)
            {
                continue;
            }

            bool menuChanged = false;
            foreach (MenuElement elem in menu.elements)
            {
                if (elem is not MenuGraphic mg || mg.graphic == null)
                {
                    continue;
                }

                int mgId = mg.GetInstanceID();
                if (!OriginalMenuGraphicTextures.TryGetValue(mgId, out Texture origTex))
                {
                    if (mg.graphic.texture == null)
                    {
                        continue;
                    }
                    origTex = mg.graphic.texture;
                    OriginalMenuGraphicTextures[mgId] = origTex;
                }

                if (origTex == null || !TryGetRussianTexture(origTex.name, out Texture2D ruTex))
                {
                    continue;
                }

                Texture desired = applyRussian ? ruTex : origTex;
                if (mg.graphic.texture != desired)
                {
                    mg.graphic.ReplaceTexture(desired);
                    menuChanged = true;
                    count++;
                }
            }

            if (menuChanged)
            {
                menu.Recalculate();
            }
        }
        return count;
    }

    private static int ApplyToDocuments(bool applyRussian)
    {
        int count = 0;
        var seenDocs = new HashSet<Document>();

        void ProcessDoc(Document doc)
        {
            if (doc == null || !seenDocs.Add(doc))
            {
                return;
            }

            if (!OriginalDocumentTextures.TryGetValue(doc, out Texture2D origDocTex))
            {
                origDocTex = doc.texture;
                OriginalDocumentTextures[doc] = origDocTex;
            }
            if (origDocTex != null && TryGetRussianTexture(origDocTex.name, out Texture2D ruDocTex))
            {
                Texture2D desired = applyRussian ? ruDocTex : origDocTex;
                if (doc.texture != desired)
                {
                    doc.texture = desired;
                    count++;
                }
            }

            if (doc.pages == null)
            {
                return;
            }

            foreach (JournalPage page in doc.pages)
            {
                if (page == null)
                {
                    continue;
                }

                if (!OriginalPageTextures.TryGetValue(page, out Texture2D origPageTex))
                {
                    origPageTex = page.texture;
                    OriginalPageTextures[page] = origPageTex;
                }

                if (origPageTex != null && TryGetRussianTexture(origPageTex.name, out Texture2D ruPageTex))
                {
                    Texture2D desired = applyRussian ? ruPageTex : origPageTex;
                    if (page.texture != desired)
                    {
                        page.texture = desired;
                        count++;
                    }
                }
            }
        }

        if (KickStarter.inventoryManager?.documents != null)
        {
            foreach (Document doc in KickStarter.inventoryManager.documents)
            {
                ProcessDoc(doc);
            }
        }

        RuntimeDocuments runtimeDocs = KickStarter.runtimeDocuments;
        if (runtimeDocs != null)
        {
            ProcessDoc(runtimeDocs.ActiveDocument);
            if (CollectedDocsField?.GetValue(runtimeDocs) is List<DocumentInstance> collected)
            {
                foreach (DocumentInstance inst in collected)
                {
                    ProcessDoc(inst?.Document);
                }
            }
        }

        return count;
    }

    private static bool TryGetRussianTexture(string origName, out Texture2D ruTex)
    {
        ruTex = null;
        if (string.IsNullOrEmpty(origName))
        {
            return false;
        }
        return RuTexturesByName.TryGetValue(origName, out ruTex);
    }

    private static Sprite GetOrCreateFullRectSprite(Sprite origSprite, Texture2D ruTex)
    {
        int oid = origSprite.GetInstanceID();
        if (RuSpritesByOrigId.TryGetValue(oid, out Sprite cached) && cached != null)
        {
            return cached;
        }

        Rect rect = new Rect(0f, 0f, ruTex.width, ruTex.height);
        if (origSprite.rect.width <= ruTex.width && origSprite.rect.height <= ruTex.height)
        {
            rect = origSprite.rect;
        }

        Sprite created = Sprite.Create(
            ruTex,
            rect,
            new Vector2(0.5f, 0.5f),
            origSprite.pixelsPerUnit > 0f ? origSprite.pixelsPerUnit : 100f,
            0u,
            SpriteMeshType.FullRect);
        created.name = origSprite.name + "_RU";
        created.hideFlags = HideFlags.DontSave;
        RuSpritesByOrigId[oid] = created;
        return created;
    }
}
