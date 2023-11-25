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
    internal class Fastfall : IPatch
    {
        private Environment Env;
        public Fastfall(Environment env) { Env = env; }

        public string Name { get; } = "Fast Fall";

        public string Description { get; } = "Holding the Down-Button while descending enters fast fall mode, in which you are descending more quickly.";

        public bool Enabled { get; protected set; }

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("FastFall_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;

            var timingEntry = cat.CreateEntry<float>("FastFall_TimingS", TimingS);
            timingEntry.DisplayName = "Fast Fall Timing";
            timingEntry.Description = "Time in Seconds for how long the down key must be held to enter fast fall.";
            TimingS = timingEntry.Value;

            var inertiaEntry = cat.CreateEntry<float>("FastFall_InertiaBoost", Inertia);
            inertiaEntry.DisplayName = "Fast Fall Inertia";
            inertiaEntry.Description = "Boost to downwards velocity when entering fast fall.";
            Inertia = inertiaEntry.Value;
        }

        public static Fastfall? CurrInstance = null;


        private static float TimingS = 0.05f;
        private static float Inertia = 8.0f;

        private float timePassed;
        private RicassoActor? lastActor;
        private bool inFastFall = false;

        private static void Postfix_StartJump(RicassoActor __instance, RicassoActor.JumpType jumpType)
        {
            CurrInstance.timePassed = 0; //blindnly assuming ControlJumpInertia gets called every frame so deltaFrame will be enough
            CurrInstance.lastActor = __instance;
            CurrInstance.inFastFall = false;
        }

        private static void Postfix_ControlJumpInertia(HumanoidCharacter __instance)
        {
            if (__instance != CurrInstance.lastActor) return;
            if (__instance.GetInertia().y > 0) return;
            bool pressed = __instance.input.down.IsPress;

            if (!pressed)
            {
                CurrInstance.timePassed = 0;
            }
            else {
                CurrInstance.timePassed += UnityEngine.Time.deltaTime;
                if(CurrInstance.timePassed > TimingS) {
                    __instance.AddInertiaY(-Inertia, 999);
                    CurrInstance.lastActor = null;
                }
            }
        }

        public void Patch()
        {
            CurrInstance = this;
            if (!Enabled) return;

            MethodInfo? method = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StartJump), new Type[] { typeof(RicassoActor.JumpType) });
            MethodInfo? replacement = typeof(Fastfall).GetMethod(nameof(Postfix_StartJump), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException($"Failed to find {(method == null ? "Injection" : "Replacement")} Method: Postfix_StartJump");
            }
            Env.Harmony.Patch(method, null, new HarmonyLib.HarmonyMethod(replacement), null);

            method = typeof(HumanoidCharacter).GetMethod(nameof(HumanoidCharacter.ControlJumpInertia));
            replacement = typeof(Fastfall).GetMethod(nameof(Postfix_ControlJumpInertia), BindingFlags.Static | BindingFlags.NonPublic);
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
