using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Server.Envir;
using Library.SystemModels;
using Library;
using System.Collections.Generic;
using System.Linq;
using Models = Zircon.Server.Models;

namespace Server.Web.Pages
{
    [Authorize]
    [IgnoreAntiforgeryToken]
    public class NpcsModel : PageModel
    {
        public List<NpcViewModel> Npcs { get; set; } = new();
        public int TotalCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Keyword { get; set; }

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
            LoadNpcs();
        }

        private NpcViewModel ToViewModel(NPCInfo npc)
        {
            // 查找在线NPC实例获取坐标
            int locationX = 0, locationY = 0;
            bool isOnline = false;
            foreach (var map in SEnvir.Maps.Values)
            {
                var npcObj = map?.NPCs?.FirstOrDefault(n => n?.NPCInfo?.Index == npc.Index);
                if (npcObj != null)
                {
                    locationX = npcObj.CurrentLocation.X;
                    locationY = npcObj.CurrentLocation.Y;
                    isOnline = true;
                    break;
                }
            }

            return new NpcViewModel
            {
                Index = npc.Index,
                NPCName = npc.NPCName ?? "Unknown",
                Image = npc.Image,
                RegionIndex = npc.Region?.Index ?? 0,
                RegionName = npc.Region?.ServerDescription ?? "未设置",
                MapName = npc.Region?.Map?.Description ?? "未知地图",
                LocationX = locationX,
                LocationY = locationY,
                IsOnline = isOnline,
                HasEntryPage = npc.EntryPage != null,
                EntryPageIndex = npc.EntryPage?.Index ?? 0,
                EntryPageDescription = npc.EntryPage?.Description ?? "",
                StartQuestsCount = npc.StartQuests?.Count ?? 0,
                FinishQuestsCount = npc.FinishQuests?.Count ?? 0
            };
        }

        private void LoadNpcs()
        {
            try
            {
                if (SEnvir.NPCInfoList?.Binding == null) return;

                var query = SEnvir.NPCInfoList.Binding.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(Keyword))
                {
                    query = query.Where(n =>
                        (n.NPCName?.Contains(Keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (n.Region?.ServerDescription?.Contains(Keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (n.Region?.Map?.Description?.Contains(Keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                        n.Index.ToString().Contains(Keyword));
                }

                TotalCount = query.Count();

                Npcs = query
                    .OrderBy(n => n.Index)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .Select(ToViewModel)
                    .ToList();
            }
            catch { }
        }

        #region NPC CRUD

        // 获取NPC详情
        public IActionResult OnGetNpcDetail(int npcIndex)
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var npc = SEnvir.NPCInfoList?.Binding?.FirstOrDefault(n => n.Index == npcIndex);
                if (npc == null)
                    return new JsonResult(new { success = false, message = "NPC不存在" });

                // 获取在线NPC实例的当前位置
                int locationX = 0, locationY = 0;
                int currentMapIndex = npc.Region?.Map?.Index ?? 0;
                foreach (var map in SEnvir.Maps.Values)
                {
                    var npcObj = map?.NPCs?.FirstOrDefault(n => n?.NPCInfo?.Index == npcIndex);
                    if (npcObj != null)
                    {
                        locationX = npcObj.CurrentLocation.X;
                        locationY = npcObj.CurrentLocation.Y;
                        currentMapIndex = map.Info?.Index ?? 0;
                        break;
                    }
                }

                var detail = new NpcDetailViewModel
                {
                    Index = npc.Index,
                    NPCName = npc.NPCName ?? "",
                    Image = npc.Image,
                    RegionIndex = npc.Region?.Index ?? 0,
                    RegionName = npc.Region?.ServerDescription ?? "",
                    MapIndex = currentMapIndex,
                    MapName = npc.Region?.Map?.Description ?? "",
                    LocationX = locationX,
                    LocationY = locationY,
                    EntryPageIndex = npc.EntryPage?.Index ?? 0,
                    EntryPageDescription = npc.EntryPage?.Description ?? "",
                    EntryPageDialogType = npc.EntryPage?.DialogType.ToString() ?? "",
                    EntryPageSay = npc.EntryPage?.Say ?? "",
                    StartQuestsCount = npc.StartQuests?.Count ?? 0,
                    FinishQuestsCount = npc.FinishQuests?.Count ?? 0,
                    ChecksCount = npc.EntryPage?.Checks?.Count ?? 0,
                    ActionsCount = npc.EntryPage?.Actions?.Count ?? 0,
                    ButtonsCount = npc.EntryPage?.Buttons?.Count ?? 0,
                    GoodsCount = npc.EntryPage?.Goods?.Count ?? 0
                };

                return new JsonResult(new { success = true, data = detail });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 新建NPC
        public IActionResult OnPostCreateNpc(string npcName, int image, int regionIndex, int mapIndex = 0, int locationX = 0, int locationY = 0)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                var result = new { success = false, message = "权限不足，需要 SuperAdmin 权限" };
                if (IsAjaxRequest()) return new JsonResult(result);
                Message = result.message;
                LoadNpcs();
                return Page();
            }

            try
            {
                if (string.IsNullOrWhiteSpace(npcName))
                {
                    var result = new { success = false, message = "NPC名称不能为空" };
                    if (IsAjaxRequest()) return new JsonResult(result);
                    Message = result.message;
                    LoadNpcs();
                    return Page();
                }

                var newNpc = SEnvir.NPCInfoList?.CreateNewObject();
                if (newNpc == null)
                {
                    var result = new { success = false, message = "创建NPC失败" };
                    if (IsAjaxRequest()) return new JsonResult(result);
                    Message = result.message;
                    LoadNpcs();
                    return Page();
                }

                newNpc.NPCName = npcName;
                newNpc.Image = image;

                // 处理区域设置
                if (regionIndex > 0)
                {
                    // 使用选择的区域
                    var region = SEnvir.MapRegionList?.Binding?.FirstOrDefault(r => r.Index == regionIndex);
                    if (region != null)
                        newNpc.Region = region;
                }
                else if (mapIndex > 0 && (locationX > 0 || locationY > 0))
                {
                    // 选择了地图和坐标，创建一个专属的单点区域
                    var map = SEnvir.MapInfoList?.Binding?.FirstOrDefault(m => m.Index == mapIndex);
                    if (map != null)
                    {
                        var newRegion = SEnvir.MapRegionList?.CreateNewObject();
                        if (newRegion != null)
                        {
                            newRegion.Map = map;
                            newRegion.Description = $"NPC_{npcName}_{locationX}_{locationY}";
                            newRegion.Size = 0;
                            // 设置单点区域
                            newRegion.PointRegion = new System.Drawing.Point[] { new System.Drawing.Point(locationX, locationY) };
                            newRegion.PointList = new List<System.Drawing.Point> { new System.Drawing.Point(locationX, locationY) };
                            newNpc.Region = newRegion;
                            SEnvir.Log($"[Admin] 为NPC [{newNpc.Index}] 创建专属区域: {newRegion.Description}");
                        }
                    }
                }
                else if (mapIndex > 0)
                {
                    // 只选择了地图没有坐标，查找该地图的第一个区域
                    var region = SEnvir.MapRegionList?.Binding?.FirstOrDefault(r => r.Map?.Index == mapIndex);
                    if (region != null)
                    {
                        newNpc.Region = region;
                    }
                    else
                    {
                        // 没有现有区域，提示用户
                        var result = new { success = false, message = "该地图没有可用区域，请指定坐标或先创建区域" };
                        if (IsAjaxRequest()) return new JsonResult(result);
                        Message = result.message;
                        LoadNpcs();
                        return Page();
                    }
                }

                SEnvir.Log($"[Admin] 新建NPC: [{newNpc.Index}] {npcName}, Region: {newNpc.Region?.ServerDescription ?? "无"}");

                // 如果设置了坐标，立即生成NPC实例并移动到指定位置
                if (mapIndex > 0 && (locationX > 0 || locationY > 0))
                {
                    SpawnAndMoveNpc(newNpc, mapIndex, locationX, locationY);
                }

                var success = new { success = true, message = $"NPC [{newNpc.Index}] {npcName} 创建成功", data = ToViewModel(newNpc) };
                if (IsAjaxRequest()) return new JsonResult(success);
                Message = success.message;
                LoadNpcs();
                return Page();
            }
            catch (System.Exception ex)
            {
                var result = new { success = false, message = $"创建失败: {ex.Message}" };
                if (IsAjaxRequest()) return new JsonResult(result);
                Message = result.message;
                LoadNpcs();
                return Page();
            }
        }

        // 生成NPC并移动到指定位置
        private void SpawnAndMoveNpc(NPCInfo npcInfo, int mapIndex, int locationX, int locationY)
        {
            if (npcInfo == null || mapIndex <= 0) return;

            try
            {
                // 查找目标地图
                var targetMap = SEnvir.Maps.Values.FirstOrDefault(m => m?.Info?.Index == mapIndex);
                if (targetMap == null)
                {
                    SEnvir.Log($"[Admin] 地图 {mapIndex} 不存在，NPC将在服务器重启后生成");
                    return;
                }

                // 尝试在目标地图创建NPC实例
                var targetPoint = new System.Drawing.Point(locationX, locationY);

                // 检查该NPC是否已有实例
                Models.NPCObject? existingNpc = null;
                Models.Map? currentMap = null;
                foreach (var map in SEnvir.Maps.Values)
                {
                    existingNpc = map?.NPCs?.FirstOrDefault(n => n?.NPCInfo?.Index == npcInfo.Index);
                    if (existingNpc != null)
                    {
                        currentMap = map;
                        break;
                    }
                }

                if (existingNpc != null)
                {
                    // 已有实例，移动到新位置
                    MoveNpcToLocation(existingNpc, currentMap, targetMap, targetPoint);
                    SEnvir.Log($"[Admin] NPC [{npcInfo.Index}] 已移动到 {targetMap.Info?.Description} ({locationX}, {locationY})");
                }
                else
                {
                    // 没有实例，创建新的NPC实例（使用对象初始化器）
                    var npcObject = new Models.NPCObject
                    {
                        NPCInfo = npcInfo,
                        CurrentMap = targetMap,
                        CurrentLocation = targetPoint
                    };

                    var cell = targetMap.GetCell(targetPoint);
                    if (cell != null)
                    {
                        npcObject.CurrentCell = cell;
                        cell.AddObject(npcObject);
                        targetMap.NPCs?.Add(npcObject);
                        SEnvir.Log($"[Admin] NPC [{npcInfo.Index}] 已在 {targetMap.Info?.Description} ({locationX}, {locationY}) 生成");
                    }
                    else
                    {
                        SEnvir.Log($"[Admin] 位置 ({locationX}, {locationY}) 无效，NPC将在服务器重启后在区域内随机生成");
                    }
                }
            }
            catch (System.Exception ex)
            {
                SEnvir.Log($"[Admin] 生成NPC并移动失败: {ex.Message}");
            }
        }

        // 更新NPC
        public IActionResult OnPostUpdateNpc(int npcIndex, string npcName, int image, int regionIndex, int mapIndex = 0, int locationX = 0, int locationY = 0)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                var result = new { success = false, message = "权限不足，需要 SuperAdmin 权限" };
                if (IsAjaxRequest()) return new JsonResult(result);
                Message = result.message;
                LoadNpcs();
                return Page();
            }

            try
            {
                var npc = SEnvir.NPCInfoList?.Binding?.FirstOrDefault(n => n.Index == npcIndex);
                if (npc == null)
                {
                    var result = new { success = false, message = $"NPC索引 {npcIndex} 不存在" };
                    if (IsAjaxRequest()) return new JsonResult(result);
                    Message = result.message;
                    LoadNpcs();
                    return Page();
                }

                var oldName = npc.NPCName;
                npc.NPCName = npcName;
                npc.Image = image;

                // 查找当前在线的NPC对象
                Models.NPCObject? npcObject = null;
                Models.Map? currentMap = null;
                foreach (var map in SEnvir.Maps.Values)
                {
                    npcObject = map?.NPCs?.FirstOrDefault(n => n?.NPCInfo?.Index == npcIndex);
                    if (npcObject != null)
                    {
                        currentMap = map;
                        break;
                    }
                }

                string locationInfo = "";

                if (regionIndex > 0)
                {
                    // 使用区域放置
                    var region = SEnvir.MapRegionList?.Binding?.FirstOrDefault(r => r.Index == regionIndex);
                    npc.Region = region;

                    if (region != null && npcObject != null)
                    {
                        // 将NPC移动到区域内的随机位置
                        var targetMap = SEnvir.GetMap(region.Map);
                        if (targetMap != null && region.PointList?.Count > 0)
                        {
                            var randomPoint = region.PointList[SEnvir.Random.Next(region.PointList.Count)];
                            MoveNpcToLocation(npcObject, currentMap, targetMap, randomPoint);
                            locationInfo = $" 已移动到区域 [{region.ServerDescription}] ({randomPoint.X},{randomPoint.Y})";
                        }
                    }
                }
                else if (mapIndex > 0 && (locationX > 0 || locationY > 0))
                {
                    // 使用自定义坐标放置
                    var mapInfo = SEnvir.MapInfoList?.Binding?.FirstOrDefault(m => m.Index == mapIndex);
                    if (mapInfo != null)
                    {
                        // 查找或创建一个单点区域来存储NPC位置
                        // 由于NPCInfo必须有Region才能正常工作，我们需要设置Region
                        // 但同时直接移动在线NPC到指定位置
                        var region = SEnvir.MapRegionList?.Binding?.FirstOrDefault(r => r.Map?.Index == mapIndex);
                        if (region != null)
                        {
                            npc.Region = region;
                        }

                        // 移动在线NPC到指定坐标
                        if (npcObject != null)
                        {
                            var targetMap = SEnvir.GetMap(mapInfo);
                            if (targetMap != null)
                            {
                                var targetPoint = new System.Drawing.Point(locationX, locationY);
                                if (MoveNpcToLocation(npcObject, currentMap, targetMap, targetPoint))
                                {
                                    locationInfo = $" 已移动到坐标 ({locationX},{locationY})";
                                }
                                else
                                {
                                    locationInfo = " (坐标无效，未能移动)";
                                }
                            }
                        }
                        else
                        {
                            locationInfo = " (NPC离线，坐标将在重启后生效)";
                        }
                    }
                }
                else
                {
                    npc.Region = null;
                }

                SEnvir.Log($"[Admin] 修改NPC: [{npcIndex}] {oldName} -> {npcName}{locationInfo}");
                var success = new { success = true, message = $"NPC [{npcIndex}] {npcName} 已更新{locationInfo}", data = ToViewModel(npc) };
                if (IsAjaxRequest()) return new JsonResult(success);
                Message = success.message;
                LoadNpcs();
                return Page();
            }
            catch (System.Exception ex)
            {
                var result = new { success = false, message = $"修改失败: {ex.Message}" };
                if (IsAjaxRequest()) return new JsonResult(result);
                Message = result.message;
                LoadNpcs();
                return Page();
            }
        }

        // 移动NPC到指定位置
        private bool MoveNpcToLocation(Models.NPCObject npcObject, Models.Map? fromMap, Models.Map targetMap, System.Drawing.Point location)
        {
            try
            {
                // 检查目标位置是否有效
                var cell = targetMap.GetCell(location);
                if (cell == null) return false;

                // 从当前位置移除
                if (fromMap != null && npcObject.CurrentCell != null)
                {
                    npcObject.CurrentCell.RemoveObject(npcObject);
                }

                // 设置新位置
                npcObject.CurrentLocation = location;
                npcObject.CurrentCell = cell;

                // 如果地图变了，更新地图引用
                if (fromMap != targetMap)
                {
                    fromMap?.NPCs?.Remove(npcObject);
                    targetMap.NPCs?.Add(npcObject);
                }

                // 广播位置变更给附近玩家
                npcObject.Broadcast(new Library.Network.ServerPackets.ObjectTurn
                {
                    ObjectID = npcObject.ObjectID,
                    Direction = npcObject.Direction,
                    Location = location
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        // 删除NPC
        public IActionResult OnPostDeleteNpc(int npcIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                var result = new { success = false, message = "权限不足，需要 SuperAdmin 权限" };
                if (IsAjaxRequest()) return new JsonResult(result);
                Message = result.message;
                LoadNpcs();
                return Page();
            }

            try
            {
                var npc = SEnvir.NPCInfoList?.Binding?.FirstOrDefault(n => n.Index == npcIndex);
                if (npc == null)
                {
                    var result = new { success = false, message = $"NPC索引 {npcIndex} 不存在" };
                    if (IsAjaxRequest()) return new JsonResult(result);
                    Message = result.message;
                    LoadNpcs();
                    return Page();
                }

                var npcName = npc.NPCName;

                // 删除关联的入口页面
                if (npc.EntryPage != null)
                {
                    DeletePageRecursive(npc.EntryPage);
                    npc.EntryPage = null;
                }

                // 从列表中移除
                SEnvir.NPCInfoList.Binding.Remove(npc);

                SEnvir.Log($"[Admin] 删除NPC: [{npcIndex}] {npcName}");
                var success = new { success = true, message = $"NPC [{npcIndex}] {npcName} 已删除" };
                if (IsAjaxRequest()) return new JsonResult(success);
                Message = success.message;
                LoadNpcs();
                return Page();
            }
            catch (System.Exception ex)
            {
                var result = new { success = false, message = $"删除失败: {ex.Message}" };
                if (IsAjaxRequest()) return new JsonResult(result);
                Message = result.message;
                LoadNpcs();
                return Page();
            }
        }

        #endregion

        #region NPC Page CRUD

        // 获取NPC页面详情
        public IActionResult OnGetNpcPageDetail(int pageIndex)
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var page = FindPage(pageIndex);
                if (page == null)
                    return new JsonResult(new { success = false, message = "NPC页面不存在" });

                var detail = new NpcPageDetailViewModel
                {
                    Index = page.Index,
                    Description = page.Description ?? "",
                    DialogType = page.DialogType.ToString(),
                    DialogTypeValue = (int)page.DialogType,
                    Say = page.Say ?? "",
                    Arguments = page.Arguments ?? "",
                    SuccessPageIndex = page.SuccessPage?.Index,
                    SuccessPageDescription = page.SuccessPage?.Description ?? "",
                    Checks = page.Checks?.Select(c => new NpcCheckViewModel
                    {
                        Index = c.Index,
                        CheckType = c.CheckType.ToString(),
                        CheckTypeValue = (int)c.CheckType,
                        Operator = c.Operator.ToString(),
                        OperatorValue = (int)c.Operator,
                        StringParameter1 = c.StringParameter1 ?? "",
                        IntParameter1 = c.IntParameter1,
                        IntParameter2 = c.IntParameter2,
                        ItemIndex = c.ItemParameter1?.Index ?? 0,
                        ItemName = c.ItemParameter1?.ItemName ?? "",
                        FailPageIndex = c.FailPage?.Index ?? 0,
                        FailPageDescription = c.FailPage?.Description ?? ""
                    }).ToList() ?? new List<NpcCheckViewModel>(),
                    Actions = page.Actions?.Select(a => new NpcActionViewModel
                    {
                        Index = a.Index,
                        ActionType = a.ActionType.ToString(),
                        ActionTypeValue = (int)a.ActionType,
                        StringParameter1 = a.StringParameter1 ?? "",
                        IntParameter1 = a.IntParameter1,
                        IntParameter2 = a.IntParameter2,
                        ItemIndex = a.ItemParameter1?.Index ?? 0,
                        ItemName = a.ItemParameter1?.ItemName ?? "",
                        MapIndex = a.MapParameter1?.Index ?? 0,
                        MapName = a.MapParameter1?.Description ?? ""
                    }).ToList() ?? new List<NpcActionViewModel>(),
                    Buttons = page.Buttons?.Select(b => new NpcButtonViewModel
                    {
                        Index = b.Index,
                        ButtonID = b.ButtonID,
                        DestinationPageIndex = b.DestinationPage?.Index ?? 0,
                        DestinationPageDescription = b.DestinationPage?.Description ?? ""
                    }).ToList() ?? new List<NpcButtonViewModel>(),
                    Goods = page.Goods?.Select(g => new NpcGoodViewModel
                    {
                        Index = g.Index,
                        ItemIndex = g.Item?.Index ?? 0,
                        ItemName = g.Item?.ItemName ?? "",
                        Rate = g.Rate,
                        Cost = g.Cost
                    }).ToList() ?? new List<NpcGoodViewModel>()
                };

                return new JsonResult(new { success = true, data = detail });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 为NPC创建入口页面
        public IActionResult OnPostCreateEntryPage(int npcIndex, string description, int dialogType, string say)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });

            try
            {
                var npc = SEnvir.NPCInfoList?.Binding?.FirstOrDefault(n => n.Index == npcIndex);
                if (npc == null)
                    return new JsonResult(new { success = false, message = "NPC不存在" });

                if (npc.EntryPage != null)
                    return new JsonResult(new { success = false, message = "NPC已有入口页面，请先删除现有页面" });

                var newPage = SEnvir.NPCPageList?.CreateNewObject();
                if (newPage == null)
                    return new JsonResult(new { success = false, message = "创建页面失败" });

                newPage.Description = description;
                newPage.DialogType = (NPCDialogType)dialogType;
                newPage.Say = say;
                npc.EntryPage = newPage;

                SEnvir.Log($"[Admin] 为NPC [{npcIndex}] 创建入口页面 [{newPage.Index}]");
                return new JsonResult(new { success = true, message = "入口页面创建成功", pageIndex = newPage.Index });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 更新NPC页面
        public IActionResult OnPostUpdateNpcPage(int pageIndex, string description, int dialogType, string say, string arguments)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });

            try
            {
                var page = FindPage(pageIndex);
                if (page == null)
                    return new JsonResult(new { success = false, message = $"NPC页面 {pageIndex} 不存在" });

                page.Description = description;
                page.DialogType = (NPCDialogType)dialogType;
                page.Say = say;
                page.Arguments = arguments;

                SEnvir.Log($"[Admin] 修改NPC页面: [{pageIndex}] {description}");
                return new JsonResult(new { success = true, message = $"NPC页面 [{pageIndex}] 已更新" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 删除NPC入口页面
        public IActionResult OnPostDeleteEntryPage(int npcIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });

            try
            {
                var npc = SEnvir.NPCInfoList?.Binding?.FirstOrDefault(n => n.Index == npcIndex);
                if (npc == null)
                    return new JsonResult(new { success = false, message = "NPC不存在" });

                if (npc.EntryPage == null)
                    return new JsonResult(new { success = false, message = "NPC没有入口页面" });

                var pageIndex = npc.EntryPage.Index;
                DeletePageRecursive(npc.EntryPage);
                npc.EntryPage = null;

                SEnvir.Log($"[Admin] 删除NPC [{npcIndex}] 入口页面 [{pageIndex}]");
                return new JsonResult(new { success = true, message = "入口页面已删除" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region NPCCheck CRUD

        public IActionResult OnPostAddCheck(int pageIndex, int checkType, int operatorType, string stringParam, int intParam1, int intParam2, int itemIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var page = FindPage(pageIndex);
                if (page == null)
                    return new JsonResult(new { success = false, message = "页面不存在" });

                var newCheck = SEnvir.NPCCheckList?.CreateNewObject();
                if (newCheck == null)
                    return new JsonResult(new { success = false, message = "创建检查条件失败" });

                newCheck.CheckType = (NPCCheckType)checkType;
                newCheck.Operator = (Operator)operatorType;
                newCheck.StringParameter1 = stringParam;
                newCheck.IntParameter1 = intParam1;
                newCheck.IntParameter2 = intParam2;

                if (itemIndex > 0)
                {
                    var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                    newCheck.ItemParameter1 = item;
                }

                newCheck.Page = page;
                page.Checks?.Add(newCheck);

                SEnvir.Log($"[Admin] 为页面 [{pageIndex}] 添加检查条件 [{newCheck.Index}]");
                return new JsonResult(new { success = true, message = "检查条件已添加", index = newCheck.Index });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public IActionResult OnPostUpdateCheck(int checkIndex, int checkType, int operatorType, string stringParam, int intParam1, int intParam2, int itemIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var check = SEnvir.NPCCheckList?.Binding?.FirstOrDefault(c => c.Index == checkIndex);
                if (check == null)
                    return new JsonResult(new { success = false, message = "检查条件不存在" });

                check.CheckType = (NPCCheckType)checkType;
                check.Operator = (Operator)operatorType;
                check.StringParameter1 = stringParam;
                check.IntParameter1 = intParam1;
                check.IntParameter2 = intParam2;

                if (itemIndex > 0)
                {
                    var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                    check.ItemParameter1 = item;
                }
                else
                {
                    check.ItemParameter1 = null;
                }

                SEnvir.Log($"[Admin] 更新检查条件 [{checkIndex}]");
                return new JsonResult(new { success = true, message = "检查条件已更新" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public IActionResult OnPostDeleteCheck(int checkIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var check = SEnvir.NPCCheckList?.Binding?.FirstOrDefault(c => c.Index == checkIndex);
                if (check == null)
                    return new JsonResult(new { success = false, message = "检查条件不存在" });

                check.Page?.Checks?.Remove(check);
                SEnvir.NPCCheckList?.Binding?.Remove(check);

                SEnvir.Log($"[Admin] 删除检查条件 [{checkIndex}]");
                return new JsonResult(new { success = true, message = "检查条件已删除" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region NPCAction CRUD

        public IActionResult OnPostAddAction(int pageIndex, int actionType, string stringParam, int intParam1, int intParam2, int itemIndex, int mapIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var page = FindPage(pageIndex);
                if (page == null)
                    return new JsonResult(new { success = false, message = "页面不存在" });

                var newAction = SEnvir.NPCActionList?.CreateNewObject();
                if (newAction == null)
                    return new JsonResult(new { success = false, message = "创建动作失败" });

                newAction.ActionType = (NPCActionType)actionType;
                newAction.StringParameter1 = stringParam;
                newAction.IntParameter1 = intParam1;
                newAction.IntParameter2 = intParam2;

                if (itemIndex > 0)
                {
                    var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                    newAction.ItemParameter1 = item;
                }

                if (mapIndex > 0)
                {
                    var map = SEnvir.MapInfoList?.Binding?.FirstOrDefault(m => m.Index == mapIndex);
                    newAction.MapParameter1 = map;
                }

                newAction.Page = page;
                page.Actions?.Add(newAction);

                SEnvir.Log($"[Admin] 为页面 [{pageIndex}] 添加动作 [{newAction.Index}]");
                return new JsonResult(new { success = true, message = "动作已添加", index = newAction.Index });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public IActionResult OnPostUpdateAction(int actionIndex, int actionType, string stringParam, int intParam1, int intParam2, int itemIndex, int mapIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var action = SEnvir.NPCActionList?.Binding?.FirstOrDefault(a => a.Index == actionIndex);
                if (action == null)
                    return new JsonResult(new { success = false, message = "动作不存在" });

                action.ActionType = (NPCActionType)actionType;
                action.StringParameter1 = stringParam;
                action.IntParameter1 = intParam1;
                action.IntParameter2 = intParam2;

                if (itemIndex > 0)
                {
                    var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                    action.ItemParameter1 = item;
                }
                else
                {
                    action.ItemParameter1 = null;
                }

                if (mapIndex > 0)
                {
                    var map = SEnvir.MapInfoList?.Binding?.FirstOrDefault(m => m.Index == mapIndex);
                    action.MapParameter1 = map;
                }
                else
                {
                    action.MapParameter1 = null;
                }

                SEnvir.Log($"[Admin] 更新动作 [{actionIndex}]");
                return new JsonResult(new { success = true, message = "动作已更新" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public IActionResult OnPostDeleteAction(int actionIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var action = SEnvir.NPCActionList?.Binding?.FirstOrDefault(a => a.Index == actionIndex);
                if (action == null)
                    return new JsonResult(new { success = false, message = "动作不存在" });

                action.Page?.Actions?.Remove(action);
                SEnvir.NPCActionList?.Binding?.Remove(action);

                SEnvir.Log($"[Admin] 删除动作 [{actionIndex}]");
                return new JsonResult(new { success = true, message = "动作已删除" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region NPCButton CRUD

        public IActionResult OnPostAddButton(int pageIndex, int buttonId)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var page = FindPage(pageIndex);
                if (page == null)
                    return new JsonResult(new { success = false, message = "页面不存在" });

                var newButton = SEnvir.NPCButtonList?.CreateNewObject();
                if (newButton == null)
                    return new JsonResult(new { success = false, message = "创建按钮失败" });

                newButton.ButtonID = buttonId;
                newButton.Page = page;
                page.Buttons?.Add(newButton);

                SEnvir.Log($"[Admin] 为页面 [{pageIndex}] 添加按钮 [{newButton.Index}]");
                return new JsonResult(new { success = true, message = "按钮已添加", index = newButton.Index });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public IActionResult OnPostUpdateButton(int buttonIndex, int buttonId, int destPageIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var button = SEnvir.NPCButtonList?.Binding?.FirstOrDefault(b => b.Index == buttonIndex);
                if (button == null)
                    return new JsonResult(new { success = false, message = "按钮不存在" });

                button.ButtonID = buttonId;

                if (destPageIndex > 0)
                {
                    var destPage = FindPage(destPageIndex);
                    button.DestinationPage = destPage;
                }
                else
                {
                    button.DestinationPage = null;
                }

                SEnvir.Log($"[Admin] 更新按钮 [{buttonIndex}]");
                return new JsonResult(new { success = true, message = "按钮已更新" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public IActionResult OnPostDeleteButton(int buttonIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var button = SEnvir.NPCButtonList?.Binding?.FirstOrDefault(b => b.Index == buttonIndex);
                if (button == null)
                    return new JsonResult(new { success = false, message = "按钮不存在" });

                button.Page?.Buttons?.Remove(button);
                SEnvir.NPCButtonList?.Binding?.Remove(button);

                SEnvir.Log($"[Admin] 删除按钮 [{buttonIndex}]");
                return new JsonResult(new { success = true, message = "按钮已删除" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 为按钮创建目标页面
        public IActionResult OnPostCreateButtonDestPage(int buttonIndex, string description, int dialogType, string say)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var button = SEnvir.NPCButtonList?.Binding?.FirstOrDefault(b => b.Index == buttonIndex);
                if (button == null)
                    return new JsonResult(new { success = false, message = "按钮不存在" });

                if (button.DestinationPage != null)
                    return new JsonResult(new { success = false, message = "按钮已有目标页面" });

                var newPage = SEnvir.NPCPageList?.CreateNewObject();
                if (newPage == null)
                    return new JsonResult(new { success = false, message = "创建页面失败" });

                newPage.Description = description;
                newPage.DialogType = (NPCDialogType)dialogType;
                newPage.Say = say;
                button.DestinationPage = newPage;

                SEnvir.Log($"[Admin] 为按钮 [{buttonIndex}] 创建目标页面 [{newPage.Index}]");
                return new JsonResult(new { success = true, message = "目标页面创建成功", pageIndex = newPage.Index });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region NPCGood CRUD

        public IActionResult OnPostAddGood(int pageIndex, int itemIndex, decimal rate)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var page = FindPage(pageIndex);
                if (page == null)
                    return new JsonResult(new { success = false, message = "页面不存在" });

                var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                if (item == null)
                    return new JsonResult(new { success = false, message = "物品不存在" });

                var newGood = SEnvir.NPCGoodList?.CreateNewObject();
                if (newGood == null)
                    return new JsonResult(new { success = false, message = "创建商品失败" });

                newGood.Item = item;
                newGood.Rate = rate > 0 ? rate : 1M;
                newGood.Page = page;
                page.Goods?.Add(newGood);

                SEnvir.Log($"[Admin] 为页面 [{pageIndex}] 添加商品 [{newGood.Index}] {item.ItemName}");
                return new JsonResult(new { success = true, message = "商品已添加", index = newGood.Index });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public IActionResult OnPostUpdateGood(int goodIndex, int itemIndex, decimal rate)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var good = SEnvir.NPCGoodList?.Binding?.FirstOrDefault(g => g.Index == goodIndex);
                if (good == null)
                    return new JsonResult(new { success = false, message = "商品不存在" });

                if (itemIndex > 0)
                {
                    var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                    good.Item = item;
                }

                good.Rate = rate > 0 ? rate : 1M;

                SEnvir.Log($"[Admin] 更新商品 [{goodIndex}]");
                return new JsonResult(new { success = true, message = "商品已更新" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public IActionResult OnPostDeleteGood(int goodIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var good = SEnvir.NPCGoodList?.Binding?.FirstOrDefault(g => g.Index == goodIndex);
                if (good == null)
                    return new JsonResult(new { success = false, message = "商品不存在" });

                good.Page?.Goods?.Remove(good);
                SEnvir.NPCGoodList?.Binding?.Remove(good);

                SEnvir.Log($"[Admin] 删除商品 [{goodIndex}]");
                return new JsonResult(new { success = true, message = "商品已删除" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region Helper APIs

        // 获取在线NPC对象
        public IActionResult OnGetOnlineNpcs()
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var npcObjects = new List<NpcObjectViewModel>();

                foreach (var map in SEnvir.Maps.Values)
                {
                    if (map?.NPCs == null) continue;

                    foreach (var npc in map.NPCs)
                    {
                        if (npc?.NPCInfo == null) continue;

                        npcObjects.Add(new NpcObjectViewModel
                        {
                            ObjectID = npc.ObjectID,
                            NPCIndex = npc.NPCInfo.Index,
                            NPCName = npc.NPCInfo.NPCName ?? "Unknown",
                            MapName = map.Info?.Description ?? "Unknown",
                            LocationX = npc.CurrentLocation.X,
                            LocationY = npc.CurrentLocation.Y
                        });
                    }
                }

                return new JsonResult(new { success = true, data = npcObjects, total = npcObjects.Count });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取区域列表
        public IActionResult OnGetRegions()
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var regions = SEnvir.MapRegionList?.Binding?
                    .Select(r => new { index = r.Index, description = r.ServerDescription ?? "", mapName = r.Map?.Description ?? "", mapIndex = r.Map?.Index ?? 0 })
                    .OrderBy(r => r.mapName)
                    .ThenBy(r => r.description)
                    .ToList();

                return new JsonResult(new { success = true, data = regions });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取所有地图列表
        public IActionResult OnGetAllMaps()
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var maps = SEnvir.MapInfoList?.Binding?
                    .Select(m => new { index = m.Index, name = m.Description ?? "", fileName = m.FileName ?? "" })
                    .OrderBy(m => m.name)
                    .ToList();

                return new JsonResult(new { success = true, data = maps });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取指定地图的区域列表
        public IActionResult OnGetMapRegions(int mapIndex)
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var regions = SEnvir.MapRegionList?.Binding?
                    .Where(r => r.Map?.Index == mapIndex)
                    .Select(r => new {
                        index = r.Index,
                        description = r.Description ?? "",
                        size = r.Size,
                        pointCount = r.PointList?.Count ?? 0
                    })
                    .OrderBy(r => r.description)
                    .ToList();

                return new JsonResult(new { success = true, data = regions });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取地图信息（包括尺寸）
        public IActionResult OnGetMapInfo(int mapIndex)
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var mapInfo = SEnvir.MapInfoList?.Binding?.FirstOrDefault(m => m.Index == mapIndex);
                if (mapInfo == null)
                    return new JsonResult(new { success = false, message = "地图不存在" });

                // 尝试获取地图实例以获取实际尺寸
                var map = SEnvir.Maps.Values.FirstOrDefault(m => m.Info?.Index == mapIndex);
                int width = map?.Width ?? 0;
                int height = map?.Height ?? 0;

                return new JsonResult(new {
                    success = true,
                    data = new {
                        index = mapInfo.Index,
                        name = mapInfo.Description ?? "",
                        fileName = mapInfo.FileName ?? "",
                        width = width,
                        height = height
                    }
                });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取区域信息（包括中心坐标）
        public IActionResult OnGetRegionInfo(int regionIndex)
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var region = SEnvir.MapRegionList?.Binding?.FirstOrDefault(r => r.Index == regionIndex);
                if (region == null)
                    return new JsonResult(new { success = false, message = "区域不存在" });

                // 计算区域中心坐标
                int centerX = 0, centerY = 0;
                if (region.PointList != null && region.PointList.Count > 0)
                {
                    centerX = (int)region.PointList.Average(p => p.X);
                    centerY = (int)region.PointList.Average(p => p.Y);
                }

                return new JsonResult(new {
                    success = true,
                    data = new {
                        index = region.Index,
                        description = region.Description ?? "",
                        mapIndex = region.Map?.Index ?? 0,
                        mapName = region.Map?.Description ?? "",
                        centerX = centerX,
                        centerY = centerY,
                        pointCount = region.PointList?.Count ?? 0
                    }
                });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取对话类型列表
        public IActionResult OnGetDialogTypes()
        {
            var types = System.Enum.GetValues(typeof(NPCDialogType))
                .Cast<NPCDialogType>()
                .Select(t => new { value = (int)t, name = t.ToString() })
                .ToList();
            return new JsonResult(new { success = true, data = types });
        }

        // 获取检查类型列表
        public IActionResult OnGetCheckTypes()
        {
            var types = System.Enum.GetValues(typeof(NPCCheckType))
                .Cast<NPCCheckType>()
                .Select(t => new { value = (int)t, name = t.ToString() })
                .ToList();
            return new JsonResult(new { success = true, data = types });
        }

        // 获取操作符列表
        public IActionResult OnGetOperators()
        {
            var ops = System.Enum.GetValues(typeof(Operator))
                .Cast<Operator>()
                .Select(o => new { value = (int)o, name = o.ToString() })
                .ToList();
            return new JsonResult(new { success = true, data = ops });
        }

        // 获取动作类型列表
        public IActionResult OnGetActionTypes()
        {
            var types = System.Enum.GetValues(typeof(NPCActionType))
                .Cast<NPCActionType>()
                .Select(t => new { value = (int)t, name = t.ToString() })
                .ToList();
            return new JsonResult(new { success = true, data = types });
        }

        // 搜索物品
        public IActionResult OnGetSearchItems(string keyword)
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var items = SEnvir.ItemInfoList?.Binding?
                    .Where(i => string.IsNullOrEmpty(keyword) ||
                               (i.ItemName?.Contains(keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                               i.Index.ToString().Contains(keyword))
                    .Take(50)
                    .Select(i => new { index = i.Index, name = i.ItemName ?? "", price = i.Price })
                    .ToList();

                return new JsonResult(new { success = true, data = items });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 搜索地图
        public IActionResult OnGetSearchMaps(string keyword)
        {
            if (!HasPermission(AccountIdentity.Supervisor))
                return new JsonResult(new { success = false, message = "权限不足" });

            try
            {
                var maps = SEnvir.MapInfoList?.Binding?
                    .Where(m => string.IsNullOrEmpty(keyword) ||
                               (m.Description?.Contains(keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                               m.Index.ToString().Contains(keyword))
                    .Take(50)
                    .Select(m => new { index = m.Index, name = m.Description ?? "" })
                    .ToList();

                return new JsonResult(new { success = true, data = maps });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region Private Helpers

        private NPCPage? FindPage(int pageIndex)
        {
            // 先从NPCPageList查找
            var page = SEnvir.NPCPageList?.Binding?.FirstOrDefault(p => p.Index == pageIndex);
            if (page != null) return page;

            // 如果没找到，遍历NPC查找
            foreach (var npc in SEnvir.NPCInfoList?.Binding ?? Enumerable.Empty<NPCInfo>())
            {
                if (npc.EntryPage?.Index == pageIndex)
                    return npc.EntryPage;

                page = FindPageInHierarchy(npc.EntryPage, pageIndex);
                if (page != null) return page;
            }
            return null;
        }

        private NPCPage? FindPageInHierarchy(NPCPage? page, int targetIndex, HashSet<int>? visited = null)
        {
            if (page == null) return null;

            visited ??= new HashSet<int>();
            if (visited.Contains(page.Index)) return null;
            visited.Add(page.Index);

            if (page.Index == targetIndex) return page;

            var found = FindPageInHierarchy(page.SuccessPage, targetIndex, visited);
            if (found != null) return found;

            if (page.Checks != null)
            {
                foreach (var check in page.Checks)
                {
                    found = FindPageInHierarchy(check.FailPage, targetIndex, visited);
                    if (found != null) return found;
                }
            }

            if (page.Buttons != null)
            {
                foreach (var button in page.Buttons)
                {
                    found = FindPageInHierarchy(button.DestinationPage, targetIndex, visited);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private void DeletePageRecursive(NPCPage? page, HashSet<int>? visited = null)
        {
            if (page == null) return;

            visited ??= new HashSet<int>();
            if (visited.Contains(page.Index)) return;
            visited.Add(page.Index);

            // 删除子页面
            if (page.SuccessPage != null)
            {
                DeletePageRecursive(page.SuccessPage, visited);
                page.SuccessPage = null;
            }

            // 删除检查条件及其失败页面
            if (page.Checks != null)
            {
                foreach (var check in page.Checks.ToList())
                {
                    if (check.FailPage != null)
                    {
                        DeletePageRecursive(check.FailPage, visited);
                        check.FailPage = null;
                    }
                    SEnvir.NPCCheckList?.Binding?.Remove(check);
                }
                page.Checks.Clear();
            }

            // 删除动作
            if (page.Actions != null)
            {
                foreach (var action in page.Actions.ToList())
                    SEnvir.NPCActionList?.Binding?.Remove(action);
                page.Actions.Clear();
            }

            // 删除按钮及其目标页面
            if (page.Buttons != null)
            {
                foreach (var button in page.Buttons.ToList())
                {
                    if (button.DestinationPage != null)
                    {
                        DeletePageRecursive(button.DestinationPage, visited);
                        button.DestinationPage = null;
                    }
                    SEnvir.NPCButtonList?.Binding?.Remove(button);
                }
                page.Buttons.Clear();
            }

            // 删除商品
            if (page.Goods != null)
            {
                foreach (var good in page.Goods.ToList())
                    SEnvir.NPCGoodList?.Binding?.Remove(good);
                page.Goods.Clear();
            }

            // 删除页面本身
            SEnvir.NPCPageList?.Binding?.Remove(page);
        }

        private bool HasPermission(AccountIdentity required)
        {
            var permissionClaim = User.FindFirst("Permission")?.Value;
            if (string.IsNullOrEmpty(permissionClaim)) return false;

            if (int.TryParse(permissionClaim, out int permValue))
                return permValue >= (int)required;
            return false;
        }

        #endregion
    }

    #region ViewModels

    public class NpcViewModel
    {
        public int Index { get; set; }
        public string NPCName { get; set; } = "";
        public int Image { get; set; }
        public int RegionIndex { get; set; }
        public string RegionName { get; set; } = "";
        public string MapName { get; set; } = "";
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public bool IsOnline { get; set; }
        public bool HasEntryPage { get; set; }
        public int EntryPageIndex { get; set; }
        public string EntryPageDescription { get; set; } = "";
        public int StartQuestsCount { get; set; }
        public int FinishQuestsCount { get; set; }
    }

    public class NpcDetailViewModel
    {
        public int Index { get; set; }
        public string NPCName { get; set; } = "";
        public int Image { get; set; }
        public int RegionIndex { get; set; }
        public string RegionName { get; set; } = "";
        public int MapIndex { get; set; }
        public string MapName { get; set; } = "";
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public int EntryPageIndex { get; set; }
        public string EntryPageDescription { get; set; } = "";
        public string EntryPageDialogType { get; set; } = "";
        public string EntryPageSay { get; set; } = "";
        public int StartQuestsCount { get; set; }
        public int FinishQuestsCount { get; set; }
        public int ChecksCount { get; set; }
        public int ActionsCount { get; set; }
        public int ButtonsCount { get; set; }
        public int GoodsCount { get; set; }
    }

    public class NpcPageDetailViewModel
    {
        public int Index { get; set; }
        public string Description { get; set; } = "";
        public string DialogType { get; set; } = "";
        public int DialogTypeValue { get; set; }
        public string Say { get; set; } = "";
        public string Arguments { get; set; } = "";
        public int? SuccessPageIndex { get; set; }
        public string SuccessPageDescription { get; set; } = "";
        public List<NpcCheckViewModel> Checks { get; set; } = new();
        public List<NpcActionViewModel> Actions { get; set; } = new();
        public List<NpcButtonViewModel> Buttons { get; set; } = new();
        public List<NpcGoodViewModel> Goods { get; set; } = new();
    }

    public class NpcCheckViewModel
    {
        public int Index { get; set; }
        public string CheckType { get; set; } = "";
        public int CheckTypeValue { get; set; }
        public string Operator { get; set; } = "";
        public int OperatorValue { get; set; }
        public string StringParameter1 { get; set; } = "";
        public int IntParameter1 { get; set; }
        public int IntParameter2 { get; set; }
        public int ItemIndex { get; set; }
        public string ItemName { get; set; } = "";
        public int FailPageIndex { get; set; }
        public string FailPageDescription { get; set; } = "";
    }

    public class NpcActionViewModel
    {
        public int Index { get; set; }
        public string ActionType { get; set; } = "";
        public int ActionTypeValue { get; set; }
        public string StringParameter1 { get; set; } = "";
        public int IntParameter1 { get; set; }
        public int IntParameter2 { get; set; }
        public int ItemIndex { get; set; }
        public string ItemName { get; set; } = "";
        public int MapIndex { get; set; }
        public string MapName { get; set; } = "";
    }

    public class NpcButtonViewModel
    {
        public int Index { get; set; }
        public int ButtonID { get; set; }
        public int DestinationPageIndex { get; set; }
        public string DestinationPageDescription { get; set; } = "";
    }

    public class NpcGoodViewModel
    {
        public int Index { get; set; }
        public int ItemIndex { get; set; }
        public string ItemName { get; set; } = "";
        public decimal Rate { get; set; }
        public int Cost { get; set; }
    }

    public class NpcObjectViewModel
    {
        public uint ObjectID { get; set; }
        public int NPCIndex { get; set; }
        public string NPCName { get; set; } = "";
        public string MapName { get; set; } = "";
        public int LocationX { get; set; }
        public int LocationY { get; set; }
    }

    #endregion
}
