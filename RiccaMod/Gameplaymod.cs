using MelonLoader;
using UnityEngine;
using Il2Cpp;
using System.Reflection;
using RiccaMod.Patches;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace RiccaMod
{
    public class Gameplaymod : MelonMod
    {

        List<IPatch> patches = new List<IPatch>();
        private void InitAllPatches()
        {
            var cat = MelonPreferences.CreateCategory("SomeRiccaGameplayMod");
            cat.SetFilePath("Mods/RiccaMod.cfg");

            Patches.Environment env = new Patches.Environment(this.LoggerInstance, this.HarmonyInstance);
            patches.Add(new RisingGuards(env));
            patches.Add(new ShortHop(env));
            //patches.Add(new WingFly(env));
            patches.Add(new AttackCombos(env));
            patches.Add(new FasterDashAttack(env));
            patches.Add(new Balance(env));
            patches.Add(new Demosaic(env));

            foreach (IPatch patch in patches)
            {
                patch.LoadSettings(cat);
            }
        }

        public override void OnInitializeMelon()
        {
            InitAllPatches();
            foreach (IPatch patch in patches)
            {
                if (patch.Enabled)
                {
                    try
                    {
                        patch.Patch();
                        LoggerInstance.Msg($"Applied Gameplay-Mod [{patch.Name}]");
                    }
                    catch (Exception e)
                    {
                        LoggerInstance.Error($"Failed to Apply Gameplay-Mod [{patch.Name}]:\r\n{e}]");
                    }
                }
                else
                {
                    LoggerInstance.Msg($"Did not apply Gameplay-Mod [{patch.Name}] (was disabled)");
                }

            }

            LoggerInstance.Msg($"Test loaded.");
        }

        
        public override void OnDeinitializeMelon()
        {
            foreach (IPatch patch in patches)
            {
                patch.Unpatch();
            }
        }

    }
}