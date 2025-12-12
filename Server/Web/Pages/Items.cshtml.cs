using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Server.Envir;
using Zircon.Server.Models;
using Library.SystemModels;
using Library;
using System.Collections.Generic;
using System.Linq;

namespace Server.Web.Pages
{
    [Authorize]
    public class ItemsModel : PageModel
    {
        public List<ItemViewModel> Items { get; set; } = new();
        public int TotalCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Keyword { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ItemType { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SortBy { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SortOrder { get; set; }

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        public int PageSize { get; set; } = 50;
        public int TotalPages => (TotalCount + PageSize - 1) / PageSize;

        public string? Message { get; set; }

        private bool IsAjaxRequest()
        {
            if (HttpContext?.Request?.Headers == null) return false;

            return HttpContext.Request.Headers.TryGetValue("X-Requested-With", out var values)
                   && values.Any(v => v.Equals("XMLHttpRequest", System.StringComparison.OrdinalIgnoreCase));
        }

        public void OnGet()
        {
            LoadItems();
        }

        private ItemViewModel ToViewModel(ItemInfo item)
        {
            return new ItemViewModel
            {
                Index = item.Index,
                ItemName = item.ItemName ?? "Unknown",
                ItemType = item.ItemType.ToString(),
                RequiredType = item.RequiredType.ToString(),
                RequiredAmount = item.RequiredAmount,
                Price = item.Price,
                StackSize = item.StackSize,
                Rarity = item.Rarity.ToString(),
                Effect = item.Effect.ToString()
            };
        }

        private void LoadItems()
        {
            try
            {
                if (SEnvir.ItemInfoList?.Binding == null) return;

                var query = SEnvir.ItemInfoList.Binding.AsEnumerable();

                // Keyword filter
                if (!string.IsNullOrWhiteSpace(Keyword))
                {
                    query = query.Where(i =>
                        (i.ItemName?.Contains(Keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                        i.Index.ToString().Contains(Keyword));
                }

                // ItemType filter
                if (!string.IsNullOrWhiteSpace(ItemType) && System.Enum.TryParse<ItemType>(ItemType, out var type))
                {
                    query = query.Where(i => i.ItemType == type);
                }

                TotalCount = query.Count();

                // Sorting
                bool descending = string.Equals(SortOrder, "desc", System.StringComparison.OrdinalIgnoreCase);

                query = SortBy?.ToLower() switch
                {
                    "price" => descending ? query.OrderByDescending(i => i.Price) : query.OrderBy(i => i.Price),
                    "name" => descending ? query.OrderByDescending(i => i.ItemName) : query.OrderBy(i => i.ItemName),
                    "level" => descending ? query.OrderByDescending(i => i.RequiredAmount) : query.OrderBy(i => i.RequiredAmount),
                    "index" => descending ? query.OrderByDescending(i => i.Index) : query.OrderBy(i => i.Index),
                    "rarity" => descending ? query.OrderByDescending(i => i.Rarity) : query.OrderBy(i => i.Rarity),
                    "stacksize" => descending ? query.OrderByDescending(i => i.StackSize) : query.OrderBy(i => i.StackSize),
                    _ => query.OrderBy(i => i.ItemType).ThenBy(i => i.RequiredAmount) // default sorting
                };

                Items = query
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .Select(ToViewModel)
                    .ToList();
            }
            catch
            {
                // Prevent enumeration errors
            }
        }

        public IActionResult OnPostGiveItem(string playerName, int itemIndex, int count)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                if (IsAjaxRequest())
                    return new JsonResult(new { success = false, message = "权限不足，需要 Admin 权限" });

                Message = "权限不足，需要 Admin 权限";
                LoadItems();
                return Page();
            }

            try
            {
                var player = SEnvir.Players.FirstOrDefault(p =>
                    p?.Character?.CharacterName?.Equals(playerName, System.StringComparison.OrdinalIgnoreCase) == true);

                if (player == null)
                {
                    if (IsAjaxRequest())
                        return new JsonResult(new { success = false, message = $"玩家 {playerName} 不在线" });

                    Message = $"玩家 {playerName} 不在线";
                    LoadItems();
                    return Page();
                }

                var itemInfo = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                if (itemInfo == null)
                {
                    if (IsAjaxRequest())
                        return new JsonResult(new { success = false, message = $"物品索引 {itemIndex} 不存在" });

                    Message = $"物品索引 {itemIndex} 不存在";
                    LoadItems();
                    return Page();
                }

                // Create item and give to player
                var item = SEnvir.CreateFreshItem(itemInfo);
                if (item != null)
                {
                    item.Count = count > 0 ? count : 1;
                    if (player.CanGainItems(false, new ItemCheck(item, item.Count, item.Flags, item.ExpireTime)))
                    {
                        player.GainItem(item);
                        SEnvir.Log($"[Admin] 给予物品: {playerName} <- {count}x {itemInfo.ItemName}");
                        if (IsAjaxRequest())
                            return new JsonResult(new { success = true, message = $"已给予 {playerName} {count}x {itemInfo.ItemName}" });

                        Message = $"已给予 {playerName} {count}x {itemInfo.ItemName}";
                    }
                    else
                    {
                        if (IsAjaxRequest())
                            return new JsonResult(new { success = false, message = $"{playerName} 背包已满" });

                        Message = $"{playerName} 背包已满";
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (IsAjaxRequest())
                    return new JsonResult(new { success = false, message = $"操作失败: {ex.Message}" });

                Message = $"操作失败: {ex.Message}";
            }

            if (IsAjaxRequest())
                return new JsonResult(new { success = false, message = "未知错误" });

            LoadItems();
            return Page();
        }

        // 获取物品详情 (AJAX)
        public IActionResult OnGetItemDetail(int itemIndex)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            try
            {
                var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                if (item == null)
                {
                    return new JsonResult(new { success = false, message = "物品不存在" });
                }

                var detail = new ItemDetailViewModel
                {
                    Index = item.Index,
                    ItemName = item.ItemName ?? "",
                    ItemType = (int)item.ItemType,
                    RequiredClass = (int)item.RequiredClass,
                    RequiredGender = (int)item.RequiredGender,
                    RequiredType = (int)item.RequiredType,
                    RequiredAmount = item.RequiredAmount,
                    Shape = item.Shape,
                    Effect = (int)item.Effect,
                    Image = item.Image,
                    Durability = item.Durability,
                    Price = item.Price,
                    Weight = item.Weight,
                    StackSize = item.StackSize,
                    Rarity = (int)item.Rarity,
                    Stats = item.ItemStats?.Select(s => new ItemStatViewModel
                    {
                        StatType = (int)s.Stat,
                        StatName = s.Stat.ToString(),
                        Amount = s.Amount
                    }).ToList() ?? new List<ItemStatViewModel>()
                };

                return new JsonResult(new { success = true, data = detail });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取属性类型列表（分组）
        public IActionResult OnGetStatTypes()
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            // 定义属性分组
            var statGroups = new Dictionary<string, List<Stat>>
            {
                ["基础防御"] = new List<Stat> { Stat.MinAC, Stat.MaxAC, Stat.MinMR, Stat.MaxMR },
                ["攻击力"] = new List<Stat> { Stat.MinDC, Stat.MaxDC, Stat.MinMC, Stat.MaxMC, Stat.MinSC, Stat.MaxSC },
                ["基础属性"] = new List<Stat> { Stat.Health, Stat.Mana, Stat.Accuracy, Stat.Agility, Stat.AttackSpeed, Stat.Luck, Stat.Strength, Stat.Light },
                ["元素攻击"] = new List<Stat> { Stat.FireAttack, Stat.IceAttack, Stat.LightningAttack, Stat.WindAttack, Stat.HolyAttack, Stat.DarkAttack, Stat.PhantomAttack },
                ["元素抵抗"] = new List<Stat> { Stat.FireResistance, Stat.IceResistance, Stat.LightningResistance, Stat.WindResistance, Stat.HolyResistance, Stat.DarkResistance, Stat.PhantomResistance, Stat.PhysicalResistance },
                ["元素亲和"] = new List<Stat> { Stat.FireAffinity, Stat.IceAffinity, Stat.LightningAffinity, Stat.WindAffinity, Stat.HolyAffinity, Stat.DarkAffinity, Stat.PhantomAffinity },
                ["特殊效果"] = new List<Stat> { Stat.LifeSteal, Stat.CriticalChance, Stat.CriticalDamage, Stat.DamageReduction, Stat.ReflectDamage, Stat.BlockChance, Stat.EvasionChance, Stat.MagicShield, Stat.Healing },
                ["负重背包"] = new List<Stat> { Stat.BagWeight, Stat.WearWeight, Stat.HandWeight, Stat.PickUpRadius },
                ["倍率加成"] = new List<Stat> { Stat.ExperienceRate, Stat.DropRate, Stat.GoldRate }
            };

            var result = new List<object>();
            foreach (var group in statGroups)
            {
                var items = group.Value.Select(s => new StatTypeInfo
                {
                    Value = (int)s,
                    Name = s.ToString(),
                    Group = group.Key
                }).ToList();
                result.Add(new { group = group.Key, stats = items });
            }

            return new JsonResult(new { success = true, data = result });
        }

        // 新建物品
        public IActionResult OnPostCreateItem(
            string itemName,
            int itemType,
            int requiredClass,
            int requiredGender,
            int requiredType,
            int requiredAmount,
            int shape,
            int effect,
            int image,
            int durability,
            int price,
            int weight,
            int stackSize,
            int rarity,
            string? stats)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                if (IsAjaxRequest())
                    return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });

                Message = "权限不足，需要 SuperAdmin 权限";
                LoadItems();
                return Page();
            }

            try
            {
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    if (IsAjaxRequest())
                        return new JsonResult(new { success = false, message = "物品名称不能为空" });

                    Message = "物品名称不能为空";
                    LoadItems();
                    return Page();
                }

                var newItem = SEnvir.ItemInfoList?.CreateNewObject();
                if (newItem == null)
                {
                    if (IsAjaxRequest())
                        return new JsonResult(new { success = false, message = "创建物品失败" });

                    Message = "创建物品失败";
                    LoadItems();
                    return Page();
                }

                newItem.ItemName = itemName;
                newItem.ItemType = (ItemType)itemType;
                newItem.RequiredClass = (RequiredClass)requiredClass;
                newItem.RequiredGender = (RequiredGender)requiredGender;
                newItem.RequiredType = (RequiredType)requiredType;
                newItem.RequiredAmount = requiredAmount;
                newItem.Shape = shape;
                newItem.Effect = (ItemEffect)effect;
                newItem.Image = image;
                newItem.Durability = durability;
                newItem.Price = price;
                newItem.Weight = weight;
                newItem.StackSize = stackSize > 0 ? stackSize : 1;
                newItem.Rarity = (Rarity)rarity;

                // 处理属性
                if (!string.IsNullOrWhiteSpace(stats))
                {
                    UpdateItemStats(newItem, stats);
                }

                SEnvir.Log($"[Admin] 新建物品: [{newItem.Index}] {itemName}");
                if (IsAjaxRequest())
                {
                    return new JsonResult(new
                    {
                        success = true,
                        message = $"物品 [{newItem.Index}] {itemName} 创建成功",
                        data = ToViewModel(newItem)
                    });
                }

                Message = $"物品 [{newItem.Index}] {itemName} 创建成功";
            }
            catch (System.Exception ex)
            {
                if (IsAjaxRequest())
                    return new JsonResult(new { success = false, message = $"创建失败: {ex.Message}" });

                Message = $"创建失败: {ex.Message}";
            }

            if (IsAjaxRequest())
                return new JsonResult(new { success = false, message = "未知错误" });

            LoadItems();
            return Page();
        }

        // 编辑物品
        public IActionResult OnPostUpdateItem(
            int itemIndex,
            string itemName,
            int itemType,
            int requiredClass,
            int requiredGender,
            int requiredType,
            int requiredAmount,
            int shape,
            int effect,
            int image,
            int durability,
            int price,
            int weight,
            int stackSize,
            int rarity,
            string? stats)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                if (IsAjaxRequest())
                    return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });

                Message = "权限不足，需要 SuperAdmin 权限";
                LoadItems();
                return Page();
            }

            try
            {
                var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                if (item == null)
                {
                    if (IsAjaxRequest())
                        return new JsonResult(new { success = false, message = $"物品索引 {itemIndex} 不存在" });

                    Message = $"物品索引 {itemIndex} 不存在";
                    LoadItems();
                    return Page();
                }

                var oldName = item.ItemName;

                item.ItemName = itemName;
                item.ItemType = (ItemType)itemType;
                item.RequiredClass = (RequiredClass)requiredClass;
                item.RequiredGender = (RequiredGender)requiredGender;
                item.RequiredType = (RequiredType)requiredType;
                item.RequiredAmount = requiredAmount;
                item.Shape = shape;
                item.Effect = (ItemEffect)effect;
                item.Image = image;
                item.Durability = durability;
                item.Price = price;
                item.Weight = weight;
                item.StackSize = stackSize > 0 ? stackSize : 1;
                item.Rarity = (Rarity)rarity;

                // 处理属性
                if (!string.IsNullOrWhiteSpace(stats))
                {
                    UpdateItemStats(item, stats);
                }

                SEnvir.Log($"[Admin] 修改物品: [{itemIndex}] {oldName} -> {itemName}");
                if (IsAjaxRequest())
                {
                    return new JsonResult(new
                    {
                        success = true,
                        message = $"物品 [{itemIndex}] {itemName} 已更新",
                        data = ToViewModel(item)
                    });
                }

                Message = $"物品 [{itemIndex}] {itemName} 已更新";
            }
            catch (System.Exception ex)
            {
                if (IsAjaxRequest())
                    return new JsonResult(new { success = false, message = $"修改失败: {ex.Message}" });

                Message = $"修改失败: {ex.Message}";
            }

            if (IsAjaxRequest())
                return new JsonResult(new { success = false, message = "未知错误" });

            LoadItems();
            return Page();
        }

        /// <summary>
        /// 更新物品属性
        /// </summary>
        /// <param name="item">物品对象</param>
        /// <param name="statsJson">属性JSON字符串，格式: [{"statType":1,"amount":10},...]</param>
        private void UpdateItemStats(ItemInfo item, string statsJson)
        {
            if (item.ItemStats == null) return;

            try
            {
                var statInputs = System.Text.Json.JsonSerializer.Deserialize<List<StatInput>>(statsJson);
                if (statInputs == null) return;

                // 构建要保存的属性字典
                var newStats = new Dictionary<Stat, int>();
                foreach (var input in statInputs)
                {
                    var stat = (Stat)input.StatType;
                    if (input.Amount != 0)
                    {
                        newStats[stat] = input.Amount;
                    }
                }

                // 删除不再需要的属性
                var toRemove = item.ItemStats.Where(s => !newStats.ContainsKey(s.Stat)).ToList();
                foreach (var stat in toRemove)
                {
                    item.ItemStats.Remove(stat);
                    // 从数据库中删除
                    stat.Delete();
                }

                // 更新或添加属性
                foreach (var kvp in newStats)
                {
                    var existingStat = item.ItemStats.FirstOrDefault(s => s.Stat == kvp.Key);
                    if (existingStat != null)
                    {
                        // 更新现有属性
                        existingStat.Amount = kvp.Value;
                    }
                    else
                    {
                        // 创建新属性
                        var newStat = SEnvir.ItemInfoStatList?.CreateNewObject();
                        if (newStat != null)
                        {
                            newStat.Stat = kvp.Key;
                            newStat.Amount = kvp.Value;
                            // 设置关联会自动将 newStat 添加到 item.ItemStats (MirDB 关联机制)
                            newStat.Item = item;
                        }
                    }
                }

                // 触发属性重算
                item.StatsChanged();
            }
            catch (System.Exception ex)
            {
                SEnvir.Log($"[Admin] 更新物品属性失败: {ex.Message}");
            }
        }

        private bool HasPermission(AccountIdentity required)
        {
            var permissionClaim = User.FindFirst("Permission")?.Value;
            if (string.IsNullOrEmpty(permissionClaim)) return false;

            if (int.TryParse(permissionClaim, out int permValue))
            {
                return permValue >= (int)required;
            }
            return false;
        }
    }

    public class ItemViewModel
    {
        public int Index { get; set; }
        public string ItemName { get; set; } = "";
        public string ItemType { get; set; } = "";
        public string RequiredType { get; set; } = "";
        public int RequiredAmount { get; set; }
        public int Price { get; set; }
        public int StackSize { get; set; }
        public string Rarity { get; set; } = "";
        public string Effect { get; set; } = "";
    }

    public class ItemDetailViewModel
    {
        public int Index { get; set; }
        public string ItemName { get; set; } = "";
        public int ItemType { get; set; }
        public int RequiredClass { get; set; }
        public int RequiredGender { get; set; }
        public int RequiredType { get; set; }
        public int RequiredAmount { get; set; }
        public int Shape { get; set; }
        public int Effect { get; set; }
        public int Image { get; set; }
        public int Durability { get; set; }
        public int Price { get; set; }
        public int Weight { get; set; }
        public int StackSize { get; set; }
        public int Rarity { get; set; }
        public List<ItemStatViewModel> Stats { get; set; } = new();
    }

    public class ItemStatViewModel
    {
        public int StatType { get; set; }
        public string StatName { get; set; } = "";
        public int Amount { get; set; }
    }

    public class StatTypeInfo
    {
        public int Value { get; set; }
        public string Name { get; set; } = "";
        public string Group { get; set; } = "";
    }

    /// <summary>
    /// 用于接收前端提交的属性数据
    /// </summary>
    public class StatInput
    {
        public int StatType { get; set; }
        public int Amount { get; set; }
    }
}
