using Sandbox;
using System.Linq;

namespace GrappleShip;

/// <summary>
/// Live-tunable values for Phase 1 grapple iteration.
/// One instance per scene; queried by player components via DebugTuning.GetCurrent(Scene).
/// All numeric properties have [Range(...)] so they show up as sliders in the inspector
/// and can be tweaked live while playing.
/// </summary>
[Title( "Debug Tuning" )]
[Category( "GrappleShip" )]
[Icon( "tune" )]
public sealed class DebugTuning : Component
{
	// ── Grapple — line behavior ──────────────────────────────────────────────
	[Property, Group( "Grapple" ), Range( 100f, 3000f )]
	public float ReelSpeed { get; set; } = 800f;

	[Property, Group( "Grapple" ), Range( 500f, 5000f )]
	public float MaxRange { get; set; } = 2000f;

	[Property, Group( "Grapple" ), Range( 0f, 5000f )]
	public float TensionForce { get; set; } = 1500f;

	[Property, Group( "Grapple" ), Range( 0f, 5f )]
	public float AirDrag { get; set; } = 0.0f;

	// ── Grapple — pulling physics objects (separate so we can crank it without
	// affecting the player constraint that uses TensionForce) ────────────────
	[Property, Group( "Grapple" ), Range( 0f, 50000f )]
	public float ObjectPullForce { get; set; } = 8000f;

	// ── Stamina ──────────────────────────────────────────────────────────────
	[Property, Group( "Stamina" ), Range( 10f, 500f )]
	public float StaminaMax { get; set; } = 100f;

	[Property, Group( "Stamina" ), Range( 0f, 200f )]
	public float StaminaDrainRate { get; set; } = 30f;

	[Property, Group( "Stamina" ), Range( 0f, 200f )]
	public float StaminaRegenRate { get; set; } = 25f;

	public static DebugTuning GetCurrent( Scene scene )
	{
		return scene?.GetAll<DebugTuning>().FirstOrDefault();
	}
}
