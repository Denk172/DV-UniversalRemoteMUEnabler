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
            catch (Exception ex)
            {
                Logger.Error($"Failed Harmony patching: {ex.Message}");
                return false;
            }
            return true;
        }

        public static void DebugLog(string message)
        {
            if (settings != null && settings.enableDebugLog)
            {
                Logger.Log(message);
            }
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("<b>Enable remote control (MU) for locomotives:</b>");
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();

            // DM
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

            GUILayout.Space(20);

            // Debug
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("<b>Advanced / Debug:</b>");
            GUILayout.Space(5);
            settings.enableDebugLog = GUILayout.Toggle(settings.enableDebugLog, " Enable Debug Logging (Spams the log, use only for troubleshooting)");
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

                // Pokud mašina už má nativní vanillový MU modul, vůbec na ni nesahat
                if (__instance.muModule != null && __instance.GetComponent<DummyMUFlag>() == null)
                {
                    return;
                }

                string carTypeName = __instance.carType.ToString().ToLower();

                if (carTypeName.Contains("handcar")) return;

                // FIX: DE6 je v kódu hry 'locodiesel', DE2 je 'locoshunter'
                if (carTypeName == "locoshunter" ||
                    carTypeName == "locodiesel" ||
                    carTypeName.Contains("de2") ||
                    carTypeName.Contains("de6") ||
                    carTypeName.Contains("dh4") ||
                    carTypeName.Contains("slug"))
                {
                    return;
                }

                bool ActivateRemoteMU = false;
                if (__instance.carType == TrainCarType.LocoDM3 && Main.settings.DM3)
                {
                    ActivateRemoteMU = true;
                    if (__instance.GetComponent<DM3GearboxSync>() == null)
                    {
                        __instance.gameObject.AddComponent<DM3GearboxSync>();
                    }
                }
                else if ((__instance.carType == TrainCarType.LocoSteamHeavy || carTypeName.Contains("tender")) && Main.settings.S282)
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
                else if (Main.settings.MOD_LOCO && __instance.IsLoco)
                {
                    ActivateRemoteMU = true;
                }

                if (ActivateRemoteMU)
                {
                    if (__instance.GetComponent<UniversalCableGenerator>() == null)
                    {
                        __instance.gameObject.AddComponent<UniversalCableGenerator>();
                    }

                    if (__instance.muModule != null) return;

                    try
                    {
                        Main.DebugLog($"[Universal MU] Injecting core MU module into: {__instance.carType}");

                        if (__instance.GetComponent<DummyMUFlag>() == null)
                        {
                            __instance.gameObject.AddComponent<DummyMUFlag>();
                        }

                        var frontAdapter = __instance.gameObject.AddComponent<CouplingHoseMultipleUnitAdapter>();
                        var rearAdapter = __instance.gameObject.AddComponent<CouplingHoseMultipleUnitAdapter>();

                        frontAdapter.gameObject.AddComponent<DummyMUFlag>();
                        rearAdapter.gameObject.AddComponent<DummyMUFlag>();

                        foreach (var f in typeof(CouplingHoseMultipleUnitAdapter).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                        {
                            if (f.FieldType.Name.Contains("Coupler"))
                            {
                                f.SetValue(frontAdapter, __instance.frontCoupler);
                                f.SetValue(rearAdapter, __instance.rearCoupler);
                            }
                        }

                        DV.MultipleUnit.MultipleUnitModule muModule = __instance.gameObject.AddComponent<DV.MultipleUnit.MultipleUnitModule>();
                        __instance.muModule = muModule;

                        foreach (var field in typeof(DV.MultipleUnit.MultipleUnitModule).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                        {
                            if (field.FieldType == typeof(CouplingHoseMultipleUnitAdapter))
                            {
                                if (field.Name.ToLower().Contains("front")) field.SetValue(muModule, frontAdapter);
                                if (field.Name.ToLower().Contains("rear")) field.SetValue(muModule, rearAdapter);
                            }
                        }

                        muModule.Initialize(__instance);
                        Main.DebugLog($"[Universal MU] Successfully initialized MU module for: {__instance.carType}");
                    }
                    catch (System.Exception ex)
                    {
                        Main.Logger.Error($"[Universal MU] ERROR INSTALLING MU for {__instance.carType}: {ex.Message}\n{ex.StackTrace}");
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

        private int cachedMUConnections = -1;
        private TrainCar lastPlayerCar = null;

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
            Main.DebugLog("[DM3 v2.0.25] Script attached to locomotive: " + (trainCar != null ? trainCar.ID : "Unknown"));
        }

        private static bool CheckOneWayMUConnection(TrainCar carA, TrainCar carB)
        {
            if (carA == null || carB == null) return false;

            var comps = carA.GetComponentsInChildren<UnityEngine.Component>(true);
            foreach (var comp in comps)
            {
                if (comp == null) continue;

                string tName = comp.GetType().Name;
                if (!tName.Contains("Cable") && !tName.Contains("Adapter") && !tName.Contains("Hose") && !tName.Contains("MultipleUnit"))
                    continue;

                var type = comp.GetType();

                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var f in fields)
                {
                    object val = null;
                    try { val = f.GetValue(comp); } catch { }
                    if (val is UnityEngine.Component targetComp && targetComp != null)
                    {
                        var targetCar = targetComp.GetComponentInParent<TrainCar>();
                        if (targetCar == carB) return true;
                    }
                }

                var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var p in props)
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    object val = null;
                    try { val = p.GetValue(comp, null); } catch { }
                    if (val is UnityEngine.Component targetComp && targetComp != null)
                    {
                        var targetCar = targetComp.GetComponentInParent<TrainCar>();
                        if (targetCar == carB) return true;
                    }
                }
            }
            return false;
        }

        private static bool IsMUConnected(TrainCar carA, TrainCar carB)
        {
            if (carA == null || carB == null || carA == carB) return false;
            return CheckOneWayMUConnection(carA, carB) || CheckOneWayMUConnection(carB, carA);
        }

        private System.Collections.Generic.List<TrainCar> GetCabledDM3Chain(TrainCar startCar)
        {
            var result = new System.Collections.Generic.List<TrainCar>();
            if (startCar == null || startCar.trainset == null) return result;

            var visited = new System.Collections.Generic.HashSet<TrainCar>();
            var queue = new System.Collections.Generic.Queue<TrainCar>();

            queue.Enqueue(startCar);
            visited.Add(startCar);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add(current);

                foreach (var car in startCar.trainset.cars)
                {
                    if (car == null || visited.Contains(car) || !car.carType.ToString().Contains("DM3")) continue;

                    if (IsMUConnected(current, car))
                    {
                        visited.Add(car);
                        queue.Enqueue(car);
                    }
                }
            }
            return result;
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
                        Main.DebugLog("[DM3 v2.0.25] SimController linked for " + trainCar.ID);
                        break;
                    }
                }
            }

            if (simController == null) return;

            var trainset = trainCar.trainset;
            if (trainset == null || trainset.cars == null || trainset.cars.Count <= 1) return;

            TrainCar currentPlayerCar = null;
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
                try { currentPlayerCar = playerCarProp.GetValue(null) as TrainCar; } catch { }
            }

            if (currentPlayerCar != lastPlayerCar)
            {
                lastPlayerCar = currentPlayerCar;
                cachedMUConnections = -1;
            }

            var chain = GetCabledDM3Chain(trainCar);
            if (chain.Count <= 1)
            {
                if (cachedMUConnections != 0)
                {
                    fastPairs.Clear();
                    cachedMUConnections = 0;
                }
                return;
            }

            chain = chain.OrderBy(c => c.ID).ToList();
            TrainCar masterCar = (currentPlayerCar != null && chain.Contains(currentPlayerCar)) ? currentPlayerCar : chain[0];

            if (masterCar == null) return;
            if (trainCar != masterCar) return;

            int currentMUConnections = chain.Count - 1;

            if (chain.Count != cachedCarCount || masterCar != lastMasterCar || currentMUConnections != cachedMUConnections)
            {
                cachedCarCount = chain.Count;
                lastMasterCar = masterCar;
                cachedMUConnections = currentMUConnections;
                fastPairs.Clear();

                for (int i = 0; i < chain.Count; i++)
                {
                    var slaveCar = chain[i];
                    if (slaveCar == masterCar || slaveCar == null) continue;

                    var slaveSync = slaveCar.GetComponent<DM3GearboxSync>();
                    if (slaveSync == null || slaveSync.simController == null) continue;

                    BuildFastCache(simController, slaveSync.simController, 0);
                }

                Main.DebugLog($"[DM3 v2.0.25] Sync cache rebuilt for {masterCar.ID}. Synchronizing {currentMUConnections} cabled DM3(s). Cached {fastPairs.Count} fields.");
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

        public bool enableDebugLog = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }

    public class DummyMUFlag : UnityEngine.MonoBehaviour { }

    [HarmonyPatch(typeof(DV.MultipleUnit.MultipleUnitCable), "Connect", new System.Type[] { typeof(DV.MultipleUnit.MultipleUnitCable), typeof(bool) })]
    public class MUCable_Connect_Patch
    {
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                Main.DebugLog($"[Universal Cable] Ignored native MU connect crash (typical for steam locos): {__exception.Message}");
            }
            return null;
        }
    }

    public class UniversalCableGenerator : UnityEngine.MonoBehaviour
    {
        void Start()
        {
            var trainCar = GetComponent<TrainCar>();
            if (trainCar != null)
            {
                StartCoroutine(UniversalMUCableInstaller.TryInstallCablesCoroutine(trainCar));
            }
        }
    }

    public static class UniversalMUCableInstaller
    {
        private static GameObject muCablePrefab;

        public static System.Collections.IEnumerator TryInstallCablesCoroutine(TrainCar car)
        {
            if (car == null) yield break;

            yield return new UnityEngine.WaitForSeconds(1.0f);

            // Zkontroluje, jestli už mašina nemá originální vanillový kabel
            var existingAdapters = car.GetComponentsInChildren<CouplingHoseMultipleUnitAdapter>(true);
            if (existingAdapters.Any(a => a.GetComponent<DummyMUFlag>() == null))
            {
                yield break;
            }

            int attempts = 0;
            while (car != null && attempts < 15)
            {
                bool frontExists = car.frontCoupler != null && car.frontCoupler.transform.Find("MUCable_Front") != null;
                bool rearExists = car.rearCoupler != null && car.rearCoupler.transform.Find("MUCable_Rear") != null;

                if (frontExists && rearExists) yield break;

                if (muCablePrefab == null)
                {
                    FindAndCacheCablePrefab();
                }

                if (muCablePrefab != null)
                {
                    AttachCablesAndInitializeMU(car);
                    yield break;
                }

                attempts++;
                yield return new UnityEngine.WaitForSeconds(1.0f);
            }
        }

        private static void FindAndCacheCablePrefab()
        {
            if (muCablePrefab != null) return;

            try
            {
                var allAdapters = UnityEngine.Resources.FindObjectsOfTypeAll<CouplingHoseMultipleUnitAdapter>();
                foreach (var adapter in allAdapters)
                {
                    if (adapter != null && adapter.GetComponent<DummyMUFlag>() == null)
                    {
                        var parentCar = adapter.GetComponentInParent<TrainCar>();
                        if (parentCar != null && parentCar.GetComponent<DummyMUFlag>() != null) continue;

                        muCablePrefab = adapter.gameObject;
                        Main.DebugLog("[Universal Cable] SUCCESS: Cached native MU cable prefab!");
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.Logger.Error($"[Universal Cable] Error caching prefab: {ex.Message}");
            }
        }

        private static void AttachCablesAndInitializeMU(TrainCar car)
        {
            if (muCablePrefab == null || car == null) return;

            Vector3 frontOffset = new Vector3(0.4f, 0.05f, -0.45f);
            Vector3 rearOffset = new Vector3(0.4f, 0.05f, -0.45f);

            Vector3 frontRotation = Vector3.zero;
            Vector3 rearRotation = Vector3.zero;

            string typeName = car.carType.ToString().ToLower();

            switch (car.carType)
            {
                case TrainCarType.LocoDM3:
                    frontOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    rearOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    break;
                case TrainCarType.LocoMicroshunter:
                    frontOffset = new Vector3(0.4f, -0.01f, -0.45f);
                    rearOffset = new Vector3(0.4f, -0.01f, -0.45f);
                    break;
                case TrainCarType.LocoS060:
                    frontOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    rearOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    break;
                case TrainCarType.LocoSteamHeavy:
                    frontOffset = new Vector3(0.4f, -0.1f, -0.47f);
                    rearOffset = new Vector3(0.4f, 0.1f, -0.21f);
                    frontRotation = new Vector3(0f, 12f, 0f);
                    break;
                case TrainCarType.LocoDM1U:
                    frontOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    rearOffset = new Vector3(0.5f, 0.05f, -0.42f);
                    break;
                default:
                    if (typeName.Contains("tender"))
                    {
                        frontOffset = new Vector3(0.5f, 0.15f, -0.05f);
                        rearOffset = new Vector3(0.3f, 0.05f, -0.45f);
                    }
                    break;
            }

            CouplingHoseMultipleUnitAdapter frontAdapter = null;
            CouplingHoseMultipleUnitAdapter rearAdapter = null;

            if (car.frontCoupler != null)
            {
                Transform existing = car.frontCoupler.transform.Find("MUCable_Front");
                GameObject frontObj = existing != null ? existing.gameObject : UnityEngine.Object.Instantiate(muCablePrefab, car.frontCoupler.transform);
                frontObj.name = "MUCable_Front";
                frontObj.transform.localPosition = frontOffset;
                frontObj.transform.localRotation = Quaternion.Euler(frontRotation);
                frontObj.SetActive(true);

                frontAdapter = frontObj.GetComponent<CouplingHoseMultipleUnitAdapter>();
                FixChildReferences(frontObj, car, car.frontCoupler, frontAdapter);
            }

            if (car.rearCoupler != null)
            {
                Transform existing = car.rearCoupler.transform.Find("MUCable_Rear");
                GameObject rearObj = existing != null ? existing.gameObject : UnityEngine.Object.Instantiate(muCablePrefab, car.rearCoupler.transform);
                rearObj.name = "MUCable_Rear";
                rearObj.transform.localPosition = rearOffset;
                rearObj.transform.localRotation = Quaternion.Euler(rearRotation);
                rearObj.SetActive(true);

                rearAdapter = rearObj.GetComponent<CouplingHoseMultipleUnitAdapter>();
                FixChildReferences(rearObj, car, car.rearCoupler, rearAdapter);
            }

            DV.MultipleUnit.MultipleUnitModule muModule = car.GetComponent<DV.MultipleUnit.MultipleUnitModule>();
            if (muModule == null)
            {
                muModule = car.gameObject.AddComponent<DV.MultipleUnit.MultipleUnitModule>();
            }
            car.muModule = muModule;

            var moduleFields = typeof(DV.MultipleUnit.MultipleUnitModule).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var f in moduleFields)
            {
                if (f.FieldType == typeof(CouplingHoseMultipleUnitAdapter))
                {
                    string nameLower = f.Name.ToLower();
                    if (nameLower.Contains("front") && frontAdapter != null) f.SetValue(muModule, frontAdapter);
                    if (nameLower.Contains("rear") && rearAdapter != null) f.SetValue(muModule, rearAdapter);
                }
            }

            try
            {
                muModule.Initialize(car);
                Main.DebugLog($"[Universal Cable] SUCCESS: MU Module and Cables successfully initialized on {car.ID}");
            }
            catch (System.Exception ex)
            {
                Main.DebugLog($"[Universal Cable] MU Module init note: {ex.Message}");
            }
        }

        private static void FixChildReferences(GameObject cableObj, TrainCar car, Coupler coupler, CouplingHoseMultipleUnitAdapter adapter)
        {
            if (cableObj == null) return;

            if (adapter != null)
            {
                var adapterFields = adapter.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var f in adapterFields)
                {
                    if (f.FieldType.Name.Contains("Coupler"))
                    {
                        f.SetValue(adapter, coupler);
                    }
                }
            }

            foreach (var comp in cableObj.GetComponentsInChildren<UnityEngine.Component>(true))
            {
                if (comp == null) continue;

                var fields = comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var f in fields)
                {
                    if (f.FieldType == typeof(TrainCar))
                    {
                        f.SetValue(comp, car);
                    }
                    else if (f.FieldType.Name.Contains("Coupler"))
                    {
                        f.SetValue(comp, coupler);
                    }
                    else if (f.FieldType == typeof(CouplingHoseMultipleUnitAdapter))
                    {
                        if (adapter != null) f.SetValue(comp, adapter);
                    }
                }
            }
        }
    }
}