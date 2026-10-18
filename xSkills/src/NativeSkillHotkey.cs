using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HMLLibrary;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using xSkills.GUI;

namespace xSkills
{
    internal static class NativeSkillHotkey
    {
        private const string ActionId = "xskills_skilltree";
        private const string PrefKey = "xskills_skilltree_mainkey";
        private const string DefaultKeyMigration = "xskills_skilltree_default_c_migrated";
        private const string HarmonyId = "xskills.native-keybinds";
        private static Harmony harmony;
        private static int handledFrame = -100;
        private static bool installed;
        private static bool inputHooked;
        private static float nextMaintenanceAt;
        private static int lastMissingRowCount = -1;
        private static readonly List<ControlsSettingsBox> TrackedBoxes = new List<ControlsSettingsBox>();
        private static readonly MethodInfo RebuildKeybindInterfaces =
            AccessTools.Method(typeof(ControlsSettingsBox), "InitializeKeybindInterfaces");
        private static readonly Dictionary<RectTransform, float> OriginalContentHeights =
            new Dictionary<RectTransform, float>();
        private static readonly Dictionary<RectTransform, float> AppliedContentHeights =
            new Dictionary<RectTransform, float>();

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
            if (!PlayerPrefs.HasKey(DefaultKeyMigration))
            {
                // Старое значение по умолчанию переносится на C однократно
                if (PlayerPrefs.GetInt(PrefKey, (int)KeyCode.O) == (int)KeyCode.O)
                    PlayerPrefs.SetInt(PrefKey, (int)KeyCode.C);
                PlayerPrefs.SetInt(DefaultKeyMigration, 1);
                PlayerPrefs.Save();
            }
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
                TrackExistingBoxes();
                Maintain(true);
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Native keybind initialization failed: " + ex);
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
                foreach (ControlsSettingsBox box in TrackedBoxes)
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
            catch (Exception ex) { Debug.LogWarning("[xSkills] Keybind cleanup: " + ex.Message); }
            finally
            {
                RestoreContentHeights();
                TrackedBoxes.Clear();
                lastMissingRowCount = -1;
                nextMaintenanceAt = 0f;
                inputHooked = false;
            }
        }

        // Привязка и строка настроек восстанавливаются после пересоздания UI
        public static void Tick()
        {
            if (!installed) return;
            if (Time.unscaledTime < nextMaintenanceAt) return;
            nextMaintenanceAt = Time.unscaledTime + 1.5f;
            Maintain(false);
        }

        private static void TrackBox(ControlsSettingsBox box)
        {
            if (!installed || box == null || !box.gameObject.scene.IsValid() || TrackedBoxes.Contains(box)) return;
            TrackedBoxes.Add(box);
        }

        private static void TrackExistingBoxes()
        {
            foreach (ControlsSettingsBox box in Resources.FindObjectsOfTypeAll<ControlsSettingsBox>())
                TrackBox(box);
        }

        private static void Maintain(bool initial)
        {
            try
            {
                int seen = 0;
                int missing = 0;
                for (int i = TrackedBoxes.Count - 1; i >= 0; i--)
                {
                    ControlsSettingsBox box = TrackedBoxes[i];
                    if (box == null || !box.gameObject.scene.IsValid())
                    {
                        TrackedBoxes.RemoveAt(i);
                        continue;
                    }
                    seen++;
                    if (EnsureRow(box))
                    {
                        if (RebuildKeybindInterfaces != null) RebuildKeybindInterfaces.Invoke(box, null);
                    }
                    if (FindRow(box) == null) missing++;
                    else
                    {
                        SyncExistingBinding(box);
                        TranslateLabel(FindRow(box));
                    }
                }
                if (MyInput.Keybinds != null && !MyInput.Keybinds.ContainsKey(ActionId))
                {
                    MyInput.Keybinds[ActionId] = new Keybind(ActionId, SavedDefault());
                }
                if ((initial || missing != lastMissingRowCount) && missing > 0)
                    Debug.LogWarning("[xSkills] Native settings row missing in " + missing + " of " + seen + " controls panels");
                lastMissingRowCount = missing;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Native keybind maintenance: " + ex);
            }
        }

        public static bool WasPressed()
        {
            if (KeybindInterface.AssigningKey) return false;
            Keybind binding = GetBinding();
            KeyCode main = binding == null ? SavedDefault() : binding.MainKey;
            KeyCode alternative = binding == null ? KeyCode.None : binding.AltKey;
            bool pressed = Pressed(main) || Pressed(alternative);
            return pressed;
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
            return (KeyCode)PlayerPrefs.GetInt(PrefKey, (int)KeyCode.C);
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
            if (target == null)
            {
                Debug.LogWarning("[xSkills] Keybind target missing: " + type.Name + "." + methodName);
                return;
            }
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
                    inputHooked = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[xSkills] Could not hook " + method + ": " + ex.Message);
                }
            }
            if (!inputHooked)
            {
                Debug.LogWarning("[xSkills] No compatible MyInput(string) input reader found; vanilla action may still fire");
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
            TrackBox(__instance);
            EnsureRow(__instance);
        }

        private static void BeforeLoad(ControlsSettingsBox __instance)
        {
            TrackBox(__instance);
            EnsureRow(__instance);
            KeybindInterface row = FindRow(__instance);
            if (row != null) TranslateLabel(row);
        }

        private static void AfterLoad(ControlsSettingsBox __instance)
        {
            TrackBox(__instance);
            if (EnsureRow(__instance))
            {
                if (RebuildKeybindInterfaces != null) RebuildKeybindInterfaces.Invoke(__instance, null);
            }
            MigrateLegacyBinding(__instance);
            SyncExistingBinding(__instance);
            KeybindInterface loadedRow = FindRow(__instance);
            if (loadedRow != null)
            {
                TranslateLabel(loadedRow);
            }
        }

        private static void AfterSetFocus(ControlsSettingsBox __instance, bool gamepad)
        {
            TrackBox(__instance);
            if (gamepad || FindRow(__instance) == null) return;
            ExpandContent(__instance, 64f);
            KeybindInterface focusedRow = FindRow(__instance);
            TranslateLabel(focusedRow);
        }

        private static void BeforeSave(ControlsSettingsBox __instance)
        {
            TrackBox(__instance);
            if (EnsureRow(__instance))
            {
                if (RebuildKeybindInterfaces != null) RebuildKeybindInterfaces.Invoke(__instance, null);
            }
        }

        private static void AfterSave(ControlsSettingsBox __instance)
        {
            TrackBox(__instance);
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
                    Debug.LogWarning("[xSkills] Native controls row: no KeybindInterface template was found");
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
                    Debug.LogWarning("[xSkills] Normal controls row template not found");
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
                // Новое действие помещается в конец списка штатного управления
                GameObject instance = UnityEngine.Object.Instantiate(source.gameObject, parent);
                instance.name = "xSkills_SkillTree_Keybind";
                // Отключается ванильная локализация, перезаписывающая подпись клона
                foreach (Behaviour component in instance.GetComponentsInChildren<Behaviour>(true))
                {
                    if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                        component.enabled = false;
                }
                instance.transform.SetAsLastSibling();
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

                RectTransform sourceRect = source.GetComponent<RectTransform>();
                RectTransform targetRect = instance.GetComponent<RectTransform>();
                bool automaticLayout = parent.GetComponent<LayoutGroup>() != null;
                float step = sourceRect != null ? Mathf.Max(40f, sourceRect.rect.height + 6f) : 48f;
                if (!automaticLayout && sourceRect != null && targetRect != null)
                {
                    // Без LayoutGroup новая строка помещается ниже последней ванильной
                    RectTransform previous = parent.childCount > 1
                        ? parent.GetChild(parent.childCount - 2) as RectTransform : null;
                    targetRect.anchoredPosition = (previous != null
                        ? previous.anchoredPosition : sourceRect.anchoredPosition) + Vector2.down * step;
                }
                if (automaticLayout)
                {
                    RectTransform groupRect = parent as RectTransform;
                    if (groupRect != null) LayoutRebuilder.MarkLayoutForRebuild(groupRect);
                }
                ExpandContent(box, step);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Unable to add native control row: " + ex);
                return false;
            }
        }

        private static void ExpandContent(ControlsSettingsBox box, float extra)
        {
            FieldInfo field = AccessTools.Field(typeof(ControlsSettingsBox), "contentBox");
            RectTransform content = field != null ? field.GetValue(box) as RectTransform : null;
            if (content == null) return;
            Vector2 size = content.sizeDelta;
            float requiredHeight = Mathf.Max(size.y, 2684f + extra);
            if (Mathf.Abs(size.y - requiredHeight) < .01f) return;
            if (!OriginalContentHeights.ContainsKey(content))
                OriginalContentHeights[content] = size.y;
            AppliedContentHeights[content] = requiredHeight;
            size.y = requiredHeight;
            content.sizeDelta = size;
            LayoutRebuilder.MarkLayoutForRebuild(content);
        }

        private static void RestoreContentHeights()
        {
            foreach (KeyValuePair<RectTransform, float> entry in OriginalContentHeights)
            {
                RectTransform content = entry.Key;
                if (content == null) continue;
                float applied;
                if (!AppliedContentHeights.TryGetValue(content, out applied) ||
                    Mathf.Abs(content.sizeDelta.y - applied) > .01f) continue;
                Vector2 size = content.sizeDelta;
                size.y = entry.Value;
                content.sizeDelta = size;
                LayoutRebuilder.MarkLayoutForRebuild(content);
            }
            OriginalContentHeights.Clear();
            AppliedContentHeights.Clear();
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
            if (!found)
            {
                Debug.LogWarning("[xSkills] Control row has no non-button Text/TMP label");
            }
        }

        private static void MigrateLegacyBinding(ControlsSettingsBox box)
        {
            // Сохранённое старое O заменяется, если привязка не изменялась
            if (SavedDefault() != KeyCode.C) return;
            KeybindInterface row = FindRow(box);
            if (row == null || row.Keybind == null) return;
            Keybind binding = row.Keybind;
            if (binding.MainKey != KeyCode.O || binding.AltKey != KeyCode.None) return;
            binding.MainKey = KeyCode.C;
            row.Set(binding);
            if (MyInput.Keybinds != null) MyInput.Keybinds[ActionId] = binding;
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
