using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Extensions;
using BDArmory.Weapons.Missiles;
using BDArmory.Weapons;
using System;

namespace BDArmory.Radar
{
    public class RadarWarningReceiver : BDAPartModule
    {
        public delegate void RadarPing(Vessel v, Vector3 source, RWRThreatTypes type, float persistTime, Vessel vSource);

        public static event RadarPing OnRadarPing;

        public delegate void MissileLaunchWarning(Vector3 source, Vector3 direction, bool radar, Vessel vSource);

        public static event MissileLaunchWarning OnMissileLaunch;

        // IMPORTANT NOTE: These are used for bitshifts for the bit mask!
        // IF ANY OF THE VALUES ARE CHANGED, ALL HARDCODED BITSHIFTS NEED
        // TO BE CHECKED AND CHANGED TO REFLECT THE NEW VALUES!
        public enum RWRThreatTypes
        {
            None = -1,
            SAM = 0,
            Fighter = 1,
            AWACS = 2,
            MissileLaunch = 3,
            MissileLock = 4,
            Detection = 5,
            Sonar = 6,
            Torpedo = 7,
            TorpedoLock = 8,
            Jamming = 9,
            MWS = 10,
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanDetectRWRThreat(int detectedTypes, RWRThreatTypes threat)
        {
            // Technically it would be good to check for the None case
            // but we assume we're smart enough not to feed bad data...
            // In any case the below will still work with None input
            // if (threat == RWRThreatTypes.None) return false;

            return (detectedTypes & threat.ToBits()) != 0;
        }

        string[] iconLabels = new string[] { "S", "F", "A", "M", "M", "D", "So", "T", "T", "J" };

        // This field may not need to be persistent.  It was combining display with active RWR status.
        [KSPField(isPersistant = true)] public bool rwrEnabled;
        //for if the RWR should detect everything, or only be able to detect radar sources
        [KSPField(isPersistant = true)] public bool omniDetection = true;

        [KSPField] public float fieldOfView = 360; //for if making separate RWR and WM for mod competitions, etc.

        [KSPField] public float RWRMWSRange = 20000; //range of the MWS in m
        [KSPField] public float RWRMWSUpdateRate = 0.5f; //interval in s between MWS updates,
                                                         //only here for performance and spam reasons, human pilot won't need a super high
                                                         //update rate and we don't want the warning sound to be played at every frame

        public bool performMWSCheck = true;
        public float TimeOfNextMWSUpdate = -1f;
        private RWRSignatureData[] MWSData;
        private int MWSSlots = 0;

        private RWRSignatureData[] missileLockData;
        private int _missileLockHead = 0;
        public int _missileLockSize { get; private set; } = 0;

        public bool displayRWR = false; // This field was added to separate RWR active status from the display of the RWR.  the RWR should be running all the time...
        public Vessel selectedAntiradTarget = null; // Emitter vessel manually selected by the player on the RWR scope for antirad targeting (#834)
        internal static bool resizingWindow = false;

        public Rect RWRresizeRect = new Rect(
            BDArmorySetup.WindowRectRwr.width - (16 * BDArmorySettings.RWR_WINDOW_SCALE),
            BDArmorySetup.WindowRectRwr.height - (16 * BDArmorySettings.RWR_WINDOW_SCALE),
            (16 * BDArmorySettings.RWR_WINDOW_SCALE),
            (16 * BDArmorySettings.RWR_WINDOW_SCALE));

        public static Texture2D rwrDiamondTexture =
            GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "rwrDiamond", false);

        public static Texture2D rwrMissileTexture =
            GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "rwrMissileIcon", false);

        public static AudioClip radarPingSound;
        public static AudioClip missileLockSound;
        public static AudioClip missileLaunchSound;
        public static AudioClip sonarPing;
        public static AudioClip torpedoPing;
        private float torpedoPingPitch;
        private float audioSourceRepeatDelay;
        private const float audioSourceRepeatDelayTime = 0.5f;


        const int dataCount = 12;

        internal float rwrDisplayRange = BDArmorySettings.MAX_ACTIVE_RADAR_RANGE;
        internal static float RwrSize = 256;
        internal static float BorderSize = 10;
        internal static float HeaderSize = 15;

        public RWRSignatureData[] pingsData;
        //public Vector3[] pingWorldPositions;
        //List<TargetSignatureData> launchWarnings;

        private RWRSignatureData[] launchWarnings;
        private int _launchWarningsHead = 0;
        private int _launchWarningsSize = 0;

        private float ReferenceUpdateTime = -1f;
        //public float TimeSinceReferenceUpdate => Time.time - ReferenceUpdateTime;

        Transform rt;

        Transform referenceTransform
        {
            get
            {
                if (!rt)
                {
                    rt = new GameObject().transform;
                    rt.parent = part.transform;
                    rt.localPosition = Vector3.zero;
                }
                return rt;
            }
        }

        internal static Rect RwrDisplayRect = new Rect(0, 0, RwrSize * BDArmorySettings.RWR_WINDOW_SCALE, RwrSize * BDArmorySettings.RWR_WINDOW_SCALE);

        GUIStyle rwrIconLabelStyle;

        AudioSource audioSource;
        public static bool WindowRectRWRInitialized;

        public override void OnAwake()
        {
            radarPingSound = SoundUtils.GetAudioClip("BDArmory/Sounds/rwrPing");
            missileLockSound = SoundUtils.GetAudioClip("BDArmory/Sounds/rwrMissileLock");
            missileLaunchSound = SoundUtils.GetAudioClip("BDArmory/Sounds/mLaunchWarning");
            sonarPing = SoundUtils.GetAudioClip("BDArmory/Sounds/rwr_sonarping");
            torpedoPing = SoundUtils.GetAudioClip("BDArmory/Sounds/rwr_torpedoping");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            part.force_activate();

            pingsData = new RWRSignatureData[dataCount];
            MWSData = new RWRSignatureData[2 * dataCount];
            //pingWorldPositions = new Vector3[dataCount];
            RWRSignatureData.ResetRWRSDArray(ref pingsData);
            launchWarnings = new RWRSignatureData[2 * dataCount]; //new List<TargetSignatureData>();
            missileLockData = new RWRSignatureData[2 * dataCount];

            rwrIconLabelStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                border = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Overflow,
                wordWrap = false,
                fontStyle = FontStyle.Bold,
                normal = new GUIStyleState { textColor = Color.green }
            };

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.minDistance = 500;
            audioSource.maxDistance = 1000;
            audioSource.spatialBlend = 1;
            audioSource.dopplerLevel = 0;
            audioSource.loop = false;

            UpdateVolume();
            BDArmorySetup.OnVolumeChange += UpdateVolume;

            if (!WindowRectRWRInitialized)
            {
                BDArmorySetup.WindowRectRwr = new Rect(BDArmorySetup.WindowRectRwr.x, BDArmorySetup.WindowRectRwr.y, RwrDisplayRect.height + BorderSize, RwrDisplayRect.height + BorderSize + HeaderSize);
                // BDArmorySetup.WindowRectRwr = new Rect(40, Screen.height - RwrDisplayRect.height, RwrDisplayRect.height + BorderSize, RwrDisplayRect.height + BorderSize + HeaderSize);
                WindowRectRWRInitialized = true;
            }

            using (var mf = VesselModuleRegistry.GetMissileFires(vessel).GetEnumerator())
                while (mf.MoveNext())
                {
                    if (mf.Current == null) continue;
                    mf.Current.rwr = this; // Set the rwr on all weapon managers to this.
                }
            //if (rwrEnabled) EnableRWR();
            EnableRWR();
        }

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_UI_VOLUME;
            }
        }

        public void UpdateReferenceTransform()
        {
            if (ReferenceUpdateTime >= Time.time)
                return;

            Vector3 upVec = VectorUtils.GetUpDirection(transform.position);

            referenceTransform.rotation = Quaternion.LookRotation(vessel.ReferenceTransform.up.ProjectOnPlanePreNormalized(upVec), upVec);

            ReferenceUpdateTime = Time.time;
        }

        public void EnableRWR()
        {
            OnRadarPing += ReceivePing;
            OnMissileLaunch += ReceiveLaunchWarning;
            rwrEnabled = true;
        }

        public void DisableRWR()
        {
            OnRadarPing -= ReceivePing;
            OnMissileLaunch -= ReceiveLaunchWarning;
            rwrEnabled = false;
        }

        void OnDestroy()
        {
            OnRadarPing -= ReceivePing;
            OnMissileLaunch -= ReceiveLaunchWarning;
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();

            if (!(HighLogic.LoadedSceneIsFlight && FlightGlobals.ready)) return;

            CleanPings();
            CleanMissileLocks();
            CleanLaunchWarnings();

            if (!omniDetection || !rwrEnabled || !performMWSCheck || (Time.time < TimeOfNextMWSUpdate)) return;

            MWSSlots = 0;

            TimeOfNextMWSUpdate = Time.time + RWRMWSUpdateRate;

            float sqrDist = float.PositiveInfinity;

            UpdateReferenceTransform();

            for (int i = 0; i < BDATargetManager.FiredMissiles.Count; i++)
            {
                MissileBase currMissile = BDATargetManager.FiredMissiles[i] as MissileBase;

                if (PerformMWSCheck(currMissile, out float currSqrDist) && sqrDist < currSqrDist)
                {
                    sqrDist = currSqrDist;
                }
            }

            if (!float.IsPositiveInfinity(sqrDist))
            {
                PlayWarningSound(RWRThreatTypes.MWS, sqrDist);
            }
        }

        public void ResetMWSSlots()
        {
            MWSSlots = 0;
        }

        public bool PerformMWSCheck(MissileBase currMissile, out float currSqrDist, bool addTarget = true)
        {
            currSqrDist = -1f;

            // No nulls and no torps!
            if (currMissile == null || currMissile.vessel == null || currMissile.SourceVessel == vessel || currMissile.GetWeaponClass() == WeaponClasses.SLW) return false;

            float currRange = RWRMWSRange;

            if (BDArmorySettings.VARIABLE_MISSILE_VISIBILITY) // assume same detectability logic as visual detection, does mean the MWS is implied to be IR based
            {
                currRange *= (currMissile.MissileState == MissileBase.MissileStates.Boost ? 1 : (currMissile.MissileState == MissileBase.MissileStates.Cruise ? 0.75f : 0.33f));
            }

            Vector3 relativePos = vessel.CoM - currMissile.vessel.CoM;

            currSqrDist = relativePos.sqrMagnitude;

            // Are we out of range?
            if (currRange * currRange < currSqrDist) return false;

            // Is the missile facing us?
            if (Vector3.Dot(relativePos, currMissile.GetForwardTransform()) < 0) return false;

            if (!addTarget) return true;

            if (MWSSlots < MWSData.Length)
            {
                Vector2 currPos = RadarUtils.WorldToRadar(currMissile.vessel.CoM, referenceTransform, RwrDisplayRect, rwrDisplayRange);
                MWSData[MWSSlots] = new RWRSignatureData(currMissile.vessel.CoM, currPos, true, RWRThreatTypes.MWS, currMissile.vessel);
                //pingWorldPositions[openIndex] = source; //FIXME source is improperly defined
                ++MWSSlots;
            }

            /*int openIndex = -1;
            bool foundPing = false;
            Vector2 currPos = RadarUtils.WorldToRadar(currMissile.vessel.CoM, referenceTransform, RwrDisplayRect, rwrDisplayRange);
            for (int i = 0; i < pingsData.Length; i++)
            {
                TargetSignatureData tempPing = pingsData[i];
                if (!tempPing.exists)
                {
                    // as soon as we have an open index, break
                    openIndex = i;
                    break;
                }

                // Consider swapping this to a vessel check, since we know the vessel anyways.
                if ((tempPing.pingPosition - currPos).sqrMagnitude < (BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY ? 100f : 900f))    //prevent ping spam
                {
                    foundPing = true;
                    break;
                }
            }

            if (openIndex >= 0)
            {
                pingsData[openIndex] = new TargetSignatureData(currMissile.vessel.CoM, currPos, true, RWRThreatTypes.MWS, currMissile.vessel);
                //pingWorldPositions[openIndex] = source; //FIXME source is improperly defined
                StartCoroutine(PingLifeRoutine(openIndex, RWRMWSUpdateRate));

                return true;
            }*/

            return true;
        }

        int _missileLockIndexer = 0;
        public void SetRadarMissileIndex()
        {
            _missileLockIndexer = _missileLockHead;
        }

        public RWRSignatureData GetNextRadarMissile()
        {
            if (_missileLockIndexer >= missileLockData.Length)
                _missileLockIndexer = 0;

            return missileLockData[_missileLockIndexer++];
        }

        public bool IsRadarMissileDetected(Vessel v)
        {
            int index = _missileLockHead;
            //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarWarningReceiver] Vessel: {vessel.vesselName} Searching Locks for missile: {(v ? v.name : "null")}, with UUID: {(v ? v.id : "null")}, _missileLockHead: {_missileLockHead}, _missileLockSize: {_missileLockSize}");
            for (int i = 0; i < _missileLockSize; i++)
            {
                if (index >= missileLockData.Length)
                {
                    index = 0;
                }

                //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarWarningReceiver] Vessel at idx: {index} is: {(missileLockData[index].vessel ? missileLockData[index].vessel.vesselName : "null")}, with UUID: {(missileLockData[index].vessel ? missileLockData[index].vessel.id : "null")}, exists: {missileLockData[index].exists}, expirationTime: {missileLockData[index].expirationTime}");
                if (missileLockData[index++].vessel == v) return true;
            }

            return false;
        }

        public RWRSignatureData GetRadarMissileDetected(Vessel v)
        {
            RWRSignatureData target;
            int index = _missileLockHead;
            for (int i = 0; i < _missileLockSize; i++)
            {
                if (index >= missileLockData.Length)
                    index = 0;

                if ((target = missileLockData[index++]).vessel == v) return target;
            }

            return RWRSignatureData.noTarget;
        }

        public bool IsVesselDetected(Vessel v)
        {
            for (int i = 0; i < pingsData.Length; i++)
            {
                // Should account for the noTarget values as well as those have vessel == null
                if (pingsData[i].vessel == v)
                    return true;
            }
            return false;
        }

        public RWRSignatureData GetVesselDetected(Vessel v)
        {
            RWRSignatureData target;
            for (int i = 0; i < pingsData.Length; i++)
            {
                if ((target = pingsData[i]).vessel == v) return target;
            }

            return RWRSignatureData.noTarget;
        }

        private void CleanPings()
        {
            float currentTime = Time.time;
            for (int i = 0; i < pingsData.Length; i++)
            {
                if (pingsData[i].exists && currentTime >= pingsData[i].expirationTime)
                {
                    pingsData[i] = RWRSignatureData.noTarget;
                }
            }
        }

        private void CleanMissileLocks()
        {
            float currentTime = Time.time;
            //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarWarningReceiver] Vessel: {vessel.vesselName} with UUID: {vessel.id} Cleaning Locks, _missileLockSize: {_missileLockSize}, _missileLockHead: {_missileLockHead}, currTime: {currentTime}");
            while (_missileLockSize > 0)
            {
                int idx = _missileLockHead;
                RWRSignatureData currData = missileLockData[idx];
                //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarWarningReceiver] Curr idx: {idx}, exists? {currData.exists}, expirationTime: {currData.expirationTime}");
                if (currData.exists && currentTime < currData.expirationTime)
                    break;

                missileLockData[idx] = RWRSignatureData.noTarget;
                ++_missileLockHead;
                if (_missileLockHead >= missileLockData.Length)
                {
                    _missileLockHead = 0;
                }
                --_missileLockSize;
            }
            //if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarWarningReceiver] Vessel: {vessel.vesselName} Done Cleaning Locks! _missileLockSize: {_missileLockSize}, _missileLockHead: {_missileLockHead}");
        }

        private void CleanLaunchWarnings()
        {
            float currentTime = Time.time;
            while (_launchWarningsSize > 0)
            {
                int idx = _launchWarningsHead;
                if (launchWarnings[idx].exists && currentTime < launchWarnings[idx].expirationTime)
                    break;

                launchWarnings[idx] = RWRSignatureData.noTarget;
                ++_launchWarningsHead;
                if (_launchWarningsHead >= launchWarnings.Length)
                {
                    _launchWarningsHead = 0;
                }
                --_launchWarningsSize;
            }
        }

        void ReceiveLaunchWarning(Vector3 source, Vector3 direction, bool radar, Vessel vSource)
        {
            if (referenceTransform == null) return;
            if (part == null || !part.isActiveAndEnabled) return;
            var weaponManager = vessel.ActiveController().WM;
            if (weaponManager == null) return;
            if (!omniDetection && !radar) return;

            UpdateReferenceTransform();

            Vector3 currPos = part.transform.position;
            float sqrDist = (currPos - source).sqrMagnitude;
            //if ((weaponManager && weaponManager.guardMode) && (sqrDist > (weaponManager.guardRange * weaponManager.guardRange))) return; //doesn't this clamp the RWR to visual view range, not radar/RWR range?
            if ((radar || sqrDist < RWRMWSRange * RWRMWSRange) && sqrDist > 10000f && VectorUtils.Angle(direction, currPos - source) < 15f)
            {
                if (_launchWarningsSize < launchWarnings.Length)
                {
                    int currIndex = _launchWarningsHead + _launchWarningsSize;
                    if (currIndex >= launchWarnings.Length)
                    {
                        currIndex -= launchWarnings.Length;
                    }

                    launchWarnings[currIndex] = new RWRSignatureData(source,
                                                                     RadarUtils.WorldToRadar(source, referenceTransform, RwrDisplayRect, rwrDisplayRange),
                                                                     true, RWRThreatTypes.MissileLaunch, vSource, RadarUtils.LAUNCH_PING_PERSIST_TIME);
                    ++_launchWarningsSize;
                }
                else
                {
                    if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarWarningReceiver] Missile Launch Warning buffer full! Unable to add missile: {(vSource ? vSource.GetDisplayName() : "null")}");
                }
                PlayWarningSound(RWRThreatTypes.MissileLaunch);

                if (weaponManager.guardMode)
                {
                    //weaponManager.FireAllCountermeasures(Random.Range(1, 2)); // Was 2-4, but we don't want to take too long doing this initial dump before other routines kick in
                    weaponManager.incomingThreatPosition = source;
                    weaponManager.missileIsIncoming = true;
                }
            }
        }

        void ReceivePing(Vessel v, Vector3 source, RWRThreatTypes type, float persistTime, Vessel vSource)
        {
            if (v == null || v.packed || !v.loaded || !v.isActiveAndEnabled || v != vessel) return;
            if (referenceTransform == null) return;
            var weaponManager = vessel.ActiveController().WM;
            if (weaponManager == null) return;
            if (!rwrEnabled) return;

            //if we are airborne or on land, no Sonar or SLW type weapons on the RWR!
            if ((type == RWRThreatTypes.Torpedo || type == RWRThreatTypes.TorpedoLock || type == RWRThreatTypes.Sonar) && (vessel.situation != Vessel.Situations.SPLASHED))
            {
                // rwr stays silent...
                return;
            }

            UpdateReferenceTransform();

            if (type == RWRThreatTypes.MissileLaunch || type == RWRThreatTypes.Torpedo)
            {
                if (_launchWarningsSize < launchWarnings.Length)
                {
                    int currIndex = _launchWarningsHead + _launchWarningsSize;
                    if (currIndex >= launchWarnings.Length)
                    {
                        currIndex -= launchWarnings.Length;
                    }

                    launchWarnings[currIndex] = new RWRSignatureData(source,
                                                                     RadarUtils.WorldToRadar(source, referenceTransform, RwrDisplayRect, rwrDisplayRange),
                                                                     true, type, vSource, RadarUtils.LAUNCH_PING_PERSIST_TIME);
                    ++_launchWarningsSize;
                }
                else
                {
                    if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarWarningReceiver] Missile Launch Warning buffer full! Unable to add missile: {(vSource ? vSource.GetDisplayName() : "null")}");
                }
                PlayWarningSound(type, (source - vessel.CoM).sqrMagnitude);
                return;
            }

            Vector2 currPos = RadarUtils.WorldToRadar(source, referenceTransform, RwrDisplayRect, rwrDisplayRange);

            if (type == RWRThreatTypes.MissileLock)
            {
                if (weaponManager.guardMode)
                {
                    weaponManager.FireChaff();
                    weaponManager.missileIsIncoming = true;
                    // TODO: if torpedo inbound, also fire accoustic decoys (not yet implemented...)
                }

                // If at capacity, exit out
                if (_missileLockSize == missileLockData.Length)
                {
                    if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.RadarWarningReceiver] Missile lock buffer full! Unable to add missile: {(vSource ? vSource.GetDisplayName() : "null")}");
                    PlayWarningSound(type, (source - vessel.CoM).sqrMagnitude);
                    return;
                }

                // Get the current index
                int currIndex = _missileLockHead + _missileLockSize;
                // Basically modulo (though I think this should tell the
                // compiler that it doesn't need to check the bounds)
                if (currIndex >= missileLockData.Length)
                {
                    currIndex -= missileLockData.Length;
                }

                // Set data
                missileLockData[currIndex] = new RWRSignatureData(source, currPos, true, type, vSource, RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME);
                // Increment size
                ++_missileLockSize;

                PlayWarningSound(type, (source - vessel.CoM).sqrMagnitude);

                return;
            }

            int openIndex = -1;
            bool refreshed = false;
            float sqrThresh = BDArmorySettings.LOGARITHMIC_RADAR_DISPLAY ? 100f : 900f;
            float currentTime = Time.time;
            for (int i = 0; i < pingsData.Length; i++)
            {
                RWRSignatureData tempPing = pingsData[i];

                if (!tempPing.exists || currentTime >= tempPing.expirationTime)
                {
                    // Remember the first open slot, but keep scanning: the live entry for this
                    // emitter may sit in a later slot, and matching it must win over creating a
                    // second icon next to the stale one.
                    if (openIndex < 0) openIndex = i;
                    continue;
                }

                if (tempPing.signalType != type) continue;

                // Match by emitter vessel identity whenever it is known (every PingRWR caller
                // supplies one), falling back to positional proximity only when it isn't.
                bool sameEmitter = (vSource != null && tempPing.vessel != null)
                    ? tempPing.vessel == vSource
                    : (tempPing.pingPosition - currPos).sqrMagnitude < sqrThresh;

                if (sameEmitter)
                {
                    // Same source pinging again while its entry is still alive: refresh the
                    // stored position and extend the entry instead of dropping the ping - the
                    // scope icon then tracks the emitter at the sensor's ping rate (no frozen
                    // or duplicated icons lingering for the doubled #796-4 persist time), and
                    // continuous emitters (lock/track) don't go blind for a frame every
                    // persistTime when the entry expires mid-stream (#796-4).
                    tempPing.expirationTime = currentTime + persistTime;
                    tempPing.position = source;
                    tempPing.pingPosition = currPos;
                    pingsData[i] = tempPing;
                    refreshed = true;
                    break;
                }
            }

            if (!refreshed && openIndex >= 0)
            {
                pingsData[openIndex] = new RWRSignatureData(source, currPos, true, type, vSource, persistTime);
                //pingWorldPositions[openIndex] = source; //FIXME source is improperly defined
                if (weaponManager.hasAntiRadiationOrdnance)
                {
                    BDATargetManager.ReportVessel(AIUtils.VesselClosestTo(source), weaponManager); // Report RWR ping as target for anti-rads
                } //MissileFire RWR-vessel checks are all (RWR ping position - guardtarget.CoM).Magnitude < 20*20?, could we simplify the more complex vessel aquistion function used here?

                PlayWarningSound(type, (source - vessel.CoM).sqrMagnitude);
            }
        }

        public void PlayWarningSound(RWRThreatTypes type, float sqrDistance = 0f)
        {
            if (vessel.isActiveVessel && audioSourceRepeatDelay <= 0f)
            {
                switch (type)
                {
                    case RWRThreatTypes.MissileLaunch:
                        if (audioSource.isPlaying)
                            break;
                        audioSource.clip = missileLaunchSound;
                        audioSource.Play();
                        break;

                    case RWRThreatTypes.Sonar:
                        if (audioSource.isPlaying)
                            break;
                        audioSource.clip = sonarPing;
                        audioSource.Play();
                        break;

                    case RWRThreatTypes.Torpedo:
                    case RWRThreatTypes.TorpedoLock:
                        if (audioSource.isPlaying)
                            break;
                        torpedoPingPitch = Mathf.Lerp(1.5f, 1.0f, sqrDistance / (2000 * 2000)); //within 2km increase ping pitch
                        audioSource.Stop();
                        audioSource.clip = torpedoPing;
                        audioSource.pitch = torpedoPingPitch;
                        audioSource.Play();
                        audioSourceRepeatDelay = audioSourceRepeatDelayTime;    //set a min repeat delay to prevent too much audi pinging
                        break;

                    case RWRThreatTypes.MissileLock:
                    case RWRThreatTypes.MWS:
                        if (audioSource.isPlaying)
                            break;
                        audioSource.clip = (missileLockSound);
                        audioSource.Play();
                        audioSourceRepeatDelay = audioSourceRepeatDelayTime;    //set a min repeat delay to prevent too much audi pinging
                        break;
                    case RWRThreatTypes.None:
                        break;
                    default:
                        if (!audioSource.isPlaying)
                        {
                            audioSource.clip = (radarPingSound);
                            audioSource.Play();
                            audioSourceRepeatDelay = audioSourceRepeatDelayTime;    //set a min repeat delay to prevent too much audi pinging
                        }
                        break;
                }
            }
        }

        void OnGUI()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || !BDArmorySetup.GAME_UI_ENABLED ||
                !vessel.isActiveVessel || !displayRWR) return;
            if (audioSourceRepeatDelay > 0)
                audioSourceRepeatDelay -= Time.fixedDeltaTime;

            if (resizingWindow && Event.current.type == EventType.MouseUp) { resizingWindow = false; }

            if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectRwr.position);
            BDArmorySetup.WindowRectRwr = GUI.Window(94353, BDArmorySetup.WindowRectRwr, WindowRwr, "Radar Warning Receiver", GUI.skin.window);
        }

        internal void WindowRwr(int windowID)
        {
            if (GUI.Button(new Rect(BDArmorySetup.WindowRectRwr.width - 18, 2, 16, 16), " X", BDArmorySetup.CloseButtonStyleDark))
            {
                displayRWR = false;
                BDArmorySetup.SaveConfig();
            }
            GUI.BeginGroup(new Rect(BorderSize / 2, HeaderSize + (BorderSize / 2), RwrDisplayRect.width, RwrDisplayRect.height));

            GUI.DrawTexture(RwrDisplayRect, VesselRadarData.omniBgTexture, ScaleMode.StretchToFill, false);
            float pingSize = 32 * BDArmorySettings.RWR_WINDOW_SCALE;

            for (int i = 0; i < pingsData.Length; i++)
            {
                RWRSignatureData currPing = pingsData[i];
                if (!currPing.exists) continue;
                Vector2 pingPosition = currPing.pingPosition;
                //pingPosition = Vector2.MoveTowards(displayRect.center, pingPosition, displayRect.center.x - (pingSize/2));
                Rect pingRect = new Rect(pingPosition.x - (pingSize / 2), pingPosition.y - (pingSize / 2), pingSize,
                    pingSize);

                if (currPing.signalType == RWRThreatTypes.MissileLock || currPing.signalType == RWRThreatTypes.MWS)
                {
                    GUI.DrawTexture(pingRect, rwrMissileTexture, ScaleMode.StretchToFill, true);
                }
                else
                {
                    bool isSelectedEmitter = selectedAntiradTarget != null && currPing.vessel != null && currPing.vessel == selectedAntiradTarget;
                    if (isSelectedEmitter)
                    {
                        // Highlight the manually selected emitter with a yellow ring (#834)
                        Color prevColor = GUI.color;
                        GUI.color = Color.yellow;
                        GUI.DrawTexture(new Rect(pingRect.x - 4, pingRect.y - 4, pingRect.width + 8, pingRect.height + 8), rwrDiamondTexture, ScaleMode.StretchToFill, true);
                        GUI.color = prevColor;
                    }
                    GUI.DrawTexture(pingRect, rwrDiamondTexture, ScaleMode.StretchToFill, true);
                    GUI.Label(pingRect, iconLabels[(int)currPing.signalType], rwrIconLabelStyle);
                    // Click an emitter to select/deselect it as the antirad missile target (#834)
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && pingRect.Contains(Event.current.mousePosition))
                    {
                        selectedAntiradTarget = isSelectedEmitter ? null : currPing.vessel;
                        Event.current.Use();
                    }
                }
            }

            int index = _missileLockHead;
            float currTime = Time.time;
            for (int i = 0; i < _missileLockSize; i++)
            {
                if (index >= missileLockData.Length)
                    index = 0;

                if (missileLockData[index].expirationTime - currTime < RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME * 0.5f)
                    continue;

                Vector2 pingPosition = missileLockData[index].pingPosition;
                ++index;

                //pingPosition = Vector2.MoveTowards(displayRect.center, pingPosition, displayRect.center.x - (pingSize/2));
                Rect pingRect = new Rect(pingPosition.x - (pingSize / 2), pingPosition.y - (pingSize / 2), pingSize,
                    pingSize);

                GUI.DrawTexture(pingRect, rwrMissileTexture, ScaleMode.StretchToFill, true);
            }

            // Tell the compiler to not worry about bounds checking
            for (int i = 0; i < MWSData.Length; i++)
            {
                // Actual end of for loop
                if (i + 1 > MWSSlots) break;
                Vector2 pingPosition = MWSData[i].pingPosition;
                //pingPosition = Vector2.MoveTowards(displayRect.center, pingPosition, displayRect.center.x - (pingSize/2));
                Rect pingRect = new Rect(pingPosition.x - (pingSize / 2), pingPosition.y - (pingSize / 2), pingSize,
                    pingSize);

                GUI.DrawTexture(pingRect, rwrMissileTexture, ScaleMode.StretchToFill, true);
            }

            index = _launchWarningsHead;
            for (int i = 0; i < _launchWarningsSize; i++)
            {
                if (index >= launchWarnings.Length)
                    index = 0;

                Vector2 pingPosition = launchWarnings[index].pingPosition;
                ++index;

                //pingPosition = Vector2.MoveTowards(displayRect.center, pingPosition, displayRect.center.x - (pingSize/2));
                Rect pingRect = new Rect(pingPosition.x - (pingSize / 2), pingPosition.y - (pingSize / 2), pingSize,
                    pingSize);
                GUI.DrawTexture(pingRect, rwrMissileTexture, ScaleMode.StretchToFill, true);
            }
            GUI.EndGroup();

            // Resizing code block.
            RWRresizeRect = new Rect(BDArmorySetup.WindowRectRwr.width - 18, BDArmorySetup.WindowRectRwr.height - 18, 16, 16);
            GUI.DrawTexture(RWRresizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && RWRresizeRect.Contains(Event.current.mousePosition))
            {
                resizingWindow = true;
            }
            else GUIUtils.DragWindow();

            if (Event.current.type == EventType.Repaint && resizingWindow)
            {
                if (Mouse.delta.x != 0 || Mouse.delta.y != 0)
                {
                    float diff = (Mathf.Abs(Mouse.delta.x) > Mathf.Abs(Mouse.delta.y) ? Mouse.delta.x : Mouse.delta.y) / BDArmorySettings.UI_SCALE_ACTUAL;
                    BDArmorySettings.RWR_WINDOW_SCALE = Mathf.Clamp(BDArmorySettings.RWR_WINDOW_SCALE + diff / RwrSize, BDArmorySettings.RWR_WINDOW_SCALE_MIN, BDArmorySettings.RWR_WINDOW_SCALE_MAX);
                    BDArmorySetup.ResizeRwrWindow(BDArmorySettings.RWR_WINDOW_SCALE);
                }
            }
            // End Resizing code.

            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectRwr);
            GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectRwr);
        }

        public static void PingRWR(Vessel v, Vector3 source, RWRThreatTypes type, float persistTime, Vessel vSource)
        {
            OnRadarPing?.Invoke(v, source, type, persistTime, vSource);
        }

        public static void PingRWR(Ray ray, float fov, RWRThreatTypes type, float persistTime, Vessel vSource)
        {
            using (var vessel = FlightGlobals.Vessels.GetEnumerator())
                while (vessel.MoveNext())
                {
                    if (vessel.Current == null || !vessel.Current.loaded) continue;
                    if (VesselModuleRegistry.IgnoredVesselTypes.Contains(vessel.Current.vesselType)) continue;
                    Vector3 dirToVessel = vessel.Current.CoM - ray.origin;
                    if (VectorUtils.Angle(ray.direction, dirToVessel) < fov * 0.5f)
                    {
                        PingRWR(vessel.Current, ray.origin, type, persistTime, vSource);
                    }
                }
        }

        public static void WarnMissileLaunch(Vector3 source, Vector3 direction, bool radarMissile, Vessel vSource)
        {
            OnMissileLaunch?.Invoke(source, direction, radarMissile, vSource);
        }
    }

    // Cut down version of TargetSignatureData
    public struct RWRSignatureData : IEquatable<RWRSignatureData>
    {
        public Vector3 geoPos;
        public bool exists;
        public float timeAcquired;
        public RadarWarningReceiver.RWRThreatTypes signalType;
        public Vector2 pingPosition;
        public Vessel vessel;
        public float expirationTime;

        public bool Equals(RWRSignatureData other)
        {
            return
                exists == other.exists &&
                geoPos == other.geoPos &&
                timeAcquired == other.timeAcquired;
        }

        public Vector3 position
        {
            get
            {
                return VectorUtils.GetWorldSurfacePostion(geoPos, FlightGlobals.currentMainBody);
            }
            set
            {
                geoPos = VectorUtils.WorldPositionToGeoCoords(value, FlightGlobals.currentMainBody);
            }
        }

        public RWRSignatureData(Vector3 _position, Vector2 _pingPosition, bool _exists, RadarWarningReceiver.RWRThreatTypes _signalType, Vessel _vessel, float lifeTime = RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME)
        {
            geoPos = VectorUtils.WorldPositionToGeoCoords(_position, FlightGlobals.currentMainBody);
            exists = _exists;
            timeAcquired = Time.time;
            signalType = _signalType;
            pingPosition = _pingPosition;
            vessel = _vessel;
            expirationTime = _exists ? (Time.time + lifeTime) : -1f;
        }

        public RWRSignatureData()
        {
            geoPos = Vector3.zero;
            exists = false;
            timeAcquired = -1;
            signalType = RadarWarningReceiver.RWRThreatTypes.None;
            pingPosition = Vector2.zero;
            vessel = null;
            expirationTime = -1f;
        }

        public static RWRSignatureData noTarget => new RWRSignatureData();

        public static void ResetRWRSDArray(ref RWRSignatureData[] tsdArray)
        {
            RWRSignatureData nullTarget = noTarget;
            for (int i = 0; i < tsdArray.Length; i++)
            {
                tsdArray[i] = nullTarget;
            }
        }
    }

    public static class RWRExtension
    {
        public static int ToBits(this RadarWarningReceiver.RWRThreatTypes[] rwrThreatTypes) => rwrThreatTypes.Aggregate(0, (val, rwr) => val |= rwr.ToBits());
        public static int ToBits(this RadarWarningReceiver.RWRThreatTypes rwr) => rwr != RadarWarningReceiver.RWRThreatTypes.None ? 1 << (int)rwr : 0; // None=-1 is the special case that equates to 0 (i.e., 1>>1).
    }
}
