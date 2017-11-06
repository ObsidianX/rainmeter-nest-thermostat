using System;
using Rainmeter;

namespace ObsidianX.Rainmeter.Nest
{
    public interface IPlugin : IDisposable
    {
        void Reload (API rainmeter, ref double maxValue);
        double Update ();
        string GetString ();
        void ExecuteBang (string args);
    }
}