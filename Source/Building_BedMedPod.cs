﻿using System.Collections.Generic;
using System.Text;
using Verse;
using RimWorld;
using UnityEngine;
using System.Linq;
using System;

namespace MedPod
{
    class Building_BedMedPod : Building_Bed
    {
        public CompPowerTrader powerComp;

        private List<Hediff> patientTreatableHediffs;

        private float totalNormalizedSeverities = 0;

        public int DiagnosingTicks = 0;

        public int MaxDiagnosingTicks = GenTicks.SecondsToTicks(5);

        public int HealingTicks = 0;

        public int MaxHealingTicks = GenTicks.SecondsToTicks(5);

        public int ProgressHealingTicks = 0;

        public int TotalHealingTicks = 0;

        public enum MedPodStatus
        {
            Idle = 0,
            DiagnosisStarted,
            DiagnosisFinished,
            HealingStarted,
            HealingFinished,
            PatientDischarged,
            Error
        }

        public MedPodStatus status = MedPodStatus.Idle;   

        private IntVec3 InvisibleBlockerPosition
        {
            get
            {
                IntVec3 position;
                if (Rotation == Rot4.North)
                {
                    position = new IntVec3(Position.x + 0, Position.y, Position.z - 1);
                }
                else if (Rotation == Rot4.East)
                {
                    position = new IntVec3(Position.x - 1, Position.y, Position.z + 0);
                }
                else if (Rotation == Rot4.South)
                {
                    position = new IntVec3(Position.x + 0, Position.y, Position.z + 1);
                }
                else // Default: West
                {
                    position = new IntVec3(Position.x + 1, Position.y, Position.z + 0);
                }

                return position;
            }
        }

        private Thing resultingBlocker;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();

            // Add a blocker region for the MedPod main machinery
            // (If one already exists, then we are probably loading a save with an existing MedPod)
            Thing something = Map.thingGrid.ThingsListAtFast(InvisibleBlockerPosition).FirstOrDefault(x => x.def.Equals(MedPodDef.MedPodInvisibleBlocker));

            if (something != null)
            {
                something.DeSpawn();
            }

            Thing t = ThingMaker.MakeThing(MedPodDef.MedPodInvisibleBlocker);
            Log.Warning("MedPod :: Placing blocker for " + ThingID.ToString());
            GenPlace.TryPlaceThing(t, InvisibleBlockerPosition, Map, ThingPlaceMode.Direct, out resultingBlocker, null, null, Rotation);
            Log.Warning("MedPod :: Blocker placed for " + ThingID.ToString());
        }

        private Pawn PatientPawn
        {
            get
            {
                if (GetCurOccupant(0) != null)
                {
                    return GetCurOccupant(0);
                }
                return null;
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (powerComp.PowerOn && ( (status == MedPodStatus.DiagnosisFinished) || (status == MedPodStatus.HealingStarted) || (status == MedPodStatus.HealingFinished) ) )
            {
                WakePatient(PatientPawn, false);
            }
            this.ForPrisoners = false;
            this.Medical = false;

            // Remove the blocker region
            resultingBlocker.DeSpawn();

            Room room = this.GetRoom(RegionType.Set_Passable);
            base.DeSpawn(mode);
            if (room != null)
            {
                room.Notify_RoomShapeOrContainedBedsChanged();
            }
        }

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();

            string inspectorStatus = null;

            stringBuilder.AppendInNewLine(powerComp.CompInspectStringExtra());

            if (this.def.building.bed_humanlike)
            {
                if (this.ForPrisoners)
                {
                    stringBuilder.AppendInNewLine("ForPrisonerUse".Translate());
                }
                else
                {
                    stringBuilder.AppendInNewLine("ForColonistUse".Translate());
                }
            }

            if (!powerComp.PowerOn)
            {
                inspectorStatus = "Error: No power";
            }
            else
            {
                switch (status)
                {
                    case MedPodStatus.DiagnosisStarted:
                        float diagnosingProgress = (float)(MaxDiagnosingTicks - DiagnosingTicks) / MaxDiagnosingTicks * 100;
                        inspectorStatus = "Diagnosing (" + (int)diagnosingProgress + "%)";
                        break;
                    case MedPodStatus.DiagnosisFinished:
                        inspectorStatus = "Diagnosis complete";
                        break;
                    case MedPodStatus.HealingStarted:
                    case MedPodStatus.HealingFinished:
                        Log.Warning("MedPod :: " + ProgressHealingTicks);
                        float healingProgress = (float) ProgressHealingTicks / TotalHealingTicks * 100;
                        inspectorStatus = "Reatomizing (" + (int)healingProgress + "%)";
                        break;
                    case MedPodStatus.PatientDischarged:
                        inspectorStatus = "100% Clear";
                        break;
                    case MedPodStatus.Idle:
                    default:
                        inspectorStatus = "Idle";
                        break;
                }
            }

            stringBuilder.AppendInNewLine(inspectorStatus);

            return stringBuilder.ToString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            string medicalToggleStr = "CommandBedSetAsMedicalLabel".Translate();
            foreach (Gizmo g in base.GetGizmos())
            {
                if (g is Command_Toggle act && (act.defaultLabel == medicalToggleStr))
                {
                    continue; // Hide the Medical bed toggle, as MedPods are always Medical beds
                }
                yield return g;
            }
        }

        private void SwitchState()
        {
            switch (status)
            {
                case MedPodStatus.Idle:
                    status = MedPodStatus.DiagnosisStarted;
                    break;

                case MedPodStatus.DiagnosisStarted:
                    status = MedPodStatus.DiagnosisFinished;
                    break;

                case MedPodStatus.DiagnosisFinished:
                    status = MedPodStatus.HealingStarted;
                    break;

                case MedPodStatus.HealingStarted:
                    status = MedPodStatus.HealingFinished;
                    break;

                case MedPodStatus.HealingFinished:
                    status = MedPodStatus.PatientDischarged;
                    break;

                case MedPodStatus.PatientDischarged:
                    status = MedPodStatus.Idle;
                    break;

                default:
                    status = MedPodStatus.Error;
                    break;
            }
        }

        private void DiagnosePatient(Pawn patientPawn)
        {
            // List all of the patient's hediffs/injuries, sorted by body part hierarchy then severity
            // Hediffs with no body part defined (i.e. "Whole Body" hediffs) are moved to the bottom of the list)
            patientTreatableHediffs = patientPawn.health.hediffSet.hediffs.OrderBy((Hediff x) => x.Part == null ? 9999 : x.Part.Index).ThenByDescending((Hediff x) => x.Severity).ToList();

            // Induce coma in the patient so that they don't run off during treatment
            // (Pawns tend to get up as soon as they are "no longer incapable of walking")
            AnesthesizePatient(patientPawn);

            string hediffList = null;

            foreach (Hediff currentHediff in patientTreatableHediffs)
            {
                string currentBodyPart = (currentHediff.Part != null) ? currentHediff.Part.Label : "Whole Body";

                string currentBodyPartIndex = (currentHediff.Part != null) ? currentHediff.Part.Index.ToString() : "unknown";

                float currentSeverity = currentHediff.Severity;

                float currentBodyPartMaxHealth = (currentHediff.Part != null) ? currentHediff.Part.def.GetMaxHealth(patientPawn) : 1 ;

                float currentNormalizedSeverity = (currentSeverity < 1) ? currentSeverity : currentSeverity / currentBodyPartMaxHealth;

                // LoadID is unique per pawn, per savegame
                // currentHediff.Part will throw an error if a hediff is applied to the whole body (e.g. malnutrition), as part == null

                string hediffDebugData =
                    "\tLoadID = " + currentHediff.loadID.ToString() + ", " +
                    "(Index " + currentBodyPartIndex + "), " +
                    "Body Part = " + currentBodyPart +
                    "Type = " + currentHediff.def.ToString() + ", " +
                    "Severity = " + currentSeverity.ToString() +
                    " (Normalized = " + currentNormalizedSeverity.ToString() + ")";

                hediffList += hediffDebugData + "\n";

                totalNormalizedSeverities += currentNormalizedSeverity;

                TotalHealingTicks += (int)Math.Ceiling(GetHediffNormalizedSeverity(currentHediff) * MaxHealingTicks);
            }

            Log.Warning(
                "MedPod :: Diagnosis complete for patient " + patientPawn.Name.ToString() + "\n\n" +
                "Hediffs (sorted by body part index, then severity):\n" +
                hediffList + "(" + patientTreatableHediffs.Count.ToString() + " total)\n\n" +
                "(Total Normalized Severity " + totalNormalizedSeverities.ToString() + ")\n\n" +
                "(Total Healing Ticks Requires " + TotalHealingTicks.ToString() + ")\n\n");

        }

        private float GetHediffNormalizedSeverity(Hediff specificHediff = null)
        {
            Hediff currentHediff = (specificHediff == null) ? patientTreatableHediffs.First() : specificHediff ;

            float currentHediffSeverity = currentHediff.Severity;

            float currentHediffBodyPartMaxHealth = (currentHediff.Part != null) ? currentHediff.Part.def.GetMaxHealth(PatientPawn) : 1;

            float currentHediffNormalizedSeverity = (currentHediffSeverity < 1) ? currentHediffSeverity : currentHediffSeverity / currentHediffBodyPartMaxHealth;

            return currentHediffNormalizedSeverity;
        }

        private void AnesthesizePatient(Pawn patientPawn)
        {
            Hediff inducedComa = HediffMaker.MakeHediff(HediffDef.Named("MedPod_InducedComa"), patientPawn);
            patientPawn.health.AddHediff(inducedComa);
        }

        private void WakePatient(Pawn patientPawn, bool wakeNormally = true)
        {
            patientPawn.health.hediffSet.hediffs.RemoveAll((Hediff x) => x.def.defName == "MedPod_InducedComa");

            string corticalStimulationType = wakeNormally ? "MedPod_CorticalStimulation" : "MedPod_CorticalStimulationImproper";

            Hediff corticalStimulation = HediffMaker.MakeHediff(HediffDef.Named(corticalStimulationType), patientPawn);
            patientPawn.health.AddHediff(corticalStimulation);
        }

        public override void Tick()
        {
            base.Tick();

            if (!powerComp.PowerOn)
            {
                if (PatientPawn != null)
                {
                    if ( (status == MedPodStatus.DiagnosisFinished) || (status == MedPodStatus.HealingStarted) || (status == MedPodStatus.HealingFinished) )
                    {
                        // Wake patient up abruptly, as power was interrupted during treatment
                        WakePatient(PatientPawn, false);
                    }

                    if (status == MedPodStatus.PatientDischarged)
                    {
                        // Wake patient up normally, as treatment was already completed when power was interrupted
                        WakePatient(PatientPawn);
                    }
                }

                status = MedPodStatus.Idle;

                return;
            }

            powerComp.PowerOutput = -125f;

            if (this.IsHashIntervalTick(60))
            {

                if (PatientPawn != null)
                {
                    if (status == MedPodStatus.Idle)
                    {
                        DiagnosingTicks = MaxDiagnosingTicks;
                        SwitchState();
                    }

                    if (status == MedPodStatus.DiagnosisFinished)
                    {
                        DiagnosePatient(PatientPawn);

                        // Scale healing time for current hediff according to its (normalized) severity
                        // i.e. More severe hediffs take longer
                        HealingTicks = (int) Math.Ceiling(GetHediffNormalizedSeverity() * MaxHealingTicks);
                        
                        Log.Warning("MedPod :: Healing ticks for current hediff should be " + GetHediffNormalizedSeverity().ToString() + " x " + MaxHealingTicks.ToString() + " = " + HealingTicks.ToString());

                        SwitchState();
                    }

                    if (status == MedPodStatus.HealingFinished)
                    {
                        PatientPawn.health.hediffSet.hediffs.Remove(patientTreatableHediffs.First());
                        patientTreatableHediffs.RemoveAt(0);

                        if (!patientTreatableHediffs.NullOrEmpty())
                        {
                            // Scale healing time for current hediff according to its (normalized) severity
                            // i.e. More severe hediffs take longer
                            HealingTicks = (int)Math.Ceiling(GetHediffNormalizedSeverity() * MaxHealingTicks);

                            Log.Warning("MedPod :: Healing ticks for current hediff should be " + GetHediffNormalizedSeverity().ToString() + " x " + MaxHealingTicks.ToString() + " = " + HealingTicks.ToString());

                            status = MedPodStatus.HealingStarted;
                        }
                        else
                        {
                            SwitchState();
                        }
                    }

                    if (status == MedPodStatus.PatientDischarged)
                    {
                        WakePatient(PatientPawn);
                        SwitchState();
                        ProgressHealingTicks = 0;
                        TotalHealingTicks = 0;
                    }
                }
                else
                {
                    status = MedPodStatus.Idle;
                }
            }

            if (DiagnosingTicks > 0)
            {
                DiagnosingTicks--;
                powerComp.PowerOutput = -500f;

                if (DiagnosingTicks == 0)
                {
                    SwitchState();
                }
            }

            if (HealingTicks > 0)
            {
                HealingTicks--;
                ProgressHealingTicks++;
                powerComp.PowerOutput = -1000f;

                if (HealingTicks == 0)
                {
                    SwitchState();
                }
            }
        }
    }
}
