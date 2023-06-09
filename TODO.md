### Bugs
- Auto-tuning with numeric input fields enabled in the AI GUI won't let the values change
- Changing the slider resolution sometimes triggers clamping of unclamped values
- HP of asteroids in the SPH is wrong.


### TODO (smaller items and specific requests / higher priority)
- Fix bugs

- Wiki entries
    - Auto-Tuning

- ? Add an action group trigger to the WM based on the current target being an enemy vessel within a custom distance. - Make it a collapsable section of custom triggers to include other conditions later.
- Artillery aiming support
- Lift stacking improvements with logical wing segments
- Add a distance based modifier to PID: lower P, higher D at longer distances.
- Add a Panic Button to the AI that triggers an action group, triggered by:
    - Being in a flat spin for X seconds while below min altitude and less than Y seconds from impact.
    - Being stalled for X seconds while below min altitude and less than Y seconds from impact. — define "stalled"

- Gauntlet mode doesn't properly randomise teams/spawning? - check
- Fix the piñata spawning logic - spawn the piñata(s) separately after circular spawning has occured.
- Motherships branch
- Inertial correction to pitch, roll, yaw errors for PID calcuations. Rotate the vessel reference transform first, computing debugPos2 from the top 
- Preallocate the explosion merge buffer and reuse it.
- If re-assign teams is disabled, custom spawn templates should use the team name of the first vessel in each team as the team name.
- Fix the AIGUI slider limits and increments for the variable precision slider in the PAW
- Low altitude AI setting should be aware of killer GM low altitude.
- Adjust explosions to maintain velocity or not based on atmospheric density.
- Add VTOL AI to the AI GUI.
- Proper 3-axis PID sliders + single PID with axis weighting
- Memory for AI state so that it can resume once finished extending/evading instead of just scanning for new targets.
- KerbalSafety - why are some kerbals not registering/deploying chutes
- Tag mode should disable team icons to get colours right
- Improve the VTOL AI:
    - Terrain avoidance
    - Other logic from the pilot AI.

- BDAVesselMover
    - Camera behaviour is weird if the mouse is over various windows, also when CameraTools is enabled above 100km — both are likely related to krakensbane
    - PRE can sometimes break KSP — not sure there's much we can do? maybe check if something steals the camera and switch back again?


### Ideas (more general things / lower priority)
- Meteor Cannon: Summons an asteroid just in front of the vessel, then accelerates it to ~1000m/s (10x force pushing asteroid, 1x force pushing back on vessel)
    - Meteor Cannon class has its own selection of meteors that it randomly selects from when instantiating it. Generate at start and replenish as necessary.
- Add a PID_NeuralCoprocessor — A small FC neural network with configurable depth to modify the PID by ±pid (separate scale per channel) that learns when enabled
- AutoPilot:
    - On takeoff, look diagonally down and turn if the terrain normal is too steep.
- Autotuning:
    - Make the fly-to points dynamic instead of static (e.g., move sideways at fixed velocity) to avoid under-tuning I.
- Evasion/Strafing
    - When attacking a ground target with a turreted gun with at least 90° yaw, aim to circle around at ~2*turn radius at default altitude instead of strafing it directly. Adjust for min/max gun range. Don't use strafing speed (use cruise speed?).
- Waypoints
    - Use a spline between current position and velocity, waypoint and next waypoint 
- Add BDA's FlyToPosition as the first flight controller in MouseAimFlight via reflection.
- Tournaments
    - Add in-built score and ranked tournaments.
    - Add pop-up for entering score weights in the vessel switcher and sort by score instead of just hits. Right click sorting button should toggle this pop-up
    - This would allow for ranked tournaments and other tournament styles
    - Add a randomised vs ranked toggle for heat generation.
        - ranked => dynamic round allocation instead of pre-determined.
        - In ranked mode the deficit should be distributed amongst the top ranked heats.
    - Boss fight tournament mode
- Record starting conditions for bullets, position every 1000m and time and position of first impact in vessel traces. Also, bullet type. This should be sufficient for approximate curves in blender and colours, etc. can be found from the configs.
- Add a max morgue capacity and recycle kerbals once it's full. Regen the main 4 and discard the others? Would need special handling for custom kerbals.
- Profile the infinite ordinance option for spawning missile parts. "I wonder if it's possible to avoid a lot of the spawning cost and memory leakage by detaching the Vessel component from the missile prior to getting destroyed, packing and disabling it, then attaching, unpacking and enabling it on a new missile? Something to look at in the future..."
- Strafing planes are wobbly initially (maybe at low speeds in general?)


### Older notes (may not be still valid)
- Check the physx branch for changes related to using accelerated time.
- Time scaling unpauses the game and is undone by manually pausing/unpausing.
- Allow parsing multiple tournaments as a single large tournament
- Add a auto-link function/option (UI toggle + action group options) to the radar data receiver.
- Remove dead kerbals from the roster
- Completely disable ramming logic and scores when "disable ramming" is globally enabled
- using the clamp/unclamp option from the AI tab UI resets unclamped values to clamped maximum when re-clamping (check: is this still an issue?)
- Add a tournament option for having one "boss" team that each other team fights each round
- Do the same vessel-relative checks for rockets
- Once the final target has been acquired in aiming, do a simulation with raycasts to check for obstacles in the way. If the target is blocked, then give it a modifier for target selection in the future that slowly recovers.
- Kerbal Safety: if a NaN orbit is detected, try setting the orbit of the kerbal based on the orbit of the part it left.


- See https://github.com/BrettRyland/BDArmory/issues/50