// Based on Knsgf's CC0 implementation. No locomotive telemetry or background tasks.
using Newtonsoft.Json;
using UnityEngine;
using CatenaryDC.catenary_editor;
using CatenaryDC.Power;

namespace CatenaryDC.catenary;

interface power_supply : catenary_object_user { }
public partial class overhead_equipment
{
    [JsonObject]
    private class substation : catenary_object, power_supply
    {
        [JsonIgnore] private readonly SubstationState _state;
        [JsonProperty] public string map_location;
        [JsonProperty] public float supply_voltage;
        [JsonProperty] public float maximum_load;
        [JsonProperty] public bool has_inverter;

        [JsonConstructor]
        public substation(string map_location, float supply_voltage, float maximum_load, bool has_inverter,
            int x, int z, float y, Quaternion orientation) : base("GantryArrow", x, z, y, orientation)
        {
            if (string.IsNullOrWhiteSpace(map_location)) throw new System.ArgumentException("Substation ID is empty.");
            this.map_location = map_location;
            this.supply_voltage = supply_voltage;
            // Store nominal data: repeatedly exporting/reloading must not multiply it again.
            this.maximum_load = maximum_load;
            this.has_inverter = has_inverter;
            _state = new SubstationState(supply_voltage, maximum_load * editor_settings.load_limit_factor, has_inverter);
        }
        public override bool reveal()
        {
#if DEBUG
            return base.reveal();
#else
            return false;
#endif
        }
        public float wire_voltage(int wire_x, int wire_z, float wire_y, float load_current, float wire_1m_resistance)
        {
            float distance = ElectricalMath.Distance(x, z, y, wire_x, wire_z, wire_y);
            _state.AddLoad(load_current);
            return Mathf.Max(0, _state.Voltage - _state.Load * wire_1m_resistance * distance);
        }
        public void simulate_load()
        {
            if (_state.Tick(Time.fixedDeltaTime, UnityEngine.Random.value))
                Main.release_log("Substation tripped: " + map_location + ", current=" + _state.Load);
        }
    }
}
