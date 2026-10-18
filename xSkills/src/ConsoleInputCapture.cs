using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace xSkills
{
    internal static class ConsoleInputCapture
    {
        public static bool IsTyping()
        {
            EventSystem events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null)
            {
                GameObject selected = events.currentSelectedGameObject;
                InputField legacy = selected.GetComponent<InputField>();
                if (legacy != null && IsVisible(legacy.transform) &&
                    legacy.isActiveAndEnabled && legacy.isFocused) return true;
                TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
                if (tmp != null && IsVisible(tmp.transform) &&
                    tmp.isActiveAndEnabled && tmp.isFocused) return true;
            }
            foreach (InputField field in UnityEngine.Object.FindObjectsOfType<InputField>())
                if (field != null && field.isActiveAndEnabled && field.isFocused &&
                    IsVisible(field.transform)) return true;
            foreach (TMP_InputField field in UnityEngine.Object.FindObjectsOfType<TMP_InputField>())
                if (field != null && field.isActiveAndEnabled && field.isFocused &&
                    IsVisible(field.transform)) return true;
            return false;
        }

        private static bool IsVisible(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            Canvas canvas = target.GetComponentInParent<Canvas>();
            if (canvas != null && !canvas.isActiveAndEnabled) return false;
            for (Transform current = target; current != null; current = current.parent)
            {
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group != null && (group.alpha <= .01f || !group.interactable))
                    return false;
                Canvas nested = current.GetComponent<Canvas>();
                if (nested != null && !nested.enabled) return false;
            }
            return true;
        }
    }
}
