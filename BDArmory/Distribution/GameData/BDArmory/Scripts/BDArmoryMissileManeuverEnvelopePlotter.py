import numpy as np
import matplotlib.pyplot as plt
import math
from KSPFloatCurveCalc import FloatCurve
from os import system
import configparser

## ---------------------------------- Instructions ---------------------------------- ##

# To run this code, install numpy and matplotlib in your Python install via the
# "py -[version] -m pip install numpy" and "py -[version] -m pip install matplotlib"
# commands. Ensure you are installing these on the right version of Python. For more
# detailed instructions, there are a number of tutorials on YouTube and Google.

# Edit the settings section to match your missile's parameters and your BD settings,
# then run the code.

# Advanced users may replace the atmospheric model with their own model. Note that
# the provided model is a simplified one-equation fit of Kerbin's atmosphere, up to
# 37,879.4 m of altitude.

# The code will output 4 graphs, the achievable gs at the given conditions, AoA limit,
# the required torque at the given conditions and the torque margin (the difference 
# between the required torque and the available torque). A 5th graph will be output if
# there are risky conditions in the expected operating envelope of the missile, where
# there is a minimal torque margin and the AoA limit is below 1°, where an excessive
# steerMult may cause the missile to oscillate and lose control.

## ---------------------------------------------------------------------------------- ##
## ------------------------------------ Settings ------------------------------------ ##
## ---------------------------------------------------------------------------------- ##

# These are BD Armory global settings, if you don't know what they are, don't change them
GLOBAL_LIFT_MULTIPLIER = 0.25
GLOBAL_DRAG_MULTIPLIER = 6

# Missile parameters
liftArea = 0.0020
dragArea = 0.0020
thrust = 25.0
mass = 0.152
maxTorque = 80
maxTorqueAero = 0.0
maxAoA = 45
gLimit = 40
gMargin = 0

# maxAltitude can be between 0 and 37879.4 m with the provided atmospheric model
maxAltitude = 35000
maxAirspeed = 1600

# numAlt and numAirspeed are the number of steps that should be used. For altitude,
# it's the steps from 0 m to maxAltitude, and for airspeed it's from 0 to maxAirspeed
# though not inclusive of 0 airspeed (since it provides a trivial answer)
# Using a 1 for either will provide a 2D graph at maxAltitude or maxAirspeed
numAlt = 50
numAirspeed = 1000

## ---------------------------------------------------------------------------------- ##
## -------------------------------- Atmospheric Model ------------------------------- ##
## ---------------------------------------------------------------------------------- ##

# This function returns the density for a given altitude, it can be replaced with
# any atmospheric model of your choice, however the function must work with numpy
# matrices, returning a matrix of the same shape.
def rhoFromAlt(alt):
    return (2.522601349 / (1 + np.exp(0.000200946 * alt + 0.0575766)))

## ---------------------------------------------------------------------------------- ##
## --------------------------------- Basic Functions -------------------------------- ##
## ---------------------------------------------------------------------------------- ##

DEG2RAD = np.pi / 180
RAD2DEG = 180 / np.pi

liftCurveKeys = [
    np.array([0, 0, 0.04375, 0.04375]),
    np.array([8, 0.35, 0.04801136, 0.04801136]),
    np.array([30, 1.5]),
    np.array([65, 0.6]),
    np.array([90, 0.7])
    ]

liftCurve = FloatCurve(liftCurveKeys)

dragCurveKeys = [
    np.array([0, 0.00215, 0, 0]),
    np.array([5, 0.00285, 0.0002775, 0.0002775]),
    np.array([30, 0.01, 0.0002142857, 0.01115385]),
    np.array([55, 0.3, 0.008434067, 0.008434067]),
    np.array([90, 0.5, 0.005714285, 0.005714285])
    ]

dragCurve = FloatCurve(dragCurveKeys)

def calcAeroPerf(q, liftArea, dragArea, thrust, mass, AoA):
    lift = q * liftArea * GLOBAL_LIFT_MULTIPLIER * liftCurve.Evaluate(AoA)
    drag = q * dragArea * GLOBAL_DRAG_MULTIPLIER * dragCurve.Evaluate(AoA)

    torque = lift * np.cos(AoA * DEG2RAD) + drag * np.sin(AoA * DEG2RAD)

    gAchieved = (lift + thrust * np.sin(AoA * DEG2RAD)) / (mass * 9.80665)

    return [gAchieved, torque]

## ---------------------------------------------------------------------------------- ##
## ------------------------------------ g Limiter ----------------------------------- ##
## ---------------------------------------------------------------------------------- ##

TRatioInflec1 = 1.181181181181181 # Thrust to Lift Ratio (at AoA of 30) where the maximum occurs after the 65 degree mark
TRatioInflec2 = 2.242242242242242 # Thrust to Lift Ratio (at AoA of 30) where a local maximum no longer exists, above this every section must be searched

AoACurveKeys = [
    np.array([0.0000000000, 30.0000000000, 5.577463, 5.577463]),
    np.array([0.7107107107, 33.9639639640, 6.24605, 6.24605]),
    np.array([1.5315315315, 39.6396396396, 8.396343, 8.396343]),
    np.array([1.9419419419, 43.6936936937, 12.36403, 12.36403]),
    np.array([2.1421421421, 46.6666666667, 19.63926, 19.63926]),
    np.array([2.2122122122, 48.3783783784, 34.71423, 34.71423]),
    np.array([2.2422422422, 49.7297297297, 44.99994, 44.99994])
    ]   # Floatcurve containing AoA of (local) max acceleration
        # for a given thrust to lift (at the max CL of 1.5 at 30 degrees of AoA) ratio. Limited to a max
        # of TRatioInflec2 where a local maximum no longer exists

AoACurve = FloatCurve(AoACurveKeys)

AoAEqCurveKeys = [
    np.array([1.1911911912, 89.6396396396, -53.40001, -53.40001]),
    np.array([1.3413413413, 81.6216216216, -49.69999, -49.69999]),
    np.array([1.5215215215, 73.3333333333, -37.62499, -37.62499]),
    np.array([1.7217217217, 67.4774774775, -24.31731, -24.31731]),
    np.array([1.9819819820, 62.4324324324, -24.09232, -24.09232]),
    np.array([2.1821821822, 56.6666666667, -48.1499, -48.1499]),
    np.array([2.2422422422, 52.6126126126, -67.49978, -67.49978])
    ]   # Floatcurve containing AoA after which the acceleration goes above
        # that of the local maximums'. Only exists between TRatioInflec1 and TRatioInflec2.

AoAEqCurve = FloatCurve(AoAEqCurveKeys)

gMaxCurveKeys = [
    np.array([0.0000000000, 1.5000000000, 0.8248255, 0.8248255]),
    np.array([1.2012012012, 2.4907813293, 0.8942869, 0.8942869]),
    np.array([1.9119119119, 3.1757276995, 1.019205, 1.019205]),
    np.array([2.2422422422, 3.5307206802, 1.074661, 1.074661])
    ]   # Floatcurve containing max acceleration times the mass (total force)
        # normalized by q*S*GLOBAL_LIFT_MULTIPLIER for TRatio between 0 and TRatioInflec2. Note that after TRatioInflec1
        # this becomes a local maxima not a global maxima. This is used to narrow down what part of the curve we should
        # solve on.

gMaxCurve = FloatCurve(gMaxCurveKeys)

# Linearized CL v.s. AoA curve to enable fast solving. Algorithm performs bisection using the fast calculations of the bounds
# and then performs a linear solve 
linAoA = [ 0, 10, 24, 30, 38, 57, 65, 90 ]
linCL = [ 0, 0.454444597111092, 1.34596044049850, 1.5, 1.38043381924198, 0.719566180758018, 0.6, 0.7 ]
# Sin at the points
linSin = [ 0, 0.173648177666930, 0.406736643075800, 0.5, 0.615661475325658, 0.838670567945424, 0.906307787036650, 1 ]
# Slope of CL at the intervals
linSlope = [ 0.0454444597111092, 0.0636797030991005, 0.0256732599169169, -0.0149457725947522, -0.0347825072886297, -0.0149457725947522, 0.004 ]
# y-Intercept of line at those intervals
linIntc = [ 0, -0.182352433879912, 0.729802202492494, 1.94837317784257, 2.70216909620991, 1.57147521865889, 0.34 ]

def getGLimit(q, mass, liftArea, thrust, gLim, margin, maxAoA):
    gLimited = False
    # Force required to reach g-limit
    gLim *= (mass * 9.80665)

    currAoA = maxAoA

    interval = 0

    # Factor by which to multiply the lift coefficient to get lift, it's the dynamic pressure times the lift area times
    # the global lift multiplier
    qSk = q * liftArea * GLOBAL_LIFT_MULTIPLIER

    currG = 0

    # If we're in the post thrust state
    if (thrust == 0):
        # If the maximum lift achievable is not enough to reach the request accel
        # the we turn to the AoA required for max lift
        if (gLim > 1.5 * qSk):
            currAoA = 30
        else:
            # Otherwise, first we calculate the lift in interval 2 (between 24 and 30 AoA)
            currG = linCL[2] * qSk; # CL(alpha)*qSk + thrust*sin(alpha)

            # If the resultant g at 24 AoA is < gLim then we're in interval 2
            if (currG < gLim):
                interval = 2
            else:
                # Otherwise check interval 1
                currG = linCL[1] * qSk
                
                if (currG > gLim):
                    # If we're still > gLim then we're in interval 0
                    interval = 0
                else:
                    # Otherwise we're in interval 1
                    interval = 1

            # Calculate AoA for G, since no thrust we can use the faster linear equation
            currAoA = calcAoAforGLinear(qSk, gLim, linSlope[interval], linIntc[interval], 0)

        # Are we gLimited?
        gLimited = currAoA < maxAoA
        return currAoA if gLimited else maxAoA
    else:
        # If we're under thrust, first calculate the ratio of Thrust to lift at max CL
        TRatio = thrust / (1.5 * qSk)

        # Initialize bisection limits
        LHS = 0
        RHS = 7

        if (TRatio < TRatioInflec2):
            # If we're below TRatioInflec2 then we know there's a local max
            currG = gMaxCurve.Evaluate(TRatio) * qSk

            if (TRatio > TRatioInflec1):
                # If we're above TRatioInflec1 then we know it's only a local max

                # First calculate the allowable force margin
                # This exists because drag gets very bad above the local max
                margin = max(margin, 0)
                margin *= mass

                if (gLim < currG + margin):
                    # If we're within the margin
                    if (currG > gLim):
                        # And our local max is > gLim, then we know that 
                        # there is a solution. Calculate the AoAMax
                        # where the local max occurs
                        AoAMax = AoACurve.Evaluate(TRatio)
                        
                        # And determine our right hand bound based on
                        # our AoAMax
                        if (AoAMax > linAoA[4]):
                            RHS = 5
                        elif (AoAMax > linAoA[3]):
                            RHS = 4
                        else:
                            RHS = 3
                    else:
                        # If our local max is < gLim then we can simply set
                        # our AoA to be the AoA of the local max
                        currAoA = AoACurve.Evaluate(TRatio)
                        gLimited = currAoA < maxAoA
                        return currAoA if gLimited else maxAoA
                else:
                    # If we're not within the margin then we need to consider
                    # the high AoA section. First calculate the absolute maximum
                    # g we can achieve
                    currG = 0.7 * qSk + thrust

                    # If the absolute maximum g we can achieve is not enough, then return
                    # the local maximum in order to preserve energy
                    if (currG < gLim):
                        currAoA = AoACurve.Evaluate(TRatio)
                        gLimited = currAoA < maxAoA
                        return currAoA if gLimited else maxAoA

                    # If we're within the limit, then find the AoA where the normal force
                    # once again reaches the local max value
                    AoAEq = AoAEqCurve.Evaluate(TRatio)

                    # And determine the left hand bound from there
                    if (AoAEq > linAoA[6]):
                        # If we're in the final section then just calculate it directly
                        currAoA = calcAoAforGNonLin(qSk, gLim, linSlope[6], linIntc[6], 0)
                        gLimited = currAoA < maxAoA
                        return currAoA if gLimited else maxAoA
                    elif (AoAEq > linAoA[5]):
                        LHS = 5
                    else:
                        LHS = 4
            else:
                # If we're not above TRatioInflec1 then we only have to consider the
                # curve up to the local max
                AoAMax = AoACurve.Evaluate(TRatio)

                # Determine the right hand bound for calculation
                if (gLim < currG):
                    if (AoAMax > linAoA[3]):
                        RHS = 4
                    else:
                        RHS = 3
                else:
                    gLimited = currAoA < maxAoA
                    return currAoA if gLimited else maxAoA
        else:
            # If we're above TRatioInflec2 then we have to search the whole thing, but past that ratio
            # the function is monotonically increasing so it's OK

            # That being said, first calculate the absolute maximum
            # g we can achieve
            currG = 0.7 * qSk + thrust

            # If the absolute maximum g we can achieve is not enough, then return
            # max AoA
            if (currG < gLim):
                return maxAoA

        currG = linCL[RHS] * qSk + thrust * linSin[RHS]
        if (currG < gLim):
            return maxAoA

        # Bisection search
        while ( (RHS - LHS) > 1):
            interval = math.floor(0.5 * (RHS + LHS))

            currG = linCL[interval] * qSk + thrust * linSin[interval]

            if (currG < gLim):
                LHS = interval
            else:
                RHS = interval

        if (LHS == 0):
            # If we're below 15 (here 10 degrees) then use the linear approximation for sin
            currAoA = calcAoAforGLinear(qSk, gLim, linSlope[LHS], linIntc[LHS], thrust)
        else:
            # Otherwise use the second order approximation centered at pi/2
            currAoA = calcAoAforGNonLin(qSk, gLim, linSlope[LHS], linIntc[LHS], thrust)
        
        gLimited = currAoA < maxAoA
        return currAoA if gLimited else maxAoA
    # Pseudocode / logic
    # If T = 0
    # We know it's in the first section. If m*gReq > (1.5*q*k*s) then set to min of maxAoA and 30 (margin?). If
    # < then we first make linear estimate, then solve by bisection of intervals first -> solve on interval.
    # If TRatio < TRatioInflec2
    # First we check the endpoints -> both gMax, and, if TRatio > TRatioInflec1, then 0.7*q*S*k + T (90 degree case).
    # If gMax > m*gReq then the answer < AoACurve -> Determine where it is via calculating the pre-calculated points
    # then seeing which one has gCalc > m*gReq, using the interval bounded by the point with gCalc > m*gReq on the
    # right end. Use bisection -> we know it's bounded at the RHS by the 38 or the 57 section. We can compare the
    # AoACurve with 38, if > 38 then use 57 as the bound, otherwise bisection with 38 as the bound. Using this to
    # determine which interval we're looking at, we then calc AoACalc. Return the min of maxAoA and AoACalc.
    # If gMax < m*gReq, then if TRatio < TRatioInflec1, set to min of AoACurve and maxAoA. If TRatio > TRatioInflec1
    # then we look at the 0.3*q*S*k + T. If < m*gReq then we'll set it to the min of maxAoA and either AoACurve or
    # 90, depends on the margin. See below. If > m*gReq then it's in the last two sections, bound by AoAEq on the LHS.
    # If AoAEq > 65, then we solve on the last section. If AoAEq < 65 then we check the point at AoA = 65 using the
    # pre-calculated values. If > m*gReq then we know that it's in the 57-65 section, otherwise we know it's in the
    # 65-90 section.
    # Consider adding a margin, if gMax only misses m*gReq by a little we should probably avoid going to the higher
    # angles as it adds a lot of drag. Maybe distance based? User settable?
    # If TRatio > TRatioInflec2 then we have a continuously monotonically increasing function
    # We use the fraction m*gReq/(0.3*q*S*k + T) to determine along which interval we should solve, noting that this
    # is an underestimate of the thrust required. (Maybe use arcsin for a more accurate estimate? Costly.) Then simply
    # calculate the pre-calculated value at the next point -> bisection and solve on the interval.
    
    # For all cases, if AoA < 15 then we can use the linear approximation of sin, if an interval includes both AoA < 15
    # and AoA > 15 then try < 15 (interval 2) first, then if > 15 try the non-linear starting from 15. Otherwise we use
    # non-linear equation.

# Calculate AoA for a given g loading, given m*g, the dynamic pressure times the lift area times the lift multiplier,
# the linearized approximation of the AoA curve (in slope, y-intercept form) and the thrust. Linear uses a linear
# small angle approximation for sin and non-linear uses a 2nd order approximation of sin about pi/2
def calcAoAforGLinear(qSk, mg, CLalpha, CLintc, thrust):
    return (mg - CLintc * qSk) / (CLalpha * qSk + thrust * DEG2RAD)

def calcAoAforGNonLin(qSk, mg, CLalpha, CLintc, thrust):
    CLalpha *= qSk
    return (2 * CLalpha + np.pi * thrust * DEG2RAD - 2 * np.sqrt(CLalpha * CLalpha + np.pi * thrust * DEG2RAD * CLalpha + 2 * thrust * (CLintc * qSk + thrust - mg) * DEG2RAD * DEG2RAD)) / (2 * thrust * DEG2RAD * DEG2RAD)

## ---------------------------------------------------------------------------------- ##
## --------------------------------- Torque Limiter --------------------------------- ##
## ---------------------------------------------------------------------------------- ##

torqueAoAReturnKeys = [
    np.array([2.6496350364963499, 88.7129999999999939, -106.9758, -106.9758]),
    np.array([2.73134328358208922, 79.9722000000000008, -70.59726, -70.59726]),
    np.array([3.14937759336099621, 65.6675999999999931, -28.9337, -28.9337]),
    np.array([3.52488687782805465, 56.7873000000000019, -31.87921, -31.87921]),
    np.array([3.69483568075117441, 49.9707000000000008, -61.73428, -61.73428]),
    np.array([3.76190476190476275, 44.3798999999999992, -83.35883, -18.59649]),
    np.array([3.83091787439613629, 43.0964999999999989, -23.74979, -23.74979]),
    np.array([3.92610837438423754, 40.3451999999999984, -28.9031, -28.9031])
    ]

torqueAoAReturn = FloatCurve(torqueAoAReturnKeys)

# Note we use linAoA for this as well
linLiftTorque = [ 0, 0.449212170675488687, 1.23071251302548967, 1.29903810567665712, 1.08779669420507852, 0.391903830317496704, 0.253570957044423284, 0 ]
linDragTorque = [ 0, 0.000748453415988048856, 0.00346671023416293559, 0.00499999999999927499, 0.0656669812489726473, 0.26524150275361541, 0.336257675049945692, 0.5 ]

# Slope of cos * CL at the intervals
linLiftTorqueSlope = [ 0.0449212178074, 0.0558214214286, 0.0113876666667, -0.026405125, -0.0366259562991, -0.0172916091591, -0.0101428382818 ]
# y-Intercept of line at those intervals
linLiftTorqueIntc = [ 0, -0.109002114286, 0.957408, 2.09119175, 2.47958333937, 1.37752555239, 0.91285544536 ]

# Slope of sin * CD at the intervals
linDragTorqueSlope = [ 0.000166666666667, 0.000166666666667, 0.000166666666667, 0.00691309375, 0.0107346842105, 0.009046, 0.00653472 ]
# y-Intercept of line at those intervals
linDragTorqueIntc = [ 0, 0, 0, -0.2023928125, -0.347613, -0.251358, -0.0881248 ]

DLRatioInflec1 = 2.63636363636363624
DLRatioInflec2 = 3.92610837438423754

torqueBounds = [-1, 7]
torqueAoABounds = [-1.0, -1.0, -1.0]

# Algorithm is similar to getGLimit, except in this case we only calculate which sections to search in whenever
# the liftArea and dragArea change. We define this using a set of numbers, torqueAoAReturn, the AoA at which torque goes past the local
# maximum which occurs at around 28° AoA, if this is set to -1, then we ONLY search the lower portion of the plot and torqueMaxLocal which
# gives the non-dim torque (which needs to be pre-multiplied by the SUM of liftArea * liftMult and dragArea * dragMult before being saved)
# which is +ve when there's a local maximum, it is set to the negative of the number when a local maximum does not exist, it is not set to
# -1 instead as it still provides a useful bisection point, with which we can determine the first LHS/RHS index.
#public static float[] linAoA = { 0f, 10f, 24f, 30f, 38f, 57f, 65f, 90f };
def setupTorqueAoALimit(liftArea, dragArea):
    global torqueBounds
    global torqueAoABounds

    # Drag / Lift ratio
    DL = (GLOBAL_DRAG_MULTIPLIER * dragArea)/(GLOBAL_LIFT_MULTIPLIER * liftArea)
    # The % contribution of drag, note that this will error out if there's no drag,
    # but that's not supposed to happen.
    SkR = DL / (DL + 1)

    # If we're above DLRationInflec2 then we must search the whole range of AoAs
    if (DL < DLRatioInflec2):
        # If we're below DLRatioInflec1 then we're bounded on the right by 30° 
        if (DL < DLRatioInflec1):
            torqueBounds = [3, -1]
        else:
            AoARHS = torqueAoAReturn.Evaluate(DL)
            if (AoARHS > linAoA[6]):
                torqueBounds = [6, 7]
            elif (AoARHS > linAoA[5]):
                torqueBounds = [5, 7]
            else:
                torqueBounds = [4, 7]

            torqueAoABounds[2] = AoARHS
        # This AoA happens to be a linear function of D/L
        torqueAoABounds[0] = 0.0307482 * DL + 28.49333
        # This non-dimensionalized torque happens to be a
        # linear function of SkR
        torqueAoABounds[1] = -1.30417 * SkR + 1.30879

    return [torqueBounds, torqueAoABounds]

def getTorqueAoALimit(q, liftArea, dragArea, maxTorque):
    # Technically not required, but in case anyone starts allowing for the CoL to vary
    CoLDist = 1

    # Divide out the dynamic pressure and CoLDist components of torque
    maxTorque /= q * CoLDist
    maxTorque *= 0.8 # Let's only go up to 80% of maxTorque to leave some leeway

    LHS = 0
    RHS = 7
    interval = 3

    # Drag and Lift Area multipliers
    dragSk = dragArea * GLOBAL_DRAG_MULTIPLIER
    liftSk = liftArea * GLOBAL_LIFT_MULTIPLIER

    # Here we store the AoA of local max torque, we set it to 180f as for the case
    # where the entire range must be searched, this gives the correct AoA
    currAoA = 180

    if (torqueBounds[0] > 0):
        # If we have a left torque bound then we don't need to search the entire range
        torqueMaxLocal = torqueAoABounds[0]
        currAoA = torqueAoABounds[1]

        if (torqueBounds[1] > 0):
            # If we have a right torque bound then we need to determine if we're searching
            # in the low AoA or the high AoA section, this is decided by if the maxTorque
            # is greater than torqueAoABounds times dragSk + liftSk
            if (maxTorque > torqueMaxLocal * (dragSk + liftSk)):
                # If maxTorque exceeds the max aerodynamic torque possible, then just return 180f
                if (maxTorque > (liftSk * linLiftTorque[7] + dragSk * linDragTorque[7])):
                    return 180

                LHS = torqueBounds[0]
                RHS = torqueBounds[1]
            else:
                RHS = torqueBounds[0]
        else:
            # If we don't have a right torque bound then we're bound only by the low
            # AoA section, and hence can return immediately if torque exceeds the localMax
            if (maxTorque > torqueMaxLocal * (dragSk + liftSk)):
                return 180

            # Otherwise we just search the low AoA portion
            RHS = torqueBounds[0]
    else:
        # If maxTorque exceeds the max aerodynamic torque possible, then just return 180f
        if (maxTorque > (liftSk * linLiftTorque[7] + dragSk * linDragTorque[7])):
            return 180

    currTorque = 0

    # Bisection search
    while ((RHS - LHS) > 1):
        interval = math.floor(0.5 * (RHS + LHS))

        currTorque = liftSk * linLiftTorque[interval] + dragSk * linDragTorque[interval]

        if (currTorque < maxTorque):
            LHS = interval
        else:
            RHS = interval

    currAoA = (maxTorque - (linLiftTorqueIntc[LHS] * liftSk + linDragTorqueIntc[LHS] * dragSk)) / (linLiftTorqueSlope[LHS] * liftSk + linDragTorqueSlope[LHS] * dragSk)
    return currAoA

## ---------------------------------------------------------------------------------- ##
## ----------------------------------- Calculation ---------------------------------- ##
## ---------------------------------------------------------------------------------- ##
def calculate():
    global plotWarn
    global gAchievedMat
    global AoAMat
    global torqueMat
    global torqueMarginMat
    global warnTorqueMat
    global turnRateMat

    gAchievedMat = np.zeros(qMat.shape)
    AoAMat = np.zeros(qMat.shape)
    torqueMat = np.zeros(qMat.shape)
    torqueMarginMat = np.zeros(qMat.shape)
    warnTorqueMat = np.zeros(qMat.shape)

    plotWarn = False

    for j in range(numAlt):
        for i in range(numAirspeed):
            currq = qMat[i, j]

            currMaxTorque = maxTorque + currq * maxTorqueAero

            currAoA = getGLimit(currq, mass, liftArea, thrust, gLimit, gMargin, maxAoA)

            currAoA = min(currAoA, getTorqueAoALimit(currq, liftArea, dragArea, currMaxTorque))
            
            [currgAchieved, currTorque] = calcAeroPerf(currq, liftArea, dragArea, thrust, mass, currAoA)

            gAchievedMat[i, j] = currgAchieved
            AoAMat[i, j] = currAoA
            torqueMat[i, j] = currTorque
            torqueMarginMat[i, j] = currMaxTorque - currTorque

            if (currTorque / currMaxTorque > 0.95 and currAoA < 1):
                plotWarn = True
                warnTorqueMat[i, j] = 1.0

    turnRateMat = 9.80665 * np.divide(gAchievedMat, speedMat) * RAD2DEG
    turnRateMat[speedMat < 100] = 0.0

## ---------------------------------------------------------------------------------- ##
## ------------------------------------ Plotting ------------------------------------ ##
## ---------------------------------------------------------------------------------- ##

def plot():
    if (numAirspeed > 1 and numAlt > 1):
        plt.figure(1)
        ax = plt.axes(projection = '3d')
        ax.plot_surface(altMat, speedMat, gAchievedMat, cmap=plt.cm.jet,
                        linewidth=0, antialiased=False)
        plt.title('Achievable g-loading')
        ax.set_xlabel('Altitude (m)')
        ax.set_ylabel('Airspeed (m/s)')
        ax.set_zlabel('Achievable gs')

        plt.figure(2)
        ax = plt.axes(projection = '3d')
        ax.plot_surface(altMat, speedMat, turnRateMat, cmap=plt.cm.jet,
                        linewidth=0, antialiased=False)
        plt.title('Instantaneous Turn Rate')
        ax.set_xlabel('Altitude (m)')
        ax.set_ylabel('Airspeed (m/s)')
        ax.set_zlabel('Turn Rate (°/s)')

        plt.figure(3)
        ax = plt.axes(projection = '3d')
        ax.plot_surface(altMat, speedMat, AoAMat, cmap=plt.cm.jet,
                        linewidth=0, antialiased=False)
        plt.title('AoA Limit')
        ax.set_xlabel('Altitude (m)')
        ax.set_ylabel('Airspeed (m/s)')
        ax.set_zlabel('AoA Limit (°)')

        plt.figure(4)
        ax = plt.axes(projection = '3d')
        ax.plot_surface(altMat, speedMat, torqueMat, cmap=plt.cm.jet,
                        linewidth=0, antialiased=False)
        plt.title('Required Torque')
        ax.set_xlabel('Altitude (m)')
        ax.set_ylabel('Airspeed (m/s)')
        ax.set_zlabel('Torque (kN-m)')

        plt.figure(5)
        ax = plt.axes(projection = '3d')
        ax.plot_surface(altMat, speedMat, torqueMarginMat, cmap=plt.cm.jet,
                        linewidth=0, antialiased=False)
        plt.title('Torque Margin')
        ax.set_xlabel('Altitude (m)')
        ax.set_ylabel('Airspeed (m/s)')
        ax.set_zlabel('Torque Margin (kN-m)')

        if (plotWarn):
            plt.figure(6)
            ax = plt.axes(projection = '3d')
            ax.plot_surface(altMat, speedMat, warnTorqueMat, cmap=plt.cm.jet,
                            linewidth=0, antialiased=False)
            plt.title('Warning! Potential Instability With High steerMult!')
            ax.set_xlabel('Altitude (m)')
            ax.set_ylabel('Airspeed (m/s)')
        
    elif (numAirspeed > 1):
        plt.figure(1)
        plt.plot(speedMat, gAchievedMat)
        plt.title(f'Achievable g-loading at Altitude {maxAltitude} m')
        plt.xlabel('Airspeed (m/s)')
        plt.ylabel('Achievable gs')

        plt.figure(2)
        plt.plot(speedMat, turnRateMat)
        plt.title(f'Instantaneous Turn Rate at Altitude {maxAltitude} m')
        plt.xlabel('Airspeed (m/s)')
        plt.ylabel('Turn Rate (°/s)')

        plt.figure(3)
        plt.plot(speedMat, AoAMat)
        plt.title(f'AoA Limit at Altitude {maxAltitude} m')
        plt.xlabel('Airspeed (m/s)')
        plt.ylabel('AoA Limit (°)')

        plt.figure(4)
        plt.plot(speedMat, torqueMat)
        plt.title(f'Required Torque at Altitude {maxAltitude} m')
        plt.xlabel('Airspeed (m/s)')
        plt.ylabel('Torque (kN-m)')

        plt.figure(5)
        plt.plot(speedMat, torqueMarginMat)
        plt.title(f'Torque Margin at Altitude {maxAltitude} m')
        plt.xlabel('Airspeed (m/s)')
        plt.ylabel('Torque (kN-m)')

        if (plotWarn):
            plt.figure(6)
            plt.plot(speedMat, warnTorqueMat)
            plt.title(f'Warning! Potential Instability With High steerMult! at Altitude {maxAltitude} m')
            plt.xlabel('Airspeed (m/s)')
    else:
        plt.figure(1)
        plt.plot(altMat[0], gAchievedMat[0])
        plt.title(f'Achievable g-loading at Airspeed {maxAirspeed} m/s')
        plt.xlabel('Altitude (m)')
        plt.ylabel('Achievable gs')

        plt.figure(2)
        plt.plot(altMat[0], turnRateMat[0])
        plt.title(f'Instantaneous Turn Rate at Airspeed {maxAirspeed} m/s')
        plt.xlabel('Altitude (m)')
        plt.ylabel('Turn Rate (°/s)')

        plt.figure(3)
        plt.plot(altMat[0], AoAMat[0])
        plt.title(f'AoA Limit at Airspeed {maxAirspeed} m/s')
        plt.xlabel('Altitude (m)')
        plt.ylabel('AoA Limit (°)')

        plt.figure(4)
        plt.plot(altMat[0], torqueMat[0])
        plt.title(f'Required Torque at Airspeed {maxAirspeed} m/s')
        plt.xlabel('Altitude (m)')
        plt.ylabel('Torque (kN-m)')

        plt.figure(5)
        plt.plot(altMat[0], torqueMarginMat[0])
        plt.title(f'Torque Margin at Airspeed {maxAirspeed} m/s')
        plt.xlabel('Altitude (m)')
        plt.ylabel('Torque (kN-m)')

        if (plotWarn):
            plt.figure(6)
            plt.plot(altMat[0], warnTorqueMat[0])
            plt.title(f'Warning! Potential Instability With High steerMult! at Airspeed {maxAirspeed} m/s')
            plt.xlabel('Altitude (m)')
    plt.draw()
    plt.pause(0.05)
    input('Press Enter to close plots...')
    plt.close('all')

## ---------------------------------------------------------------------------------- ##
## -------------------------------------- Setup ------------------------------------- ##
## ---------------------------------------------------------------------------------- ##

def setup():
    global altMat
    global speedMat
    global qMat
    global maxAltitude

    if (maxAltitude > 37879.4):
        print('Warning! Max altitude > atmospheric model, reducing to 37879.4 m.\n'
              'If you have altered the atmospheric model, please change this check in the setup() function.')
        maxAltitude = 37879.4

    if (numAlt > 1):
        altArr = np.linspace(0, maxAltitude, numAlt)
    else:
        altArr = maxAltitude

    if (numAirspeed > 1):
        speedArr = np.linspace(maxAirspeed / numAirspeed, maxAirspeed, numAirspeed)
    else:
        speedArr = maxAirspeed

    altMat, speedMat = np.meshgrid(altArr, speedArr)
    rhoMat = rhoFromAlt(altMat)
    qMat = 0.5 * np.multiply(rhoMat, np.square(speedMat))

    setupTorqueAoALimit(liftArea, dragArea)

## ---------------------------------------------------------------------------------- ##
## ------------------------------------- Display ------------------------------------ ##
## ---------------------------------------------------------------------------------- ##

def display():
    system("clear||cls")
    print(f'## ---------------------------------------------------------------------------------- ##\n'
          f'## --------------------- BDA+ Missile Maneuver Envelope Plotter --------------------- ##\n'
          f'## ----------------------------------- by: BillNye ---------------------------------- ##\n'
          f'## ---------------------------------------------------------------------------------- ##\n'
          f'\n'
          f'Current Missile Parameters:\n'
          f'liftArea                = {liftArea}\n'
          f'dragArea                = {dragArea}\n'
          f'thrust                  = {thrust}\n'
          f'mass                    = {mass}\n'
          f'maxTorque               = {maxTorque}\n'
          f'maxTorqueAero           = {maxTorqueAero}\n'
          f'maxAoA                  = {maxAoA}\n'
          f'gLimit                  = {gLimit}\n'
          f'gMargin                 = {gMargin}\n'
          f'\n'
          f'Current BDA+ Settings:\n'
          f'GLOBAL_LIFT_MULTIPLIER  = {GLOBAL_LIFT_MULTIPLIER}\n'
          f'GLOBAL_DRAG_MULTIPLIER  = {GLOBAL_DRAG_MULTIPLIER}\n'
          f'\n'
          f'Current Calculation Settings:\n'
          f'maxAltitude             = {maxAltitude} m\n'
          f'maxAirspeed             = {maxAirspeed} m/s\n'
          f'numAlt                  = {numAlt}\n'
          f'numAirspeed             = {numAirspeed}\n'
          f'\n'
          f'Ready to Plot           = {calculationComplete}\n')

## ---------------------------------------------------------------------------------- ##
## -------------------------------------- Main -------------------------------------- ##
## ---------------------------------------------------------------------------------- ##

setupComplete = False
calculationComplete = False
config = configparser.ConfigParser()

while (True):
    display()

    if (not setupComplete):
        setup()
        setupComplete = True

    command = input().lower()
    display()

    match command:
        case 'calculate' | 'c':
            print('Calculation in progress...')
            calculate()
            calculationComplete = True
        case 'plot' | 'p':
            if (not calculationComplete):
                print('Calculation in progress...')
                calculate()
            plot()
        case 'liftarea' | 'la':
            liftArea = float(input('liftArea = '))
            calculationComplete = False
        case 'dragarea' | 'da':
            dragArea = float(input('dragArea = '))
            calculationComplete = False
        case 'thrust' | 't':
            thrust = float(input('thrust = '))
            calculationComplete = False
        case 'mass' | 'm':
            mass = float(input('mass = '))
            calculationComplete = False
        case 'maxtorque' | 'mt':
            maxTorque = float(input('maxTorque = '))
            calculationComplete = False
        case 'maxtorqueaero' | 'mta':
            maxTorqueAero = float(input('maxTorqueAero = '))
            calculationComplete = False
        case 'maxaoa' | 'aoa':
            maxAoA = float(input('maxAoA = '))
            calculationComplete = False
        case 'glimit' | 'g':
            gLimit = float(input('gLimit = '))
            calculationComplete = False
        case 'gmargin' | 'gm':
            gMargin = float(input('gMargin = '))
            calculationComplete = False
        case 'global_lift_multiplier' | 'glm':
            GLOBAL_LIFT_MULTIPLIER = float(input('GLOBAL_LIFT_MULTIPLIER = '))
            calculationComplete = False
        case 'global_drag_multiplier' | 'gdm':
            GLOBAL_DRAG_MULTIPLIER = float(input('GLOBAL_DRAG_MULTIPLIER = '))
            calculationComplete = False
        case 'maxaltitude' | 'alt':
            maxAltitude = float(input('maxAltitude = '))
            calculationComplete = False
            setupComplete = False
        case 'maxairspeed' | 'speed':
            maxAirspeed = float(input('maxAirspeed = '))
            calculationComplete = False
            setupComplete = False
        case 'numalt' | 'nalt':
            numAlt = int(input('numAlt = '))
            calculationComplete = False
            setupComplete = False
        case 'numairspeed' | 'nspeed':
            numAirspeed = int(input('numAirspeed = '))
            calculationComplete = False
            setupComplete = False
        case 'load' | 'l':
            filename = input('filename = ') + '.ini'
            success = True
            try:
                config.read(filename)
            except:
                print(f'file: "{filename}" is formatted incorrectly!')
                success = False
                input('Press Enter to continue...')

            if (len(config.sections()) == 0):
                print(f'file: "{filename}" does not exist!')
                success = False
                input('Press Enter to continue...')

            if (success):
                if config.has_section('Missile'):
                    calculationComplete = False
                    for key in config['Missile']:
                        match key.lower():
                            case 'liftarea':
                                liftArea = float(config['Missile'][key])
                            case 'dragarea':
                                dragArea = float(config['Missile'][key])
                            case 'thrust':
                                thrust = float(config['Missile'][key])
                            case 'mass':
                                mass = float(config['Missile'][key])
                            case 'maxtorque':
                                maxTorque = float(config['Missile'][key])
                            case 'maxtorqueaero':
                                maxTorqueAero = float(config['Missile'][key])
                            case 'maxaoa':
                                maxAoA = float(config['Missile'][key])
                            case 'glimit':
                                gLimit = float(config['Missile'][key])
                            case 'gmargin':
                                gMargin = float(config['Missile'][key])
                
                if config.has_section('BDSettings'):
                    calculationComplete = False
                    for key in config['BDSettings']:
                        match key.lower():
                            case 'global_lift_multiplier':
                                GLOBAL_LIFT_MULTIPLIER = float(config['BDSettings'][key])
                            case 'global_drag_multiplier':
                                GLOBAL_DRAG_MULTIPLIER = float(config['BDSettings'][key])

                if config.has_section('EnvelopeSettings'):
                    calculationComplete = False
                    setupComplete = False
                    for key in config['EnvelopeSettings']:
                        match key.lower():
                            case 'maxaltitude':
                                maxAltitude = float(config['EnvelopeSettings'][key])
                            case 'maxairspeed':
                                maxAirspeed = float(config['EnvelopeSettings'][key])
                            case 'numalt':
                                numAlt = int(config['EnvelopeSettings'][key])
                            case 'numairspeed':
                                numAirspeed = int(config['EnvelopeSettings'][key])
        case 'save' | 's':
            filename = input('filename = ')
            config = configparser.ConfigParser()

            config['BDSettings'] = {'GLOBAL_LIFT_MULTIPLIER': GLOBAL_LIFT_MULTIPLIER,
                                    'GLOBAL_DRAG_MULTIPLIER': GLOBAL_DRAG_MULTIPLIER}

            config['Missile'] = {'liftArea': liftArea,
                                 'dragArea': dragArea,
                                 'thrust': thrust,
                                 'mass': mass,
                                 'maxTorque': maxTorque,
                                 'maxTorqueAero': maxTorqueAero,
                                 'maxAoA': maxAoA,
                                 'gLimit': gLimit,
                                 'gMargin': gMargin}

            config['EnvelopeSettings'] = {'maxAltitude': maxAltitude,
                                          'maxAirspeed': maxAirspeed,
                                          'numAlt': numAlt,
                                          'numAirspeed': numAirspeed}

            with open(filename + '.ini', 'w') as configfile:
                config.write(configfile)
        case 'help' | 'h':
            print('Available Commands:\n'
                  '"Field Name"    - allows you to edit the value of the field, E.G. "liftArea".\n'
                  '"calculate"     - performs calculation.\n'
                  '"plot"          - plots data, performs calculation if needed.\n'
                  '"load"          - loads parameters from an ini file, you will be prompted for a filename.\n'
                  '"save"          - saves parameters to an ini file, you will be prompted for a filename.\n'
                  '"quit"          - exits the program.\n'
                  'Note shorthands are available, usually the first letter of any command. Fields use abbreviations like "la" for liftArea, and "AoA for maxAoA.')
            input("Press Enter to continue...")
        case 'quit' | 'exit' | 'x' | 'q':
            break
        case _:
            print('Unknown command. Use "help" to get available commands.')
            input("Press Enter to continue...")

## ---------------------------------------------------------------------------------- ##
## ---------------------------------------------------------------------------------- ##
## ---------------------------------------------------------------------------------- ##