using System;
using System.CodeDom;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Rainmeter;

namespace ObsidianX.Rainmeter.Nest
{
    public static class Plugin
    {
        private static IntPtr _stringBuffer = IntPtr.Zero;

        static Plugin ()
        {
            string codeBase = Assembly.GetExecutingAssembly ().CodeBase;
            var uri = new UriBuilder (codeBase);
            string path = Uri.UnescapeDataString (uri.Path);
            string dllDirectory = Path.GetDirectoryName (path);

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                string[] tokens = args.Name.Split (',');
                return Assembly.LoadFile (Path.Combine (dllDirectory, $"{tokens[0]}.dll"));
            };
        }

        // ReSharper disable once RedundantAssignment
        [DllExport]
        public static void Initialize (ref IntPtr handle, IntPtr rainmeter)
        {
            var api = new API (rainmeter);
            IntPtr skin = api.GetSkin ();
            string parentName = api.ReadString ("Parent", null);

            IPlugin plugin;
            if (string.IsNullOrEmpty (parentName)) {
                plugin = new NestPlugin ();
            } else {
                plugin = new ReporterPlugin (NestPlugin.GetParent (skin, parentName));
            }

            handle = GCHandle.ToIntPtr (GCHandle.Alloc (plugin));
        }

        [DllExport]
        public static void Finalize (IntPtr handle)
        {
            IPlugin nest = GetPlugin (handle);
            nest?.Dispose ();

            GCHandle.FromIntPtr (handle).Free ();

            if (_stringBuffer != IntPtr.Zero) {
                Marshal.FreeHGlobal (_stringBuffer);
                _stringBuffer = IntPtr.Zero;
            }
        }

        [DllExport]
        public static void Reload (IntPtr handle, IntPtr rainmeter, ref double maxValue)
        {
            IPlugin nest = GetPlugin (handle);
            nest?.Reload (new API (rainmeter), ref maxValue);
        }

        [DllExport]
        public static double Update (IntPtr handle)
        {
            IPlugin nest = GetPlugin (handle);
            return nest?.Update () ?? 0;
        }

        [DllExport]
        public static IntPtr GetString (IntPtr handle)
        {
            IPlugin nest = GetPlugin (handle);

            if (_stringBuffer != IntPtr.Zero) {
                Marshal.FreeHGlobal (_stringBuffer);
                _stringBuffer = IntPtr.Zero;
            }

            if (nest == null) {
                return IntPtr.Zero;
            }

            string str = nest.GetString ();
            if (str != null) {
                _stringBuffer = Marshal.StringToHGlobalUni (str);
            }

            return _stringBuffer;
        }

        [DllExport]
        public static void ExecuteBang (IntPtr handle, IntPtr args)
        {
            var plugin = GetPlugin (handle);
            plugin?.ExecuteBang (Marshal.PtrToStringUni (args));
        }

        private static IPlugin GetPlugin (IntPtr handle)
        {
            return GCHandle.FromIntPtr (handle).Target as IPlugin;
        }
    }
}