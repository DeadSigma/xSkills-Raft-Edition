using System;
using System.Reflection;
using HarmonyLib;
using HMLLibrary;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using xSkills.GUI;

using xSkills.Services;
namespace xSkills
{
    internal static class NativeSkillHotkey
    {
        private const string ActionId = "xskills_skilltree";
        private const string PrefKey = "xskills_skilltree_mainkey";
        private const string HarmonyId = "xskills.native-keybinds";
        private static Harmony harmony;
        private static int handledFrame = -100;
        private static bool installed;
        private static float nextMaintenanceAt;

        public static KeyCode CurrentMainKey
        {
            get
            {
                Keybind binding = GetBinding();
                return binding != null ? binding.MainKey : SavedDefault();
            }
        }

        public static void Install()
        {
            if (installed) return;
            installed = true;
            if (!PlayerPrefs.HasKey(PrefKey))
            {
                PlayerPrefs.SetInt(PrefKey, (int)KeyCode.C);
                PlayerPrefs.Save();
            }

            try
            {
                harmony = new Harmony(HarmonyId);
                Patch(typeof(ControlsSettingsBox), "InitializeKeybindInterfaces", nameof(BeforeInitialize));
                Patch(typeof(ControlsSettingsBox), "Load", nameof(BeforeLoad));
                MethodInfo load = AccessTools.Method(typeof(ControlsSettingsBox), "Load", new[] { typeof(RGD_Settings) });
                if (load != null)
                    harmony.Patch(load, postfix: new HarmonyMethod(typeof(NativeSkillHotkey), nameof(AfterLoad)));
                MethodInfo focus = AccessTools.Method(typeof(ControlsSettingsBox), "SetFocus", new[] { typeof(bool) });
                if (focus != null)
                    harmony.Patch(focus, postfix: new HarmonyMethod(typeof(NativeSkillHotkey), nameof(AfterSetFocus)));
                MethodInfo save = AccessTools.Method(typeof(ControlsSettingsBox), "Save", new[] { typeof(RGD_Settings) });
                if (save != null)
                    harmony.Patch(save,
                        prefix: new HarmonyMethod(typeof(NativeSkillHotkey), nameof(BeforeSave)),
                        postfix: new HarmonyMethod(typeof(NativeSkillHotkey), nameof(AfterSave)));
                PatchVanillaInput();
                Maintain();
            }
            catch (Exception)
            {
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
                installed = false;
            }
        }

        public static void Uninstall()
        {
            if (!installed) return;
            installed = false;
            try
            {
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
                foreach (ControlsSettingsBox box in Resources.FindObjectsOfTypeAll<ControlsSettingsBox>())
                {
                    if (box == null || !box.gameObject.scene.IsValid()) continue;
                    KeybindInterface row = FindRow(box);
                    if (row == null) continue;
                    Transform root = row.transform;
                    while (root.parent != null && root.name != "xSkills_SkillTree_Keybind"
                        && root.parent != box.transform)
                        root = root.parent;
                    UnityEngine.Object.Destroy(root.name == "xSkills_SkillTree_Keybind" ? root.gameObject : row.gameObject);
                }
                if (MyInput.Keybinds != null) MyInput.Keybinds.Remove(ActionId);
            }
            catch (Exception) { }
        }

        // Привязка и строка настроек восстанавливаются после пересоздания UI
        public static void Tick()
        {
            if (!installed || Time.unscaledTime < nextMaintenanceAt) return;
            nextMaintenanceAt = Time.unscaledTime + 1.5f;
            Maintain();
        }

        private static void Maintain()
        {
            try
            {
                foreach (ControlsSettingsBox box in Resources.FindObjectsOfTypeAll<ControlsSettingsBox>())
                {
                    if (box == null || !box.gameObject.scene.IsValid()) continue;
                    if (EnsureRow(box))
                    {
                        MethodInfo rebuild = AccessTools.Method(typeof(ControlsSettingsBox), "InitializeKeybindInterfaces");
                        if (rebuild != null) rebuild.Invoke(box, null);
                    }
                    KeybindInterface row = FindRow(box);
                    if (row != null)
                    {
                        PlaceRowAtBottom(box, row);
                        SyncExistingBinding(box);
                        TranslateLabel(row);
                    }
                }
                if (MyInput.Keybinds != null && !MyInput.Keybinds.ContainsKey(ActionId))
                    MyInput.Keybinds[ActionId] = new Keybind(ActionId, SavedDefault());
            }
            catch (Exception) { }
        }

        public static bool WasPressed()
        {
            if (KeybindInterface.AssigningKey) return false;
            Keybind binding = GetBinding();
            KeyCode main = binding == null ? SavedDefault() : binding.MainKey;
            KeyCode alternative = binding == null ? KeyCode.None : binding.AltKey;
            return Pressed(main) || Pressed(alternative);
        }

        public static void MarkHandled()
        {
            handledFrame = Time.frameCount;
        }

        private static bool Pressed(KeyCode key)
        {
            return key != KeyCode.None && Input.GetKeyDown(key);
        }

        private static KeyCode SavedDefault()
        {
            return (KeyCode)PlayerPrefs.GetInt(PrefKey, (int)KeyCode.O);
        }

        private static Keybind GetBinding()
        {
            try
            {
                Keybind value;
                if (MyInput.Keybinds != null && MyInput.Keybinds.TryGetValue(ActionId, out value)) return value;
            }
            catch (Exception) { }
            return null;
        }

        private static void Patch(Type type, string methodName, string prefix)
        {
            MethodInfo target = AccessTools.Method(type, methodName);
            if (target == null) return;
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(NativeSkillHotkey), prefix));
        }

        private static void PatchVanillaInput()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MethodInfo method in typeof(MyInput).GetMethods(flags))
            {
                if (method.IsGenericMethod || method.ReturnType != typeof(bool)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;
                string name = method.Name.ToLowerInvariant();
                if (!name.Contains("down") && !name.Contains("button") && !name.Contains("key")) continue;
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeSkillHotkey), nameof(BeforeVanillaInput)));
                }
                catch (Exception) { }
            }
        }

        private static bool BeforeVanillaInput(string __0, ref bool __result)
        {
            if (string.IsNullOrEmpty(__0) || __0 == ActionId) return true;
            Keybind other;
            try
            {
                if (MyInput.Keybinds == null || !MyInput.Keybinds.TryGetValue(__0, out other) || other == null)
                    return true;
                Keybind own = GetBinding();
                KeyCode main = own != null ? own.MainKey : SavedDefault();
                KeyCode alt = own != null ? own.AltKey : KeyCode.None;
                bool conflict = (other.MainKey != KeyCode.None && (other.MainKey == main || other.MainKey == alt) && Pressed(other.MainKey))
                    || (other.AltKey != KeyCode.None && (other.AltKey == main || other.AltKey == alt) && Pressed(other.AltKey));
                if (!conflict) return true;
                if (Time.frameCount != handledFrame && !GameInputGuard.CanToggle(false)) return true;
                __result = false;
                return false;
            }
            catch (Exception) { return true; }
        }

        private static void BeforeInitialize(ControlsSettingsBox __instance)
        {
            EnsureRow(__instance);
        }

        private static void BeforeLoad(ControlsSettingsBox __instance)
        {
            EnsureRow(__instance);
            KeybindInterface row = FindRow(__instance);
            if (row != null) TranslateLabel(row);
        }

        private static void AfterLoad(ControlsSettingsBox __instance)
        {
            if (EnsureRow(__instance))
            {
                MethodInfo rebuild = AccessTools.Method(typeof(ControlsSettingsBox), "InitializeKeybindInterfaces");
                if (rebuild != null) rebuild.Invoke(__instance, null);
            }
            SyncExistingBinding(__instance);
            KeybindInterface loadedRow = FindRow(__instance);
            if (loadedRow != null)
            {
                PlaceRowAtBottom(__instance, loadedRow);
                TranslateLabel(loadedRow);
            }
        }

        private static void AfterSetFocus(ControlsSettingsBox __instance, bool gamepad)
        {
            if (gamepad) return;
            KeybindInterface row = FindRow(__instance);
            if (row == null) return;
            PlaceRowAtBottom(__instance, row);
            TranslateLabel(row);
        }

        private static void BeforeSave(ControlsSettingsBox __instance)
        {
            if (EnsureRow(__instance))
            {
                MethodInfo rebuild = AccessTools.Method(typeof(ControlsSettingsBox), "InitializeKeybindInterfaces");
                if (rebuild != null) rebuild.Invoke(__instance, null);
            }
        }

        private static void AfterSave(ControlsSettingsBox __instance)
        {
            KeybindInterface row = FindRow(__instance);
            if (row == null || row.Keybind == null) return;
            PlayerPrefs.SetInt(PrefKey, (int)row.Keybind.MainKey);
            PlayerPrefs.Save();
        }

        private static KeybindInterface FindRow(ControlsSettingsBox box)
        {
            if (box == null) return null;
            foreach (KeybindInterface candidate in box.GetComponentsInChildren<KeybindInterface>(true))
                if (candidate != null && candidate.Identifier == ActionId) return candidate;
            return null;
        }

        private static bool EnsureRow(ControlsSettingsBox box)
        {
            if (box == null || FindRow(box) != null) return false;
            try
            {
                KeybindInterface[] originals = box.GetComponentsInChildren<KeybindInterface>(true);
                if (originals.Length == 0)
                {
                    return false;
                }

                // Выбирается обычная строка с подписью, не скрытый UnbindableKeybinds
                KeybindInterface template = null;
                foreach (KeybindInterface candidate in originals)
                {
                    if (candidate == null || candidate.Identifier == ActionId) continue;
                    if (candidate.Identifier == "GameInfo" &&
                        candidate.GetComponentsInChildren<Text>(true).Length +
                        candidate.GetComponentsInChildren<TMP_Text>(true).Length > 0)
                    {
                        template = candidate;
                        break;
                    }
                }
                if (template == null)
                {
                    foreach (KeybindInterface candidate in originals)
                    {
                        if (candidate == null || candidate.Identifier == ActionId) continue;
                        if (candidate.GetComponentsInChildren<Text>(true).Length +
                            candidate.GetComponentsInChildren<TMP_Text>(true).Length == 0) continue;
                        if (candidate.transform.parent != null &&
                            candidate.transform.parent.name == "UnbindableKeybinds") continue;
                        template = candidate;
                        break;
                    }
                }
                if (template == null)
                {
                    return false;
                }

                Transform source = template.transform;
                Transform candidateRoot = template.transform.parent;
                if (candidateRoot != null && candidateRoot.parent != null &&
                    candidateRoot.GetComponentsInChildren<KeybindInterface>(true).Length == 1 &&
                    candidateRoot.GetComponentsInChildren<Text>(true).Length +
                    candidateRoot.GetComponentsInChildren<TMP_Text>(true).Length > 2)
                    source = candidateRoot;
                Transform parent = source.parent;
                // Строка вставляется под ходьбой - её не нужно искать прокруткой
                Transform firstAction = parent.Find("Walk");
                int index = firstAction != null ? firstAction.GetSiblingIndex() : source.GetSiblingIndex();
                GameObject instance = UnityEngine.Object.Instantiate(source.gameObject, parent);
                instance.name = "xSkills_SkillTree_Keybind";
                // Отключается ванильная локализация, перезаписывающая подпись клона
                foreach (Behaviour component in instance.GetComponentsInChildren<Behaviour>(true))
                {
                    if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                        component.enabled = false;
                }
                instance.transform.SetSiblingIndex(index + 1);
                KeybindInterface row = instance.GetComponentInChildren<KeybindInterface>(true);
                if (row == null)
                {
                    UnityEngine.Object.Destroy(instance);
                    return false;
                }
                AccessTools.Field(typeof(KeybindInterface), "identifier").SetValue(row, ActionId);
                AccessTools.Field(typeof(KeybindInterface), "mainKeyDefault").SetValue(row, KeyCode.C);
                AccessTools.Field(typeof(KeybindInterface), "altKeyDefault").SetValue(row, KeyCode.None);
                row.Set(new Keybind(ActionId, SavedDefault()));
                TranslateLabel(row);

                RectTransform sourceRect = firstAction != null
                    ? firstAction.GetComponent<RectTransform>() : source.GetComponent<RectTransform>();
                RectTransform targetRect = instance.GetComponent<RectTransform>();
                bool automaticLayout = parent.GetComponent<LayoutGroup>() != null;
                float step = sourceRect != null ? Mathf.Max(40f, sourceRect.rect.height + 6f) : 48f;
                if (PlaceRowAtBottom(box, row)) return true;
                if (!automaticLayout && sourceRect != null && targetRect != null)
                {
                    // Соседние строки сдвигаются, чтобы новая не перекрывала управление
                    for (int i = index + 2; i < parent.childCount; i++)
                    {
                        RectTransform next = parent.GetChild(i) as RectTransform;
                        if (next == null || next == targetRect) continue;
                        next.anchoredPosition += Vector2.down * step;
                    }
                    targetRect.anchoredPosition = sourceRect.anchoredPosition + Vector2.down * step;
                }
                if (automaticLayout)
                {
                    RectTransform groupRect = parent as RectTransform;
                    if (groupRect != null) LayoutRebuilder.MarkLayoutForRebuild(groupRect);
                }
                ExpandContent(box, step);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool PlaceRowAtBottom(ControlsSettingsBox box, KeybindInterface row)
        {
            if (box == null || row == null) return false;
            FieldInfo field = AccessTools.Field(typeof(ControlsSettingsBox), "contentBox");
            RectTransform content = field != null ? field.GetValue(box) as RectTransform : null;
            if (content == null) return false;

            Transform root = row.transform;
            while (root.parent != null && root.name != "xSkills_SkillTree_Keybind"
                && root.parent != box.transform)
                root = root.parent;
            if (root.name != "xSkills_SkillTree_Keybind" || !root.IsChildOf(content)) return false;
            RectTransform rect = root as RectTransform;
            Transform parent = root.parent;
            if (rect == null || parent == null) return false;

            float step = Mathf.Max(48f, rect.rect.height + 8f);
            if (root.GetSiblingIndex() != parent.childCount - 1)
                root.SetAsLastSibling();

            RectTransform parentRect = parent as RectTransform;
            if (parent.GetComponent<LayoutGroup>() != null)
            {
                if (parentRect != null) LayoutRebuilder.MarkLayoutForRebuild(parentRect);
            }
            else
            {
                // Строка устанавливается после последней штатной привязки
                float lowest = float.PositiveInfinity;
                Vector3[] corners = new Vector3[4];
                foreach (KeybindInterface candidate in box.GetComponentsInChildren<KeybindInterface>(true))
                {
                    if (candidate == null || candidate == row) continue;
                    Transform candidateRoot = candidate.transform;
                    while (candidateRoot.parent != null && candidateRoot.parent != parent
                        && candidateRoot != parent)
                        candidateRoot = candidateRoot.parent;
                    if (candidateRoot == parent || candidateRoot.parent != parent ||
                        !candidateRoot.gameObject.activeSelf) continue;
                    RectTransform otherRect = candidateRoot as RectTransform;
                    if (otherRect == null) continue;
                    otherRect.GetWorldCorners(corners);
                    float bottom = parent.InverseTransformPoint(corners[0]).y;
                    if (bottom < lowest) lowest = bottom;
                }
                if (!float.IsPositiveInfinity(lowest))
                {
                    rect.GetWorldCorners(corners);
                    float top = parent.InverseTransformPoint(corners[1]).y;
                    float movement = lowest - 8f - top;
                    if (Mathf.Abs(movement) > .25f)
                        rect.anchoredPosition += Vector2.up * movement;
                }
            }

            ExpandContent(box, step);
            EnsureRowScrollRange(box, content, rect);
            return true;
        }

        private static void ExpandContent(ControlsSettingsBox box, float extra)
        {
            FieldInfo field = AccessTools.Field(typeof(ControlsSettingsBox), "contentBox");
            RectTransform content = field != null ? field.GetValue(box) as RectTransform : null;
            if (content == null) return;
            float required = 2684f + extra;
            if (content.rect.height + .5f >= required) return;
            Vector2 minimum = content.offsetMin;
            minimum.y -= required - content.rect.height;
            content.offsetMin = minimum;
        }

        private static void EnsureRowScrollRange(ControlsSettingsBox box, RectTransform content, RectTransform row)
        {
            // Область прокрутки расширяется только если строка выходит за её границу
            FitRowInContent(content, row, 16f);
            ScrollRect scroll = content.GetComponentInParent<ScrollRect>(true);
            if (scroll == null || scroll.content == null || !content.IsChildOf(scroll.content))
            {
                foreach (ScrollRect candidate in box.GetComponentsInChildren<ScrollRect>(true))
                {
                    if (candidate.content != null && content.IsChildOf(candidate.content))
                    {
                        scroll = candidate;
                        break;
                    }
                }
            }
            if (scroll == null || scroll.content == null) return;
            if (scroll.content != content && content.IsChildOf(scroll.content))
                FitRowInContent(scroll.content, row, 16f);
        }

        private static void FitRowInContent(RectTransform content, RectTransform row, float padding)
        {
            if (content == null || row == null) return;
            Vector3[] corners = new Vector3[4];
            row.GetWorldCorners(corners);
            float bottom = content.InverseTransformPoint(corners[0]).y;
            float missing = content.rect.yMin + padding - bottom;
            if (missing <= .5f) return;
            Vector2 minimum = content.offsetMin;
            minimum.y -= missing + 4f;
            content.offsetMin = minimum;
        }

        private static void TranslateLabel(KeybindInterface row)
        {
            string translated = Localizer.T("hotkey-xskills-menu");
            bool found = false;
            // Сначала проверяется клон всей строки, включая подписи вне KeybindInterface
            Transform labelRoot = row.transform;
            if (row.transform.parent != null && row.transform.parent.GetComponentsInChildren<KeybindInterface>(true).Length == 1)
                labelRoot = row.transform.parent;
            foreach (Text label in labelRoot.GetComponentsInChildren<Text>(true))
            {
                if (label.GetComponentInParent<Button>() != null) continue;
                label.text = translated;
                found = true;
                break;
            }
            if (!found)
            {
                foreach (TMP_Text label in labelRoot.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label.GetComponentInParent<Button>() != null) continue;
                    label.text = translated;
                    found = true;
                    break;
                }
            }
        }

        private static void SyncExistingBinding(ControlsSettingsBox box)
        {
            KeybindInterface row = FindRow(box);
            if (row == null) return;
            if (MyInput.Keybinds == null) return;
            Keybind existing;
            if (MyInput.Keybinds.TryGetValue(ActionId, out existing) && existing != null)
            {
                if (row.Keybind == null || row.Keybind.MainKey != existing.MainKey || row.Keybind.AltKey != existing.AltKey)
                    row.Set(existing);
            }
            else
            {
                if (row.Keybind == null) row.Set(new Keybind(ActionId, SavedDefault()));
                MyInput.Keybinds[ActionId] = row.Keybind;
            }
        }
    }
}
