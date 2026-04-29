using Sandbox;
using System;
using System.Linq;

namespace GrappleShip;

/// <summary>
/// Phase 1 grapple hook. Sits on the Player GameObject alongside s&box's built-in
/// Sandbox.PlayerController + a CharacterController. Fires from the main camera,
/// anchors on hit, and writes to CharacterController.Velocity to apply tension.
/// (PlayerController.Velocity is read-only — the writable surface is on CharacterController,
/// which the controller's MoveModeWalk uses internally.)
///
///  • F  (GrappleFire)    — fire / release
///  • E  (GrappleReelIn)  — reel in (costs stamina)
///  • Q  (GrappleReelOut) — reel out (free; pays out rope so you can fall/swing)
///
/// LMB and RMB are intentionally NOT used here — reserved for melee combat (Phase 2)
/// and ship cannons (Phase 6). Bindings are declared in ProjectSettings/Input.config
/// under the "Grapple" group.
///
/// If the hit point is on a movable object (Rigidbody), the anchor follows that object
/// and tension force is applied equally to it (Newton's third law) so cubes get yanked
/// when grappled instead of acting as an immovable point.
/// </summary>
[Title( "Grapple Hook" )]
[Category( "GrappleShip" )]
[Icon( "anchor" )]
public sealed class GrappleHook : Component
{
	[Property] public CharacterController Cc { get; set; }

	[Property, ReadOnly] public float Stamina { get; set; }
	[Property, ReadOnly] public bool IsAttached { get; set; }
	[Property, ReadOnly] public Vector3 AnchorPoint { get; set; }
	[Property, ReadOnly] public float CurrentMaxLineLength { get; set; }

	private GameObject _hitObject;
	private Vector3 _hitLocalOffset;

	protected override void OnStart()
	{
		Cc ??= GetComponent<CharacterController>();
		var tuning = DebugTuning.GetCurrent( Scene );
		Stamina = tuning?.StaminaMax ?? 100f;
	}

	protected override void OnUpdate()
	{
		var tuning = DebugTuning.GetCurrent( Scene );
		if ( tuning == null ) return;

		if ( Input.Pressed( "GrappleFire" ) )
		{
			if ( !IsAttached ) TryFire( tuning );
			else Release();
		}

		if ( IsAttached )
		{
			Gizmo.Draw.LineThickness = 2f;
			Gizmo.Draw.Color = Color.Yellow;
			Gizmo.Draw.Line( WorldPosition, AnchorPoint );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !IsAttached || !Cc.IsValid() ) return;
		var tuning = DebugTuning.GetCurrent( Scene );
		if ( tuning == null ) return;

		if ( _hitObject.IsValid() )
		{
			AnchorPoint = _hitObject.WorldPosition + _hitObject.WorldRotation * _hitLocalOffset;
		}

		var dt = Time.Delta;
		var toAnchor = AnchorPoint - WorldPosition;
		var distance = toAnchor.Length;
		if ( distance < 0.01f ) return;
		var dir = toAnchor / distance;

		bool reelingIn  = Input.Down( "GrappleReelIn" );
		bool reelingOut = Input.Down( "GrappleReelOut" );

		if ( reelingIn && Stamina > 0f )
		{
			CurrentMaxLineLength = MathF.Max( 50f, CurrentMaxLineLength - tuning.ReelSpeed * dt );
			Stamina = MathF.Max( 0f, Stamina - tuning.StaminaDrainRate * dt );
		}
		else if ( reelingOut )
		{
			CurrentMaxLineLength = MathF.Min( tuning.MaxRange, CurrentMaxLineLength + tuning.ReelSpeed * dt );
		}
		else
		{
			Stamina = MathF.Min( tuning.StaminaMax, Stamina + tuning.StaminaRegenRate * dt );
		}

		// Rope physics. "Taut" means we're at or past the max line length.
		// Player tension is conditional on moving away (otherwise it'd jitter when at exact length),
		// but the *anchor object* always feels the pull while taut — that way heavy cubes still move
		// and apparent action-reaction is correct.
		bool taut = distance >= CurrentMaxLineLength;

		if ( taut )
		{
			var velAlongRope = Vector3.Dot( Cc.Velocity, dir );
			if ( velAlongRope < 0f )
			{
				Cc.Velocity += dir * tuning.TensionForce * dt;
			}

			var hitRb = _hitObject.IsValid()
				? ( _hitObject.GetComponentInParent<Rigidbody>() ?? _hitObject.GetComponent<Rigidbody>() )
				: null;
			if ( hitRb.IsValid() )
			{
				// Wake the body in case it was sleeping, then apply pull force.
				// ObjectPullForce is separate from TensionForce so we can dial it up
				// without distorting the player-side constraint.
				hitRb.Sleeping = false;
				hitRb.ApplyForce( -dir * tuning.ObjectPullForce );
			}

			if ( distance > CurrentMaxLineLength )
			{
				var overstretch = distance - CurrentMaxLineLength;
				WorldPosition += dir * overstretch;
			}
		}

		if ( tuning.AirDrag > 0f )
		{
			Cc.Velocity *= 1f - ( tuning.AirDrag * dt );
		}
	}

	void TryFire( DebugTuning tuning )
	{
		var camera = Scene.GetAllComponents<CameraComponent>()
			.Where( x => x.IsMainCamera )
			.FirstOrDefault();
		if ( camera is null ) return;

		var origin = camera.WorldPosition;
		var forward = camera.WorldRotation.Forward;
		var trace = Scene.Trace
			.Ray( origin, origin + forward * tuning.MaxRange )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( trace.Hit )
		{
			_hitObject = trace.GameObject;
			if ( _hitObject.IsValid() )
			{
				_hitLocalOffset = _hitObject.WorldRotation.Inverse * ( trace.HitPosition - _hitObject.WorldPosition );
			}
			AnchorPoint = trace.HitPosition;
			CurrentMaxLineLength = ( AnchorPoint - WorldPosition ).Length;
			IsAttached = true;
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
