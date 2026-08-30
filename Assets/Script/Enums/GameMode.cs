namespace FreeFlow.Enums
{
    /// <summary>
    /// The two campaigns. They are separate level sets with separate progress, not difficulty
    /// settings on one campaign.
    ///
    /// The split exists because mechanics turned out to be a different product rather than a
    /// harder version of the same one: three rule types on one board reads as bookkeeping, not
    /// challenge (see GAME_EXPANSION_PLAN §6.25). The genre's own answer is the same -- Flow Free
    /// ships Bridges, Hexes and Warps as separate apps rather than mixing them into the base game.
    ///
    /// Classic is the default and the front door: pure routing, the thing a new player understands
    /// without being told. Advanced is opt-in, and carries at most ONE mechanic per level.
    /// </summary>
    public enum GameMode
    {
        Classic = 0,
        Advanced = 1
    }
}
