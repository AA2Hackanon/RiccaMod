using MelonLoader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RiccaMod.Patches
{
    internal class Environment
    {
        private static ProcessModule FindGameAssembly()
        {
            var modules = Process.GetCurrentProcess().Modules;
            return modules.Cast<ProcessModule>().Single(x => x.ModuleName == "GameAssembly.dll");
        }

        private static Lazy<ProcessModule> _gameDll = new Lazy<ProcessModule>(FindGameAssembly);
        public ulong GameDllBase {
            get { return (ulong)_gameDll.Value.BaseAddress.ToInt64();  }
        }

        public MelonLogger.Instance Logger { get; init; }

        public HarmonyLib.Harmony Harmony { get; set; }

        public Environment(MelonLogger.Instance logger, HarmonyLib.Harmony harmony)
        {
            this.Logger = logger;
            Harmony = harmony;
        }
    }
}
