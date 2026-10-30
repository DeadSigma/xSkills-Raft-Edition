using System;
using HMLLibrary;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Services;

namespace xSkills.Systems
{
    internal sealed class LastStandEffect : MonoBehaviour
    {
        private const float Lifetime = 3.35f;
        private const float FlyDuration = 1.05f;
        private const int ParticleCount = 48;
        private static LastStandEffect active;
        private static Sprite sparkleSprite;
        private static Texture2D sparkleTexture;

        private readonly SpriteParticle[] particles = new SpriteParticle[ParticleCount];
        private RectTransform totem;
        private RectTransform sigil;
        private RectTransform burst;
        private RectTransform glow;
        private Image totemImage;
        private Image sigilImage;
        private Image burstImage;
        private Image glowImage;
        private Image flashImage;
        private ParticleSystem worldParticles;
        private Material particleMaterial;
        private float elapsed;

        private sealed class SpriteParticle
        {
            internal RectTransform rect;
            internal Image image;
            internal Vector2 direction;
            internal float distance;
            internal float delay;
            internal float size;
            internal float rotation;
            internal float spin;
            internal Color color;
        }

        internal static void Play()
        {
            if (active != null)
            {
                UnityEngine.Object.Destroy(active.gameObject);
                active = null;
            }
            var go = new GameObject("xSkills - Last Stand FX");
            active = go.AddComponent<LastStandEffect>();
            try
            {
                active.CreateVisuals();
                SoundPlayer.Play("laststand");
            }
            catch
            {
                UnityEngine.Object.Destroy(go);
                active = null;
                throw;
            }
        }

        internal static void Dispose()
        {
            if (active != null)
            {
                UnityEngine.Object.Destroy(active.gameObject);
                active = null;
            }
            if (sparkleSprite != null) UnityEngine.Object.Destroy(sparkleSprite);
            if (sparkleTexture != null) UnityEngine.Object.Destroy(sparkleTexture);
            sparkleSprite = null;
            sparkleTexture = null;
        }

        private void CreateVisuals()
        {
            var canvasObject = new GameObject("Last Stand - Screen Overlay", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            RectTransform panel = canvasObject.GetComponent<RectTransform>();
            Sprite sparkle = GetSparkleSprite();
            flashImage = MakeImage(panel, "Screen flash", sparkle, Vector2.zero, new Vector2(2500f, 1500f));
            flashImage.color = new Color(.86f, 1f, .65f, 0f);
            RectTransform flashRect = flashImage.rectTransform;
            flashRect.localScale = Vector3.one;

            glowImage = MakeImage(panel, "Emerald glow", sparkle, Vector2.zero, new Vector2(700f, 700f));
            glow = glowImage.rectTransform;
            sigilImage = MakeImage(panel, "Rotating sigil",
                ResourceLoader.Sprite("effects", "laststand_sigil"), Vector2.zero, new Vector2(730f, 730f));
            sigil = sigilImage.rectTransform;
            burstImage = MakeImage(panel, "Healing burst",
                ResourceLoader.Sprite("effects", "laststand_burst"), Vector2.zero, new Vector2(850f, 850f));
            burst = burstImage.rectTransform;
            totemImage = MakeImage(panel, "Last Stand totem",
                ResourceLoader.Sprite("effects", "laststand_totem"), Vector2.zero, new Vector2(490f, 490f));
            totem = totemImage.rectTransform;
            if (totemImage.sprite == null || sigilImage.sprite == null || burstImage.sprite == null)
                Debug.LogWarning("[xSkills] Last Stand: missing PNG in assets/textures/gui/skilltree/effects");

            for (int i = 0; i < particles.Length; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var p = new SpriteParticle();
                p.direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                p.distance = UnityEngine.Random.Range(290f, 650f);
                p.delay = UnityEngine.Random.Range(.24f, .74f);
                p.size = UnityEngine.Random.Range(11f, 29f);
                p.rotation = UnityEngine.Random.Range(-120f, 120f);
                p.spin = UnityEngine.Random.Range(-360f, 360f);
                p.color = (i % 3 == 0) ? new Color(1f, .84f, .3f) :
                    new Color(.49f, 1f, .43f);
                p.image = MakeImage(panel, "Life spark " + i, sparkle,
                    Vector2.zero, new Vector2(p.size, p.size));
                p.rect = p.image.rectTransform;
                p.image.color = Color.clear;
                particles[i] = p;
            }

            TryCreateWorldParticles();
            UpdateVisuals(0f);
        }

        private static Image MakeImage(RectTransform parent, string name, Sprite sprite,
            Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Sprite GetSparkleSprite()
        {
            if (sparkleSprite != null) return sparkleSprite;
            const int resolution = 64;
            sparkleTexture = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false);
            sparkleTexture.name = "xSkills Life Spark";
            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = (x - 31.5f) / 31.5f;
                    float dy = (y - 31.5f) / 31.5f;
                    float radial = Mathf.Exp(-(dx * dx + dy * dy) * 8f);
                    float rays = Mathf.Exp(-Mathf.Abs(dx) * 25f) *
                        Mathf.Exp(-Mathf.Abs(dy) * 3.2f) +
                        Mathf.Exp(-Mathf.Abs(dy) * 25f) * Mathf.Exp(-Mathf.Abs(dx) * 3.2f);
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f,
                        Mathf.Clamp01(radial + rays * .48f));
                }
            }
            sparkleTexture.SetPixels(pixels);
            sparkleTexture.Apply(false, false);
            sparkleTexture.filterMode = FilterMode.Bilinear;
            sparkleSprite = Sprite.Create(sparkleTexture,
                new Rect(0, 0, resolution, resolution), new Vector2(.5f, .5f), 100f);
            return sparkleSprite;
        }

        private void TryCreateWorldParticles()
        {
            try
            {
                Network_Player player = RAPI.GetLocalPlayer();
                Camera camera = player != null ? player.Camera : Camera.main;
                if (camera == null) return;
                Shader shader = Shader.Find("Particles/Additive");
                if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");
                if (shader == null) return;
                var go = new GameObject("Last Stand - Particles");
                go.transform.SetParent(camera.transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 1.1f);
                worldParticles = go.AddComponent<ParticleSystem>();
                worldParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = worldParticles.main;
                main.loop = false;
                main.duration = 1.4f;
                main.playOnAwake = false;
                main.maxParticles = 130;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.65f, 1.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, 1.45f);
                main.startSize = new ParticleSystem.MinMaxCurve(.024f, .07f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(.36f, 1f, .34f, .85f), new Color(1f, .84f, .28f, .95f));
                var shape = worldParticles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = .12f;
                var emission = worldParticles.emission;
                emission.enabled = false;
                var colorOverLifetime = worldParticles.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .3f),
                        new GradientAlphaKey(0f, 1f) });
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                particleMaterial = new Material(shader);
                particleMaterial.name = "xSkills Last Stand Spark";
                particleMaterial.mainTexture = sparkleTexture;
                renderer.material = particleMaterial;
                worldParticles.Emit(90);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Last Stand world particles disabled: " + ex.Message);
                if (worldParticles != null) UnityEngine.Object.Destroy(worldParticles.gameObject);
                worldParticles = null;
            }
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Vector2 Bezier(Vector2 start, Vector2 controlA,
            Vector2 controlB, Vector2 finish, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * inverse * start +
                3f * inverse * inverse * t * controlA +
                3f * inverse * t * t * controlB +
                t * t * t * finish;
        }

        private void Update()
        {
            if (!RAPI.IsCurrentSceneGame())
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= Lifetime)
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }
            UpdateVisuals(elapsed);
        }

        private void UpdateVisuals(float time)
        {
            float appear = Smooth(time / .24f);
            float holdFade = 1f - Smooth((time - 2.57f) / .7f);
            float visibility = appear * holdFade;

            float flyTime = Mathf.Clamp01(time / FlyDuration);
            float flyProgress = Smooth(flyTime);
            Vector2 position = Bezier(
                new Vector2(0f, -12f),
                new Vector2(255f, -135f),
                new Vector2(255f, 175f),
                new Vector2(0f, -6f), flyProgress);
            float hover = Smooth((time - FlyDuration) / .32f);
            float hoverTime = Mathf.Max(0f, time - FlyDuration);
            position.x += hover * (7f * Mathf.Sin(hoverTime * 1.65f));
            position.y += hover * (12f * Mathf.Sin(hoverTime * 2.8f));
            totem.anchoredPosition = position;

            float spin = 1f - Smooth(time / .93f);
            float yaw = 360f * spin + hover * 3f * Mathf.Sin(hoverTime * 1.8f);
            totem.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float arrivalPulse = .22f * Mathf.Exp(-Mathf.Pow((flyTime - .8f) / .17f, 2f));
            float floatingScale = 1f + .015f * hover * Mathf.Sin(hoverTime * 2.6f);
            float scale = Mathf.Lerp(.3f, 1.06f, Smooth(flyTime)) *
                (1f + arrivalPulse) * floatingScale;
            totem.localScale = Vector3.one * scale;
            totemImage.color = new Color(1f, 1f, 1f, visibility);

            sigil.localScale = Vector3.one * Mathf.Lerp(.35f, 1.05f, Smooth(time / .85f));
            sigil.localEulerAngles = new Vector3(0f, 0f, time * 250f - 120f);
            sigilImage.color = new Color(1f, 1f, 1f,
                Smooth(time / .25f) * (1f - Smooth((time - 2.48f) / .68f)) * .57f);

            glow.localScale = Vector3.one * Mathf.Lerp(.55f, 1.4f, Smooth(time / 1.4f));
            glow.anchoredPosition = position * .12f;
            float glowAlpha = Mathf.Lerp(.48f, .16f, Smooth(time / 1.45f));
            glowImage.color = new Color(.42f, 1f, .36f,
                glowAlpha * Smooth(time / .35f) * holdFade);
            burst.localScale = Vector3.one * Mathf.Lerp(.1f, 1.4f,
                Smooth((time - .19f) / .95f));
            burst.localEulerAngles = new Vector3(0f, 0f, time * -45f);
            burstImage.color = new Color(1f, 1f, 1f,
                Smooth((time - .18f) / .16f) *
                (1f - Smooth((time - .45f) / .9f)) * .74f);

            float flash = Smooth((time - .17f) / .06f) *
                (1f - Smooth((time - .25f) / .55f));
            flashImage.color = new Color(.73f, 1f, .65f, flash * .14f);

            for (int i = 0; i < particles.Length; i++)
            {
                SpriteParticle p = particles[i];
                float progress = (time - p.delay) / 1.24f;
                if (progress < 0f || progress >= 1f)
                {
                    p.image.color = Color.clear;
                    continue;
                }
                float distance = Mathf.Lerp(18f, p.distance, Smooth(progress));
                float curve = Mathf.Sin(progress * Mathf.PI) * 45f;
                Vector2 perpendicular = new Vector2(-p.direction.y, p.direction.x);
                p.rect.anchoredPosition = p.direction * distance + perpendicular * curve;
                p.rect.localEulerAngles = new Vector3(0f, 0f, p.rotation + p.spin * progress);
                p.rect.localScale = Vector3.one * (1f - .45f * progress);
                float alpha = Smooth(progress / .13f) *
                    (1f - Smooth((progress - .45f) / .55f));
                p.image.color = new Color(p.color.r, p.color.g, p.color.b, alpha);
            }
        }

        private void OnDestroy()
        {
            if (active == this) active = null;
            if (worldParticles != null) UnityEngine.Object.Destroy(worldParticles.gameObject);
            if (particleMaterial != null) UnityEngine.Object.Destroy(particleMaterial);
        }
    }
}
