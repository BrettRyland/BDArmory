using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.WeaponMounts
{
    public class ModuleTurret : BDAPartModule
    {
        [KSPField] public int turretID = 0;

        [KSPField] public string pitchTransformName = "pitchTransform";
        public Transform pitchTransform;

        [KSPField] public string yawTransformName = "yawTransform";
        public Transform yawTransform;

        [KSPField] public string baseTransformName = "";
        public Transform baseTransform;

        public Transform referenceTransform { get; }
        Transform _referenceTransform; //set this to gun's fireTransform

        public ModuleWeapon turretWeapon = null;
        public MissileTurret turretMissile = null;

        public TurretAxisManager yawAxisManager = null;
        public int yawAxisIndex = 0;
        public TurretAxisManager pitchAxisManager = null;
        public int pitchAxisIndex = 0;

        [KSPField] public int turretPriority = 0;

        [KSPField] public float pitchSpeedDPS;
        [KSPField] public float yawSpeedDPS;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxPitch"),//Max Pitch
         UI_FloatRange(minValue = 0f, maxValue = 60f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float maxPitch;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinPitch"),//Min Pitch
         UI_FloatRange(minValue = 1f, maxValue = 0f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float minPitch;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_YawRange"),//Yaw Range
         UI_FloatRange(minValue = 1f, maxValue = 60f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float yawRange;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_YawStandbyAngle"),
         UI_FloatRange(minValue = -90f, maxValue = 90f, stepIncrement = 0.5f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.None)]
        public float yawStandbyAngle = 0;
        Quaternion standbyLocalRotation;// = Quaternion.identity;
        bool _yawStandbyAngleEnabled = true;

        [KSPField(isPersistant = true)] public float minPitchLimit = 400;
        [KSPField(isPersistant = true)] public float maxPitchLimit = 400;
        [KSPField(isPersistant = true)] public float yawRangeLimit = 400;

        [KSPField] public bool smoothRotation = false;
        [KSPField] public float smoothMultiplier = 10;

        //sfx
        [KSPField] public string audioPath;
        [KSPField] public float maxAudioPitch = 0.5f;
        [KSPField] public float minAudioPitch = 0f;
        [KSPField] public float maxVolume = 1;
        [KSPField] public float minVolume = 0;

        AudioClip soundClip;
        AudioSource audioSource;
        bool hasAudio;
        float audioRotationRate;
        float targetAudioRotationRate;
        Vector3 lastTurretDirection;
        float maxAudioRotRate;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            SetupTransforms();

            SetupTweakables();

            if (yawTransform && !yawAxisManager)
            {
                yawAxisManager = part.gameObject.AddComponent<TurretAxisManager>();
                if (!yawAxisManager.AddTurrets(part, true, yawTransform))
                {
                    yawAxisManager = null;
                }
            }

            if (pitchTransform && !pitchAxisManager)
            {
                pitchAxisManager = part.gameObject.AddComponent<TurretAxisManager>();
                if (!pitchAxisManager.AddTurrets(part, false, pitchTransform))
                {
                    pitchAxisManager = null;
                }
            }

            if (!string.IsNullOrEmpty(audioPath) && (yawSpeedDPS != 0 || pitchSpeedDPS != 0))
            {
                soundClip = SoundUtils.GetAudioClip(audioPath);

                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = soundClip;
                audioSource.loop = true;
                audioSource.dopplerLevel = 0;
                audioSource.minDistance = .5f;
                audioSource.maxDistance = 150;
                audioSource.Play();
                audioSource.volume = 0;
                audioSource.pitch = 0;
                audioSource.priority = 9999;
                audioSource.spatialBlend = 1;

                if (pitchTransform || yawTransform)
                {
                    lastTurretDirection = baseTransform.InverseTransformDirection(pitchTransform ? pitchTransform.forward : yawTransform.forward);
                }

                audioRotationRate = 0;
                //maxAudioRotRate = Mathf.Min(yawSpeedDPS, pitchSpeedDPS);
                maxAudioRotRate = Mathf.Min(yawTransform ? yawSpeedDPS : float.MaxValue, pitchTransform ? pitchSpeedDPS : float.MaxValue);

                // If one of the two values is zero, try to salvage things
                if (maxAudioRotRate <= 0)
                {
                    maxAudioRotRate = Mathf.Max(yawTransform ? yawSpeedDPS : -1, pitchTransform ? pitchSpeedDPS : -1);
                }

                // If all else fails, default to 90 DPS
                if (maxAudioRotRate == float.MaxValue || maxAudioRotRate <= 0)
                {
                    maxAudioRotRate = 90;
                }

                hasAudio = true;
            }
        }

        bool transformsSetup = false;

        public void SetupTransforms()
        {
            if (transformsSetup) return;

            pitchTransform = part.FindModelTransform(pitchTransformName);
            yawTransform = part.FindModelTransform(yawTransformName);
            if (!string.IsNullOrEmpty(baseTransformName))
            {
                baseTransform = part.FindModelTransform(baseTransformName);
            }

            if (!pitchTransform)
            {
                Debug.LogWarning($"[BDArmory.ModuleTurret]: {part.partInfo.title} has no pitchTransform");
            }

            if (!yawTransform)
            {
                Debug.LogWarning($"[BDArmory.ModuleTurret]: {part.partInfo.title} has no yawTransform");
            }

            if (!baseTransform)
            {
                Debug.Log($"[BDArmory.ModuleTurret]: {part.partInfo.title} has no baseTransform");
                if (yawTransform)
                {
                    Debug.Log($"[BDArmory.ModuleTurret]: {part.partInfo.title} defaulting baseTransform to yawTransform.parent");
                    baseTransform = yawTransform.parent;
                }
                else if (pitchTransform)
                {
                    Debug.Log($"[BDArmory.ModuleTurret]: {part.partInfo.title} defaulting baseTransform to pitchTransform.parent as there was no yawTransform!");
                    baseTransform = pitchTransform.parent;
                }
                else
                {
                    Debug.LogWarning($"[BDArmory.ModuleTurret]: {part.partInfo.title} defaulting baseTransform to part.transform as there was no yawTransform or pitchTransform! Turret unlikely to function properly!");
                    baseTransform = part.transform;
                }
            }

            if (!_referenceTransform)
            {
                if (pitchTransform)
                {
                    SetReferenceTransform(pitchTransform);
                }
                else if (yawTransform)
                {
                    SetReferenceTransform(yawTransform);
                }
                else
                {
                    SetReferenceTransform(baseTransform);
                }
            }

            transformsSetup = true;
        }

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (hasAudio)
                {
                    audioRotationRate = Mathf.Lerp(audioRotationRate, targetAudioRotationRate, 20 * Time.fixedDeltaTime);
                    audioRotationRate = Mathf.Clamp01(audioRotationRate);

                    if (audioRotationRate < 0.05f)
                    {
                        audioSource.volume = 0;
                    }
                    else
                    {
                        audioSource.volume = Mathf.Clamp(2f * audioRotationRate,
                            minVolume * BDArmorySettings.BDARMORY_WEAPONS_VOLUME,
                            maxVolume * BDArmorySettings.BDARMORY_WEAPONS_VOLUME);
                        audioSource.pitch = Mathf.Clamp(audioRotationRate, minAudioPitch, maxAudioPitch);
                    }

                    if (yawTransform || pitchTransform)
                    {
                        Vector3 tDir = baseTransform.InverseTransformDirection(pitchTransform ? pitchTransform.forward : yawTransform.forward);
                        float angle = VectorUtils.Angle(tDir, lastTurretDirection);
                        float rate = Mathf.Clamp01((angle / Time.fixedDeltaTime) / maxAudioRotRate);
                        lastTurretDirection = tDir;

                        targetAudioRotationRate = rate;
                    }
                }
            }
        }

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (hasAudio)
                {
                    if (!BDArmorySetup.GameIsPaused && audioRotationRate > 0.05f)
                    {
                        if (!audioSource.isPlaying) audioSource.Play();
                    }
                    else
                    {
                        if (audioSource.isPlaying)
                        {
                            audioSource.Stop();
                        }
                    }
                }
            }
        }

        void OnDestroy()
        {
            GameEvents.onEditorPartPlaced.Remove(OnEditorPartPlaced);

            if (yawAxisManager)
            {
                Destroy(yawAxisManager);
            }
            
            if (pitchAxisManager)
            {
                Destroy(pitchAxisManager);
            }
        }
        public void AimToTarget(Vector3 targetPosition, bool pitch = true, bool yaw = true, bool activeWeap = false)
        {
            AimInDirection(targetPosition - _referenceTransform.position, pitch, yaw, activeWeap);
        }

        public void AimInDirection(Vector3 targetDirection, bool pitch = true, bool yaw = true, bool activeWeap = false)
        {
            if (!(pitch || yaw)) return;

            float deltaTime = Time.fixedDeltaTime;

            Vector3 yawNormal;
            float yawOffset;
            float targetYawAngle;
            Vector3 yawComponent;
            Vector3 pitchComponent;

            // Perform the yaw axis manager check here, as we can skip all the calculations if false
            if (yawTransform && (!yawAxisManager || yawAxisManager.CheckTurret(this, false, activeWeap)))
            {
                yawNormal = yawTransform.up;
                yawComponent = targetDirection.ProjectOnPlanePreNormalized(yawNormal);
                pitchComponent = targetDirection.ProjectOnPlane(Vector3.Cross(yawComponent, yawNormal));

                float currentYaw = Yaw;
                float yawError = VectorUtils.SignedAngleDP(
                    _referenceTransform.forward.ProjectOnPlanePreNormalized(yawNormal),
                    yawComponent,
                    Vector3.Cross(yawNormal, _referenceTransform.forward));
                yawOffset = Mathf.Abs(yawError);
                targetYawAngle = (currentYaw + yawError).ToAngle();

                // clamp target yaw in a non-wobbly way
                if (Mathf.Abs(targetYawAngle) > yawRange / 2)
                {
                    var nonWobblyWay = Vector3.Dot(baseTransform.right, targetDirection + _referenceTransform.position - yawTransform.position);
                    //if (float.IsNaN(nonWobblyWay)) return;
                    targetYawAngle = yawRange / 2 * Math.Sign(nonWobblyWay);
                }

                if (yawRange < 360 && Mathf.Abs(currentYaw - targetYawAngle) >= 180)
                {
                    //if (float.IsNaN(currentYaw)) return;
                    targetYawAngle = currentYaw - (Math.Sign(currentYaw) * 179);
                }
            }
            else
            {
                yawOffset = 0;
                targetYawAngle = 0;
                yaw = false;

                yawNormal = baseTransform.up;
                yawComponent = targetDirection.ProjectOnPlanePreNormalized(yawNormal);
                pitchComponent = targetDirection.ProjectOnPlane(Vector3.Cross(yawComponent, yawNormal));
            }

            float pitchOffset;
            float targetPitchAngle;

            // Perform the pitch axis manager check here, as we can skip all the calculations if false
            if (pitchTransform && (!pitchAxisManager || pitchAxisManager.CheckTurret(this, false, activeWeap)))
            {
                float pitchError = (float)(Vector3d.Angle(pitchComponent, yawNormal) - Vector3d.Angle(_referenceTransform.forward, yawNormal));
                float currentPitch = Pitch; // from current rotation transform
                targetPitchAngle = currentPitch - pitchError;
                pitchOffset = Mathf.Abs(targetPitchAngle - currentPitch);
                targetPitchAngle = Mathf.Clamp(targetPitchAngle, minPitch, maxPitch); // clamp pitch
            }
            else
            {
                pitchOffset = 0;
                targetPitchAngle = 0;
                pitch = false;
            }

            if (!(pitch || yaw)) return;

            float yawSpeed;
            float pitchSpeed;
            if (smoothRotation)
            {
                yawSpeed = Mathf.Clamp(yawOffset * smoothMultiplier, 1f, yawSpeedDPS) * deltaTime;
                pitchSpeed = Mathf.Clamp(pitchOffset * smoothMultiplier, 1f, pitchSpeedDPS) * deltaTime;
            }
            else
            {
                yawSpeed = yawSpeedDPS * deltaTime;
                pitchSpeed = pitchSpeedDPS * deltaTime;
            }

            if (yaw)
            {
                float linYawMult = pitch && pitchOffset > 0 ? Mathf.Clamp01((yawOffset / pitchOffset) * (pitchSpeedDPS / yawSpeedDPS)) : 1;
                yawTransform.localRotation = Quaternion.RotateTowards(yawTransform.localRotation, Quaternion.Euler(0, targetYawAngle, 0), yawSpeed * linYawMult);
            }
            if (pitch)
            {
                float linPitchMult = yaw && yawOffset > 0 ? Mathf.Clamp01((pitchOffset / yawOffset) * (yawSpeedDPS / pitchSpeedDPS)) : 1;
                pitchTransform.localRotation = Quaternion.RotateTowards(pitchTransform.localRotation, Quaternion.Euler(-targetPitchAngle, 0, 0), pitchSpeed * linPitchMult);
            }
        }

        public float Pitch => -pitchTransform.localEulerAngles.x.ToAngle();
        public float Yaw => yawTransform.localEulerAngles.y.ToAngle();

        public bool ReturnTurret(bool pitch = true, bool yaw = true, bool reloading = false)
        {
            if (!(pitch || yaw)) return true;

            float deltaTime = Time.fixedDeltaTime;

            float yawOffset;
            // Are we yawing? Is there a yawTransform? Are we actually yawed?
            // The last check is important as it allows us to skip the CheckTurret check, which causes turrets that were stowed to redeploy
            bool checkYaw = yaw && yawTransform && !(yawTransform.localRotation == standbyLocalRotation);
            
            // If we're yawing, there's a yawTransform, we're yawed, and there's no axis manager or the axis manager allows the movement...
            if (checkYaw && (!yawAxisManager || yawAxisManager.CheckTurret(this, !reloading)))
            {
                yawOffset = Quaternion.Angle(yawTransform.localRotation, standbyLocalRotation);
            }
            else
            {
                yawOffset = 0;
                yaw = false;
            }

            float pitchOffset;
            // Are we pitching? Is there a pitchTransform? Are we actually pitched?
            // The last check is important as it allows us to skip the CheckTurret check, which causes turrets that were stowed to redeploy
            bool checkPitch = pitch && pitchTransform && !(pitchTransform.localRotation == Quaternion.identity);

            // If we're pitching, there's a pitchTransform, we're pitched, and there's no axis manager or the axis manager allows the movement...
            if (checkPitch && (!pitchAxisManager || pitchAxisManager.CheckTurret(this, !reloading)))
            {
                pitchOffset = VectorUtils.Angle(pitchTransform.forward, yawTransform ? yawTransform.forward : baseTransform.forward);
            }
            else
            {
                pitchOffset = 0;
                pitch = false;
            }

            if (!(checkPitch || checkYaw)) return true;

            float yawSpeed;
            float pitchSpeed;

            if (smoothRotation)
            {
                yawSpeed = Mathf.Clamp(yawOffset * smoothMultiplier, 1f, yawSpeedDPS) * deltaTime;
                pitchSpeed = Mathf.Clamp(pitchOffset * smoothMultiplier, 1f, pitchSpeedDPS) * deltaTime;
            }
            else
            {
                yawSpeed = yawSpeedDPS * deltaTime;
                pitchSpeed = pitchSpeedDPS * deltaTime;
            }

            if (yaw)
            {
                float linYawMult = pitch && pitchOffset > 0 ? Mathf.Clamp01((yawOffset / pitchOffset) * (pitchSpeedDPS / yawSpeedDPS)) : 1;
                yawTransform.localRotation = Quaternion.RotateTowards(yawTransform.localRotation, standbyLocalRotation, yawSpeed * linYawMult);
            }
            if (pitch)
            {
                float linPitchMult = yaw && yawOffset > 0 ? Mathf.Clamp01((pitchOffset / yawOffset) * (yawSpeedDPS / pitchSpeedDPS)) : 1;
                pitchTransform.localRotation = Quaternion.RotateTowards(pitchTransform.localRotation, Quaternion.identity, pitchSpeed * linPitchMult);
            }

            return (!checkYaw || yawTransform.localRotation == standbyLocalRotation) && (!checkPitch || pitchTransform.localRotation == Quaternion.identity);
        }

        public bool TargetInRange(Vector3 targetPosition, float maxDistance, float thresholdDegrees = 0)
        {
            if (!_referenceTransform) return false;
            Vector3 vectorToTarget = targetPosition - _referenceTransform.position;
            if (vectorToTarget.sqrMagnitude > maxDistance * maxDistance) return false;

            float angleYaw = VectorUtils.Angle(vectorToTarget.ProjectOnPlanePreNormalized(_referenceTransform.up), _referenceTransform.forward);
            float signedAnglePitch = 90 - VectorUtils.Angle(_referenceTransform.up, vectorToTarget);
            bool withinView = thresholdDegrees > 0 ? VectorUtils.Angle(vectorToTarget, _referenceTransform.forward) < thresholdDegrees : (signedAnglePitch > minPitch && signedAnglePitch < maxPitch && angleYaw < yawRange / 2);
            return withinView;
        }

        public void SetReferenceTransform(Transform t)
        {
            _referenceTransform = t;
        }

        void SetupTweakables()
        {
            UI_FloatRange minPitchRange = (UI_FloatRange)Fields[nameof(minPitch)].uiControlEditor;
            if (minPitchLimit > 90)
            {
                minPitchLimit = minPitch;
            }
            if (minPitchLimit == 0)
            {
                Fields[nameof(minPitch)].guiActiveEditor = false;
            }
            minPitchRange.minValue = minPitchLimit;
            minPitchRange.maxValue = 0;
            if (minPitchLimit != 0)
                minPitchRange.stepIncrement = Mathf.Pow(10, Mathf.Min(1f, Mathf.Floor(Mathf.Log10(Mathf.Abs(minPitchLimit)) + (1 - Mathf.Log10(20f) - 1e-4f)))) / 10f; // Use between 20 and 200 divisions

            UI_FloatRange maxPitchRange = (UI_FloatRange)Fields[nameof(maxPitch)].uiControlEditor;
            if (maxPitchLimit > 90)
            {
                maxPitchLimit = maxPitch;
            }
            if (maxPitchLimit == 0)
            {
                Fields[nameof(maxPitch)].guiActiveEditor = false;
            }
            maxPitchRange.maxValue = maxPitchLimit;
            maxPitchRange.minValue = 0;
            if (maxPitchLimit != 0)
                maxPitchRange.stepIncrement = Mathf.Pow(10, Mathf.Min(1f, Mathf.Floor(Mathf.Log10(Mathf.Abs(maxPitchLimit)) + (1 - Mathf.Log10(20f) - 1e-4f)))) / 10f; // Use between 20 and 200 divisions

            UI_FloatRange yawRangeEd = (UI_FloatRange)Fields[nameof(yawRange)].uiControlEditor;
            if (yawRangeLimit > 360)
            {
                yawRangeLimit = yawRange;
            }

            if (yawRangeLimit == 0)
            {
                Fields[nameof(yawRange)].guiActiveEditor = false;
            }
            else if (yawRangeLimit < 0)
            {
                yawRangeEd.minValue = 0;
                yawRangeEd.maxValue = 360;

                if (yawRange < 0) yawRange = 360;
            }
            else
            {
                yawRangeEd.minValue = 0;
                yawRangeEd.maxValue = yawRangeLimit;
            }
            if (yawRange != 0)
                yawRangeEd.stepIncrement = Mathf.Pow(10, Math.Min(1f, Mathf.Floor(Mathf.Log10(Mathf.Abs(yawRange)) + (1 - Mathf.Log10(20f) - 1e-4f)))) / 10f; // Use between 20 and 200 divisions

            yawRangeEd.onFieldChanged = SetupStandbyLocalRotation;
            SetupStandbyLocalRotation();
        }
        void SetupStandbyLocalRotation(BaseField field = null, object obj = null)
        {
            UI_FloatRange yawStandbyAngleEd = (UI_FloatRange)Fields[nameof(yawStandbyAngle)].uiControlEditor;
            yawStandbyAngleEd.minValue = -yawRange / 2f;
            yawStandbyAngleEd.maxValue = yawRange / 2f;
            yawStandbyAngle = Mathf.Clamp(yawStandbyAngle, yawStandbyAngleEd.minValue, yawStandbyAngleEd.maxValue);
            yawStandbyAngleEd.onFieldChanged = OnStandbyAngleChanged;
            GameEvents.onEditorPartPlaced.Add(OnEditorPartPlaced);
            SetStandbyAngle();
        }

        void OnEditorPartPlaced(Part p = null) { if (p == part) OnStandbyAngleChanged(); }

        void OnStandbyAngleChanged(BaseField field = null, object obj = null)
        {
            SetStandbyAngle();
            PropagateStandbyAngle();
            foreach (Part symmetryPart in part.symmetryCounterparts)
            {
                ModuleTurret symmetryTurret = null;
                
                List<ModuleTurret> turrets = symmetryPart.FindModulesImplementing<ModuleTurret>();
                for (int i = 0; i < turrets.Count; i++)
                {
                    if (turrets[i] == null) continue;
                    if (turrets[i].turretID == turretID) symmetryTurret = turrets[i];
                }

                if (!symmetryTurret) continue;

                if (part.symMethod == SymmetryMethod.Mirror)
                {
                    symmetryTurret.yawStandbyAngle = -yawStandbyAngle;
                }
                else
                {
                    symmetryTurret.yawStandbyAngle = yawStandbyAngle;
                }

                symmetryTurret.SetStandbyAngle();
                symmetryTurret.PropagateStandbyAngle();
            }
        }

        void PropagateStandbyAngle()
        {
            if (yawAxisManager)
            {
                yawAxisManager.SetYawStandbyAngle(this, yawStandbyAngle);
            }
        }

        public void SetStandbyAngle()
        {
            standbyLocalRotation = Quaternion.AngleAxis(yawStandbyAngle, Vector3.up);
            if (yawTransform != null && _yawStandbyAngleEnabled) yawTransform.localRotation = standbyLocalRotation;
        }

        public void DisableYawStandbyAngle()
        {
            Fields[nameof(yawStandbyAngle)].guiActiveEditor = false;
            _yawStandbyAngleEnabled = false;
        }

        public float DeployIfBlocking(bool yaw)
        {
            if (turretWeapon)
            {
                return turretWeapon.DeployIfBlocking();
            }
            if (turretMissile)
            {
                return turretMissile.DeployIfBlocking(yaw);
            }

            return 0;
        }

        public void StowTurret()
        {
            if (turretWeapon)
            {
                turretWeapon.ReturnWeapon();
            }
            if (turretMissile)
            {
                turretMissile.ReturnTurret();
            }
        }

        public void SetDeployFlag(bool yawEnabled, bool pitchEnabled)
        {
            if (yawAxisManager) yawAxisManager.SetTurretFlag(!yawEnabled, yawAxisIndex);
            if (pitchAxisManager) pitchAxisManager.SetTurretFlag(!pitchEnabled, pitchAxisIndex);
        }

        public void SetYawDeployFlag(bool yawEnabled)
        {
            if (yawAxisManager) yawAxisManager.SetTurretFlag(!yawEnabled, yawAxisIndex);
        }

        public void SetPitchDeployFlag(bool pitchEnabled)
        {
            if (pitchAxisManager) pitchAxisManager.SetTurretFlag(!pitchEnabled, pitchAxisIndex);
        }

        public bool turretEnabled()
        {
            if (turretWeapon)
            {
                switch (turretWeapon.weaponState)
                {
                    case ModuleWeapon.WeaponStates.Enabled:
                    case ModuleWeapon.WeaponStates.PoweringUp:
                    case ModuleWeapon.WeaponStates.Locked:
                    case ModuleWeapon.WeaponStates.EnabledForSecondaryFiring:
                        {
                            return true;
                        }
                    default:
                        {
                            return false;
                        }
                }
            }

            if (turretMissile)
            {
                return turretMissile.turretEnabled;
            }

            return false;
        }
    }
    public class BDAScaleByDistance : BDAPartModule
    {
        /// <summary>
        /// Sibling Module to FXModuleLookAtConstraint, causes indicated mesh object to scale based on distance to target transform
        /// Module ported over to fix the spring on the M230Chaingun (no Stock equivalent), though I guess it could be used for other things as well
        /// </summary>
        [KSPField(isPersistant = false)]
        public string transformToScaleName;

        public Transform transformToScale;

        [KSPField(isPersistant = false)]
        public string scaleFactor = "0,0,1";
        Vector3 scaleFactorV;

        [KSPField(isPersistant = false)]
        public string distanceTransformName;

        public Transform distanceTransform;


        public override void OnStart(PartModule.StartState state)
        {
            ParseScale();
            transformToScale = part.FindModelTransform(transformToScaleName);
            distanceTransform = part.FindModelTransform(distanceTransformName);
        }

        public void Update()
        {
            Vector3 finalScaleFactor;
            float distance = Vector3.Distance(transformToScale.position, distanceTransform.position);
            float sfX = (scaleFactorV.x != 0) ? scaleFactorV.x * distance : 1;
            float sfY = (scaleFactorV.y != 0) ? scaleFactorV.y * distance : 1;
            float sfZ = (scaleFactorV.z != 0) ? scaleFactorV.z * distance : 1;
            finalScaleFactor = new Vector3(sfX, sfY, sfZ);

            transformToScale.localScale = finalScaleFactor;
        }



        void ParseScale()
        {
            string[] split = scaleFactor.Split(',');
            float[] splitF = new float[split.Length];
            for (int i = 0; i < split.Length; i++)
            {
                splitF[i] = float.Parse(split[i]);
            }
            scaleFactorV = new Vector3(splitF[0], splitF[1], splitF[2]);
        }

    }
}
