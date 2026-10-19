using UnityEngine;

namespace xSkills.Systems
{
    internal sealed class TemperatureNauseaCapture : MonoBehaviour
    {
        private RenderTexture capturedFrame;
        private int capturedWidth;
        private int capturedHeight;

        internal RenderTexture CapturedFrame { get { return capturedFrame; } }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!enabled || !BodyTemperatureSystem.NeedsNauseaCapture)
            {
                Graphics.Blit(source, destination);
                return;
            }
            int width = Mathf.Min(960, Mathf.Max(256, source.width / 2));
            int height = Mathf.Min(540, Mathf.Max(144, source.height / 2));
            if (capturedFrame == null || capturedWidth != width || capturedHeight != height)
            {
                ReleaseFrame();
                capturedFrame = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                capturedFrame.filterMode = FilterMode.Bilinear;
                capturedFrame.wrapMode = TextureWrapMode.Clamp;
                capturedFrame.Create();
                capturedWidth = width;
                capturedHeight = height;
            }
            // Последним проходом всегда восстанавливается штатный вывод камеры
            Graphics.Blit(source, capturedFrame);
            Graphics.Blit(source, destination);
        }

        private void OnDisable()
        {
            ReleaseFrame();
        }

        private void OnDestroy()
        {
            ReleaseFrame();
        }

        private void ReleaseFrame()
        {
            if (capturedFrame == null) return;
            capturedFrame.Release();
            Destroy(capturedFrame);
            capturedFrame = null;
            capturedWidth = 0;
            capturedHeight = 0;
        }
    }
}
