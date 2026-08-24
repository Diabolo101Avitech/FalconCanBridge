using System.Runtime.InteropServices;

namespace FalconCanBridge.Simulators.Falcon4.Native;

/// <summary>
/// Byte-for-byte mirror of lightningstools/F4SharedMem's <c>BMS4FlightData</c> struct (see
/// src/lightningstools/F4SharedMem/Headers/BMS4FlightData.cs in this repo), which is the
/// community-maintained reference for the layout of the primary "FalconSharedMemoryArea"
/// shared-memory block BMS publishes.
///
/// This struct is never used to read shared memory directly - no P/Invoke, no
/// <see cref="Marshal.PtrToStructure(System.IntPtr, System.Type)"/> happens here. It exists purely
/// so <see cref="Marshal.OffsetOf{T}(string)"/> can compute the byte offset of every field for us
/// at class-init time (see <see cref="Falcon4NativeFieldMapBuilder"/>), instead of hand-typing
/// offsets into JSON - which is exactly how this project's previous best-effort field table went
/// stale/wrong in the first place. As long as this struct's field order, types, and
/// [MarshalAs]/[StructLayout] attributes are kept identical to the upstream source, the computed
/// offsets are correct by construction - no manual arithmetic involved, and nothing to keep in
/// sync besides this file if lightningstools ever revises the layout.
///
/// Field names and units are kept exactly as upstream (see the original comments there) so the
/// generated telemetry signal names stay traceable back to the source struct.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct BMS4FlightDataNative
{
    public const int MaxRwrObjects = 40; // FlightData.MAX_RWR_OBJECTS upstream

    // These are outputs from the sim
    public float x;            // Ownship North (Ft)
    public float y;            // Ownship East (Ft)
    public float z;            // Ownship Down (Ft)
    public float xDot;         // Ownship North Rate (ft/sec)
    public float yDot;         // Ownship East Rate (ft/sec)
    public float zDot;         // Ownship Down Rate (ft/sec)
    public float alpha;        // Ownship AOA (Degrees)
    public float beta;         // Ownship Beta (Degrees)
    public float gamma;        // Ownship Gamma (Radians)
    public float pitch;        // Ownship Pitch (Radians)
    public float roll;         // Ownship Roll (Radians)
    public float yaw;          // Ownship Yaw (Radians)
    public float mach;         // Ownship Mach number
    public float kias;         // Ownship Indicated Airspeed (Knots)
    public float vt;           // Ownship True Airspeed (Ft/Sec)
    public float gs;           // Ownship Normal Gs
    public float windOffset;   // Wind delta to FPM (Radians)
    public float nozzlePos;    // Ownship engine nozzle percent open (0-100)
    public float internalFuel; // Ownship internal fuel (Lbs)
    public float externalFuel; // Ownship external fuel (Lbs)
    public float fuelFlow;     // Ownship fuel flow (Lbs/Hour)
    public float rpm;          // Ownship engine rpm (Percent 0-103)
    public float ftit;         // Ownship Forward Turbine Inlet Temp (Degrees C)
    public float gearPos;      // Ownship Gear position 0 = up, 1 = down
    public float speedBrake;   // Ownship speed brake position 0 = closed, 1 = 60 Degrees open
    public float epuFuel;      // Ownship EPU fuel (Percent 0-100)
    public float oilPressure;  // Ownship Oil Pressure (Percent 0-100)
    public uint lightBits;     // Cockpit Indicator Lights, one bit per bulb - see LightBits enum

    // Inputs. NB: do not work when TrackIR device is enabled; need '-head' command line parameter
    public float headPitch;    // Head pitch offset from design eye (radians)
    public float headRoll;     // Head roll offset from design eye (radians)
    public float headYaw;      // Head yaw offset from design eye (radians)

    public uint lightBits2;    // Cockpit Indicator Lights - see LightBits2 enum
    public uint lightBits3;    // Cockpit Indicator Lights - see LightBits3 enum

    public float ChaffCount;   // Number of Chaff left
    public float FlareCount;   // Number of Flare left

    public float NoseGearPos;  // Position of the nose landing gear
    public float LeftGearPos;  // Position of the left landing gear
    public float RightGearPos; // Position of the right landing gear

    public float AdiIlsHorPos; // Position of horizontal ILS bar
    public float AdiIlsVerPos; // Position of vertical ILS bar

    public int courseState;    // HSI_STA_CRS_STATE
    public int headingState;   // HSI_STA_HDG_STATE
    public int totalStates;    // HSI_STA_TOTAL_STATES; never set

    public float courseDeviation;     // HSI_VAL_CRS_DEVIATION
    public float desiredCourse;       // HSI_VAL_DESIRED_CRS
    public float distanceToBeacon;    // HSI_VAL_DISTANCE_TO_BEACON
    public float bearingToBeacon;     // HSI_VAL_BEARING_TO_BEACON
    public float currentHeading;      // HSI_VAL_CURRENT_HEADING
    public float desiredHeading;      // HSI_VAL_DESIRED_HEADING
    public float deviationLimit;      // HSI_VAL_DEV_LIMIT
    public float halfDeviationLimit;  // HSI_VAL_HALF_DEV_LIMIT
    public float localizerCourse;     // HSI_VAL_LOCALIZER_CRS
    public float airbaseX;            // HSI_VAL_AIRBASE_X
    public float airbaseY;            // HSI_VAL_AIRBASE_Y
    public float totalValues;         // HSI_VAL_TOTAL_VALUES; never set

    public float TrimPitch;  // Value of trim in pitch axis, -0.5 to +0.5
    public float TrimRoll;   // Value of trim in roll axis, -0.5 to +0.5
    public float TrimYaw;    // Value of trim in yaw axis, -0.5 to +0.5

    public uint hsiBits;     // HSI flags - see HsiBits enum

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public DedPflLineNative[] DEDLines;  // 25 usable chars
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public DedPflLineNative[] Invert;     // inversion marker per DED char
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public DedPflLineNative[] PFLLines;  // 25 usable chars
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public DedPflLineNative[] PFLInvert; // inversion marker per PFL char

    public int UFCTChan;
    public int AUXTChan;

    public int RwrObjectCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRwrObjects)] public int[] RWRsymbol;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRwrObjects)] public float[] bearing;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRwrObjects)] public uint[] missileActivity;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRwrObjects)] public uint[] missileLaunch;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRwrObjects)] public uint[] selected;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRwrObjects)] public float[] lethality;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRwrObjects)] public uint[] newDetection;

    public float fwd;
    public float aft;
    public float total;

    public int VersionNum;   // Version of Mem area
    public int VersionNum2;  // Version of Mem area

    public float headX; // Head X offset from design eye (feet)
    public float headY; // Head Y offset from design eye (feet)
    public float headZ; // Head Z offset from design eye (feet)
    public int MainPower; // Main Power switch state, 0=down, 1=middle, 2=up
}

/// <summary>Mirror of F4SharedMem's <c>DED_PFL_LineOfText</c> - 26 raw characters of a DED/PFL line.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct DedPflLineNative
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 26)]
    public byte[] chars;
}
