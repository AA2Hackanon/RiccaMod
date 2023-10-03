using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RiccaMod.Patches
{
    /*
     * Allows 
     */
    internal class RisingGuards : IPatch
    {
        private Environment Env;
        public RisingGuards(Environment env) { Env = env; }

        public string Name { get; } = "Allow Rising Guard";
        public string Description { get; } = "Normally, you are not allowed to block whiile still moving upwards from a jump. This patch enables this, allowing you to cancel an attack into a jump and then immediately Blocking.";

        public bool Enabled { get; protected set; }

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("RisingGuards_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;
        }

        public static RisingGuards? CurrInstance = null;

        /*
                             **************************************************************
                             *                          FUNCTION                          *
                             **************************************************************
                             bool __fastcall RicassoActor$$ControlAirCancelAction(Ric
                               assume GS_OFFSET = 0xff00000000
             bool              AL:1           <RETURN>
             RicassoActor_o    RCX:8          __this
             MethodInfo *      RDX:8          method
                             RicassoActor$$ControlAirCancelAction            XREF[6]:     RicassoActor$$ControlBlowOff:180
                                                                                          RicassoActor$$ControlJump:180598
                                                                                          RicassoActor$$InternalUpdatePost
                                                                                          RicassoActor$$InternalUpdatePost
                                                                                          18259c6a8(*), 182a33edc(*)  
       180596d50 48 89 5c        MOV        qword ptr [RSP + local_res8],RBX
                 24 08

        [...]
       18059702d f3 0f 10        MOVSS      XMM0,dword ptr [RBX + 0x85c]
                 83 5c 08 
                 00 00
       180597035 f3 0f 59        MULSS      XMM0,dword ptr [DAT_181ed7be4]                   = 3E4CCCCDh
                 05 a7 0b 
                 94 01
       18059703d 0f 2f 40 04     COMISS     XMM0,dword ptr [RAX + 0x4]
       180597041 0f 86 8b        JBE        LAB_1805970d2                                    <--- this jump skips guard commands if still rising, so we just nop it out
                 00 00 00
            */

        public ulong GetPatchLocation()
        {
            //TODO extact the function start from the dll somehow. actually, TODO search for the opcodes from function start
            ulong functionBase = CurrInstance.Env.GameDllBase + 0x0596d50;
            ulong offset = 0x180597041 - 0x180596d50;
            functionBase += offset;
            return functionBase;
        }

        private byte[] origs = new byte[0];
        public void Patch()
        {
            CurrInstance = this;
            if (!Enabled) return;

            ulong address = GetPatchLocation();
            PatchUtils.ReplaceCodeBytes(address, new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }, new byte[] { 0x0F }, out origs);
        }

        public void Unpatch()
        {
            if (!Enabled) return;

            if (origs.Length == 0) return;
            ulong address = GetPatchLocation();
            PatchUtils.ReplaceCodeBytes(address, origs, new byte[0] , out byte[] dummy);
        }
    }
}
