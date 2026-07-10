using DV.MultipleUnit;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using DV.ThingTypes;
using DV_UniversalRemoteMUEneabler;
using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;
namespace DV_UniversalRemoteMUEneabler
{
    public static class Main
    {
        public static UnityModManager.ModEntry.ModLogger Logger;
        public static YourModSettings settings;
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            settings = YourModSettings.Load<YourModSettings>(modEntry);
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            try
            {
                var harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch
            (Exception ex)
            {
                Logger.Error($"Failed Harmony patching: {ex.Message}");
                return false;
            }
            return true;
        }
        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("<b>Enable remote control (MU) for locomotives:</b>");
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();

            // DE/DH/DM
            GUILayout.BeginVertical(GUILayout.Width(180));
            GUILayout.Label("<b>Diesel & Mechanical</b>");
            GUILayout.Space(5);
            settings.DM3 = GUILayout.Toggle(settings.DM3, " DM3");
            settings.DM1U = GUILayout.Toggle(settings.DM1U, " DM1U");
            GUILayout.EndVertical();
            GUILayout.Space(30);

            // Steam
            GUILayout.BeginVertical(GUILayout.Width(180));
            GUILayout.Label("<b>Steam Locomotives</b>");
            GUILayout.Space(5);
            settings.S282 = GUILayout.Toggle(settings.S282, " S282");
            settings.S060 = GUILayout.Toggle(settings.S060, " S060");
            GUILayout.EndVertical();
            GUILayout.Space(30);

            // Other / Modded
            GUILayout.BeginVertical(GUILayout.Width(200));
            GUILayout.Label("<b>Electric & Custom</b>");
            GUILayout.Space(5);
            settings.BE2 = GUILayout.Toggle(settings.BE2, " BE2 (Battery)");
            settings.MOD_LOCO = GUILayout.Toggle(settings.MOD_LOCO, " Custom Modded Locos");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }

        [HarmonyPatch(typeof(TrainCar), "Awake")]
        class TrainCar_Awake_Patch
        {
            static void Postfix(TrainCar __instance)
            {
                if (__instance == null) return;

                bool ActivateRemoteMU = false;
                string carTypeSTR = __instance.carType.ToString();
                if (__instance.carType == TrainCarType.LocoDM3 && Main.settings.DM3)
                {
                    ActivateRemoteMU = true;
                    __instance.gameObject.AddComponent<DM3GearboxSync>();
                }
                else if (__instance.carType == TrainCarType.LocoSteamHeavy && Main.settings.S282)
                {
                    ActivateRemoteMU = true;
                }
                else if (__instance.carType == TrainCarType.LocoS060 && Main.settings.S060)
                {
                    ActivateRemoteMU = true;
                }
                else if (__instance.carType == TrainCarType.LocoMicroshunter && Main.settings.BE2)
                {
                    ActivateRemoteMU = true;
                }
                else if (__instance.carType == TrainCarType.LocoDM1U && Main.settings.DM1U)
                {
                    ActivateRemoteMU = true;
                }
                else if (__instance.carType == TrainCarType.LocoSteamHeavy && Main.settings.S282)
                {
                    ActivateRemoteMU = true;
                }
                else if (Main.settings.MOD_LOCO)
                {
                    if (__instance.IsLoco)
                    {
                        ActivateRemoteMU = true;
                    }
                }
                if (ActivateRemoteMU)
                {
                    if (__instance.muModule != null) return;

                    try
                    {
                        Main.Logger.Log($"[Universal MU] Injecting MU module into: {__instance.carType}");

                        MultipleUnitModule muModule = __instance.gameObject.AddComponent<MultipleUnitModule>();
                        __instance.muModule = muModule;

                        if (__instance.frontCoupler != null)
                        {
                            muModule.frontCableAdapter = __instance.frontCoupler.gameObject.AddComponent<CouplingHoseMultipleUnitAdapter>();
                        }

                        if (__instance.rearCoupler != null)
                        {
                            muModule.rearCableAdapter = __instance.rearCoupler.gameObject.AddComponent<CouplingHoseMultipleUnitAdapter>();
                        }

                        muModule.Initialize(__instance);

                        Main.Logger.Log($"[Universal MU] Successfully initialized MU module for: {__instance.carType}");
                    }
                    catch (System.Exception ex)
                    {
                        Main.Logger.Log($"[Universal MU] ERROR INSTALLING MU for {__instance.carType}: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
        }
    }

    public class DM3GearboxSync : UnityEngine.MonoBehaviour
    {
        private TrainCar trainCar;
        private UnityEngine.Component simController;

        private static System.Type cachedPlayerManagerType = null;
        private static System.Reflection.PropertyInfo playerCarProp = null;

        private System.Collections.Generic.List<FastSyncPair> fastPairs = new System.Collections.Generic.List<FastSyncPair>();
        private int cachedCarCount = -1;
        private TrainCar lastMasterCar = null;

        private class FastSyncPair
        {
            public System.Reflection.FieldInfo field;
            public object masterObj;
            public object slaveObj;
            public string debugName;
        }

        void Start()
        {
            trainCar = GetComponent<TrainCar>();
            Main.Logger.Log("[DM3 v2.0.25] Script attached to locomotive: " + (trainCar != null ? trainCar.ID : "Unknown"));
        }

        void Update()
        {
            if (trainCar == null) trainCar = GetComponent<TrainCar>();
            if (trainCar == null) return;

            if (simController == null)
            {
                foreach (var comp in GetComponentsInChildren<UnityEngine.Component>(true))
                {
                    if (comp != null && comp.GetType() != typeof(DM3GearboxSync) && comp.GetType().Name.Contains("SimController"))
                    {
                        simController = comp;
                        Main.Logger.Log("[DM3 v2.0.25] SimController linked for " + trainCar.ID);
                        break;
                    }
                }
            }

            if (simController == null) return;

            var trainset = trainCar.trainset;
            if (trainset == null || trainset.cars == null || trainset.cars.Count <= 1) return;

            TrainCar masterCar = null;
            if (cachedPlayerManagerType == null)
            {
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name == "PlayerManager") { cachedPlayerManagerType = type; break; }
                    }
                    if (cachedPlayerManagerType != null) break;
                }
            }

            if (cachedPlayerManagerType != null && playerCarProp == null)
            {
                try
                {
                    playerCarProp = cachedPlayerManagerType.GetProperty("Car", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                 ?? cachedPlayerManagerType.GetProperty("car", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                }
                catch { }
            }

            if (playerCarProp != null)
            {
                try { masterCar = playerCarProp.GetValue(null) as TrainCar; } catch { }
            }

            if (masterCar == null || !trainset.cars.Contains(masterCar) || !masterCar.carType.ToString().Contains("DM3"))
            {
                foreach (var car in trainset.cars)
                {
                    if (car != null && car.carType.ToString().Contains("DM3")) { masterCar = car; break; }
                }
            }

            if (masterCar == null) return;
            if (trainCar != masterCar) return;

            if (trainset.cars.Count != cachedCarCount || masterCar != lastMasterCar)
            {
                cachedCarCount = trainset.cars.Count;
                lastMasterCar = masterCar;
                fastPairs.Clear();

                for (int i = 0; i < trainset.cars.Count; i++)
                {
                    var car = trainset.cars[i];
                    if (car == trainCar || car == null || !car.carType.ToString().Contains("DM3")) continue;

                    var slaveSync = car.GetComponent<DM3GearboxSync>();
                    if (slaveSync == null || slaveSync.simController == null) continue;

                    BuildFastCache(simController, slaveSync.simController, 0);
                }

                Main.Logger.Log("[DM3 v2.0.25] Sync cache rebuilt. Cached " + fastPairs.Count + " fields.");
            }

            for (int i = 0; i < fastPairs.Count; i++)
            {
                var pair = fastPairs[i];
                try
                {
                    var mVal = pair.field.GetValue(pair.masterObj);
                    var sVal = pair.field.GetValue(pair.slaveObj);

                    if (mVal != null && !mVal.Equals(sVal))
                    {
                        pair.field.SetValue(pair.slaveObj, mVal);
                    }
                }
                catch { }
            }
        }

        private void BuildFastCache(object masterObj, object slaveObj, int depth)
        {
            if (masterObj == null || slaveObj == null || depth > 3) return;

            var type = masterObj.GetType();
            if (type.IsPrimitive || type == typeof(string) || type.Name.StartsWith("UnityEngine")) return;

            try
            {
                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    string nameLower = field.Name.ToLower();

                    if (field.FieldType == typeof(int) || field.FieldType == typeof(float))
                    {
                        if (nameLower.Contains("gear") || nameLower.Contains("box") || nameLower.Contains("drive") || nameLower.Contains("clutch") || nameLower.Contains("transmission"))
                        {
                            fastPairs.Add(new FastSyncPair { field = field, masterObj = masterObj, slaveObj = slaveObj, debugName = field.Name });
                        }
                    }
                    else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType))
                    {
                        var mList = field.GetValue(masterObj) as System.Collections.IEnumerable;
                        var sList = field.GetValue(slaveObj) as System.Collections.IEnumerable;
                        if (mList != null && sList != null)
                        {
                            var mEnum = mList.GetEnumerator();
                            var sEnum = sList.GetEnumerator();

                            while (mEnum.MoveNext() && sEnum.MoveNext())
                            {
                                if (mEnum.Current == null || sEnum.Current == null) continue;

                                var idF = mEnum.Current.GetType().GetField("id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                       ?? mEnum.Current.GetType().GetField("name", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                var valF = mEnum.Current.GetType().GetField("value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                                if (idF != null && valF != null)
                                {
                                    string idVal = idF.GetValue(mEnum.Current)?.ToString() ?? "";
                                    string idValLower = idVal.ToLower();
                                    if (idValLower.Contains("gear") || idValLower.Contains("box") || idValLower.Contains("drive") || idValLower.Contains("clutch") || idValLower.Contains("transmission"))
                                    {
                                        fastPairs.Add(new FastSyncPair { field = valF, masterObj = mEnum.Current, slaveObj = sEnum.Current, debugName = idVal });
                                    }
                                }
                            }
                        }
                    }
                    else if (field.FieldType.IsClass && !field.FieldType.Name.StartsWith("System"))
                    {
                        var mSub = field.GetValue(masterObj);
                        var sSub = field.GetValue(slaveObj);
                        if (mSub != null && sSub != null)
                        {
                            BuildFastCache(mSub, sSub, depth + 1);
                        }
                    }
                }
            }
            catch { }
        }
    }
    public class YourModSettings : UnityModManager.ModSettings
    {
        public bool DM3 = true;
        public bool S282 = true;
        public bool S060 = true;
        public bool BE2 = true;
        public bool DM1U = true;
        public bool MOD_LOCO = true;
        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}