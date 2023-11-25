using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RiccaMod.Patches
{
    internal class Balance : IPatch
    {
        private Environment Env;
        public Balance(Environment env) { Env = env; }
        public string Name => "Balance";

        public string Description => "Changes a few minor things, like the counter spear not actually doing more damage on counters.";

        public bool Enabled { get; protected set; }

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("Balance_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;

            var guardBonusEntry = cat.CreateEntry<float>("Balance_GuardBonusMultipler", GuardPowerMultiplier);
            guardBonusEntry.DisplayName = "Guard Bonus Multiplier";
            guardBonusEntry.Description = "That one spear has a bonus that increases Counter Damage, but the bonus doesnt actually do anything. This gives that bonus a multiplier on its counter damage.";
            GuardPowerMultiplier = guardBonusEntry.Value;
        }

        private static float GuardPowerMultiplier = 2.0f;

        public static void Postfix_CalcCounterAttackInfo(RicassoActor __instance, ref Il2Cpp.AttackInfo __result, Il2Cpp.AttackInfo attackInfo, float damage, float counterDamagescale)
        {
            if(__instance == null || __instance.abnormalData == null) return;
            if ((__instance.abnormalData.StateMask & Il2CppACT.AbnormalState.CounterBoost) != 0)
            {
                __result.attackPower *= GuardPowerMultiplier;
            }
        }

        public void Patch()
        {
            if (!Enabled) return;

            MethodInfo? method = typeof(RicassoActor).GetMethod(nameof(RicassoActor.CalcCounterAttackInfo));
            MethodInfo? replacement = typeof(Balance).GetMethod(nameof(Postfix_CalcCounterAttackInfo));
            if(method == null || replacement == null)
            {
                throw new InvalidOperationException("Failed to find Replacement Method: CalcCounterAttackInfo");
            }
            Env.Harmony.Patch(method, null, new HarmonyLib.HarmonyMethod(replacement), null);
        }

        public void Unpatch()
        {
            if (!Enabled) return;
        }
    }
}
