#define	QuakeUnits
using System;
using System.Collections.Generic;
using BSPZone;
using MeshLib;
using UtilityLib;
using InputLib;
using EntityLib;

using SharpDX;

using MatLib = MaterialLib.MaterialLib;
using Collision = BSPZone.Collision;


namespace LD48
{
	internal partial class MapLoop
	{
		enum GameContents
		{
			Water	=(1 << 18),
			Lava	=(1 << 20),
			Slime	=(1 << 19)
		}

		//player vitals
		int		mMaxHealth, mMaxMana;
		int		mHealth, mMana;

		//player character stuff
		IArch					mPArch;
		Character				mPChar;
		MatLib					mPMats;
		AnimLib					mPAnims;
		ShadowHelper.Shadower	mPShad;
		Mobile					mPMob, mPCamMob;
		bool					mbFly	=false;
		BoundingBox				mFatBox;
		List<int>				mModelsHit	=new List<int>();
		Entity					mPEntity;
		MeshLighting			mPMeshLighting;

		//physics stuffs
		Vector3	mVelocity		=Vector3.Zero;
		Vector3	mCamVelocity	=Vector3.Zero;

		//constants
		//player
#if QuakeUnits
		const float	PlayerBoxWidth		=32f;
		const float	PlayerBoxStanding	=56f;
		const float	PlayerBoxCrouching	=36f;
		const float	PlayerEyeStanding	=48f;
		const float	PlayerEyeCrouching	=32f;
#elif ValveUnits
		const float	PlayerBoxWidth		=32f;
		const float	PlayerBoxStanding	=72f;
		const float	PlayerBoxCrouching	=36f;
		const float	PlayerEyeStanding	=64f;
		const float	PlayerEyeCrouching	=28f;
#elif GrogUnits
		const float	PlayerBoxWidth		=32f;
		const float	PlayerBoxStanding	=72f;
		const float	PlayerBoxCrouching	=36f;
		const float	PlayerEyeStanding	=64f;
		const float	PlayerEyeCrouching	=28f;
#endif

		//physics
		const float	JogMoveForce		=2000f;	//Fig Newtons
		const float	FlyMoveForce		=1000f;	//Fig Newtons
		const float	FlyUpMoveForce		=300f;	//Fig Newtons
		const float	MidAirMoveForce		=100f;	//Slight wiggle midair
		const float	SwimMoveForce		=900f;	//Swimmery
		const float	SwimUpMoveForce		=900f;	//Swimmery
		const float	StumbleMoveForce	=700f;	//Fig Newtons
		const float	JumpForce			=20000;	//leapometers
		const float	GravityForce		=980f;	//Gravitons
		const float	BouyancyForce		=700f;	//Gravitons
		const float	GroundFriction		=10f;	//Frictols
		const float	StumbleFriction		=6f;	//Frictols
		const float	AirFriction			=0.1f;	//Frictols
		const float	FlyFriction			=2f;	//Frictols
		const float	SwimFriction		=10f;	//Frictols


		void AccumulateVelocity(Vector3 moveVec)
		{
			mCamVelocity	+=moveVec * 0.5f;
		}


		void ApplyFriction(float secDelta, float friction)
		{
			mCamVelocity	-=(friction * mCamVelocity * secDelta * 0.5f);
		}


		void ApplyForce(float force, Vector3 direction, float secDelta)
		{
			mCamVelocity	+=direction * force * (secDelta * 0.5f);
		}


		Vector3 UpdateSwimming(float secDelta, List<Input.InputAction> actions, PlayerSteering ps)
		{
			bool	bSwimUp	=false;

			ps.Method	=PlayerSteering.SteeringMethod.Swim;

			foreach(Input.InputAction act in actions)
			{
				if(act.mAction.Equals(Program.MyActions.Climb))
				{
					//swim up
					bSwimUp	=true;
				}
			}

			Vector3	startPos	=mPCamMob.GetGroundPos();
			Vector3	moveVec		=ps.Update(startPos, mGD.GCam.Forward, mGD.GCam.Left, mGD.GCam.Up, actions);

			moveVec	*=SwimMoveForce;

			AccumulateVelocity(moveVec);

			Vector3	pos	=startPos;

			if(bSwimUp)
			{
				if(bCanClimbOut())
				{
					ApplyForce(JumpForce, Vector3.Up, secDelta);
				}
				else
				{
					ApplyForce(SwimUpMoveForce, Vector3.Up, secDelta);
				}
			}

			//friction / gravity / bouyancy
			ApplyFriction(secDelta, SwimFriction);
			ApplyForce(GravityForce, Vector3.Down, secDelta);
			ApplyForce(BouyancyForce, Vector3.Up, secDelta);

			pos	+=mCamVelocity * secDelta;

			mCamVelocity	+=moveVec * 0.5f;

			if(bSwimUp)
			{
				ApplyForce(SwimUpMoveForce, Vector3.Up, secDelta);
			}

			//friction / gravity / bouyancy
			ApplyFriction(secDelta, SwimFriction);
			ApplyForce(GravityForce, Vector3.Down, secDelta);
			ApplyForce(BouyancyForce, Vector3.Up, secDelta);

			return	pos;
		}


		Vector3 UpdateFly(float secDelta, List<Input.InputAction> actions, PlayerSteering ps)
		{
			bool	bFlyUp	=false;

			foreach(Input.InputAction act in actions)
			{
				if(act.mAction.Equals(Program.MyActions.Climb))
				{
					//fly up
					bFlyUp	=true;
				}
			}
			Vector3	startPos	=mPCamMob.GetGroundPos();
			Vector3	moveVec		=ps.Update(startPos, mGD.GCam.Forward, mGD.GCam.Left, mGD.GCam.Up, actions);

			moveVec	*=FlyMoveForce;

			AccumulateVelocity(moveVec);
			ApplyFriction(secDelta, FlyFriction);

			Vector3	pos	=startPos;

			if(bFlyUp)
			{
				ApplyForce(FlyUpMoveForce, Vector3.Up, secDelta);
			}
			pos	+=mCamVelocity * secDelta;

			AccumulateVelocity(moveVec);
			ApplyFriction(secDelta, FlyFriction);

			if(bFlyUp)
			{
				ApplyForce(FlyUpMoveForce, Vector3.Up, secDelta);
			}

			return	pos;
		}


		Vector3 UpdateGround(float secDelta, List<Input.InputAction> actions,
							PlayerSteering ps, out bool bJumped)
		{
			bool	bGravity	=false;
			float	friction	=GroundFriction;

			ps.Method	=PlayerSteering.SteeringMethod.FirstPerson;

			if(mPCamMob.IsOnGround())
			{
				if(!mPCamMob.IsBadFooting())
				{
					friction	=GroundFriction;
				}
				else
				{
					friction	=AirFriction;
				}
			}
			else
			{
				bGravity	=true;
				friction	=AirFriction;
			}

			bJumped	=false;

			foreach(Input.InputAction act in actions)
			{
				if(act.mAction.Equals(Program.MyActions.Jump))
				{
					if(mPCamMob.IsOnGround())
					{
						friction	=AirFriction;
						bJumped		=true;
					}
				}
			}

			Vector3	startPos	=mPCamMob.GetGroundPos();
			Vector3	moveVec		=ps.Update(startPos, mGD.GCam.Forward, mGD.GCam.Left, mGD.GCam.Up, actions);

			if(mPCamMob.IsOnGround())
			{
				moveVec	*=JogMoveForce;
			}
			else if(mPCamMob.IsBadFooting())
			{
				moveVec	*=StumbleMoveForce;
			}
			else
			{
				moveVec	*=MidAirMoveForce;
			}

			AccumulateVelocity(moveVec);
			ApplyFriction(secDelta, friction);

			Vector3	pos	=startPos;

			if(bGravity)
			{
				ApplyForce(GravityForce, Vector3.Down, secDelta);
			}
			if(bJumped)
			{
				ApplyForce(JumpForce, Vector3.Up, secDelta);

				//jump use a 60fps delta time for consistency
				pos	+=mCamVelocity * (1f/60f);
			}
			else
			{
				pos	+=mCamVelocity * secDelta;
			}

			AccumulateVelocity(moveVec);
			ApplyFriction(secDelta, friction);
			if(bGravity)
			{
				ApplyForce(GravityForce, Vector3.Down, secDelta);
			}
			if(bJumped)
			{
				ApplyForce(JumpForce, Vector3.Up, secDelta);
			}

			return	pos;
		}


		void UpdateMiscKeys(List<Input.InputAction> actions, PlayerSteering ps)
		{
			foreach(Input.InputAction act in actions)
			{
				if(act.mAction.Equals(Program.MyActions.AB1))
				{
					mHealth--;
					UpdateHealthGump();
				}
				else if(act.mAction.Equals(Program.MyActions.AB2))
				{
					mMana--;
					UpdateManaGump();
				}
			}
		}


		void UpdateHealthGump()
		{
			float	perc	=mHealth / (float)mMaxHealth;

			mUI.ModifyGumpSecondLayerOffset("HealthMeter",
				perc * Vector2.UnitY - Vector2.UnitY);
		}


		void UpdateManaGump()
		{
			float	perc	=mMana / (float)mMaxMana;

			mUI.ModifyGumpSecondLayerOffset("ManaMeter",
				perc * Vector2.UnitY - Vector2.UnitY);
		}


		//check to see if a bipedal player can climb out of liquid
		//onto some solid ground.  Assumes player in water
		bool	bCanClimbOut()
		{
			Vector3	midPos	=mPCamMob.GetMiddlePos();
			Vector3	eyePos	=mPCamMob.GetEyePos();

			UInt32	contents	=mZone.GetWorldContents(eyePos);
			if(contents != 0)
			{
				mST.ModifyStringText(mFonts[0], "Eye Contents: " + contents, "ClimbStatus");
				return	false;
			}

			//noggin is in empty space
			//Not to be confused with empty contents
			BoundingBox	crouchBox	=Misc.MakeBox(PlayerBoxWidth,
				PlayerBoxCrouching, PlayerBoxWidth);

			//trace upward to about crouch height above the eye
			Vector3	traceStart	=eyePos;
			Vector3	traceTarget	=eyePos + Vector3.Up * PlayerBoxCrouching;

			Collision	col;
			bool	bHit	=mZone.TraceAll(null, crouchBox,
				traceStart, traceTarget, out col);
			if(bHit)
			{
				mST.ModifyStringText(mFonts[0], "Eye Contents: " + contents +
					", Head hit something...", "ClimbStatus");
				return	false;	//banged into something
			}

			//get a horizon leveled view direction
			//cam direction backward
			Vector3	flatLookVec	=-mGD.GCam.Forward;
			flatLookVec.Y	=0f;
			flatLookVec.Normalize();

			//trace forward about 1.5 box widths
			traceStart	=traceTarget;
			traceTarget	+=flatLookVec * (PlayerBoxWidth * 1.5f);

			bHit	=mZone.TraceAll(null, crouchBox,
				traceStart, traceTarget, out col);
			if(bHit)
			{
				mST.ModifyStringText(mFonts[0], "Eye Contents: " + contents +
					", Forward trace hit something...", "ClimbStatus");
				return	false;	//banged into something
			}

			//trace down to check for solid ground
			traceStart	=traceTarget;
			traceTarget	+=Vector3.Down * (PlayerBoxCrouching * 2f);

			bHit	=mZone.TraceAll(null, crouchBox,
				traceStart, traceTarget, out col);
			if(!bHit)
			{
				mST.ModifyStringText(mFonts[0], "Eye Contents: " + contents +
					", Down trace empty...", "ClimbStatus");
				return	false;	//nothing to climb onto
			}

			//see if the ground is good
			return	col.mPlaneHit.IsGround();
		}
	}
}
