using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static Il2Cpp.RicassoActor;

namespace RiccaMod.Patches
{
    internal class FasterDashAttack : IPatch
    {
        private Environment Env;
        public FasterDashAttack(Environment env) { Env = env; }

        public string Name { get; } = "Faster Dash Attacks";

        public string Description { get; } = "In the original game, you have to walk for about half a second before being able to dash attack. This module reduces this time.";
        public bool Enabled { get; protected set; }

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("FasterDashAttack_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;

            var timingEntry = cat.CreateEntry<float>("FasterDashAttack_TimingS", DashAttackRunTime);
            timingEntry.DisplayName = "Dash Attack Timing";
            timingEntry.Description = "Time in Seconds of running until attacks become dash attacks.";
            DashAttackRunTime = timingEntry.Value;
        }

        private static float DashAttackRunTime = 0.125f;

        /*
                             **************************************************************
                             *                          FUNCTION                          *
                             **************************************************************
                             bool __fastcall RicassoActor$$ControlGround(RicassoActor
                               assume GS_OFFSET = 0xff00000000
             bool              AL:1           <RETURN>
             RicassoActor_o    RCX:8          __this
             MethodInfo *      RDX:8          method
                             RicassoActor$$ControlGround                     XREF[5]:     RicassoActor$$InternalUpdatePost
                                                                                          18241d76c(*), 18241d77c(*), 
                                                                                          18259c678(*), 182a33f24(*)  
       180597500 40 55           PUSH       RBP

       1806b2e90 40 55           PUSH       RBP
        [...]
       180597645 e8 26 0b        CALL       FUN_180298170                                    undefined FUN_180298170()
                 d0 ff
       18059764a f3 0f 5c        SUBSS      XMM0,dword ptr [RBX + 0x86c]
                 83 6c 08 
                 00 00
       180597652 0f 2f 05        COMISS     XMM0,dword ptr [DAT_181ed7bf8]                   = 3F000000h            <--- this compares to a constant 0.5, time in seconds before run attack is possible.
                 9f 05 94 01                                                                                        unfortunately, we cant change that value, as its used in many other places;
       180597659 0f 86 2c        JBE        LAB_18059778b                                                           So we will need to replace it with a reference to a different float instead
                 01 00 00

         */
        public ulong GetPatchLocation()
        {
            //TODO extact the function start from the dll somehow
            ulong functionBase = Env.GameDllBase + 0x0597500;
            ulong offset = 0x180597652 - 0x180597500;
            functionBase += offset;
            return functionBase;
        }

        private byte[] origs = new byte[0];
        private static IntPtr redirectFloat = (IntPtr)0;

        public void Patch()
        {
            if (!Enabled) return;

            ulong address = GetPatchLocation();
            if (redirectFloat == (IntPtr)0)
            {
                redirectFloat = PatchUtils.AllocateCloseTo(address, 4, 0xFFFFFFFF, PatchUtils.MemoryProtection.ReadWrite);
                unsafe
                {
                    ulong addr = (ulong)redirectFloat;
                    float* f = (float*)addr;
                    *f = DashAttackRunTime;
                }
            }

            ulong addrDiff = (ulong)redirectFloat - (address + 7);
            PatchUtils.ReplaceCodeBytes(
                address
                , new byte[] { 0x0F, 0x2F, 0x05, (byte)(addrDiff >> 0), (byte)(addrDiff >> 8), (byte)(addrDiff >> 16), (byte)(addrDiff >> 24) }
                , new byte[] { 0x0F, 0x2F }
                , out origs
             );
        }

        public void Unpatch()
        {
            if (!Enabled) return;

            if (origs.Length == 0) return;
            ulong address = GetPatchLocation();
            PatchUtils.ReplaceCodeBytes(address, origs, new byte[0], out byte[] dummy);
        }

    }
}
