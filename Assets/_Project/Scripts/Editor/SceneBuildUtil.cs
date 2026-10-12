#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

namespace IDS.EditorTools
{
    /// <summary>
    /// Primitives, materials, text and buttons for the scene builder.
    ///
    /// Everything the builder puts in the scene goes through here, so geometry and
    /// styling are defined once. Materials are created as real assets under
    /// Assets/_Project/Materials/Generated rather than as runtime instances: an
    /// unsaved material referenced by a scene is serialised into the scene file,
    /// which works but leaves materials that cannot be inspected, reused or edited
    /// by anyone else on the team.
    /// </summary>
    public static class SceneBuildUtil
    {
        public const string MaterialFolder = "Assets/_Project/Materials/Generated";
        public const string TextureFolder = "Assets/_Project/Materials/Generated/Textures";

        // ---- hierarchy -------------------------------------------------------

        public static GameObject Go(string name, Transform parent = null,
                                    Vector3 localPosition = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go;
        }

        /// <summary>
        /// A GameObject created WITH a RectTransform, for UI.
        ///
        /// Not Go() followed by AddComponent&lt;RectTransform&gt;(): adding a
        /// RectTransform to an object that already has a plain Transform is a
        /// conversion Unity performs rather than a component it adds, and relying
        /// on it is unnecessary when the constructor takes the type directly.
        /// </summary>
        public static GameObject UiGo(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;

            return go;
        }

        public static GameObject Prim(PrimitiveType type, string name, Transform parent,
                                      Vector3 localPosition, Vector3 localScale,
                                      Material material, bool collider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            if (!collider)
            {
                Collider c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }

            if (material != null)
            {
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = material;
            }

            return go;
        }

        public static GameObject Box(string name, Transform parent, Vector3 position,
                                     Vector3 size, Material material, bool collider = false)
            => Prim(PrimitiveType.Cube, name, parent, position, size, material, collider);

        public static GameObject Cyl(string name, Transform parent, Vector3 position,
                                     Vector3 size, Material material, bool collider = false)
            => Prim(PrimitiveType.Cylinder, name, parent, position, size, material, collider);

        // ---- materials -------------------------------------------------------

        private static readonly Dictionary<string, Material> Cache = new();

        /// <summary>
        /// A URP Lit material saved as an asset. Reused by name, so calling this
        /// for every verge post creates one material, not seventy.
        /// </summary>
        public static Material Mat(string name, Color colour, float smoothness = 0.2f,
                                   float metallic = 0f, bool emissive = false)
        {
            if (Cache.TryGetValue(name, out Material cached) && cached != null) return cached;

            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                Configure(existing, colour, smoothness, metallic, emissive);
                EditorUtility.SetDirty(existing);
                Cache[name] = existing;
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[Build] URP Lit shader not found — falling back to " +
                                 "the default material. Is the project on URP?");
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader) { name = name };
            Configure(material, colour, smoothness, metallic, emissive);

            AssetDatabase.CreateAsset(material, path);
            Cache[name] = material;
            return material;
        }

        private static void Configure(Material m, Color colour, float smoothness,
                                      float metallic, bool emissive)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);

            if (!emissive) return;

            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", colour);
        }

        /// <summary>
        /// A material whose base map tiles a generated dashed-line texture. One
        /// long quad with this on it replaces two hundred separate dash objects.
        /// </summary>
        public static Material DashedLineMat(string name, float tiling)
        {
            Material m = Mat(name, Color.white, 0.05f);

            Texture2D tex = DashTexture();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.mainTextureScale = new Vector2(1f, tiling);
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(1f, tiling));

            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 8×32 texture: a white dash over the middle half, dark asphalt either
        /// end. Tiled along the road it reads as a dashed centre line.
        /// </summary>
        private static Texture2D DashTexture()
        {
            EnsureFolder(TextureFolder);
            string path = $"{TextureFolder}/LaneDash.png";

            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int w = 8, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color road = new Color(0.17f, 0.17f, 0.18f);
            Color paint = new Color(0.92f, 0.92f, 0.88f);

            for (int y = 0; y < h; y++)
            {
                bool painted = y >= h / 4 && y < h * 3 / 4;
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, painted ? paint : road);
            }

            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.wrapModeV = TextureWrapMode.Repeat;
                importer.wrapModeU = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string running = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{running}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(running, parts[i]);
                running = next;
            }
        }

        // ---- built-in UI sprites ---------------------------------------------

        /// <summary>
        /// Unity's own rounded-rectangle UI sprite. Using the built-in resource
        /// means buttons and panels look like buttons and panels with no imported
        /// art and nothing to go missing.
        /// </summary>
        public static Sprite UISprite()
            => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        /// <summary>Unity's round knob sprite, used as the dwell reticle because a
        /// radial Image fill needs a sprite to fill.</summary>
        public static Sprite KnobSprite()
            => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        // ---- UI --------------------------------------------------------------

        /// <summary>
        /// A world-space canvas.
        ///
        /// `interactive` controls whether it gets a GraphicRaycaster. Only
        /// canvases with buttons should have one: VRUIPointer treats a canvas
        /// with a raycaster as something a ray can land on, so a decorative
        /// canvas with one — a HUD, an alert banner, the gaze reticle itself —
        /// sits in front of the menu and swallows the ray.
        /// </summary>
        public static Canvas WorldCanvas(string name, Transform parent, Vector3 localPosition,
                                         Quaternion localRotation, Vector2 pixelSize,
                                         float metresPerPixel, Camera worldCamera,
                                         bool interactive = true)
        {
            GameObject go = Go(name, parent, localPosition);
            go.transform.localRotation = localRotation;
            go.layer = LayerMask.NameToLayer("UI");

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Without this the GraphicRaycaster has nothing to project a world ray
            // with and silently misses every button. This was the actual cause of
            // "none of the panels have buttons".
            canvas.worldCamera = worldCamera;

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = pixelSize;
            rect.localScale = Vector3.one * metresPerPixel;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            scaler.referencePixelsPerUnit = 100f;

            if (interactive) go.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        /// <summary>A full-canvas panel with a translucent dark background.</summary>
        public static GameObject Panel(string name, Transform parent, Color background)
        {
            GameObject go = UiGo(name, parent);
            Stretch((RectTransform)go.transform);

            var image = go.AddComponent<Image>();
            image.sprite = UISprite();
            image.type = Image.Type.Sliced;
            image.color = background;

            // A panel background must not swallow rays aimed at its own buttons.
            // Buttons are children, so they are hit first by the raycaster's
            // depth sort — but a panel that is raycastTarget blocks the gaze ray
            // from reaching a button on a panel behind it, which is desired, so
            // this stays true.
            image.raycastTarget = true;

            return go;
        }

        public static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.localScale = Vector3.one;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string content,
                                           float fontSize, Color colour,
                                           TextAlignmentOptions alignment,
                                           Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = UiGo(name, parent);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = colour;
            text.alignment = alignment;
            text.raycastTarget = false;   // text must never block its own button

            // Word wrapping is on by default. It is not set explicitly because
            // the property that controls it was renamed in TextMeshPro and the
            // old name is deprecated — naming either one ties this file to a
            // particular TMP version for no gain.

            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            return text;
        }

        /// <summary>
        /// A button with a background, a label and a wired click handler.
        ///
        /// The click is added with UnityEventTools so it is a SERIALISED
        /// persistent listener, visible in the Inspector and saved with the
        /// scene — not a runtime AddListener that exists only while the builder
        /// is running and is gone by the time the scene is loaded.
        /// </summary>
        public static Button Btn(string name, Transform parent, string label,
                                 Vector2 anchoredPosition, Vector2 size,
                                 UnityAction action, Color colour, float fontSize = 34f,
                                 bool boundAtRuntime = false)
        {
            GameObject go = UiGo(name, parent);

            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.sprite = UISprite();
            image.type = Image.Type.Sliced;
            image.color = colour;

            // A button whose image is not a raycast target cannot be hit. This
            // was wrong in an earlier build and every button was unpressable.
            image.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colours.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colours.selectedColor = Color.white;
            colours.fadeDuration = 0.06f;
            button.colors = colours;

            Text($"{name}_Label", go.transform, label, fontSize, Color.white,
                 TextAlignmentOptions.Center, Vector2.zero, size);

            if (action != null)
                UnityEventTools.AddPersistentListener(button.onClick, action);
            else if (!boundAtRuntime)
                Debug.LogWarning($"[Build] button '{name}' has no action and will " +
                                 "do nothing when pressed.");

            return button;
        }

        // ---- serialized-field wiring -----------------------------------------

        /// <summary>
        /// Sets a private [SerializeField] field by name.
        ///
        /// Used where a component has no typed Configure method. It goes through
        /// SerializedObject rather than reflection so the value is written the way
        /// the Inspector writes it, which means it is correctly serialised into
        /// the scene and shows up in the Inspector afterwards.
        ///
        /// Returns false and logs if the field does not exist — a silent no-op
        /// here would leave a null reference that only shows up on the headset.
        /// </summary>
        /// <summary>
        /// A TextMeshPro label that lives in the world rather than on a canvas.
        /// Roadside signs need text without dragging a world-space Canvas and a
        /// camera reference into the environment build.
        /// </summary>
        public static TextMeshPro WorldText(string name, Transform parent,
                                            Vector3 localPosition, string content,
                                            float size, Color colour,
                                            Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = box;

            var text = go.AddComponent<TextMeshPro>();
            text.text = content;
            text.fontSize = size;
            text.color = colour;
            text.alignment = TextAlignmentOptions.Center;

            return text;
        }

        public const string CabinLayer = "CabinInterior";

        /// <summary>
        /// Finds or creates a user layer and returns its index, or -1.
        ///
        /// Needed because the mirror cameras must not render the car they are
        /// bolted to. The centre mirror sat 2 m in front of a solid rear-seat
        /// panel and showed nothing but that panel — the mirror worked
        /// perfectly, it was just pointed at the inside of the cabin.
        /// </summary>
        public static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TagManager.asset");

            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[Build] could not open TagManager.asset to add the "
                               + $"'{name}' layer.");
                return -1;
            }

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            if (layers == null)
            {
                Debug.LogError("[Build] TagManager.asset has no 'layers' property.");
                return -1;
            }

            // 0-7 are Unity's own. 8 upward are free for the project to use.
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();

                Debug.Log($"[Build] created layer '{name}' at index {i}.");
                return i;
            }

            Debug.LogError($"[Build] no free user layer for '{name}'. The mirrors "
                           + "will show the inside of the car.");
            return -1;
        }

        /// <summary>
        /// Puts every renderer under <paramref name="root"/> on a layer, skipping
        /// anything under an exclusion and anything belonging to a Canvas.
        /// </summary>
        public static int SetLayerRecursive(Transform root, int layer,
                                            params Transform[] exclusions)
        {
            if (root == null || layer < 0) return 0;

            int moved = 0;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                bool skip = false;

                foreach (Transform e in exclusions)
                    if (e != null && (r.transform == e || r.transform.IsChildOf(e)))
                    { skip = true; break; }

                if (skip || r.GetComponentInParent<Canvas>() != null) continue;

                r.gameObject.layer = layer;
                moved++;
            }

            return moved;
        }

        public static bool Wire(Object target, string field, object value)
        {
            if (target == null)
            {
                Debug.LogError($"[Build] cannot wire '{field}' — target is null.");
                return false;
            }

            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);

            if (prop == null)
            {
                Debug.LogError($"[Build] {target.GetType().Name} has no serialized " +
                               $"field '{field}'. The scene will be missing a reference.");
                return false;
            }

            switch (value)
            {
                case null: prop.objectReferenceValue = null; break;
                case Object o: prop.objectReferenceValue = o; break;
                case bool b: prop.boolValue = b; break;
                case int i: prop.intValue = i; break;
                case float f: prop.floatValue = f; break;
                case string s: prop.stringValue = s; break;
                case Vector2 v2: prop.vector2Value = v2; break;
                case Vector3 v3: prop.vector3Value = v3; break;
                case Color c: prop.colorValue = c; break;
                case System.Enum e: prop.enumValueIndex = System.Convert.ToInt32(e); break;

                case IEnumerable list:
                    return WireList(target, field, so, prop, list);

                default:
                    Debug.LogError($"[Build] Wire does not handle " +
                                   $"{value.GetType().Name} for field '{field}'.");
                    return false;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// Writes a list of object references into a serialized array, then READS
        /// IT BACK and checks every element actually landed.
        ///
        /// This is not defensive programming for its own sake. The previous
        /// version used ClearArray + InsertArrayElementAtIndex per element and
        /// then applied once at the end. It sized the array correctly and
        /// reported success, but the references serialized as fileID: 0 — the
        /// scene ended up holding three empty slots where the three country
        /// profiles should have been. Nothing caught it, so the first sign of
        /// trouble was a country-selection screen with no countries on it,
        /// discovered in the headset.
        ///
        /// So: set arraySize up front, assign each element, apply, then re-open
        /// the object and verify. If the serialized path has not taken, fall
        /// back to setting the backing field by reflection and marking the
        /// object dirty, which writes through a different mechanism, and verify
        /// again. Only a verified write returns true.
        /// </summary>
        private static bool WireList(Object target, string field,
                                     SerializedObject so, SerializedProperty prop,
                                     IEnumerable list)
        {
            var items = new List<Object>();

            foreach (object item in list)
            {
                if (item is Object io && io != null) { items.Add(io); continue; }

                Debug.LogError($"[Build] element {items.Count} of '{field}' on " +
                               $"{target.GetType().Name} is null or not a " +
                               "UnityEngine.Object. Refusing to write a list with " +
                               "holes in it.");
                return false;
            }

            if (!prop.isArray)
            {
                Debug.LogError($"[Build] '{field}' on {target.GetType().Name} is " +
                               "not a list or array.");
                return false;
            }

            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];

            so.ApplyModifiedPropertiesWithoutUndo();

            if (ListLanded(target, field, items)) return true;

            Debug.LogWarning($"[Build] serialized write of '{field}' on " +
                             $"{target.GetType().Name} did not take — falling back " +
                             "to a direct field write.");

            if (!WriteFieldByReflection(target, field, items)) return false;

            EditorUtility.SetDirty(target);

            if (ListLanded(target, field, items)) return true;

            Debug.LogError($"[Build] '{field}' on {target.GetType().Name} is still " +
                           $"empty after both write paths. {items.Count} reference(s) " +
                           "were lost and the scene is broken.");
            return false;
        }

        /// <summary>Re-opens the object and confirms every element is present.</summary>
        private static bool ListLanded(Object target, string field, List<Object> expected)
        {
            var check = new SerializedObject(target);
            SerializedProperty array = check.FindProperty(field);

            if (array == null || !array.isArray) return false;
            if (array.arraySize != expected.Count) return false;

            for (int i = 0; i < expected.Count; i++)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue != expected[i])
                    return false;

            return true;
        }

        private static bool WriteFieldByReflection(Object target, string field,
                                                   List<Object> items)
        {
            System.Reflection.FieldInfo info = null;

            for (System.Type t = target.GetType(); t != null && info == null; t = t.BaseType)
                info = t.GetField(field,
                                  System.Reflection.BindingFlags.Instance
                                  | System.Reflection.BindingFlags.Public
                                  | System.Reflection.BindingFlags.NonPublic);

            if (info == null)
            {
                Debug.LogError($"[Build] no backing field '{field}' on " +
                               $"{target.GetType().Name} to fall back to.");
                return false;
            }

            System.Type fieldType = info.FieldType;
            System.Type element = fieldType.IsArray
                ? fieldType.GetElementType()
                : fieldType.IsGenericType ? fieldType.GetGenericArguments()[0] : null;

            if (element == null)
            {
                Debug.LogError($"[Build] cannot work out the element type of " +
                               $"'{field}' on {target.GetType().Name}.");
                return false;
            }

            System.Array typed = System.Array.CreateInstance(element, items.Count);
            for (int i = 0; i < items.Count; i++) typed.SetValue(items[i], i);

            if (fieldType.IsArray)
            {
                info.SetValue(target, typed);
                return true;
            }

            object listInstance = System.Activator.CreateInstance(fieldType);
            var add = fieldType.GetMethod("Add");

            if (add == null)
            {
                Debug.LogError($"[Build] '{field}' on {target.GetType().Name} is " +
                               "neither an array nor a List.");
                return false;
            }

            foreach (Object item in items) add.Invoke(listInstance, new object[] { item });
            info.SetValue(target, listInstance);
            return true;
        }
    }
}
#endif
