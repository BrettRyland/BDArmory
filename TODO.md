### Bugs (are these still valid?)
- RemoveAllVessels can sometimes get stuck if the Kraken breaks the view frustrum due to SoI changes. Add a timeout and warning, possibly also an option to auto-quit if it breaks.
- Auto-tuning with numeric input fields enabled in the AI GUI won't let the values change
- Changing the slider resolution sometimes triggers clamping of unclamped values
- Taking off with the global 'P' button for two VTOL craft on the runway disables their engines!
- WM without AI or with stationary ground AI sometimes just sits there without attacking valid targets.
- Check whether `base.OnStart(state);` in `MissileLauncher.OnStart` can be re-enabled and some common stuff be moved to MissileBase.cs.
- Improve custom turret aiming.
- Rewrite the waypoints mode competition/tournament code to get rid of the remote orchestration (TournamentCoordinator) stuff.
	- Add a racing grid spawn layout MxN facing the first waypoint and get custom spawn templates working with waypoints.
- Sometimes `((UI_ControlXXX)Fields[nameof(xxx)]).uiControlFlight` gives an invalid cast exception. This seems to occur randomly, but could be related to load order perhaps?
- Clean up invalid UTF-8 chars by searching for `[^\x00-\x7f±°ñ—α→θψφρqω₀π²·δ↔∫•∈"γgμν‽Δ↕]` (add more to exclude as necessary). Ignore localisation files.

### TODO (smaller items and specific requests / higher priority)
- Fix bugs
	- Sometimes the field toggles in ModuleWeapon (and elsewhere) throw InvalidCast exceptions on startup. Suspect a race condition.
- Finish Gauntlet tournament heats if only opponent craft are left as only relative ranking of variants is relevant.
- Resource stealing of integer amounts should consider integer amounts per container, not overall.
- Cts spawn with NPCs

- Wiki entries
	- Auto-Tuning

- Requests from discord:
	- Elevation offset for the wing command formation editor.
	- Better heuristic to avoid dropping bombs when way off target.

	- ? Add an action group trigger to the WM based on the current target being an enemy vessel within a custom distance. - Make it a collapsable section of custom triggers to include other conditions later.
	- Artillery aiming support
	- Lift stacking improvements with logical wing segments
	- Add a distance based modifier to PID: lower P, higher D at longer distances.
	- Add a Panic Button to the AI that triggers an action group, triggered by:
		- Being in a flat spin for X seconds while below min altitude and less than Y seconds from impact.
		- Being stalled for X seconds while below min altitude and less than Y seconds from impact. — define "stalled"
		- Entering evasion.
	- Smart part that can trigger an action group when one of the specified parts gets below X% HP.
		- Would have to work similarly to the KAL to remember which parts it should affect/monitor.
	- Scope view for aiming tank turrets (similar to the targeting pod, but more direct), maybe holding a button adjusts the camera zoom based on the distance to the target?
	- Team continuous spawning (could be done in multiple ways — requires UI settings to configure it):
		- NvN...vN where each team replenishes craft from their pool.
		- NvN...vN where a new team spawns once one team is dead.

- Ilya_G requests:
	- Omni-radars to include a radiation pattern so that they don't see well along the dipole axis.
	- Switchable ammo types in-the-field for large gun types
		- include a significant delay when doing so (e.g., 2-5x the reload time for reconfiguring them)
		- would require manual intervention, unless some good way for the AI to decide to switch ammo is added
	- Seismic charges - needs Concodroid's model.
	- Difficulty settings possibilities:
		- Makes it easier for human vs AI combat.
		- Adds "measurement" noise to the target's position, velocity and acceleration
		- AI lacking acceleration info for targeting.
		- Precision reduction option in aiming guns/guiding missiles.
		- Laser turrets will still be deadly accurate (increasing maxDeviation would amount to the same thing as targeting jitter).
		- Add noise (fn of game time, not proper random) to targeting info.
		- Multiply pos, vel, acc by 1+sin(t)/X for X=10, 100, etc. to simulate sampling noise. It doesn't need to be game time, but something related to the vessel (e.g., speed + time)

- Improve Immelmann angle / target behind logic.
- Add tooltips to settings.
- Fix the piñata spawning logic - spawn the piñata(s) separately after circular spawning has occured.
- Add NPC and piñata support for single competitions as well (currently they're only supported in tournaments)
	- Add "role" option in the VM for specifying PC, NPC, piñata, etc.
- Figure out why bullet hole decals are frequently offset behind the craft. - krakensbane or flightintegrator at time of decal attachment?
- Inertial correction to pitch, roll, yaw errors for PID calcuations. Rotate the vessel reference transform first, computing debugPos2 from the top 
- Low altitude AI setting should be aware of killer GM low altitude.
- Memory for AI state so that it can resume once finished extending/evading instead of just scanning for new targets.
- Tag mode should disable team icons to get colours right
- Improve the VTOL AI:
	- Terrain avoidance
	- Other logic from the pilot AI.

- BDAVesselMover
	- Camera behaviour is weird if the mouse is over various windows, also when CameraTools is enabled above 100km — both are likely related to krakensbane
	- PRE can sometimes break KSP — not sure there's much we can do? maybe check if something steals the camera and switch back again?


### Ideas (more general things / lower priority)
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
	- Boss fight tournament mode
- Record starting conditions for bullets, position every 1000m and time and position of first impact in vessel traces. Also, bullet type. This should be sufficient for approximate curves in blender and colours, etc. can be found from the configs.
- Add a max morgue capacity and recycle kerbals once it's full. Regen the main 4 and discard the others? Would need special handling for custom kerbals.
- Profile the infinite ordnance option for spawning missile parts. "I wonder if it's possible to avoid a lot of the spawning cost and memory leakage by detaching the Vessel component from the missile prior to getting destroyed, packing and disabling it, then attaching, unpacking and enabling it on a new missile? Something to look at in the future..."
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
- Once the final target has been acquired in aiming, do a simulation with raycasts to check for obstacles in the way. If the target is blocked, then give it a modifier for target selection in the future that slowly recovers.
- Kerbal Safety: if a NaN orbit is detected, try setting the orbit of the kerbal based on the orbit of the part it left.


- See https://github.com/BrettRyland/BDArmory/issues/50