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
        public override string Version => "1.0.1";
        
        public override void OnEngineInit() {
            Harmony harmony = new Harmony($"net.{Author}.{Name}");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(ListEditor))]
        class ListEditor_Patch
        {
            readonly static List<ListEditor> listList = new List<ListEditor>();

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

            [HarmonyPatch("Setup")]
            [HarmonyPostfix]
            public static void ListSetup(ListEditor __instance) {
                listList.Add(__instance);
                __instance.Slot.ChildAdded += (s, c) => CleanUpForeignItems(__instance);
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
                if (____targetList.Target != null && !___setup && listList.Contains(__instance))
                {
                    ___setup = true;
                    ____registeredList = ____targetList.Target;
                    __instance.Slot.DestroyChildren();
                    ____targetList.Target.ElementsAdded += (list, index, count) => Target_ElementsAdded(__instance, list, index, count);
                    ____targetList.Target.ElementsRemoved += AccessTools.Method(__instance.GetType(), "Target_ElementsRemoved")
                        .CreateDelegate(typeof(SyncListElementsEvent), __instance) as SyncListElementsEvent;
                    ____targetList.Target.ListCleared += AccessTools.Method(__instance.GetType(), "Target_ListCleared")
                        .CreateDelegate(typeof(SyncListEvent), __instance) as SyncListEvent;
                    Target_ElementsAdded(__instance, ____targetList.Target, 0, ____targetList.Target.Count);
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