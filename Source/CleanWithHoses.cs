using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace TKS_CleanWithHoses
{

	[DefOf]
	public class TKS_Def
    {
		public static EffecterDef HoseCleaningEffect;
		public static EffecterDef Clean_Inside;
	}

	[StaticConstructorOnStartup]
	public static class InsertHarmony
	{
		static InsertHarmony()
		{
			Harmony harmony = new Harmony("TKS_CleanWithHoses");
			//Harmony.DEBUG = true;
			harmony.PatchAll();
			//Harmony.DEBUG = false;
			Log.Message("TKS_CleanWithHoses: Patching finished");
		}
	}

	[HarmonyPatch(typeof(JobDriver_CleanFilth))]
	public static class Toils_Patches
	{
		[HarmonyPatch(typeof(JobDriver_CleanFilth), "MakeNewToils")]
		[HarmonyPostfix]
		static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_CleanFilth __instance)
        {
			Pawn pawn = __instance.pawn;

			Toil initExtractTargetFromQueue = Toils_JobTransforms.ClearDespawnedNullOrForbiddenQueuedTargets(TargetIndex.A, null);

			int i = 0;
			foreach (Toil toil in toils)
            {
				if (i == 0)
                {
					initExtractTargetFromQueue = toil;
                }
				//Log.Message("[TKS_CleanWithHoses]"+pawn.Name+" Found " + toil.ToString());
				if (toil.ToString()=="MakeNewToils")
                {
					//Log.Message("[TKS_CleanWithHoses]" + pawn.Name + " Found cleaning toil & replacing");

					PropertyInfo filthProperty = typeof(JobDriver_CleanFilth).GetProperty("Filth", BindingFlags.NonPublic | BindingFlags.Instance);

					FieldInfo cleaningWorkDoneField = typeof(JobDriver_CleanFilth).GetField("cleaningWorkDone", BindingFlags.NonPublic | BindingFlags.Instance);
					FieldInfo totalCleaningWorkDoneField = typeof(JobDriver_CleanFilth).GetField("totalCleaningWorkDone", BindingFlags.NonPublic | BindingFlags.Instance);
					FieldInfo totalCleaningWorkRequiredField = typeof(JobDriver_CleanFilth).GetField("totalCleaningWorkRequired", BindingFlags.NonPublic | BindingFlags.Instance);

					//unfortunately the best way (I know how to do) is to re-create the whole delegate here and do the check during it
					//the Filth object is only the actual target during the delegate itself, so there's no way to save the time here
					Toil clean = ToilMaker.MakeToil("MakeNewToils");
	
					clean.initAction = delegate ()
					{
						Filth filth = (Filth)filthProperty.GetValue(__instance);
						//Log.Message("[TKS_CleanWithHoses]:" + pawn.Name + " running initAction on " + filth.def.defName);
						cleaningWorkDoneField.SetValue(__instance, 0f);
						totalCleaningWorkDoneField.SetValue(__instance, 0f);
						float totalCleaningWorkRequired = 0f;
						totalCleaningWorkRequiredField.SetValue(__instance,  totalCleaningWorkRequired);
						if (filth.GetRoom() != null && filth.GetRoom().OutdoorsForWork)
						{
							clean.WithEffect(TKS_Def.HoseCleaningEffect, TargetIndex.A, null);
						}
						else
						{
							clean.WithEffect(TKS_Def.Clean_Inside, TargetIndex.A, null);
						}

						//clean.WithProgressBar(TargetIndex.A, () => (float)totalCleaningWorkDoneField.GetValue(__instance) / (float)totalCleaningWorkRequiredField.GetValue(__instance), true, -0.5f, false);

					};
					Action outdoorCleanAction = delegate ()
					{
						Filth filth = (Filth)filthProperty.GetValue(__instance);
						//Log.Message("[TKS_CleanWithHoses]:" + pawn.Name + " running tickAction on " + filth.def.defName);
						float statValueAbstract = filth.Position.GetTerrain(filth.Map).GetStatValueAbstract(StatDefOf.CleaningTimeFactor, null);
						float num = __instance.pawn.GetStatValue(StatDefOf.CleaningSpeed, true, -1);
						if (statValueAbstract != 0f)
						{
							num /= statValueAbstract;
						}

						//Log.Message("[TKS_CleanWithHoses]:" + pawn.Name + " checking location of " + filth.def.defName);

						//faster outdoors
						if (filth.GetRoom() != null && filth.GetRoom().OutdoorsForWork)
						{
							num *= 3;
						}
						
						float cleaningWorkDone = (float)cleaningWorkDoneField.GetValue(__instance);
						float totalCleaningWorkDone = (float)totalCleaningWorkDoneField.GetValue(__instance);
						cleaningWorkDone += num;
						totalCleaningWorkDone += num;
						cleaningWorkDoneField.SetValue(__instance, cleaningWorkDone);
						totalCleaningWorkDoneField.SetValue(__instance, totalCleaningWorkDone);

						//Log.Message("[TKS_CleanWithHoses]:" + pawn.Name + " checking for filth destroy");

						if (cleaningWorkDone > filth.def.filth.cleaningWorkToReduceThickness)
						{
							filth.ThinFilth();
							cleaningWorkDone = 0f;
							cleaningWorkDoneField.SetValue(__instance, cleaningWorkDone);
							if (filth.Destroyed)
							{
								//Log.Message("[TKS_CleanWithHoses]:" + pawn.Name + " filth destroyed.");
								clean.actor.records.Increment(RecordDefOf.MessesCleaned);
								__instance.ReadyForNextToil();

								return;
							}
						}
						//Log.Message("[TKS_CleanWithHoses]:" + pawn.Name + " tick Action continuing...");

					};

					clean.tickAction = outdoorCleanAction;
					clean.defaultCompleteMode = ToilCompleteMode.Never;
					//clean.WithEffect(EffecterDefOf.Clean, TargetIndex.A, null);

					clean.PlaySustainerOrSound(delegate ()
					{
						Filth filth = (Filth)filthProperty.GetValue(__instance);
						if (filth != null)
						{
							ThingDef def = filth.def;
							if (!def.filth.cleaningSound.NullOrUndefined())
							{
								return def.filth.cleaningSound;
							}
						}
						return SoundDefOf.Interact_CleanFilth;
					}, 1f);
					
					clean.JumpIfDespawnedOrNullOrForbidden(TargetIndex.A, initExtractTargetFromQueue);
					clean.JumpIfOutsideHomeArea(TargetIndex.A, initExtractTargetFromQueue);

					//Log.Message("[TKS_CleanWithHoses]:" + pawn.Name + " returning " + clean.debugName);
					yield return clean;
				} else
                {
					//Log.Message("[TKS_CleanWithHoses]:" + pawn.Name + " returning " + toil.debugName);
					yield return toil;
				}

				i += 1;
				
            }
        }
    }
		
}
