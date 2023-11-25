using Harmony;
using Il2Cpp;
using Il2CppACT;
using Il2CppAPP;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static Il2Cpp.RicassoActor;
using static MelonLoader.MelonLogger;

namespace RiccaMod.Patches
{
    internal class AirDash : IPatch
    {
        private Environment Env;
        public AirDash(Environment env) { Env = env; }

        public string Name { get; } = "Air Dodge";

        public string Description { get; } = "While in the air, press guard, special attack and a direction to perform a melee air dodge.";

        public bool Enabled { get; protected set; }

        public void LoadSettings(MelonPreferences_Category cat)
        {
            var enableEntry = cat.CreateEntry<bool>("AirDash_Enabled", true);
            enableEntry.DisplayName = Name;
            enableEntry.Description = Description;
            Enabled = enableEntry.Value;

            var timingEntry = cat.CreateEntry<float>("AirDash_TimeS", TimingS);
            timingEntry.DisplayName = "Air Dodge Duration";
            timingEntry.Description = "Time in Seconds of air dodge movement.";
            TimingS = timingEntry.Value;

            var inertiaEntry = cat.CreateEntry<float>("AirDash_InertiaBoost", Inertia);
            inertiaEntry.DisplayName = "Air Dodge Inertia";
            inertiaEntry.Description = "Velocity at the start of the dodge. Velocity falls off exponentially.";
            Inertia = inertiaEntry.Value;

            var scaleWithWeight = cat.CreateEntry<bool>("AirDash_ScaleWithWeight", ScaleWithWeight);
            scaleWithWeight.DisplayName = "Air Dodge Scale with Weight";
            scaleWithWeight.Description = "If true, the speed values are scaled by the weight-based jump bonus. "
                + "In practise, that one is between 1.0 and about 1.02, so use the scale to make it have an impact.";
            ScaleWithWeight = scaleWithWeight.Value;

            var scaleMultiplyEntry = cat.CreateEntry<float>("AirDash_WeightScaleMultiplier", ScaleWithWeightMultiplier);
            scaleMultiplyEntry.DisplayName = "Air Dodge Weight Scale Multiplier";
            scaleMultiplyEntry.Description = "Multiplier on Weight Scale (see comment on Scale with Weight bool).";
            ScaleWithWeightMultiplier = scaleMultiplyEntry.Value;

            var usedButtonEntry = cat.CreateEntry<int>("AirDash_UsedButton", UsedButton);
            usedButtonEntry.DisplayName = "Air Dodge Input";
            usedButtonEntry.Description = "Determines which button has to be pressed to airdodge. Note that settings other than 0 require you to put"
                + "map these inputs to a different button first, as they overlap with regular gameplay-buttons by default. "
                + String.Join(", ", PossibleInputs.Select((x, i) => $"{i} = {x.Name}"));
            UsedButton = usedButtonEntry.Value;

            var wavedashHoldEntry = cat.CreateEntry<bool>("AirDash_AllowHoldWavedash", AllowHoldWavedash);
            wavedashHoldEntry.DisplayName = "Hold Dodge to Wavedash";
            wavedashHoldEntry.Description = "If true, Allows holding of the air dodge button (unless mapped to special+guard) on ground to immediately airdodge once becoming airborne. "
                + "This makes it way easier to wavedash by just holding the button and pressing jump";
            AllowHoldWavedash = wavedashHoldEntry.Value;

            var iframeBaseSEntry = cat.CreateEntry<float>("AirDash_IFrameBaseS", IFrameBaseS);
            iframeBaseSEntry.DisplayName = "Air Dodge IFrame base duration";
            iframeBaseSEntry.Description = "Time in Seconds that the iframes in an airdodge last.";
            IFrameBaseS= iframeBaseSEntry.Value;

            var iframeScaleSEntry = cat.CreateEntry<float>("AirDash_IFrameScaleS", IFrameScaleS);
            iframeScaleSEntry.DisplayName = "Air Dodge IFrame level duration bonus";
            iframeScaleSEntry.Description = "Time in Seconds added to the iframe duration for each level in the backstep perk.";
            IFrameScaleS = iframeScaleSEntry.Value;
        }

        public static AirDash? CurrInstance = null; 


        private static float TimingS = 0.45f;
        private static float Inertia = 11.5f;
        private static bool ScaleWithWeight = true;
        private static float ScaleWithWeightMultiplier = 1.25f;
        private static float SpecialAttackLockoutS = 0.1f;
        private static int UsedButton = 0;
        private static bool AllowHoldWavedash = false;
        private static float IFrameBaseS = 0.1f;
        private static float IFrameScaleS = 0.1f;

        private class Input
        {
            public string Name { get; init; }
            public Func<RicassoActor, bool> CheckFunc { get; init; }
        }
        private static bool IsButtonPressed(InputManager.ButtonKind btn)
        {
            if(!AllowHoldWavedash) 
            {
                return App.inputManager.GetButtonDown(btn);
            }
            else
            {
                var state = App.inputManager.GetButtonState(btn);
                return state == InputManager.ButtonState.Trigger || state == InputManager.ButtonState.Hold;
            }
        }
        private static Input[] PossibleInputs = new Input[5] {
            new Input { 
                Name = "Guard + Special Attack", 
                CheckFunc = (RicassoActor r) => r.input.specialAttack.IsPress  && r.input.walk.IsPress
            },
            new Input
            {
                Name = "Translate Camera",
                CheckFunc = (RicassoActor r) => IsButtonPressed(InputManager.ButtonKind.CameraTranslate)
            },
            new Input
            {
                Name = "Submit",
                CheckFunc = (RicassoActor r) => IsButtonPressed(InputManager.ButtonKind.Submit)
            },
            new Input
            {
                Name = "Cancel",
                CheckFunc = (RicassoActor r) => IsButtonPressed(InputManager.ButtonKind.Cancel)
            },
            new Input
            {
                Name = "CameraRotate",
                CheckFunc = (RicassoActor r) => IsButtonPressed(InputManager.ButtonKind.CameraRotate)
            },
        };

        private RicassoActor? lastActor;
        private System.Collections.IEnumerator? activeAirdodge = null;
        private Vector3 lastAirdogeInertia;
        private float lastAirdodgeTime = 0;

        private bool inSpecialAttackLockout = false; //TODO wrap these into a class so you cant forget to se all three at once
        private RicassoActor? specialAttackLockoutActor = null;
        private float specialAttackLockoutPassed = 0;
        private bool allreadyDashed = false;

        /*
         * So, this games input detection system merges multiple conrols together, so the enum names
         * arent very sensible. Here is what they do:
		    Up,                 those 4 actually make sense
		    Down,               those 4 actually make sense
		    Left,               those 4 actually make sense
		    Right,              those 4 actually make sense
		    Attack,             combat action
		    Jump,               combat action
		    SpecialAttack,      combat action
		    Walk,               ALSO guard
		    Pause,              also close menu button
		    SkipMessage,        also free camera button
		    Submit,             also investigate (submit as in ok on menus)
		    Cancel,             the button that moves cursors to cancel options (but does not press the)
		    CameraRotate,       also play audio in dialoges
		    CameraTranslate     actually unique
         */

        private static float InertiaByTime(float timeS)
        {

            //here are fox velocity numbers during his 28 frame airdodge: 2.79 2.511 2.2599 2.03391 1.83052 1.64747 1.48272
            //1.33445 1.201 1.0809 0.97281 0.87553 0.78798 0.70918 0.63826 0.57444 0.51699 0.46529 0.41876 0.37689 0.3392
            //0.30528 0.27475 0.24728 0.22255 0.20029 0.18026 0.16224 0.14601  
            //this interpolates to almost exactly 3.1*0.9^x
            //so, lets uncreatively copy it
            //also, lets stay at max speed for a while at the beginning to make wavedashing more forgiving
            float passedPercent = (timeS / TimingS);
            float speed;
            if (passedPercent < 0.1f) speed = Inertia;
            else speed = Inertia * MathF.Pow(0.9f, passedPercent * 29.0f); //technically it would be +1/28 since melee starts at 1, but this gives us a little more speed for perfect wavedashes
            return speed;
        }

        static System.Collections.IEnumerator AirDodgeCoroutine(RicassoActor instance, float xDir, float yDir)
        {
            instance.useGravity = false;

            instance.VoiceAudioID.jump.Play(instance.characterAudioSource);
            instance.characterAudioSource.PlayCharacterAudio(instance.seAudioId.airJump);
            instance.ActionInfo.SetTrigger(instance.characterAnimator, "air_magicFailed", -1.0f, false);
            instance.etherealWingParticleSystem.Play(true);

            instance.invinsibleType = CharacterActor.InvinsibleType.EtherealAll;

            UnityEngine.Vector3 inertia = new UnityEngine.Vector3(xDir, yDir);
            inertia.Normalize();

            while (instance.ActionInfo.Time < TimingS)
            {
                float iframeTime = IFrameBaseS + instance.skillInfo.backStepLevel * IFrameScaleS;
                if (instance.ActionInfo.Time > iframeTime)
                {
                    instance.invinsibleType = CharacterActor.InvinsibleType.None;
                }
                CurrInstance.lastAirdodgeTime = instance.ActionInfo.Time;
                float speed = InertiaByTime(instance.ActionInfo.Time);
                if (ScaleWithWeight) 
                    speed *= instance.GetWeightJumpScale() * ScaleWithWeightMultiplier;

                CurrInstance.lastAirdogeInertia = inertia * speed;
                instance.SetInertia(CurrInstance.lastAirdogeInertia, true);
                yield return null;
            }

            instance.invinsibleType = CharacterActor.InvinsibleType.None;
            instance.etherealWingParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            instance.SetInertia(new UnityEngine.Vector3(0, 0), true);
            instance.useGravity = true;
        }
        static bool StopAirDodge(RicassoActor instance, ActionStopInfo info)
        {
            if (CurrInstance.lastActor != instance) return true;
            instance.useGravity = true;
            instance.invinsibleType = CharacterActor.InvinsibleType.None;
            instance.etherealWingParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (info.reason == ActionStopInfo.Reason.Landing)
            {
                //because we can only do 45 degree inputs, wavedashing is somewhat disappointing. To help with that,
                //we rotate the final inertia packet a little bit if we hit the ground quickly, esentially
                //pretending we had a flatter input angle
                if (CurrInstance.lastAirdogeInertia.x != 0)
                {
                    float speed = InertiaByTime(CurrInstance.lastAirdodgeTime);
                    if (ScaleWithWeight)
                        speed *= instance.GetWeightJumpScale() * ScaleWithWeightMultiplier;
                    float minAngle = 20;
                    float maxAngle = 45;
                    float percent;
                    if (CurrInstance.lastAirdodgeTime < 0.05f) percent = 1.0f;
                    else percent = (CurrInstance.lastAirdodgeTime - 0.05f) / 0.1f;

                    float newAngle = minAngle + (maxAngle - minAngle) * percent;
                    if (CurrInstance.lastAirdogeInertia.x < 0) newAngle = 180 + newAngle;
                    else newAngle = 360 - newAngle;

                    float rads = (MathF.PI / 180.0f) * newAngle;
                    Vector3 newInertia = new Vector3(MathF.Cos(rads) * speed, MathF.Sin(rads) * speed);
                    instance.SetInertia(newInertia, true);
                }
                
                //prevent special attacks for a while so a longer airdodge input isnt reinterpreted as one (in case that one is part of the input)
                if(UsedButton == 0)
                {
                    CurrInstance.inSpecialAttackLockout = true;
                    CurrInstance.specialAttackLockoutPassed = 0;
                    CurrInstance.specialAttackLockoutActor = instance;
                }
                    
            }
            return true;
        }

        private static void Postfix_StartJump(RicassoActor __instance, RicassoActor.JumpType jumpType)
        {
            //only reset this (and thus allow 1 dash) at jumps from the ground, or in infinite jump mode
            if (__instance.specialAction.airJumpCount == 0 || __instance.hasEnergyWing)
            {
                CurrInstance.lastActor = __instance;
                CurrInstance.allreadyDashed = false;
            }
            
        }

        delegate bool ActionStopCallback(ActionStopInfo info); 
        private static void Postfix_ControlJump(RicassoActor __instance)
        {
            //ignore if currently airdoging
            if (__instance != CurrInstance.lastActor) return;
            if (CurrInstance.activeAirdodge != null) return;

            bool pressed = PossibleInputs[UsedButton <= PossibleInputs.Length ? UsedButton : 0].CheckFunc(__instance);
            if (pressed)
            {
                float xDir = __instance.input.right.IsPress ? 1 : (__instance.input.left.IsPress ? -1 : 0);
                float yDir = __instance.input.up.IsPress ? 1 : (__instance.input.down.IsPress ? -1 : 0);
                if (xDir != 0 || yDir != 0)
                {
                    //didnt figure out how to call SetAction, so we induce a backstep instead
                    //and hook that one
                    CurrInstance.activeAirdodge = AirDodgeCoroutine(__instance, xDir, yDir);
                    __instance.StartAction(__instance.StandGuardBackStepCoroutine(), __instance.stopGuardAttackCallback, ActionStopInfo.Reason.StartOtherAction);
                    CurrInstance.inSpecialAttackLockout = false; //in case this one was still going
                }
            }
        }

        private const bool SKIP_ORIGINAL = false;
        private const bool DONT_SKIP_ORIGINAL = true;
        private static bool Prefix_BackstepInject_AirdodgeReplacement(ref bool __result)
        {
            if (CurrInstance.activeAirdodge == null) return DONT_SKIP_ORIGINAL;

            __result = CurrInstance.activeAirdodge.MoveNext();
            if(!__result)
            { 
                CurrInstance.lastActor = null;
                CurrInstance.activeAirdodge = null;
                //note: no lockout on normal end
            }
            return SKIP_ORIGINAL;
        }
        private static bool Prefix_GuardEndInject_AirdodgeReplacement(RicassoActor __instance, ActionStopInfo stopInfo)
        {
            if (CurrInstance.activeAirdodge != null)
            {
                StopAirDodge(CurrInstance.lastActor, stopInfo);
                CurrInstance.lastActor = null;
                CurrInstance.activeAirdodge = null;
                //lockout for a short while
                if(UsedButton == 0)
                {
                    CurrInstance.inSpecialAttackLockout = true;
                    CurrInstance.specialAttackLockoutPassed = 0;
                    CurrInstance.specialAttackLockoutActor = __instance;
                }
                
            }
            return DONT_SKIP_ORIGINAL;
        }

        private static bool Prefix_SpecialAttackInput(SpecialAttackData __instance, ref InputManager.ButtonState buttonState, float deltaTime)
        {
            //ignore special attack inputs for a while if requested
            if (CurrInstance.specialAttackLockoutActor != __instance.parentActor) return DONT_SKIP_ORIGINAL;
            if (!CurrInstance.inSpecialAttackLockout) return DONT_SKIP_ORIGINAL;
            buttonState = InputManager.ButtonState.None;
            CurrInstance.specialAttackLockoutPassed += deltaTime;
            if(CurrInstance.specialAttackLockoutPassed >= SpecialAttackLockoutS)
            {
                CurrInstance.inSpecialAttackLockout = false;
                CurrInstance.specialAttackLockoutActor = null;
            }
            return DONT_SKIP_ORIGINAL;
        }

        public void Patch()
        {
            CurrInstance = this;
            if (!Enabled) return;

            MethodInfo? method = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StartJump), new Type[] { typeof(RicassoActor.JumpType) });
            MethodInfo? replacement = typeof(AirDash).GetMethod(nameof(Postfix_StartJump), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException($"Failed to find {(method == null ? "Injection" : "Replacement")} Method: Postfix_StartJump");
            }
            Env.Harmony.Patch(method, null, new HarmonyLib.HarmonyMethod(replacement), null);

            method = typeof(HumanoidCharacter).GetMethod(nameof(HumanoidCharacter.ControlJumpInertia));
            replacement = typeof(AirDash).GetMethod(nameof(Postfix_ControlJump), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException($"Failed to find {(method == null ? "Injection" : "Replacement")} Method: ControlJumpInertia");
            }
            Env.Harmony.Patch(method, null, new HarmonyLib.HarmonyMethod(replacement), null);

            method = typeof(RicassoActor._StandGuardBackStepCoroutine_d__246).GetMethod(nameof(RicassoActor._StandGuardBackStepCoroutine_d__246.MoveNext));
            replacement = typeof(AirDash).GetMethod(nameof(Prefix_BackstepInject_AirdodgeReplacement), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException($"Failed to find {(method == null ? "Injection" : "Replacement")} Method: StandGuardBackStepCoroutine");
            }
            Env.Harmony.Patch(method, new HarmonyLib.HarmonyMethod(replacement), null, null);

            method = typeof(RicassoActor).GetMethod(nameof(RicassoActor.StopGuradAttack), new Type[] { typeof(ActionStopInfo) });
            replacement = typeof(AirDash).GetMethod(nameof(Prefix_GuardEndInject_AirdodgeReplacement), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException($"Failed to find {(method == null ? "Injection" : "Replacement")} Method: StopGuradAttack");
            }
            Env.Harmony.Patch(method, new HarmonyLib.HarmonyMethod(replacement), null, null);

            method = typeof(SpecialAttackData).GetMethod(nameof(SpecialAttackData.Update), new Type[] { typeof(InputManager.ButtonState), typeof(float) });
            replacement = typeof(AirDash).GetMethod(nameof(Prefix_SpecialAttackInput), BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || replacement == null)
            {
                throw new InvalidOperationException($"Failed to find {(method == null ? "Injection" : "Replacement")} Method: SpecialAttackData.Update");
            }
            Env.Harmony.Patch(method, new HarmonyLib.HarmonyMethod(replacement), null, null);
        }

        public void Unpatch()
        {
            if (!Enabled) return;
        }
    }
}
