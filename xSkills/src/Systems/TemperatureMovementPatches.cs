using HarmonyLib;
using UnityEngine;

namespace xSkills.Systems
{
    [HarmonyPatch(typeof(PersonController), "GroundControll")]
    internal static class TemperatureGroundMovementPatch
    {
        private static void Postfix(PersonController __instance, ref Vector3 ___moveDirection)
        {
            if (__instance != BodyTemperatureSystem.LocalController ||
                !__instance.IsMovementFree || __instance.IsNotFreeFalling) return;
            float factor = BodyTemperatureSystem.MovementMultiplier;
            if (factor >= 1f) return;
            ___moveDirection.x *= factor;
            ___moveDirection.z *= factor;
        }
    }

    [HarmonyPatch(typeof(PersonController), "WaterControll")]
    internal static class TemperatureWaterMovementPatch
    {
        private static void Postfix(PersonController __instance, ref Vector3 ___moveDirection,
            ref Vector3 ___externalVelocity)
        {
            if (__instance != BodyTemperatureSystem.LocalController ||
                !__instance.IsMovementFree) return;
            float factor = BodyTemperatureSystem.MovementMultiplier;
            if (factor >= 1f) return;
            ___moveDirection.x = (___moveDirection.x - ___externalVelocity.x) * factor + ___externalVelocity.x;
            ___moveDirection.z = (___moveDirection.z - ___externalVelocity.z) * factor + ___externalVelocity.z;
        }
    }
}
