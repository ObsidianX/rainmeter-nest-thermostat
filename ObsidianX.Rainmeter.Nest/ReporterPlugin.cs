using System;
using System.Reflection;
using Rainmeter;

namespace ObsidianX.Rainmeter.Nest
{
    internal class ReporterPlugin : IPlugin
    {
        private readonly NestPlugin _nest;
        private PropertyInfo _property;
        private bool _isDouble;
        private bool _isString;

        public ReporterPlugin (NestPlugin parent)
        {
            _nest = parent;
        }

        public void Dispose ()
        {
        }

        public void Reload (API rainmeter, ref double maxValue)
        {
            if (_nest == null || _nest.IsValid == 0) {
                rainmeter.Log (API.LogType.Error, "Invalid parent");
                return;
            }

            string name = rainmeter.ReadString ("Name", null);
            if (name == null) {
                rainmeter.Log (API.LogType.Error, "Name is empty or missing");
                return;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = typeof(NestPlugin);
            try {
                _property = type.GetProperty (name, flags, null, typeof(string), new Type[] { }, null);
                if (_property == null) {
                    _property = type.GetProperty (name, flags, null, typeof(double), new Type[] { }, null);
                    if (_property == null) {
                        rainmeter.Log (API.LogType.Error, "Invalid Name provided");
                    } else {
                        _isDouble = true;
                    }
                } else {
                    _isString = true;
                }
            } catch (Exception e) {
                rainmeter.Log (API.LogType.Error, "Failed to reflect property: " + e.Message);
            }
        }

        public double Update ()
        {
            if (IsValid (_isDouble)) {
                return (double) _property.GetValue (_nest);
            }
            return 0;
        }

        public string GetString ()
        {
            if (IsValid (_isString)) {
                return (string) _property.GetValue (_nest);
            }
            return "";
        }

        public void ExecuteBang (string args)
        {
        }

        private bool IsValid (bool type)
        {
            return type && _nest != null && _nest.IsValid == 1 && _property != null;
        }
    }
}