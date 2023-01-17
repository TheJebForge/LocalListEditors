using HarmonyLib;
using NeosModLoader;
using FrooxEngine;
using FrooxEngine.UIX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
// ReSharper disable InconsistentNaming

namespace LocalListEditors
{
    public class LocalListEditors : NeosMod
    {
        public override string Name => "LocalListEditors";
        public override string Author => "TheJebForge";
        public override string Version => "1.1.0";
        
        [AutoRegisterConfigKey]
        readonly ModConfigurationKey<bool> ENABLED = new ModConfigurationKey<bool>("enabled","If mod functionality is enabled", () => true);

        ModConfiguration config;
        static LocalListEditors modInstance;

        public override void OnEngineInit() {
            modInstance = this;
            config = GetConfiguration();
            
            Harmony harmony = new Harmony($"net.{Author}.{Name}");
            harmony.PatchAll();
        }

        bool IsModEnabled(IWorldElement __instance) => config.GetValue(ENABLED) && !__instance.World.IsAuthority;

        [HarmonyPatch(typeof(ListEditor))]
        class ListEditor_Patch
        {
            readonly static List<ListEditor> listList = new List<ListEditor>();
            readonly static Dictionary<ListEditor, ISyncList> listTargets = new Dictionary<ListEditor, ISyncList>();

            static void CleanUpForeignItems(ListEditor __instance) {
                var userID = __instance.LocalUser.UserID;

                __instance.World?.RunSynchronously(() =>
                {
                    foreach (var child in __instance.Slot.Children
                        .Where(child => !child.Name.Contains("(" + userID + " Local)"))) {
                        child.Destroy();
                    }
                });
            }
            
            [HarmonyPatch("AddNewPressed")]
            [HarmonyPrefix]
            static bool AddNewPressed(ListEditor __instance, IButton button, ButtonEventData eventData)
            {
                if (!modInstance.IsModEnabled(__instance)) return true;
                
                var targetList = listTargets[__instance];
                
                if (targetList is ConflictingSyncElement target && target.DirectAccessOnly && !__instance.LocalUser.IsDirectlyInteracting())
                    return false;
                targetList.AddElement();

                return false;
            }
            
            [HarmonyPatch("RemovePressed")]
            [HarmonyPrefix]
            static bool RemovePressed(ListEditor __instance, IButton button, ButtonEventData eventData) {
                if (!modInstance.IsModEnabled(__instance)) return true;
                
                var targetList = listTargets[__instance];
                
                if (targetList is ConflictingSyncElement target && target.DirectAccessOnly && !__instance.LocalUser.IsDirectlyInteracting())
                    return false;
                for (var index = 0; index < __instance.Slot.ChildrenCount; ++index) {
                    if (__instance.Slot[index].GetComponentsInChildren<Button>().All(b => b != button)) continue;
                    targetList?.RemoveElement(index);
                    break;
                }

                return false;
            }

            [HarmonyPatch("Setup")]
            [HarmonyPrefix]
            public static bool ListSetup(
                ListEditor __instance, 
                SyncRef<Button> ____addNewButton,
                ISyncList target, 
                Button button) {
                if (!modInstance.IsModEnabled(__instance)) return true;
                
                listTargets.Add(__instance, target);
                
                ____addNewButton.Target = button;
                ____addNewButton.Target.Pressed.Target = AccessTools.Method(__instance.GetType(), "AddNewPressed")
                    .CreateDelegate(typeof(ButtonEventHandler), __instance) as ButtonEventHandler;

                listList.Add(__instance);
                __instance.Slot.ChildAdded += (s, c) => CleanUpForeignItems(__instance);

                return false;
            }
            
            static void Target_ElementsAdded(ListEditor __instance, ISyncList list, int startIndex, int count) => __instance.World?.RunSynchronously(() =>
            {
                if (!listList.Contains(__instance))
                    return;
                
                var userID = __instance.LocalUser.UserID;

                for (var index = startIndex; index < startIndex + count; ++index)
                {
                    var root = __instance.Slot.InsertSlot(index, "Element (" + userID + " Local)");
                    AccessTools.Method(__instance.GetType(), "BuildListItem")
                        .Invoke(__instance, new object[] { list, index, root });
                }
            });

            [HarmonyPatch("OnChanges")]
            [HarmonyPrefix]
            public static bool ChangesPatch(
                ListEditor __instance,
                ref SyncRef<ISyncList> ____targetList,
                ref ISyncList ____registeredList,
                ref bool ___setup,
                ref bool ___reindex) {
                if (!modInstance.IsModEnabled(__instance)) return true;

                var listTarget = ____targetList.Target ?? listTargets[__instance];
                
                if (listTarget != null && !___setup && listList.Contains(__instance))
                {
                    ___setup = true;
                    ____registeredList = listTarget;
                    __instance.Slot.DestroyChildren();
                    listTarget.ElementsAdded += (list, index, count) => Target_ElementsAdded(__instance, list, index, count);
                    listTarget.ElementsRemoved += AccessTools.Method(__instance.GetType(), "Target_ElementsRemoved")
                        .CreateDelegate(typeof(SyncListElementsEvent), __instance) as SyncListElementsEvent;
                    listTarget.ListCleared += AccessTools.Method(__instance.GetType(), "Target_ListCleared")
                        .CreateDelegate(typeof(SyncListEvent), __instance) as SyncListEvent;
                    Target_ElementsAdded(__instance, listTarget, 0, listTarget.Count);
                    __instance.RunInSeconds(0.5f, () => CleanUpForeignItems(__instance));
                }
                if (!___reindex)
                    return false;

                AccessTools.Method(__instance.GetType(), "Reindex").Invoke(__instance, null);
                ___reindex = false;

                return false;
            }
        }
    }
}