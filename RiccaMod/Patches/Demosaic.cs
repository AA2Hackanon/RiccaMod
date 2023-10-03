using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;
using Il2CppInterop.Runtime.Injection;

namespace RiccaMod.Patches
{
    internal class Demosaic : IPatch
    {
        private Environment Env;
        public Demosaic(Environment env) { Env = env; }
        public string Name => "Demosaic";

        public string Description => "( ͡° ͜ʖ ͡°)";

        public bool Enabled { get; private set;  } = true;

        private static string MosaicMaterial = "MoguraMosaicVCol";
        private static Demosaic? CurrInstance;

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("Demosaic_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;
        }

        static void NoMosaic(ACTCoverMesh __instance)
        {
            foreach (var i in __instance.rendererInfoList)
            {
                if (i.skinnedMeshRenderer) foreach (var mat in i.skinnedMeshRenderer.materials)
                    {
                        if (mat.shader.name.Contains(MosaicMaterial))
                        {
                            mat.shader.maximumLOD = -2;
                        }
                    }
            }
        }

        public void Patch()
        {
            CurrInstance = this;
            if (!Enabled) return;
            MethodInfo?[] injections = new MethodInfo?[] { 
                typeof(ACTCoverMesh).GetMethod(nameof(ACTCoverMesh.UpdateRendererMaterialArray)),
                typeof(ACTCoverMesh).GetMethod(nameof(ACTCoverMesh.UpdateRendererMaterialArrayWithPlayState)),
                typeof(ACTCoverMesh).GetMethod(nameof(ACTCoverMesh.UpdateMesh), new Type[] { typeof(UnityEngine.Renderer) }),
                typeof(ACTCoverMesh).GetMethod(nameof(ACTCoverMesh.UpdateMesh), new Type[] { typeof(Il2CppSystem.Collections.Generic.List<UnityEngine.Renderer>) }),
            };

            foreach(MethodInfo? i in injections)
            {
                if (i == null)
                {
                    throw new InvalidOperationException("Failed to find Replacement Method: Demosaic");
                }
            }

            MethodInfo? replacement = typeof(Demosaic).GetMethod("NoMosaic", BindingFlags.Static | BindingFlags.NonPublic);
            if (replacement == null)
            {
                throw new InvalidOperationException("Failed to find Replacement Method: Demosaic");
            }

            foreach (MethodInfo? i in injections)
            {
                Env.Harmony.Patch(i, null, new HarmonyLib.HarmonyMethod(replacement), null);
            }
            
        }

        public void Unpatch()
        {
            
        }
    }
}
