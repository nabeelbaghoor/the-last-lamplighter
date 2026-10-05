namespace Lamplighter
{
    /// <summary>Scripted input for the automated playtest ("bot" command). Off in normal play.</summary>
    public static class Autopilot
    {
        public static bool Active;
        /// <summary>Input is set directly by the QA harness instead of the scripted run.</summary>
        public static bool Manual;
        public static int Dir;
        public static bool Jump;

        /// <summary>Design-pixel x positions where the bot jumps while running right.</summary>
        public static readonly float[] JumpAt =
        {
            860,                    // cottage crates
            1500, 1690, 1905,       // market crates, then over the first canal
            2880, 3130, 3382, 3650, // rooftops
            4165, 4405, 4690, 4935, // canal bridges
            5615, 5830, 6010, 6150, // bell tower
        };
    }
}
