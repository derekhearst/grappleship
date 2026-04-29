using Sandbox;
using System;
using System.Linq;

namespace GrappleShip;

/// <summary>
/// Phase 1 grapple hook. Sits on the Player GameObject alongside s&amp;box's built-in
/// Sandbox.PlayerController + a CharacterController.
///
/// Uses a positional distance constraint — same approach as Branchpanic's
/// sbox-grapple (https://github.com/branchpanic/sbox-grapple): predict where the
/// body wants to go next tick, clamp to the rope sphere if it's outside, set
/// velocity to land exactly on the clamp. Gravity stays on for both ends. The
/// rope only acts when taut, so swinging happens naturally.
///
///  • F  (GrappleFire)    — fire / release   (currently bound to LMB while testing)
///  • E  (GrappleReelIn)  — reel in (costs stamina)
///  • Q  (GrappleReelOut) — reel out (free; pays out rope so you can fall/swing)
///
/// LMB and RMB are intentionally NOT used here — reserved for melee combat (Phase 2)
/// and ship cannons (Phase 6). Bindings are declared in ProjectSettings/Input.config
/// under the "Grapple" group.
/// </summary>
[Title( "Grapple Hook" )]
[Category( "GrappleShip" )]
[Icon( "anchor" )]
public sealed class GrappleHook : Component
{
	[Property] public CharacterController Cc { get; set; }

	// ── Tunables: line ──────────────────────────────────────────────────────
	[Property, Group( "Tuning — Line" ), Range( 100f, 3000f )]
	public float ReelSpeed { get; set; } = 800f;

	[Property, Group( "Tuning — Line" ), Range( 500f, 5000f )]
	public float MaxRange { get; set; } = 2000f;

	[Property, Group( "Tuning — Line" ), Range( 20f, 500f )]
	public float MinLineLength { get; set; } = 50f;

	[Property, Group( "Tuning — Line" ), Range( 0f, 5f )]
	public float AirDrag { get; set; } = 0.0f;

	// ── Tunables: stamina ──────────────────────────────────────────────────
	[Property, Group( "Tuning — Stamina" ), Range( 10f, 500f )]
	public float StaminaMax { get; set; } = 100f;

	[Property, Group( "Tuning — Stamina" ), Range( 0f, 200f )]
	public float StaminaDrainRate { get; set; } = 30f;

	[Property, Group( "Tuning — Stamina" ), Range( 0f, 200f )]
	public float StaminaRegenRate { get; set; } = 25f;

	// ── Runtime state (visible read-only for debugging) ─────────────────────
	[Property, Group( "State" ), ReadOnly] public float Stamina { get; set; }
	[Property, Group( "State" ), ReadOnly] public bool IsAttached { get; set; }
	[Property, Group( "State" ), ReadOnly] public Vector3 AnchorPoint { get; set; }
	[Property, Group( "State" ), ReadOnly] public float CurrentMaxLineLength { get; set; }

	private GameObject _hitObject;
	private Vector3 _hitLocalOffset;

	protected override void OnStart()
	{
		Cc ??= GetComponent<CharacterController>();
		Stamina = StaminaMax;
	}

	protected override void OnUpdate()
	{
		if ( Input.Pressed( "GrappleFire" ) )
		{
			if ( !IsAttached ) TryFire();
			else Release();
		}
	}

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( !IsAttached ) return;

		Gizmo.Draw.LineThickness = 2f;
		Gizmo.Draw.Color = Color.Yellow;

		Gizmo.Draw.Line( WorldPosition, AnchorPoint );
	}

	protected override void OnFixedUpdate()
	{
		if ( !IsAttached || !Cc.IsValid() ) return;

		// Track moving anchor (cube, ship, etc.) — anchor follows hit object's transform.
		if ( _hitObject.IsValid() )
		{
			AnchorPoint = _hitObject.WorldPosition + _hitObject.WorldRotation * _hitLocalOffset;
		}

		var dt = Time.Delta;

		// ── Reel input ───────────────────────────────────────────────────────
		bool reelingIn = Input.Down( "GrappleReelIn" );
		bool reelingOut = Input.Down( "GrappleReelOut" );

		if ( reelingIn && Stamina > 0f )
		{
			CurrentMaxLineLength = MathF.Max( MinLineLength, CurrentMaxLineLength - ReelSpeed * dt );
			Stamina = MathF.Max( 0f, Stamina - StaminaDrainRate * dt );
		}
		else if ( reelingOut )
		{
			CurrentMaxLineLength = MathF.Min( MaxRange, CurrentMaxLineLength + ReelSpeed * dt );
		}
		else
		{
			Stamina = MathF.Min( StaminaMax, Stamina + StaminaRegenRate * dt );
		}

		// ── Distance constraint, player side ────────────────────────────────
		// Predict, clamp to rope sphere, derive velocity to land on the clamp.
		// Lets the player fall freely under gravity until the rope catches.
		{
			var playerGoal = WorldPosition + Cc.Velocity * dt;
			var toGoal = playerGoal - AnchorPoint;
			if ( toGoal.Length > CurrentMaxLineLength )
			{
				playerGoal = AnchorPoint + toGoal.Normal * CurrentMaxLineLength;
				Cc.Velocity = ( playerGoal - WorldPosition ) / dt;
			}
		}

		// ── Distance constraint, anchor-object side ─────────────────────────
		// If we hooked a Rigidbody, apply the same projection so it swings off the player.
		// Gravity stays enabled — the cube falls naturally until the rope catches.
		var hitRb = _hitObject.IsValid()
			? ( _hitObject.GetComponentInParent<Rigidbody>() ?? _hitObject.GetComponent<Rigidbody>() )
			: null;
		if ( hitRb.IsValid() )
		{
			var pb = hitRb.PhysicsBody;
			var cubePos = _hitObject.WorldPosition;
			var cubeVel = pb.IsValid() ? pb.Velocity : hitRb.Velocity;
			var cubeGoal = cubePos + cubeVel * dt;
			var toCubeGoal = cubeGoal - WorldPosition;
			if ( toCubeGoal.Length > CurrentMaxLineLength )
			{
				cubeGoal = WorldPosition + toCubeGoal.Normal * CurrentMaxLineLength;
				var newVel = ( cubeGoal - cubePos ) / dt;
				if ( pb.IsValid() )
				{
					pb.Sleeping = false;
					pb.Velocity = newVel;
				}
				else
				{
					hitRb.Velocity = newVel;
				}
			}
			// else: cube is slack — let physics handle it normally (gravity, friction, etc.)
		}

		if ( AirDrag > 0f )
		{
			Cc.Velocity *= 1f - ( AirDrag * dt );
		}
	}

	void TryFire()
	{
		var camera = Scene.GetAllComponents<CameraComponent>()
			.Where( x => x.IsMainCamera )
			.FirstOrDefault();
		if ( camera is null ) return;

		var origin = camera.WorldPosition;
		var forward = camera.WorldRotation.Forward;
		var trace = Scene.Trace
			.Ray( origin, origin + forward * MaxRange )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( trace.Hit )
		{
			_hitObject = trace.GameObject;
			if ( _hitObject.IsValid() )
			{
				_hitLocalOffset = _hitObject.WorldRotation.Inverse * (trace.HitPosition - _hitObject.WorldPosition);
			}
			AnchorPoint = trace.HitPosition;
			CurrentMaxLineLength = MathF.Max( MinLineLength, ( AnchorPoint - WorldPosition ).Length );
			IsAttached = true;

			// Zero the hit Rigidbody's velocity so any residual motion doesn't make the
			// constraint snap it on the first fixed update.
			var rb = _hitObject?.GetComponentInParent<Rigidbody>() ?? _hitObject?.GetComponent<Rigidbody>();
			if ( rb.IsValid() )
			{
				if ( rb.PhysicsBody.IsValid() )
				{
					rb.PhysicsBody.Velocity = Vector3.Zero;
					rb.PhysicsBody.AngularVelocity = Vector3.Zero;
				}
				rb.Velocity = Vector3.Zero;
				rb.AngularVelocity = Vector3.Zero;
			}

			Log.Info( $"[Grapple] Fired → {_hitObject?.Name ?? "<none>"} | hasRb={rb.IsValid()} | dist={CurrentMaxLineLength:F0}" );
		}
		else
		{
			Log.Info( "[Grapple] Fired → no hit within range" );
		}
	}

	void Release()
	{
		IsAttached = false;
		AnchorPoint = Vector3.Zero;
		CurrentMaxLineLength = 0f;
		_hitObject = null;
		_hitLocalOffset = Vector3.Zero;
	}
}
