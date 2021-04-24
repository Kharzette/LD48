using System;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using InputLib;
using UtilityLib;

using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Windows;


namespace LD48
{
    internal static class Program
    {
		internal enum MyActions
		{
			MoveForwardBack, MoveForward, MoveBack,
			MoveLeftRight, MoveLeft, MoveRight,
			FlyDown, Crouch, Punch, Block,
			Turn, TurnLeft, TurnRight, Jump,
			Pitch, PitchUp, PitchDown, Climb,
			ToggleMouseLookOn, ToggleMouseLookOff,
			SensitivityUp, SensitivityDown,
			AB1, AB2, AB3, AB4, AB5, AB6, AB7, AB8,
			AbilityUI, Nothing
		};

		const float	MaxTimeDelta	=0.1f;


		[STAThread]
		static void Main()
		{
			//turn this on for help with leaky stuff
			Configuration.EnableObjectTracking	=true;

			GraphicsDevice	gd	=new GraphicsDevice("Depth of Mind",
				FeatureLevel.Level_11_0, 0.1f, 3000f);
				
			//save renderform position
			gd.RendForm.DataBindings.Add(new System.Windows.Forms.Binding("Location",
					Settings.Default,
					"MainWindowPos", true,
					System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));

			int	borderWidth		=gd.RendForm.Size.Width - gd.RendForm.ClientSize.Width;
			int	borderHeight	=gd.RendForm.Size.Height - gd.RendForm.ClientSize.Height;

			gd.RendForm.Location	=Settings.Default.MainWindowPos;
			gd.RendForm.Size		=new System.Drawing.Size(
				1280 + borderWidth,
				720 + borderHeight);

			gd.CheckResize();

			//used to have a hard coded path here for #debug
			//but now can just use launch.json to provide it
			string	rootDir	=".";

			//set title of progress window
			SharedForms.ShaderCompileHelper.mTitle	="Compiling Shaders...";

			//hold right click to turn, or turn anytime mouse moves?
			bool	bRightClickToTurn	=false;

			MapLoop	mapLoop	=new MapLoop(gd, rootDir);
			
			PlayerSteering	pSteering	=SetUpSteering();
			Input			inp			=SetUpInput(bRightClickToTurn);
			Random			rand		=new Random();
			UserSettings	sets		=new UserSettings();

			UpdateTimer	time	=new UpdateTimer(true, false);

			time.SetFixedTimeStepSeconds(1f / 60f);	//60fps update rate
			time.SetMaxDeltaSeconds(MaxTimeDelta);

			Vector3	pos				=Vector3.One * 5f;
			Vector3	lightDir		=-Vector3.UnitY;
			bool	bMouseLookOn	=false;

			EventHandler	actHandler	=new EventHandler(
				delegate(object s, EventArgs ea)
				{
					inp.ClearInputs();
					if(!bRightClickToTurn)
					{
						bMouseLookOn	=true;
						gd.SetCapture(true);
					}
				});

			EventHandler<EventArgs>	deActHandler	=new EventHandler<EventArgs>(
				delegate(object s, EventArgs ea)
				{
					gd.SetCapture(false);
					bMouseLookOn	=false;
				});

			gd.RendForm.Activated		+=actHandler;
			gd.RendForm.AppDeactivated	+=deActHandler;

			List<Input.InputAction>	acts	=new List<Input.InputAction>();

			RenderLoop.Run(gd.RendForm, () =>
			{
				if(!gd.RendForm.Focused)
				{
					Thread.Sleep(33);
				}

				gd.CheckResize();

				if(bMouseLookOn && gd.RendForm.Focused)
				{
					gd.ResetCursorPos();
				}

				//Clear views
				gd.ClearViews();

				time.Stamp();
				while(time.GetUpdateDeltaSeconds() > 0f)
				{
					acts	=UpdateInput(inp, sets, gd, bRightClickToTurn,
						time.GetUpdateDeltaSeconds(), ref bMouseLookOn);
					if(!gd.RendForm.Focused)
					{
						acts.Clear();
						bMouseLookOn	=false;
						gd.SetCapture(false);
					}
					mapLoop.Update(time, acts, pSteering);
					time.UpdateDone();
				}

				mapLoop.RenderUpdate(time.GetRenderUpdateDeltaMilliSeconds());

				mapLoop.Render();

				gd.Present();

				acts.Clear();
			});

			Settings.Default.Save();
			sets.SaveSettings();

			gd.RendForm.Activated		-=actHandler;
			gd.RendForm.AppDeactivated	-=deActHandler;

			mapLoop.FreeAll();
			inp.FreeAll();
			
			//Release all resources
			gd.ReleaseAll();
		}


		static List<Input.InputAction> UpdateInput(
			Input inp, UserSettings sets,
			GraphicsDevice gd, bool bHoldClickTurn,
			float delta, ref bool bMouseLookOn)
		{
			List<Input.InputAction>	actions	=inp.GetAction();

			inp.ClampInputTimes(MaxTimeDelta);

			if(bHoldClickTurn)
			{
				foreach(Input.InputAction act in actions)
				{
					if(act.mAction.Equals(MyActions.ToggleMouseLookOn))
					{
						bMouseLookOn	=true;
						gd.SetCapture(true);
						inp.MapAxisAction(MyActions.Pitch, Input.MoveAxis.MouseYAxis);
						inp.MapAxisAction(MyActions.Turn, Input.MoveAxis.MouseXAxis);
					}
					else if(act.mAction.Equals(MyActions.ToggleMouseLookOff))
					{
						bMouseLookOn	=false;
						gd.SetCapture(false);
						inp.UnMapAxisAction(Input.MoveAxis.MouseYAxis);
						inp.UnMapAxisAction(Input.MoveAxis.MouseXAxis);
					}
				}
			}

			//delta scale analogs, since there's no timestamp stuff in gamepad code
			foreach(Input.InputAction act in actions)
			{
				if(!act.mbTime && act.mDevice == Input.InputAction.DeviceType.ANALOG)
				{
					//analog needs a time scale applied
					act.mMultiplier	*=delta;
				}
			}

			//scale inputs to user prefs
			foreach(Input.InputAction act in actions)
			{
				if(act.mAction.Equals(MyActions.Turn)
					|| act.mAction.Equals(MyActions.TurnLeft)
					|| act.mAction.Equals(MyActions.TurnRight)
					|| act.mAction.Equals(MyActions.Pitch)
					|| act.mAction.Equals(MyActions.PitchDown)
					|| act.mAction.Equals(MyActions.PitchUp))
				{
					if(act.mDevice == Input.InputAction.DeviceType.MOUSE)
					{
						act.mMultiplier	*=UserSettings.MouseTurnMultiplier
							* sets.mTurnSensitivity;
					}
					else if(act.mDevice == Input.InputAction.DeviceType.ANALOG)
					{
						act.mMultiplier	*=UserSettings.AnalogTurnMultiplier;
					}
					else if(act.mDevice == Input.InputAction.DeviceType.KEYS)
					{
						act.mMultiplier	*=UserSettings.KeyTurnMultiplier;
					}
				}
			}

			//sensitivity adjust
			foreach(Input.InputAction act in actions)
			{
				float	sense	=sets.mTurnSensitivity;
				if(act.mAction.Equals(MyActions.SensitivityUp))
				{
					sense	+=0.1f;
				}
				else if(act.mAction.Equals(MyActions.SensitivityDown))
				{
					sense	-=0.1f;
				}
				else
				{
					continue;
				}
				sets.mTurnSensitivity	=Math.Clamp(sense, 0.1f, 10f);
			}

			return	actions;
		}

		static Input SetUpInput(bool bHoldClickTurn)
		{
			Input	inp	=new InputLib.Input(1f / Stopwatch.Frequency);
			
			inp.MapAction(MyActions.MoveForward, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.W);
			inp.MapAction(MyActions.MoveLeft, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.A);
			inp.MapAction(MyActions.MoveBack, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.S);
			inp.MapAction(MyActions.MoveRight, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.D);

			//arrow keys
			inp.MapAction(MyActions.MoveForward, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.Up);
			inp.MapAction(MyActions.MoveBack, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.Down);
			inp.MapAction(MyActions.TurnLeft, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.Left);
			inp.MapAction(MyActions.TurnRight, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.Right);
			inp.MapAction(MyActions.PitchUp, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.Q);
			inp.MapAction(MyActions.PitchDown, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.E);

			//press and hold style jump
			inp.MapAction(MyActions.Jump, ActionTypes.ActivateOnce,
				Modifiers.None, System.Windows.Forms.Keys.Space);
			inp.MapAction(MyActions.Jump, ActionTypes.ActivateOnce,
				Modifiers.ShiftHeld, System.Windows.Forms.Keys.Space);
			inp.MapAction(MyActions.Jump, ActionTypes.ActivateOnce,
				Modifiers.ControlHeld, System.Windows.Forms.Keys.Space);
			inp.MapAction(MyActions.Jump, ActionTypes.ActivateOnce,
				Modifiers.None,	Input.VariousButtons.GamePadY);

			//climb / swim up / fly up style jump
			inp.MapAction(MyActions.Climb, ActionTypes.ContinuousHold,
				Modifiers.None, System.Windows.Forms.Keys.Space);

			if(bHoldClickTurn)
			{
				inp.MapToggleAction(MyActions.ToggleMouseLookOn,
					MyActions.ToggleMouseLookOff, Modifiers.None,
					Input.VariousButtons.RightMouseButton);
			}
			else
			{
				inp.MapAxisAction(MyActions.Pitch, Input.MoveAxis.MouseYAxis);
				inp.MapAxisAction(MyActions.Turn, Input.MoveAxis.MouseXAxis);
			}

			inp.MapAxisAction(MyActions.Pitch, Input.MoveAxis.GamePadRightYAxis);
			inp.MapAxisAction(MyActions.Turn, Input.MoveAxis.GamePadRightXAxis);
			inp.MapAxisAction(MyActions.MoveLeftRight, Input.MoveAxis.GamePadLeftXAxis);
			inp.MapAxisAction(MyActions.MoveForwardBack, Input.MoveAxis.GamePadLeftYAxis);


			//sensitivity adjust
			inp.MapAction(MyActions.SensitivityDown, ActionTypes.PressAndRelease,
				Modifiers.None, System.Windows.Forms.Keys.OemMinus);
			//for numpad
			inp.MapAction(MyActions.SensitivityUp, ActionTypes.PressAndRelease,
				Modifiers.None, System.Windows.Forms.Keys.Oemplus);
			//non numpad will have shift held too
			inp.MapAction(MyActions.SensitivityUp, ActionTypes.PressAndRelease,
				Modifiers.ShiftHeld, System.Windows.Forms.Keys.Oemplus);

			return	inp;
		}

		static PlayerSteering SetUpSteering()
		{
			PlayerSteering	pSteering	=new PlayerSteering();
			pSteering.Method			=PlayerSteering.SteeringMethod.Fly;

			pSteering.SetMoveEnums(MyActions.MoveForwardBack, MyActions.MoveLeftRight,
				MyActions.MoveForward, MyActions.MoveBack,
				MyActions.MoveLeft, MyActions.MoveRight,
				MyActions.Nothing, MyActions.Nothing,
				MyActions.Nothing, MyActions.Nothing);

			pSteering.SetTurnEnums(MyActions.Turn, MyActions.TurnLeft, MyActions.TurnRight);

			pSteering.SetPitchEnums(MyActions.Pitch, MyActions.PitchUp, MyActions.PitchDown);

			return	pSteering;
		}
	}
}
