using System;
using System.Collections.Generic;
using HarmonyLib;
using HMLLibrary;
using UnityEngine;
using UnityEngine.UI;
using xSkills.GUI;
using xSkills.Services;
using xSkills.Skills;

namespace xSkills.Systems
{
    internal static class StrongBackSystem
    {
        private const string HarmonyId = "xskills.survival.strongback";
        private const int MaximumSlots = 6;
        private static readonly List<Slot> ExtraSlots = new List<Slot>(MaximumSlots);
        private static Harmony harmony;
        private static PlayerInventory owner;
        private static float nextCheck;
        private static int visibleCount = -1;

        internal static int ActiveSlots { get { return Mathf.Max(0, visibleCount); } }

        internal static void Initialize()
        {
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            try
            {
                var method = AccessTools.Method(typeof(PlayerInventory), "InitializeSlots");
                if (method != null)
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(StrongBackSystem), nameof(AfterInitializeSlots)));
                else
                    Debug.LogWarning("[xSkills] Strong Back: PlayerInventory.InitializeSlots not found");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Strong Back initialization: " + ex);
            }
        }

        private static void AfterInitializeSlots(PlayerInventory __instance)
        {
            AttachSlots(__instance);
        }

        internal static void Tick()
        {
            bool maintain = Time.unscaledTime >= nextCheck;
            bool requestedSwitch = Input.GetKeyDown(KeyCode.R);
            if (!maintain && !requestedSwitch) return;
            Network_Player player;
            try
            {
                if (!RAPI.IsCurrentSceneGame() || SaveAndLoad.IsGameLoading) return;
                player = RAPI.GetLocalPlayer();
            }
            catch { return; }
            if (player == null || !player.IsLocalPlayer || player.Inventory == null) return;

            PlayerInventory inventory = player.Inventory;
            if (maintain)
            {
                nextCheck = Time.unscaledTime + .12f;
                AttachSlots(inventory);
                if (owner == inventory && ExtraSlots.Count == MaximumSlots)
                    UpdateAvailableSlots(GetUnlockedSlotCount());
            }
            if (owner != inventory || ExtraSlots.Count != MaximumSlots) return;

            if (!requestedSwitch || visibleCount == 0 ||
                CanvasHelper.ActiveMenu != MenuType.None || Player.LocalPlayerIsDead ||
                KeybindInterface.AssigningKey || ConsoleInputCapture.IsTyping() ||
                ChatTextFieldController.IsChatWindowSelected || Inventory.fromSlot != null ||
                player.PlayerItemManager == null || !player.PlayerItemManager.CanSwitch() ||
                player.BlockCreator == null || player.BlockCreator.aimingWithPlaceable ||
                player.BlockCreator.IsRotating || player.PersonController == null ||
                !player.PersonController.IsMovementFree)
                return;

            SwapWithHotbar(inventory);
        }

        private static int GetUnlockedSlotCount()
        {
            if (!SkillProgressStorage.IsWorldBound) return 0;
            SkillData skill = SkillCatalog.Find("survival");
            AbilityData ability = skill != null ? skill.FindAbility("strongback") : null;
            if (ability == null) return 0;
            int maxAllowed = Mathf.Max(0, skill.Level - ability.RequiredLevel + 1);
            int tier = Mathf.Clamp(ability.PreviewTier, 0, Mathf.Min(maxAllowed, ability.MaxTier));
            if (tier <= 0 || ability.ValuesPerTier < 1) return 0;
            int index = (tier - 1) * ability.ValuesPerTier;
            return index >= 0 && index < ability.Values.Length
                ? Mathf.Clamp(ability.Values[index], 0, MaximumSlots) : 0;
        }

        private static void AttachSlots(PlayerInventory inventory)
        {
            if (inventory == null || inventory.allSlots == null || inventory.allSlots.Count == 0 ||
                inventory.backpackSlots == null || inventory.backpackSlots.Count == 0) return;
            if (owner == inventory && ExtraSlots.Count == MaximumSlots && ExtraSlots[0] != null &&
                inventory.allSlots.Contains(ExtraSlots[0])) return;

            owner = inventory;
            ExtraSlots.Clear();
            visibleCount = -1;

            Slot template = null;
            foreach (Slot slot in inventory.backpackSlots)
                if (slot != null && slot.gameObject != null) { template = slot; break; }
            if (template == null) return;

            Transform parent = template.transform.parent;
            bool autoLayout = parent != null && parent.GetComponent<LayoutGroup>() != null;
            Vector2 start = Vector2.zero;
            float stepX = 0f;
            float stepY = 0f;
            if (!autoLayout)
            {
                RectTransform reference = template.GetComponent<RectTransform>();
                start = reference.anchoredPosition;
                float width = reference.rect.width > 0f ? reference.rect.width : 60f;
                float height = reference.rect.height > 0f ? reference.rect.height : 60f;
                GridLayoutGroup grid = inventory.gridLayoutGroup;
                stepX = width + (grid != null ? grid.spacing.x : 4f);
                stepY = height + (grid != null ? grid.spacing.y : 4f);
                // Новые ячейки располагаются ниже существующих ячеек рюкзака
                float lowest = start.y;
                foreach (Slot slot in inventory.backpackSlots)
                {
                    if (slot == null || slot.transform.parent != parent) continue;
                    RectTransform rect = slot.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        lowest = Mathf.Min(lowest, rect.anchoredPosition.y);
                        start.x = Mathf.Min(start.x, rect.anchoredPosition.x);
                    }
                }
                start.y = lowest - stepY;
            }

            for (int i = 0; i < MaximumSlots; i++)
            {
                GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                copy.name = "xSkills_StrongBack_" + (i + 1);
                copy.SetActive(false);
                Slot slot = copy.GetComponent<Slot>();
                if (slot == null) { UnityEngine.Object.Destroy(copy); continue; }
                // Тип Normal исключает зависимость от надетого рюкзака
                slot.slotType = SlotType.Normal;
                slot.locked = false;
                slot.active = false;
                slot.InitializeEventListeners(inventory);
                slot.SetItem((ItemInstance)null);
                if (!autoLayout)
                {
                    RectTransform rect = copy.GetComponent<RectTransform>();
                    if (rect != null)
                        rect.anchoredPosition = start + new Vector2((i % 3) * stepX, -(i / 3) * stepY);
                }
                ExtraSlots.Add(slot);
                inventory.allSlots.Add(slot);
            }
            if (ExtraSlots.Count != MaximumSlots)
                Debug.LogWarning("[xSkills] Strong Back: not all 6 slots were created");
        }

        private static void UpdateAvailableSlots(int count)
        {
            count = Mathf.Clamp(count, 0, ExtraSlots.Count);
            if (count == visibleCount) return;
            if (count < visibleCount)
                RelocateLockedItems(count);
            for (int i = 0; i < ExtraSlots.Count; i++)
            {
                Slot slot = ExtraSlots[i];
                if (slot == null) continue;
                bool enabled = i < count;
                slot.active = enabled;
                slot.gameObject.SetActive(enabled);
                if (enabled) slot.RefreshComponents();
            }
            visibleCount = count;
        }

        private static void RelocateLockedItems(int count)
        {
            if (owner == null) return;
            bool movedToHotbar = false;
            for (int i = Mathf.Max(0, count); i < ExtraSlots.Count; i++)
            {
                Slot from = ExtraSlots[i];
                if (from == null || from.IsEmpty) continue;
                foreach (Slot target in owner.allSlots)
                {
                    if (target == null || target == from || !target.active || !target.IsEmpty ||
                        ExtraSlots.Contains(target) && ExtraSlots.IndexOf(target) >= count) continue;
                    target.SetItem(from.itemInstance);
                    from.SetItem((ItemInstance)null);
                    if (owner.hotbar != null && owner.hotbar.ContainsSlot(target))
                        movedToHotbar = true;
                    break;
                }
            }
            if (movedToHotbar && owner.hotbar != null)
                owner.hotbar.ReselectCurrentSlot();
        }

        private static void SwapWithHotbar(PlayerInventory inventory)
        {
            if (ExtraSlots.Count != MaximumSlots || inventory.hotslotCount < visibleCount) return;
            ItemInstance[] normalItems = new ItemInstance[visibleCount];
            ItemInstance[] reserveItems = new ItemInstance[visibleCount];
            for (int i = 0; i < visibleCount; i++)
            {
                Slot hot = inventory.GetSlot(i);
                Slot reserve = ExtraSlots[i];
                if (hot == null || reserve == null || !reserve.active) return;
                normalItems[i] = hot.IsEmpty ? null : hot.itemInstance.Clone();
                reserveItems[i] = reserve.IsEmpty ? null : reserve.itemInstance.Clone();
            }
            for (int i = 0; i < visibleCount; i++)
            {
                inventory.GetSlot(i).SetItem(reserveItems[i]);
                ExtraSlots[i].SetItem(normalItems[i]);
            }
            if (inventory.hotbar != null)
                inventory.hotbar.ReselectCurrentSlot();
        }

        internal static void Dispose()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            // При выгрузке мода предметы возвращаются в доступный инвентарь
            if (owner != null && ExtraSlots.Count > 0)
            {
                RelocateLockedItems(0);
                foreach (Slot slot in ExtraSlots)
                {
                    if (slot == null) continue;
                    if (!slot.IsEmpty)
                    {
                        Network_Player player = null;
                        try { player = RAPI.GetLocalPlayer(); } catch { }
                        if (player == null || player.CameraTransform == null || player.PersonController == null)
                            continue;
                        Helper.DropItem(slot.itemInstance.Clone(), player.transform.position,
                            player.CameraTransform.forward, player.PersonController.HasRaftAsParent);
                        slot.SetItem((ItemInstance)null);
                    }
                    owner.allSlots.Remove(slot);
                    UnityEngine.Object.Destroy(slot.gameObject);
                }
            }
            owner = null;
            ExtraSlots.Clear();
            visibleCount = -1;
            nextCheck = 0f;
        }
    }
}
