using System.Collections.Generic;

namespace ObsidianX.Rainmeter.Nest.Data
{
    public class Thermostats
    {
        public string path { get; set; }
        public Dictionary<string, Thermostat> data { get; set; }
    }

    public class Thermostat
    {
        public bool has_leaf { get; set; }
        public double target_temperature_f { get; set; }
        public double ambient_temperature_f { get; set; }
        public string hvac_mode { get; set; }
        public string hvac_state { get; set; }
    }
}