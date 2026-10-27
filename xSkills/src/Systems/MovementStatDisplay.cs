using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace xSkills.Systems
{
    internal static class MovementStatDisplay
    {
        private static FieldInfo equipmentWalkField;
        private static readonly FieldInfo playerEquipmentArrayField =
            AccessTools.Field(typeof(PlayerEquipment), "equipment");
        private static readonly Dictionary<Type, FieldInfo> swimFields =
            new Dictionary<Type, FieldInfo>();
        private static float nextEquipmentLookup;

        internal static void Read(Network_Player player, out float walk, out float swim)
        {
            walk = 1f;
            swim = 1f;
            if (player == null || player.PersonController == null) return;
            PersonController controller = player.PersonController;
            float temperature = BodyTemperatureSystem.LocalController == controller
                ? BodyTemperatureSystem.MovementMultiplier : 1f;

            float walkEquipment = ReadEquipmentWalkMultiplier();
            float wellBeingWalk = Stat_WellBeing.Factor == WellBeing.Bad
                ? Stat_WellBeing.groundSpeedMultiplier : 1f;
            float wellBeingSwim = Stat_WellBeing.Factor == WellBeing.Bad
                ? Stat_WellBeing.swimSpeedMultiplier : 1f;

            walk = Sanitize(controller.GroundVelocityModifier) *
                Sanitize(walkEquipment) * Sanitize(wellBeingWalk) * temperature *
                (1f + SurvivalPerkSystem.CurrentGroundSpeedBonusPercent / 100f);
            float waterModifier = Sanitize(controller.WaterVelocityModifier);
            float flippersModifier = ReadEquippedFlippersMultiplier(player);
            // Уже применённый бонус ласт не учитывается дважды
            float effectiveWater = waterModifier >= 1f
                ? Mathf.Max(waterModifier, flippersModifier)
                : waterModifier * flippersModifier;
            swim = Sanitize(effectiveWater) * Sanitize(wellBeingSwim) * temperature;
        }

        private static float ReadEquipmentWalkMultiplier()
        {
            if (equipmentWalkField == null && Time.unscaledTime >= nextEquipmentLookup)
            {
                nextEquipmentLookup = Time.unscaledTime + 10f;
                try
                {
                    Type patch = AccessTools.TypeByName("_AugmentedEquipment.PersonControllerGroundControll_Patch");
                    if (patch != null)
                        equipmentWalkField = AccessTools.Field(patch, "WalkSpeedModifier");
                }
                catch (Exception) { }
            }
            if (equipmentWalkField == null) return 1f;
            try
            {
                object value = equipmentWalkField.GetValue(null);
                return value is float ? Sanitize((float)value) : 1f;
            }
            catch (Exception) { return 1f; }
        }

        internal static float ReadEquippedFlippersMultiplier(Network_Player player)
        {
            if (player.PlayerEquipment == null || playerEquipmentArrayField == null)
                return 1f;

            Equipment[] equipment;
            try
            {
                equipment = playerEquipmentArrayField.GetValue(player.PlayerEquipment) as Equipment[];
            }
            catch (Exception)
            {
                return 1f;
            }

            if (equipment == null) return 1f;

            float result = 1f;
            for (int i = 0; i < equipment.Length; i++)
            {
                Equipment item = equipment[i];
                if (item == null || !item.Equipped || item.equipableItem == null)
                    continue;

                string itemName = item.equipableItem.UniqueName;
                if (string.IsNullOrEmpty(itemName) ||
                    itemName.IndexOf("flipper", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Множитель хранится в наследнике Equipment, а не в базовом классе
                Type itemType = item.GetType();
                FieldInfo speedField;
                if (!swimFields.TryGetValue(itemType, out speedField))
                {
                    speedField = AccessTools.Field(itemType, "swimSpeedMultiplier");
                    swimFields[itemType] = speedField;
                }

                bool fieldRead = false;
                if (speedField != null)
                {
                    try
                    {
                        object value = speedField.GetValue(item);
                        if (value is float)
                        {
                            result = Mathf.Max(result, Sanitize((float)value));
                            fieldRead = true;
                        }
                    }
                    catch (Exception) { }
                }

                // Резервные значения применяются только если поле недоступно
                if (!fieldRead)
                {
                    if (string.Equals(itemName, "Flipper", StringComparison.OrdinalIgnoreCase))
                        result = Mathf.Max(result, 1.5f);
                    else if (string.Equals(itemName, "Reinforced_Flippers", StringComparison.OrdinalIgnoreCase))
                        result = Mathf.Max(result, 1.9f);
                    else if (string.Equals(itemName, "Carbon_Steel_Flippers", StringComparison.OrdinalIgnoreCase))
                        result = Mathf.Max(result, 2.6f);
                }
            }
            return result;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 1f : value;
        }
    }
}
