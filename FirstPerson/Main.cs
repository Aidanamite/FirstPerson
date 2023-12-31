using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using System.Runtime.CompilerServices;
using Object = UnityEngine.Object;
using ConfigTweaks;

namespace FirstPerson
{
    [BepInPlugin("com.aidanamite.FirstPerson", "First Person", "1.0.1")]
    [BepInDependency("com.aidanamite.ConfigTweaks")]
    public class Main : BaseUnityPlugin
    {
        [ConfigField]
        public static bool Enabled = true;
        [ConfigField]
        public static KeyCode Toggle = KeyCode.KeypadPlus;
        [ConfigField]
        public static float LookSensitivity = 1.5f;
        [ConfigField]
        public static bool ThirdPersonFlight = false;
        [ConfigField]
        public static bool ControlFlightTurn = true;
        [ConfigField]
        public static KeyCode HoldToUnlockMouse = KeyCode.Q;

        public void Awake()
        {
            Application.focusChanged += FocusChanged;
            new Harmony("com.aidanamite.FirstPerson").PatchAll();
            Logger.LogInfo("Loaded");
        }

        public static bool ApplicationFocused = Application.isFocused;
        public void FocusChanged(bool focused)
        {
            ApplicationFocused = true;
            CheckLockMouse();
        }

        public static void CheckLockMouse()
        {
            Patch_HideAvatar.skip = true;
            if (Patch_CameraController.IsFPSMode && ApplicationFocused && UiAvatarControls.pInstance?.pAVController)
            {
                if ((Cursor.lockState != CursorLockMode.Locked) == Patch_CameraController.LockMouse)
                    Cursor.lockState = Patch_CameraController.LockMouse ? CursorLockMode.Locked : CursorLockMode.None;
                var l = new List<Renderer>();
                var drag = UiAvatarControls.pInstance.pAVController.GetDraggable();
                if (drag)
                    foreach (var r in drag.GetComponentsInChildren<Renderer>(true))
                        if (r && r.enabled)
                            l.Add(r);
                if (UiAvatarControls.pInstance.pAVController.pPlayerCarrying)
                    foreach (var r in UiAvatarControls.pInstance.pAVController.pCarriedObject.GetComponentsInChildren<Renderer>(true))
                        if (r && r.enabled)
                            l.Add(r);
                UiAvatarControls.pInstance.pAVController.EnableRenderer(false);
                foreach (var r in l)
                    if (r)
                        r.enabled = true;
            }
            else
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                    Cursor.lockState = CursorLockMode.None;
                if (UiAvatarControls.pInstance?.pAVController && !UiAvatarControls.pInstance.pAVController.AvatarHidden)
                    UiAvatarControls.pInstance.pAVController.EnableRenderer(true);
                Patch_GetKAAxis.blockHorizontal = false;
                Patch_GetKAAxis.blockVertical = false;
            }
            Patch_HideAvatar.skip = false;
        }

        public void Update()
        {
            if (Patch_CameraController.IsFPSMode && (!Patch_CameraController.instance || Patch_CameraController.updatesSinceUpdate > 2))
            {
                Patch_CameraController.IsFPSMode = false;
                CheckLockMouse();
            }
            if (Input.GetKeyDown(Toggle))
            {
                Enabled = !Enabled;
                Config.Save();
            }
            if (Patch_CameraController.updatesSinceUpdate < 32768)
                Patch_CameraController.updatesSinceUpdate++;
        }
    }

    static class ExtentionMethods
    {
        static FieldInfo _mDraggable = typeof(AvAvatarController).GetField("mDraggable", ~BindingFlags.Default);
        public static ObDraggable GetDraggable(this AvAvatarController controller) => (ObDraggable)_mDraggable.GetValue(controller);

        static FieldInfo _mSeatBone = typeof(SanctuaryPet).GetField("mSeatBone", ~BindingFlags.Default);
        static FieldInfo _mAvatarMountOffset = typeof(SanctuaryPet).GetField("mAvatarMountOffset", ~BindingFlags.Default);
        public static Vector3 GetCameraPos(this SanctuaryPet pet, float cameraOffset)
        {
            var t = ((Transform)_mSeatBone.GetValue(pet));
            return t.position + t.up * cameraOffset + ((Vector3)_mAvatarMountOffset.GetValue(pet));
        }

        static FieldInfo _mTurnFactor = typeof(AvAvatarController).GetField("mTurnFactor", ~BindingFlags.Default);
        public static float GetTurnFactor(this AvAvatarController controller) => (float)_mTurnFactor.GetValue(controller);
    }

    [HarmonyPatch(typeof(CaAvatarCam), "LateUpdate")]
    static class Patch_CameraController
    {
        public static CaAvatarCam instance;
        public static bool IsFPSMode = false;
        public static float x = 0;
        public static float y = 0;
        public static Vector3 camOffset = Vector3.up * 1.5f;
        static bool lockMouse = true;
        public static int updatesSinceUpdate = 0;
        public static bool LockMouse
        {
            get => lockMouse;
            set
            {
                lockMouse = value;
                Main.CheckLockMouse();
            }
        }
        static bool Prefix(CaAvatarCam __instance, CaAvatarCam.CamInterpData[] ___mCamData, CaAvatarCam.CameraLayer ___mCurLayer, bool ___mForceFreeRotate, Transform ___mTarget)
        {
            updatesSinceUpdate = 0;
            instance = __instance;
            Patch_GetKAAxis.blockHorizontal = false;
            Patch_GetKAAxis.blockVertical = false;
            Patch_AvatarVelocityUpdate.HorizontalControl = false;
            if (Main.Enabled && AvAvatar.pState != AvAvatarState.PAUSED && ___mCamData != null && ___mCamData[(int)___mCurLayer].mode == CaAvatarCam.CameraMode.MODE_RELATIVE && (___mForceFreeRotate || AvAvatar.pState != AvAvatarState.NONE) && (!Main.ThirdPersonFlight || AvAvatar.pSubState == AvAvatarSubState.FLYING || AvAvatar.pSubState == AvAvatarSubState.GLIDING) && AvAvatar.pSubState != AvAvatarSubState.WALLCLIMB && !(MyRoomsIntMain.pInstance && MyRoomsIntMain.pInstance.pIsBuildMode))
            {
                var data = ___mCamData[(int)___mCurLayer];
                if (!IsFPSMode)
                {
                    IsFPSMode = true;
                    x = data.lookAt.eulerAngles.y + data.offset.x;
                    y = data.offset.y;
                    Main.CheckLockMouse();
                }
                if (LockMouse == Input.GetKey(Main.HoldToUnlockMouse))
                    LockMouse = !LockMouse;
                var xDelta = LockMouse ? Input.GetAxis("Mouse X") + KAInput.GetAxis("CameraRotationX") : 0;
                var yDelta = LockMouse ? Input.GetAxis("Mouse Y") + KAInput.GetAxis("CameraRotationY") : 0;
                x = (x + xDelta * Main.LookSensitivity) % 360;
                y = Mathf.Clamp(y + -yDelta * Main.LookSensitivity, -90, 90);
                __instance.camera.transform.rotation = Quaternion.Euler(0, x, 0) * Quaternion.Euler(y, 0, 0);
                var controller = data.lookAt?.GetComponent<AvAvatarController>();
                if (controller)
                {
                    __instance.camera.transform.position = (controller.pPlayerMounted && SanctuaryManager.pCurPetInstance ? SanctuaryManager.pCurPetInstance.GetCameraPos(0.3f) : (data.lookAt.position + Vector3.up * 1.3f));
                    if (!Input.GetKey(KeyCode.LeftAlt) && !(controller.IsFlyingOrGliding() && !Main.ControlFlightTurn))
                    {
                        Patch_GetKAAxis.blockHorizontal = true;
                        var change = Mathf.DeltaAngle(data.lookAt.eulerAngles.y, x);
                        if (controller.IsFlyingOrGliding())
                        {
                            var turnRate = Math.Abs(controller.pFlyingData._YawTurnRate * 90 * Time.deltaTime);
                            change = Math.Max(-turnRate, Math.Min(turnRate, change));
                            Patch_GetKAAxis.blockVertical = true;
                            var pitchRange = Math.Max(controller.pFlyingData._FlyingMaxDownPitch, controller.pFlyingData._FlyingMaxUpPitch);
                            float rx = Math.Max(-pitchRange, Math.Min(pitchRange, y));
                            float rz = Math.Max(Math.Min(change / turnRate,1),-1) * controller.pFlyingData._MaxRoll;
                            controller.pFlyingPitch = y / 360;
                            controller._MainRoot.localEulerAngles = new Vector3(rx, 0f, rz);
                            var transform = controller.transform.Find("Wing");
                            if (transform)
                                transform.localEulerAngles = controller._MainRoot.localEulerAngles;
                        }
                        else if (controller.pSubState == AvAvatarSubState.UWSWIMMING)
                        {
                            Patch_GetKAAxis.blockVertical = true;
                            controller.pUWSwimmingPitch = Mathf.MoveTowards(controller.pUWSwimmingPitch, y / 360, controller.pUWSwimmingData._PitchTurnRate * Time.deltaTime);
                            change = Math.Max(-controller.pUWSwimmingData._RollTurnRate * Time.deltaTime * 360, Math.Min(controller.pUWSwimmingData._RollTurnRate * Time.deltaTime * 360, change));
                        }
                        else
                            Patch_AvatarVelocityUpdate.HorizontalControl = true;
                        data.lookAt.Rotate(0, change, 0);
                        var drag = controller.GetDraggable();
                        if (drag)
                            drag.RotateAround(data.lookAt.position, data.lookAt.up, change);
                    }
                }
                else
                    __instance.camera.transform.position = data.GetLookAt();
                if (Input.GetMouseButtonUp(1))
                    Object.FindObjectOfType<UiAvatarCSM>()?.OpenCSM();
                data.offset.y = y;
                data.offset.x = 0;
                return false;
            }
            else if (IsFPSMode)
            {
                IsFPSMode = false;
                Main.CheckLockMouse();
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AvAvatarController), "VelocityUpdate")]
    static class Patch_AvatarVelocityUpdate
    {
        public static bool HorizontalControl = false;
        static ConditionalWeakTable<AvAvatarController, ExtraControllerData> table = new ConditionalWeakTable<AvAvatarController, ExtraControllerData>();
        static void Postfix(AvAvatarController __instance, float targetSpeed, ref Vector3 ___mVelocity, ref float ___mCurSpeed)
        {
            var extra = table.GetOrCreateValue(__instance);
            if (HorizontalControl)
            {
                Patch_GetKAAxis.blockHorizontal = false;
                var speed = KAInput.GetAxis("Horizontal");
                Patch_GetKAAxis.blockHorizontal = true;
                if (__instance.pSubState == AvAvatarSubState.NORMAL || __instance.pSubState == AvAvatarSubState.DIVESUIT)
                {
                    speed *= (__instance.pCurrentStateData._MaxForwardSpeed + __instance.pCurrentStateData._MaxBackwardSpeed) / 2 * (__instance.pPlayerCarrying ? 0.75f : 1f);
                    if (SanctuaryManager.pCurPetInstance != null && __instance.pPlayerMounted)
                        speed *= SanctuaryManager.pCurPetInstance.GetMountedSpeedModifer();
                }
                extra.mCurSpeed = Mathf.Lerp(extra.mCurSpeed, speed, __instance.pCurrentStateData._Acceleration * Time.deltaTime);
                var vector = __instance.transform.right;
                vector.y = 0f;
                vector = vector.normalized * extra.mCurSpeed;
                ___mVelocity.x += vector.x;
                ___mVelocity.z += vector.z;
            }
            else
                extra.mCurSpeed = 0;

        }
        class ExtraControllerData
        {
            public float mCurSpeed;
        }
    }

    [HarmonyPatch(typeof(KAInput), "GetAxis")]
    static class Patch_GetKAAxis
    {
        public static bool blockHorizontal = false;
        public static bool blockVertical = false;
        static bool Prefix(string inAxisName, ref float __result)
        {
            if (blockHorizontal && inAxisName == "Horizontal")
            {
                __result = 0;
                return false;
            }
            if (blockVertical && inAxisName == "Vertical")
            {
                __result = 0;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    static class Patch_HideAvatar
    {
        public static bool skip = false;
        [HarmonyPatch(typeof(UiAvatarControls), "HideAvatar")]
        [HarmonyPostfix]
        static void UiAvatarControls()
        {
            if (skip)
                return;
            Main.CheckLockMouse();
        }
        [HarmonyPatch(typeof(ObAvatarRespawnDelayed), "HideAvatar")]
        [HarmonyPostfix]
        static void ObAvatarRespawnDelayed(bool hide)
        {
            if (skip)
                return;
            if (!hide)
                Main.CheckLockMouse();
        }
        [HarmonyPatch(typeof(AvAvatarController), "EnableRenderer")]
        [HarmonyPostfix]
        static void UiAvatarControls(bool enable)
        {
            if (skip)
                return;
            if (enable)
                Main.CheckLockMouse();
        }
        [HarmonyPatch(typeof(AvatarBlink), "Update")]
        [HarmonyPrefix]
        static bool BlinkUpdate() => !Patch_CameraController.IsFPSMode;
    }
}