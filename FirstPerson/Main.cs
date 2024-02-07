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
using System.Diagnostics;
using System.Globalization;

namespace FirstPerson
{
    [BepInPlugin("com.aidanamite.FirstPerson", "First Person", "1.3.0")]
    [BepInDependency("com.aidanamite.ConfigTweaks")]
    public class Main : BaseUnityPlugin
    {
        static Main()
        {
            TomlTypeConverter.AddConverter(typeof(Dictionary<string, Vector3>), new TypeConverter()
            {
                ConvertToObject = (str, type) =>
                {
                    var d = new Dictionary<string, Vector3>();
                    if (str == null)
                        return d;
                    var split = str.Split('|');
                    foreach (var i in split)
                        if (i.Length != 0)
                        {
                            var parts = i.Split(',');
                            if (parts.Length != 4)
                                Debug.LogWarning($"Could not load entry \"{i}\". Entries must have exactly 4 values divided by commas" );
                            else
                            {
                                if (d.ContainsKey(parts[0]))
                                    Debug.LogWarning($"Duplicate entry name \"{parts[0]}\" from \"{i}\". Only last entry will be kept");
                                var vector = new Vector3();
                                for (int j = 0; j < 3; j++)
                                    if (parts[j + 1].Length == 0)
                                        vector[j] = 0;
                                    else if (float.TryParse(parts[j + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                                        vector[j] = v;
                                    else
                                        Debug.LogWarning($"Value \"{parts[j + 1]}\" in \"{i}\". Could not be parsed as a number");
                                d[parts[0]] = vector;
                            }
                        }
                    return d;
                },
                ConvertToString = (obj, type) =>
                {
                    if (!(obj is Dictionary<string, Vector3> d))
                        return "";
                    var str = new StringBuilder();
                    var k = d.Keys.ToList();
                    k.Sort();
                    foreach (var key in k)
                    {
                        if (str.Length > 0)
                            str.Append("|");
                        str.Append(key);
                        for (int i = 0; i < 3; i++)
                        {
                            str.Append(",");
                            str.Append(d[key][i].ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    return str.ToString();
                }
            });
        }

        [ConfigField]
        public static bool Enabled = true;
        [ConfigField]
        public static KeyCode Toggle = KeyCode.KeypadPlus;
        [ConfigField]
        public static float LookSensitivity = 1.5f;
        [ConfigField]
        public static FlightMode FlightMode = FlightMode.CameraControlsFlight;
        [ConfigField]
        public static KeyCode HoldToUnlockMouse = KeyCode.Q;
        [ConfigField]
        public static KeyCode GeneralInteract = KeyCode.E;
        [ConfigField]
        public static Dictionary<string, Vector3> RidingCameraOffsets = new Dictionary<string, Vector3>();

        static Main instance;
        public void Awake()
        {
            var key = Config.OrphanedEntries.Keys.FirstOrDefault(x => x.Key == "ThirdPersonFlight");
            if (key != null)
            {
                if (Config.OrphanedEntries[key].ToLowerInvariant() == "true")
                    FlightMode = FlightMode.ThirdPerson;
                Config.OrphanedEntries.Remove(key);
                key = Config.OrphanedEntries.Keys.FirstOrDefault(x => x.Key == "ControlFlightTurn");
                if (key != null)
                    Config.OrphanedEntries.Remove(key);
                Config.Save();
            }
            else
            {
                key = Config.OrphanedEntries.Keys.FirstOrDefault(x => x.Key == "ControlFlightTurn");
                if (Config.OrphanedEntries[key].ToLowerInvariant() == "false")
                    FlightMode = FlightMode.NoControl;
                Config.OrphanedEntries.Remove(key);
                Config.Save();
            }
            instance = this;
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

        public static Vector3 GetRidingCameraOffset(string dragon)
        {
            if (RidingCameraOffsets.TryGetValue(dragon, out var v))
                return v;
            RidingCameraOffsets[dragon] = default;
            instance.Config.Save();
            return default;
        }
    }

    public enum FlightMode
    {
        ThirdPerson,
        NoControl,
        FlightMovesCamera,
        FlightControlsCamera,
        CameraControlsFlight
    }

    static class ExtentionMethods
    {
        static FieldInfo _mDraggable = typeof(AvAvatarController).GetField("mDraggable", ~BindingFlags.Default);
        public static ObDraggable GetDraggable(this AvAvatarController controller) => (ObDraggable)_mDraggable.GetValue(controller);

        static FieldInfo _mSeatBone = typeof(SanctuaryPet).GetField("mSeatBone", ~BindingFlags.Default);
        static FieldInfo _mAvatarMountOffset = typeof(SanctuaryPet).GetField("mAvatarMountOffset", ~BindingFlags.Default);
        public static Vector3 GetCameraPos(this SanctuaryPet pet, float cameraOffset)
        {
            var customOffset = Main.GetRidingCameraOffset(pet.GetTypeSettings()._Name);
            var t = pet.GetSeatBone();
            return t.position + t.up * (cameraOffset + customOffset.y) + t.forward * customOffset.z + t.right * customOffset.x + ((Vector3)_mAvatarMountOffset.GetValue(pet));
        }
        public static Transform GetSeatBone(this SanctuaryPet pet) => (Transform)_mSeatBone.GetValue(pet);

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
        public static float previousPet = 1000;
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
            if (Main.Enabled && ___mCamData != null && ___mCamData[(int)___mCurLayer].mode == CaAvatarCam.CameraMode.MODE_RELATIVE && (___mForceFreeRotate || AvAvatar.pState != AvAvatarState.NONE) && (Main.FlightMode != FlightMode.ThirdPerson || (AvAvatar.pSubState != AvAvatarSubState.FLYING && AvAvatar.pSubState != AvAvatarSubState.GLIDING)) && AvAvatar.pSubState != AvAvatarSubState.WALLCLIMB && !(MyRoomsIntMain.pInstance && MyRoomsIntMain.pInstance.pIsBuildMode))
            {
                var data = ___mCamData[(int)___mCurLayer];
                var controller = data.lookAt?.GetComponent<AvAvatarController>();
                if (controller && !(controller.pActiveFishingZone && controller.pActiveFishingZone._CurrentFishingRod))
                {
                    if (!IsFPSMode)
                    {
                        IsFPSMode = true;
                        x = data.lookAt.eulerAngles.y + data.offset.x;
                        y = data.offset.y;
                        Main.CheckLockMouse();
                    }
                    if (LockMouse == (Input.GetKey(Main.HoldToUnlockMouse) || !AvAvatar.pInputEnabled || AvAvatar.pState >= AvAvatarState.PAUSED || AvAvatar.pState == AvAvatarState.NONE || (controller.IsFlyingOrGliding() && Main.FlightMode == FlightMode.FlightControlsCamera)))
                        LockMouse = !LockMouse;
                    var xDelta = LockMouse ? Input.GetAxis("Mouse X") + KAInput.GetAxis("CameraRotationX") : 0;
                    var yDelta = LockMouse ? Input.GetAxis("Mouse Y") + KAInput.GetAxis("CameraRotationY") : 0;
                    if (controller.IsFlyingOrGliding())
                    {
                        var a = Vector3.SignedAngle(controller.transform.forward, Vector3.forward, Vector3.up);
                        if (Main.FlightMode == FlightMode.FlightMovesCamera && Math.Abs(previousPet - a) < 10)
                            xDelta += (previousPet - a) * 0.5f;
                        previousPet = a;
                    }
                    x = (x + xDelta * Main.LookSensitivity) % 360;
                    y = Mathf.Clamp(y + -yDelta * Main.LookSensitivity, -90, 90);
                    __instance.camera.transform.rotation =
                        controller.pPlayerMounted && SanctuaryManager.pCurPetInstance && controller.IsFlyingOrGliding() && Main.FlightMode == FlightMode.FlightControlsCamera
                        ? SanctuaryManager.pCurPetInstance.GetSeatBone().rotation
                        : (Quaternion.Euler(0, x, 0) * Quaternion.Euler(y, 0, 0));
                    __instance.camera.transform.position = 
                        controller.pPlayerMounted && SanctuaryManager.pCurPetInstance
                        ? SanctuaryManager.pCurPetInstance.GetCameraPos(0.3f)
                        : (data.lookAt.position + Vector3.up * 1.3f);
                    if (!Input.GetKey(KeyCode.LeftAlt) && !(controller.IsFlyingOrGliding() && Main.FlightMode != FlightMode.CameraControlsFlight))
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
                            float rz = Math.Max(Math.Min(change / turnRate, 1), -1) * controller.pFlyingData._MaxRoll;
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
                    if (LockMouse)
                    {
                        if (Input.GetMouseButtonUp(1))
                            Object.FindObjectOfType<UiAvatarCSM>()?.OpenCSM();
                        else if (Input.GetKeyDown(Main.GeneralInteract))
                            for (int i = Patch_ContextUI.Enabled.Count - 1; i >= 0; i--)
                                if (Patch_ContextUI.Enabled[i])
                                {
                                    Patch_ContextUI.Enabled[i].pUI.OnClick(Patch_ContextUI.Enabled[i]);
                                    break;
                                }
                    }
                    data.offset.y = y;
                    data.offset.x = 0;
                    return false;
                }
            }
            if (IsFPSMode)
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
                if (__instance.pSubState != AvAvatarSubState.FLYING && __instance.pSubState != AvAvatarSubState.GLIDING && __instance.pSubState != AvAvatarSubState.SKATING)
                {
                    speed *= (__instance.pCurrentStateData._MaxForwardSpeed + __instance.pCurrentStateData._MaxBackwardSpeed) / 2;
                    if (__instance.pSubState == AvAvatarSubState.NORMAL || __instance.pSubState == AvAvatarSubState.DIVESUIT)
                    {
                        if (__instance.pPlayerCarrying)
                            speed *=  0.75f;
                        if (SanctuaryManager.pCurPetInstance != null && __instance.pPlayerMounted)
                            speed *= SanctuaryManager.pCurPetInstance.GetMountedSpeedModifer();
                    }
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

    [HarmonyPatch(typeof(KAWidget))]
    static class Patch_ContextUI
    {
        public static List<KAButton> Enabled = new List<KAButton>();
        [HarmonyPatch("UpdateVisibility")]
        [HarmonyPrefix]
        static void UpdateVisibility(KAWidget __instance, bool inVisible, bool ___mParentVisible, bool ____Visible)
        {

            if (__instance is KAButton b)
            {
                var ind = Enabled.IndexOf(b);
                if (inVisible && ___mParentVisible && ____Visible)
                {
                    if (ind >= 0)
                        return;
                    var p = __instance.transform.position;
                    if (p.x == 0 && p.y < 0)
                        Enabled.Add(b);
                }
                else if (ind >= 0)
                    Enabled.RemoveAt(ind);
                //Debug.Log($"Set [{__instance}] visibility to {inVisible && ___mParentVisible && ____Visible}");
            }
        }
        [HarmonyPatch("OnDestroy")]
        [HarmonyPrefix]
        static void OnDestroy(KAWidget __instance)
        {
            if (__instance is KAButton b)
                Enabled.Remove(b);
                //Debug.Log($"Set [{__instance}] visibility to False");
        }
    }
}