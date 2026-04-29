using Sandbox;

namespace GrappleShip;

/// <summary>
/// Deprecated. Tunables are now [Property] fields directly on the components that
/// use them (see GrappleHook). This empty stub is kept so the existing scene's
/// reference doesn't error on load — the DebugTuning GameObject can be safely
/// deleted from the scene whenever.
/// </summary>
[Title( "Debug Tuning (deprecated)" )]
[Category( "GrappleShip" )]
[Icon( "tune" )]
public sealed class DebugTuning : Component
{
}
