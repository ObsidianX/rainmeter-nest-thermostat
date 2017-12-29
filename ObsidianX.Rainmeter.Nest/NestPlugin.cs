using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Forms;
using Newtonsoft.Json;
using ObsidianX.Rainmeter.Nest.Data;
using ObsidianX.Web.EventSource;
using Rainmeter;

namespace ObsidianX.Rainmeter.Nest
{
    internal enum ThermostatModeEnum
    {
        Off = 0,
        Heat,
        Cool,
        HeatCool,
        Eco
    }

    internal class NestPlugin : IPlugin
    {
        private static readonly List<NestPlugin> _parents = new List<NestPlugin> ();
        private const string _apiHost = "developer-api.nest.com";
        private const int _apiPort = 443;

        private API _rainmeter;
        private IntPtr _skinHandle;
        private string _name;
        private string _clientId;
        private string _clientSecret;
        private string _uuid;
        private string _token;
        private string _thermostatId;
        private EventSource _eventSource;
        private HttpClient _httpClient;

        public double IsValid { get; private set; }
        public double Authenticated { get; private set; }
        public double CurrentTemperature { get; private set; }
        public double TargetTemperature { get; private set; }
        public double EcoMode { get; private set; }
        public double ThermostatMode { get; private set; }
        public double ThermostatCurrentMode { get; private set; }

        private string ConfigPath => $"{_rainmeter.ReplaceVariables ("#@#")}\\nest.token";

        public static NestPlugin GetParent (IntPtr skin, string name)
        {
            foreach (NestPlugin plugin in _parents) {
                if (plugin._skinHandle == skin && plugin._name == name) {
                    return plugin;
                }
            }

            return null;
        }

        public NestPlugin ()
        {
            CurrentTemperature = 0;
            TargetTemperature = 0;
            EcoMode = 0;
            ThermostatMode = (double) ThermostatModeEnum.Off;
            ThermostatCurrentMode = (double) ThermostatModeEnum.Off;

            _parents.Add (this);
        }

        public void Dispose ()
        {
            _parents.Remove (this);
            if (_eventSource != null && _eventSource.ReadyState == ReadyState.Open) {
                _eventSource.Close ();
            }
        }

        public void Reload (API rainmeter, ref double maxValue)
        {
            _rainmeter = rainmeter;
            _skinHandle = rainmeter.GetSkin ();
            _name = rainmeter.GetMeasureName ();

            _clientId = rainmeter.ReadString ("ClientId", null);
            _clientSecret = rainmeter.ReadString ("ClientSecret", null);

            IsValid = !string.IsNullOrEmpty (_clientId) && !string.IsNullOrEmpty (_clientSecret) ? 1 : 0;
            if (IsValid == 0) {
                return;
            }

            _httpClient = new HttpClient ();

            if (File.Exists (ConfigPath)) {
                _token = File.ReadAllText (ConfigPath).Trim ();
            }

            if (string.IsNullOrEmpty (_token)) {
                Authenticated = 0;
                ThermostatMode = (double) ThermostatModeEnum.Off;
            } else {
                SaveToken (_token);
            }
        }

        public double Update ()
        {
            if (Authenticated == 1 && (_eventSource == null || _eventSource.ReadyState == ReadyState.Closed)) {
                GetThermostats ();
            }

            return -1;
        }

        public string GetString ()
        {
            return "";
        }

        public void ExecuteBang (string args)
        {
            if (IsValid == 0) {
                return;
            }

            if (args == "Authenticate") {
                _uuid = Guid.NewGuid ().ToString ();
                API.Execute (_skinHandle, $"https://home.nest.com/login/oauth2?client_id={_clientId}&state={_uuid}");

                ShowPinDialog ();
            }
        }

        private void ShowPinDialog ()
        {
            var form = new Form {
                Width = 240,
                Height = 120,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = "Authorization PIN",
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false
            };

            var input = new TextBox {Left = 10, Top = 10, Width = 200};
            var cancel = new Button {Text = "Cancel", Left = input.Width - 40, Top = input.Bottom + 10, DialogResult = DialogResult.Cancel};
            var submit = new Button {Text = "OK", Left = cancel.Left - 10, Top = cancel.Top, DialogResult = DialogResult.OK};
            submit.Left -= submit.Width;

            submit.Click += (s, e) => {
                form.Close ();
                FetchAuthToken (input.Text.Trim ());
            };
            cancel.Click += (s, e) => form.Close ();

            form.Controls.Add (input);
            form.Controls.Add (submit);
            form.Controls.Add (cancel);
            form.AcceptButton = submit;
            form.CancelButton = cancel;

            form.ShowDialog ();
        }

        private async void FetchAuthToken (string pin)
        {
            var bodyDict = new Dictionary<string, string> ();
            bodyDict["client_id"] = _clientId;
            bodyDict["client_secret"] = _clientSecret;
            bodyDict["grant_type"] = "authorization_code";
            bodyDict["code"] = pin;
            var body = new FormUrlEncodedContent (bodyDict);

            HttpResponseMessage response = await _httpClient.PostAsync ("https://api.home.nest.com/oauth2/access_token", body);
            if (response.StatusCode != HttpStatusCode.OK) {
                // show error
                ShowPinDialog ();
                return;
            }

            string content = await response.Content.ReadAsStringAsync ();

            var auth = JsonConvert.DeserializeObject<AuthResponse> (content);
            SaveToken (auth.access_token);

            string configPath = $"{_rainmeter.ReplaceVariables ("#@#")}\\nest.token";
            File.WriteAllText (configPath, _token);
        }

        private void SaveToken (string token)
        {
            _token = token;
            Authenticated = 1;
        }

        private void GetThermostats ()
        {
            var uri = new Uri ($"https://{_apiHost}:{_apiPort}/devices/thermostats");

            _eventSource = new EventSource (uri);
            _eventSource.Headers.Add ("Authorization", $"Bearer {_token}");
            _eventSource.OnError += OnEventSourceError;
            _eventSource.OnMessage += ReadThermostats;
            _eventSource.Start ();
        }

        private void OnEventSourceError (string reason, string data, int code)
        {
            if (code == 401) {
                _rainmeter.Log (API.LogType.Warning, "Lost authentication");
                _token = null;
                if (File.Exists (ConfigPath)) {
                    File.Delete (ConfigPath);
                }

                Authenticated = 0;
                ThermostatMode = (double) ThermostatModeEnum.Off;
            } else {
                _rainmeter.Log (API.LogType.Error, $"EventSource: {reason}: {data}");
            }
        }

        private void ReadThermostats (Event evt)
        {
            if (evt.name != "put") {
                return;
            }

            var thermostats = JsonConvert.DeserializeObject<Thermostats> (evt.data);

            Thermostat thermostat = null;

            if (_thermostatId != null && thermostats.data.ContainsKey (_thermostatId)) {
                thermostat = thermostats.data[_thermostatId];
            } else if (_thermostatId == null || !thermostats.data.ContainsKey (_thermostatId)) {
                foreach (KeyValuePair<string, Thermostat> pair in thermostats.data) {
                    _thermostatId = pair.Key;
                    thermostat = pair.Value;
                    break;
                }
            }

            if (_thermostatId == null || thermostat == null) {
                return;
            }

            CurrentTemperature = thermostat.ambient_temperature_f;
            TargetTemperature = thermostat.target_temperature_f;
            EcoMode = thermostat.has_leaf ? 1 : 0;
            switch (thermostat.hvac_mode) {
                case "heat":
                    ThermostatMode = (double) ThermostatModeEnum.Heat;
                    break;
                case "cool":
                    ThermostatMode = (double) ThermostatModeEnum.Cool;
                    break;
                case "heat-cool":
                    ThermostatMode = (double) ThermostatModeEnum.HeatCool;
                    break;
                case "eco":
                    ThermostatMode = (double) ThermostatModeEnum.Eco;
                    break;
                default:
                    ThermostatMode = (double) ThermostatModeEnum.Off;
                    break;
            }

            switch (thermostat.hvac_state) {
                case "heating":
                    ThermostatCurrentMode = (double) ThermostatModeEnum.Heat;
                    break;
                case "cooling":
                    ThermostatCurrentMode = (double) ThermostatModeEnum.Cool;
                    break;
                default:
                    ThermostatCurrentMode = (double) ThermostatModeEnum.Off;
                    break;
            }
        }
    }
}