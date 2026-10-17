using VoxelTycoon.Game.UI.ModernUI;

namespace ForecastMod.UI.Native
{
    /// <summary>The "Forecast" button in the game's toolbar menu for extra tools (where mods add buttons).</summary>
    public class ForecastToolbarAction : ToolbarAction
    {
        public override bool IsActive => NativeForecastWindow.IsOpen;

        public override void Toggle() => NativeForecastWindow.Toggle();
    }
}
