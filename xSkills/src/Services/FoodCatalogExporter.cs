using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace xSkills.Services
{
    internal static class FoodCatalogExporter
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static bool Export(out string path, out string error)
        {
            path = "";
            error = "";
            try
            {
                string folder = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Logs");
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, "xSkills-food-catalog.txt");

                StringBuilder report = new StringBuilder(192 * 1024);
                report.AppendLine("xSkills food and recipes catalog (diagnostic)");
                report.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Culture));
                report.AppendLine("FoodType and FoodForm values are raw Raft values, not yet diet categories");
                report.AppendLine();

                Item_Base[] available = Resources.LoadAll<Item_Base>("IItems");
                Dictionary<string, Item_Base> items = new Dictionary<string, Item_Base>(StringComparer.OrdinalIgnoreCase);
                foreach (Item_Base item in available)
                    if (item != null && !string.IsNullOrEmpty(item.UniqueName)) items[item.UniqueName] = item;

                // Добавленные модами предметы дополняют базовый список
                try
                {
                    FieldInfo registered = typeof(ItemManager).GetField("allAvailableItems",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    IEnumerable liveItems = registered == null ? null : registered.GetValue(null) as IEnumerable;
                    if (liveItems != null)
                        foreach (object entry in liveItems)
                        {
                            Item_Base item = entry as Item_Base;
                            if (item != null && !string.IsNullOrEmpty(item.UniqueName)) items[item.UniqueName] = item;
                        }
                }
                catch (Exception ex) { report.AppendLine("ITEM_REGISTRY_WARNING " + Clean(ex.Message)); }

                List<Item_Base> sortedItems = items.Values.OrderBy(i => i.UniqueName, StringComparer.OrdinalIgnoreCase).ToList();
                report.AppendLine("[CONSUMABLES]");
                report.AppendLine("UniqueName | UniqueIndex | DisplayName | FoodType | FoodForm | Hunger | BonusHunger | Thirst | BonusThirst | Oxygen | IsRaw");
                int consumables = 0;
                foreach (Item_Base item in sortedItems)
                {
                    try
                    {
                        ItemInstance_Consumeable food = item.settings_consumeable;
                        if (food == null) continue;
                        bool nutrition = food.HungerYield != 0f || food.BonusHungerYield != 0f ||
                            food.ThirstYield != 0f || food.BonusThirstYield != 0f || food.OxygenYield != 0f;
                        if (!nutrition && string.Equals(food.FoodType.ToString(), "None", StringComparison.OrdinalIgnoreCase)) continue;
                        consumables++;
                        report.Append(Clean(item.UniqueName)).Append(" | ").Append(item.UniqueIndex).Append(" | ");
                        report.Append(Clean(item.settings_Inventory == null ? "" : item.settings_Inventory.DisplayName)).Append(" | ");
                        report.Append(food.FoodType).Append(" | ").Append(food.FoodForm).Append(" | ");
                        report.Append(N(food.HungerYield)).Append(" | ").Append(N(food.BonusHungerYield)).Append(" | ");
                        report.Append(N(food.ThirstYield)).Append(" | ").Append(N(food.BonusThirstYield)).Append(" | ");
                        report.Append(N(food.OxygenYield)).Append(" | ").Append(food.IsRaw).AppendLine();
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine("ITEM_ERROR " + Clean(item.UniqueName) + " " + Clean(ex.Message));
                    }
                }
                report.AppendLine("TOTAL_CONSUMABLES=" + consumables);
                report.AppendLine();
                report.AppendLine("[ALL_ITEMS_FOR_INGREDIENT_LOOKUP]");
                foreach (Item_Base item in sortedItems) report.Append(Clean(item.UniqueName)).Append(" | ").Append(item.UniqueIndex).AppendLine();
                report.AppendLine("TOTAL_ITEMS=" + sortedItems.Count);
                report.AppendLine();

                report.AppendLine("[RECIPE_ASSETS]");
                ScriptableObject[] loadedAssets;
                try
                {
                    // Полный поиск запускается только по кнопке диагностики
                    loadedAssets = Resources.LoadAll<ScriptableObject>("");
                }
                catch (Exception ex)
                {
                    loadedAssets = Resources.FindObjectsOfTypeAll<ScriptableObject>();
                    report.AppendLine("ALL_RESOURCES_WARNING " + Clean(ex.Message));
                }
                Dictionary<int, ScriptableObject> candidates = new Dictionary<int, ScriptableObject>();
                foreach (ScriptableObject asset in loadedAssets)
                    if (IsRecipe(asset)) candidates[asset.GetInstanceID()] = asset;
                foreach (ScriptableObject asset in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                    if (IsRecipe(asset)) candidates[asset.GetInstanceID()] = asset;

                int recipeCount = 0;
                foreach (ScriptableObject asset in candidates.Values.OrderBy(a => a.GetType().Name).ThenBy(a => a.name))
                {
                    if (recipeCount >= 1500) { report.AppendLine("RECIPE_LIMIT_REACHED"); break; }
                    recipeCount++;
                    report.AppendLine("RECIPE " + recipeCount + " " + Clean(asset.GetType().FullName) + " :: " + Clean(asset.name));
                    try { DumpFields(report, asset, "  ", 0, new HashSet<int>()); }
                    catch (Exception ex) { report.AppendLine("  ERROR=" + Clean(ex.Message)); }
                    report.AppendLine();
                }
                report.AppendLine("TOTAL_RECIPE_ASSETS=" + recipeCount);
                report.AppendLine("If this count is zero or incomplete, recipe data may be stored in cooking station components or non-Resources assets");

                File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
                Debug.Log("[xSkills Diet] Food catalog saved: " + path + "; items=" + sortedItems.Count +
                    "; consumables=" + consumables + "; recipe assets=" + recipeCount);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                Debug.LogWarning("[xSkills Diet] Catalog export failed: " + error);
                return false;
            }
        }

        private static bool IsRecipe(ScriptableObject asset)
        {
            if (asset == null || asset is Item_Base) return false;
            string type = asset.GetType().Name.ToLowerInvariant();
            string name = (asset.name ?? "").ToLowerInvariant();
            return type.Contains("recipe") || type.Contains("cooking") || type.Contains("juice") ||
                   name.Contains("recipe") || name.Contains("cooking") || name.Contains("juice");
        }

        private static void DumpFields(StringBuilder report, object value, string indent, int depth, HashSet<int> visited)
        {
            if (value == null || depth > 4 || report.Length > 1800000) return;
            Type type = value.GetType();
            for (Type cursor = type; cursor != null && cursor != typeof(UnityEngine.Object) && cursor != typeof(ScriptableObject);
                cursor = cursor.BaseType)
            {
                FieldInfo[] fields = cursor.GetFields(Fields);
                int count = 0;
                foreach (FieldInfo field in fields)
                {
                    if (field.IsStatic || field.Name.StartsWith("m_", StringComparison.Ordinal) ||
                        field.Name.IndexOf("BackingField", StringComparison.Ordinal) >= 0) continue;
                    if (++count > 65) { report.AppendLine(indent + "...fields omitted"); break; }
                    object data;
                    try { data = field.GetValue(value); }
                    catch (Exception) { continue; }
                    DumpValue(report, indent + field.Name, data, depth + 1, visited);
                }
            }
        }

        private static void DumpValue(StringBuilder report, string key, object value, int depth, HashSet<int> visited)
        {
            if (report.Length > 1800000) return;
            if (value == null) { report.AppendLine(key + " = null"); return; }
            if (value is Item_Base)
            {
                Item_Base item = (Item_Base)value;
                report.AppendLine(key + " = ITEM:" + Clean(item.UniqueName) + " #" + item.UniqueIndex);
                return;
            }
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal)
            {
                report.AppendLine(key + " = " + Clean(Convert.ToString(value, Culture)));
                return;
            }
            if (value is UnityEngine.Object)
            {
                UnityEngine.Object obj = (UnityEngine.Object)value;
                report.AppendLine(key + " = " + Clean(type.Name) + ":" + Clean(obj.name));
                if (depth <= 3 && obj is ScriptableObject && IsRecipe((ScriptableObject)obj) &&
                    visited.Add(obj.GetInstanceID()))
                    DumpFields(report, obj, key + ".", depth, visited);
                return;
            }
            IEnumerable sequence = value as IEnumerable;
            if (sequence != null && !(value is string))
            {
                int index = 0;
                foreach (object entry in sequence)
                {
                    if (index >= 60) { report.AppendLine(key + " ...more entries"); break; }
                    DumpValue(report, key + "[" + index + "]", entry, depth + 1, visited);
                    index++;
                }
                if (index == 0) report.AppendLine(key + " = []");
                return;
            }
            report.AppendLine(key + " = " + Clean(type.Name));
            if (depth <= 4 && !type.IsPointer) DumpFields(report, value, key + ".", depth, visited);
        }

        private static string N(float number) { return number.ToString("0.###", Culture); }

        private static string Clean(string text)
        {
            return (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
        }
    }
}
