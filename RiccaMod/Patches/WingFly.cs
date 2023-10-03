using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Il2Cpp.RicassoActor;

namespace RiccaMod.Patches
{
    internal class WingFly : IPatch
    {
        private Environment Env;
        public WingFly(Environment env) { Env = env; }

        public string Name { get; } = "Wing Fly";

        public string Description { get; } = "Equiping a Wing will actually give you the infinite flight from the boss fight.";

        public bool Enabled { get; protected set; }

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("WingFly_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;
        }

        public static WingFly? CurrInstance = null;

        private static void Postfix_PostInitialize(RicassoActor __instance)
        {
            foreach (var v in __instance.costumeChanger.costumeEquipmentList)
            {
                CurrInstance.Env.Logger.Msg($"{v.gameObject.name} {v.name} {v.coverMesh.name} {v.equipmentSlot}");
            }
            /*bool hasWing = false;
            if(__instance.costumeChanger.wingEquip != null)
            {
                hasWing = __instance.costumeChanger.wingEquip.CostumePartProfile.isWing;
            }
            CurrInstance.Env.Logger.Msg($"Spawned with wing = {hasWing}");
            if (hasWing)
            {
                __instance.hasEnergyWing = true;
                __instance.isEqupimentWing = true;
            }*/
        }

        public void Patch()
        {
            CurrInstance = this;
            if (!Enabled) return;

            MethodInfo? method = typeof(RicassoActor).GetMethod(nameof(RicassoActor.PostInitialize));
            MethodInfo? replacement = typeof(WingFly).GetMethod(nameof(Postfix_PostInitialize), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException("Failed to find Replacement Method: PostInitialize");
            }
            Env.Harmony.Patch(method, null, new HarmonyLib.HarmonyMethod(replacement), null);
        }

        public void Unpatch()
        {
            if (!Enabled) return;
        }
    }
}
