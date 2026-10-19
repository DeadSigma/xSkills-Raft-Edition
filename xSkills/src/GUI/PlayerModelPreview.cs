using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using xSkills.Systems;

namespace xSkills.GUI
{
    internal sealed class PlayerModelPreview : IDisposable
    {
        private const int PreviewLayer = 28;
        private const float MaxTurnDegrees = 45f;
        private const float TurnSpeed = 0.82f;
        private const float RenderInterval = 1f / 30f;
        private const float PreviewZoom = 1.30f;

        private readonly StringBuilder poseDiagnostics = new StringBuilder(8192);
        private readonly Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform>();
        private readonly Dictionary<Transform, Transform> extraHairBones = new Dictionary<Transform, Transform>();
        private readonly List<Mesh> bakedMeshes = new List<Mesh>();
        private readonly HashSet<SkinnedMeshRenderer> sourceHair = new HashSet<SkinnedMeshRenderer>();
        private readonly HashSet<Transform> hairRoots = new HashSet<Transform>();
        private readonly Dictionary<Equipment_Model, GameObject> equipmentVisuals =
            new Dictionary<Equipment_Model, GameObject>();
        private static readonly System.Reflection.FieldInfo EquipmentRemoteModel =
            typeof(Equipment_Model).GetField("remoteModel",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo EquipmentLocalModel =
            typeof(Equipment_Model).GetField("localModel",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private Network_Player equipmentPlayer;
        private CharacterModelModifications equipmentCharacterModel;
        private readonly HashSet<Equipment_Model> missingEquipmentVisuals = new HashSet<Equipment_Model>();
        private float nextEquipmentUpdate;
        private int appliedOutfitIndex = -1;
        private GameObject sceneRoot;
        private Transform turntable;
        private Transform copiedRig;
        private Camera camera;
        private RenderTexture texture;
        private Transform sourceRoot;
        private Transform modelRoot;
        private SkinnedMeshRenderer fullBody;
        private Transform sourceHeadBone;
        private SkinnedMeshRenderer firstPersonArms;
        private Transform hatParent;
        private Transform leftHandHolder;
        private Transform rightHandHolder;
        private Transform leftHandRopeRoot;
        private Transform rightHandHookRoot;
        private Transform hazmatBody;
        private Transform hazmatArms;
        private bool hazmatActive;
        private int rendererCount;
        private float createdAt;
        private float nextRenderAt;

        public RenderTexture Texture { get { return texture; } }
        public bool Available { get { return rendererCount > 0 && camera != null && texture != null; } }

        public PlayerModelPreview(Network_Player player)
        {
            if (player == null || player.Animator == null || player.currentModel == null) return;
            try
            {
                poseDiagnostics.AppendLine("xSkills v56 - Flat Foot Contact");
                poseDiagnostics.AppendLine("UTC: " + DateTime.UtcNow.ToString("u"));
                CharacterModelModifications model = player.currentModel;
                equipmentPlayer = player;
                equipmentCharacterModel = model;
                LogAnimatorState("SOURCE", player.Animator.anim, false);
                sourceRoot = player.Animator.transform;
                modelRoot = model.transform;
                fullBody = model.fullBodyMesh;
                sourceHeadBone = model.headBone;
                hazmatActive = player.HazmatSuit != null && player.HazmatSuit.Equipped;

                if (model.hairMeshes != null)
                    foreach (SkinnedMeshRenderer hair in model.hairMeshes)
                        if (hair != null) sourceHair.Add(hair);

                // Волосы выбираются по активной причёске, включая скрытые меши
                CollectHairStyle(model);
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer != null && IsHair(renderer.transform)) sourceHair.Add(renderer);
                hatParent = model.hatParent;
                leftHandHolder = model.leftHandItemHolder;
                rightHandHolder = model.rightHandItemHolder;
                FindHookVisuals(player);

                // Доступ к полям берётся из игровых классов без создания копий MonoBehaviour
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var armField = typeof(CharacterModelModifications).GetField("armMesh", flags);
                var suitBodyField = typeof(CharacterModelModifications).GetField("fullBodyMeshHazmat", flags);
                var suitArmField = typeof(CharacterModelModifications).GetField("armMeshHazmat", flags);
                firstPersonArms = armField != null ? armField.GetValue(model) as SkinnedMeshRenderer : null;
                GameObject suitBody = suitBodyField != null ? suitBodyField.GetValue(model) as GameObject : null;
                GameObject suitArm = suitArmField != null ? suitArmField.GetValue(model) as GameObject : null;
                hazmatBody = suitBody != null ? suitBody.transform : null;
                hazmatArms = suitArm != null ? suitArm.transform : null;

                sceneRoot = new GameObject("xSkills Character Preview Stage");
                sceneRoot.transform.position = new Vector3(10000f, 10000f, 10000f);
                turntable = new GameObject("Turntable").transform;
                turntable.SetParent(sceneRoot.transform, false);
                copiedRig = new GameObject("Character Rig").transform;
                copiedRig.SetParent(turntable, false);
                copiedRig.localScale = sourceRoot.lossyScale;
                copies[sourceRoot] = copiedRig;
                CopyHierarchy(sourceRoot, copiedRig, 0);
                PrepareBodyBoneHierarchy();
                ApplyFixedHookPose(player, model);
                CopyVisuals();
                SyncOutfitMaterials();
                poseDiagnostics.AppendLine("RENDERERS=" + rendererCount);
                CopyHandVisual(leftHandRopeRoot, leftHandHolder, "xSkills Left Hand Rope");
                CopyHandVisual(rightHandHookRoot, rightHandHolder, "xSkills Right Hand Hook");

                if (rendererCount == 0)
                {
                    Dispose();
                    return;
                }

                texture = new RenderTexture(512, 768, 24, RenderTextureFormat.ARGB32);
                texture.name = "xSkills Character RT";
                texture.antiAliasing = 2;
                texture.Create();

                GameObject cameraObject = new GameObject("Character Preview Camera");
                cameraObject.transform.SetParent(sceneRoot.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.depth = -1000f;
                camera.useOcclusionCulling = false;
                camera.renderingPath = RenderingPath.Forward;
                camera.depthTextureMode = DepthTextureMode.None;
                camera.cullingMask = 1 << PreviewLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.allowHDR = false;
                camera.orthographic = true;
                camera.nearClipPlane = 0.03f;
                camera.farClipPlane = 40f;
                camera.targetTexture = texture;

                GameObject lightObject = new GameObject("Character Preview Light");
                lightObject.transform.SetParent(sceneRoot.transform, false);
                Light lamp = lightObject.AddComponent<Light>();
                lamp.type = LightType.Directional;
                lamp.color = new Color(1f, .93f, .83f, 1f);
                lamp.intensity = 1.3f;
                lamp.cullingMask = 1 << PreviewLayer;
                lamp.shadows = LightShadows.None;
                lightObject.transform.rotation = Quaternion.Euler(30f, 145f, 0f);

                FitFullBody();
                FitFromRenderedSilhouette();
                SyncEquipmentVisuals();
                createdAt = Time.unscaledTime;
                nextRenderAt = 0f;
                Render();
            }
            catch (Exception ex)
            {
                poseDiagnostics.AppendLine("PREVIEW ERROR=" + ex);
                Dispose();
            }
        }

        private void CopyHierarchy(Transform original, Transform parent, int depth)
        {
            if (depth > 50 || copies.Count > 900) return;
            foreach (Transform child in original)
            {
                if (child == null) continue;
                Transform clone = new GameObject(child.name).transform;
                clone.gameObject.layer = PreviewLayer;
                clone.SetParent(parent, false);
                clone.localPosition = child.localPosition;
                clone.localRotation = child.localRotation;
                clone.localScale = child.localScale;
                copies[child] = clone;
                CopyHierarchy(child, clone, depth + 1);
            }
        }

        private void PrepareBodyBoneHierarchy()
        {
            if (fullBody == null || fullBody.bones == null) return;

            int mapped = 0;
            int external = 0;
            foreach (Transform bone in fullBody.bones)
            {
                if (bone == null) continue;
                Transform existing;
                if (copies.TryGetValue(bone, out existing) && existing != null)
                {
                    mapped++;
                    continue;
                }
                if (MapExternalBodyBone(bone, 0) != null) external++;
            }
            poseDiagnostics.AppendLine("BODY BONE HIERARCHY: original=" + mapped +
                " external=" + external + " total=" + fullBody.bones.Length);
        }

        private Transform MapExternalBodyBone(Transform sourceBone, int depth)
        {
            if (sourceBone == null || depth > 75) return null;

            Transform mapped;
            if (copies.TryGetValue(sourceBone, out mapped) && mapped != null) return mapped;
            if (extraHairBones.TryGetValue(sourceBone, out mapped) && mapped != null) return mapped;

            Transform sourceParent = sourceBone.parent;
            Transform copyParent = null;
            if (sourceParent != null && sourceRoot != null &&
                sourceParent != sourceRoot.parent && sourceParent != sourceRoot.root)
                copyParent = MapExternalBodyBone(sourceParent, depth + 1);

            bool inherited = copyParent != null;
            if (!inherited) copyParent = copiedRig;
            if (copyParent == null) return null;

            Transform clone = new GameObject("xSkills Body Bone " + sourceBone.name).transform;
            clone.gameObject.layer = PreviewLayer;
            clone.SetParent(copyParent, false);
            if (inherited)
            {
                clone.localPosition = sourceBone.localPosition;
                clone.localRotation = sourceBone.localRotation;
                clone.localScale = sourceBone.localScale;
            }
            else
            {
                // Внешний скелет привязывается к сцене превью в координатах аниматора
                clone.localPosition = sourceRoot.InverseTransformPoint(sourceBone.position);
                clone.localRotation = Quaternion.Inverse(sourceRoot.rotation) * sourceBone.rotation;
                clone.localScale = Vector3.one;
            }
            extraHairBones[sourceBone] = clone;
            return clone;
        }

        private bool TryGetCopiedBone(Transform sourceBone, out Transform mapped)
        {
            mapped = null;
            if (sourceBone == null) return false;
            if (copies.TryGetValue(sourceBone, out mapped) && mapped != null) return true;
            if (extraHairBones.TryGetValue(sourceBone, out mapped) && mapped != null) return true;
            mapped = null;
            return false;
        }

        private static bool IsHair(Transform transform)
        {
            for (Transform t = transform; t != null; t = t.parent)
            {
                if (t.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (t.GetComponent<CharacterModelModifications>() != null) break;
            }
            return false;
        }

        private void CollectHairStyle(CharacterModelModifications model)
        {
            if (model.hairStyles == null) return;
            foreach (HairStyleConnection connection in model.hairStyles)
            {
                if (connection == null || connection.hairStyle != model.CurrentHairStyle) continue;
                var flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                foreach (var field in connection.GetType().GetFields(flags))
                {
                    if (field.Name == "hairStyle") continue;
                    object value;
                    try { value = field.GetValue(connection); }
                    catch { continue; }
                    AddHairReferences(value);
                }
            }
        }

        private void AddHairReferences(object value)
        {
            if (value == null || value is string) return;
            GameObject go = value as GameObject;
            Transform transform = value as Transform;
            Component component = value as Component;
            if (go != null) transform = go.transform;
            else if (component != null) transform = component.transform;
            if (transform != null)
            {
                if (transform != modelRoot && transform.IsChildOf(modelRoot) &&
                    (fullBody == null || !IsWithin(fullBody.transform, transform)))
                    hairRoots.Add(transform);
                return;
            }
            var enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null) return;
            foreach (object element in enumerable)
                if (!object.ReferenceEquals(element, value)) AddHairReferences(element);
        }

        private bool IsSelectedHair(Transform transform)
        {
            if (IsHair(transform)) return true;
            foreach (Transform root in hairRoots)
                if (IsWithin(transform, root)) return true;
            return false;
        }

        private static bool HasAnimatorParameter(Animator animator, string name,
            AnimatorControllerParameterType type)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.name == name && parameter.type == type) return true;
            return false;
        }

        private struct GenericLeg
        {
            public Transform thigh;
            public Transform shin;
            public Transform foot;
        }

        private static int GetBoneSide(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string lower = name.ToLowerInvariant();
            if (lower.Contains("left")) return -1;
            if (lower.Contains("right")) return 1;
            string[] parts = lower.Split(new[] { '.', '_', '-', ' ', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "l") return -1;
                if (parts[i] == "r") return 1;
            }
            if (lower.StartsWith("lthigh") || lower.StartsWith("lshin") ||
                lower.StartsWith("lfoot") || lower.StartsWith("lcalf")) return -1;
            if (lower.StartsWith("rthigh") || lower.StartsWith("rshin") ||
                lower.StartsWith("rfoot") || lower.StartsWith("rcalf")) return 1;
            return 0;
        }

        private static int GetLegPart(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string n = name.ToLowerInvariant().Replace("_", "").Replace("-", "");
            if (n.Contains("thigh") || n.Contains("upperleg") || n.Contains("upleg")) return 1;
            if (n.Contains("shin") || n.Contains("calf") || n.Contains("lowerleg")) return 2;
            if ((n.Contains("foot") || n.Contains("ankle")) &&
                !n.Contains("toe") && !n.Contains("roll") && !n.Contains("target")) return 3;
            return 0;
        }

        private static int StepsToAncestor(Transform child, Transform ancestor)
        {
            int steps = 0;
            for (Transform current = child; current != null && steps < 30; current = current.parent, steps++)
                if (current == ancestor) return steps;
            return -1;
        }

        private bool TryFindGenericLeg(int side, out GenericLeg leg)
        {
            leg = new GenericLeg();
            if (fullBody == null || fullBody.bones == null) return false;
            int bestScore = int.MinValue;
            Transform mappedFoot, mappedShin, mappedThigh;
            foreach (Transform foot in fullBody.bones)
            {
                if (foot == null || GetBoneSide(foot.name) != side || GetLegPart(foot.name) != 3 ||
                    !TryGetCopiedBone(foot, out mappedFoot)) continue;
                foreach (Transform shin in fullBody.bones)
                {
                    if (shin == null || GetBoneSide(shin.name) != side || GetLegPart(shin.name) != 2 ||
                        !TryGetCopiedBone(shin, out mappedShin)) continue;
                    foreach (Transform thigh in fullBody.bones)
                    {
                        if (thigh == null || GetBoneSide(thigh.name) != side || GetLegPart(thigh.name) != 1 ||
                            !TryGetCopiedBone(thigh, out mappedThigh)) continue;

                        Vector3 hip = mappedThigh.position;
                        Vector3 knee = mappedShin.position;
                        Vector3 ankle = mappedFoot.position;
                        float upper = Vector3.Distance(hip, knee);
                        float lower = Vector3.Distance(knee, ankle);
                        // Кости выбираются по реальной геометрии, а не по родительским связям Rigify
                        if (upper < .12f || lower < .12f || upper > 1.1f || lower > 1.1f ||
                            Vector3.Dot(hip - knee, copiedRig.up) < .10f ||
                            Vector3.Dot(knee - ankle, copiedRig.up) < .10f) continue;
                        int score = 100;
                        if (thigh.name.StartsWith("DEF-thigh_01", StringComparison.OrdinalIgnoreCase)) score += 40;
                        if (shin.name.StartsWith("DEF-shin_01", StringComparison.OrdinalIgnoreCase)) score += 40;
                        if (foot.name.StartsWith("DEF-foot", StringComparison.OrdinalIgnoreCase)) score += 20;
                        score -= Mathf.RoundToInt(Mathf.Abs(upper - lower) * 20f);
                        if (score <= bestScore) continue;
                        bestScore = score;
                        leg.thigh = mappedThigh;
                        leg.shin = mappedShin;
                        leg.foot = mappedFoot;
                    }
                }
            }
            return leg.thigh != null && leg.shin != null && leg.foot != null;
        }

        private static string LegDescription(GenericLeg leg)
        {
            return leg.thigh.name + " / " + leg.shin.name + " / " + leg.foot.name;
        }

        private struct PoseLegBone
        {
            public Transform bone;
            public Vector3 originalPosition;
            public Quaternion originalRotation;
            public Vector3 targetPosition;
            public Quaternion targetRotation;
            public int hierarchyDepth;
        }

        private static int PoseHierarchyDepth(Transform bone)
        {
            int depth = 0;
            while (bone != null && depth < 150)
            {
                depth++;
                bone = bone.parent;
            }
            return depth;
        }

        private static bool SolveVirtualKnee(Vector3 hip, Vector3 knee, Vector3 ankle,
            Vector3 ankleTarget, Vector3 bendDirection, out Vector3 kneeTarget)
        {
            kneeTarget = knee;
            float upper = Vector3.Distance(hip, knee);
            float lower = Vector3.Distance(knee, ankle);
            Vector3 difference = ankleTarget - hip;
            float length = difference.magnitude;
            if (length < .20f || upper < .12f || lower < .12f ||
                length >= upper + lower - .003f || length <= Mathf.Abs(upper - lower) + .003f)
                return false;
            Vector3 axis = difference / length;
            float along = (upper * upper - lower * lower + length * length) / (2f * length);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 forward = Vector3.ProjectOnPlane(bendDirection, axis);
            if (forward.sqrMagnitude < .0001f) return false;
            kneeTarget = hip + axis * along + forward.normalized * height;
            return true;
        }

        private Transform FindGenericToe(int side, Transform ankle)
        {
            if (fullBody == null || fullBody.bones == null) return null;
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (Transform sourceBone in fullBody.bones)
            {
                if (sourceBone == null || GetBoneSide(sourceBone.name) != side ||
                    sourceBone.name.IndexOf("toe", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Transform mapped;
                if (!TryGetCopiedBone(sourceBone, out mapped) || mapped == null) continue;
                float distance = Vector3.Distance(ankle.position, mapped.position);
                if (distance < .025f || distance > .5f || distance >= bestDistance) continue;
                best = mapped;
                bestDistance = distance;
            }
            return best;
        }

        private Quaternion GetFootDownRotation(GenericLeg leg, int side)
        {
            const float footPitchDegrees = 15f;
            Transform toe = FindGenericToe(side, leg.foot);
            if (toe == null)
            {
                poseDiagnostics.AppendLine("  FOOT PITCH " + side + ": toe bone not found, fixed tilt=" +
                    footPitchDegrees.ToString("F1"));
                return Quaternion.AngleAxis(footPitchDegrees, copiedRig.right);
            }

            Vector3 ankle = leg.foot.position;
            Vector3 toeOffset = toe.position - ankle;
            Quaternion forwardTilt = Quaternion.AngleAxis(footPitchDegrees, copiedRig.right);
            Quaternion backwardTilt = Quaternion.AngleAxis(-footPitchDegrees, copiedRig.right);
            float before = Vector3.Dot(toeOffset, copiedRig.up);
            float forwardHeight = Vector3.Dot(forwardTilt * toeOffset, copiedRig.up);
            float backwardHeight = Vector3.Dot(backwardTilt * toeOffset, copiedRig.up);

            // Выбирается поворот, который опускает носок относительно щиколотки
            Quaternion result = forwardHeight <= backwardHeight ? forwardTilt : backwardTilt;
            float after = Mathf.Min(forwardHeight, backwardHeight);
            poseDiagnostics.AppendLine("  FOOT PITCH " + side +
                " toe=" + toe.name +
                " before=" + before.ToString("F3") +
                " after=" + after.ToString("F3") +
                " angle=" + (forwardHeight <= backwardHeight ? footPitchDegrees : -footPitchDegrees).ToString("F1"));
            return result;
        }

        private bool PlanVirtualLeg(GenericLeg leg, Vector3 footTarget, Vector3 bendDirection,
            int side, List<PoseLegBone> planned)
        {
            Vector3 hip = leg.thigh.position;
            Vector3 knee = leg.shin.position;
            Vector3 ankle = leg.foot.position;
            Vector3 newKnee;
            if (!SolveVirtualKnee(hip, knee, ankle, footTarget, bendDirection, out newKnee))
            {
                poseDiagnostics.AppendLine("  IK " + side + ": skipped - invalid leg geometry");
                return false;
            }

            Quaternion upperSwing = Quaternion.FromToRotation(knee - hip, newKnee - hip);
            Quaternion lowerSwing = Quaternion.FromToRotation(ankle - knee, footTarget - newKnee);
            float hipAngle = Quaternion.Angle(Quaternion.identity, upperSwing);
            float kneeAngle = Quaternion.Angle(Quaternion.identity, lowerSwing);
            poseDiagnostics.AppendLine("  IK " + side + " hipAngle=" + hipAngle.ToString("F1") +
                " kneeAngle=" + kneeAngle.ToString("F1") +
                " hip=" + hip.ToString("F3") + " knee=" + knee.ToString("F3") +
                " foot=" + ankle.ToString("F3") + " target=" + footTarget.ToString("F3"));
            if (hipAngle > 45f || kneeAngle > 60f) return false;

            // Стопа разворачивается вокруг щиколотки после расчёта положения ноги
            Quaternion footPitch = GetFootDownRotation(leg, side);
            foreach (Transform sourceBone in fullBody.bones)
            {
                if (sourceBone == null || GetBoneSide(sourceBone.name) != side) continue;
                string n = sourceBone.name.ToLowerInvariant();
                int part = GetLegPart(sourceBone.name);
                if (part == 0 && (n.Contains("toe") || n.Contains("heel"))) part = 3;
                if (part == 0) continue;
                Transform clone;
                if (!TryGetCopiedBone(sourceBone, out clone)) continue;
                Vector3 pos = clone.position;
                Quaternion rot = clone.rotation;
                PoseLegBone entry = new PoseLegBone();
                entry.bone = clone;
                entry.originalPosition = pos;
                entry.originalRotation = rot;
                entry.hierarchyDepth = PoseHierarchyDepth(clone);
                if (part == 1)
                {
                    entry.targetPosition = hip + upperSwing * (pos - hip);
                    entry.targetRotation = upperSwing * rot;
                }
                else if (part == 2)
                {
                    entry.targetPosition = newKnee + lowerSwing * (pos - knee);
                    entry.targetRotation = lowerSwing * rot;
                }
                else
                {
                    entry.targetPosition = footTarget + footPitch * (pos - ankle);
                    entry.targetRotation = footPitch * rot;
                }
                planned.Add(entry);
            }
            return planned.Count >= 3;
        }

        private void LogGenericBoneCandidates()
        {
            poseDiagnostics.AppendLine("GENERIC LEG BONE CANDIDATES:");
            if (fullBody == null || fullBody.bones == null)
            {
                poseDiagnostics.AppendLine("  no skinned body bones");
                return;
            }
            int count = 0;
            Transform mappedBone;
            foreach (Transform bone in fullBody.bones)
            {
                if (bone == null || GetLegPart(bone.name) == 0 || count >= 48) continue;
                count++;
                poseDiagnostics.AppendLine("  " + bone.name + " side=" + GetBoneSide(bone.name) +
                    " part=" + GetLegPart(bone.name) + " mapped=" +
                    TryGetCopiedBone(bone, out mappedBone));
            }
            poseDiagnostics.AppendLine("  candidates=" + count + " of " + fullBody.bones.Length);
        }

        private void AdjustGenericStandingLegs()
        {
            poseDiagnostics.AppendLine("GENERIC STANDING POSE:");
            LogGenericBoneCandidates();
            GenericLeg left, right;
            if (!TryFindGenericLeg(-1, out left) || !TryFindGenericLeg(1, out right))
            {
                poseDiagnostics.AppendLine("  skipped - no geometrically valid thigh/knee/foot pairs");
                LogLegBoneHierarchy();
                return;
            }
            poseDiagnostics.AppendLine("  LEFT: " + LegDescription(left));
            poseDiagnostics.AppendLine("  RIGHT: " + LegDescription(right));
            if (left.thigh == right.thigh || left.shin == right.shin || left.foot == right.foot)
            {
                poseDiagnostics.AppendLine("  skipped - shared leg bones");
                return;
            }

            Vector3 sideways = copiedRig.right;
            Vector3 forward = copiedRig.forward;
            Vector3 up = copiedRig.up;
            Vector3 leftFoot = left.foot.position;
            Vector3 rightFoot = right.foot.position;
            float rigScale = Mathf.Clamp(copiedRig.lossyScale.y, .5f, 1.5f);
            float widthBefore = Mathf.Abs(Vector3.Dot(leftFoot - rightFoot, sideways));
            float preferredWidth = .27f * rigScale;
            float targetWidth = widthBefore > preferredWidth + .015f ? preferredWidth : widthBefore;

            Vector3 midpoint = (leftFoot + rightFoot) * .5f;
            Vector3 hipMidpoint = (left.thigh.position + right.thigh.position) * .5f;
            float depthBefore = Vector3.Dot(midpoint - hipMidpoint, forward);
            float desiredDepth = -.02f * rigScale;
            float depthShift = Mathf.Clamp(desiredDepth - depthBefore, -.25f * rigScale, .25f * rigScale);
            float targetDepth = depthBefore + depthShift;
            poseDiagnostics.AppendLine("  FOOT WIDTH BEFORE=" + widthBefore.ToString("F3") +
                " TARGET=" + targetWidth.ToString("F3"));
            poseDiagnostics.AppendLine("  FOOT DEPTH BEFORE=" + depthBefore.ToString("F3") +
                " TARGET=" + targetDepth.ToString("F3") +
                " SHIFT=" + depthShift.ToString("F3"));
            if (Mathf.Abs(widthBefore - targetWidth) <= .015f && Mathf.Abs(depthShift) <= .025f * rigScale)
            {
                poseDiagnostics.AppendLine("  skipped - feet already below hips");
                return;
            }

            // Ступни располагаются под тазом вместо смещения вперёд
            midpoint += forward * depthShift;
            float sign = Mathf.Sign(Vector3.Dot(leftFoot - rightFoot, sideways));
            if (sign == 0f) sign = -1f;
            Vector3 leftTarget = midpoint + sideways * (sign * targetWidth * .5f);
            Vector3 rightTarget = midpoint - sideways * (sign * targetWidth * .5f);
            leftTarget += up * .006f;
            rightTarget += up * .006f;

            List<PoseLegBone> planned = new List<PoseLegBone>();
            if (!PlanVirtualLeg(left, leftTarget, forward, -1, planned) ||
                !PlanVirtualLeg(right, rightTarget, forward, 1, planned))
            {
                poseDiagnostics.AppendLine("  skipped - unsafe virtual IK targets");
                return;
            }
            planned.Sort((a, b) => a.hierarchyDepth.CompareTo(b.hierarchyDepth));
            poseDiagnostics.AppendLine("  VIRTUAL BONES=" + planned.Count);
            try
            {
                // Позиции применяются сверху вниз, чтобы не смещались дочерние кости
                foreach (PoseLegBone change in planned)
                {
                    change.bone.position = change.targetPosition;
                    change.bone.rotation = change.targetRotation;
                }
                float widthAfter = Mathf.Abs(Vector3.Dot(left.foot.position - right.foot.position, sideways));
                float depthAfter = Vector3.Dot(
                    (left.foot.position + right.foot.position) * .5f - hipMidpoint, forward);
                poseDiagnostics.AppendLine("  FOOT WIDTH AFTER=" + widthAfter.ToString("F3"));
                poseDiagnostics.AppendLine("  FOOT DEPTH AFTER=" + depthAfter.ToString("F3"));
                if (Mathf.Abs(widthAfter - targetWidth) <= .035f * rigScale &&
                    Mathf.Abs(depthAfter - targetDepth) <= .035f * rigScale)
                {
                    poseDiagnostics.AppendLine("  IK APPLIED");
                    return;
                }
                poseDiagnostics.AppendLine("  IK REVERTED - foot target not reached");
            }
            catch (Exception ex)
            {
                poseDiagnostics.AppendLine("  IK REVERTED - " + ex.Message);
            }
            foreach (PoseLegBone change in planned)
            {
                change.bone.position = change.originalPosition;
                change.bone.rotation = change.originalRotation;
            }
        }

        private void LogLegBoneHierarchy()
        {
            if (fullBody == null || fullBody.bones == null) return;
            foreach (Transform bone in fullBody.bones)
            {
                if (bone == null || GetLegPart(bone.name) == 0) continue;
                Transform mapped;
                bool found = TryGetCopiedBone(bone, out mapped);
                poseDiagnostics.AppendLine("  BONE " + bone.name +
                    " parent=" + (bone.parent == null ? "NULL" : bone.parent.name) +
                    " copiedParent=" + (!found || mapped.parent == null ? "NULL" : mapped.parent.name) +
                    " pos=" + (!found ? "NULL" : mapped.position.ToString("F3")));
            }
        }

        private void LogAnimatorState(string stage, Animator anim, bool listClips)
        {
            poseDiagnostics.AppendLine("ANIMATOR " + stage + ":");
            if (anim == null) { poseDiagnostics.AppendLine("  null"); return; }
            try
            {
                Avatar avatar = anim.avatar;
                poseDiagnostics.AppendLine("  name=" + anim.name + " enabled=" + anim.enabled +
                    " controller=" + (anim.runtimeAnimatorController == null ? "NULL" : anim.runtimeAnimatorController.name));
                poseDiagnostics.AppendLine("  avatar=" + (avatar == null ? "NULL" : avatar.name) +
                    " valid=" + (avatar != null && avatar.isValid) +
                    " humanoid=" + (avatar != null && avatar.isHuman));
                poseDiagnostics.AppendLine("  initialized=" + anim.isInitialized + " layers=" + anim.layerCount);
                if (anim.isInitialized && anim.layerCount > 0)
                {
                    AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
                    poseDiagnostics.AppendLine("  state hash=" + info.fullPathHash +
                        " normalized=" + info.normalizedTime.ToString("F3") +
                        " inTransition=" + anim.IsInTransition(0));
                    foreach (AnimatorClipInfo clip in anim.GetCurrentAnimatorClipInfo(0))
                        poseDiagnostics.AppendLine("  ACTIVE CLIP=" + (clip.clip == null ? "NULL" : clip.clip.name) +
                            " weight=" + clip.weight.ToString("F3"));
                }
                if (listClips && anim.runtimeAnimatorController != null)
                {
                    AnimationClip[] clips = anim.runtimeAnimatorController.animationClips;
                    poseDiagnostics.AppendLine("  AVAILABLE CLIPS=" + (clips == null ? 0 : clips.Length));
                    if (clips != null)
                        for (int i = 0; i < clips.Length && i < 120; i++)
                            if (clips[i] != null) poseDiagnostics.AppendLine("    " + clips[i].name);
                }
            }
            catch (Exception ex)
            {
                poseDiagnostics.AppendLine("  ANIMATOR READ ERROR=" + ex);
            }
        }

        private void ApplyFixedHookPose(Network_Player player, CharacterModelModifications model)
        {
            Animator source = player.Animator.anim;
            if (source == null || model.thirdPersonController == null)
            {
                poseDiagnostics.AppendLine("FIXED POSE: SKIPPED (missing source or third person controller)");
                return;
            }

            Transform animatorRoot;
            if (!copies.TryGetValue(source.transform, out animatorRoot) || animatorRoot == null)
            {
                poseDiagnostics.AppendLine("FIXED POSE: SKIPPED (source animator transform not mapped)");
                return;
            }

            Animator animator = null;
            try
            {
                // Отдельный аниматор приводит скелет превью к одной позе крюка
                animator = animatorRoot.gameObject.AddComponent<Animator>();
                animator.avatar = source.avatar;
                animator.runtimeAnimatorController = model.thirdPersonController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                LogAnimatorState("PREVIEW BEFORE PARAMS", animator, false);

                if (HasAnimatorParameter(animator, "ItemID", AnimatorControllerParameterType.Int))
                    animator.SetInteger("ItemID", 0);
                if (HasAnimatorParameter(animator, "FullBodyIndex", AnimatorControllerParameterType.Int))
                    animator.SetInteger("FullBodyIndex", 0);
                if (HasAnimatorParameter(animator, "HookInHand", AnimatorControllerParameterType.Bool))
                    animator.SetBool("HookInHand", true);
                if (HasAnimatorParameter(animator, "Crouching", AnimatorControllerParameterType.Bool))
                    animator.SetBool("Crouching", false);
                foreach (string velocity in new[] { "VelocityX", "VelocityY", "VelocityZ", "VelocityW" })
                    if (HasAnimatorParameter(animator, velocity, AnimatorControllerParameterType.Float))
                        animator.SetFloat(velocity, 0f);
                if (HasAnimatorParameter(animator, "Switch", AnimatorControllerParameterType.Trigger))
                    animator.SetTrigger("Switch");

                // Переходы завершаются до создания рендереров
                for (int i = 0; i < 15; i++) animator.Update(0.1f);
                LogAnimatorState("PREVIEW ANIMATION SETTLED", animator, false);
                animator.enabled = false;
                AdjustGenericStandingLegs();
            }
            catch (Exception ex)
            {
                poseDiagnostics.AppendLine("FIXED POSE ERROR=" + ex);
            }
            finally
            {
                // Дальнейшие игровые анимации на превью не действуют
                if (animator != null)
                {
                    animator.enabled = false;
                    UnityEngine.Object.Destroy(animator);
                }
            }
        }

        private static bool IsHeldObject(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string n = current.name.ToLowerInvariant();
                if (n.Contains("notebook") || n.Contains("note_book") ||
                    n.Contains("handitem") || n.Contains("helditem") ||
                    n.Contains("book") || n.Contains("equipment") ||
                    n.Contains("leftitem") || n.Contains("rightitem") ||
                    n.Contains("itemholder") || n.Contains("equipitem"))
                    return true;
                if (current.GetComponent<CharacterModelModifications>() != null) break;
            }
            return false;
        }

        private bool IsWithin(Transform target, Transform ancestor)
        {
            return target != null && ancestor != null &&
                (target == ancestor || target.IsChildOf(ancestor));
        }

        private static bool HasVisualMesh(Transform root)
        {
            if (root == null) return false;
            if (root.GetComponentInChildren<MeshRenderer>(true) != null) return true;
            return root.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
        }

        private static Transform GetHookVisual(Hook hook, string fieldName)
        {
            if (hook == null) return null;
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var field = typeof(Hook).GetField(fieldName, flags);
            if (field == null) return null;
            GameObject visual = field.GetValue(hook) as GameObject;
            return visual != null && HasVisualMesh(visual.transform) ? visual.transform : null;
        }

        private void FindHookVisuals(Network_Player player)
        {
            // Сначала используются детали крюка своего персонажа
            foreach (Hook hook in player.GetComponentsInChildren<Hook>(true))
            {
                if (leftHandRopeRoot == null)
                    leftHandRopeRoot = GetHookVisual(hook, "leftHandRopeModel");
                if (rightHandHookRoot == null)
                    rightHandHookRoot = GetHookVisual(hook, "hookMesh");
                if (leftHandRopeRoot != null && rightHandHookRoot != null) return;
            }

            // Недостающие детали извлекаются из загруженных префабов
            foreach (Hook hook in Resources.FindObjectsOfTypeAll<Hook>())
            {
                if (leftHandRopeRoot == null)
                    leftHandRopeRoot = GetHookVisual(hook, "leftHandRopeModel");
                if (rightHandHookRoot == null)
                    rightHandHookRoot = GetHookVisual(hook, "hookMesh");
                if (leftHandRopeRoot != null && rightHandHookRoot != null) break;
            }
        }

        private static Vector3 DivideScale(Vector3 value, Vector3 parent)
        {
            return new Vector3(
                Mathf.Abs(parent.x) > .0001f ? value.x / parent.x : value.x,
                Mathf.Abs(parent.y) > .0001f ? value.y / parent.y : value.y,
                Mathf.Abs(parent.z) > .0001f ? value.z / parent.z : value.z);
        }

        private void CopyHandVisual(Transform visual, Transform holder, string name)
        {
            if (visual == null || holder == null) return;
            Transform holderCopy;
            if (!copies.TryGetValue(holder, out holderCopy) || holderCopy == null) return;

            Transform visualCopy = new GameObject(name).transform;
            visualCopy.gameObject.layer = PreviewLayer;
            visualCopy.SetParent(holderCopy, false);

            if (visual == holder || visual.IsChildOf(holder))
            {
                visualCopy.localPosition = holder.InverseTransformPoint(visual.position);
                visualCopy.localRotation = Quaternion.Inverse(holder.rotation) * visual.rotation;
                visualCopy.localScale = DivideScale(visual.lossyScale, holder.lossyScale);
            }
            else
            {
                // Для префаба используется локальное смещение модели
                visualCopy.localPosition = visual.localPosition;
                visualCopy.localRotation = visual.localRotation;
                visualCopy.localScale = visual.localScale;
                if (visualCopy.localPosition.sqrMagnitude > .25f)
                    visualCopy.localPosition = Vector3.zero;
            }

            CopyItemVisualTree(visual, visualCopy, 0);
        }

        private void CopyItemVisualTree(Transform original, Transform target, int depth)
        {
            if (original == null || target == null || depth > 15 || rendererCount >= 80) return;

            SkinnedMeshRenderer skinned = original.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null)
            {
                // Отключённая игровая модель сохраняет привязку к костям превью
                if (CopyLiveSkinFallback(skinned, target) || TryBakeSkin(skinned, target))
                    rendererCount++;
                else
                    poseDiagnostics.AppendLine("EQUIPMENT SKIN FAILED: " + original.name);
            }
            else
            {
                MeshFilter filter = original.GetComponent<MeshFilter>();
                MeshRenderer meshRenderer = original.GetComponent<MeshRenderer>();
                if (filter != null && filter.sharedMesh != null && meshRenderer != null)
                {
                    target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    MeshRenderer renderCopy = target.gameObject.AddComponent<MeshRenderer>();
                    renderCopy.sharedMaterials = meshRenderer.sharedMaterials;
                    renderCopy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderCopy.receiveShadows = false;
                    renderCopy.enabled = true;
                    rendererCount++;
                }
            }

            foreach (Transform child in original)
            {
                if (child == null || rendererCount >= 80) continue;
                Transform childCopy = new GameObject(child.name).transform;
                childCopy.gameObject.layer = PreviewLayer;
                childCopy.SetParent(target, false);
                childCopy.localPosition = child.localPosition;
                childCopy.localRotation = child.localRotation;
                childCopy.localScale = child.localScale;
                CopyItemVisualTree(child, childCopy, depth + 1);
            }
        }

        private static Transform GetEquipmentVisual(Equipment_Model equipment)
        {
            if (equipment == null) return null;
            try
            {
                // Для превью нужен меш от третьего лица, даже если он выключен
                Transform remote = EquipmentRemoteModel != null
                    ? EquipmentRemoteModel.GetValue(equipment) as Transform : null;
                if (HasVisualMesh(remote)) return remote;
                Transform local = EquipmentLocalModel != null
                    ? EquipmentLocalModel.GetValue(equipment) as Transform : null;
                return HasVisualMesh(local) ? local : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string EquipmentModelDescription(Equipment_Model item)
        {
            try
            {
                Transform remote = EquipmentRemoteModel != null
                    ? EquipmentRemoteModel.GetValue(item) as Transform : null;
                Transform local = EquipmentLocalModel != null
                    ? EquipmentLocalModel.GetValue(item) as Transform : null;
                return "remote=" + (remote == null ? "NULL" : remote.name) +
                    " local=" + (local == null ? "NULL" : local.name);
            }
            catch (Exception)
            {
                return "model fields unavailable";
            }
        }

        private void SyncEquipmentVisuals()
        {
            if (equipmentPlayer == null || copiedRig == null) return;
            PlayerEquipment equipment = equipmentPlayer.PlayerEquipment;
            if (equipment == null) return;
            bool hideForHazmat = equipmentPlayer.HazmatSuit != null &&
                equipmentPlayer.HazmatSuit.Equipped;

            foreach (Equipment_Model item in equipment.GetComponentsInChildren<Equipment_Model>(true))
            {
                if (item == null) continue;
                bool visible = item.Equipped && !hideForHazmat;
                GameObject existing;
                if (equipmentVisuals.TryGetValue(item, out existing))
                {
                    if (existing != null && existing.activeSelf != visible)
                        existing.SetActive(visible);
                    continue;
                }
                if (!visible) continue;

                Transform source = GetEquipmentVisual(item);
                if (source == null || source == modelRoot || source == sourceRoot)
                {
                    if (missingEquipmentVisuals.Add(item))
                    {
                        string detail = "EQUIPMENT MISSING: " + item.GetType().Name +
                            " / " + item.name + " " + EquipmentModelDescription(item);
                        poseDiagnostics.AppendLine(detail);
                        Debug.LogWarning("[xSkills Preview] " + detail);
                    }
                    continue;
                }

                GameObject visual = CreateEquipmentVisual(source);
                if (visual != null)
                {
                    equipmentVisuals.Add(item, visual);
                    missingEquipmentVisuals.Remove(item);
                    string detail = "EQUIPMENT ADDED: " + item.GetType().Name +
                        " model=" + source.name + " meshCount=" +
                        visual.GetComponentsInChildren<Renderer>(true).Length +
                        " " + EquipmentModelDescription(item);
                    poseDiagnostics.AppendLine(detail);
                }
                else if (missingEquipmentVisuals.Add(item))
                {
                    string detail = "EQUIPMENT COPY FAILED: " + item.GetType().Name +
                        " / " + item.name + " " + EquipmentModelDescription(item);
                    poseDiagnostics.AppendLine(detail);
                    Debug.LogWarning("[xSkills Preview] " + detail);
                }
            }
        }

        private GameObject CreateEquipmentVisual(Transform source)
        {
            if (source == null || copiedRig == null || sourceRoot == null) return null;
            Transform sourceAnchor = source;
            Transform targetAnchor = null;
            while (sourceAnchor != null)
            {
                if (TryGetCopiedBone(sourceAnchor, out targetAnchor)) break;
                sourceAnchor = sourceAnchor.parent;
            }

            Transform visual = new GameObject("xSkills Equipment " + source.name).transform;
            visual.gameObject.layer = PreviewLayer;
            visual.SetParent(targetAnchor != null ? targetAnchor : copiedRig, false);

            if (targetAnchor != null && sourceAnchor != null)
            {
                visual.localPosition = sourceAnchor.InverseTransformPoint(source.position);
                visual.localRotation = Quaternion.Inverse(sourceAnchor.rotation) * source.rotation;
                visual.localScale = DivideScale(source.lossyScale, sourceAnchor.lossyScale);
            }
            else
            {
                // Для внешнего префаба применяется привязка к исходному скелету
                visual.localPosition = sourceRoot.InverseTransformPoint(source.position);
                visual.localRotation = Quaternion.Inverse(sourceRoot.rotation) * source.rotation;
                visual.localScale = DivideScale(source.lossyScale, sourceRoot.lossyScale);
            }

            int previousCount = rendererCount;
            CopyItemVisualTree(source, visual, 0);
            if (rendererCount > previousCount) return visual.gameObject;
            UnityEngine.Object.Destroy(visual.gameObject);
            return null;
        }

        private void SyncOutfitMaterials()
        {
            if (equipmentPlayer == null || equipmentCharacterModel == null ||
                equipmentPlayer.characterSettings == null) return;
            int index = equipmentPlayer.characterSettings.OutfitIndex;
            if (index == appliedOutfitIndex) return;
            Outfit[] outfits = equipmentCharacterModel.outfits;
            if (outfits == null || index < 0 || index >= outfits.Length || outfits[index] == null)
                return;

            Outfit outfit = outfits[index];
            Transform bodyTarget;
            if (!hazmatActive && outfit.bodyMaterial != null && fullBody != null &&
                copies.TryGetValue(fullBody.transform, out bodyTarget))
            {
                Renderer body = bodyTarget.GetComponent<SkinnedMeshRenderer>();
                if (body == null) body = bodyTarget.GetComponent<MeshRenderer>();
                SetPreviewMaterial(body, outfit.bodyMaterial);
            }
            if (outfit.hairMaterial != null)
            {
                foreach (KeyValuePair<Transform, Transform> pair in copies)
                {
                    SkinnedMeshRenderer skin = pair.Key.GetComponent<SkinnedMeshRenderer>();
                    if (skin == null ||
                        !(sourceHair.Contains(skin) || IsSelectedHair(pair.Key))) continue;
                    Renderer output = pair.Value.GetComponent<SkinnedMeshRenderer>();
                    if (output == null) output = pair.Value.GetComponent<MeshRenderer>();
                    SetPreviewMaterial(output, outfit.hairMaterial);
                }
            }
            appliedOutfitIndex = index;
            string detail = "OUTFIT: index=" + index + " body=" +
                (outfit.bodyMaterial == null ? "NULL" : outfit.bodyMaterial.name);
            poseDiagnostics.AppendLine(detail);
        }

        private static void SetPreviewMaterial(Renderer renderer, Material selected)
        {
            if (renderer == null || selected == null) return;
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return;
            if (materials[0] == selected) return;
            materials[0] = selected;
            renderer.sharedMaterials = materials;
        }

        private void CopyVisuals()
        {
            foreach (KeyValuePair<Transform, Transform> pair in copies)
            {
                Transform original = pair.Key;
                if (rendererCount >= 80) continue;
                if (hatParent != null && IsWithin(original, hatParent)) continue;
                if (!IsWithin(original, modelRoot) || IsHeldObject(original)) continue;

                SkinnedMeshRenderer skin = original.GetComponent<SkinnedMeshRenderer>();
                if (skin != null && skin.sharedMesh != null)
                {
                    if (skin == firstPersonArms || IsWithin(original, hazmatArms)) continue;
                    bool isBody = skin == fullBody;
                    bool isSuitBody = IsWithin(original, hazmatBody);
                    bool hair = sourceHair.Contains(skin) || IsSelectedHair(original);
                    if (isBody && hazmatActive) continue;
                    if (isSuitBody && !hazmatActive) continue;
                    if (!isBody && !isSuitBody && !hair) continue;

                    // Тело и волосы используют неподвижный скелет превью
                    bool copied = CopyLiveSkinFallback(skin, pair.Value) ||
                        TryBakeSkin(skin, pair.Value);
                    if (copied) rendererCount++;
                    if (isBody || isSuitBody)
                        poseDiagnostics.AppendLine("BODY RENDERER=" + original.name +
                            " mode=" + (pair.Value.GetComponent<SkinnedMeshRenderer>() != null ? "LIVE" : "BAKED") +
                            " copied=" + copied);
                    continue;
                }

                MeshFilter filter = original.GetComponent<MeshFilter>();
                MeshRenderer sourceMesh = original.GetComponent<MeshRenderer>();
                if (sourceMesh != null && filter != null && filter.sharedMesh != null &&
                    IsSelectedHair(original))
                {
                    if (IsWithin(original, hazmatArms) ||
                        (IsWithin(original, hazmatBody) && !hazmatActive)) continue;
                    pair.Value.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    MeshRenderer output = pair.Value.gameObject.AddComponent<MeshRenderer>();
                    output.sharedMaterials = sourceMesh.sharedMaterials;
                    output.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    output.receiveShadows = false;
                    output.enabled = true;
                    rendererCount++;
                }
            }
        }

        private bool TryBakeSkin(SkinnedMeshRenderer source, Transform target)
        {
            Mesh snapshot = null;
            try
            {
                // BakeMesh сохраняет уже деформированную модель и устраняет растяжение из-за костей
                snapshot = new Mesh();
                snapshot.name = "xSkills " + source.name + " Snapshot";
                source.BakeMesh(snapshot);
                if (snapshot.vertexCount == 0 || snapshot.bounds.size.sqrMagnitude < .0000001f)
                {
                    UnityEngine.Object.Destroy(snapshot);
                    return false;
                }
                MeshFilter filter = target.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = snapshot;
                MeshRenderer output = target.gameObject.AddComponent<MeshRenderer>();
                output.sharedMaterials = source.sharedMaterials;
                output.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                output.receiveShadows = false;
                output.enabled = true;
                bakedMeshes.Add(snapshot);
                return true;
            }
            catch (Exception)
            {
                if (snapshot != null) UnityEngine.Object.Destroy(snapshot);
                return false;
            }
        }

        private Transform MapHairBone(Transform sourceBone, int depth)
        {
            if (sourceBone == null || depth > 120) return null;

            Transform mapped;
            if (copies.TryGetValue(sourceBone, out mapped) && mapped != null)
                return mapped;
            if (extraHairBones.TryGetValue(sourceBone, out mapped) && mapped != null)
                return mapped;

            // Дополнительные кости восстанавливаются отдельно от общего словаря
            // Список рендереров при этом не изменяется
            bool insideRig = sourceRoot != null && sourceBone.IsChildOf(sourceRoot);
            Transform parent = insideRig
                ? MapHairBone(sourceBone.parent, depth + 1)
                : copiedRig;
            if (parent == null) return null;

            Transform proxy = new GameObject("xSkills Hair Bone " + sourceBone.name).transform;
            proxy.gameObject.layer = PreviewLayer;
            proxy.SetParent(parent, false);
            if (insideRig)
            {
                proxy.localPosition = sourceBone.localPosition;
                proxy.localRotation = sourceBone.localRotation;
                proxy.localScale = sourceBone.localScale;
            }
            else
            {
                // Внешняя кость переводится в координаты корня аниматора
                proxy.localPosition = sourceRoot.InverseTransformPoint(sourceBone.position);
                proxy.localRotation = Quaternion.Inverse(sourceRoot.rotation) * sourceBone.rotation;
                proxy.localScale = Vector3.one;
            }
            extraHairBones[sourceBone] = proxy;
            return proxy;
        }

        private bool CopyLiveSkinFallback(SkinnedMeshRenderer source, Transform target)
        {
            if (source == null || target == null || source.sharedMesh == null) return false;
            try
            {
                Transform[] sourceBones = source.bones;
                if (sourceBones == null || sourceBones.Length == 0) return false;
                Transform mappedRoot = source.rootBone != null
                    ? MapHairBone(source.rootBone, 0) : null;
                Transform[] mappedBones = new Transform[sourceBones.Length];
                for (int i = 0; i < mappedBones.Length; i++)
                {
                    // Для пустого элемента скелета используется корневая кость
                    mappedBones[i] = sourceBones[i] != null
                        ? MapHairBone(sourceBones[i], 0) : mappedRoot;
                    if (mappedBones[i] == null) return false;
                }
                if (mappedRoot == null) mappedRoot = mappedBones[0];

                // Выключенный в первом лице меш волос получает независимую
                // привязку к копии скелета и отображается без BakeMesh
                SkinnedMeshRenderer copy = target.gameObject.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = source.sharedMesh;
                copy.sharedMaterials = source.sharedMaterials;
                copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                copy.receiveShadows = false;
                copy.rootBone = mappedRoot;
                copy.bones = mappedBones;
                copy.localBounds = source.localBounds;
                copy.updateWhenOffscreen = true;
                copy.enabled = true;
                for (int i = 0; i < source.sharedMesh.blendShapeCount; i++)
                    copy.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));

                // Для скрытого в первом лице стиля восстанавливается нужный blend shape
                CharacterModelModifications model = sourceRoot != null
                    ? sourceRoot.GetComponentInChildren<CharacterModelModifications>(true) : null;
                if (model != null && model.hairStyles != null)
                {
                    foreach (HairStyleConnection style in model.hairStyles)
                    {
                        if (style == null || style.hairStyle != model.CurrentHairStyle ||
                            style.blendShapeRenderer != source) continue;
                        if (style.blendShapeIndex >= 0 &&
                            style.blendShapeIndex < source.sharedMesh.blendShapeCount)
                            copy.SetBlendShapeWeight(style.blendShapeIndex, style.blendShapeValue);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryGetFullBodyBounds(out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            if (fullBody == null) return false;

            Transform bodyCopy;
            if (!copies.TryGetValue(fullBody.transform, out bodyCopy) || bodyCopy == null)
                return false;

            // Границы рассчитываются по геометрии тела, а не по вспомогательным мешам
            MeshFilter filter = bodyCopy.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Bounds local = filter.sharedMesh.bounds;
                Vector3 min = local.min;
                Vector3 max = local.max;
                bool initialized = false;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 corner = bodyCopy.TransformPoint(new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z));
                            if (!initialized)
                            {
                                bounds = new Bounds(corner, Vector3.zero);
                                initialized = true;
                            }
                            else bounds.Encapsulate(corner);
                        }
                    }
                }
                if (initialized && bounds.size.y > .6f && bounds.size.y < 5f)
                    return true;
            }

            Renderer renderer = bodyCopy.GetComponent<Renderer>();
            if (renderer != null && renderer.bounds.size.y > .6f && renderer.bounds.size.y < 5f)
            {
                bounds = renderer.bounds;
                return true;
            }
            return false;
        }

        private void FitFullBody()
        {
            Bounds bodyBounds;
            bool fromBody = TryGetFullBodyBounds(out bodyBounds);
            Bounds drawnBounds = new Bounds(sceneRoot.transform.position, Vector3.zero);
            bool haveDrawn = false;
            foreach (Renderer renderer in copiedRig.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                Bounds part = renderer.bounds;
                if (part.size.sqrMagnitude < .00001f || part.size.magnitude > 8f) continue;
                if (!haveDrawn) { drawnBounds = part; haveDrawn = true; }
                else drawnBounds.Encapsulate(part);
            }
            if (!fromBody)
            {
                bodyBounds = haveDrawn ? drawnBounds :
                    new Bounds(copiedRig.position + Vector3.up * .9f,
                        new Vector3(.8f, 1.8f, .6f));
            }

            // Верх тела вычисляется по кости головы, а не по мешу одежды
            Vector3 headPosition = Vector3.zero;
            Transform copiedHead = null;
            bool headFound = sourceHeadBone != null &&
                copies.TryGetValue(sourceHeadBone, out copiedHead) && copiedHead != null;
            if (headFound) headPosition = copiedHead.position;

            float feetY = bodyBounds.min.y;
            float topY = bodyBounds.max.y;
            if (headFound)
            {
                float headMargin = Mathf.Clamp(bodyBounds.size.y * .11f, .15f, .25f);
                float proposedTop = Mathf.Max(topY, headPosition.y + headMargin);
                float proposedHeight = proposedTop - feetY;
                if (proposedHeight >= 1.0f && proposedHeight <= 3.6f)
                {
                    topY = proposedTop;
                }
            }
            if (topY - feetY < 1.20f)
            {
                // Резервное значение не позволяет кадрировать персонажа по одним ногам
                topY = feetY + 1.85f;
            }

            // Вращение происходит вокруг середины роста персонажа
            Vector3 center = new Vector3(bodyBounds.center.x, (feetY + topY) * .5f,
                bodyBounds.center.z);
            turntable.position = center;
            copiedRig.SetParent(turntable, true);

            float height = topY - feetY;
            float aspect = (float)texture.width / texture.height;
            float halfWidth = Mathf.Max(.37f,
                Mathf.Sqrt(bodyBounds.extents.x * bodyBounds.extents.x +
                           bodyBounds.extents.z * bodyBounds.extents.z));
            if (haveDrawn && drawnBounds.size.x < 3f && drawnBounds.size.z < 3f)
                halfWidth = Mathf.Max(halfWidth,
                    Mathf.Sqrt(drawnBounds.extents.x * drawnBounds.extents.x +
                               drawnBounds.extents.z * drawnBounds.extents.z));

            // Увеличение 1.30x относительно базового кадра с запасом
            float baseline = Mathf.Max(height * .5f / .74f,
                halfWidth / (aspect * .74f));
            float zoomed = baseline / PreviewZoom;
            // Голова и ноги не должны выходить за пределы области превью
            float fitLimit = Mathf.Max(height * .5f / .958f,
                halfWidth / (aspect * .95f));
            camera.orthographicSize = Mathf.Max(zoomed, fitLimit);
            float distance = Mathf.Max(4f, bodyBounds.extents.z + 4f);
            camera.transform.position = center + Vector3.forward * distance;
            camera.transform.LookAt(center);

        }

        // Фактический силуэт используется вместо ненадёжных границ костей
        private struct PixelFrame
        {
            public int MinX, MinY, MaxX, MaxY, Count;
        }

        private bool MeasureSilhouette(Texture2D probe, out PixelFrame frame)
        {
            frame = new PixelFrame();
            if (camera == null || texture == null) return false;

            RenderTexture previous = RenderTexture.active;
            RenderTexture resolved = null;
            try
            {
                camera.Render();
                // MSAA-изображение предварительно разрешается перед чтением CPU
                resolved = RenderTexture.GetTemporary(texture.width, texture.height,
                    0, RenderTextureFormat.ARGB32);
                Graphics.Blit(texture, resolved);
                RenderTexture.active = resolved;
                probe.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                probe.Apply(false, false);
                Color32[] pixels = probe.GetPixels32();
                int width = texture.width;
                int height = texture.height;
                frame.MinX = width;
                frame.MinY = height;
                frame.MaxX = -1;
                frame.MaxY = -1;

                // Каждый второй пиксель достаточен для рамки 512x768
                for (int y = 0; y < height; y += 2)
                {
                    int offset = y * width;
                    for (int x = 0; x < width; x += 2)
                    {
                        if (pixels[offset + x].a < 42) continue;
                        if (x < frame.MinX) frame.MinX = x;
                        if (x > frame.MaxX) frame.MaxX = x;
                        if (y < frame.MinY) frame.MinY = y;
                        if (y > frame.MaxY) frame.MaxY = y;
                        frame.Count++;
                    }
                }
                return frame.Count > 80 && frame.MaxX > frame.MinX && frame.MaxY > frame.MinY;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (resolved != null) RenderTexture.ReleaseTemporary(resolved);
            }
        }

        private void RecenterTurntable(Vector3 shift)
        {
            if (turntable == null || copiedRig == null) return;
            // Переносится только ось вращения, а не сама модель
            copiedRig.SetParent(sceneRoot.transform, true);
            turntable.position += shift;
            copiedRig.SetParent(turntable, true);
        }

        private void FitFromRenderedSilhouette()
        {
            if (camera == null || texture == null || copiedRig == null) return;
            Texture2D probe = null;
            try
            {
                probe = new Texture2D(texture.width, texture.height,
                    TextureFormat.RGBA32, false);
                probe.name = "xSkills Framing Probe";
                int width = texture.width;
                int height = texture.height;
                float aspect = (float)width / height;

                // На первом кадре показывается большой участок сцены, включая голову
                camera.orthographicSize = Mathf.Max(3.0f, camera.orthographicSize);
                PixelFrame pixels;
                bool detected = MeasureSilhouette(probe, out pixels);
                if (!detected)
                {
                    // Первоначальный центр мог находиться за пределами модельной сетки
                    Vector3 fallback = copiedRig.position + Vector3.up * 0.9f;
                    camera.transform.position = fallback + Vector3.forward * 5f;
                    camera.transform.LookAt(fallback);
                    detected = MeasureSilhouette(probe, out pixels);
                }
                if (!detected)
                {
                    return;
                }

                // Пока пиксели упираются в край, объект ещё не целиком в кадре
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    bool atTop = pixels.MaxY >= height - 10;
                    bool atBottom = pixels.MinY <= 10;
                    bool atLeft = pixels.MinX <= 10;
                    bool atRight = pixels.MaxX >= width - 10;
                    if (!atTop && !atBottom && !atLeft && !atRight) break;
                    if (atTop && atBottom || atLeft && atRight)
                    {
                        camera.orthographicSize *= 1.5f;
                    }
                    else
                    {
                        float vertical = atTop ? camera.orthographicSize * .42f :
                            atBottom ? -camera.orthographicSize * .42f : 0f;
                        float horizontal = atRight ? camera.orthographicSize * aspect * .42f :
                            atLeft ? -camera.orthographicSize * aspect * .42f : 0f;
                        camera.transform.position += camera.transform.up * vertical +
                            camera.transform.right * horizontal;
                    }
                    if (!MeasureSilhouette(probe, out pixels)) break;
                }

                // Центр видимого силуэта переводится из пикселей в мировые единицы
                float midX = (pixels.MinX + pixels.MaxX) * .5f;
                float midY = (pixels.MinY + pixels.MaxY) * .5f;
                float worldX = ((midX / (width - 1f)) - .5f) *
                    2f * camera.orthographicSize * aspect;
                float worldY = ((midY / (height - 1f)) - .5f) *
                    2f * camera.orthographicSize;
                Vector3 shift = camera.transform.right * worldX + camera.transform.up * worldY;
                camera.transform.position += shift;
                RecenterTurntable(shift);

                float pixelWidth = (pixels.MaxX - pixels.MinX + 2f) / width;
                float pixelHeight = (pixels.MaxY - pixels.MinY + 2f) / height;
                float initialSize = camera.orthographicSize;
                float sizeFactor = Mathf.Max(pixelHeight / .87f, pixelWidth / .78f);
                // В кадре остаётся тело нормального роста, а не увеличенные ноги
                float finalSize = Mathf.Max(.98f, initialSize * sizeFactor);
                camera.orthographicSize = Mathf.Clamp(finalSize, .98f, 8f);

            }
            catch (Exception)
            {
            }
            finally
            {
                if (probe != null) UnityEngine.Object.Destroy(probe);
            }
        }

        public void Render()
        {
            if (!Available) return;
            if (Time.unscaledTime < nextRenderAt)
            {
                camera.enabled = false;
                return;
            }
            nextRenderAt = Time.unscaledTime + RenderInterval;
            if (Time.unscaledTime >= nextEquipmentUpdate)
            {
                nextEquipmentUpdate = Time.unscaledTime + .25f;
                SyncEquipmentVisuals();
                SyncOutfitMaterials();
            }
            try
            {
                float elapsed = Time.unscaledTime - createdAt;
                float yaw = Mathf.Sin(elapsed * TurnSpeed) * MaxTurnDegrees;
                if (turntable != null) turntable.localRotation = Quaternion.Euler(0f, yaw, 0f);
                // Камера выводится Unity до игровой камеры, без ручного вызова Render
                camera.enabled = true;
            }
            catch (Exception)
            {
                camera.enabled = false;
            }
        }

        public void Dispose()
        {
            if (camera != null)
            {
                camera.enabled = false;
                camera.targetTexture = null;
            }
            if (previewLight != null) previewLight.enabled = false;
            if (sceneRoot != null)
            {
                sceneRoot.SetActive(false);
                UnityEngine.Object.Destroy(sceneRoot);
            }
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
            foreach (Mesh mesh in bakedMeshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            bakedMeshes.Clear();
            copies.Clear();
            extraHairBones.Clear();
            sourceHair.Clear();
            hairRoots.Clear();
            equipmentVisuals.Clear();
            missingEquipmentVisuals.Clear();
            equipmentPlayer = null;
            equipmentCharacterModel = null;
            appliedOutfitIndex = -1;
            nextEquipmentUpdate = 0f;
            hatParent = null;
            leftHandHolder = null;
            rightHandHolder = null;
            leftHandRopeRoot = null;
            rightHandHookRoot = null;
            rendererCount = 0;
            camera = null;
            previewLight = null;
            texture = null;
            sceneRoot = null;
            turntable = null;
            copiedRig = null;
            sourceRoot = null;
            modelRoot = null;
            fullBody = null;
            sourceHeadBone = null;
            firstPersonArms = null;
            hazmatBody = null;
            hazmatArms = null;
        }
    }
}
