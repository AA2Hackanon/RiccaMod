using Harmony;
using HarmonyLib;
using Il2Cpp;
using Il2CppACT;
using MelonLoader;
using MelonLoader.NativeUtils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static Il2Cpp.CharacterActor;
using static Il2Cpp.RicassoActor;
using static Il2CppACT.ADVIconRenderer;
using static MelonLoader.MelonLogger;

using static RiccaMod.Patches.PatchUtils;

namespace RiccaMod.Patches
{
    internal class AttackCombos : IPatch
    {

        private Environment Env;
        public AttackCombos(Environment env) { Env = env; }

        public string Name { get; } = "Attack Combos";

        public string Description { get; } = "Allows to switch from Heavy to Light attacks mid-chain, as well as not lose your attack chain when using up- or down tilts";
        public bool Enabled { get; protected set; }

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("AttackCombos_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;
        }

        public static AttackCombos? CurrInstance = null;


        /// <summary>
        /// Additional state associated with rica actors.
        /// TODO: this leaks. clean this up after a while of inactivity.
        /// </summary>
        private class RiccaState
        {
            public int lastAttackOrdinal = 0;
            public AttackType lastAttackType = AttackType.Light;
            public bool recursionBlocker = false;
            public bool animationHack = false;

            private IntPtr ricca;

            public RiccaState(IntPtr ricca) { this.ricca = ricca; }

            private static Dictionary<IntPtr, RiccaState> riccaStates = new();
            private static Dictionary<IntPtr, RiccaState> riccaActionInfo = new();
            public static RiccaState Get(RicassoActor ricca)
            {
                if (!riccaStates.TryGetValue(ricca.Pointer, out RiccaState? val))
                {
                    val = new RiccaState(ricca.Pointer);
                    riccaStates.Add(ricca.Pointer, val);
                    riccaActionInfo.Add(ricca.ActionInfo.Pointer, val);
                }

                return val;
            }
            public static RiccaState? TryGetFromActionInfo(ActionInfo info)
            {
                if(!riccaActionInfo.TryGetValue(info.Pointer, out RiccaState? value))
                {
                    return null;
                }
                return value;
            }
        }

        private const bool SKIP_ORIGINAL = false;
        private const bool DONT_SKIP_ORIGINAL = true;

        private static bool PrefixInject_StartAction(CharacterActor __instance, Il2CppSystem.Collections.IEnumerator? actionCoroutine,
                                                    ActionStopCallback stopCallback, ActionStopInfo.Reason stopReason)
        {
            //we hook this purely to find out if player used initial light attacks.
            if (actionCoroutine == null) return DONT_SKIP_ORIGINAL;
            RicassoActor? ricca = __instance.TryCast<RicassoActor>();
            if (ricca == null) return DONT_SKIP_ORIGINAL;
            //we determine action type based on class of the IEnumerator
            if(actionCoroutine.TryCast<RicassoActor._StandNormalLightAttack01Coroutine_d__311>() != null
                || actionCoroutine.TryCast<RicassoActor._StandNormalUpperAttackCoroutine_d__328>() != null
                || actionCoroutine.TryCast<RicassoActor._StandNormalUpperSlashAttackCoroutine_d__330>() != null
                || actionCoroutine.TryCast<RicassoActor._StandNormalUpperStabAttackCoroutine_d__329>() != null)
            {
                RiccaState state = RiccaState.Get(ricca);
                state.lastAttackOrdinal = 1;
                state.lastAttackType = AttackType.Light;
            }
            else if (actionCoroutine.TryCast<RicassoActor._StandNormalAttack01Coroutine_d__317>() != null)
            {
                RiccaState state = RiccaState.Get(ricca);
                state.lastAttackOrdinal = 1;
                state.lastAttackType = AttackType.Heavy;
            } 

            return DONT_SKIP_ORIGINAL;
        } 

        private enum AttackType
        {
            Light, Heavy, Uptilt, Downtilt, None
        }


        private static AttackType DetermineNextChainMember(RicassoActor rica)
        {
            if ((rica.comboAttackInput.Input.GetButtonPressFlagsAll() & (CharacterInput.ButtonFlag.Right | CharacterInput.ButtonFlag.Left)) != 0) {
                return AttackType.Heavy;
            }
            else {
                return AttackType.Light;
            }
        }

        private static AttackType GetInputAttackType(RicassoActor ricca, bool allowHeavy = true, bool allowTilts = true, bool forceAttack = false)
        {
            if (!forceAttack && (ricca.comboAttackInput.Input.GetButtonPressFlagsAll() & CharacterInput.ButtonFlag.Attack) == 0)
            {
                //no attack input
                return AttackType.None;
            }
            if (allowTilts && (ricca.comboAttackInput.Input.GetButtonPressFlagsAll() & CharacterInput.ButtonFlag.Up) != 0)
            {
                return AttackType.Uptilt;
            }
            if (allowTilts && (ricca.comboAttackInput.Input.GetButtonPressFlagsAll() & CharacterInput.ButtonFlag.Down) != 0)
            {
                return AttackType.Downtilt;
            }
            if (allowHeavy && (ricca.comboAttackInput.Input.GetButtonPressFlagsAll() & (CharacterInput.ButtonFlag.Right | CharacterInput.ButtonFlag.Left)) != 0)
            {
                return AttackType.Heavy;
            }
            else
            {
                return AttackType.Light;
            }
        }

        private static Il2CppSystem.Collections.IEnumerator? StartNextAttackCoroutine(RicassoActor ricca, bool allowTilts = true)
        {
            RiccaState state = RiccaState.Get(ricca);
            AttackType nextAttack = GetInputAttackType(ricca, state.lastAttackType == AttackType.Heavy, allowTilts);

            int lastOrdinal;
            if((state.lastAttackType == AttackType.Uptilt || state.lastAttackType == AttackType.Downtilt)
                && (nextAttack == AttackType.Uptilt || nextAttack == AttackType.Downtilt))
            {
                //consecutive tils reset attack ordinal
                state.lastAttackOrdinal = 2;
                lastOrdinal = 1;
            }
            else
            {
                lastOrdinal = state.lastAttackOrdinal++;
            }
            if(state.lastAttackType != AttackType.Heavy && nextAttack == AttackType.Heavy)
            {
                state.animationHack = true;
            }
            else
            {
                state.animationHack = false;
            }
            state.lastAttackType = nextAttack;

            switch (lastOrdinal)
            {
                case 1:
                    switch(nextAttack)
                    {
                        case AttackType.Light:
                            return ricca.StandNormalLightAttack02Coroutine();
                        case AttackType.Heavy:
                            return ricca.StandNormalAttack02Coroutine();
                        case AttackType.Uptilt:
                            return ricca.StandNormalLightAttackUpperCoroutine(false); //note: false means 2, true means 3. weird but true.
                        case AttackType.Downtilt:
                            return ricca.StandNormalLightAttackDownCoroutine(false);
                        default:
                            return null;
                    }
                case 2:
                    switch (nextAttack)
                    {
                        case AttackType.Light:
                            return ricca.StandNormalLightAttack03Coroutine();
                        case AttackType.Heavy:
                            return ricca.StandNormalAttack03Coroutine();
                        case AttackType.Uptilt:
                            return ricca.StandNormalLightAttackUpperCoroutine(true);
                        case AttackType.Downtilt:
                            return ricca.StandNormalLightAttackDownCoroutine(true);
                        default:
                            return null;
                    }
                case 3:
                    switch (nextAttack)
                    {
                        case AttackType.Light:
                            return ricca.StandNormalLightAttack04Coroutine();
                        case AttackType.Heavy:
                            return ricca.StandNormalAttack04Coroutine();
                        case AttackType.Uptilt:
                            return ricca.StandNormalLightAttackUpperCoroutine(false);
                        case AttackType.Downtilt:
                            return ricca.StandNormalLightAttackDownCoroutine(false);
                        default:
                            return null;
                    }
                default:
                    return null;
            }
        }

        private static bool AttackChainPrefixInjection(RicassoActor __instance, ref Il2CppSystem.Collections.IEnumerator __result, AttackType origAttack, int requestedOrdinal)
        {
            RiccaState state = RiccaState.Get(__instance);
            if (state.recursionBlocker) return DONT_SKIP_ORIGINAL;
            state.recursionBlocker = true;
            AttackType nextAttack = GetInputAttackType(__instance, state.lastAttackType == AttackType.Heavy);

            if (nextAttack == origAttack)
            {
                state.lastAttackOrdinal = requestedOrdinal; //commit this ordinal
                state.recursionBlocker = false;
                return DONT_SKIP_ORIGINAL;
            }

            //pretend it was last ordinal, the logic will disagree with the result anyway
            state.lastAttackOrdinal = requestedOrdinal - 1;
            var newEnum = StartNextAttackCoroutine(__instance);

            if (newEnum == null)
            {
                state.recursionBlocker = false;
                return DONT_SKIP_ORIGINAL;
            }
            else
            {
                __result = newEnum;
                state.recursionBlocker = false;
                return SKIP_ORIGINAL;
            }
        }
        private static Il2CppSystem.Collections.IEnumerator? AttachChainUptiltInjection(RicassoActor rica, bool isStart)
        {
            RiccaState state = RiccaState.Get(rica);
            if (state.recursionBlocker) return null;
            state.recursionBlocker = true;

            if (isStart) state.lastAttackOrdinal = 1;   //from stand, this counts as the first attack
            else state.lastAttackOrdinal++;             //else, this was a normal part of the attack chain
            var newEnum = StartNextAttackCoroutine(rica);

            state.recursionBlocker = false;
            return newEnum;
        }

        private static bool PrefixInject_Light_02(RicassoActor __instance, ref Il2CppSystem.Collections.IEnumerator __result) { return AttackChainPrefixInjection(__instance, ref __result, AttackType.Light, 2); }
        private static bool PrefixInject_Light_03(RicassoActor __instance, ref Il2CppSystem.Collections.IEnumerator __result) { return AttackChainPrefixInjection(__instance, ref __result, AttackType.Light, 3); }
        private static bool PrefixInject_Light_04(RicassoActor __instance, ref Il2CppSystem.Collections.IEnumerator __result) { return AttackChainPrefixInjection(__instance, ref __result, AttackType.Light, 4); }
        private static bool PrefixInject_Smash_02(RicassoActor __instance, ref Il2CppSystem.Collections.IEnumerator __result) { return AttackChainPrefixInjection(__instance, ref __result, AttackType.Heavy, 2); }
        private static bool PrefixInject_Smash_03(RicassoActor __instance, ref Il2CppSystem.Collections.IEnumerator __result) { return AttackChainPrefixInjection(__instance, ref __result, AttackType.Heavy, 3); }
        private static bool PrefixInject_Smash_04(RicassoActor __instance, ref Il2CppSystem.Collections.IEnumerator __result) { return AttackChainPrefixInjection(__instance, ref __result, AttackType.Heavy, 4); }
        private static bool PrefixInject_RiccaComboUptilt(RicassoActor._StandNormalLightAttackUpperCoroutine_d__314 __instance) {
            //check for additional inputs during state 6
            if (__instance.__1__state != 6) return DONT_SKIP_ORIGINAL;
            RicassoActor rica = __instance.__4__this;
            Il2CppSystem.Collections.IEnumerator? redirect = AttachChainUptiltInjection(rica, false);

            if (redirect != null)
            {
                rica.StartAction(redirect, rica.stopNormalAttackCallback, ActionStopInfo.Reason.StartOtherAction);
                __instance.__1__state = -1;
                return DONT_SKIP_ORIGINAL;
            }
            else
            {
                return DONT_SKIP_ORIGINAL;
            }
        }
        private static bool PrefixInject_RiccaComboDowntilt(RicassoActor._StandNormalLightAttackDownCoroutine_d__313 __instance) {
            //check for additional inputs during state 6
            if (__instance.__1__state != 4) return DONT_SKIP_ORIGINAL;
            RicassoActor rica = __instance.__4__this;

            Il2CppSystem.Collections.IEnumerator? redirect = AttachChainUptiltInjection(rica, false);

            if (redirect != null)
            {
                rica.StartAction(redirect, rica.stopNormalAttackCallback, ActionStopInfo.Reason.StartOtherAction);
                __instance.__1__state = -1;
                return DONT_SKIP_ORIGINAL;
            }
            else
            {
                return DONT_SKIP_ORIGINAL;
            }
        }


        private struct HarmonyInjection
        {
            public MethodInfo? injection;
            public MethodInfo? prefix;
            public MethodInfo? postfix;
        }
        public void Patch()
        {
            CurrInstance = this;
            if (!Enabled) return;

            List<HarmonyInjection> injections = new()
            {
                new() { 
                    injection = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StandNormalLightAttack02Coroutine)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_Light_02), BindingFlags.Static | BindingFlags.NonPublic) 
                },
                new() {
                    injection = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StandNormalLightAttack03Coroutine)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_Light_03), BindingFlags.Static | BindingFlags.NonPublic)
                },
                new() {
                    injection = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StandNormalLightAttack04Coroutine)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_Light_04), BindingFlags.Static | BindingFlags.NonPublic)
                },
                new() {
                    injection = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StandNormalAttack02Coroutine)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_Smash_02), BindingFlags.Static | BindingFlags.NonPublic)
                },
                new() {
                    injection = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StandNormalAttack03Coroutine)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_Smash_03), BindingFlags.Static | BindingFlags.NonPublic)
                },
                new() {
                    injection = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StandNormalAttack04Coroutine)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_Smash_04), BindingFlags.Static | BindingFlags.NonPublic)
                },
                new() {
                    injection = typeof(CharacterActor).GetMethod(nameof(CharacterActor.StartAction)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_StartAction), BindingFlags.Static | BindingFlags.NonPublic)
                },
                new() {
                    injection = typeof(RicassoActor._StandNormalLightAttackUpperCoroutine_d__314).GetMethod(nameof(RicassoActor._StandNormalLightAttackUpperCoroutine_d__314.MoveNext)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_RiccaComboUptilt), BindingFlags.Static | BindingFlags.NonPublic)
                },
                new() {
                    injection = typeof(RicassoActor._StandNormalLightAttackDownCoroutine_d__313).GetMethod(nameof(RicassoActor._StandNormalLightAttackDownCoroutine_d__313.MoveNext)),
                    prefix = typeof(AttackCombos).GetMethod(nameof(PrefixInject_RiccaComboDowntilt), BindingFlags.Static | BindingFlags.NonPublic)
                },
            };

            //verify all are found
            foreach(HarmonyInjection inj in injections)
            {
                if (inj.injection == null)
                {
                    throw new InvalidOperationException("Failed to find Injection Method for attack combo");
                }
                if (inj.postfix == null && inj.prefix == null)
                {
                    throw new InvalidOperationException("Failed to find Replacement Method for attack combo");
                }
            }

            foreach (HarmonyInjection inj in injections)
            {
                HarmonyLib.HarmonyMethod? prefix = inj.prefix == null ? null : new(inj.prefix);
                HarmonyLib.HarmonyMethod? postfix = inj.postfix == null ? null : new(inj.postfix);
                Env.Harmony.Patch(inj.injection, prefix, postfix, null);
            }
        }

        public void Unpatch()
        {
            if (!Enabled) return;
        }


        ////////////////////////
        /// Note: these are old direct assembly injections from 1.22.
        /// THey are replaced by the full hooks that are easier to update and replicate for other characters.


        // private const Int32 END_FUNCTION = 0;
        // private const Int32 CONTINUE_FUNCITON = 1;

        // ulong GetUpTiltPatchLocation()
        // {
        //    return Env.GameDllBase + 0xB63EAC;
        // }

        // [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        // delegate Int32 FIrstMoveUpTiltStubFUnction(IntPtr rica);
        // StubInjection<FIrstMoveUpTiltStubFUnction> uptiltInjection;
        // [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvFastcall) })]
        // private static Int32 NativeStubInject_UpTilt(IntPtr rawRicca)
        // {
        //     RicassoActor rica = new RicassoActor(rawRicca);
        //     Il2CppSystem.Collections.IEnumerator? redirect = AttachChainUptiltInjection(rica, true);

        //     if (redirect != null)
        //     {
        //         var followup = rica.StandNormalLightAttack02Coroutine();
        //         rica.StartAction(redirect, rica.stopNormalAttackCallback, ActionStopInfo.Reason.StartOtherAction);
        //         return END_FUNCTION;
        //     }
        //     else
        //     {
        //         return CONTINUE_FUNCITON;
        //     }

        // }

        // [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        // delegate Int32 ComboUpTiltStubFUnction(IntPtr rica);
        // StubInjection<FIrstMoveUpTiltStubFUnction> comboUptiltInjection;
        // [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvFastcall) })]
        // private static Int32 NativeStubInject_ComboUpTilt(IntPtr rawRicca)
        // {
        //     RicassoActor rica = new RicassoActor(rawRicca);
        //     Il2CppSystem.Collections.IEnumerator? redirect = AttachChainUptiltInjection(rica, false);

        //     if (redirect != null)
        //     {
        //         rica.StartAction(redirect, rica.stopNormalAttackCallback, ActionStopInfo.Reason.StartOtherAction);
        //         return END_FUNCTION;
        //     }
        //     else
        //     {
        //         return CONTINUE_FUNCITON;
        //     }
        // }

        // [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        // delegate Int32 ComboDownTiltStubFunction(IntPtr rica);
        // StubInjection<ComboDownTiltStubFunction> comboDowntiltInjection;
        // [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvFastcall) })]
        // private static Int32 NativeStubInject_ComboDownTilt(IntPtr rawRicca)
        // {
        //     RicassoActor rica = new RicassoActor(rawRicca);
        //     Il2CppSystem.Collections.IEnumerator? redirect = AttachChainUptiltInjection(rica, false);

        //     if (redirect != null)
        //     {
        //         rica.StartAction(redirect, rica.stopNormalAttackCallback, ActionStopInfo.Reason.StartOtherAction);
        //         return END_FUNCTION;
        //     }
        //     else
        //     {
        //         return CONTINUE_FUNCITON;
        //     }
        // }

        // /*
        //  * Ok, here is the plan for the more complex injections:
        //  * 1: We manually VirtualAlloc code space and write assembly code in there as bytes, because linking asm files to a cä project is apparently a massive pain in the ass for some reason
        //  * 2: the assembly contains a call to a nop-stub function
        //  * 3: we native-hook that stub function
        //  * 4: we shed a tear about how stupid this idea is
        //  * 5: we remember the whole thing was written in unity
        //  * 6: lets go
        //  */



        // /////////////////////
        // /// UP Tilts  
        // ////////////////////

        // /* Function for up tilt from nothing: StandNormalUpperAttackCoroutine_d__3
        //                 **************************************************************
        //                 *                          FUNCTION                          *
        //                 **************************************************************
        //                 bool RicassoActor.<>d__325$$MoveNext(RicassoActor__Stand
        //                         assume GS_OFFSET = 0xff00000000
        //       bool              AL:1           <RETURN>
        //       RicassoActor__    RCX:8          __this
        //       MethodInfo *      RDX:8          method
        //                       RicassoActor.<>d__325$$MoveNext                 XREF[1]:     1824aa03c(*)  
        // 180b637d0 48 89 5c        MOV        qword ptr [RSP + 0x8],RBX
        //           24 08
        // [...]
        // 180b63eac f3 0f 10        MOVSS XMM0, dword ptr[DAT_181bf9a14]                   = 3F2CCCCDh            <--- we inject here, replacing the movss and 
        //      05 60 5b                                                                                           <--- repeating it in our replacement func.
        //      09 01                                                                                              <--- at this point, ecx is ricca
        // 180b63eb4 0f 2f 40 28     COMISS XMM0, dword ptr[RAX + 0x28]

        // We inject the following code:
        // 7FF8C7480000 - 50                    - push rax
        // 7FF8C7480001 - 51                    - push rcx
        // 7FF8C7480002 - 52                    - push rdx
        // 7FF8C7480003 - 41 50                 - push r8
        // 7FF8C7480005 - 48 83 EC 20           - sub rsp,20
        // 7FF8C7480009 - 48 8B CB              - mov rcx,rbx
        // 7FF8C748000C - E8 1E000000           - call 7FF8C748002F
        // 7FF8C7480011 - 48 83 C4 20           - add rsp,20
        // 7FF8C7480015 - 85 C0                 - test eax,eax
        // 7FF8C7480017 - 41 58                 - pop r8
        // 7FF8C7480019 - 5A                    - pop rdx
        // 7FF8C748001A - 59                    - pop rcx
        // 7FF8C748001B - 58                    - pop rax
        // 7FF8C748001C - 0F84 F33E8101         - je GameAssembly.dll+B63F15
        // 7FF8C7480022 - F3 0F10 05 EA998A02   - movss xmm0,[GameAssembly.dll+1BF9A14]
        // 7FF8C748002A - E9 853E8101           - jmp GameAssembly.dll+B63EB4
        // 7FF8C748002F - 90                    - nop 
        // 7FF8C7480030 - 90                    - nop 
        // 7FF8C7480031 - 90                    - nop 
        // 7FF8C7480032 - 90                    - nop 
        // 7FF8C7480033 - 90                    - nop 
        // 7FF8C7480034 - 90                    - nop 
        // 7FF8C7480035 - 90                    - nop 
        // 7FF8C7480036 - 90                    - nop 
        // 7FF8C7480037 - 90                    - nop 
        // 7FF8C7480038 - 90                    - nop 
        // 7FF8C7480039 - C3                    - ret 

        //  */


        // private StubInjection<FIrstMoveUpTiltStubFUnction> PatchUptilt()
        // {
        //     StubFunctionData data = new StubFunctionData()
        //     {
        //         Assembly = new byte[] {
        //             0x50,0x51,0x52,0x41,0x50,0x48,0x83,0xEC,0x20,0x48,0x8B,0xCB,0xE8,0x1E,0x00,0x00,0x00,
        //             0x48,0x83,0xC4,0x20,0x85,0xC0,0x41,0x58,0x5A,0x59,0x58,0x0F,0x84,0xF3,0x3E,0x81,0x01,
        //             0xF3,0x0F,0x10,0x05,0xEA,0x99,0x8A,0x02,0xE9,0x85,0x3E,0x81,0x01,0x90,0x90,0x90,0x90,
        //             0x90,0x90,0x90,0x90,0x90,0x90,0xC3,
        //         },
        //         AssemblyHookFunctionOffset = 0x2F,
        //         AssemblyReturnJumpOffset = 0x2A,
        //         Redirects = new[] {
        //             new StubFunctionData.RelativeRedirect { AddressOffset = 0x22 + 4, TargetAddress = Env.GameDllBase + 0x1BF9A14 } ,
        //             new StubFunctionData.RelativeRedirect { AddressOffset = 0x1C + 2, TargetAddress = Env.GameDllBase + 0xB63F15 }
        //         },
        //         FunctionOffset = 0x0b637d0,
        //         InjectionOffset = 0x0b63eac - 0x0b637d0,
        //         InjectionReplacementSize = 8,
        //         ReplacedBytesToVerify = new byte[] { 0xF3, 0x0F, 0x10, 0x05 }
        //     };
        //     StubFunction funcs = DoStubInjection(data, Env);

        //     NativeHook<FIrstMoveUpTiltStubFUnction> hook = new NativeHook<FIrstMoveUpTiltStubFUnction>();
        //     unsafe
        //     {
        //         delegate* unmanaged[Fastcall]<IntPtr, Int32> detourPtr = &NativeStubInject_UpTilt;
        //         hook.Target = funcs.StubStart;
        //         hook.Detour = (IntPtr)detourPtr;
        //         hook.Attach();
        //     }

        //     return new StubInjection<FIrstMoveUpTiltStubFUnction>()
        //     {
        //         Data = data,
        //         Function = funcs,
        //         Hook = hook
        //     };
        // }


        // /*
        //  Function for up-tilt out of light attacks: StandNormalLightAttackUpperCoroutine_d__
        //                      **************************************************************
        //                      *                          FUNCTION                          *
        //                      **************************************************************
        //                      bool RicassoActor.<>d__311$$MoveNext(RicassoActor__Stand
        //                        assume GS_OFFSET = 0xff00000000
        //      bool              AL:1           <RETURN>
        //      RicassoActor__    RCX:8          __this
        //      MethodInfo *      RDX:8          method
        //                      RicassoActor.<>d__311$$MoveNext                 XREF[3]:     181f0d2c4(*), 181f0d2d4(*), 
        //                                                                                   1824e7ac4(*)  
        //1810c4e70 48 89 5c        MOV        qword ptr [RSP + 0x10],RBX
        //          24 10

        // [...]
        //1810c53c1 f3 0f 10        MOVSS      XMM0,dword ptr [DAT_181bbe180]                   = GameAssembly.dll+1BBE180
        //          05 b7 8d 
        //          af 00
        //1810c53c9 0f 2f 40 28     COMISS     XMM0,dword ptr [RAX + 0x28]
        // or maybe here?
        // 1810c52c0 f3 0f 10        MOVSS      XMM0,dword ptr [DAT_181a4b33c]                   = 3E99999Ah
        //          05 74 60 
        //          98 00
        //1810c52c8 0f 2f 40 28     COMISS     XMM0,dword ptr [RAX + 0x28]
        // or maybe here?
        //1810c534b f3 0f 10        MOVSS      XMM0,dword ptr [DAT_181bbde5c]                   = 3ECCCCCDh
        //          05 09 8b 
        //          af 00
        //1810c5353 0f 2f 40 28     COMISS     XMM0,dword ptr [RAX + 0x28]



        // 7FF8A7EF0000 - 50                    - push rax
        // 7FF8A7EF0001 - 51                    - push rcx
        // 7FF8A7EF0002 - 52                    - push rdx
        // 7FF8A7EF0003 - 41 50                 - push r8
        // 7FF8A7EF0005 - 48 83 EC 20           - sub rsp,20
        // 7FF8A7EF0009 - 48 8B CB              - mov rcx,rbx
        // 7FF8A7EF000C - E8 1E000000           - call 7FF8A7EF002F
        // 7FF8A7EF0011 - 48 83 C4 20           - add rsp,20
        // 7FF8A7EF0015 - 85 C0                 - test eax,eax
        // 7FF8A7EF0017 - 41 58                 - pop r8
        // 7FF8A7EF0019 - 5A                    - pop rdx
        // 7FF8A7EF001A - 59                    - pop rcx
        // 7FF8A7EF001B - 58                    - pop rax
        // 7FF8A7EF001C - 0F84 0854EFFC         - je GameAssembly.dll+10C542A
        // 7FF8A7EF0022 - F3 0F10 05 12B387FD   - movss xmm0,[GameAssembly.dll+1BBE180]
        // 7FF8A7EF002A - E9 9952EFFC           - jmp GameAssembly.dll+10C52C8
        // 7FF8A7EF002F - 90                    - nop 
        // 7FF8A7EF0030 - 90                    - nop 
        // 7FF8A7EF0031 - 90                    - nop 
        // 7FF8A7EF0032 - 90                    - nop 
        // 7FF8A7EF0033 - 90                    - nop 
        // 7FF8A7EF0034 - 90                    - nop 
        // 7FF8A7EF0035 - 90                    - nop 
        // 7FF8A7EF0036 - 90                    - nop 
        // 7FF8A7EF0037 - 90                    - nop 
        // 7FF8A7EF0038 - 90                    - nop 
        // 7FF8A7EF0039 - C3                    - ret 


        //  */
        // private StubInjection<FIrstMoveUpTiltStubFUnction> PatchComboUptilt()
        // {
        //     StubFunctionData data = new StubFunctionData()
        //     {
        //         Assembly = new byte[] {
        //             0x50,0x51,0x52,0x41,0x50,0x48,0x83,0xEC,0x20,0x48,0x8B,0xCB,0xE8,0x1E,0x00,0x00,0x00,
        //             0x48,0x83,0xC4,0x20,0x85,0xC0,0x41,0x58,0x5A,0x59,0x58,0x0F,0x84,0xF3,0x3E,0x81,0x01,
        //             0xF3,0x0F,0x10,0x05,0xEA,0x99,0x8A,0x02,0xE9,0x85,0x3E,0x81,0x01,0x90,0x90,0x90,0x90,
        //             0x90,0x90,0x90,0x90,0x90,0x90,0xC3,
        //         },
        //         AssemblyHookFunctionOffset = 0x2F,
        //         AssemblyReturnJumpOffset = 0x2A,
        //         Redirects = new[] {
        //             new StubFunctionData.RelativeRedirect { AddressOffset = 0x22 + 4, TargetAddress = Env.GameDllBase + 0x1BBE180 } ,
        //             new StubFunctionData.RelativeRedirect { AddressOffset = 0x1C + 2, TargetAddress = Env.GameDllBase + 0x10C52C8 }
        //         },
        //         FunctionOffset = 0x10c4e70,
        //         InjectionOffset = 0x10c53c1 - 0x10c4e70,
        //         InjectionReplacementSize = 8,
        //         ReplacedBytesToVerify = new byte[] { 0xF3, 0x0F, 0x10, 0x05 }
        //     };
        //     StubFunction funcs = DoStubInjection(data, Env);

        //     NativeHook<FIrstMoveUpTiltStubFUnction> hook = new NativeHook<FIrstMoveUpTiltStubFUnction>();
        //     unsafe
        //     {
        //         delegate* unmanaged[Fastcall]<IntPtr, Int32> detourPtr = &NativeStubInject_ComboDownTilt;
        //         hook.Target = funcs.StubStart;
        //         hook.Detour = (IntPtr)detourPtr;
        //         hook.Attach();
        //     }

        //     return new StubInjection<FIrstMoveUpTiltStubFUnction>()
        //     {
        //         Data = data,
        //         Function = funcs,
        //         Hook = hook
        //     };
        // }

        // /////////////////////
        // /// DOWN Tilts  
        // ////////////////////

        // /*
        //                      **************************************************************
        //                      *                          FUNCTION                          *
        //                      **************************************************************
        //                      bool RicassoActor.<>d__310$$MoveNext(RicassoActor__Stand
        //                        assume GS_OFFSET = 0xff00000000
        //      bool              AL:1           <RETURN>
        //      RicassoActor__    RCX:8          __this
        //      MethodInfo *      RDX:8          method
        //                      RicassoActor.<>d__310$$MoveNext                 XREF[1]:     1824e7aac(*)  
        //1810c49f0 48 89 5c        MOV        qword ptr [RSP + 0x8],RBX
        //          24 08

        // [...]
        // 1810c4da3 f3 0f 10        MOVSS      XMM0,dword ptr [DAT_181bbdd60]                   = 3EE66666h
        //          05 b5 8f 
        //          af 00
        //1810c4dab 0f 2f 40 28     COMISS     XMM0,dword ptr [RAX + 0x28]

        // 7FF8A7EF0000 - 50                    - push rax
        // 7FF8A7EF0001 - 51                    - push rcx
        // 7FF8A7EF0002 - 52                    - push rdx
        // 7FF8A7EF0003 - 41 50                 - push r8
        // 7FF8A7EF0005 - 48 83 EC 20           - sub rsp,20
        // 7FF8A7EF0009 - 48 8B CB              - mov rcx,rbx
        // 7FF8A7EF000C - E8 1E000000           - call 7FF8A7EF002F
        // 7FF8A7EF0011 - 48 83 C4 20           - add rsp,20
        // 7FF8A7EF0015 - 85 C0                 - test eax,eax
        // 7FF8A7EF0017 - 41 58                 - pop r8
        // 7FF8A7EF0019 - 5A                    - pop rdx
        // 7FF8A7EF001A - 59                    - pop rcx
        // 7FF8A7EF001B - 58                    - pop rax
        // 7FF8A7EF001C - 0F84 0854EFFC         - je GameAssembly.dll+10C542A
        // 7FF8A7EF0022 - F3 0F10 05 12B387FD   - movss xmm0,[GameAssembly.dll+1BBDD60]
        // 7FF8A7EF002A - E9 9952EFFC           - jmp GameAssembly.dll+10C4E0C
        // 7FF8A7EF002F - 90                    - nop 
        // 7FF8A7EF0030 - 90                    - nop 
        // 7FF8A7EF0031 - 90                    - nop 
        // 7FF8A7EF0032 - 90                    - nop 
        // 7FF8A7EF0033 - 90                    - nop 
        // 7FF8A7EF0034 - 90                    - nop 
        // 7FF8A7EF0035 - 90                    - nop 
        // 7FF8A7EF0036 - 90                    - nop 
        // 7FF8A7EF0037 - 90                    - nop 
        // 7FF8A7EF0038 - 90                    - nop 
        // 7FF8A7EF0039 - C3                    - ret 
        //  */
        // private StubInjection<ComboDownTiltStubFunction> PatchComboDownTilt()
        // {
        //     StubFunctionData data = new StubFunctionData()
        //     {
        //         Assembly = new byte[] {
        //             0x50,0x51,0x52,0x41,0x50,0x48,0x83,0xEC,0x20,0x48,0x8B,0xCB,0xE8,0x1E,0x00,0x00,0x00,
        //             0x48,0x83,0xC4,0x20,0x85,0xC0,0x41,0x58,0x5A,0x59,0x58,0x0F,0x84,0xF3,0x3E,0x81,0x01,
        //             0xF3,0x0F,0x10,0x05,0xEA,0x99,0x8A,0x02,0xE9,0x85,0x3E,0x81,0x01,0x90,0x90,0x90,0x90,
        //             0x90,0x90,0x90,0x90,0x90,0x90,0xC3,
        //         },
        //         AssemblyHookFunctionOffset = 0x2F,
        //         AssemblyReturnJumpOffset = 0x2A,
        //         Redirects = new[] {
        //             new StubFunctionData.RelativeRedirect { AddressOffset = 0x22 + 4, TargetAddress = Env.GameDllBase + 0x10C542A } ,
        //             new StubFunctionData.RelativeRedirect { AddressOffset = 0x1C + 2, TargetAddress = Env.GameDllBase + 0x10C4E0C }
        //         },
        //         FunctionOffset = 0x10c49f0,
        //         InjectionOffset = 0x10c4da3 - 0x10c49f0,
        //         InjectionReplacementSize = 8,
        //         ReplacedBytesToVerify = new byte[] { 0xF3, 0x0F, 0x10, 0x05 }
        //     };
        //     StubFunction funcs = DoStubInjection(data, Env);

        //     NativeHook<ComboDownTiltStubFunction> hook = new();
        //     unsafe
        //     {
        //         delegate* unmanaged[Fastcall]<IntPtr, Int32> detourPtr = &NativeStubInject_ComboUpTilt;
        //         hook.Target = funcs.StubStart;
        //         hook.Detour = (IntPtr)detourPtr;
        //         hook.Attach();
        //     }

        //     return new StubInjection<ComboDownTiltStubFunction>()
        //     {
        //         Data = data,
        //         Function = funcs,
        //         Hook = hook
        //     };
        // }
    }
}
