using Harmony;
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
    internal class ShortHop : IPatch
    {
        
        private Environment Env;
        public ShortHop(Environment env) { Env = env; }

        public string Name { get; } = "Short Hop";

        public string Description { get; } = "Tapping the Jump button will result in a small hop rather than the larger, full jump that you usually do.";

        public bool Enabled { get; protected set; }

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("ShortHop_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;

            var timingEntry = cat.CreateEntry<float>("ShortHop_TimingS", TimingS);
            timingEntry.DisplayName = "Short Hop Timing";
            timingEntry.Description = "Time in Seconds one has to release the jump button before it stops counting as a short hop.";
            TimingS = timingEntry.Value;

            var inertiaEntry = cat.CreateEntry<float>("ShortHop_Inertia", Inertia);
            inertiaEntry.DisplayName = "Short Hop Inertia";
            inertiaEntry.Description = "Modified Inertia Value for a short hop.";
            Inertia = inertiaEntry.Value;
        }

        public static ShortHop? CurrInstance = null;


        private static float TimingS = 0.03f;
        private static float Inertia = 5.0f;

        private float timePassed;
        private RicassoActor? lastActor;

        private static void Postfix_StartJump(RicassoActor __instance, RicassoActor.JumpType jumpType)
        {
            if (jumpType == JumpType.StandJump)
            {
                //float f = __instance.input.jump.lastTriggerTime;
                //CurrInstance.lastTriggerTime = f;
                //well, turns out lastTriggerTime is actually unused and always contains -9999. bummer.
                CurrInstance.timePassed = 0; //blindnly assuming ControlJumpInertia gets called every frame so deltaFrame will be enough
                CurrInstance.lastActor = __instance;
            }

        }

        private static void Postfix_ControlJumpInertia(HumanoidCharacter __instance)
        {
            if (__instance != CurrInstance.lastActor) return;
            var jumpInpput = __instance.input.jump;
            bool pressed = jumpInpput.IsPress;

            CurrInstance.timePassed += UnityEngine.Time.deltaTime;
            if (!pressed)
            {
                UnityEngine.Vector3 vec = __instance.GetInertia();
                vec.y = Inertia;
                __instance.SetInertia(vec, false);
                CurrInstance.lastActor = null;
            }
            if (CurrInstance.timePassed >= TimingS)
            {
                CurrInstance.lastActor = null;
            }
        }

        public void Patch()
        {
            CurrInstance = this;
            if (!Enabled) return;

            MethodInfo? method = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StartJump), new Type[] { typeof(RicassoActor.JumpType) });
            MethodInfo? replacement = typeof(ShortHop).GetMethod(nameof(Postfix_StartJump), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException($"Failed to find {(method == null ? "Injection" : "Replacement")} Method: Postfix_StartJump");
            }
            Env.Harmony.Patch(method, null, new HarmonyLib.HarmonyMethod(replacement), null);

            method = typeof(HumanoidCharacter).GetMethod(nameof(HumanoidCharacter.ControlJumpInertia));
            replacement = typeof(ShortHop).GetMethod(nameof(Postfix_ControlJumpInertia), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException($"Failed to find {(method == null ? "Injection" : "Replacement")} Method: ControlJumpInertia");
            }
            Env.Harmony.Patch(method, null, new HarmonyLib.HarmonyMethod(replacement), null);
        }

        public void Unpatch()
        {
            if (!Enabled) return;
        }

    }
}
