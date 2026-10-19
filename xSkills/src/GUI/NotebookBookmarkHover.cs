using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace xSkills.GUI
{
    internal sealed class NotebookBookmarkHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform target;
        private Vector3 normalScale;
        private Coroutine animation;

        internal void Setup(RectTransform rect)
        {
            target = rect;
            normalScale = rect.localScale;
        }

        public void OnPointerEnter(PointerEventData data)
        {
            AnimateTo(1.08f);
        }

        public void OnPointerExit(PointerEventData data)
        {
            AnimateTo(1f);
        }

        private void AnimateTo(float multiplier)
        {
            if (target == null) return;
            if (animation != null) StopCoroutine(animation);
            animation = StartCoroutine(Animate(multiplier));
        }

        private IEnumerator Animate(float multiplier)
        {
            Vector3 start = target.localScale;
            Vector3 end = normalScale * multiplier;
            float elapsed = 0f;
            while (elapsed < 0.12f)
            {
                elapsed += Time.unscaledDeltaTime;
                target.localScale = Vector3.Lerp(start, end, Mathf.Clamp01(elapsed / 0.12f));
                yield return null;
            }
            target.localScale = end;
            animation = null;
        }

        private void OnDisable()
        {
            if (animation != null) StopCoroutine(animation);
            animation = null;
            if (target != null) target.localScale = normalScale;
        }
    }
}
