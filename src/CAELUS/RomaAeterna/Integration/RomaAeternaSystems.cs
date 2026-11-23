using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace RomaAeterna
{
    public class RomaAeternaMassBalance : PartModule, IPartMassModifier
    {
        [KSPField] public float hardwareDryMass;
        [KSPField] public Vector3 hardwareCoM;
        [KSPField] public Vector3 crewCoM;
        [KSPField] public float crewTargetMass = .09375f;
        private readonly Dictionary<string, Vector3> centroids = new Dictionary<string, Vector3>();
        private static readonly FieldInfo EditorCrewField = typeof(Part).GetField("partCrew", BindingFlags.NonPublic | BindingFlags.Instance);
        public float StockCrewMass { get; private set; }
        public float ApprovedCrewMass { get; private set; }
        public float MassCorrection { get; private set; }

        private static Vector3 Parse(string text)
        {
            string[] values = text.Split(',');
            return new Vector3(float.Parse(values[0], CultureInfo.InvariantCulture),
                float.Parse(values[1], CultureInfo.InvariantCulture), float.Parse(values[2], CultureInfo.InvariantCulture));
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            foreach (ConfigNode item in node.GetNodes("RESOURCE_CENTROID"))
                centroids[item.GetValue("name")] = Parse(item.GetValue("position"));
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            foreach (var item in centroids)
            {
                ConfigNode child = node.AddNode("RESOURCE_CENTROID");
                child.AddValue("name", item.Key);
                child.AddValue("position", item.Value.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    item.Value.y.ToString("R", CultureInfo.InvariantCulture) + "," + item.Value.z.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        private List<ProtoCrewMember> Crew()
        {
            var list = new List<ProtoCrewMember>();
            if (part == null) return list;
            if (part.protoModuleCrew != null)
                foreach (var crew in part.protoModuleCrew) if (crew != null) list.Add(crew);
            if (HighLogic.LoadedSceneIsEditor && list.Count == 0 && EditorCrewField != null)
            {
                var assigned = EditorCrewField.GetValue(part) as ProtoCrewMember[];
                if (assigned != null) foreach (var crew in assigned) if (crew != null) list.Add(crew);
            }
            return list;
        }
        public float GetModuleMass(float defaultMass, ModifierStagingSituation situation)
        {
            StockCrewMass = 0;
            var crew = Crew();
            foreach (var member in crew)
                StockCrewMass += PhysicsGlobals.KerbalCrewMass + member.InventoryMass() + member.ResourceMass();
            ApprovedCrewMass = crew.Count * crewTargetMass;

            MassCorrection = hardwareDryMass - defaultMass + ApprovedCrewMass - StockCrewMass;
            return MassCorrection;
        }
        public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (centroids.Count == 0 && part.partInfo != null && part.partInfo.partConfig != null)
            {
                foreach (ConfigNode module in part.partInfo.partConfig.GetNodes("MODULE"))
                    if (module.GetValue("name") == "RomaAeternaMassBalance")
                        foreach (ConfigNode item in module.GetNodes("RESOURCE_CENTROID"))
                            centroids[item.GetValue("name")] = Parse(item.GetValue("position"));
            }
            UpdateCentre();
            Debug.Log("[RomaAeterna] Mass balance loaded: " + part.partInfo.name + ", approved hardware " + hardwareDryMass + " t.");
        }
        private void UpdateCentre()
        {
            if (part == null || hardwareDryMass <= 0) return;
            double mass = hardwareDryMass;
            Vector3 moment = hardwareCoM * hardwareDryMass;
            foreach (PartResource resource in part.Resources)
            {
                double weight = resource.amount * resource.info.density;
                Vector3 position;
                if (!centroids.TryGetValue(resource.resourceName, out position)) position = hardwareCoM;
                moment += position * (float)weight;
                mass += weight;
            }
            float crewMass = Crew().Count * crewTargetMass;
            moment += crewCoM * crewMass;
            mass += crewMass;
            if (mass > 0) part.CoMOffset = moment / (float)mass;
        }
        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight) UpdateCentre();
        }
    }

    public class RomaAeternaCrewAccess : PartModule
    {
        [KSPField] public string hatchTransform = "RA_Airlock";
        [KSPField] public string exitTransform = "RA_Airlock";
        private Transform hatch;
        private Transform exit;
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            hatch = part.FindModelTransform(hatchTransform);
            exit = part.FindModelTransform(exitTransform);
            if (hatch) part.airlock = hatch;
            else Debug.LogError("[RomaAeterna] Missing real hatch/boarding transform on Aquila.");
        }
        private void Exit(int index)
        {
            if (!HighLogic.LoadedSceneIsFlight || !hatch || FlightEVA.fetch == null ||
                part.protoModuleCrew == null || part.protoModuleCrew.Count <= index) return;

            KerbalEVA eva = FlightEVA.fetch.spawnEVA(part.protoModuleCrew[index], part, hatch, false);
            Debug.Log("[RomaAeterna] EVA requested through approved hatch; real EVA created=" + (eva != null));
        }
        [KSPEvent(guiActive = true, guiName = "EVA: crew member 1", active = true)]
        public void EVAFirst() { Exit(0); }
        [KSPEvent(guiActive = true, guiName = "EVA: crew member 2", active = true)]
        public void EVASecond() { Exit(1); }
        public void Update()
        {
            int count = part != null && part.protoModuleCrew != null ? part.protoModuleCrew.Count : 0;
            Events["EVAFirst"].active = count > 0;
            Events["EVASecond"].active = count > 1;
            if (count > 0) Events["EVAFirst"].guiName = "EVA: " + part.protoModuleCrew[0].name;
            if (count > 1) Events["EVASecond"].guiName = "EVA: " + part.protoModuleCrew[1].name;
        }
    }
}
