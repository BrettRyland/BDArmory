﻿#if DEBUG

// This will only be live in debug builds
using System;
using UnityEngine;

using BDArmory.Settings;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KrakensbaneDebug : MonoBehaviour
    {
        float lastShift = 0;

        void FixedUpdate()
        {
            if (!FloatingOrigin.Offset.IsZero())
                lastShift = Time.time;
        }

        void OnGUI()
        {
            if (BDArmorySettings.DEBUG_TELEMETRY)
            {
                var frameVelocity = Krakensbane.GetFrameVelocityV3f();
                //var rFrameVelocity = FlightGlobals.currentMainBody.getRFrmVel(Vector3d.zero);
                //var rFrameRotation = rFrameVelocity - FlightGlobals.currentMainBody.getRFrmVel(VectorUtils.GetUpDirection(Vector3.zero));
                GUI.Label(new Rect(10, 150, 400, 400),
                    $"Frame velocity: {frameVelocity.magnitude} ({frameVelocity}){Environment.NewLine}"
                    + $"FO offset: {(Vector3)FloatingOrigin.Offset:G3}{Environment.NewLine}"
                    + $"N-Kb offset: {(Vector3)FloatingOrigin.OffsetNonKrakensbane:G3}{Environment.NewLine}"
                    + $"Last offset {Time.time - lastShift}s ago{Environment.NewLine}"
                    + $"Local vessel speed: {FlightGlobals.ActiveVessel.rb_velocity.magnitude}, ({FlightGlobals.ActiveVessel.rb_velocity}){Environment.NewLine}"
                    //+ $"Reference frame speed: {rFrameVelocity}{Environment.NewLine}"
                    //+ $"Reference frame rotation speed: {rFrameRotation}{Environment.NewLine}"
                    //+ $"Reference frame angular speed: {rFrameRotation.magnitude / Mathf.PI * 180}{Environment.NewLine}"
                    //+ $"Ref frame is {(FlightGlobals.RefFrameIsRotating ? "" : "not ")}rotating{Environment.NewLine}"
                    );
            }
        }
    }
}

#endif
