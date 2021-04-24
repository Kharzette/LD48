using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BSPZone;
using MeshLib;
using UtilityLib;
using MaterialLib;
using ParticleLib;
using AudioLib;
using InputLib;
using EntityLib;

using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D;

using MatLib = MaterialLib.MaterialLib;


namespace LD48
{
	internal partial class MapLoop
	{
		//data
		Zone			mZone;
		IndoorMesh		mZoneDraw;
		MatLib			mZoneMats;
		string			mGameRootDir;
		StuffKeeper		mSKeeper;

		//entities
		EntityBoss		mEBoss	=new EntityBoss();
		List<Component>	mBModelMovers;
		List<Component>	mTriggers;
		List<Component>	mPickUpCVs;
		List<Component>	mStaticComps;
		List<Component>	mVisibleSMC	=new List<Component>();

		//list of levels
		List<string>	mLevels		=new List<string>();
		int				mCurLevel	=0;

		//list of available anims
		List<string>	mAnims			=new List<string>();
		int				mCurAnim		=0;
		float			mCurAnimTime	=0f;

		//dyn lights
		DynamicLights	mDynLights;
		List<int>		mActiveLights	=new List<int>();

		Random	mRand	=new Random();

		//helpers
		ParticleHelper		mPHelper		=new ParticleHelper();
		IntermissionHelper	mIMHelper		=new IntermissionHelper();
		ShadowHelper		mShadowHelper	=new ShadowHelper();
		IDKeeper			mKeeper			=new IDKeeper();

		//static stuff
		MatLib						mStaticMats;
		Dictionary<string, IArch>	mStatics		=new Dictionary<string, IArch>();

		//static shadows
		Dictionary<StaticMeshComp, ShadowHelper.Shadower>	mStaticShads
			=new Dictionary<StaticMeshComp, ShadowHelper.Shadower>();

		//gpu
		GraphicsDevice	mGD;
		PostProcess		mPost;
		ParticleBoss	mPB;
		MatLib			mPartMats;

		//audio
		Audio	mAudio	=new Audio();

		//Fonts / UI
		ScreenText		mST;
		ScreenUI		mUI;
		MatLib			mFontMats, mUIMats;
		Matrix			mTextProj;
		Mover2			mTextMover	=new Mover2();
		int				mResX, mResY;
		List<string>	mFonts	=new List<string>();

		//test particle emitters
		List<int>	mTestEmitters	=new List<int>();

		//constants
		const float	ShadowSlop			=12f;


		internal MapLoop(GraphicsDevice gd, string gameRootDir)
		{
			mGD				=gd;
			mGameRootDir	=gameRootDir;
			mResX			=gd.RendForm.ClientRectangle.Width;
			mResY			=gd.RendForm.ClientRectangle.Height;

			mSKeeper	=new StuffKeeper();

			mSKeeper.eCompileNeeded	+=SharedForms.ShaderCompileHelper.CompileNeededHandler;
			mSKeeper.eCompileDone	+=SharedForms.ShaderCompileHelper.CompileDoneHandler;

			mSKeeper.Init(mGD, gameRootDir);

			mZoneMats	=new MatLib(gd, mSKeeper);
			mZone		=new Zone();
			mZoneDraw	=new MeshLib.IndoorMesh(gd, mZoneMats);
			mPartMats	=new MatLib(mGD, mSKeeper);
			mPB			=new ParticleBoss(gd.GD, mPartMats);
			mFontMats	=new MatLib(gd, mSKeeper);

			mFontMats.CreateMaterial("Text");
			mFontMats.SetMaterialEffect("Text", "2D.fx");
			mFontMats.SetMaterialTechnique("Text", "Text");

			mFonts	=mSKeeper.GetFontList();

			mST	=new ScreenText(gd.GD, mFontMats, mFonts[0], 1000);

			mTextProj	=Matrix.OrthoOffCenterLH(0, mResX, mResY, 0, 0.1f, 5f);

			MakeHealthManaGumps();

			Vector4	color	=Vector4.UnitY + (Vector4.UnitW * 0.15f);

			//string indicators for various statusy things
			mST.AddString(mFonts[0], "Stuffs", "ClimbStatus",
				color, Vector2.UnitX * 20f + Vector2.UnitY * 600f, Vector2.One);
			mST.AddString(mFonts[0], "Stuffs", "LevelStatus",
				color, Vector2.UnitX * 20f + Vector2.UnitY * 620f, Vector2.One);
			mST.AddString(mFonts[0], "Stuffs", "PosStatus",
				color, Vector2.UnitX * 20f + Vector2.UnitY * 640f, Vector2.One);
			mST.AddString(mFonts[0], "(G), (H) to clear:  Dynamic Lights: 0", "DynStatus",
				color, Vector2.UnitX * 20f + Vector2.UnitY * 660f, Vector2.One);

			mZoneMats.InitCelShading(1);
//			mZoneMats.GenerateCelTexturePreset(gd.GD,
//				gd.GD.FeatureLevel == FeatureLevel.Level_9_3, false, 0);
//			mZoneMats.SetCelTexture(0);

			float	[]thresholds	=new float[3 - 1];
			float	[]levels		=new float[3];
			
			thresholds[0]	=0.7f;
			thresholds[1]	=0.3f;

			levels[0]	=1;
			levels[1]	=0.8f;
			levels[2]	=0.5f;

			mZoneMats.GenerateCelTexture(gd.GD,
				gd.GD.FeatureLevel == SharpDX.Direct3D.FeatureLevel.Level_9_3,
				0, 256, thresholds, levels);
			mZoneMats.SetCelTexture(0);

			mZoneDraw	=new IndoorMesh(gd, mZoneMats);

			mAudio.LoadAllSounds(mGameRootDir + "\\Audio\\SoundFX");

			//set up post processing module
			mPost	=new PostProcess(gd, mZoneMats, "Post.fx");

#if true
			mPost.MakePostTarget(gd, "SceneColor", mResX, mResY, Format.R16G16B16A16_Float);
			mPost.MakePostDepth(gd, "SceneDepth", mResX, mResY,
				(gd.GD.FeatureLevel != FeatureLevel.Level_9_3)?
					Format.D32_Float_S8X24_UInt : Format.D24_UNorm_S8_UInt);
			mPost.MakePostTarget(gd, "SceneDepthMatNorm", mResX, mResY, Format.R16G16B16A16_Float);
			mPost.MakePostTarget(gd, "Bleach", mResX, mResY, Format.R16G16B16A16_Float);
			mPost.MakePostTarget(gd, "Outline", mResX, mResY, Format.R16G16B16A16_Float);
			mPost.MakePostTargetHalfRes(gd, "Bloom1", mResX/2, mResY/2, Format.R16G16B16A16_Float);
			mPost.MakePostTargetHalfRes(gd, "Bloom2", mResX/2, mResY/2, Format.R16G16B16A16_Float);
#elif ThirtyTwo
			mPost.MakePostTarget(gd, "SceneColor", mResX, mResY, Format.R8G8B8A8_UNorm);
			mPost.MakePostDepth(gd, "SceneDepth", mResX, mResY,
				(gd.GD.FeatureLevel != FeatureLevel.Level_9_3)?
					Format.D32_Float_S8X24_UInt : Format.D24_UNorm_S8_UInt);
			mPost.MakePostTarget(gd, "SceneDepthMatNorm", mResX, mResY, Format.R16G16B16A16_Float);
			mPost.MakePostTarget(gd, "Bleach", mResX, mResY, Format.R8G8B8A8_UNorm);
			mPost.MakePostTarget(gd, "Outline", mResX, mResY, Format.R8G8B8A8_UNorm);
			mPost.MakePostTarget(gd, "Bloom1", mResX/2, mResY/2, Format.R8G8B8A8_UNorm);
			mPost.MakePostTarget(gd, "Bloom2", mResX/2, mResY/2, Format.R8G8B8A8_UNorm);
#else
			mPost.MakePostTarget(gd, "SceneColor", mResX, mResY, Format.B5G5R5A1_UNorm);
			mPost.MakePostDepth(gd, "SceneDepth", mResX, mResY,
				(gd.GD.FeatureLevel != FeatureLevel.Level_9_3)?
					Format.D32_Float_S8X24_UInt : Format.D24_UNorm_S8_UInt);
			mPost.MakePostTarget(gd, "SceneDepthMatNorm", mResX, mResY, Format.R16G16B16A16_Float);
			mPost.MakePostTarget(gd, "Bleach", mResX, mResY, Format.B5G5R5A1_UNorm);
			mPost.MakePostTarget(gd, "Outline", mResX, mResY, Format.B5G5R5A1_UNorm);
			mPost.MakePostTarget(gd, "Bloom1", mResX/2, mResY/2, Format.B5G5R5A1_UNorm);
			mPost.MakePostTarget(gd, "Bloom2", mResX/2, mResY/2, Format.B5G5R5A1_UNorm);
#endif

			if(gd.GD.FeatureLevel != FeatureLevel.Level_9_3)
			{
				mDynLights	=new DynamicLights(mGD, mZoneMats, "BSP.fx");
			}

			//see if any static stuff
			if(Directory.Exists(mGameRootDir + "/Statics"))
			{
				DirectoryInfo	di	=new DirectoryInfo(mGameRootDir + "/Statics");

				FileInfo[]	fi	=di.GetFiles("*.MatLib", SearchOption.TopDirectoryOnly);

				if(fi.Length > 0)
				{
					mStaticMats	=new MatLib(gd, mSKeeper);
					mStaticMats.ReadFromFile(fi[0].DirectoryName + "\\" + fi[0].Name);

					mStaticMats.InitCelShading(1);
					mStaticMats.GenerateCelTexturePreset(gd.GD,
						(gd.GD.FeatureLevel == FeatureLevel.Level_9_3),
						true, 0);
					mStaticMats.SetCelTexture(0);
				}
				mStatics	=Mesh.LoadAllStaticMeshes(mGameRootDir + "\\Statics", gd.GD);

				//gen bounds, they don't seem to save correctly
				foreach(KeyValuePair<string, IArch> ia in mStatics)
				{
					ia.Value.UpdateBounds();
				}
			}

			//load character stuff if any around
			if(Directory.Exists(mGameRootDir + "/Characters"))
			{
				DirectoryInfo	di	=new DirectoryInfo(mGameRootDir + "/Characters");

				FileInfo[]	fi	=di.GetFiles("*.AnimLib", SearchOption.TopDirectoryOnly);
				if(fi.Length > 0)
				{
					mPAnims	=new AnimLib();
					mPAnims.ReadFromFile(fi[0].DirectoryName + "\\" + fi[0].Name);

					List<Anim>	anims	=mPAnims.GetAnims();
					foreach(Anim a in anims)
					{
						mAnims.Add(a.Name);
					}
				}

				fi	=di.GetFiles("*.MatLib", SearchOption.TopDirectoryOnly);
				if(fi.Length > 0)
				{
					mPMats	=new MatLib(mGD, mSKeeper);
					mPMats.ReadFromFile(fi[0].DirectoryName + "\\" + fi[0].Name);
					mPMats.InitCelShading(1);
					mPMats.GenerateCelTexturePreset(gd.GD,
						gd.GD.FeatureLevel == FeatureLevel.Level_9_3, false, 0);
					mPMats.SetCelTexture(0);
				}

				fi	=di.GetFiles("*.Character", SearchOption.TopDirectoryOnly);
				if(fi.Length > 0)
				{
					mPArch	=new CharacterArch();
					mPArch.ReadFromFile(fi[0].DirectoryName + "\\" + fi[0].Name, mGD.GD, false);
				}

				fi	=di.GetFiles("*.CharacterInstance", SearchOption.TopDirectoryOnly);
				if(fi.Length > 0)
				{
					mPChar	=new Character(mPArch, mPAnims);
					mPChar.ReadFromFile(fi[0].DirectoryName + "\\" + fi[0].Name);
					mPChar.SetMatLib(mPMats);

					mPShad	=new ShadowHelper.Shadower();

					mPShad.mChar	=mPChar;
					mPShad.mContext	=this;

					mPEntity	=new Entity(true, mEBoss);

					mPMeshLighting	=new MeshLighting(mPEntity, mZone, PlayerBoxStanding / 2f, mZoneDraw.GetStyleStrength);

					mPEntity.AddComponent(mPMeshLighting);
				}
			}

			mPMob		=new Mobile(mPChar, PlayerBoxWidth, PlayerBoxStanding, PlayerEyeStanding, true);
			mPCamMob	=new Mobile(mPChar, PlayerBoxWidth, PlayerBoxStanding, PlayerEyeStanding, true);
			mFatBox		=Misc.MakeBox(PlayerBoxWidth + 1, PlayerBoxStanding);

			mKeeper.AddLib(mZoneMats);

			if(mStaticMats != null)
			{
				mKeeper.AddLib(mStaticMats);
			}

			if(mPMats != null)
			{
				mKeeper.AddLib(mPMats);
			}

			//example material groups
			//these treat all materials in the group
			//as a single material for the purposes
			//of drawing cartoony outlines around them
			List<string>	skinMats	=new List<string>();

			skinMats.Add("Face");
			skinMats.Add("Skin");
			skinMats.Add("EyeWhite");
			skinMats.Add("EyeLiner");
			skinMats.Add("LeftIris");
			skinMats.Add("LeftPupil");
			skinMats.Add("RightIris");
			skinMats.Add("RightPupil");
//			skinMats.Add("Nails");
			mKeeper.AddMaterialGroup("SkinGroup", skinMats);

			if(Directory.Exists(mGameRootDir + "/Levels"))
			{
				DirectoryInfo	di	=new DirectoryInfo(mGameRootDir + "/Levels");

				FileInfo[]	fi	=di.GetFiles("*.Zone", SearchOption.TopDirectoryOnly);
				foreach(FileInfo f in fi)
				{
					mLevels.Add(f.Name.Substring(0, f.Name.Length - 5));
				}
			}

			//if debugger lands here, levels are sort of needed
			//otherwise there's not much point for this test prog
			ChangeLevel(mLevels[mCurLevel]);
			mST.ModifyStringText(mFonts[0], "(L) CurLevel: " + mLevels[mCurLevel], "LevelStatus");
		}


		//if running on a fixed timestep, this might be called
		//more often with a smaller delta time than RenderUpdate()
		internal void Update(UpdateTimer time, List<Input.InputAction> actions, PlayerSteering ps)
		{
			//Thread.Sleep(30);

			float	secDelta	=time.GetUpdateDeltaSeconds();

			mZone.ClearPushableVelocities(secDelta);

			//update model movers
			foreach(Component c in mBModelMovers)
			{
				c.Update(time);
			}

			Vector3	pos			=Vector3.Zero;
			bool	bGroundMove	=false;

			UInt32	contents	=mPCamMob.GetWorldContents();
			if(mbFly)
			{
				pos	=UpdateFly(secDelta, actions, ps);
			}
			else if(Misc.bFlagSet(contents, (UInt32) GameContents.Lava)
				|| Misc.bFlagSet(contents, (UInt32) GameContents.Water)
				|| Misc.bFlagSet(contents, (UInt32) GameContents.Slime))
			{
				pos	=UpdateSwimming(secDelta, actions, ps);
			}
			else
			{
				pos	=UpdateGround(secDelta, actions, ps, out bGroundMove);

				//flip ground move as it returns as a jump bool
				bGroundMove	=!bGroundMove;
			}

			UpdateMiscKeys(actions, ps);
			UpdateDynamicLights(actions);

			Vector3	camPos	=Vector3.Zero;
			Vector3	endPos	=pos;
			float	msDelta	=time.GetUpdateDeltaMilliSeconds();

			mPCamMob.Move(endPos, msDelta, false, mbFly,
				bGroundMove, true, out endPos, out camPos);

			//check resulting move against triggers / pickups
			if(!mbFly)
			{
				foreach(Trigger t in mTriggers)
				{
					t.BoxTriggerCheck(mPCamMob, mPCamMob.GetBounds(),
						pos, endPos, msDelta);
				}
				foreach(ConvexVolume cv in mPickUpCVs)
				{
					if(!cv.Active)
					{
						continue;
					}
					if(cv.SphereMotionIntersects(PlayerBoxWidth, pos, endPos))
					{
						PickUpThing(cv);
						cv.StateChange(ConvexVolume.State.Active, 0);
					}
				}
			}


			//check a slightly expanded box to see if any interactives are being touched
			mModelsHit.Clear();
			if(!mbFly)
			{
				if(mZone.TraceStaticBoxVsModels(mFatBox, endPos, mModelsHit))
				{
					//do stuff
					foreach(int model in mModelsHit)
					{
						foreach(BModelMover bmm in mBModelMovers)
						{
							if(bmm.GetModelIndex() == model)
							{
								bmm.StateChange(BModelMover.States.Forward, 1);
								bmm.StateChange(BModelMover.States.Moving, 1);
							}
						}
					}
				}
			}

			//check pvs against entities
			mVisibleSMC.Clear();
			foreach(StaticMeshComp smc in mStaticComps)
			{
				Vector3	smPos	=smc.mMat.TranslationVector;

				if(mZone.IsVisibleFrom(endPos, smPos))
				{
					mVisibleSMC.Add(smc);

					//update the entity
					smc.mOwner.Update(time);
				}
			}
			
			mGD.GCam.Update(camPos, ps.Pitch, ps.Yaw, ps.Roll);

			if(!mbFly)
			{
				if(mPCamMob.IsOnGround())
				{
					//kill downward velocity so previous
					//falling momentum doesn't contribute to
					//a new jump
					if(mCamVelocity.Y < 0f)
					{
						mCamVelocity.Y	=0f;
					}
				}
				if(mPCamMob.IsBadFooting())
				{
					//reduce downward velocity to avoid
					//getting stuck in V shaped floors
					if(mCamVelocity.Y < 0f)
					{
						mCamVelocity.Y	-=(StumbleFriction * mCamVelocity.Y * secDelta);
					}
				}
			}

			mPB.Update(mGD.DC, time.GetUpdateDeltaMilliSeconds());

			mAudio.Update(mGD.GCam);

			mST.ModifyStringText(mFonts[0], "ModelOn: " + mPCamMob.GetModelOn() + " : "
				+ (int)mGD.GCam.Position.X + ", "
				+ (int)mGD.GCam.Position.Y + ", "
				+ (int)mGD.GCam.Position.Z + " (F)lyMode: " + mbFly
				+ (mPCamMob.IsBadFooting()? " BadFooting!" : "")
				+ " ModelsHit: " + mModelsHit.Count, "PosStatus");

			mST.Update(mGD.DC);
			mUI.Update(mGD.DC);
		}


		//called once before render with accumulated delta
		//do all once per render style updates in here
		internal void RenderUpdate(float msDelta)
		{
			if(msDelta <= 0f)
			{
				return;	//can happen if fixed time and no remainder
			}

			mZoneDraw.Update(msDelta);

			mZoneMats.UpdateWVP(Matrix.Identity, mGD.GCam.View, mGD.GCam.Projection, mGD.GCam.Position);

			if(mStaticMats !=null)
			{
				mStaticMats.UpdateWVP(Matrix.Identity, mGD.GCam.View, mGD.GCam.Projection, mGD.GCam.Position);
			}
			if(mPMats != null)
			{
				mPMats.UpdateWVP(Matrix.Identity, mGD.GCam.View, mGD.GCam.Projection, mGD.GCam.Position);
			}
		}


		internal void Render()
		{
			mPost.SetTargets(mGD, "SceneDepthMatNorm", "SceneDepth");

			mPost.ClearTarget(mGD, "SceneDepthMatNorm", Color.White);
			mPost.ClearDepth(mGD, "SceneDepth");

			mZoneDraw.DrawDMN(mGD, mZone.IsMaterialVisibleFromPos,
				mZone.GetModelTransform, RenderExternalDMN);

			mPost.SetTargets(mGD, "SceneColor", "SceneDepth");

			mPost.ClearTarget(mGD, "SceneColor", Color.CornflowerBlue);
			mPost.ClearDepth(mGD, "SceneDepth");

			if(mDynLights != null)
			{
				mDynLights.SetParameter();
			}

			mZoneDraw.Draw(mGD, mShadowHelper.GetShadowCount(),
				mZone.IsMaterialVisibleFromPos,
				mZone.GetModelTransform,
				RenderExternal,
				mShadowHelper.DrawShadows,
				SetUpAlphaRenderTargets);

			mPost.SetTargets(mGD, "Outline", "null");
			mPost.SetParameter("mNormalTex", "SceneDepthMatNorm");
			mPost.DrawStage(mGD, "Outline");

			mPost.SetTargets(mGD, "Bloom1", "null");
			mPost.SetParameter("mBlurTargetTex", "SceneColor");
			mPost.DrawStage(mGD, "BloomExtract");

			mPost.SetTargets(mGD, "Bloom2", "null");
			mPost.SetParameter("mBlurTargetTex", "Bloom1");
			mPost.DrawStage(mGD, "GaussianBlurX");

			mPost.SetTargets(mGD, "Bloom1", "null");
			mPost.SetParameter("mBlurTargetTex", "Bloom2");
			mPost.DrawStage(mGD, "GaussianBlurY");

			mPost.SetTargets(mGD, "Bleach", "null");
			mPost.SetParameter("mBlurTargetTex", "Bloom1");
			mPost.SetParameter("mColorTex", "SceneColor");
			mPost.DrawStage(mGD, "BloomCombine");

			mPost.SetTargets(mGD, "BackColor", "BackDepth");
			mPost.SetParameter("mBlurTargetTex", "Outline");
			mPost.SetParameter("mColorTex", "Bleach");
			mPost.DrawStage(mGD, "Modulate");

			mUI.Draw(mGD.DC, Matrix.Identity, mTextProj);
			mST.Draw(mGD.DC, Matrix.Identity, mTextProj);
		}


		internal void RenderNoPost()
		{
			if(mDynLights != null)
			{
				mDynLights.SetParameter();
			}

			mPost.SetTargets(mGD, "BackColor", "BackDepth");
			mPost.ClearTarget(mGD, "BackColor", Color.CornflowerBlue);
			mPost.ClearDepth(mGD, "BackDepth");

			mZoneDraw.Draw(mGD, mShadowHelper.GetShadowCount(),
				mZone.IsMaterialVisibleFromPos,
				mZone.GetModelTransform,
				RenderExternal,
				mShadowHelper.DrawShadows,
				SetUpAlphaRenderTargetsNoPost);

			mST.Draw(mGD.DC, Matrix.Identity, mTextProj);
		}


		internal void FreeAll()
		{
			FreeLevelData();

			mShadowHelper.FreeAll();
			mPost.FreeAll(mGD);
			mFontMats.FreeAll();
			mZoneMats.FreeAll();
			mKeeper.Clear();
			if(mStaticMats != null)
			{
				mStaticMats.FreeAll();
			}
			if(mPMats != null)
			{
				mPMats.FreeAll();
			}
			if(mPChar != null)
			{
				mPChar.FreeAll();
			}
			mPartMats.FreeAll();

			if(mPAnims != null)
			{
				mPArch.FreeAll();
			}

			if(mDynLights != null)
			{
				mDynLights.FreeAll();
			}

			foreach(KeyValuePair<string, IArch> stat in mStatics)
			{
				stat.Value.FreeAll();
			}
			mStatics.Clear();

			mAudio.FreeAll();

			mSKeeper.FreeAll();
		}


		void FreeLevelData()
		{
			foreach(KeyValuePair<StaticMeshComp, ShadowHelper.Shadower> sh in mStaticShads)
			{
				mShadowHelper.UnRegisterShadower(sh.Value);
			}
			mStaticShads.Clear();

			mZone.FreeAll();
			mZoneDraw.FreeAll();
			mPB.FreeAll();
			mZoneMats.FreeAll();

			if(mBModelMovers != null)
			{
				mBModelMovers.Clear();
				mTriggers.Clear();
				mPickUpCVs.Clear();
				mStaticComps.Clear();
			}

			mEBoss.FreeAll();
		}


		void SetUpAlphaRenderTargets()
		{
			//shadows will mess with targets
			mPost.SetTargets(mGD, "SceneColor", "SceneDepth");
		}


		void SetUpAlphaRenderTargetsNoPost()
		{
			//shadows will mess with targets
			mPost.SetTargets(mGD, "BackColor", "BackDepth");
		}


		void RenderExternalDMN(GameCamera gcam)
		{
			foreach(StaticMeshComp smc in mStaticComps)
			{
				StaticMesh	sm	=smc.mDrawObject as StaticMesh;
				if(sm == null)
				{
					continue;
				}
				if(mVisibleSMC.Contains(smc))
				{
					sm.DrawDMN(mGD.DC, mStaticMats);
				}

			}

			if(mPChar != null)
			{
				mPChar.DrawDMN(mGD.DC, mPMats);
			}

			mPB.DrawDMN(mGD.DC, gcam.View, gcam.Projection, gcam.Position);
		}


		void SetTriLightForSMC(StaticMeshComp smc)
		{
			MeshLighting	ml	=smc.mOwner.GetComponent(
				typeof(MeshLighting)) as MeshLighting;
			StaticMesh	sm	=smc.mDrawObject as StaticMesh;
			
			if(ml == null || sm == null)
			{
				return;
			}

			Vector4	lightCol0, lightCol1, lightCol2;
			Vector3	lightPos, lightDir;
			bool	bDir;
			float	intensity;

			ml.GetCurrentValues(
				out lightCol0, out lightCol1, out lightCol2,
				out intensity, out lightPos, out lightDir, out bDir);

			sm.SetTriLightValues(lightCol0, lightCol1, lightCol2, lightDir);
		}


		void RenderExternal(AlphaPool ap, GameCamera gcam)
		{
			foreach(StaticMeshComp smc in mStaticComps)
			{
				StaticMesh	sm	=smc.mDrawObject as StaticMesh;
				if(sm == null)
				{
					continue;
				}

				if(!mVisibleSMC.Contains(smc))
				{
					continue;
				}

				SetTriLightForSMC(smc);

				sm.SetTransform(smc.mMat);
				sm.Draw(mGD.DC, mStaticMats);
			}

			if(mPMeshLighting == null)
			{
				return;
			}

			Vector4	lightCol0, lightCol1, lightCol2;
			Vector3	lightPos, lightDir;
			bool	bDir;
			float	intensity;

			mPMeshLighting.GetCurrentValues(
				out lightCol0, out lightCol1, out lightCol2,
				out intensity, out lightPos, out lightDir, out bDir);

			if(mPChar != null)
			{
				mPMats.SetTriLightValues(lightCol0, lightCol1, lightCol2, lightDir);
				mPChar.Draw(mGD.DC, mPMats);
			}

			mPB.Draw(ap, gcam.View, gcam.Projection);
		}


		void ChangeLevel(string level)
		{
			UpdateTimer	fakeUT	=new UpdateTimer(false, false);
			fakeUT.Stamp();

			string	lev	=mGameRootDir + "/Levels/" + level;

			FreeLevelData();

			mZone	=new Zone();

			mZoneMats.ReadFromFile(lev + ".MatLib");
			mZone.Read(lev + ".Zone", false);
			mZoneDraw.Read(mGD, mSKeeper, lev + ".ZoneDraw", false);

			//for less state changes
			mZoneMats.FinalizeMaterials();

			mZoneMats.SetLightMapsToAtlas();

			QuakeTranslator	qtrans	=new QuakeTranslator();

			qtrans.TranslateModels(mEBoss, mZone);
			qtrans.TranslateTriggers(mEBoss, mZone);
			qtrans.TranslateLights(mEBoss, mZone, mZoneDraw.SwitchLight);
			qtrans.TranslateItems(mEBoss, mZone, GetDrawObject);
			qtrans.TranslateWeapons(mEBoss, mZone, GetDrawObject);
			fakeUT.Stamp();

			//grab the model movers
			mBModelMovers	=mEBoss.GetEntityComponents(typeof(BModelMover));

			//grab triggers
			mTriggers	=mEBoss.GetEntityComponents(typeof(Trigger));

			//grab convex volumes for pickups
			mPickUpCVs	=mEBoss.GetEntityComponents(typeof(ConvexVolume));

			mStaticComps	=mEBoss.GetEntityComponents(typeof(StaticMeshComp));

			//make meshlighting for statics
			foreach(StaticMeshComp smc in mStaticComps)
			{
				StaticMesh	sm	=smc.mDrawObject as StaticMesh;

				float	baseToMiddle	=sm.GetBoxBound().Height / 2;

				MeshLighting	ml	=new MeshLighting(smc.mOwner, mZone, baseToMiddle, mZoneDraw.GetStyleStrength);

				smc.mOwner.AddComponent(ml);
			}

			mPHelper.Initialize(mZone, mPB);
			mIMHelper.Initialize(mZone);

			List<ZoneEntity>	wEnt	=mZone.GetEntities("worldspawn");
			Debug.Assert(wEnt.Count == 1);

			float	mDirShadowAtten;
			string	ssa	=wEnt[0].GetValue("SunShadowAtten");
			if(!Single.TryParse(ssa, out mDirShadowAtten))
			{
				mDirShadowAtten	=200f;	//default
			}

			mShadowHelper.Initialize(mGD, 512, mDirShadowAtten,
				mZoneMats, mPost, GetCurShadowLightInfo, GetTransdBounds);

//			mGraph.Load(lev + ".Pathing");
//			mGraph.GenerateGraph(mZone.GetWalkableFaces, 32, 18f, CanPathReach);
//			mGraph.Save(mLevels[index] + ".Pathing");
//			mGraph.BuildDrawInfo(gd);

//			mPathMobile.SetZone(mZone);

			mPMob.SetZone(mZone);
			mPCamMob.SetZone(mZone);

			MakeStaticShadowers();

			float	ang;
			Vector3	gpos	=mZone.GetPlayerStartPos(out ang);
			mPMob.SetGroundPos(gpos);
			mPCamMob.SetGroundPos(gpos);

			mKeeper.Scan();
			mKeeper.AssignIDsToEffectMaterials("BSP.fx");

			if(mPChar != null)
			{
				mShadowHelper.RegisterShadower(mPShad, mPMats);
				mPChar.AssignMaterialIDs(mKeeper);
			}

			foreach(StaticMeshComp smc in mStaticComps)
			{
				StaticMesh	sm	=smc.mDrawObject as StaticMesh;
				if(sm == null)
				{
					continue;
				}
				sm.AssignMaterialIDs(mKeeper);
			}

			//update entities once
			//holy crap this is slow
			mEBoss.Update(fakeUT);
		}


		//grab static mesh instances for entities
		void GetDrawObject(string archPath, string instPath, out object draw, out BoundingBox box)
		{
			draw	=null;
			box		=mPMob.GetBounds();	//whateva

			if(archPath == null || archPath == "")
			{
				return;
			}
			if(instPath == null || instPath == "")
			{
				return;
			}

			if(!mStatics.ContainsKey(archPath))
			{
				return;
			}

			StaticMesh	sm	=new StaticMesh(mStatics[archPath]);

			sm.ReadFromFile(mGameRootDir + "\\Statics\\" + instPath);
			sm.SetMatLib(mStaticMats);

			draw	=sm;
			box		=sm.GetBoxBound();

			if(box.Size.IsZero)
			{
				//no bounds saved in the mesh?  Gen
				sm.UpdateBounds();
				box	=sm.GetBoxBound();
			}
		}


		void UpdateDynamicLights(List<Input.InputAction> actions)
		{
			if(mDynLights == null)
			{
				return;
			}
			foreach(Input.InputAction act in actions)
			{
				/*
				if(act.mAction.Equals(Program.MyActions.PlaceDynamicLight))
				{
					int	id;
					mDynLights.CreateDynamicLight(mGD.GCam.Position,
						Mathery.RandomColorVector(mRand),
						300, out id);
					mActiveLights.Add(id);
					mST.ModifyStringText(mFonts[0], "(G), (H) to clear:  Dynamic Lights: "
						+ mActiveLights.Count, "DynStatus");
				}
				else if(act.mAction.Equals(Program.MyActions.ClearDynamicLights))
				{
					foreach(int id in mActiveLights)
					{
						mDynLights.Destroy(id);
					}
					mActiveLights.Clear();
					mST.ModifyStringText(mFonts[0], "(G), (H) to clear:  Dynamic Lights: 0", "DynStatus");
				}*/
			}

			mDynLights.Update(mGD);
		}


		BoundingBox GetTransdBounds(ShadowHelper.Shadower shadower)
		{
			if(shadower.mChar != mPChar)
			{
				return	new BoundingBox();
			}

			BoundingBox	ret	=mPMob.GetTransformedBound();

			//add a little bit to account for a bit of sloppiness
			ret.Maximum	+=Vector3.One * ShadowSlop;
			ret.Minimum	-=Vector3.One * ShadowSlop;

			return	ret;
		}


		bool GetCurShadowLightInfo(ShadowHelper.Shadower shadower,
			out Matrix shadowerTransform,
			out float intensity, out Vector3 lightPos,
			out Vector3 lightDir, out bool bDirectional)
		{
			Vector4	col0, col1, col2;

			if(shadower.mContext is StaticMeshComp)
			{
				StaticMeshComp	smc=shadower.mContext as StaticMeshComp;

				shadowerTransform	=smc.mMat;

				if(!mVisibleSMC.Contains(smc))
				{
					intensity		=0f;
					lightPos		=lightDir	=Vector3.Zero;
					bDirectional	=true;
					return	false;
				}

				MeshLighting	ml	=smc.mOwner.GetComponent(
					typeof(MeshLighting)) as MeshLighting;

				if(ml == null || !ml.NeedsShadow())
				{
					intensity		=0f;
					lightPos		=lightDir	=Vector3.Zero;
					bDirectional	=true;
					return	false;
				}

				ml.GetCurrentValues(out col0, out col1, out col2,
						out intensity, out lightPos, out lightDir, out bDirectional);
				return	true;
			}

			if(shadower.mChar == mPChar)
			{
				shadowerTransform	=mPChar.GetTransform();
			}
			else
			{
				intensity			=0f;
				lightPos			=Vector3.Zero;
				lightDir			=Vector3.Zero;
				bDirectional		=false;
				shadowerTransform	=Matrix.Identity;

				return	false;
			}

			return	mPMeshLighting.GetCurrentValues(out col0, out col1, out col2,
				out intensity, out lightPos, out lightDir, out bDirectional);
		}


		void MakeStaticShadowers()
		{
			foreach(StaticMeshComp smc in mStaticComps)
			{
				ShadowHelper.Shadower	shad	=new ShadowHelper.Shadower();

				shad.mChar		=null;
				shad.mStatic	=smc.mDrawObject as StaticMesh;
				shad.mContext	=smc;

				mStaticShads.Add(smc, shad);

				mShadowHelper.RegisterShadower(shad, mStaticMats);
			}
		}


		void PickUpThing(ConvexVolume cv)
		{
			PickUp	pu	=cv.mOwner.GetComponent(typeof(PickUp)) as PickUp;

			pu.StateChange(PickUp.State.WaitingRespawn, 1);

			StaticMeshComp	smc	=pu.mOwner.GetComponent(typeof(StaticMeshComp)) as StaticMeshComp;

			StaticMesh	sm	=smc.mDrawObject as StaticMesh;

			int	numParts	=sm.GetNumParts();
			for(int i=0;i < numParts;i++)
			{
				sm.SetPartVisible(i, false);
			}
		}


		void MakeHealthManaGumps()
		{
			mUIMats	=new MatLib(mGD, mSKeeper);
			mUIMats.CreateMaterial("Text");
			mUIMats.SetMaterialEffect("Text", "2D.fx");
			mUIMats.SetMaterialTechnique("Text", "Text");

			mUI	=new ScreenUI(mGD.GD, mUIMats, 16);

			Vector4	healthColour	=Vector4.UnitX + Vector4.UnitW;
			Vector4	manaColour		=Vector4.UnitZ + Vector4.UnitW;

			mUI.AddGump("UI\\HMBubble", "HealthBubble", healthColour,
				Vector2.UnitX * 20f + Vector2.UnitY * 520f,
				Vector2.One * 1.5f);

			mUI.AddGump("UI\\HMBubble", "ManaBubble", manaColour,
				Vector2.UnitX * 1070f + Vector2.UnitY * 520f,
				Vector2.One * 1.5f);
		}


		void SpawnTestParticles(Vector3 pos)
		{
			mTestEmitters.Add(
				mPB.CreateEmitter("Particles\\HeartPart",
					new Vector4(0f, 1f, 0f, 0.25f),
					Emitter.Shapes.Sphere, 8f, 100,
					mGD.GCam.Position, Vector3.Up * 100f,
					22f, 2.6f, 0.04f, -3f/1000f, 3f/1000f,
					1.5f/1000f, 4f/1000f, 30f, 0.15f/1000f, 0.25f/1000f,
					new Vector4(5f, -2f, 0f, 1f)/10000f,
					new Vector4(7f, -3f, 0f, 1f)/10000f,
					3000, 4000));
		}
	}
}
