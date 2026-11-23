using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RomaAeterna {
    static class FreeIvaBridge {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly object[] OpenArgs = { true }, CloseArgs = { false };
        static bool looked;
        static Type addonType, hatchType, moduleType;
        static PropertyInfo instanceProp, activeKerbalProp, shellBoundsProp;
        static FieldInfo buckledField, portNameField, nodeIdField, shellBoundsField;
        static MethodInfo setOpenMethod;

        static void Find() {
            if (looked) return;
            looked = true;
            Assembly freeIva = null;
            foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies) {
                if (a.assembly == null || a.assembly.GetName().Name != "FreeIva") continue;
                freeIva = a.assembly;
                addonType = a.assembly.GetType("FreeIva.KerbalIvaAddon");
                hatchType = a.assembly.GetType("FreeIva.FreeIvaHatch");
                moduleType = a.assembly.GetType("FreeIva.InternalModuleFreeIva");
                break;
            }
            if (freeIva == null) {
                Debug.Log("[RomaAeterna] FreeIva not installed: walk-in interior features off");
                return;
            }
            var missing = new List<string>();
            if (addonType != null) {
                instanceProp = addonType.GetProperty("Instance", Any);
                buckledField = addonType.GetField("buckled", Any);
                activeKerbalProp = addonType.GetProperty("ActiveKerbal", Any);
            } else missing.Add("KerbalIvaAddon");
            if (hatchType != null) {
                portNameField = hatchType.GetField("dockingPortNodeName", Any);
                nodeIdField = hatchType.GetField("attachNodeId", Any);
                setOpenMethod = hatchType.GetMethod("SetDesiredOpen", Any, null, new Type[] { typeof(bool) }, null);
            } else missing.Add("FreeIvaHatch");
            if (moduleType != null) {
                shellBoundsField = moduleType.GetField("ShellColliderBounds", Any);
                if (shellBoundsField == null) shellBoundsProp = moduleType.GetProperty("ShellColliderBounds", Any);
                if (shellBoundsField != null && shellBoundsField.FieldType != typeof(Bounds)) shellBoundsField = null;
                if (shellBoundsProp != null && shellBoundsProp.PropertyType != typeof(Bounds)) shellBoundsProp = null;
                if (shellBoundsField == null && shellBoundsProp == null) missing.Add("InternalModuleFreeIva.ShellColliderBounds");
            } else missing.Add("InternalModuleFreeIva");
            if (addonType != null && instanceProp == null) missing.Add("KerbalIvaAddon.Instance");
            if (addonType != null && buckledField == null) missing.Add("KerbalIvaAddon.buckled");
            if (addonType != null && activeKerbalProp == null) missing.Add("KerbalIvaAddon.ActiveKerbal");
            if (hatchType != null && setOpenMethod == null) missing.Add("FreeIvaHatch.SetDesiredOpen");
            string version = freeIva.GetName().Version.ToString();
            if (missing.Count == 0) Debug.Log("[RomaAeterna] FreeIva " + version + " found: walk-in interiors and transfer hatches active");
            else Debug.LogWarning("[RomaAeterna] FreeIva " + version + " found but missing: " + string.Join(", ", missing.ToArray()) + " - the walk-in features that need them are off");
        }

        public static bool Available { get { Find(); return addonType != null; } }

        public static bool UnbuckledKerbal(out string activeName) {
            activeName = null;
            Find();
            if (instanceProp == null) return false;
            object addon = instanceProp.GetValue(null, null);
            if (addon == null) return false;
            bool buckled = buckledField != null && (bool)buckledField.GetValue(addon);
            ProtoCrewMember active = activeKerbalProp != null ? activeKerbalProp.GetValue(addon, null) as ProtoCrewMember : null;
            if (active != null) activeName = active.name;
            return !buckled && active != null;
        }

        public static void SetHatches(InternalModel model, string portNodeName, string attachNodeId, bool open) {
            Find();
            if (model == null || hatchType == null || setOpenMethod == null) return;
            foreach (Component hatch in model.GetComponentsInChildren(hatchType, true)) {
                if (portNodeName != null && (portNameField == null || (string)portNameField.GetValue(hatch) != portNodeName)) continue;
                if (attachNodeId != null && (nodeIdField == null || (string)nodeIdField.GetValue(hatch) != attachNodeId)) continue;
                setOpenMethod.Invoke(hatch, open ? OpenArgs : CloseArgs);
            }
        }

        public static Component InteriorModule(InternalModel model) {
            Find();
            if (model == null || moduleType == null) return null;
            return model.GetComponentInChildren(moduleType, true);
        }

        public static bool HasShellBounds { get { Find(); return shellBoundsField != null || shellBoundsProp != null; } }

        public static Bounds GetShellBounds(Component module) {
            Find();
            if (module == null) return new Bounds();
            if (shellBoundsField != null) return (Bounds)shellBoundsField.GetValue(module);
            return shellBoundsProp != null ? (Bounds)shellBoundsProp.GetValue(module, null) : new Bounds();
        }

        public static void SetShellBounds(Component module, Bounds b) {
            Find();
            if (module == null) return;
            if (shellBoundsField != null) shellBoundsField.SetValue(module, b);
            else if (shellBoundsProp != null) shellBoundsProp.SetValue(module, b, null);
        }
    }
}
