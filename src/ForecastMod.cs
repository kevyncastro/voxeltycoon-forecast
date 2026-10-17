using System;
using VoxelTycoon;
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

        protected override void Initialize()
        {
            _logger.Log("Tycoon Forecast loaded.");
        }

        protected override void OnGameStarted()
        {
            _sampler = new Sampler(_history, _logger);
            _sampler.Start();
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
