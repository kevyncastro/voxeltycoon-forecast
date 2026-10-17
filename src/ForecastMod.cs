using System;
using System.Reflection;
using ForecastMod.UI;
using UnityEngine;
using VoxelTycoon;
using Logger = VoxelTycoon.Logger;
using VoxelTycoon.Modding;
using VoxelTycoon.Serialization;

namespace ForecastMod
{
    /// <summary>
    /// Entry point. The game finds this class because it extends VoxelTycoon.Modding.Mod,
    /// creates one instance, and calls the methods below at the matching moments.
    /// </summary>
    public class ForecastMod : Mod
    {
        private readonly Logger _logger = new Logger("ForecastMod");
        private readonly History _history = new History();
        private Sampler _sampler;
        private GameObject _windowObject;

        protected override void Initialize()
        {
            _logger.Log("Tycoon Forecast " + Version + " loaded.");
        }

        /// <summary>The &lt;Version&gt; from ForecastMod.csproj, e.g. "0.1.0".</summary>
        public static string Version
        {
            get
            {
                var attr = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(ForecastMod).Assembly, typeof(AssemblyInformationalVersionAttribute));
                string v = attr?.InformationalVersion ?? typeof(ForecastMod).Assembly.GetName().Version.ToString();
                int plus = v.IndexOf('+'); // drop the "+<commit hash>" the SDK may append
                return plus >= 0 ? v.Substring(0, plus) : v;
            }
        }

        protected override void OnGameStarted()
        {
            _sampler = new Sampler(_history, _logger);
            _sampler.Start();

            try
            {
                // Lives in the game scene, so it is destroyed when you leave or reload the game.
                _windowObject = new GameObject("ForecastMod.Window");
                _windowObject.AddComponent<ForecastWindow>().History = _history;
                _logger.Log("Forecast window ready. Press " + ForecastWindow.ToggleKey + " in-game to open it.");
            }
            catch (Exception e)
            {
                _logger.LogException(e);
            }
        }

        protected override void Deinitialize()
        {
            if (_windowObject != null) UnityEngine.Object.Destroy(_windowObject);
        }

        protected override void OnLateUpdate()
        {
            _sampler?.Tick();
        }

        // The game calls Read only when loading a save that already contains this mod's data.
        protected override void Read(StateBinaryReader reader)
        {
            try
            {
                _history.Read(reader);
            }
            catch (Exception e)
            {
                _logger.LogException(e);
                _history.Clear();
            }
        }

        protected override void Write(StateBinaryWriter writer)
        {
            _history.Write(writer);
        }
    }
}
