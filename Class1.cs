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
using System.Diagnostics;
using System.IO;
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

            CableStateManager.Init(modEntry.Path);

            try
            {
                var harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());

                var updateMethod = typeof(DV.MultipleUnit.MultipleUnitModule).GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (updateMethod != null)
                {
                    harmony.Patch(updateMethod, prefix: new HarmonyMethod(typeof(MUModule_Safety).GetMethod(nameof(MUModule_Safety.Update_Prefix))));
                }

                var finalizerMethod = new HarmonyMethod(typeof(MUModule_Universal_Finalizer).GetMethod(nameof(MUModule_Universal_Finalizer.Finalizer)));
                foreach (var method in typeof(DV.MultipleUnit.MultipleUnitModule).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!method.IsAbstract && !method.IsGenericMethod)
                    {
                        try { harmony.Patch(method, finalizer: finalizerMethod); } catch { }
                    }
                }
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
            GUILayout.Label("<b>Enable remote control (MU) for vehicles:</b>");
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();

            // DM
            GUILayout.BeginVertical(GUILayout.Width(160));
            GUILayout.Label("<b>Diesel & Mechanical</b>");
            GUILayout.Space(5);
            settings.DM3 = GUILayout.Toggle(settings.DM3, " DM3");
            settings.DM1U = GUILayout.Toggle(settings.DM1U, " DM1U");
            GUILayout.EndVertical();
            GUILayout.Space(20);

            // Steam
            GUILayout.BeginVertical(GUILayout.Width(160));
            GUILayout.Label("<b>Steam Locomotives</b>");
            GUILayout.Space(5);
            settings.S282 = GUILayout.Toggle(settings.S282, " S282 (+ Tender)");
            settings.S060 = GUILayout.Toggle(settings.S060, " S060");
            GUILayout.EndVertical();
            GUILayout.Space(20);

            // Electric & Custom
            GUILayout.BeginVertical(GUILayout.Width(170));
            GUILayout.Label("<b>Electric & Custom</b>");
            GUILayout.Space(5);
            settings.BE2 = GUILayout.Toggle(settings.BE2, " BE2 (Battery)");
            settings.MOD_LOCO = GUILayout.Toggle(settings.MOD_LOCO, " Custom Modded Locos");
            GUILayout.EndVertical();
            GUILayout.Space(20);

            // Rolling Stock / Utility
            GUILayout.BeginVertical(GUILayout.Width(180));
            GUILayout.Label("<b>Rolling Stock & Utility</b>");
            GUILayout.Space(5);
            settings.Caboose = GUILayout.Toggle(settings.Caboose, " Caboose");
            settings.UtilityFlatcar = GUILayout.Toggle(settings.UtilityFlatcar, " Utility Short Flatcar");
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

                if (__instance.muModule != null && __instance.GetComponent<DummyMUFlag>() == null)
                {
                    return;
                }

                string carTypeName = __instance.carType.ToString().ToLower();

                if (carTypeName.Contains("handcar")) return;

                if (carTypeName == "locoshunter" ||
                    carTypeName == "locodiesel" ||
                    carTypeName.Contains("de2") ||
                    carTypeName.Contains("de6") ||
                    carTypeName.Contains("dh4") ||
                    carTypeName.Contains("slug"))
                {
                    return;
                }

                bool activateRemoteMU = false;
                if (__instance.carType == TrainCarType.LocoDM3 && Main.settings.DM3)
                {
                    activateRemoteMU = true;
                    if (__instance.GetComponent<DM3GearboxSync>() == null)
                    {
                        __instance.gameObject.AddComponent<DM3GearboxSync>();
                    }
                }
                else if ((__instance.carType == TrainCarType.LocoSteamHeavy || carTypeName.Contains("tender")) && Main.settings.S282)
                {
                    activateRemoteMU = true;
                }
                else if (__instance.carType == TrainCarType.LocoS060 && Main.settings.S060)
                {
                    activateRemoteMU = true;
                }
                else if (__instance.carType == TrainCarType.LocoMicroshunter && Main.settings.BE2)
                {
                    activateRemoteMU = true;
                }
                else if (__instance.carType == TrainCarType.LocoDM1U && Main.settings.DM1U)
                {
                    activateRemoteMU = true;
                    if (__instance.GetComponent<DM3GearboxSync>() == null)
                    {
                        __instance.gameObject.AddComponent<DM3GearboxSync>();
                    }
                }
                else if (carTypeName.Contains("caboose") && Main.settings.Caboose)
                {
                    activateRemoteMU = true;
                }
                else if ((__instance.carType == TrainCarType.FlatbedShort || (carTypeName.Contains("flat") && carTypeName.Contains("short")) || carTypeName.Contains("utility")) && Main.settings.UtilityFlatcar)
                {
                    activateRemoteMU = true;
                }
                else if (Main.settings.MOD_LOCO && __instance.IsLoco)
                {
                    activateRemoteMU = true;
                }

                if (activateRemoteMU)
                {
                    if (__instance.GetComponent<DummyMUFlag>() == null)
                    {
                        __instance.gameObject.AddComponent<DummyMUFlag>();
                    }

                    if (__instance.GetComponent<UniversalCableGenerator>() == null)
                    {
                        __instance.gameObject.AddComponent<UniversalCableGenerator>();
                    }
                }
            }
        }
    }

    public static class CableStateManager
    {
        private static string saveFilePath;
        private static readonly HashSet<string> activeConnections = new HashSet<string>();
        private static readonly Dictionary<DV.MultipleUnit.MultipleUnitCable, (TrainCar car, string side)> cableToInfo = new Dictionary<DV.MultipleUnit.MultipleUnitCable, (TrainCar car, string side)>();
        public static bool isQuitting = false;

        public static string SafeGetId(TrainCar car)
        {
            if (car == null) return null;
            try
            {
                if (car.logicCar == null) return null;
                return car.ID;
            }
            catch
            {
                return null;
            }
        }

        public static void Init(string modPath)
        {
            saveFilePath = Path.Combine(modPath, "mu_connections.save");
            Application.quitting += () => { isQuitting = true; };
            Load();
        }

        public static void Load()
        {
            try
            {
                activeConnections.Clear();
                if (File.Exists(saveFilePath))
                {
                    foreach (var line in File.ReadAllLines(saveFilePath))
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && trimmed.Contains("|"))
                        {
                            activeConnections.Add(trimmed);
                        }
                    }
                    Main.DebugLog($"[Universal Cable] Loaded {activeConnections.Count} saved cable connections from file.");
                }
            }
            catch (Exception ex)
            {
                Main.DebugLog($"[Universal Cable] Error loading saved connections: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(saveFilePath, activeConnections.ToArray());
                Main.DebugLog($"[Universal Cable] Saved {activeConnections.Count} active connections to disk.");
            }
            catch (Exception ex)
            {
                Main.DebugLog($"[Universal Cable] Error saving connections: {ex.Message}");
            }
        }

        public static string GetConnectionKey(TrainCar carA, string sideA, TrainCar carB, string sideB)
        {
            string idA = SafeGetId(carA);
            string idB = SafeGetId(carB);
            if (string.IsNullOrEmpty(idA) || string.IsNullOrEmpty(idB)) return null;

            string partA = $"{idA}_{sideA}";
            string partB = $"{idB}_{sideB}";
            return string.Compare(partA, partB, StringComparison.Ordinal) < 0
                ? $"{partA}|{partB}"
                : $"{partB}|{partA}";
        }

        public static bool IsConnectionSaved(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return activeConnections.Contains(key);
        }

        public static void RegisterCable(DV.MultipleUnit.MultipleUnitCable cable, TrainCar car, string side)
        {
            if (cable != null && car != null)
            {
                cableToInfo[cable] = (car, side);
            }
        }

        public static (TrainCar car, string side) GetCarAndSide(DV.MultipleUnit.MultipleUnitCable cable)
        {
            if (cable == null) return (null, null);
            if (cableToInfo.TryGetValue(cable, out var info)) return info;

            var allAdapters = UnityEngine.Object.FindObjectsOfType<CouplingHoseMultipleUnitAdapter>();
            foreach (var ad in allAdapters)
            {
                if (ad == null) continue;
                if (ad.muCable == cable)
                {
                    var car = ad.GetComponentInParent<TrainCar>();
                    string side = UniversalMUCableInstaller.GetAdapterSide(car, ad);
                    cableToInfo[cable] = (car, side);
                    return (car, side);
                }
            }
            return (null, null);
        }

        public static void OnCableConnected(DV.MultipleUnit.MultipleUnitCable a, DV.MultipleUnit.MultipleUnitCable b)
        {
            var infoA = GetCarAndSide(a);
            var infoB = GetCarAndSide(b);

            if (infoA.car != null && infoB.car != null)
            {
                string key = GetConnectionKey(infoA.car, infoA.side, infoB.car, infoB.side);
                if (!string.IsNullOrEmpty(key) && activeConnections.Add(key))
                {
                    Main.DebugLog($"[Universal Cable] Connection saved to disk: {key}");
                    Save();
                }
            }
        }

        public static void OnCableDisconnected(DV.MultipleUnit.MultipleUnitCable cable)
        {
            if (isQuitting) return;
            if (UniversalMUCableInstaller.isAutoReconnecting) return;

            var stack = new StackTrace(false);
            for (int i = 0; i < stack.FrameCount; i++)
            {
                var method = stack.GetFrame(i)?.GetMethod();
                if (method == null) continue;

                string mName = method.Name;
                string tName = method.DeclaringType != null ? method.DeclaringType.Name : "";

                if (mName == "OnDestroy" || mName == "OnDisable" || mName == "OnApplicationQuit" ||
                    mName == "AboutToBeDestroyed" || mName == "OnCarInteriorAboutToBeDestroyed" ||
                    tName.Contains("SceneSwitcher") || tName.Contains("Unload") || tName.Contains("Quit"))
                {
                    return;
                }
            }

            var (car, side) = GetCarAndSide(cable);
            if (car == null || car.logicCar == null) return;

            string carId = SafeGetId(car);
            if (!string.IsNullOrEmpty(carId) && !string.IsNullOrEmpty(side))
            {
                string searchKey = $"{carId}_{side}";
                int removed = activeConnections.RemoveWhere(k => k.StartsWith(searchKey + "|") || k.EndsWith("|" + searchKey));
                if (removed > 0)
                {
                    Main.DebugLog($"[Universal Cable] Connection manually removed from disk: {searchKey}");
                    Save();
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
            Main.DebugLog("[GearboxSync] Script attached to: " + (trainCar != null ? CableStateManager.SafeGetId(trainCar) : "Unknown"));
        }

        private static TrainCar GetCarFromCable(DV.MultipleUnit.MultipleUnitCable cable)
        {
            if (cable == null) return null;
            if (cable.muModule != null && cable.muModule.train != null)
                return cable.muModule.train;

            var (registeredCar, _) = CableStateManager.GetCarAndSide(cable);
            if (registeredCar != null) return registeredCar;

            var adapter = cable.HoseAdapter;
            if (adapter != null)
            {
                var car = adapter.GetComponentInParent<TrainCar>();
                if (car != null) return car;
            }

            return null;
        }

        private static List<TrainCar> GetMUCabledNeighbors(TrainCar car)
        {
            var neighbors = new List<TrainCar>();
            if (car == null) return neighbors;

            var adapters = car.GetComponentsInChildren<CouplingHoseMultipleUnitAdapter>(true);
            for (int i = 0; i < adapters.Length; i++)
            {
                var adapter = adapters[i];
                if (adapter == null || adapter.muCable == null) continue;

                var otherCable = adapter.muCable.connectedTo;
                if (otherCable != null)
                {
                    var otherCar = GetCarFromCable(otherCable);
                    if (otherCar != null && otherCar != car && !neighbors.Contains(otherCar))
                    {
                        neighbors.Add(otherCar);
                    }
                }
            }

            return neighbors;
        }
        private List<TrainCar> GetCabledSyncChain(TrainCar startCar)
        {
            var visited = new HashSet<TrainCar>();
            var queue = new Queue<TrainCar>();

            queue.Enqueue(startCar);
            visited.Add(startCar);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var neighbor in GetMUCabledNeighbors(current))
                {
                    if (neighbor != null && visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return visited
                .Where(c => c != null && c.logicCar != null && c.GetComponent<DM3GearboxSync>() != null)
                .OrderBy(c => CableStateManager.SafeGetId(c))
                .ToList();
        }

        void Update()
        {
            if (trainCar == null) trainCar = GetComponent<TrainCar>();
            if (trainCar == null || trainCar.logicCar == null) return;

            if (simController == null)
            {
                foreach (var comp in GetComponentsInChildren<UnityEngine.Component>(true))
                {
                    if (comp != null && comp.GetType() != typeof(DM3GearboxSync) && comp.GetType().Name.Contains("SimController"))
                    {
                        simController = comp;
                        Main.DebugLog("[GearboxSync] SimController linked for " + CableStateManager.SafeGetId(trainCar));
                        break;
                    }
                }
            }

            if (simController == null) return;

            var chain = GetCabledSyncChain(trainCar);
            if (chain.Count <= 1)
            {
                if (cachedMUConnections != 0)
                {
                    fastPairs.Clear();
                    cachedMUConnections = 0;
                }
                return;
            }

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
                    if (slaveCar == masterCar || slaveCar == null || slaveCar.logicCar == null) continue;

                    var slaveSync = slaveCar.GetComponent<DM3GearboxSync>();
                    if (slaveSync == null || slaveSync.simController == null) continue;

                    BuildFastCache(simController, slaveSync.simController, 0);
                }

                Main.DebugLog($"[GearboxSync] Sync cache rebuilt for {CableStateManager.SafeGetId(masterCar)}. Synchronizing {currentMUConnections} unit(s). Cached {fastPairs.Count} fields.");
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
        public bool Caboose = true;
        public bool UtilityFlatcar = true;

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
        public static bool Prefix(DV.MultipleUnit.MultipleUnitCable __instance, DV.MultipleUnit.MultipleUnitCable other, bool playAudio)
        {
            if (__instance == null || other == null || other == __instance) return false;

            if (__instance.connectedTo == other && other.connectedTo == __instance) return false;

            if (__instance.connectedTo != null && __instance.connectedTo != other)
            {
                __instance.connectedTo = null;
            }
            if (other.connectedTo != null && other.connectedTo != __instance)
            {
                other.connectedTo = null;
            }

            __instance.connectedTo = other;
            other.connectedTo = __instance;

            BaseControlsOverrider overrider1 = __instance.muModule != null ? __instance.muModule.controlsOverrider : null;
            BaseControlsOverrider overrider2 = other.muModule != null ? other.muModule.controlsOverrider : null;

            if (overrider1 != null)
            {
                try
                {
                    if (overrider1.Throttle != null) overrider1.Throttle.Set(0f);
                    if (overrider1.DynamicBrake != null) overrider1.DynamicBrake.Set(0f);
                    if (overrider1.Reverser != null) overrider1.Reverser.Set(0.5f);
                    if (overrider1.Sander != null) overrider1.Sander.Set(0f);
                    if (overrider1.HeadlightsFront != null) overrider1.HeadlightsFront.Set(0.4f);
                    if (overrider1.HeadlightsRear != null) overrider1.HeadlightsRear.Set(0.4f);
                }
                catch { }
            }

            if (overrider2 != null)
            {
                try
                {
                    if (overrider2.Throttle != null) overrider2.Throttle.Set(0f);
                    if (overrider2.DynamicBrake != null) overrider2.DynamicBrake.Set(0f);
                    if (overrider2.Reverser != null) overrider2.Reverser.Set(0.5f);
                    if (overrider2.Sander != null) overrider2.Sander.Set(0f);
                    if (overrider2.HeadlightsFront != null) overrider2.HeadlightsFront.Set(0.4f);
                    if (overrider2.HeadlightsRear != null) overrider2.HeadlightsRear.Set(0.4f);
                }
                catch { }
            }

            if (overrider1 != null && overrider2 != null)
            {
                try
                {
                    float b1 = overrider1.Brake != null ? overrider1.Brake.Value : 0f;
                    float b2 = overrider2.Brake != null ? overrider2.Brake.Value : 0f;
                    if (b2 > b1) overrider1.Brake?.Set(b2);
                    else overrider2.Brake?.Set(b1);

                    float ind1 = overrider1.IndependentBrake != null ? overrider1.IndependentBrake.Value : 0f;
                    float ind2 = overrider2.IndependentBrake != null ? overrider2.IndependentBrake.Value : 0f;
                    if (ind2 > ind1) overrider1.IndependentBrake?.Set(ind2);
                    else overrider2.IndependentBrake?.Set(ind1);
                }
                catch { }
            }

            try
            {
                var anyField = typeof(DV.MultipleUnit.MultipleUnitCable).GetField("AnyConnectionChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var anyDel = anyField?.GetValue(null) as MulticastDelegate;
                if (anyDel != null)
                {
                    anyDel.DynamicInvoke(true, __instance, other);
                }
            }
            catch { }

            InvokeCableConnectionChanged(__instance, true, playAudio);
            InvokeCableConnectionChanged(other, true, playAudio);

            try
            {
                CableStateManager.OnCableConnected(__instance, other);
            }
            catch { }

            return false;
        }

        private static void InvokeCableConnectionChanged(DV.MultipleUnit.MultipleUnitCable cable, bool connected, bool playAudio)
        {
            if (cable == null) return;
            try
            {
                var field = typeof(DV.MultipleUnit.MultipleUnitCable).GetField("ConnectionChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var action = field?.GetValue(cable) as Action<bool, bool>;
                action?.Invoke(connected, playAudio);
            }
            catch (Exception ex)
            {
                Main.DebugLog($"[Universal Cable] ConnectionChanged invoke note: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(DV.MultipleUnit.MultipleUnitCable), "Disconnect", new System.Type[] { typeof(bool) })]
    public class MUCable_Disconnect_Patch
    {
        public static bool Prefix(DV.MultipleUnit.MultipleUnitCable __instance, bool playAudio)
        {
            if (__instance == null || __instance.connectedTo == null)
            {
                return false;
            }

            var other = __instance.connectedTo;

            __instance.connectedTo = null;
            other.connectedTo = null;

            try
            {
                var anyField = typeof(DV.MultipleUnit.MultipleUnitCable).GetField("AnyConnectionChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var anyDel = anyField?.GetValue(null) as MulticastDelegate;
                if (anyDel != null)
                {
                    anyDel.DynamicInvoke(false, __instance, other);
                }
            }
            catch { }

            InvokeCableConnectionChanged(__instance, false, playAudio);
            InvokeCableConnectionChanged(other, false, playAudio);

            try
            {
                CableStateManager.OnCableDisconnected(__instance);
            }
            catch { }

            return false;
        }

        private static void InvokeCableConnectionChanged(DV.MultipleUnit.MultipleUnitCable cable, bool connected, bool playAudio)
        {
            if (cable == null) return;
            try
            {
                var field = typeof(DV.MultipleUnit.MultipleUnitCable).GetField("ConnectionChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var action = field?.GetValue(cable) as Action<bool, bool>;
                action?.Invoke(connected, playAudio);
            }
            catch { }
        }

        public static Exception Finalizer(Exception __exception)
        {
            return null;
        }
    }

    public static class MUModule_Safety
    {
        public static bool Update_Prefix(DV.MultipleUnit.MultipleUnitModule __instance)
        {
            if (__instance == null) return false;
            var car = __instance.train ?? __instance.GetComponent<TrainCar>() ?? __instance.GetComponentInParent<TrainCar>();
            if (car != null && !car.IsLoco && car.GetComponent<DummyMUFlag>() != null)
            {
                return false;
            }
            return true;
        }
    }

    public static class MUModule_Universal_Finalizer
    {
        public static Exception Finalizer(Exception __exception)
        {
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

                if (trainCar.carType == TrainCarType.LocoSteamHeavy)
                {
                    StartCoroutine(UniversalMUCableInstaller.MonitorS282TenderConnectionCoroutine(trainCar));
                }
            }
        }
    }

    public static class UniversalMUCableInstaller
    {
        private static GameObject muCablePrefab;
        public static bool isAutoReconnecting = false;

        public static System.Collections.IEnumerator TryInstallCablesCoroutine(TrainCar car)
        {
            if (car == null) yield break;

            yield return new UnityEngine.WaitForSeconds(1.0f);

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

                if (frontExists && rearExists)
                {
                    car.StartCoroutine(AutoReconnectAfterLoad(car));
                    yield break;
                }

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
                    if (adapter == null) continue;
                    if (adapter.GetComponent<DummyMUFlag>() != null) continue;

                    var parentCar = adapter.GetComponentInParent<TrainCar>();
                    if (parentCar != null && parentCar.GetComponent<DummyMUFlag>() != null) continue;

                    if (adapter.gameObject != null && adapter.GetComponentInChildren<Renderer>(true) != null && !adapter.IsConnected)
                    {
                        muCablePrefab = UnityEngine.Object.Instantiate(adapter.gameObject);
                        muCablePrefab.name = "UniversalMUCable_Prefab";
                        muCablePrefab.SetActive(false);
                        UnityEngine.Object.DontDestroyOnLoad(muCablePrefab);

                        var ad = muCablePrefab.GetComponent<CouplingHoseMultipleUnitAdapter>();
                        if (ad != null) ad.muCable = null;

                        Main.DebugLog("[Universal Cable] SUCCESS: Cached native MU cable prefab!");
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Main.DebugLog($"[Universal Cable] Error caching prefab: {ex.Message}");
            }
        }

        private static void SetModuleMember(DV.MultipleUnit.MultipleUnitModule module, string name, object value)
        {
            if (module == null) return;
            try
            {
                var f = typeof(DV.MultipleUnit.MultipleUnitModule).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { f.SetValue(module, value); return; }
                var p = typeof(DV.MultipleUnit.MultipleUnitModule).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanWrite) { p.SetValue(module, value, null); }
            }
            catch { }
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
                case TrainCarType.LocoS060:
                case TrainCarType.FlatbedShort:
                    frontOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    rearOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    break;
                case TrainCarType.LocoMicroshunter:
                    frontOffset = new Vector3(0.4f, -0.01f, -0.45f);
                    rearOffset = new Vector3(0.4f, -0.01f, -0.45f);
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
                    else if (typeName.Contains("caboose"))
                    {
                        frontOffset = new Vector3(0.4f, 0.05f, -0.45f);
                        rearOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    }
                    else if (typeName.Contains("flat") || typeName.Contains("utility"))
                    {
                        frontOffset = new Vector3(0.4f, 0.05f, -0.45f);
                        rearOffset = new Vector3(0.4f, 0.05f, -0.45f);
                    }
                    break;
            }

            DV.MultipleUnit.MultipleUnitModule muModule = car.GetComponent<DV.MultipleUnit.MultipleUnitModule>();
            if (muModule == null)
            {
                muModule = car.gameObject.AddComponent<DV.MultipleUnit.MultipleUnitModule>();
            }
            car.muModule = muModule;

            SetModuleMember(muModule, "train", car);

            var frontCable = new DV.MultipleUnit.MultipleUnitCable(muModule, true);
            var rearCable = new DV.MultipleUnit.MultipleUnitCable(muModule, false);

            CouplingHoseMultipleUnitAdapter frontAdapter = null;
            CouplingHoseMultipleUnitAdapter rearAdapter = null;

            GameObject frontObj = null;
            GameObject rearObj = null;

            if (car.frontCoupler != null)
            {
                Transform existing = car.frontCoupler.transform.Find("MUCable_Front");
                frontObj = existing != null ? existing.gameObject : UnityEngine.Object.Instantiate(muCablePrefab, car.frontCoupler.transform);
                frontObj.name = "MUCable_Front";
                frontObj.transform.localPosition = frontOffset;
                frontObj.transform.localRotation = Quaternion.Euler(frontRotation);

                frontAdapter = frontObj.GetComponent<CouplingHoseMultipleUnitAdapter>();
                if (frontAdapter != null)
                {
                    frontAdapter.muCable = frontCable;
                }
                CableStateManager.RegisterCable(frontCable, car, "front");
            }

            if (car.rearCoupler != null)
            {
                Transform existing = car.rearCoupler.transform.Find("MUCable_Rear");
                rearObj = existing != null ? existing.gameObject : UnityEngine.Object.Instantiate(muCablePrefab, car.rearCoupler.transform);
                rearObj.name = "MUCable_Rear";
                rearObj.transform.localPosition = rearOffset;
                rearObj.transform.localRotation = Quaternion.Euler(rearRotation);

                rearAdapter = rearObj.GetComponent<CouplingHoseMultipleUnitAdapter>();
                if (rearAdapter != null)
                {
                    rearAdapter.muCable = rearCable;
                }
                CableStateManager.RegisterCable(rearCable, car, "rear");
            }

            SetModuleMember(muModule, "frontCableAdapter", frontAdapter);
            SetModuleMember(muModule, "rearCableAdapter", rearAdapter);
            SetModuleMember(muModule, "frontCable", frontCable);
            SetModuleMember(muModule, "rearCable", rearCable);

            if (frontObj != null) frontObj.SetActive(true);
            if (rearObj != null) rearObj.SetActive(true);

            if (car.IsLoco)
            {
                try
                {
                    muModule.Initialize(car);
                    Main.DebugLog($"[Universal Cable] SUCCESS: MU Module initialized on {CableStateManager.SafeGetId(car)}");
                }
                catch (System.Exception ex)
                {
                    Main.DebugLog($"[Universal Cable] MU Module init note: {ex.Message}");
                }
            }

            car.StartCoroutine(AutoReconnectAfterLoad(car));
        }

        private static System.Collections.IEnumerator AutoReconnectAfterLoad(TrainCar car)
        {
            string carId = CableStateManager.SafeGetId(car);
            Main.DebugLog($"[Universal Cable] [{carId ?? "Unknown"}] Starting AutoReconnect coroutine...");
            yield return new UnityEngine.WaitForSeconds(2.0f);

            int attempts = 0;
            const int maxAttempts = 10;

            bool frontResolved = false;
            bool rearResolved = false;

            while (car != null && car.logicCar != null && attempts < maxAttempts)
            {
                attempts++;

                if (!frontResolved && car.frontCoupler != null)
                {
                    frontResolved = ProcessCouplerConnection(car, car.frontCoupler, "front", attempts);
                }

                if (!rearResolved && car.rearCoupler != null)
                {
                    rearResolved = ProcessCouplerConnection(car, car.rearCoupler, "rear", attempts);
                }

                if (frontResolved && rearResolved)
                {
                    Main.DebugLog($"[Universal Cable] [{CableStateManager.SafeGetId(car)}] All coupled cables resolved.");
                    yield break;
                }

                yield return new UnityEngine.WaitForSeconds(1.5f);
            }

            Main.DebugLog($"[Universal Cable] [{CableStateManager.SafeGetId(car) ?? "Unknown"}] AutoReconnect loop finished.");
        }

        public static System.Collections.IEnumerator MonitorS282TenderConnectionCoroutine(TrainCar s282Car)
        {
            yield return new UnityEngine.WaitForSeconds(5.0f);

            while (s282Car != null && s282Car.logicCar != null)
            {
                yield return new UnityEngine.WaitForSeconds(3.0f);

                if (s282Car == null || s282Car.logicCar == null || !s282Car.gameObject.activeInHierarchy) continue;

                Coupler rearCoupler = s282Car.rearCoupler;
                if (rearCoupler == null || rearCoupler.coupledTo == null) continue;

                Coupler otherCoupler = rearCoupler.coupledTo;
                TrainCar tenderCar = otherCoupler.GetComponentInParent<TrainCar>();
                if (tenderCar == null || tenderCar.logicCar == null) continue;

                string otherTypeName = tenderCar.carType.ToString().ToLower();
                string otherSide = GetCouplerSide(tenderCar, otherCoupler);

                if (otherTypeName.Contains("tender") && otherSide == "front")
                {
                    var myAdapter = FindAdapterNearCoupler(s282Car, rearCoupler);
                    var tenderAdapter = FindAdapterNearCoupler(tenderCar, otherCoupler);
                    if (myAdapter == null || tenderAdapter == null) continue;

                    var myCable = myAdapter.muCable;
                    var tenderCable = tenderAdapter.muCable;
                    if (myCable == null || tenderCable == null) continue;

                    if (myCable.connectedTo != tenderCable)
                    {
                        try
                        {
                            Main.DebugLog($"[Universal Cable] Auto-linking S282 and Tender gangway MU cables...");
                            isAutoReconnecting = true;
                            myCable.Connect(tenderCable, false);
                            Main.DebugLog($"[Universal Cable] Automatically connected S282 gangway to Tender ({CableStateManager.SafeGetId(tenderCar)}).");
                        }
                        catch (System.Exception ex)
                        {
                            Main.DebugLog($"[Universal Cable] Note during S282-Tender auto-link: {ex.Message}");
                        }
                        finally
                        {
                            isAutoReconnecting = false;
                        }
                    }
                }
            }
        }

        private static bool ProcessCouplerConnection(TrainCar car, Coupler coupler, string side, int attempt)
        {
            if (coupler == null) return true;

            Coupler otherCoupler = coupler.coupledTo;
            if (otherCoupler == null)
            {
                return attempt >= 3;
            }

            TrainCar otherCar = otherCoupler.GetComponentInParent<TrainCar>();
            if (otherCar == null || otherCar.logicCar == null) return false;

            string otherSide = GetCouplerSide(otherCar, otherCoupler);
            string connectionKey = CableStateManager.GetConnectionKey(car, side, otherCar, otherSide);

            bool isS282LocoAndTender =
                (car.carType == TrainCarType.LocoSteamHeavy && side == "rear" && otherCar.carType.ToString().ToLower().Contains("tender") && otherSide == "front") ||
                (car.carType.ToString().ToLower().Contains("tender") && side == "front" && otherCar.carType == TrainCarType.LocoSteamHeavy && otherSide == "rear");

            bool isSaved = CableStateManager.IsConnectionSaved(connectionKey);
            if (!isSaved && !isS282LocoAndTender)
            {
                if (attempt >= 2)
                {
                    Main.DebugLog($"[Universal Cable] Connection {connectionKey} NOT saved on disk. Skipping auto-reconnect.");
                    return true;
                }
                return false;
            }

            var myAdapter = FindAdapterNearCoupler(car, coupler);
            var otherAdapter = FindAdapterNearCoupler(otherCar, otherCoupler);

            if (myAdapter == null || otherAdapter == null)
            {
                Main.DebugLog($"[Universal Cable] [{CableStateManager.SafeGetId(car)}] Waiting for adapter: myAdapter={myAdapter != null}, otherAdapter={otherAdapter != null} ({side})");
                return false;
            }

            var myCable = myAdapter.muCable;
            var otherCable = otherAdapter.muCable;

            if (myCable == null || otherCable == null)
            {
                Main.DebugLog($"[Universal Cable] [{CableStateManager.SafeGetId(car)}] Waiting for cable object: myCable={myCable != null}, otherCable={otherCable != null} ({side})");
                return false;
            }

            CableStateManager.RegisterCable(myCable, car, side);
            CableStateManager.RegisterCable(otherCable, otherCar, otherSide);

            if (myCable.connectedTo == otherCable && otherCable.connectedTo == myCable)
            {
                Main.DebugLog($"[Universal Cable] [{CableStateManager.SafeGetId(car)}] Cables on {side} are already connected to {CableStateManager.SafeGetId(otherCar)}.");
                return true;
            }

            try
            {
                isAutoReconnecting = true;
                Main.DebugLog($"[Universal Cable] [{CableStateManager.SafeGetId(car)}] Restoring connection {side} -> {CableStateManager.SafeGetId(otherCar)}...");
                myCable.Connect(otherCable, false);
                Main.DebugLog($"[Universal Cable] [{CableStateManager.SafeGetId(car)}] SUCCESS: Cables connected to {CableStateManager.SafeGetId(otherCar)}!");
                return true;
            }
            catch (System.Exception ex)
            {
                Main.DebugLog($"[Universal Cable] [{CableStateManager.SafeGetId(car)}] Connect() threw: {ex.Message}");
                return false;
            }
            finally
            {
                isAutoReconnecting = false;
            }
        }

        public static string GetCouplerSide(TrainCar car, Coupler coupler)
        {
            if (car == null || coupler == null) return "front";
            return coupler == car.frontCoupler ? "front" : "rear";
        }

        public static string GetAdapterSide(TrainCar car, CouplingHoseMultipleUnitAdapter adapter)
        {
            if (car == null || adapter == null) return "front";
            if (car.frontCoupler != null && adapter.transform.IsChildOf(car.frontCoupler.transform)) return "front";
            if (car.rearCoupler != null && adapter.transform.IsChildOf(car.rearCoupler.transform)) return "rear";

            if (car.frontCoupler != null && car.rearCoupler != null)
            {
                float distFront = Vector3.Distance(adapter.transform.position, car.frontCoupler.transform.position);
                float distRear = Vector3.Distance(adapter.transform.position, car.rearCoupler.transform.position);
                return distFront <= distRear ? "front" : "rear";
            }
            return "front";
        }

        private static CouplingHoseMultipleUnitAdapter FindAdapterNearCoupler(TrainCar car, Coupler coupler)
        {
            if (car == null || coupler == null) return null;

            var adapter = coupler.GetComponentInChildren<CouplingHoseMultipleUnitAdapter>(true);
            if (adapter != null) return adapter;

            var allAdapters = car.GetComponentsInChildren<CouplingHoseMultipleUnitAdapter>(true);
            CouplingHoseMultipleUnitAdapter closest = null;
            float minDistance = 3.0f;

            foreach (var ad in allAdapters)
            {
                if (ad == null) continue;
                float dist = Vector3.Distance(ad.transform.position, coupler.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closest = ad;
                }
            }

            return closest;
        }
    }
}